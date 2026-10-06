using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using RecompOne.Runtime;
using RecompOne.Runtime.Context;
using RecompOne.Runtime.Memory;
using RecompOne.Runtime.Modding;

namespace Kf3;

/// <summary>
/// Planar reflections: the retained world drawn a second time from the camera mirrored in
/// the water, into the planar texture the reflection pass reads wherever a surface lies
/// on the water's plane (runtime 0068, drawn by 0085's <c>DrawWorldMirror</c>).
///
///     KF3_PLANAR=1            on (Video ▸ World enhancements ▸ Planar reflections; off by default)
///     KF3_PLANAR_TOLERANCE=48 how far off the plane a surface may be and still take it, world units
///     KF3_PLANAR_RIPPLE=4     how far the water's own texture bends the reflection; 0 a flat mirror
///     KF3_PLANAR_BIAS=8       how far above the plane geometry has to be to be reflected
///     KF3_PLANAR_FOG=0        fog the reflection at the mirrored camera's own depth (level by default)
///     KF3_PLANAR_MIPS=1       filter the mirror's textures through the mip atlas, as the main view's
///     KF3_PLANAR_WATER=0      leave the map's blended faces out of the mirror (a comparison)
///     KF3_PLANAR_PROBE=1      the plane, the mirror's halves and models, and the capture, every 5 s
///
/// **Not a second walk.** Verdite2's <c>PlanarWalk</c> ran the game's tile walk again from
/// a mirrored camera and replayed the object walk's submits, because its packets were the
/// picture. Here the world is already retained: the frame holds every map half the walk
/// drew and every model instance placed in the world, so the mirror is that frame seen
/// from another camera. Nothing of the game runs twice, and its RAM, GTE and stack are
/// never touched.
///
/// **The mirrored camera.** With the world's Y flipped about the plane, the view matrix
/// <c>R</c> becomes <c>F R S</c> (S flips world Y, F the view's own Y): the same yaw, the
/// pitch and roll negated, which is an ordinary camera, so back faces cull as the game
/// culls them. The camera's height goes to <c>2h - Y</c> and the view translation's Y
/// changes sign. The image is upside down; the reflection pass reads row
/// <c>2*OFY - y</c>, as it reads Verdite2's.
///
/// **What the mirror draws**: the halves the frame's main view draws (the walk's and
/// the render distance's), and a cull of its own, as Verdite2's <c>PlanarCull</c> gave
/// its mirror: every half within the draw distance whose box reaches above the plane and
/// meets the mirrored frustum, with no occlusion flood, since the inside of a cavern hidden
/// from the eye can be plain in the water. Every model instance the frame placed in the
/// world, and their blended faces keyed by the mirrored view's depth. The map's blended
/// faces after its opaque ones. Not drawn: the sky, the arm, and models standing only
/// where the mirror's cull reaches (the object walk admits by the eye's grid).
///
/// **The plane** is the runtime's: the frame's visible water triangles, at rest, binned
/// by height and screen area as the main view draws them (<c>NoteWaterPlane</c>), so the
/// next frame mirrors in the heaviest. No water on screen, no mirror. See "Planar
/// reflections" in docs/WATER.md.
/// </summary>
public static class PlanarMirror
{
    static readonly (string Overlay, uint Addr) DrawOTag = ("game", 0x8007A104);

    static bool _probe, _mips;
    static float _bias = 8f;

    static readonly ModInfo _self = new()
    {
        Id = "kf3.planar",
        Name = "Planar reflections",
        Version = "1.0",
        Description = "Reflects the retained world in water from a mirrored camera.",
    };

    public static bool Enabled => PlanarReflections.Enabled;

    /// <summary>The tolerance asked for; <see cref="Waves"/> adds the swell's height.</summary>
    public static float BaseTolerance { get; private set; } = 48f;

    public static void Configure(string? tolerance, string? ripple, string? bias, string? fog, string? probe)
    {
        var ci = CultureInfo.InvariantCulture;
        PlanarReflections.LevelFog = fog?.Trim() != "0";
        if (float.TryParse(tolerance, NumberStyles.Float, ci, out float t) && t > 0f) BaseTolerance = t;
        PlanarReflections.Tolerance = BaseTolerance;
        if (float.TryParse(ripple, NumberStyles.Float, ci, out float r) && r >= 0f) PlanarReflections.Ripple = r;
        if (float.TryParse(bias, NumberStyles.Float, ci, out float b)) _bias = b;
        _probe = probe is not (null or "" or "0");
        _mips = Environment.GetEnvironmentVariable("KF3_PLANAR_MIPS")?.Trim() == "1";
        PlanarReflections.Probe = _probe;
        // The share of the water that took the planar texture is only in the pass's readback.
        if (_probe) ScreenReflections.Probe = true;
    }

