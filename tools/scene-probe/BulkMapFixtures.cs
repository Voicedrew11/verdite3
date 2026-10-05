using System.Text.Json;
using Kf3;
using RecompOne.Runtime;
using RecompOne.Runtime.Context;
using RecompOne.Runtime.Memory;
using Game = Recompiled.KingsField3_game;

namespace SceneProbe;

/// <summary>Retained bulk-map corners against the recompiled assembler's packets.</summary>
public static class BulkMapFixtures
{
    const uint Table = 0x80010000, Vertices = 0x80011000, Normals = 0x80012000, Face = 0x80013000;
    const uint Cache = 0x80020000, Prim = 0x80030000, Ot = 0x80080000, Map = 0x801D4464, Pad = 0x1F800000;

    public static void Run(Action<bool, string> check, string output)
    {
        var m = new PSMemory();
        for (uint i = 0; i < 64000; i += 4) m.WriteU32(Map + i, uint.MaxValue);
        m.WriteU32(0x801A929C, Table);
        m.WriteU32(Table + 12, Vertices - Table - 12); m.WriteU32(Table + 16, 4);
        m.WriteU32(Table + 20, Normals - Table - 12);
        m.WriteU32(Table + 28, Face - Table - 12); m.WriteU32(Table + 32, 1);
        for (uint k = 0; k < 4; k++)
        {
            m.WriteU16(Vertices + k * 8, (ushort)((k & 1) * 600));
            m.WriteU16(Vertices + k * 8 + 2, (ushort)((k >> 1) * 600));
            m.WriteU16(Vertices + k * 8 + 4, 2400);
            m.WriteU16(Normals + k * 8, (ushort)(k * 100));
            m.WriteU16(Normals + k * 8 + 2, (ushort)(k * 200));
            m.WriteU16(Normals + k * 8 + 4, 4096);
        }
        var reports = new List<object>();
        foreach (byte command in new byte[] { 0x34, 0x36, 0x24, 0x26, 0x2C, 0x2E, 0x3C, 0x3E })
        {
            bool quad = (command & 8) != 0, gouraud = (command & 16) != 0;
            int n = quad ? 4 : 3, vertexBase = quad ? 18 : 14, normalBase = vertexBase - 2;
            int step = gouraud ? 4 : 2;
            for (uint i = 0; i < 32; i += 4) m.WriteU32(Face + 4 + i, 0);
            m.WriteU32(Face, (uint)command << 24 | 32u << 6);
            for (uint k = 0; k < n; k++)
            {
                m.WriteU16(Face + 4 + k * 4, (ushort)(0x101 * (k + 1)));
                m.WriteU16(Face + 4 + (uint)(vertexBase + k * step), (ushort)(k * 8));
                if (gouraud || k == 0)
                    m.WriteU16(Face + 4 + (uint)(normalBase + (gouraud ? k * step : 0)), (ushort)(k * 8));
            }
            if ((command & 0xFD) == 0x34)
            {
                m.WriteU16(Face + 4 + 0x12, 16);
                m.WriteU16(Face + 4 + 0x16, 24);
            }
            m.WriteU16(Face + 10, 0x60);
            // For GT3, +0x10 is normal 1 and bulk vertex 1. Model/near vertex 1
            // is +0x12, and vertex 2 is +0x16: deliberately different here.
            SeedAssembler(m);
            Game.func_80039D50(new CpuContext { A0 = 0, SP = 0x801F8000 }, m);
            uint bytes = m.ReadU32(Pad + 0x14) - Prim;
            bool skipped = (command & 0xFD) == 0x3C;
            check(bytes == (skipped ? 0u : quad ? 0x34u : 0x28u), $"bulk {command:X2}: recompiled packet size {bytes}");
            uint[] packet = Enumerable.Range(0, skipped ? 0 : n).Select(k => m.ReadU32(Prim + 8 + (uint)k * 12)).ToArray();
            for (byte rotation = 0; rotation < 4; rotation++)
            {
                m.WriteU8(Map, 0); m.WriteU8(Map + 1, 0); m.WriteU8(Map + 2, rotation); m.WriteU8(Map + 4, 0);
                RetainedMap.Invalidate(); RetainedMap.Update(m);
                var corners = RetainedScene.Static.ToArray();
                check(corners.Length == (skipped ? 0 : quad ? 6 : 3), $"bulk {command:X2}/{rotation}: retained corner count");
                if (skipped) continue;
                int[] order = quad ? [0, 1, 2, 1, 3, 2] : [0, 1, 2];
                for (int j = 0; j < corners.Length; j++)
                {
                    var v = corners[j]; int px = (int)v.X - 1024, pz = (int)v.Z - 1024;
                    (px, pz) = rotation switch { 1 => (-pz, px), 2 => (-px, -pz), 3 => (pz, -px), _ => (px, pz) };
                    Gte.Write(0, (uint)(ushort)px | (uint)(ushort)(int)v.Y << 16);
                    Gte.Write(1, (uint)(ushort)pz); Gte.Rtps(12, false);
                    check(Gte.Read(14) == packet[order[j]], $"bulk {command:X2}/{rotation}: retained corner {j} differs from recompiled packet");
                    check(v.U == (order[j] + 1) && v.V == (order[j] + 1), $"bulk {command:X2}/{rotation}: UV corner {j}");
                    int normal = gouraud ? order[j] : 0;
                    check(v.R == normal * 100 && v.G == normal * 200 && v.B == 4096, $"bulk {command:X2}/{rotation}: normal corner {j}");
                }
            }
            var model = RetainedAssets.Get(m, Table, Table + 12, RetainedAssets.Family.Lit, out string reason);
            check(model != null, $"bulk {command:X2}: model layout unavailable: {reason}");
            if ((command & 0xFD) == 0x34)
            {
                var refs = Enumerable.Range(0, 3).Select(j => RetainedScene.MeshCorners[model!.Faces[0].Corner + j].X).ToArray();
                check(refs.SequenceEqual(new float[] { 0, 2, 3 }), "bulk cache changed model/near GT3 vertex refs");
                // The bulk packet's middle vertex was the normal reference at +0x10.
                check(packet[1] == m.ReadU32(Cache + 8) && packet[2] == m.ReadU32(Cache + 16), "GT3 fixture did not distinguish overlapping bulk vertex/normal refs");
            }
            reports.Add(new { command, bytes, packet, rotations = 4 });
        }
        RetainedMap.Invalidate();
        File.WriteAllText(Path.Combine(output, "bulk-map-cases.json"), JsonSerializer.Serialize(reports));
        Console.WriteLine("Bulk map fixtures: 8 recompiled packet cases, 4 retained rotations each");
    }

    static void SeedAssembler(PSMemory m)
    {
        for (int r = 0; r < 32; r++) Gte.WriteControl(r, 0);
        Gte.WriteControl(0, 4096); Gte.WriteControl(2, 4096); Gte.WriteControl(4, 4096);
        Gte.WriteControl(24, 160u << 16); Gte.WriteControl(25, 120u << 16); Gte.WriteControl(26, 256);
        m.WriteU32(Pad + 8, Ot); m.WriteU32(Pad + 0x10, Table);
        m.WriteU32(Pad + 0x14, Prim); m.WriteU32(Pad + 0x18, Prim + 0x4000);
        m.WriteU32(Pad + 0x44, Cache); m.WriteU32(Pad + 0x4C, Vertices);
        m.WriteU32(Pad + 0x54, 0x808080); m.WriteU16(Pad + 0x66, 0);
        m.WriteU32(0x801AEC7C, 32000); m.WriteU32(0x801AEC80, 32000);
    }
}
