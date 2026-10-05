using System.Text.Json;
using Kf3;
using RecompOne.Runtime;
using RecompOne.Runtime.Context;
using RecompOne.Runtime.Memory;
using Game = Recompiled.KingsField3_game;

namespace SceneProbe;

/// <summary>
/// RenderDistance and runtime 0089's DistanceFade.
///
/// <para>The game's reach: the recompiled func_80034BF4, run on the 28-area corpus's
/// RAM (KF3_CORPUS, default /tmp/verdite3-gpu-reference/shadow-corpus) at every 64th
/// yaw and five pitches, against the model RenderDistance draws from: the eye's tile
/// is the window middle, every lit cell is inside the window, the cone and the radius
/// (the flood only clears), and no cell the model leaves out in the cone comes nearer
/// the eye than <see cref="RenderDistance.GameEdge"/>, from any place in the eye's
/// tile.</para>
///
/// <para>The selection, on a synthetic map and camera: the game's cells are never
/// added, the ring past the radius and the far halves are, and nothing behind the
/// camera, past the distance, empty, or behind the far gate is.</para>
///
/// <para>The fade: fade-cases.json, DistanceFade's CPU reference, for shader_probe.py
/// to run through the GLSL PrimFs and NormalFs compose.</para>
/// </summary>
public static class RenderDistanceFixtures
{
    const uint Map = 0x801D4464, Grid = 0x1F800120, Flag = 0x801B25E5;

    public static void Run(Action<bool, string> check, string output)
    {
        Classifier(check);
        Selection(check);
        Fade(check, output);
    }

    static void Classifier(Action<bool, string> check)
    {
        string dir = Environment.GetEnvironmentVariable("KF3_CORPUS") is { Length: > 0 } c ? c : "/tmp/verdite3-gpu-reference/shadow-corpus";
        if (!Directory.Exists(dir)) { Console.WriteLine($"render distance: no corpus at {dir}; the classifier fixture was skipped"); return; }
        // The flood calls through the dispatcher (0x80016AB8).
        RecompOne.Runtime.Dispatch.Dispatcher.Register("main", new Recompiled.MainDispatchTable());
        RecompOne.Runtime.Dispatch.Dispatcher.Register("game", new Recompiled.GameDispatchTable());
        RecompOne.Runtime.Dispatch.Dispatcher.Load("main");
        RecompOne.Runtime.Dispatch.Dispatcher.Load("game");
        int runs = 0, lit = 0, occluded = 0, areas = 0;
        var edges = new SortedDictionary<int, (float Edge, double Nearest)>();
        foreach (string path in Directory.GetFiles(dir, "area*.ram").OrderBy(p => p))
        {
            var m = new PSMemory();
            byte[] ram = File.ReadAllBytes(path);
            for (int i = 0; i + 4 <= ram.Length; i += 4) m.WriteU32(0x80000000u + (uint)i, BitConverter.ToUInt32(ram, i));
            int d = m.ReadU8(RenderDistance.RadiusAddr);
            int tileX = (int)m.ReadU32(CameraBlock.TileX), tileZ = (int)m.ReadU32(CameraBlock.TileZ);
            areas++;
            foreach (int pitch in new[] { 0, 0x100, 0x200, 0x300, 0xD00 })
                for (int yaw = 0; yaw < 4096; yaw += 64)
                {
                    m.WriteU16(CameraBlock.Angles, (ushort)pitch);
                    m.WriteU16(CameraBlock.Angles + 2, (ushort)(short)(yaw >= 2048 ? yaw - 4096 : yaw));
                    var cpu = new CpuContext { SP = 0x801FF000 };
                    Game.func_80034BF4(cpu, m);
                    runs++;
                    int s5 = CullCone.StockAngle(pitch);
                    int t5 = s5 < 600 ? d * d : (d * d * Cos12(s5 >> 1)) >> 12;
                    var cone = new RenderDistance.Cone((short)m.ReadU16(CameraBlock.Angles + 2), s5);
                    int ox = (int)m.ReadU32(RenderDistance.OriginX), oz = (int)m.ReadU32(RenderDistance.OriginZ);
                    check(tileX - ox == cone.MidCol && tileZ - oz == cone.MidRow,
                        $"{Path.GetFileName(path)} yaw {yaw}: the eye's tile is not the window middle ({tileX - ox}, {tileZ - oz}) vs ({cone.MidCol}, {cone.MidRow})");
                    for (int row = 0; row < 25; row++)
                        for (int col = 0; col < 25; col++)
                        {
                            int i = row - cone.MidRow, j = col - cone.MidCol;
                            bool cross = Math.Abs(i) + Math.Abs(j) <= 1;
                            bool model = i * i + j * j < t5 && cone.Inside(row, col);
                            bool drawn = (m.ReadU8(Grid + (uint)(row * 25 + col)) & 2) != 0;
                            if (drawn) lit++;
                            if (model && !drawn) occluded++;
                            check(!drawn || model || cross,
                                $"{Path.GetFileName(path)} yaw {yaw} pitch {pitch}: cell ({row}, {col}) is drawn outside the model (T5 {t5})");
                        }
                    // The nearest a left-out cell in the cone comes, from 9x9 places in
                    // the eye's tile, against the cached edge over every yaw.
                    float edge = RenderDistance.GameEdge(t5, s5);
                    double nearest = double.MaxValue;
                    for (int pr = 0; pr <= 8; pr++)
                        for (int pc = 0; pc <= 8; pc++)
                        {
                            double er = cone.MidRow + pr / 8.0, ec = cone.MidCol + pc / 8.0;
                            for (int row = -8; row <= 32; row++)
                                for (int col = -8; col <= 32; col++)
                                {
                                    int i = row - cone.MidRow, j = col - cone.MidCol;
                                    if ((uint)row <= 24u && (uint)col <= 24u && i * i + j * j < t5) continue;
                                    if (!cone.Inside(row, col)) continue;
                                    double dr = Math.Clamp(er, row, row + 1) - er, dc = Math.Clamp(ec, col, col + 1) - ec;
                                    nearest = Math.Min(nearest, Math.Sqrt(dr * dr + dc * dc) * 2048);
                                }
                        }
                    check(nearest >= edge - 0.5,
                        $"{Path.GetFileName(path)} yaw {yaw} pitch {pitch}: a left-out cell {nearest:0} from the eye, inside the edge {edge:0}");
                    var key = t5;
                    if (!edges.TryGetValue(key, out var e) || nearest < e.Nearest) edges[key] = (edge, nearest);
                }
        }
        Console.WriteLine($"render distance: {areas} areas, {runs} classifier runs, {lit} cells lit, all inside the model; " +
                          $"{occluded} model cells the flood cleared");
        foreach (var (t5, e) in edges)
            Console.WriteLine($"render distance: T5 {t5}: game edge {e.Edge / 2048:0.000} tiles, nearest left-out cell seen {e.Nearest / 2048:0.000}");
    }