    public static void Install() => HookAttach.OnOverlayLoad("planar", Attach);

    public static void SetEnabled(bool on)
    {
        PlanarReflections.Enabled = on;
        // The mirror's blended map faces are drawn by the backend after its opaque ones.
        RetainedScene.MirrorWater = on && Environment.GetEnvironmentVariable("KF3_PLANAR_WATER")?.Trim() != "0";
    }

    static bool _queued;

    static bool Attach()
    {
        SymbolRegistry.Build();
        var draw = SymbolRegistry.Resolve(DrawOTag.Overlay, null, DrawOTag.Addr);
        if (draw == null) return false;
        if (!_queued)
            _queued = HookManager.AddPre(_self, draw, typeof(PlanarMirror).GetMethod(nameof(BeforeDrawOTag), BindingFlags.Public | BindingFlags.Static)!);
        HookManager.Commit();
        bool ok = HookAttach.Installed(draw);
        Console.WriteLine(ok ? "[KF3] planar reflections: hooked DrawOTag" : "[KF3] planar reflections: DrawOTag not hooked; nothing will be mirrored");
        return ok;
    }

    /// <summary>From GpuWorld.Begin: the camera the frame is drawn with, which the
    /// runtime's plane finder takes the frame's water back to the world by.</summary>
    public static void Frame(in RetainedScene.View v)
    {
        if (!Enabled) return;
        Span<short> r = stackalloc short[9];
        r[0] = Fixed(v.R00); r[1] = Fixed(v.R01); r[2] = Fixed(v.R02);
        r[3] = Fixed(v.R10); r[4] = Fixed(v.R11); r[5] = Fixed(v.R12);
        r[6] = Fixed(v.R20); r[7] = Fixed(v.R21); r[8] = Fixed(v.R22);
        PlanarReflections.SetCamera(r, (float)v.CamX, (float)v.CamY, (float)v.CamZ);
    }

    static short Fixed(float f) => (short)Math.Clamp(MathF.Round(f * 4096f), short.MinValue, short.MaxValue);

    // ---- the mirror -------------------------------------------------------------------

    /// <summary>The frame's table, before the game draws it: the mirror into the planar
    /// texture of the target it is about to be drawn into. Only for a frame the retained
    /// main view is about to draw, which is the table walk LibGpu draws it at.</summary>
    public static void BeforeDrawOTag(CpuContext c, IMemory m)
    {
        if (!Enabled || !PlanarReflections.Supported || m is not PSMemory mem) return;
        int serial = RetainedScene.MainSerial;
        if (serial <= 0 || serial != RetainedScene.Serial || RetainedScene.Find(serial) is not { } f) return;
        if (!PlanarReflections.TakePlane(out float plane, out double area)) { _noWater++; Report(); return; }
        // From under the water, or level with it, there is nothing above it to see.
        if (f.View.CamY >= plane - 16f) { _below++; Report(); return; }
        long start = Stopwatch.GetTimestamp();
        if (plane != _plane) _moves++;
        _plane = plane; _area = area;
        Mirror(mem, f, plane);
        _ms += Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        Report();
    }

    static readonly float[] _clip = new float[4], _view = new float[4], _level = new float[3];

