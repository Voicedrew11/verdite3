using System.Text.Json;
using Kf3;
using RecompOne.Runtime;
using RecompOne.Runtime.Memory;

namespace SceneProbe;

/// <summary>
/// NeighbourBlend (runtime 0088): the half table and records the port hands it, and
/// the CPU reference at shared tile edges, corners, centres, a missing neighbour, the
/// map's edge and both half levels. Writes neighbour-cases.json for shader_probe.py,
/// which runs the same points through PrimFs's functions on the GPU.
/// </summary>
public static class NeighbourFixtures
{
    const uint Map = 0x801D4464, Lights = 0x801AEEFC;
    const int Ints = RetainedScene.RecordInts;

    // Records: fog pair (near, far), and which light (colour matrix and back colour).
    static readonly (int Near, int Far, int Light)[] Defs =
    [
        (6000, 18000, 0),    // 0
        (18000, 22000, 1),   // 1
        (6000, 18000, 2),    // 2: 0's fog, another light
        (9000, 18000, 0),    // 3: 0's light, other fog
        (32000, 32000, 0),   // 4: no fog
    ];

    // The lower level around tile (10, 10), rows z = 9..11, columns x = 9..11; -1 none.
    static readonly int[,] Lower = { { 1, 3, 0 }, { 2, 0, -1 }, { 4, 0, 1 } };

