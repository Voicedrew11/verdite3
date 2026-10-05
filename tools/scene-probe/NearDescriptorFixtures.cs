using System.Text.Json;
using Kf3;
using RecompOne.Runtime;
using RecompOne.Runtime.Context;
using RecompOne.Runtime.Memory;
using Game = Recompiled.KingsField3_game;

/// <summary>Compare near-map source descriptors with recompiled packet counts
/// and OT buckets from synthetic fixtures.</summary>
public static class NearDescriptorFixtures
{
    const uint Table = 0x80010000, Vertices = 0x80011000, Normals = 0x80012000, FaceBase = 0x80013000;
    const uint Cache = 0x80020000, Prim = 0x80030000, OtBase = 0x80080000;
    const int PrimRegion = 0x4000, OtRegion = 0x8000;
    const int H = 0x100, Ofx = 0x140, Ofy = 0x0F0;

    public static int Run(PSMemory memory, string output)
    {
        int assertions = 0;
        void Check(bool ok, string why)
        {
            assertions++;
            if (!ok) throw new InvalidOperationException("near descriptor fixture: " + why);
        }

        RetainedNear.FaceDescriptor Describe(RetainedAssets.Mesh mesh, uint vertices)
        {
            byte[] ram = memory.Ram.ToArray();
            uint[] pad = Enumerable.Range(0, 256).Select(i => memory.ReadU32(0x1F800000u + (uint)i * 4)).ToArray();
            var before = new Gte.State(); Gte.Save(before);
            var descriptor = RetainedNear.Describe(memory, mesh, 0, vertices, 0);
            var after = new Gte.State(); Gte.Save(after);
            Check(memory.Ram.SequenceEqual(ram), "descriptor modified RAM");
            Check(pad.Select((v, i) => v == memory.ReadU32(0x1F800000u + (uint)i * 4)).All(v => v), "descriptor modified scratchpad");
            Check(Gte.Diff(before, after) == null, "descriptor modified GTE state");
            return descriptor;
        }

        var cases = new List<object>();

        // --- treatment / level / OT key against the recompiled routine -------
        // Face depth targets stay above H/2 (128) so the division entry test does
        // not itself drop them; level 3 needs a zero depth and must be dropped.
        foreach (var (command, depth, level, label) in new (byte, int, int, string)[]
        {
            (0x24, 600, 1, "flat-tri-level1"),
            (0x24, 300, 2, "flat-tri-level2"),
            (0x2C, 600, 1, "flat-quad-level1"),
            (0x2C, 300, 2, "flat-quad-level2"),
            (0x34, 300, 2, "gouraud-tri-level2"),
            (0x3C, 300, 0, "gouraud-quad-gt4-skipped"),
            (0x24, 0, 3, "flat-tri-level3-dropped"),
        })
        {
            WriteModel(memory, [(command, depth)], out uint vertices, out _);
            RunNear(memory, PrimRegion);
            var mesh = RetainedAssets.Get(memory, Table, Table + 12, RetainedAssets.Family.Lit, out string reason);
            Check(mesh != null, $"{label}: mesh build failed: {reason}");
            var face = mesh!.Faces[0];
            var d = Describe(mesh, vertices);
            CountPackets(memory, face.Command, out int prims, out int otzBucket);

            if (command == 0x3C)
            {
                Check(!d.DepthEligible && d.Reason == "reference-command-skip", $"{label}: GT4 not attributed reference-command-skip");
                Check(prims == 0, $"{label}: near routine emitted {prims} packets for a skipped GT4");
            }
            else if (depth == 0)
            {
                Check(d.Level == level, $"{label}: level {d.Level} != {level}");
                Check(!d.DepthEligible && d.Reason == "division-depth-limit", $"{label}: depth-0 face not depth-eligible: {d.Reason}");
                Check(prims == 0, $"{label}: near routine emitted {prims} packets for an all-sub-H/2 face");
            }
            else
            {
                Check(d.DepthEligible, $"{label}: descriptor depth-ineligible: {d.Reason}");
                Check(d.Level == level, $"{label}: descriptor level {d.Level} != expected {level}");
                Check(d.Otz == depth, $"{label}: otz {d.Otz} != {depth}");
                Check(otzBucket == d.OtKey, $"{label}: OT bucket {otzBucket} != key {d.OtKey}");
                int expected = LeafPackets(level);   // the near leaf emits 4^level packets
                Check(prims == expected, $"{label}: near routine emitted {prims}, expected level {level} -> {expected}");
            }
            cases.Add(new { label, command, depth, d.Level, d.Otz, d.OtKey, d.DepthEligible, d.Reason, prims, otzBucket });
        }

        // --- per-corner differential: differing depths + interleaved normals ---
        {
            WriteGouraudTri(memory, 600, 300, 700, out uint vertices);
            RunNear(memory, PrimRegion);
            var mesh = RetainedAssets.Get(memory, Table, Table + 12, RetainedAssets.Family.Lit, out string reason);
            Check(mesh != null, "differential: mesh build failed: " + reason);
            var d = Describe(mesh!, vertices);
            Check(d.V0 == 0 && d.V1 == 1 && d.V2 == 2, $"differential: refs {d.V0},{d.V1},{d.V2} != 0,1,2 (interleaved gouraud normals)");
            Check(d.D0 == 600 && d.D1 == 300 && d.D2 == 700, $"differential: depths {d.D0},{d.D1},{d.D2}");
            Check(d.Otz == RetainedNear.MeanTri(600, 300, 700), $"differential: mean {d.Otz}");
            CountPackets(memory, 0x34, out int prims, out int bucket);
            Check(prims == LeafPackets(d.Level), $"differential: {prims} packets for level {d.Level}");
            Check(bucket == d.OtKey, $"differential: OT bucket {bucket} != {d.OtKey}");
            cases.Add(new { label = "differential-gouraud-tri", d.V0, d.V1, d.V2, d.D0, d.D1, d.D2, d.Otz, d.Level, prims, bucket });
        }

        // --- buffer-full gate: a second face must not be assembled -----------
        {
            WriteModel(memory, [(0x24, 600), (0x24, 300)], out _, out _);
            RunNear(memory, 0x1F);   // one 0x20-byte packet and not a byte more
            var mesh = RetainedAssets.Get(memory, Table, Table + 12, RetainedAssets.Family.Lit, out string reason);
            Check(mesh != null, "buffer-full: mesh build failed: " + reason);
            Check(mesh!.Faces.Length == 2, "buffer-full: expected two faces");
            CountPackets(memory, 0x24, out int prims, out int firstBucket);
            int secondBucket = FindBucket(memory, 300);
            Check(prims == 4, $"buffer-full: assembled {prims} packets, expected the gate to stop after the first four-packet face");
            Check(firstBucket == 600, $"buffer-full: first face linked bucket {firstBucket}, expected 600");
            Check(secondBucket < 0, "buffer-full: second face was assembled past the buffer end");
            cases.Add(new { label = "buffer-full-gate", prims, firstBucket, secondBucket });
        }

        // --- signed arithmetic the recompiled projection cannot produce ------
        Check(RetainedNear.MeanTri(-3, -3, -3) == -3, "mean tri (neg)");
        Check(RetainedNear.MeanTri(-1, -1, -2) == -1, "mean tri truncates toward zero");
        Check(RetainedNear.MeanTri(1, 1, 2) == 1, "mean tri remainder");
        Check(RetainedNear.MeanQuad(-4, -3, -2, -1) == -3, "mean quad signed shift");
        Check(RetainedNear.ClampOtz(7951) == 7951, "otz at the ceiling");
        Check(RetainedNear.ClampOtz(7952) == 7951, "otz above the ceiling");
        Check(RetainedNear.ClampOtz(8000) == 7951, "otz clamped");
        Check(RetainedNear.ClampOtz(-1) == -1, "otz negative survives the u16 round trip");
        Check(RetainedNear.ClampOtz(65535) == -1, "otz wraps through the u16 store");
        Check(RetainedNear.LevelOf(0) == 3 && RetainedNear.LevelOf(1) == 2 && RetainedNear.LevelOf(512) == 2, "level lower band");
        Check(RetainedNear.LevelOf(513) == 1 && RetainedNear.LevelOf(1023) == 1 && RetainedNear.LevelOf(4096) == 1, "level upper band");
        Check(RetainedNear.LevelOf(-3) == 3, "level negative");
        Check(RetainedNear.Depth(0, 0, 4000, 0, 0, 4096, 0) == 1000, "depth 1.12 rotation");
        Check(RetainedNear.Depth(0, 0, 0, 0, 0, 4096, 0) == 0, "depth clamps zero");
        Check(RetainedNear.Depth(0, 0, 0, 0, 0, 4096, 0x2000) == 2048, "depth translation");
        Check(RetainedNear.Sz2(-5) == 0 && RetainedNear.Sz2(0x10001) == 0x3FFF, "sz saturation");

        File.WriteAllText(Path.Combine(output, "near-descriptor-cases.json"),
            JsonSerializer.Serialize(cases, new JsonSerializerOptions { WriteIndented = true }));
        return assertions;
    }