    static void Mirror(PSMemory mem, RetainedScene.Frame f, float plane)
    {
        var v = f.View;
        // F R S: negate every element with exactly one index 1.
        var mv = v;
        mv.R01 = -v.R01; mv.R10 = -v.R10; mv.R12 = -v.R12; mv.R21 = -v.R21;
        mv.CamY = 2.0 * plane - v.CamY;
        mv.Ty = -v.Ty;
        RetainedScene.BeginMirror(mv);
        if (RetainedScene.MirrorSerial != RetainedScene.Serial) return;

        // The main view's plane, for the pass: a surface's height below the water, Y
        // being down, from its view position (column 1 of R).
        _view[0] = v.R01; _view[1] = v.R11; _view[2] = v.R21;
        _view[3] = (float)(v.CamY - plane);
        // Kept above the water, in the mirrored view: a view position's world Y is
        // column 1 of R' . p + Y'.
        _clip[0] = -mv.R01; _clip[1] = -mv.R11; _clip[2] = -mv.R21;
        _clip[3] = (float)(plane - _bias - mv.CamY);
        // The view's forward with its height taken out, in the mirrored view.
        float fx = mv.R20, fz = mv.R22, fl = MathF.Sqrt(fx * fx + fz * fz);
        if (fl > 0f) { fx /= fl; fz /= fl; }
        _level[0] = mv.R00 * fx + mv.R02 * fz;
        _level[1] = mv.R10 * fx + mv.R12 * fz;
        _level[2] = mv.R20 * fx + mv.R22 * fz;

        long t0 = Stopwatch.GetTimestamp();
        int halves = Halves(mem, mv, plane);
        long t1 = Stopwatch.GetTimestamp();
        int faces = BlendFaces(f, mv);
        long t2 = Stopwatch.GetTimestamp();
        _ticks[0] += t1 - t0; _ticks[1] += t2 - t1;

        PlanarReflections.Serial++;
        _clip.CopyTo(PlanarReflections.ClipPlane, 0);
        _view.CopyTo(PlanarReflections.ViewPlane, 0);
        _level.CopyTo(PlanarReflections.LevelAxis, 0);
        PlanarReflections.Captures++;
        PlanarReflections.Capturing = true;
        // The mip atlas's lookups cost the mirror as much CPU as they cost the main
        // view (1.6 ms a frame over area 5's pool); left out unless asked for.
        bool mips = RetainedScene.Mips;
        RetainedScene.Mips = mips && _mips;
        bool drawn;
        try { drawn = RecompOne.Runtime.Runtime.Gpu?.DrawRetainedMain() == true; _ticks[2] += Stopwatch.GetTimestamp() - t2; }
        finally
        {
            PlanarReflections.Capturing = false;
            RetainedScene.Mips = mips;
            RetainedScene.MirrorSerial = 0;
        }
        if (!drawn) RetainedScene.MirrorMissed++;
        _mirrors++;
        _halves += halves;
        _instances += f.MirrorInstances.Count;
        _faces += faces;
    }

    /// <summary>The mirror's halves: within the draw distance, reaching above the plane,
    /// and either the main view's or inside the mirrored frustum.</summary>
    static int Halves(PSMemory m, in RetainedScene.View mv, float plane)
    {
        var main = RetainedScene.CurrentMainHalves;
        if (main.IsEmpty) return 0;
        const float tile = 2048f;
        int cx = (int)Math.Floor(mv.CamX / tile), cz = (int)Math.Floor(mv.CamZ / tile);
        int reach = Math.Min(40, (int)Math.Ceiling(Math.Max(m.ReadU8(RenderDistance.RadiusAddr), RenderDistance.Tiles)) + 1);
        uint table = m.ReadU32(0x801A929C);
        int skipFrom = (short)m.ReadU16(RenderDistance.LoadPending) == 1 && m.ReadU8(RenderDistance.LoadFlag) != 0
            ? (int)(m.ReadU32(table + 4) >> 1) : int.MaxValue;
        var frustum = new RenderDistance.Frustum(mv);
        float top = plane - _bias;
        int n = 0;
        for (int z = Math.Max(0, cz - reach); z <= Math.Min(79, cz + reach); z++)
            for (int x = Math.Max(0, cx - reach); x <= Math.Min(79, cx + reach); x++)
                for (int upper = 0; upper < 2; upper++)
                {
                    uint half = 0x801D4464 + (uint)(z * 80 + x) * 10 + (uint)upper * 5;
                    int kind = m.ReadU8(half);
                    if (kind >= 240 || kind >= skipFrom) continue;
                    float y = -(m.ReadU8(half + 1) << 7);
                    if (y + RetainedMap.MeshYMin[kind] >= top) continue;
                    int index = (z * 80 + x) * 2 + upper;
                    if (main[index] == 0)
                    {
                        float r = Math.Max(tile / 2, RetainedMap.MeshReach[kind]);
                        float x0 = x * tile + tile / 2, z0 = z * tile + tile / 2;
                        if (!frustum.Meets(x0 - r, x0 + r, y + RetainedMap.MeshYMin[kind], Math.Min(top, y + RetainedMap.MeshYMax[kind]),
                                           z0 - r, z0 + r, out _))
                            continue;
                        _own++;
                    }
                    RetainedScene.NoteMirrorHalf(x, z, upper);
                    n++;
                }
        return n;
    }

