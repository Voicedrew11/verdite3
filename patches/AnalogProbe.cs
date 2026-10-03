using System.Reflection;
using System.Text;
using RecompOne.Runtime.Context;
using RecompOne.Runtime.Events;
using RecompOne.Runtime.Memory;
using RecompOne.Runtime.Modding;

namespace Kf3;

/// <summary>
/// The measurement that justifies <see cref="Analog"/>, and the thing to turn on
/// when an axis runs the wrong way.
///
///     KF3_ANALOG_PROBE=1     report the control state to the console
///
/// It reports what the game's control state actually did this window -- the
/// velocities, the yaw and pitch steps, the walk speed and turn rate the game
/// picked -- next to the stick deflection <see cref="Analog"/> fed in.
/// Proportionality between the two is the whole claim: a stick at 30% must
/// produce about 30% of the yaw step that full deflection does, and must not
/// produce *zero* steps, which is what the fractional carry exists to prevent.
///
/// It also dumps the action-mask table once. Those 14 words are the game's own
/// control-config mapping, so the dump is the ground truth for which pad bit
/// means "turn left" in this save -- <see cref="Analog"/> reads the same words
/// rather than hardcoding bits, and the dump is how you check it agreed with
/// reality.
///
/// Verdite2's probe was fed by calls; here it pulls instead. It runs in a post
/// hook on stage 4 <c>func_80030FCC</c>, the player's tick, which runs once a
/// world tick, and reads the state <see cref="Analog"/> publishes (<see
/// cref="Analog.LastLook"/>, <see cref="Analog.LastMove"/>,
/// <see cref="Analog.LastMouse"/>, <see cref="Analog.LookFrames"/>,
/// <see cref="Analog.MoveFrames"/>). A separate class from <see cref="Analog"/>
/// because it is the only part of the pair that costs anything when idle: its
/// hook runs once a tick whether or not a stick moved, so it stays behind its
/// own switch and its own hook. See "Analog twin-stick control" in docs/INPUT.md.
/// </summary>
public static class AnalogProbe
{
    // Stage 4 func_80030FCC, the player's tick: one call a world tick, after the
    // input stage has run, so the velocities and base angles are final. Analog
    // publishes from its hooks under the same stage, so reading here sees them.
    const uint PlayerTick = 0x80030FCC;

    // The game's per-tick control state (GAME.EXE, all off 0x801A0000).
    const uint BaseYaw   = 0x801B2612;   // u16, 12-bit base yaw, (yaw + vel) & 0xFFF
    const uint BasePitch = 0x801B2610;   // u16, 12-bit base pitch
    const uint Pad       = 0x801B265C;   // u16, the word stage 4 read this tick
    const uint YawVel    = 0x801B264C;   // s16, added to yaw each tick
    const uint PitchVel  = 0x801B264E;   // s16, added to pitch each tick
    const uint StrafeVel = 0x801B2646;   // s16, + moves along yaw-0x400
    const uint FwdVel    = 0x801B2648;   // s16, + moves along yaw
    const uint WalkMax   = 0x801B2664;   // s32, this tick's walk speed
    const uint TurnRate  = 0x801B2668;   // s16, 32 walking, 40 standing

    // The action -> button mask table: 14 u16 entries. See "The pad and the
    // action-mask table" in docs/INPUT.md.
    const uint MaskTable = 0x80081868;
    static readonly string[] MaskNames =
    [
        "Up", "Down", "Left", "Right", "Triangle", "Cross", "Square", "Circle",
        "L1", "L2", "R1", "R2", "Select", "Start",
    ];

    public const string OnKey = "kf3.analog.probe";
    public const string IntervalKey = "kf3.analog.probeinterval";

    public static bool On;
    public static float Seconds = 10f;

    static readonly ModInfo _self = new() { Id = "kf3.analogprobe", Name = "Analog probe", Version = "1.0" };

    /// <summary>Keys a KF3_ANALOG* variable set, which the saved settings must
    /// not overwrite -- the same precedence the other patches keep.</summary>
    static readonly HashSet<string> _fromEnv = new(StringComparer.Ordinal);

