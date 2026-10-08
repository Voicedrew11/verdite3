using System.Reflection;
using RecompOne.Runtime.Context;
using RecompOne.Runtime.Memory;
using RecompOne.Runtime.Modding;

namespace Kf3;

/// <summary>
/// Hold a picked-up item at the centre of the view while the camera bobs.
///
///     KF3_ITEMEYE=0          the game's own target, to compare (on by default)
///     KF3_ITEMEYE_PROBE=1    a line for each target, and the item in the camera's
///                            frame twice a second during the hold
///
/// The pickup (<c>func_8005DB30</c>) aims the item with <c>func_8005BE28</c>, which
/// ends by adding the player's position and subtracting the eye height: the target
/// sits at <c>player y - 0x640</c>. The camera's eye is
/// <c>player y + bob (0x801B2650) + dip (0x801B2654) - 0x640</c>, so while the
/// player's walk bob or landing dip is not zero, the item is centred on the
/// nominal eye and sits off the centre of the screen for the whole hold.
///
/// This post-hook runs after the call from the two places that use the target:
/// the hold (return address <c>0x8005DE74</c>) and the fly-out when the item is taken
/// (<c>0x8005E110</c>). It moves the out VECTOR by the difference between the
/// camera's position and the nominal eye, so the item is drawn from the camera.
/// The pitch is the game's to level; Stage15 leaves the smoother's view out of the
/// pickup's frames so it can.
///
/// See "The item held up at the eye" in docs/INPUT.md.
/// </summary>
public static class ItemEye
{
    const uint TargetRoutine = 0x8005BE28;  // func_8005BE28: the item's target, out VECTOR at sp+0x1C
    const uint HoldReturn = 0x8005DE74;     // the hold's call site
    const uint FlyOutReturn = 0x8005E110;   // the fly-out's call site
    const uint OutSlot = 0x1C;              // the caller's stack slot holding the out pointer
    const uint Stage15Routine = 0x800422B8;
    const uint HoldDraw = 0x8005DFE8;       // stage 15's return into the hold loop
    const uint FlyInDraw = 0x8005DF84;      // stage 15's return into the fly-in
    const uint RecordAt = 0x80;             // the pickup's sp+0x80 holds the item's record

    const uint PlayerX = 0x801B25F0, PlayerY = 0x801B25F4, PlayerZ = 0x801B25F8;
    const int EyeHeight = 0x640;

    public static bool Enabled { get; private set; } = true;
    static bool _probe, _queued;
    static long _probeTicks = -1;
    static long _flyFrames, _flyPasses;
    static readonly HashSet<short> _flyPitches = [];

    static readonly ModInfo _self = new() { Id = "kf3.itemeye", Name = "Item eye", Version = "1.0" };

    public static void Configure(string? spec)
    {
        if (!string.IsNullOrWhiteSpace(spec))
            Enabled = spec.Trim().ToLowerInvariant() is not ("0" or "off" or "false" or "no");
        _probe = Environment.GetEnvironmentVariable("KF3_ITEMEYE_PROBE")?.Trim().ToLowerInvariant() is "1" or "on" or "true";
    }

    public static void Install()
    {
        // Off, the probe still measures the game's own target, to compare.
        if (!Enabled && !_probe) return;
        HookAttach.OnOverlayLoad("item eye", Attach,
            "A picked-up item may sit off the centre of the view while the camera bobs.");
    }

    static bool Attach()
    {
        SymbolRegistry.Build();
        var target = SymbolRegistry.Resolve("game", null, TargetRoutine);
        if (target == null) return false;
        if (!_queued)
        {
            var impl = typeof(ItemEye).GetMethod(nameof(After), BindingFlags.Public | BindingFlags.Static)!;
            _queued = HookManager.AddPost(_self, target, impl);
            if (_queued && _probe && SymbolRegistry.Resolve("game", null, Stage15Routine) is { } stage15)
                HookManager.AddPre(_self, stage15,
                    typeof(ItemEye).GetMethod(nameof(Probe), BindingFlags.Public | BindingFlags.Static)!);
            if (!_queued) return false;
        }
        HookManager.Commit();
        bool ok = HookAttach.Installed(target);
        if (ok) Console.WriteLine($"[KF3] item eye: {(Enabled ? "on" : "off, probe only")}");
        else Console.Error.WriteLine("[KF3] item eye: the target hook did not install");
        return ok;
    }

