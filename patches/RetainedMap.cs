using RecompOne.Runtime;
using RecompOne.Runtime.Memory;

namespace Kf3;

/// <summary>Persistent source map chunks; light changes upload records independently.</summary>
public static class RetainedMap
{
    const uint Map = 0x801D4464, TablePointer = 0x801A929C, Lights = 0x801AEEFC;
    static readonly RetainedScene.Vertex[][] Chunks = new RetainedScene.Vertex[100][];
    static readonly ulong[] Signatures = new ulong[100];
    static readonly RetainedAssets.Mesh?[] Models = new RetainedAssets.Mesh[240];
    static readonly ulong[] ModelSignatures = new ulong[240];
    static readonly int[] Records = new int[RetainedScene.RecordCount * RetainedScene.RecordInts];
    static ulong _lights, _water;
    // Each half's record plus one, 0 for none, as NeighbourBlend takes them.
    static readonly byte[] Halves = new byte[RetainedScene.HalvesW * RetainedScene.HalvesH];
    static int _mixedHalves = -1; static long _mixedRecords = -1;
    public static bool Ready { get; private set; }
    /// <summary>Each map mesh's vertical extent and its horizontal reach from the tile's
    /// centre (the largest |X| or |Z|, so any rotation), in world units, for
    /// RenderDistance's frustum test.</summary>
    public static readonly short[] MeshYMin = new short[240], MeshYMax = new short[240], MeshReach = new short[240];
    public static long ChunkBuilds, MapUpdates, RecordUpdates;
    /// <summary>Drawn halves beside a half on the same level (of the eight around it)
    /// whose record fogs, or lights, otherwise: where NeighbourBlend changes anything.</summary>
    public static int FogMixed, LightMixed;
    public static void Invalidate()
    {
        Ready = false; _lights = 0; Array.Clear(Signatures); Array.Clear(Chunks);
        Array.Clear(Models); Array.Clear(ModelSignatures);
    }
    /// <summary>KF3_MAPPROBE=1: a line per rebuild, with what it waited for and cost.</summary>
    static readonly bool _probe = Environment.GetEnvironmentVariable("KF3_MAPPROBE") == "1";

    /// <summary>
    /// An area's tables fill in over several frames after its load (the meshes, then
    /// the water rects), and every step left most chunks stale: two to five whole-map
    /// rebuilds an arrival, 20-115 ms of build and sort and 7-24 ms of upload each.
    /// So a rebuild of more than <see cref="SettleChunks"/> chunks, or of a map not
    /// yet built, waits until what the chunks are built from has held for
    /// <see cref="SettleMs"/>; until then the halves are the game's own packets
    /// (<see cref="Submit"/>), never an old map. A door or a water change is a chunk
    /// or a few, built at once. KF3_MAP_SETTLE=ms, 0 for no wait.
    /// </summary>
    public static int SettleMs = int.TryParse(Environment.GetEnvironmentVariable("KF3_MAP_SETTLE"), out int settle) && settle >= 0 ? settle : 150;
    const int SettleChunks = 8;
    static readonly ulong[] Seen = new ulong[100];
    static long _changedAt;
    static int _waited;