    static bool _dumped;
    static double _windowStart;
    static int _frames;
    static long _lookSeen, _moveSeen;
    static long _yawSteps, _yawStepSum, _pitchStepSum;
    static int _lastYaw = -1, _lastPitch;
    static int _mouseTicks;
    static double _mouseTurnSum, _mousePitchSum;

    static bool _queued;
    static long _lastTick = -1;

    static double Now => Environment.TickCount64 / 1000.0;

    public static void Configure()
    {
        Kept.Env("KF3_ANALOG_PROBE", OnKey, ref On, _fromEnv);
        _windowStart = Now;
        if (On) Console.WriteLine($"[KF3] analog probe: on, reporting every {Seconds:0.#}s");
    }

    /// <summary>The saved setting, read once the config has loaded; an
    /// environment variable still wins over the value kept for its key.</summary>
    public static void LoadSaved()
    {
        // Only the interval: the hook attaches at boot or not at all, so a saved
        // "on" could not take effect; the switch is the environment's.
        Kept.Saved(IntervalKey, ref Seconds, _fromEnv);
    }

    public static void Install()
    {
        Event.AddListener<RuntimeReadyEvent>(_ => LoadSaved());
        if (!On) return;
        HookAttach.OnOverlayLoad("analog probe", Attach,
                                 "See \"Analog twin-stick control\" in docs/INPUT.md.");
    }

    static bool Attach()
    {
        SymbolRegistry.Build();
        var target = SymbolRegistry.Resolve("game", null, PlayerTick);
        if (target == null)
        {
            Console.Error.WriteLine($"[KF3] analog probe: no function at game/0x{PlayerTick:X8}; no probe");
            return false;
        }
        var impl = typeof(AnalogProbe)
            .GetMethod(nameof(AfterTick), BindingFlags.Public | BindingFlags.Static)!;
        if (!_queued) _queued = HookManager.AddPost(_self, target, impl);
        if (!_queued) return false;
        HookManager.Commit();
        bool ok = HookAttach.Installed(target);
        Console.WriteLine(ok ? "[KF3] analog probe: attached" : "[KF3] analog probe: not installed");
        return ok;
    }

    public static void AfterTick(CpuContext c, IMemory m)
    {
        if (!On) return;

        if (!_dumped) { DumpMasks(m); _dumped = true; }

        // Stage 4's post runs every drawn frame; its body only on a world tick.
        if (FramePacing.Ticks == _lastTick) return;
        _lastTick = FramePacing.Ticks;
        _frames++;

        int yaw = m.ReadU16(BaseYaw);
        int pitch = m.ReadU16(BasePitch);
        if (_lastYaw >= 0)
        {
            int dYaw = ((yaw - _lastYaw + 0x800) & 0xFFF) - 0x800;   // shortest way round
            if (dYaw != 0) { _yawSteps++; _yawStepSum += Math.Abs(dYaw); }
            // Pitch lives in the same 12-bit space and is stored either small and
            // positive or as 0xFD44-ish for negative, so it needs the same
            // shortest-way-round or a zero crossing reads as a 4096 jump.
            _pitchStepSum += Math.Abs((((pitch - _lastPitch) & 0xFFF) + 0x800 & 0xFFF) - 0x800);
        }
        _lastYaw = yaw;
        _lastPitch = pitch;

        if (Analog.LastMouse.Turn != 0f || Analog.LastMouse.Pitch != 0f)
        {
            _mouseTicks++;
            _mouseTurnSum += Math.Abs(Analog.LastMouse.Turn);
            _mousePitchSum += Math.Abs(Analog.LastMouse.Pitch);
            Analog.LastMouse = default;
        }

        if (Now - _windowStart < Seconds) return;
        Report(m);
    }