    public static void Run(Action<bool, string> check, string output)
    {
        var m = new PSMemory();
        for (uint i = 0; i < 64000; i += 4) m.WriteU32(Map + i, uint.MaxValue);
        m.WriteU32(0x801A929C, 0); // no model table: RetainedMap.Update stops after records and halves
        var random = new Random(8808);
        var lights = new int[3][];
        for (int l = 0; l < 3; l++)
        {
            lights[l] = new int[12];
            for (int j = 0; j < 9; j++) lights[l][j] = random.Next(-4096, 8193);
            for (int j = 9; j < 12; j++) lights[l][j] = random.Next(0, 256);
        }
        for (int r = 0; r < Defs.Length; r++)
        {
            uint p = Lights + (uint)r * 0x6C;
            for (uint turn = 0; turn < 4; turn++)
                for (uint k = 0; k < 9; k++) m.WriteU16(p + turn * 20 + k * 2, (ushort)(short)random.Next(-4096, 4097));
            for (uint k = 0; k < 9; k++) m.WriteU16(p + 0x50 + k * 2, (ushort)(short)lights[Defs[r].Light][k]);
            for (uint k = 0; k < 3; k++) m.WriteU8(p + 0x64 + k, (byte)lights[Defs[r].Light][9 + k]);
            m.WriteU16(p + 0x68, (ushort)Defs[r].Near); m.WriteU16(p + 0x6A, (ushort)Defs[r].Far);
        }
        void Put(int x, int z, int upper, int record)
        {
            uint half = Map + (uint)(z * 80 + x) * 10 + (uint)upper * 5;
            m.WriteU8(half, 7); m.WriteU8(half + 2, (byte)((x + z) & 3)); m.WriteU8(half + 4, (byte)(0xC0 | record));
        }
        for (int z = 0; z < 3; z++)
            for (int x = 0; x < 3; x++)
                if (Lower[z, x] >= 0) Put(9 + x, 9 + z, 0, Lower[z, x]);
        Put(10, 10, 1, 1); Put(11, 10, 1, 0);      // the upper level: its own neighbours
        Put(0, 0, 0, 0); Put(1, 0, 0, 1);          // the map's corner
        RetainedMap.Update(m);

        // The half table: record plus one by (z * 80 + x) * 2 + upper, the high bits
        // of the record byte ignored, and 0 where the mesh byte is 240 or more.
        var halves = NeighbourBlend.Halves;
        check(halves[10 * 160 + 10 * 2] == 1 && halves[10 * 160 + 10 * 2 + 1] == 2 && halves[10 * 160 + 11 * 2] == 0
              && halves[10 * 160 + 11 * 2 + 1] == 1 && halves[9 * 160 + 9 * 2] == 2 && halves[0] == 1 && halves[2] == 2
              && halves.Count(b => b != 0) == 12, "neighbour half table differs from the map");
        // Lower: all eight drawn halves of the 3x3 have a neighbour that fogs otherwise,
        // seven one that lights otherwise ((11,9)'s only other record, 3, shares its
        // light); the upper pair and the map's corner pair differ in both.
        check(RetainedMap.FogMixed == 12 && RetainedMap.LightMixed == 11,
            $"mixed halves {RetainedMap.FogMixed}/{RetainedMap.LightMixed}, expected 12/11");
        int generation = NeighbourBlend.Generation;
        RetainedMap.Update(m);
        check(NeighbourBlend.Generation == generation, "an unchanged map re-uploaded its half table");
        m.WriteU8(Map + (uint)(10 * 80 + 11) * 10, 7); m.WriteU8(Map + (uint)(10 * 80 + 11) * 10 + 4, 3);
        RetainedMap.Update(m);
        check(NeighbourBlend.Generation != generation && halves[10 * 160 + 11 * 2] == 4, "a map mutation missed the half table");
        m.WriteU8(Map + (uint)(10 * 80 + 11) * 10, 255);
        RetainedMap.Update(m);
        check(halves[10 * 160 + 11 * 2] == 0, "a removed half stayed in the table");

        int[] records = RetainedScene.Records.ToArray();
        byte[] table = halves.ToArray();
        var cases = new List<object>();
        int[] rec = new int[4];
        float[] kw = new float[4];
        int[][] dotSets = [[4096, 0, 0], [1200, 3000, 77], [0, 4096, 2048]];
        uint[] rgbcs = [0x808080, 0x30A0FF];
        float[] depths = [3000, 8000, 15000, 20000, 23000, 40000];

        (bool Fog, float W, bool Lit, int Rgb) Eval(int[] recs, int own, int hid, float x, float z, float depth, int[] dots, uint rgbc)
        {
            NeighbourBlend.Around(table, own, hid, x, z, rec, kw);
            bool fog = NeighbourBlend.BlendFog(recs, rec, kw, depth, out float w);
            bool lit = NeighbourBlend.BlendLight(recs, rec, kw, dots[0], dots[1], dots[2], rgbc, out int r, out int g, out int b);
            return (fog, w, lit, r | g << 8 | b << 16);
        }
        int Hid(int x, int z, int upper) => (z * 80 + x) * 2 + upper + 1;
        int Own(int x, int z, int upper) => table[z * 160 + x * 2 + upper] - 1;
        void Case(int[] recs, int variant, int x, int z, int upper, float wx, float wz)
        {
            int own = Own(x, z, upper), hid = Hid(x, z, upper);
            foreach (float depth in depths)
                for (int d = 0; d < dotSets.Length; d++)
                {
                    uint rgbc = rgbcs[(d + (int)depth) & 1];
                    var e = Eval(recs, own, hid, wx, wz, depth, dotSets[d], rgbc);
                    cases.Add(new
                    {
                        variant, own, hid, x = wx, z = wz, depth, dots = dotSets[d], rgbc,
                        fog = e.Fog ? e.W : -1f, light = e.Lit ? e.Rgb : -1,
                    });
                }
        }

        // Shared boundaries: every point on an edge or corner between two drawn halves
        // on one level is blended alike from either side.
        int shared = 0, fogBlended = 0, lightBlended = 0;
        for (int z = 9; z <= 11; z++)
            for (int x = 9; x <= 11; x++)
                for (int upper = 0; upper < 2; upper++)
                {
                    if (Own(x, z, upper) < 0) continue;
                    float cx = x * 2048 + 1024, cz = z * 2048 + 1024;
                    foreach (float ox in new[] { -1024f, -600f, 0f, 600f, 1024f })
                        foreach (float oz in new[] { -1024f, -600f, 0f, 600f, 1024f })
                        {
                            if (Math.Abs(ox) != 1024f && Math.Abs(oz) != 1024f) continue;
                            int nx = x + (ox == 1024f ? 1 : ox == -1024f ? -1 : 0), nz = z + (oz == 1024f ? 1 : oz == -1024f ? -1 : 0);
                            foreach ((int ax, int az) in new[] { (nx, z), (x, nz), (nx, nz) })
                            {
                                if ((ax, az) == (x, z) || Own(ax, az, upper) < 0) continue;
                                foreach (float depth in depths)
                                    foreach (var dots in dotSets)
                                    {
                                        var a = Eval(records, Own(x, z, upper), Hid(x, z, upper), cx + ox, cz + oz, depth, dots, 0x808080);
                                        var b = Eval(records, Own(ax, az, upper), Hid(ax, az, upper), cx + ox, cz + oz, depth, dots, 0x808080);
                                        check(a.Fog == b.Fog && Math.Abs(a.W - b.W) <= 0.01f,
                                            $"fog differs across the edge at {cx + ox},{cz + oz} level {upper} depth {depth}: {a.W}/{b.W}");
                                        check(a.Lit == b.Lit && a.Rgb == b.Rgb,
                                            $"light differs across the edge at {cx + ox},{cz + oz} level {upper}: {a.Rgb:X6}/{b.Rgb:X6}");
                                        shared++; if (a.Fog) fogBlended++; if (a.Lit) lightBlended++;
                                    }
                            }
                        }
                }
        check(shared > 0 && fogBlended > 0 && lightBlended > 0, "no blended shared boundary was exercised");

        // A tile's centre is its own record alone; just off it toward a neighbour that
        // lights otherwise, the blend is still exactly the own record's colour, so the
        // blended region meets the unblended without a step.
        {
            float cx = 10 * 2048 + 1024, cz = 10 * 2048 + 1024;
            var centre = Eval(records, 0, Hid(10, 10, 0), cx, cz, 15000, dotSets[1], 0x808080);
            check(!centre.Fog && !centre.Lit, "a tile centre blended its neighbours");
            var near = Eval(records, 0, Hid(10, 10, 0), cx - 0.01f, cz, 15000, dotSets[1], 0x808080);
            int ownRgb = OwnLight(records, 0, dotSets[1], 0x808080);
            check(near.Lit && near.Rgb == ownRgb, $"light steps where the blend begins: {near.Rgb:X6}/{ownRgb:X6}");
            // (9,10)'s record 2 shares record 0's fog: the light alone is blended there.
            check(!near.Fog, "a neighbour with the same fog blended the fog");
        }

        // A missing neighbour weighs nothing: at the middle of tile (10,10)'s edge with
        // (11,10), which has no lower half, only the own record is left.
        {
            float cx = 10 * 2048 + 1024 + 1024, cz = 10 * 2048 + 1024;
            NeighbourBlend.Around(table, 0, Hid(10, 10, 0), cx, cz, rec, kw);
            check(rec[1] == -1 && kw[1] == 0f && kw[0] > 0f, "a missing neighbour weighed in");
            var e = Eval(records, 0, Hid(10, 10, 0), cx, cz, 15000, dotSets[0], 0x808080);
            check(!e.Fog && !e.Lit, "a missing neighbour changed the edge");
        }

        // No fog is a weight of 0, blended as such: half (6000,18000) at 15000 beside
        // no fog at the shared edge of (10,11) and (9,11).
        {
            float ex = 10 * 2048, ez = 11 * 2048 + 1024;
            var e = Eval(records, 0, Hid(10, 11, 0), ex, ez, 15000, dotSets[0], 0x808080);
            float own = Math.Min(LinearDepthCue.Weight(15000, 6000, 18000), 4096);
            check(e.Fog && Math.Abs(e.W - own / 2) < 0.01f, $"no-fog neighbour blended as {e.W}, expected {own / 2}");
        }

        // The upper level blends only with the upper level: (10,10) upper is record 1,
        // beside (11,10) upper record 0, while the lower level there has no half.
        {
            float ex = 11 * 2048, ez = 10 * 2048 + 1024;
            NeighbourBlend.Around(table, 1, Hid(10, 10, 1), ex, ez, rec, kw);
            check(rec[1] == 0 && kw[1] > 0f, "the upper level missed its upper neighbour");
            NeighbourBlend.Around(table, 1, Hid(10, 10, 1), 10 * 2048, ez, rec, kw);
            check(rec[1] == -1 && kw[1] == 0f, "the upper level took the lower level's neighbour");
        }

        // The map's edge: tile (0,0) towards -X has nothing, and nothing is read past it.
        {
            NeighbourBlend.Around(table, 0, Hid(0, 0, 0), 0f, 0f, rec, kw);
            check(rec[1] == -1 && rec[2] == -1 && rec[3] == -1 && kw[1] + kw[2] + kw[3] == 0f, "the map's edge read a neighbour");
        }

        // A curve this does not blend (a Verdite2 knee) leaves the pixel as it was.
        int[] knee = records.ToArray();
        knee[3 * Ints + RetainedScene.RecCurve] = 2;
        {
            float ex = 10 * 2048 + 1024, ez = 10 * 2048;
            NeighbourBlend.Around(table, 0, Hid(10, 10, 0), ex, ez, rec, kw);
            check(rec[2] == 3 && !NeighbourBlend.BlendFog(knee, rec, kw, 15000, out _), "an unsupported curve was blended");
        }

        // The GPU's points: tile (10,10) on both levels, (9,9) and the map's corner,
        // on edges, corners, centres, just inside each side and past the edge (a mesh
        // reaches a little beyond its tile).
        foreach ((int x, int z, int upper) in new[] { (10, 10, 0), (10, 10, 1), (9, 9, 0), (0, 0, 0) })
        {
            float cx = x * 2048 + 1024, cz = z * 2048 + 1024;
            foreach (float ox in new[] { -1034f, -1024f, -1023.5f, -512f, -0.01f, 0f, 300f, 1023.5f, 1024f })
                foreach (float oz in new[] { -1024f, -700f, 0f, 1023.5f, 1030f })
                    Case(records, 0, x, z, upper, cx + ox, cz + oz);
        }
        foreach (float ox in new[] { -800f, 0f, 800f })
            Case(knee, 1, 10, 10, 0, 10 * 2048 + 1024 + ox, 10 * 2048 + 200);
        File.WriteAllText(Path.Combine(output, "neighbour-cases.json"), JsonSerializer.Serialize(new
        {
            tile = NeighbourBlend.Tile, halves = table, variants = new[] { records, knee }, cases,
        }));
        File.WriteAllText(Path.Combine(output, "NeighbourBlend.glsl"), NeighbourBlend.Glsl);
        Console.WriteLine($"Neighbour fixtures: {shared} shared-boundary points ({fogBlended} fog, {lightBlended} light blended), {cases.Count} GPU cases");
    }

    /// <summary>recordLit's colour under one record: NormalColorCol's integers.</summary>
    static int OwnLight(int[] records, int r, int[] a, uint rgbc)
    {
        int rgb = 0;
        for (int c = 0; c < 3; c++)
        {
            int v = records[r * Ints + RetainedScene.RecBk + c] << 12;
            for (int j = 0; j < 3; j++) v += records[r * Ints + RetainedScene.RecLcm + 3 * c + j] * a[j];
            int ir = Math.Clamp(v >> 12, 0, 0x7FFF);
            int mac = (((int)((rgbc >> (8 * c)) & 255u) * ir) << 4) >> 12;
            rgb |= Math.Clamp(mac >> 4, 0, 255) << (8 * c);
        }
        return rgb;
    }
}