    static int[] _mirrorOf = new int[64];

    /// <summary>The main view's blended model faces, for the mirrored instances, keyed as
    /// the mirrored table would link them: the mean of their corners' mirrored depth over
    /// four, plus the instance's bias.</summary>
    static int BlendFaces(RetainedScene.Frame f, in RetainedScene.View mv)
    {
        if (f.BlendFaces.Count == 0) return 0;
        if (_mirrorOf.Length < f.Instances.Count) _mirrorOf = new int[f.Instances.Count * 2];
        int k = 0;
        for (int i = 0; i < f.Instances.Count; i++) _mirrorOf[i] = f.Instances[i].Mirrored ? k++ : -1;
        int n = 0;
        foreach (var b in f.BlendFaces)
        {
            int at = _mirrorOf[b.Inst];
            if (at < 0) continue;
            var inst = f.MirrorInstances[at];
            var corner = RetainedScene.MeshCorners[b.Corner];
            int sum = Depth((int)corner.Dqa, inst, mv) + Depth((int)corner.Dqb, inst, mv) + Depth((int)corner.Curve, inst, mv);
            int depth = b.Corners == 6 ? (sum + Depth((int)corner.Rgbc, inst, mv)) >> 2 : sum / 3;
            int key = depth <= 0 ? -1 : depth + (int)(8192 - inst.Far);
            if ((uint)key >= 8192) continue;
            f.MirrorBlendFaces.Add(b with { Inst = at, Key = key, Seq = f.MirrorBlendFaces.Count });
            n++;
        }
        RetainedScene.MirrorBlendNoted += n;
        return n;
    }

    /// <summary>A posed vertex's depth in the mirrored view, over four, as the
    /// assembler takes an SZ for its table key.</summary>
    static int Depth(int vertex, in RetainedScene.ModelInstance m, in RetainedScene.View mv)
    {
        int at = (m.Pose - 1 + vertex * (m.PoseMorph ? 2 : 1)) * 4;
        var store = RetainedScene.PoseStore;
        if (m.Pose <= 0 || at < 0 || at + 7 >= store.Length) return 0;
        int x = store[at], y = store[at + 1], z = store[at + 2];
        if (m.PoseMorph)
        {
            x = (short)(x + (short)(store[at + 4] * m.PoseWeight >> 12));
            y = (short)(y + (short)(store[at + 5] * m.PoseWeight >> 12));
            z = (short)(z + (short)(store[at + 6] * m.PoseWeight >> 12));
        }
        double wx = m.R00 * x + m.R01 * y + m.R02 * z + m.Tx;
        double wy = m.R10 * x + m.R11 * y + m.R12 * z + m.Ty;
        double wz = m.R20 * x + m.R21 * y + m.R22 * z + m.Tz;
        double d = mv.R20 * (wx - mv.CamX) + mv.R21 * (wy - mv.CamY) + mv.R22 * (wz - mv.CamZ) + mv.Tz;
        return Math.Clamp((int)d, 0, 65535) >> 2;
    }

    // ---- the probe ---------------------------------------------------------------------

    static long _reportAt, _mirrors, _halves, _own, _instances, _faces, _noWater, _below, _moves;
    static long _drawsAt, _missedAt, _trisAt, _waterAt;
    static double _ms, _area;
    // Stopwatch ticks in the mirror's parts: its halves, its blended faces, the backend's draw.
    static readonly long[] _ticks = new long[3];
    static float _plane;