    /// <summary>A leaf emits four packets (four EmitTriMap/EmitQuadMap calls),
    /// and each subdivision multiplies by four, so a face emits 4^level.</summary>
    static int LeafPackets(int level) => 1 << (2 * level);

    static void CountPackets(PSMemory memory, byte command, out int prims, out int firstBucket)
    {
        uint cursor = memory.ReadU32(0x1F800014);
        uint size = command == 0x24 ? 0x20u : 0x28u;   // EmitTriMap 0x20, EmitQuadMap/EmitTriModels 0x28
        prims = cursor <= Prim ? 0 : (int)((cursor - Prim) / size);
        firstBucket = -1;
        for (uint k = 0; k < OtRegion; k += 4)
            if (memory.ReadU32(OtBase + k) != 0) { firstBucket = (int)(k / 4); break; }
    }

    static int FindBucket(PSMemory memory, int otz)
    {
        uint at = OtBase + (uint)otz * 4;
        return memory.ReadU32(at) != 0 ? otz : -1;
    }

    static void RunNear(PSMemory memory, int primBytes)
    {
        // RTPS controls: identity 1.12 rotation, no translation, the game's H/OFX/OFY.
        Gte.WriteControl(0, 0x00001000u);
        Gte.WriteControl(1, 0u);
        Gte.WriteControl(2, 0x00001000u);
        Gte.WriteControl(3, 0x00000000u);
        Gte.WriteControl(4, 0x00001000u);
        Gte.WriteControl(5, 0u);
        Gte.WriteControl(6, 0u);
        Gte.WriteControl(7, 0u);
        Gte.WriteControl(24, (uint)(Ofx << 16));
        Gte.WriteControl(25, (uint)(Ofy << 16));
        Gte.WriteControl(26, (uint)H);

        memory.WriteU32(0x1F800010, Table);
        memory.WriteU32(0x1F800044, Cache);
        memory.WriteU32(0x1F80004C, Vertices);
        memory.WriteU32(0x1F800054, 0x808080u);
        memory.WriteU32(0x1F800008, OtBase);
        memory.WriteU32(0x1F800014, Prim);
        memory.WriteU32(0x1F800018, Prim + (uint)primBytes);
        memory.WriteU16(0x1F800066, 0);        // near limit 0 so depth alone decides
        memory.WriteU32(0x1F800068, 0u);
        memory.WriteU32(0x1F80006C, 0u);
        memory.WriteU32(0x1F800070, 0u);
        memory.WriteU32(0x1F800084, 0u);
        memory.WriteU32(0x801AEC7C, 0u);
        memory.WriteU32(0x801AEC80, 32000u);
        for (uint k = 0; k < PrimRegion; k += 4) memory.WriteU32(Prim + k, 0u);
        for (uint k = 0; k < OtRegion; k += 4) memory.WriteU32(OtBase + k, 0u);

        var cpu = new CpuContext { A0 = 0u, SP = 0x801F8000u };
        Game.func_8003AB04(cpu, memory);
    }