    static void Selection(Action<bool, string> check)
    {
        var m = new PSMemory();
        for (uint i = 0; i < 64000; i += 4) m.WriteU32(Map + i, uint.MaxValue);
        // Every half on a 40x40 patch has mesh 7 at height 0; tile (30, 52) has none.
        for (int z = 20; z < 60; z++)
            for (int x = 20; x < 60; x++)
                for (uint upper = 0; upper < 2; upper++)
                {
                    if (x == 30 && z == 52) continue;
                    uint half = Map + (uint)(z * 80 + x) * 10 + upper * 5;
                    m.WriteU8(half, (byte)(upper == 0 ? 7 : 200)); m.WriteU8(half + 1, 0);
                }
        RetainedMap.MeshYMin[7] = -512; RetainedMap.MeshYMax[7] = 0; RetainedMap.MeshReach[7] = 1024;
        RetainedMap.MeshYMin[200] = -512; RetainedMap.MeshYMax[200] = 0; RetainedMap.MeshReach[200] = 1024;
        const uint table = 0x80010000;
        m.WriteU32(0x801A929C, table); m.WriteU32(table + 4, 240);
        // The eye in tile (40, 40), looking along +Z, level; the window as yaw 0 puts it.
        const int cx = 40, cz = 40;
        m.WriteU32(CameraBlock.TileX, cx); m.WriteU32(CameraBlock.TileZ, cz);
        var cone = new RenderDistance.Cone(0, 440);
        m.WriteU32(RenderDistance.OriginX, (uint)(cx - cone.MidCol)); m.WriteU32(RenderDistance.OriginZ, (uint)(cz - cone.MidRow));
        var view = new RetainedScene.View
        {
            R00 = 1, R11 = 1, R22 = 1, CamX = cx * 2048 + 1024, CamY = -1024, CamZ = cz * 2048 + 1024, H = 200, Cx = 160, Cy = 120,
        };
        int ox = cx - cone.MidCol, oz = cz - cone.MidRow;
        foreach (int t5 in new[] { 81, 169 })
            foreach (float tiles in new[] { 16f, 30f })
            {
                RetainedScene.BeginFrame(view);
                float edge = tiles * 2048;
                int n = RenderDistance.AddHalves(m, view, t5, edge);
                check(n > 0, "nothing added");
                var added = RenderDistance.Added.ToHashSet();
                foreach (int index in added)
                {
                    int tile = index >> 1, x = tile % 80, z = tile / 80, i = z - cz, j = x - cx;
                    check(!((uint)(x - ox) <= 24u && (uint)(z - oz) <= 24u && i * i + j * j < t5), $"T5 {t5}: the game's tile ({x}, {z}) added");
                    double nx = Math.Clamp(view.CamX, x * 2048, x * 2048 + 2048) - view.CamX, nz = Math.Clamp(view.CamZ, z * 2048, z * 2048 + 2048) - view.CamZ;
                    check(nx * nx + nz * nz < (double)edge * edge, $"T5 {t5}: tile ({x}, {z}) past the distance added");
                    check(z * 2048 + 2048 + 1024 > view.CamZ, $"T5 {t5}: tile ({x}, {z}) behind the camera added");
                    check(!(x == 30 && z == 52), "an empty half added");
                    check(RetainedScene.CurrentMainHalves[index] == 255, "an added half is not in the gate");
                }
                // The ring: straight ahead just past the radius, inside the window; the far
                // halves: straight ahead near the distance, outside the window.
                int ring = (int)Math.Ceiling(Math.Sqrt(t5));
                check(added.Contains(((cz + ring) * 80 + cx) * 2), $"T5 {t5}: the ring tile {ring} ahead not added");
                int farZ = Math.Min(59, cz + (int)tiles - 1);
                check(added.Contains((farZ * 80 + cx) * 2), $"T5 {t5}, {tiles} tiles: the far tile ({cx}, {farZ}) not added");
                check(added.Contains((farZ * 80 + cx) * 2 + 1), $"T5 {t5}: the upper half not added");
                check(!added.Contains(((cz - 3) * 80 + cx) * 2), $"T5 {t5}: a tile behind added");
                check(!added.Contains(((cz + 2) * 80 + cx) * 2), $"T5 {t5}: a tile in the game's radius added");
            }
        // The far gate: a load pending with the flag set skips the second half of the table.
        m.WriteU16(RenderDistance.LoadPending, 1); m.WriteU8(RenderDistance.LoadFlag, 1);
        RetainedScene.BeginFrame(view);
        RenderDistance.AddHalves(m, view, 81, 16 * 2048);
        check(RenderDistance.Added.Count > 0 && RenderDistance.Added.All(i => (i & 1) == 0), "the far gate let a mesh past half the table through");
        m.WriteU16(RenderDistance.LoadPending, 0);
        Console.WriteLine("render distance: selection fixtures passed");
    }