    /// <summary>The shell's <c>planar</c>: cumulative counts, for a tour to difference.</summary>
    public static string Counters() => FormattableString.Invariant(
        $"\"planarOn\":{(Enabled ? "true" : "false")},\"planarSupported\":{(PlanarReflections.Supported ? "true" : "false")},") +
        FormattableString.Invariant($"\"plane\":{_plane:0},\"planeArea\":{_area:0},\"mirrorDraws\":{RetainedScene.MirrorDraws},") +
        FormattableString.Invariant($"\"mirrorMissed\":{RetainedScene.MirrorMissed},\"mirrorTriangles\":{RetainedScene.MirrorTriangles},") +
        FormattableString.Invariant($"\"mirrorWaterTriangles\":{RetainedScene.MirrorWaterTriangles},\"mirrorInstances\":{RetainedScene.MirrorInstancesDrawn},") +
        FormattableString.Invariant($"\"mirrorBlendDrawn\":{RetainedScene.MirrorBlendDrawn},\"captures\":{PlanarReflections.Captures},") +
        FormattableString.Invariant($"\"waterTris\":{PlanarReflections.WaterTris},\"waterTilted\":{PlanarReflections.WaterTilted},") +
        FormattableString.Invariant($"\"planarPct\":{PlanarReflections.PlanarPct:0.0},\"reflectivePct\":{ScreenReflections.ReflectivePct:0.0},") +
        FormattableString.Invariant($"\"comparedPct\":{PlanarReflections.ComparedPct:0.0},\"mirrorDiff\":{PlanarReflections.MirrorDiff:0.0},\"controlDiff\":{PlanarReflections.ControlDiff:0.0},\"mapSerial\":{ScreenReflections.MapSerial}");

    static double Ms(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;

    static void Report()
    {
        if (!_probe) return;
        long now = Environment.TickCount64;
        if (now < _reportAt) return;
        _reportAt = now + 5000;
        long draws = RetainedScene.MirrorDraws - _drawsAt, mirrors = Math.Max(1, _mirrors);
        Console.WriteLine($"[KF3] planar: plane Y {_plane:F0} over {_area:F0} px (moved {_moves} time(s)); {_mirrors} mirror(s) at " +
                          $"{_ms / mirrors:F3} ms CPU (halves {Ms(_ticks[0]) / mirrors:F3}, faces {Ms(_ticks[1]) / mirrors:F3}, draw {Ms(_ticks[2]) / mirrors:F3}), {_halves / mirrors} halves a mirror ({_own / mirrors} its own cull), " +
                          $"{_instances / mirrors} instance(s), {_faces / mirrors} blended model face(s); {_noWater} frame(s) with no water, " +
                          $"{_below} under it; {draws} draw(s), {RetainedScene.MirrorMissed - _missedAt} missed, " +
                          $"{(RetainedScene.MirrorTriangles - _trisAt) / Math.Max(1, draws)} map and " +
                          $"{(RetainedScene.MirrorWaterTriangles - _waterAt) / Math.Max(1, draws)} blended triangle(s) a draw; " +
                          $"{PlanarReflections.Captures} capture(s), {PlanarReflections.Cleared} cleared, {PlanarReflections.Dropped} dropped, " +
                          $"{PlanarReflections.Read} read; {PlanarReflections.WaterTris} water tris binned, {PlanarReflections.WaterTilted} not level; " +
                          $"map serial {ScreenReflections.MapSerial}, surface ids [{string.Join(",", RetainedScene.SurfaceIds)}]; reflection passes {ScreenReflections.Passes}, {ScreenReflections.NoTarget} without a target; " +
                          $"last readback {PlanarReflections.PlanarPct:F1}% of {ScreenReflections.ReflectivePct:F1}% reflective planar, " +
                          $"{ScreenReflections.HitPct:F1}% marched, {ScreenReflections.SkyPct:F1}% sky; planar pixels marched too " +
                          $"{PlanarReflections.ComparedPct:F1}%, {PlanarReflections.MirrorDiff:F1} apart (unmirrored {PlanarReflections.ControlDiff:F1})");
        Console.Out.Flush();
        ScreenReflections.ResetCounters();
        ScreenReflections.WantMap = true;
        _drawsAt = RetainedScene.MirrorDraws; _missedAt = RetainedScene.MirrorMissed;
        _trisAt = RetainedScene.MirrorTriangles; _waterAt = RetainedScene.MirrorWaterTriangles;
        _mirrors = _halves = _own = _instances = _faces = _noWater = _below = _moves = 0;
        _ms = 0;
        Array.Clear(_ticks);
        PlanarReflections.ResetCounters();
        PlanarReflections.WaterTris = PlanarReflections.WaterTilted = PlanarReflections.WaterRested = 0;
    }
}