    static void WriteGouraudTri(PSMemory memory, int d0, int d1, int d2, out uint vertices)
    {
        vertices = Vertices;
        int[] x = [0, d1, 0], y = [0, 0, d2], z = [4 * d0, 4 * d1, 4 * d2];
        for (int k = 0; k < 3; k++)
        {
            memory.WriteU16(Vertices + (uint)k * 8, (ushort)(short)x[k]);
            memory.WriteU16(Vertices + (uint)k * 8 + 2, (ushort)(short)y[k]);
            memory.WriteU16(Vertices + (uint)k * 8 + 4, (ushort)(short)z[k]);
            memory.WriteU16(Vertices + (uint)k * 8 + 6, 0);
            memory.WriteU16(Normals + 0x100u + (uint)k * 8, 0);
            memory.WriteU16(Normals + 0x100u + (uint)k * 8 + 2, 0);
            memory.WriteU16(Normals + 0x100u + (uint)k * 8 + 4, (ushort)(4096 + k * 16));
            memory.WriteU16(Normals + 0x100u + (uint)k * 8 + 6, 0);
        }
        memory.WriteU32(FaceBase, 0x34u << 24 | 24u << 6);
        uint body = FaceBase + 4;
        memory.WriteU16(body + 2, 0);
        memory.WriteU16(body + 6, 0x0060);
        for (uint k = 0; k < 3; k++) memory.WriteU16(body + k * 4, (ushort)(k + 1));
        memory.WriteU16(body + 0xC, 0x100); memory.WriteU16(body + 0x10, 0x108); memory.WriteU16(body + 0x14, 0x110);
        memory.WriteU16(body + 0xE, 0); memory.WriteU16(body + 0x12, 8); memory.WriteU16(body + 0x16, 16);
        memory.WriteU32(Table + 0, 0u);
        memory.WriteU32(Table + 4, 0u);
        memory.WriteU32(Table + 8, 0u);
        memory.WriteU32(Table + 12, Vertices - Table - 12);
        memory.WriteU32(Table + 16, 3u);
        memory.WriteU32(Table + 20, Normals - Table - 12);
        memory.WriteU32(Table + 24, 0u);
        memory.WriteU32(Table + 28, FaceBase - Table - 12);
        memory.WriteU32(Table + 32, 1u);
    }

