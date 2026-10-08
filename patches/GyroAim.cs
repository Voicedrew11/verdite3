using System.Diagnostics;
using RecompOne.Runtime.Events;
using RecompOne.Runtime.Hardware;
using RecompOne.Runtime.Memory;

namespace Kf3;

/// <summary>
/// Aim a drawn bow with the pad's gyroscope: while a bow is in hand and being
/// drawn, turning and tipping the pad turns and tips the view, one to one.
///
///     KF3_GYROAIM=1           on (off by default)
///     KF3_GYROAIM_PROBE=1     a line a second: the weapon, the clock, the attack
///                             button, vblanks gated in, the units handed to the look
///
/// **A bow** is weapon id 27 (LARGE BOW) or 28 (ELCHRIS BOW), read off the name
/// table at <c>0x8007F620</c>, in the equipped-weapon byte <c>0x801B25AF</c>.
/// **Drawn** is <c>func_8002D2A0</c>'s bow branch with the clip byte
/// <c>u8 0x801B25AE</c> at 0 and the swing clock <c>s16 0x801B25A4</c> running:
/// from the press that nocks the arrow, through the draw, to the release, which
/// sets the clip byte to 1 (the loose). The game holds full draw while attack is
/// held, so the aim lasts as long as the hold. See Rumble for the whole branch.
///
/// The rate is integrated every vblank while the gate holds, and the look routine's
/// tick spends the sum through <see cref="Analog.BeforeLook"/> beside the right
/// stick, so it is smoothed as the stick is and stops as the mouse does. A sum the
/// look routine has not spent in <see cref="StaleMs"/> (a menu, a cutscene) is
/// dropped. See "Gyro aim with a drawn bow" in docs/INPUT.md.
/// </summary>
public static class GyroAim
{
    const uint WeaponSlot = 0x801B25AF;     // u8, the equipped weapon id, 0xFF none
    const uint SwingClock = 0x801B25A4;     // s16, the arm's swing clock, -1 idle
    const uint ClipByte = 0x801B25AE;       // u8, for a bow: 0 drawn, 1 loosed
    const uint AttackMask = 0x80081870;     // u16, entry 4 of the action mask table (the probe)
    const uint Pad = 0x801B265C;            // u16, the pad word stage 4 tests, active high

    const int LargeBow = 27, ElchrisBow = 28;

    /// <summary>A gyro rate below this, in radians a second, is a hand at rest
    /// (ItemTurn's): it keeps a pad's drift from creeping the aim.</summary>
    const float GyroRest = 0.05f;

    const float Units = 4096f / (2f * MathF.PI);     // angle units a radian

    /// <summary>A sum older than this is not the hand's any more.</summary>
    const double StaleMs = 250;

    public const string OnKey = "kf3.gyroaim.on";

    public static bool Enabled;

    static readonly HashSet<string> _fromEnv = [];
    static readonly Stopwatch _clock = Stopwatch.StartNew();
    static double _lastMs, _takenMs;
    static float _turn, _pitch;

    static bool _probe;
    static double _probeAt;
    static long _gated, _ticks;
    static float _spentTurn, _spentPitch;

    public static void Configure()
    {
        Kept.Env("KF3_GYROAIM", OnKey, ref Enabled, _fromEnv);
        _probe = Environment.GetEnvironmentVariable("KF3_GYROAIM_PROBE")?.Trim() is "1" or "on" or "true";
    }

    public static void Install()
    {
        Event.AddListener<RuntimeReadyEvent>(_ =>
        {
            Kept.Saved(OnKey, ref Enabled, _fromEnv);
            SetEnabled(Enabled);
        });
        Event.AddListener<VSyncEvent>(_ => Integrate());
    }

    public static void SetEnabled(bool on)
    {
        Enabled = on;
        WantGyro();
    }

    /// <summary>The pad's gyroscope is on while either reader would use it: a pad
    /// streams a larger report once it is (runtime 0102), and one flag serves both.</summary>
    internal static void WantGyro() =>
        Controller.WantGyro = Enabled || (ItemTurn.Enabled && ItemTurn.UseGyro);

    /// <summary>A bow in hand and being drawn.</summary>
    static bool Drawn(IMemory m)
    {
        int weapon = m.ReadU8(WeaponSlot);
        if (weapon is not (LargeBow or ElchrisBow)) return false;
        return m.ReadU8(ClipByte) == 0 && (short)m.ReadU16(SwingClock) >= 0;
    }

    static void Integrate()
    {
        double now = _clock.Elapsed.TotalMilliseconds;
        float dt = (float)Math.Clamp((now - _lastMs) / 1000.0, 0.0, 0.1);
        _lastMs = now;
        if (_probe) Probe(now);

        var m = MouseLook.Memory;
        if (!Enabled || !Controller.Gyro || m == null || ItemTurn.HoldsMouse || !Drawn(m))
        {
            _turn = _pitch = 0f;
            return;
        }
        if (now - _takenMs > StaleMs) _turn = _pitch = 0f;
        if (_probe) _gated++;

        // SDL's axes for a pad held in front of you: x across it, y up, z toward you,
        // anticlockwise positive. Turning the pad left (+y) turns the view left, which
        // is yaw increasing (Left 0x8008186C increases it). Tipping the far edge up
        // (+x) looks up, the way the mouse's pitch goes when it is pushed away, so it
        // takes the mouse's sign and the player's inversion with it.
        float gx = Rest(Controller.GyroX), gy = Rest(Controller.GyroY);
        _turn += gy * Units * dt;
        _pitch -= gx * Units * dt * (Mouse.InvertY ? -1f : 1f);
    }

    static float Rest(float rate) => MathF.Abs(rate) < GyroRest ? 0f : rate;

    /// <summary>What the gyro asks the look routine for this tick, in angle units,
    /// emptied as it is taken.</summary>
    internal static (float Turn, float Pitch) Take()
    {
        _takenMs = _clock.Elapsed.TotalMilliseconds;
        var taken = (_turn, _pitch);
        _turn = _pitch = 0f;
        if (_probe && (taken._turn != 0f || taken._pitch != 0f))
        {
            _ticks++;
            _spentTurn += taken._turn;
            _spentPitch += taken._pitch;
        }
        return taken;
    }

    static void Probe(double now)
    {
        if (now - _probeAt < 1000) return;
        _probeAt = now;
        var m = MouseLook.Memory;
        if (m == null) return;
        Console.WriteLine($"[KF3] gyro aim: {(Enabled ? "on" : "off")}, pad gyro {(Controller.Gyro ? "on" : "off")}, " +
                          $"weapon {m.ReadU8(WeaponSlot)}, clock {(short)m.ReadU16(SwingClock)}, clip {m.ReadU8(ClipByte)}, " +
                          $"attack {((m.ReadU16(Pad) & m.ReadU16(AttackMask)) != 0 ? "held" : "up")}, " +
                          $"vblanks drawn {_gated}, ticks spent {_ticks}, turn {_spentTurn:0} pitch {_spentPitch:0}");
        _gated = _ticks = 0;
        _spentTurn = _spentPitch = 0f;
    }
}