    public static void After(CpuContext c, IMemory m)
    {
        string? phase = c.RA switch
        {
            HoldReturn => "hold",
            FlyOutReturn => "fly-out",
            _ => null,
        };
        if (phase == null) return;

        uint outAt = m.ReadU32(c.SP + OutSlot);
        int playerX = (int)m.ReadU32(PlayerX), playerY = (int)m.ReadU32(PlayerY), playerZ = (int)m.ReadU32(PlayerZ);
        int camX = (int)m.ReadU32(CameraBlock.Position);
        int camY = (int)m.ReadU32(CameraBlock.Position + 4u);
        int camZ = (int)m.ReadU32(CameraBlock.Position + 8u);

        int dx = camX - playerX;
        int dy = camY - (playerY - EyeHeight);
        int dz = camZ - playerZ;

        if (!Enabled) dx = dy = dz = 0;
        int x = (int)m.ReadU32(outAt) + dx;
        int y = (int)m.ReadU32(outAt + 4u) + dy;
        int z = (int)m.ReadU32(outAt + 8u) + dz;
        m.WriteU32(outAt, (uint)x);
        m.WriteU32(outAt + 4u, (uint)y);
        m.WriteU32(outAt + 8u, (uint)z);

        if (_probe)
            Console.WriteLine($"[KF3] item eye: {phase} target moved by ({dx}, {dy}, {dz}) to ({x}, {y}, {z}), " +
                              $"view pitch {m.ReadU16(0x801B2608) & 0xFFF}, iteration ticked {FramePacing.IterationTicked}");
    }

    /// <summary>The probe: where the held item sits in the camera's frame, from the
    /// block's view matrix (<c>0x801AEB4C</c>, 1.0 = 4096) and eye, about twice a
    /// second while it is held. x is across, y down, z ahead; centred is x = 0.</summary>
    public static bool Probe(CpuContext c, IMemory m)
    {
        if (c.RA == FlyInDraw)
        {
            // The fly-in levels the view: count the frames drawn and the pitches they showed.
            _flyFrames++;
            if (!LoopPacing.InRedraw) _flyPasses++;
            if (Stage15.ViewOverride is { } drawn) _flyPitches.Add(drawn.Pitch);
            return true;
        }
        if (c.RA != HoldDraw) return true;
        if (_flyFrames > 0)
        {
            Console.WriteLine($"[KF3] item eye: fly-in drew {_flyFrames} frames over {_flyPasses} passes, " +
                              $"{_flyPitches.Count} distinct pitches");
            _flyFrames = _flyPasses = 0;
            _flyPitches.Clear();
        }
        long ticks = FramePacing.Frames / 64;
        if (ticks == _probeTicks) return true;
        _probeTicks = ticks;
        uint record = m.ReadU32(c.SP + RecordAt);
        if (record == 0) return true;
        var cam = Camera.Read(m);
        long wx = (int)m.ReadU32(record + 0x14u) - cam.X;
        long wy = (int)m.ReadU32(record + 0x18u) - cam.Y;
        long wz = (int)m.ReadU32(record + 0x1Cu) - cam.Z;
        var v = new long[3];
        for (int i = 0; i < 3; i++)
        {
            uint row = CameraBlock.ViewMatrix + (uint)(i * 6);
            v[i] = ((short)m.ReadU16(row) * wx + (short)m.ReadU16(row + 2u) * wy + (short)m.ReadU16(row + 4u) * wz) >> 12;
        }
        Console.WriteLine($"[KF3] item eye: held at ({v[0]}, {v[1]}, {v[2]}) from the eye, " +
                          $"view pitch {cam.Pitch & 0xFFF}, eye y {cam.Y}, bob {(short)m.ReadU16(0x801B2650)} dip {(short)m.ReadU16(0x801B2654)}");
        return true;
    }
}