    /// <summary>Build a synthetic one-model map table: n vertices per face, one
    /// face per entry, placed so every projected triangle is counter-clockwise
    /// (NCLIP positive) and inside the 320x240 entry-test window.</summary>
    static void WriteModel(PSMemory memory, (byte Command, int Depth)[] faces, out uint vertices, out uint faceCount)
    {
        vertices = Vertices;
        faceCount = (uint)faces.Length;
        int vertexCount = 0;
        uint at = FaceBase;
        foreach (var (command, depth) in faces)
        {
            int n = (command & 8) != 0 ? 4 : 3;
            bool gouraud = (command & 0x10) != 0;
            int baseIndex = vertexCount;
            for (int k = 0; k < n; k++)
            {
                int x = k is 1 or 3 ? depth : 0;
                int y = k >= 2 ? depth : 0;
                int z = 4 * depth;
                if (depth == 0) { x = k is 1 or 3 ? 1 : 0; y = k >= 2 ? 1 : 0; z = 2; }
                memory.WriteU16(Vertices + (uint)vertexCount * 8, (ushort)(short)x);
                memory.WriteU16(Vertices + (uint)vertexCount * 8 + 2, (ushort)(short)y);
                memory.WriteU16(Vertices + (uint)vertexCount * 8 + 4, (ushort)(short)z);
                memory.WriteU16(Vertices + (uint)vertexCount * 8 + 6, 0);
                memory.WriteU16(Normals + (uint)vertexCount * 8, 0);
                memory.WriteU16(Normals + (uint)vertexCount * 8 + 2, 0);
                memory.WriteU16(Normals + (uint)vertexCount * 8 + 4, 4096);
                memory.WriteU16(Normals + (uint)vertexCount * 8 + 6, 0);
                vertexCount++;
            }
            uint vertexBase = (uint)(n == 4 ? 18 : 14), normalBase = vertexBase - 2;
            uint step = gouraud ? 4u : 2u;
            uint bodyBytes = (vertexBase + (uint)(n - 1) * step + 2 + 3) & ~3u;
            memory.WriteU32(at, (uint)command << 24 | bodyBytes << 6);
            uint body = at + 4;
            memory.WriteU16(body + 2, 0);          // CLUT
            memory.WriteU16(body + 6, 0x0060);     // page, the blend-mode bits the parser reads
            for (uint k = 0; k < (uint)n; k++)
            {
                memory.WriteU16(body + k * 4, (ushort)(((k + 1) & 0xFF) | (((k + 2) & 0xFF) << 8)));
                memory.WriteU16(body + vertexBase + k * step, (ushort)((uint)(baseIndex + (int)k) * 8));
                if (gouraud || k == 0) memory.WriteU16(body + normalBase + (gouraud ? k * step : 0), (ushort)((uint)(baseIndex + (int)k) * 8));
            }
            at = body + bodyBytes;
        }
        memory.WriteU32(Table + 0, 0u);
        memory.WriteU32(Table + 4, 0u);
        memory.WriteU32(Table + 8, 0u);
        memory.WriteU32(Table + 12, Vertices - Table - 12);   // +0 vertices offset
        memory.WriteU32(Table + 16, (uint)vertexCount);        // +4 vertex count
        memory.WriteU32(Table + 20, Normals - Table - 12);     // +8 normals offset
        memory.WriteU32(Table + 24, 0u);                       // +0xC unused
        memory.WriteU32(Table + 28, FaceBase - Table - 12);    // +0x10 faces offset
        memory.WriteU32(Table + 32, (uint)faces.Length);       // +0x14 face count
    }
}