    public static void Update(PSMemory m)
    {
        int sigChanged = 0;
        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        UpdateRecords(m);
        UpdateHalves(m);
        uint table = m.ReadU32(TablePointer);
        if (!RetainedAssets.InRam(table, 12)) { Ready = false; return; }
        Span<bool> used = stackalloc bool[240]; used.Clear();
        for (uint i = 0; i < 12800; i++) { byte kind = m.ReadU8(Map + i * 5); if (kind < 240) used[kind] = true; }
        for (uint i = 0; i < 240; i++)
        {
            if (!used[(int)i]) continue;
            uint header = table + 12 + i * 28;
            Models[i] = RetainedAssets.Get(m, table, header, RetainedAssets.Family.MapBulk, out string reason);
            var mesh = Models[i];
            uint vertices = m.ReadU32(header + 4), address = table + 12 + m.ReadU32(header);
            if (mesh == null || vertices > 8192 || !RetainedAssets.InRam(address, vertices * 8) || mesh.MaxVertex >= vertices)
            { Models[i] = null; ModelSignatures[i] = 0; GpuWorld.Fallback(0x8003BB04, 0, reason.Length > 0 ? reason : "map-vertex-range"); continue; }
            ulong signature = mesh.FaceHash ^ mesh.NormalHash ^ RetainedAssets.Hash(m.Ram, address, vertices * 8);
            if (signature != ModelSignatures[i]) { Bounds(m, (int)i, address, vertices); sigChanged++; }
            ModelSignatures[i] = signature;
        }
        // The water's free corners (WaterSwell) are worked out over the whole map, from
        // what places a half's mesh and which faces are water, before any chunk is built.
        ulong water = WaterRects.Key * 1099511628211;
        for (uint i = 0; i < 12800; i++)
        {
            uint half = Map + i * 5; byte kind = m.ReadU8(half);
            if (kind >= 240) continue;
            water = (water ^ (i << 8 | kind | (uint)m.ReadU8(half + 1) << 24 | (uint)(m.ReadU8(half + 2) & 3) << 22)) * 1099511628211;
            water = (water ^ ModelSignatures[kind]) * 1099511628211;
        }
        if (water != _water) { _water = water; WaterSwell.Build(m, Map, table, Models); }
        Span<ulong> now = stackalloc ulong[100];
        int stale = 0;
        bool changed = false;
        for (int chunk = 0; chunk < 100; chunk++)
        {
            // A chunk's water faces and free corners (WaterSwell), its tiles and meshes.
            ulong hash = (14695981039346656037 ^ WaterSwell.ChunkHash[chunk]) * 1099511628211;
            int x0 = chunk % 10 * 8, z0 = chunk / 10 * 8;
            for (int z = z0; z < z0 + 8; z++)
                for (int x = x0; x < x0 + 8; x++)
                {
                    uint tile = Map + (uint)(z * 80 + x) * 10;
                    for (uint half = 0; half <= 5; half += 5)
                    {
                        // Only what BuildChunk reads: the game rewrites bits 2-3 of +2
                        // on the tiles round the player as they walk, and hashing the
                        // whole record re-sorted and re-uploaded the map each time.
                        byte kind = m.ReadU8(tile + half);
                        uint read = kind | (uint)m.ReadU8(tile + half + 1) << 8 | (uint)(m.ReadU8(tile + half + 2) & 3) << 16
                            | (uint)(m.ReadU8(tile + half + 4) & 63) << 18;
                        hash = (hash ^ read) * 1099511628211;
                        if (kind < 240) hash = (hash ^ ModelSignatures[kind]) * 1099511628211;
                    }
                }
            now[chunk] = hash;
            if (hash != Seen[chunk]) { Seen[chunk] = hash; changed = true; }
            if (Chunks[chunk] == null || Signatures[chunk] != hash) stale++;
        }
        if (changed) _changedAt = t0;
        if (stale == 0) return;
        if ((!Ready || stale > SettleChunks) &&
            System.Diagnostics.Stopwatch.GetElapsedTime(_changedAt, t0).TotalMilliseconds < SettleMs)
        { _waited++; return; }

        // Each stale chunk on its own core: they read guest RAM and the meshes only,
        // and the game thread waits here, so nothing writes either meanwhile.
        Span<int> todo = stackalloc int[stale];
        for (int chunk = 0, n = 0; chunk < 100; chunk++)
            if (Chunks[chunk] == null || Signatures[chunk] != now[chunk]) todo[n++] = chunk;
        int[] work = todo.ToArray();
        unsafe
        {
            fixed (byte* ram = m.Ram)
            {
                nint at = (nint)ram;
                Parallel.For(0, work.Length, i =>
                {
                    int chunk = work[i];
                    Chunks[chunk] = BuildChunk((byte*)at, table, chunk % 10 * 8, chunk / 10 * 8);
                });
            }
        }
        for (int i = 0; i < work.Length; i++) Signatures[work[i]] = now[work[i]];
        ChunkBuilds += work.Length;
        long t1 = System.Diagnostics.Stopwatch.GetTimestamp();

        int total = 0;
        foreach (var chunk in Chunks) if (chunk != null) total += chunk.Length;
        if (_all.Length < total) _all = new RetainedScene.Vertex[total + total / 4];
        total = 0;
        foreach (var chunk in Chunks)
            if (chunk != null) { chunk.CopyTo(_all, total); total += chunk.Length; }
        RetainedScene.SetStatic(_all.AsSpan(0, total)); MapUpdates++;
        if (_probe)
            Console.WriteLine($"[KF3] mapprobe: built {work.Length} chunk(s) after {_waited} update(s) waiting, " +
                $"{total} verts, models changed {sigChanged}, " +
                $"build {System.Diagnostics.Stopwatch.GetElapsedTime(t0, t1).TotalMilliseconds:0.0} ms, " +
                $"concat+sort {System.Diagnostics.Stopwatch.GetElapsedTime(t1).TotalMilliseconds:0.0} ms, " +
                $"overlays {string.Join("+", RecompOne.Runtime.Dispatch.Dispatcher.ActiveNames)}");
        _waited = 0;
        Ready = RetainedScene.StaticCount[0] > 0;
    }
    /// <summary>The chunks laid end to end for SetStatic, kept between rebuilds.</summary>
    static RetainedScene.Vertex[] _all = [];
    static void Bounds(PSMemory m, int kind, uint address, uint vertices)
    {
        int low = short.MaxValue, high = short.MinValue, reach = 0;
        for (uint v = 0; v < vertices; v++)
        {
            int x = (short)m.ReadU16(address + v * 8), y = (short)m.ReadU16(address + v * 8 + 2), z = (short)m.ReadU16(address + v * 8 + 4);
            low = Math.Min(low, y); high = Math.Max(high, y); reach = Math.Max(reach, Math.Max(Math.Abs(x), Math.Abs(z)));
        }
        if (low > high) low = high = 0;
        MeshYMin[kind] = (short)low; MeshYMax[kind] = (short)high; MeshReach[kind] = (short)Math.Min(reach, short.MaxValue);
    }
    static unsafe byte U8(byte* ram, uint a) => ram[a & 0x1FFFFF];
    static unsafe ushort U16(byte* ram, uint a) => *(ushort*)(ram + (a & 0x1FFFFF));
    static unsafe uint U32(byte* ram, uint a) => *(uint*)(ram + (a & 0x1FFFFF));
    static unsafe RetainedScene.Vertex[] BuildChunk(byte* ram, uint table, int x0, int z0)
    {
        var list = new List<RetainedScene.Vertex>(); var store = RetainedScene.MeshCorners;
        for (int z = z0; z < z0 + 8; z++)
            for (int x = x0; x < x0 + 8; x++)
                for (uint upper = 0; upper < 2; upper++)
                {
                    uint half = Map + (uint)(z * 80 + x) * 10 + upper * 5;
                    byte kind = U8(ram, half); if (kind >= 240 || Models[kind] is not { } mesh) continue;
                    int rot = U8(ram, half + 2) & 3, record = U8(ram, half + 4) & 63;
                    uint vertices = table + 12 + U32(ram, table + 12 + (uint)kind * 28);
                    uint light = RetainedScene.PackLight(record, rot, 0, 0, 0, false, false, false, false, false);
                    // Last face first, as the table walk draws one slot's faces
                    // (RetainedAssets.Build): the first face is drawn on top.
                    for (int f = mesh.Faces.Length - 1; f >= 0; f--)
                    {
                        var face = mesh.Faces[f];
                        // The bulk assembler deliberately ignores GT4; near uses a
                        // different subdivision policy and stays an explicit route.
                        if ((face.Command & 0xFD) == 0x3C) continue;
                        // Water (WaterRects) is flagged for the murk, the ripples, the
                        // surface buffer and the plane finder; its free corners swell.
                        bool water = WaterRects.IsWater(face.Semi, (uint)store[face.Corner].Texpage, store[face.Corner].Rect);
                        for (int j = 0; j < face.Corners; j++)
                        {
                            var v = store[face.Corner + j]; uint p = vertices + (uint)v.X * 8;
                            int px = (short)U16(ram, p), py = (short)U16(ram, p + 2), pz = (short)U16(ram, p + 4);
                            (px, pz) = rot switch { 1 => (pz, -px), 2 => (-px, -pz), 3 => (-pz, px), _ => (px, pz) };
                            int wx = x * 2048 + 1024 + px, wy = -(U8(ram, half + 1) << 7) + py, wz = z * 2048 + 1024 + pz;
                            v.X = wx; v.Y = wy; v.Z = wz;
                            v.Dqa = v.Dqb = v.Curve = 0; v.Light = light; v.Rgbc = 0x808080;
                            // recordLit returns a colour, whereas a model mesh carries
                            // raw light dots. Keeping FlagDots here lights it twice.
                            v.Flags = (v.Flags & ~(RetainedScene.FlagQuadTail | RetainedScene.FlagDots))
                                | RetainedScene.HalfFlag(x, z, (int)upper)
                                | (water ? RetainedScene.FlagWater : 0)
                                | (water && WaterSwell.IsFree(wx, wy, wz) ? RetainedScene.FlagSwell : 0);
                            list.Add(v);
                        }
                    }
                }
        return list.ToArray();
    }
    static void UpdateRecords(PSMemory m)
    {
        ulong hash = RetainedAssets.Hash(m.Ram, Lights, 64 * 0x6C);
        if (hash == _lights) return;
        for (uint r = 0; r < 64; r++)
        {
            uint p = Lights + r * 0x6C; int at = (int)r * RetainedScene.RecordInts;
            for (uint turn = 0; turn < 4; turn++)
                for (uint k = 0; k < 9; k++) Records[at + turn * 9 + k] = (short)m.ReadU16(p + turn * 20 + k * 2);
            for (uint k = 0; k < 9; k++) Records[at + 36 + k] = (short)m.ReadU16(p + 0x50 + k * 2);
            for (uint k = 0; k < 3; k++) Records[at + 45 + k] = m.ReadU8(p + 0x64 + k) << 4;
            ushort near = m.ReadU16(p + 0x68), far = m.ReadU16(p + 0x6A);
            Records[at + 48] = near | far << 16;
            Records[at + 49] = near; Records[at + 50] = far;
            Records[at + 51] = near >= 32000 ? 0 : (int)LinearDepthCue.Curve;
        }
        RetainedScene.SetRecords(Records); _lights = hash; RecordUpdates++;
    }
    static void UpdateHalves(PSMemory m)
    {
        // Half i of the map is (z * 80 + x) * 2 + upper, the table's own layout.
        for (uint i = 0; i < 12800; i++)
        {
            uint half = Map + i * 5;
            Halves[i] = m.ReadU8(half) < 240 ? (byte)((m.ReadU8(half + 4) & 63) + 1) : (byte)0;
        }
        NeighbourBlend.SetHalves(Halves);
        if (_mixedHalves == NeighbourBlend.Generation && _mixedRecords == RecordUpdates) return;
        _mixedHalves = NeighbourBlend.Generation; _mixedRecords = RecordUpdates;
        int fog = 0, light = 0;
        for (int z = 0; z < 80; z++)
            for (int x = 0; x < 80; x++)
                for (int upper = 0; upper < 2; upper++)
                {
                    int own = Halves[z * 160 + x * 2 + upper] - 1;
                    if (own < 0) continue;
                    bool f = false, l = false;
                    for (int dz = -1; dz <= 1; dz++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int nx = x + dx, nz = z + dz;
                            if ((uint)nx >= 80 || (uint)nz >= 80) continue;
                            int r = Halves[nz * 160 + nx * 2 + upper] - 1;
                            if (r < 0 || r == own) continue;
                            f |= Records[r * RetainedScene.RecordInts + RetainedScene.RecWord] != Records[own * RetainedScene.RecordInts + RetainedScene.RecWord];
                            for (int k = RetainedScene.RecLcm; k < RetainedScene.RecWord && !l; k++)
                                l = Records[r * RetainedScene.RecordInts + k] != Records[own * RetainedScene.RecordInts + k];
                        }
                    if (f) fog++;
                    if (l) light++;
                }
        FogMixed = fog; LightMixed = light;
    }
    public static bool Submit(PSMemory m, uint half, bool near, uint caller)
    {
        if (!GpuWorld.Capture) return false;
        // Not built for this area yet (settling): the game's own packets, not an old map.
        if (!Ready) return false;
        GpuWorld.Submissions++;
        uint index = half - Map;
        if (index >= 64000 || index % 5 != 0) { GpuWorld.Fallback(0x8003BB04, caller, "independent-map-cell"); return false; }
        uint kind = m.ReadU8(half);
        if (kind >= 240 || Models[kind] == null) { GpuWorld.Fallback(0x8003BB04, caller, "map-mesh-unavailable"); return false; }
        if ((m.ReadU32(0x1F800054) & 0xFFFFFF) != 0x808080)
        { GpuWorld.Fallback(0x8003BB04, caller, "map-source-colour"); return false; }
        if (near)
        {
            // Opt-in source descriptor extraction: a probe, which draws nothing.
            if (RetainedNear.Enabled)
            {
                uint table = m.ReadU32(TablePointer), header = table + 12 + kind * 28;
                var mesh = RetainedAssets.Get(m, table, header, RetainedAssets.Family.Lit, out _);
                if (mesh != null) RetainedNear.Probe(m, mesh, half);
            }
        }
        // A near half is the same static mesh: the GPU clips it at the eye, where the
        // near path's libgte division left corners at or behind the eye without a depth
        // (their packets drew in painter's order over the models).
        int tile = (int)(index / 10); RetainedScene.NoteHalf(tile % 80, tile / 80, (int)(index % 10 / 5));
        RenderDistance.NoteWalk((int)(index / 5));
        GpuWorld.Retained++;
        return GpuWorld.Drawing;
    }
}