    static void Report(IMemory m)
    {
        double window = Now - _windowStart;
        _windowStart = Now;

        long look = Analog.LookFrames - _lookSeen; _lookSeen = Analog.LookFrames;
        long move = Analog.MoveFrames - _moveSeen; _moveSeen = Analog.MoveFrames;

        Console.WriteLine(
            $"[KF3] analog probe: {_frames} ticks in {window:0.#}s | " +
            $"look {look} move {move} mouse {_mouseTicks}" +
            (_mouseTicks == 0 ? "" :
                $" (mean |turn| {_mouseTurnSum / _mouseTicks:0.0}, |pitch| {_mousePitchSum / _mouseTicks:0.0}" +
                $", capture {(Mouse.Captured ? "on" : "off")})") + " | " +
            $"stick R({Analog.LastLook.X:+0.00;-0.00;0.00},{Analog.LastLook.Y:+0.00;-0.00;0.00}) " +
            $"L({Analog.LastMove.X:+0.00;-0.00;0.00},{Analog.LastMove.Y:+0.00;-0.00;0.00})");

        Console.WriteLine(
            $"                    yaw {m.ReadU16(BaseYaw),5}  pitch {m.ReadU16(BasePitch),5}  " +
            $"turnVel {(short)m.ReadU16(YawVel),4}  pitchVel {(short)m.ReadU16(PitchVel),4}  " +
            $"fwdVel {(short)m.ReadU16(FwdVel),5}  strafeVel {(short)m.ReadU16(StrafeVel),5}");

        Console.WriteLine(
            $"                    rate {Analog.LastLook.Rate,3} speed {Analog.LastMove.Speed,4}  " +
            $"turnRate {(short)m.ReadU16(TurnRate),3}  walkMax {(int)m.ReadU32(WalkMax),4}  " +
            $"pad 0x{m.ReadU16(Pad):X4}  | yaw stepped {_yawSteps}/{_frames} ticks, mean |step| " +
            $"{(_yawSteps == 0 ? 0.0 : (double)_yawStepSum / _yawSteps):0.00}, " +
            $"pitch total {_pitchStepSum}");

        _frames = _mouseTicks = 0;
        _mouseTurnSum = _mousePitchSum = 0;
        _yawSteps = _yawStepSum = _pitchStepSum = 0;
    }

    // The game's action -> button mask table, word-spaced and named in the order
    // it is tested. Printed whole, once, so the table is on the record whether or
    // not a given entry is bound.
    static void DumpMasks(IMemory m)
    {
        Console.WriteLine("[KF3] analog probe: action mask table 0x80081868..0x80081882 " +
                          "(the game's own control config; the patch reads these, it does not assume bits)");
        for (int i = 0; i < MaskNames.Length; i++)
        {
            uint a = MaskTable + (uint)(2 * i);
            ushort v = m.ReadU16(a);
            Console.WriteLine($"    0x{a:X8}  0x{v:X4}  {Buttons(v),-18} {MaskNames[i]}");
        }
    }

    static readonly string[] BitNames =
    [
        "Select", "L3", "R3", "Start", "Up", "Right", "Down", "Left",
        "L2", "R2", "L1", "R1", "Triangle", "Circle", "Cross", "Square",
    ];

    /// <summary>
    /// Name the buttons in a mask.
    ///
    /// The game's pad word carries the two button bytes in the opposite order to
    /// Controller's bit layout -- libetc's PadRead hands back the BIOS buffer's
    /// halves as they sit in memory -- so bit i of a mask is Controller bit
    /// (i + 8) &amp; 15. Decoding it straight makes the movement keys come out as
    /// Triangle/Cross/Square/Circle; swapped, they read Up/Down/Left/Right with
    /// L1/R1 strafing and L2/R2 looking, which is King's Field's actual control
    /// scheme.
    ///
    /// <see cref="Analog"/> itself never needs this: it ORs the game's own mask
    /// words back into the game's own pad word, so it is layout-agnostic. Only the
    /// label is.
    /// </summary>
    static string Buttons(uint mask)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < BitNames.Length; i++)
            if ((mask & (1u << i)) != 0)
            {
                if (sb.Length > 0) sb.Append('+');
                sb.Append(BitNames[(i + 8) & 15]);
            }
        return sb.Length == 0 ? "-" : sb.ToString();
    }
}
