using System.Runtime.InteropServices;
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
    static ulong _lights;
    public static bool Ready { get; private set; }
    public static long ChunkBuilds, MapUpdates, RecordUpdates;
    public static void Invalidate()
    {
        Ready = false; _lights = 0; Array.Clear(Signatures); Array.Clear(Chunks);
        Array.Clear(Models); Array.Clear(ModelSignatures);
    }
    public static void Update(PSMemory m)
    {
        UpdateRecords(m);
        uint table = m.ReadU32(TablePointer);
        if (!RetainedAssets.InRam(table, 12)) { Ready = false; return; }
        Span<bool> used = stackalloc bool[240]; used.Clear();
        for (uint i = 0; i < 12800; i++) { byte kind = m.ReadU8(Map + i * 5); if (kind < 240) used[kind] = true; }
        for (uint i = 0; i < 240; i++)
        {
            if (!used[(int)i]) continue;
            uint header = table + 12 + i * 28;
            Models[i] = RetainedAssets.Get(m, table, header, RetainedAssets.Family.Lit, out string reason);
            var mesh = Models[i];
            uint vertices = m.ReadU32(header + 4), address = table + 12 + m.ReadU32(header);
            if (mesh == null || vertices > 8192 || !RetainedAssets.InRam(address, vertices * 8) || mesh.MaxVertex >= vertices)
            { Models[i] = null; ModelSignatures[i] = 0; GpuWorld.Fallback(0x8003BB04, 0, reason.Length > 0 ? reason : "map-vertex-range"); continue; }
            ModelSignatures[i] = mesh.FaceHash ^ mesh.NormalHash ^ RetainedAssets.Hash(m.Ram, address, vertices * 8);
        }
        bool dirty = false;
        for (int chunk = 0; chunk < 100; chunk++)
        {
            ulong hash = 14695981039346656037;
            int x0 = chunk % 10 * 8, z0 = chunk / 10 * 8;
            for (int z = z0; z < z0 + 8; z++)
                for (int x = x0; x < x0 + 8; x++)
                {
                    uint tile = Map + (uint)(z * 80 + x) * 10;
                    hash = (hash ^ RetainedAssets.Hash(m.Ram, tile, 10)) * 1099511628211;
                    for (uint half = 0; half <= 5; half += 5)
                    { byte kind = m.ReadU8(tile + half); if (kind < 240) hash = (hash ^ ModelSignatures[kind]) * 1099511628211; }
                }
            if (Chunks[chunk] != null && Signatures[chunk] == hash) continue;
            Chunks[chunk] = BuildChunk(m, table, x0, z0); Signatures[chunk] = hash;
            ChunkBuilds++; dirty = true;
        }
        if (dirty)
        {
            var vertices = new List<RetainedScene.Vertex>();
            foreach (var chunk in Chunks) if (chunk != null) vertices.AddRange(chunk);
            RetainedScene.SetStatic(CollectionsMarshal.AsSpan(vertices)); MapUpdates++;
        }
        Ready = RetainedScene.StaticCount[0] > 0;
    }
    static RetainedScene.Vertex[] BuildChunk(PSMemory m, uint table, int x0, int z0)
    {
        var list = new List<RetainedScene.Vertex>(); var store = RetainedScene.MeshCorners;
        for (int z = z0; z < z0 + 8; z++)
            for (int x = x0; x < x0 + 8; x++)
                for (uint upper = 0; upper < 2; upper++)
                {
                    uint half = Map + (uint)(z * 80 + x) * 10 + upper * 5;
                    byte kind = m.ReadU8(half); if (kind >= 240 || Models[kind] is not { } mesh) continue;
                    int rot = m.ReadU8(half + 2) & 3, record = m.ReadU8(half + 4) & 63;
                    uint vertices = table + 12 + m.ReadU32(table + 12 + (uint)kind * 28);
                    uint light = RetainedScene.PackLight(record, rot, 0, 0, 0, false, false, false, false, false);
                    foreach (var face in mesh.Faces)
                    {
                        // The bulk assembler deliberately ignores GT4; near uses a
                        // different subdivision policy and stays an explicit route.
                        if ((face.Command & 0xFD) == 0x3C) continue;
                        for (int j = 0; j < face.Corners; j++)
                        {
                            var v = store[face.Corner + j]; uint p = vertices + (uint)v.X * 8;
                            int px = (short)m.ReadU16(p), py = (short)m.ReadU16(p + 2), pz = (short)m.ReadU16(p + 4);
                            (px, pz) = rot switch { 1 => (pz, -px), 2 => (-px, -pz), 3 => (-pz, px), _ => (px, pz) };
                            v.X = x * 2048 + 1024 + px; v.Y = -(m.ReadU8(half + 1) << 7) + py; v.Z = z * 2048 + 1024 + pz;
                            v.Dqa = v.Dqb = v.Curve = 0; v.Light = light; v.Rgbc = 0x808080;
                            // recordLit returns a colour, whereas a model mesh carries
                            // raw light dots. Keeping FlagDots here lights it twice.
                            v.Flags = (v.Flags & ~(RetainedScene.FlagQuadTail | RetainedScene.FlagDots))
                                | RetainedScene.HalfFlag(x, z, (int)upper);
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
    public static bool Submit(PSMemory m, uint half, bool near, uint caller)
    {
        if (!GpuWorld.Capture) return false;
        GpuWorld.Submissions++;
        uint index = half - Map;
        if (index >= 64000 || index % 5 != 0) { GpuWorld.Fallback(0x8003BB04, caller, "independent-map-cell"); return false; }
        uint kind = m.ReadU8(half);
        if (kind >= 240 || Models[kind] == null) { GpuWorld.Fallback(0x8003BB04, caller, "map-mesh-unavailable"); return false; }
        if ((m.ReadU32(0x1F800054) & 0xFFFFFF) != 0x808080)
        { GpuWorld.Fallback(0x8003BB04, caller, "map-source-colour"); return false; }
        if (near)
        {
            // Opt-in source descriptor extraction; never suppresses the near route.
            RetainedNear.Probe(m, Models[kind]!, half);
            GpuWorld.Fallback(0x8003BB04, caller, "near-subdivision-pending");
            return false;
        }
        int tile = (int)(index / 10); RetainedScene.NoteHalf(tile % 80, tile / 80, (int)(index % 10 / 5));
        GpuWorld.Retained++;
        return GpuWorld.Drawing;
    }
}
