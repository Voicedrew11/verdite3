using RecompOne.Runtime;
using RecompOne.Runtime.Memory;

namespace Kf3;

/// <summary>
/// The swell half of <see cref="Waves"/>: the water's own vertices lifted and lowered by
/// a slow wave field in world space. Verdite2's <c>WaterSwell</c>, which moved the
/// vertices in guest RAM for its packets as well; here the map is the retained mesh
/// only, so the port works out which corners may move and flags them
/// (<c>RetainedScene.FlagSwell</c>, set by <see cref="RetainedMap"/>), and runtime 0085's
/// vertex shader moves them by the three waves <see cref="Publish"/> hands it each frame,
/// in the colour pass and the surface pass alike. The packet renderer
/// (<c>KF3_GPU_WORLD=0</c>) draws the water still.
///
/// **What may move**, as Verdite2 decides it: a water position (a corner of a water face,
/// <see cref="WaterRects.IsWater"/>) moves only if it is interior to the water -- not on
/// an edge only one water face has (the rim), and not where any other face of the map
/// has a corner -- so a shore, a wall or a bank is never pulled and nothing opens a
/// crack. Worked out over the whole map when the rects, the map's halves or their
/// meshes change.
/// </summary>
public static class WaterSwell
{
    static readonly HashSet<long> _free = new();

    /// <summary>Bumped by every build: the chunks carry which corners are free, so a new
    /// set is a rebuild of every chunk.</summary>
    public static int Generation { get; private set; }

    public static int WaterPositions { get; private set; }
    public static int RimPositions { get; private set; }
    public static int SharedPositions { get; private set; }
    public static int FreePositions => _free.Count;
    public static long Builds { get; private set; }
    public static double BuildMs { get; private set; }

    static long Key(int x, int y, int z) =>
        ((long)(x + (1 << 19)) << 41) | ((long)(y + (1 << 20)) << 20) | (uint)(z + (1 << 19));

    /// <summary>Whether a world position is one the swell moves.</summary>
    public static bool IsFree(int x, int y, int z) => _free.Count > 0 && _free.Contains(Key(x, y, z));

    /// <summary>The whole map's water, from <see cref="RetainedMap.Update"/> before its
    /// chunks are built: every half's faces placed as the chunks place them.</summary>
    public static void Build(PSMemory m, uint map, uint table, RetainedAssets.Mesh?[] models)
    {
        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        _free.Clear();
        Generation++;
        Builds++;
        WaterPositions = RimPositions = SharedPositions = 0;
        if (WaterRects.N == 0) return;

        var water = new HashSet<long>();
        var edges = new Dictionary<(long, long), int>();
        var other = new List<long>();
        var store = RetainedScene.MeshCorners;
        Span<long> keys = stackalloc long[6];
        for (uint i = 0; i < 12800; i++)
        {
            uint half = map + i * 5;
            byte kind = m.ReadU8(half);
            if (kind >= 240 || models[kind] is not { } mesh) continue;
            uint tile = i >> 1;
            int tx = (int)(tile % 80), tz = (int)(tile / 80);
            int rot = m.ReadU8(half + 2) & 3, y0 = -(m.ReadU8(half + 1) << 7);
            uint vertices = table + 12 + m.ReadU32(table + 12 + (uint)kind * 28);
            foreach (var face in mesh.Faces)
            {
                if (face.Corners == 0 || (face.Command & 0xFD) == 0x3C) continue;
                for (int j = 0; j < face.Corners; j++)
                {
                    uint p = vertices + (uint)store[face.Corner + j].X * 8;
                    int px = (short)m.ReadU16(p), py = (short)m.ReadU16(p + 2), pz = (short)m.ReadU16(p + 4);
                    (px, pz) = rot switch { 1 => (pz, -px), 2 => (-px, -pz), 3 => (-pz, px), _ => (px, pz) };
                    keys[j] = Key(tx * 2048 + 1024 + px, y0 + py, tz * 2048 + 1024 + pz);
                }
                ref readonly var c0 = ref store[face.Corner];
                if (!WaterRects.IsWater(face.Semi, (uint)c0.Texpage, c0.Rect))
                {
                    for (int j = 0; j < face.Corners; j++) other.Add(keys[j]);
                    continue;
                }
                // A quad is stored as the strip 0,1,2 / 1,3,2: its outline is 0,1,3,2.
                ReadOnlySpan<int> loop = face.Corners == 6 ? [0, 1, 4, 2] : [0, 1, 2];
                for (int k = 0; k < loop.Length; k++)
                {
                    long a = keys[loop[k]], b = keys[loop[(k + 1) % loop.Length]];
                    water.Add(a);
                    var e = a < b ? (a, b) : (b, a);
                    edges[e] = edges.GetValueOrDefault(e) + 1;
                }
            }
        }

        var rim = new HashSet<long>();
        foreach (var (e, n) in edges)
            if (n == 1) { rim.Add(e.Item1); rim.Add(e.Item2); }
        var shared = new HashSet<long>();
        foreach (long k in other) if (water.Contains(k)) shared.Add(k);
        foreach (long k in water)
            if (!rim.Contains(k) && !shared.Contains(k)) _free.Add(k);
        WaterPositions = water.Count;
        RimPositions = rim.Count;
        SharedPositions = shared.Count;
        BuildMs = System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    }

    static readonly float[] _published = new float[12];

    /// <summary>The swell this frame moves the water by, to the frame begun: three long
    /// waves at unrelated headings, each a wavenumber along X and Z, a height and a
    /// phase; a wave's period goes as the root of its length, as deep water's does.
    /// Verdite2's <c>Height</c>, which the runtime's <c>swellDy</c> evaluates.</summary>
    public static void Publish()
    {
        if (_free.Count == 0 || !Waves.Enabled || Waves.Swell <= 0f) return;
        double t = Waves.Time, len = Waves.SwellSize;
        var p = _published;
        void Wave(int i, double dx, double dz, double l, double amp)
        {
            double k = 2.0 * Math.PI / l;
            p[i * 4] = (float)(k * dx);
            p[i * 4 + 1] = (float)(k * dz);
            p[i * 4 + 2] = (float)(Waves.Swell * amp);
            p[i * 4 + 3] = (float)((2.0 * Math.PI / (7.0 * Math.Sqrt(l / 12000.0)) * t) % (2.0 * Math.PI));
        }
        Wave(0, 0.87, 0.50, len, 0.5);
        Wave(1, -0.34, 0.94, len * 0.71, 0.3);
        Wave(2, 0.60, -0.80, len * 0.53, 0.2);
        RetainedScene.SetSwell(p);
    }

    public static string Describe() => WaterPositions == 0
        ? "swell: no water"
        : $"swell: {_free.Count} of {WaterPositions} water position(s) free ({RimPositions} rim, {SharedPositions} shared; " +
          $"built in {BuildMs:F1} ms, {Builds} build(s))";
}
