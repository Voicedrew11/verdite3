using System.Reflection;
using RecompOne.Runtime.Context;
using RecompOne.Runtime.Memory;
using RecompOne.Runtime.Modding;

namespace Kf3;

/// <summary>
/// Hold a picked-up item at the centre of the view: the camera's real eye, and the
/// middle of the item's own geometry.
///
///     KF3_ITEMEYE=0          the game's own target, to compare (on by default)
///     KF3_ITEMEYE_PROBE=1    a line for each target, and the item in the camera's
///                            frame twice a second during the hold
///
/// <b>The eye.</b>
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
/// <b>The geometry</b> (Verdite2's correction, 2026-10-13). The game aims the item's
/// <i>origin</i>, and an item's mesh stands on its origin rather than around it, so
/// the middle of what is drawn sat off the centre of the picture and swung about as
/// it spun. Where the walk draws the held record (ModelWalk.Carry, after ItemTurn's
/// turn), <see cref="Place"/> moves the position it hands the submitter so the middle
/// of the mesh's box sits on the view's axis at the origin's distance, whatever the
/// rotation. The box is measured after the record's submit (<see cref="NoteMesh"/>),
/// from the sub-model the submitter drew, so the first frame of a pickup is drawn as
/// the game has it, and the fly-in's weight is 0 there anyway. It is blended in over
/// the fly-in and out over the fly-out, carried between ticks as the record is.
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
    const uint Pickup = 0x8005DB30;         // func_8005DB30, the pickup's own loop
    const uint FaceBackDraw = 0x8005E194;   // stage 15's return as the item turns back
    const uint FlyOutDraw = 0x8005E278;     // stage 15's return into the fly-out
    const uint Table = 0x1F800010;          // scratchpad: the submitted model's sub-model table

    const uint PlayerX = 0x801B25F0, PlayerY = 0x801B25F4, PlayerZ = 0x801B25F8;
    const int EyeHeight = 0x640;

    public static bool Enabled { get; private set; } = true;
    static bool _probe, _queued;
    static long _probeTicks = -1;
    static long _flyFrames, _flyPasses;
    static readonly HashSet<short> _flyPitches = [];
    static long _placeProbeFrames = -1;

    // The pickup as stage 15 sees it: the record, and how far the centring is in at
    // the loop's last two passes (drawn between them, as the record is).
    static bool _inPickup;
    static uint _record;
    static double _weightWas, _weight;
    static bool _weighed;

    // The middle of the held mesh's box, in its own space, by where its vertices are.
    static bool _haveMesh;
    static uint _meshVerts, _meshCount;
    static double _cx, _cy, _cz;

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
        var pickup = SymbolRegistry.Resolve("game", null, Pickup);
        var stage15 = SymbolRegistry.Resolve("game", null, Stage15Routine);
        if (target == null || pickup == null || stage15 == null) return false;
        if (!_queued)
        {
            _queued = HookManager.AddPost(_self, target, Own(nameof(After)))
                      && HookManager.AddPre(_self, pickup, Own(nameof(Enter)))
                      && HookManager.AddPost(_self, pickup, Own(nameof(Leave)))
                      && HookManager.AddPre(_self, stage15, Own(nameof(BeforeStage15)));
            if (_queued && _probe)
                HookManager.AddPre(_self, stage15, Own(nameof(Probe)));
            if (!_queued) return false;
        }
        HookManager.Commit();
        bool ok = HookAttach.Installed(target) && HookAttach.Installed(pickup) && HookAttach.Installed(stage15);
        if (ok) Console.WriteLine($"[KF3] item eye: {(Enabled ? "on" : "off, probe only")}");
        else Console.Error.WriteLine("[KF3] item eye: a hook did not install");
        return ok;
    }

    static MethodInfo Own(string name) =>
        typeof(ItemEye).GetMethod(name, BindingFlags.Public | BindingFlags.Static)!;

    public static bool Enter(CpuContext c, IMemory m)
    {
        _inPickup = true;
        _record = 0;
        _haveMesh = false;
        _weighed = false;
        _weightWas = _weight = 0.0;
        return true;
    }

    public static void Leave(CpuContext c, IMemory m)
    {
        _inPickup = false;
        _record = 0;
    }

    /// <summary>How far the centring is in, from where stage 15 was called. The fly-in
    /// steps its <c>s0</c> by 0x200 after placing the item and before drawing it, and
    /// the fly-out steps it down the same way, so the step the drawn frame was placed
    /// at is <c>s0 - 0x200</c> and <c>s0 + 0x200</c>. Taken on the loop's own pass,
    /// not loop pacing's redraws.</summary>
    public static bool BeforeStage15(CpuContext c, IMemory m)
    {
        if (!_inPickup || LoopPacing.InRedraw) return true;
        double w;
        switch (c.RA)
        {
            case FlyInDraw: w = ((int)c.S0 - 0x200) / 4096.0; break;
            case HoldDraw:
            case FaceBackDraw: w = 1.0; break;
            case FlyOutDraw: w = ((int)c.S0 + 0x200) / 4096.0; break;
            default: return true;
        }
        w = Math.Clamp(w, 0.0, 1.0);
        _record = m.ReadU32(c.SP + RecordAt);
        _weightWas = _weighed ? _weight : w;
        _weight = w;
        _weighed = true;
        return true;
    }

    /// <summary>Whether this record is the item the pickup holds up, for the walk.</summary>
    public static bool Holds(uint record) => Enabled && _inPickup && record == _record && record != 0;

    /// <summary>
    /// Move the position the walk hands the submitter for the held item so the middle
    /// of its mesh is drawn on the view's axis, at the origin's distance along it.
    /// <paramref name="pitch"/>, <paramref name="yaw"/>, <paramref name="roll"/> are the
    /// angles it is drawn with (ItemTurn's turn in them); the scale is the record's
    /// <c>+0x2C</c>, the vector the walk passes the submitter.
    /// </summary>
    public static void Place(IMemory m, uint rec, ref int x, ref int y, ref int z, short pitch, short yaw, short roll)
    {
        if (!_haveMesh) return;
        double w = _weightWas + (_weight - _weightWas) * FramePacing.TickFraction;
        if (w <= 0.0) return;

        // The mesh's middle, scaled and turned as the submitter will.
        double sx = (short)m.ReadU16(rec + 0x2Cu) / 4096.0, sy = (short)m.ReadU16(rec + 0x2Eu) / 4096.0,
               sz = (short)m.ReadU16(rec + 0x30u) / 4096.0;
        var r = ItemTurn.Rotation(pitch, yaw, roll);
        double lx = _cx * sx, ly = _cy * sy, lz = _cz * sz;
        double rcx = r[0, 0] * lx + r[0, 1] * ly + r[0, 2] * lz;
        double rcy = r[1, 0] * lx + r[1, 1] * ly + r[1, 2] * lz;
        double rcz = r[2, 0] * lx + r[2, 1] * ly + r[2, 2] * lz;

        // The origin's distance along the view, and the point on the axis there.
        var cam = Camera.Read(m);
        double fx = View(m, 2, 0), fy = View(m, 2, 1), fz = View(m, 2, 2);
        double depth = fx * (x - cam.X) + fy * (y - cam.Y) + fz * (z - cam.Z);
        double px = cam.X + fx * depth - rcx, py = cam.Y + fy * depth - rcy, pz = cam.Z + fz * depth - rcz;

        int ox = x, oy = y, oz = z;
        x = (int)Math.Round(ox + (px - ox) * w);
        y = (int)Math.Round(oy + (py - oy) * w);
        z = (int)Math.Round(oz + (pz - oz) * w);

        if (_probe && w >= 1.0 && FramePacing.Frames / 32 != _placeProbeFrames)
        {
            _placeProbeFrames = FramePacing.Frames / 32;
            var was = Eye(m, cam, ox + rcx, oy + rcy, oz + rcz);
            var now = Eye(m, cam, x + rcx, y + rcy, z + rcz);
            Console.WriteLine($"[KF3] item eye: mesh middle from the eye ({was.X:0}, {was.Y:0}, {was.Z:0}) " +
                              $"drawn at ({now.X:0}, {now.Y:0}, {now.Z:0}); origin moved by " +
                              $"({x - ox}, {y - oy}, {z - oz}); scale ({sx:0.###}, {sy:0.###}, {sz:0.###})");
        }
    }

    /// <summary>The held item's mesh, read after its submit: the sub-model is the low
    /// seven bits of the walk's clip word (<paramref name="clip"/>, the submitter's
    /// <c>sp+0x1C</c>), its header 28 bytes a sub-model at <c>table + 0xC</c>, the
    /// vertex offset at <c>+0</c> and the count at <c>+4</c>, eight bytes a vertex
    /// (the submitter's own walk of them, func_8003E34C at 0x8003EE84).</summary>
    public static void NoteMesh(IMemory m, uint clip)
    {
        if (!Enabled) return;
        uint table = m.ReadU32(Table);
        if (table < 0x80010000u || table >= 0x80200000u) return;
        uint header = table + 0xCu + (clip & 0x7Fu) * 28u;
        uint verts = table + 0xCu + m.ReadU32(header);
        uint count = m.ReadU32(header + 4u);
        if (count == 0 || count > 4096 || verts < 0x80010000u || verts + count * 8u > 0x80200000u) return;
        if (_haveMesh && verts == _meshVerts && count == _meshCount) return;

        Span<int> lo = [int.MaxValue, int.MaxValue, int.MaxValue], hi = [int.MinValue, int.MinValue, int.MinValue];
        for (uint i = 0; i < count; i++)
            for (int k = 0; k < 3; k++)
            {
                int v = (short)m.ReadU16(verts + i * 8u + (uint)k * 2u);
                lo[k] = Math.Min(lo[k], v);
                hi[k] = Math.Max(hi[k], v);
            }
        _haveMesh = true;
        _meshVerts = verts;
        _meshCount = count;
        _cx = (lo[0] + hi[0]) * 0.5;
        _cy = (lo[1] + hi[1]) * 0.5;
        _cz = (lo[2] + hi[2]) * 0.5;
        if (_probe)
            Console.WriteLine($"[KF3] item eye: mesh {count} vertices (clip 0x{clip:X}), x {lo[0]}..{hi[0]} " +
                              $"y {lo[1]}..{hi[1]} z {lo[2]}..{hi[2]}, middle ({_cx:0}, {_cy:0}, {_cz:0}) from the origin");
    }

    /// <summary>The block's view matrix, 1.0 = 4096: row 0 the camera's right, 2 its forward.</summary>
    static double View(IMemory m, int row, int col) =>
        (short)m.ReadU16(CameraBlock.ViewMatrix + (uint)(row * 6 + col * 2)) / 4096.0;

    static (double X, double Y, double Z) Eye(IMemory m, Camera cam, double x, double y, double z)
    {
        x -= cam.X; y -= cam.Y; z -= cam.Z;
        return (View(m, 0, 0) * x + View(m, 0, 1) * y + View(m, 0, 2) * z,
                View(m, 1, 0) * x + View(m, 1, 1) * y + View(m, 1, 2) * z,
                View(m, 2, 0) * x + View(m, 2, 1) * y + View(m, 2, 2) * z);
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