    static void Fade(Action<bool, string> check, string output)
    {
        var cases = new List<float[]>();
        var random = new Random(8902);
        float[][] settings = [[24576, 6144, 65024], [16000, 2048, 65024], [61440, 8192, 65024], [20000, 0, 65024], [20000, 4096, 0], [0, 0, 0]];
        foreach (var s in settings)
            for (int k = 0; k < 60; k++)
            {
                float camX = random.Next(0, 163840), camZ = random.Next(0, 163840);
                float dist = k < 20 ? s[0] - s[1] * k / 19f : random.Next(0, 70000);
                double a = random.NextDouble() * Math.PI * 2;
                float x = camX + (float)(dist * Math.Cos(a)), z = camZ + (float)(dist * Math.Sin(a));
                float depth = k % 3 == 0 ? random.Next(60000, 66000) : random.Next(0, 70000);
                float w = DistanceFade.Weight(x - camX, z - camZ, depth, s[0], s[1], s[2]);
                cases.Add([camX, camZ, s[0], s[1], s[2], x, z, depth, w]);
            }
        // Horizontal only, 0 at and past the edge and the depth limit, 1 well inside.
        check(DistanceFade.Weight(3000, 4000, 100, 20000, 4096, 65024) == 1f, "inside the band is faded");
        check(DistanceFade.Weight(12000, 16000, 100, 20000, 4096, 65024) == 0f, "the edge is not 0");
        check(DistanceFade.Weight(0, 0, 65024, 61440, 0, 65024) == 0f, "the depth limit is not 0");
        check(DistanceFade.Weight(0, 0, 65023, 61440, 0, 65024) < 0.001f, "the depth limit fades late");
        check(DistanceFade.Weight(0, 0, 70000, 0, 0, 0) == 1f, "off fades");
        File.WriteAllText(Path.Combine(output, "fade-cases.json"), JsonSerializer.Serialize(cases));
        File.WriteAllText(Path.Combine(output, "DistanceFade.glsl"), DistanceFade.Glsl);
        var dropped = new List<int[]>();
        foreach (float fade in new[] { 0f, 0.05f, 0.25f, 0.5f, 0.74f, 0.999f, 1f })
            for (int y = 0; y < 4; y++)
                for (int x = 0; x < 4; x++)
                    dropped.Add([(int)(fade * 1000), x, y, DistanceFade.Dropped(fade, x, y) ? 1 : 0]);
        File.WriteAllText(Path.Combine(output, "fade-dither.json"), JsonSerializer.Serialize(dropped));
        Console.WriteLine($"render distance: {cases.Count} fade cases and {dropped.Count} dither cases exported");
    }

    static int Cos12(int a) => (int)Math.Round(4096 * Math.Cos(a * Math.PI / 2048));
}
