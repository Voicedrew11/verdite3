using System.Runtime.InteropServices;
using RecompOne.Runtime;
using RecompOne.Runtime.Memory;

namespace Kf3;

/// <summary>Content-addressed source meshes and poses; no projected vertex capture.</summary>
public static class RetainedAssets
{
    public enum Family { Lit, Sky, MapBulk }
    public sealed class Mesh
    {
        public int Start, Opaque, Total, MaxVertex;
        public uint FaceBytes, NormalBytes;
        public ulong FaceHash, NormalHash;
        public Face[] Faces = [];
        public readonly Dictionary<byte, int> IgnoredCommands = new();
    }
    public readonly record struct Face(int Corner, int Corners, int Mode, bool Semi, byte Command);
    static readonly Dictionary<(uint Table, uint Header, Family Family), Mesh> Meshes = new();
    static readonly Dictionary<(uint Address, int Count, ulong Hash), int> Rigid = new();
    static int _generation = -1;
    public static long MeshBuilds, MeshHits, MeshMutations, RigidBuilds, RigidHits;
    public static bool InRam(uint a, uint bytes) => (a & 0x1FFFFFFFu) < 0x200000u
        && bytes <= 0x200000u - (a & 0x1FFFFFFFu);
    public static ulong Hash(ReadOnlySpan<byte> ram, uint a, uint bytes)
    {
        ulong h = 14695981039346656037ul;
        foreach (byte b in ram.Slice((int)(a & 0x1FFFFF), (int)bytes)) h = (h ^ b) * 1099511628211ul;
        return h;
    }
    static void CheckGeneration()
    {
        if (_generation == RetainedScene.MeshGeneration) return;
        Meshes.Clear(); Rigid.Clear(); _generation = RetainedScene.MeshGeneration;
    }
    public static Mesh? Get(PSMemory m, uint table, uint header, Family family, out string reason)
    {
        CheckGeneration(); reason = "";
        if (!InRam(header, 28)) { reason = "mesh-header-outside-ram"; return null; }
        uint faces = table + 12 + m.ReadU32(header + 16), normals = table + 12 + m.ReadU32(header + 8);
        uint count = m.ReadU32(header + 20);
        if (count > 4096 || !InRam(faces, 4) || !InRam(normals, 8)) { reason = "mesh-range"; return null; }
        var key = (table, header, family);
        if (Meshes.TryGetValue(key, out var cached))
        {
            if (cached.Faces.Length == count && InRam(faces, cached.FaceBytes) && InRam(normals, cached.NormalBytes)
                && Hash(m.Ram, faces, cached.FaceBytes) == cached.FaceHash
                && Hash(m.Ram, normals, cached.NormalBytes) == cached.NormalHash)
            { MeshHits++; return cached; }
            MeshMutations++;
        }
        var mesh = Build(m, faces, count, normals, family, out reason);
        if (mesh != null) { Meshes[key] = mesh; MeshBuilds++; }
        return mesh;
    }
    static Mesh? Build(PSMemory m, uint source, uint count, uint normals, Family family, out string reason)
    {
        reason = "";
        var opaque = new List<RetainedScene.Vertex>(); var blended = new List<RetainedScene.Vertex>();
        var mesh = new Mesh { Faces = new Face[count], MaxVertex = -1 };
        // Each face's corners, in source order; stored below in reverse.
        var corners = new RetainedScene.Vertex[count][];
        Span<uint> vi = stackalloc uint[4], ni = stackalloc uint[4], uv = stackalloc uint[4];
        uint at = source;
        for (int i = 0; i < count; i++)
        {
            if (!InRam(at, 4)) { reason = "face-header-outside-ram"; return null; }
            uint word = m.ReadU32(at), body = at + 4, bytes = (word >> 6) & 0x3FC;
            byte command = (byte)(word >> 24), type = (byte)(command & 0xFD);
            if (!InRam(body, bytes)) { reason = "face-body-outside-ram"; return null; }
            int n; bool gouraud = true, textured = true;
            int vertexBase, normalBase, step;
            switch (type)
            {
                case 0x24 when family is Family.Lit or Family.MapBulk: n = 3; gouraud = false; vertexBase = 14; normalBase = 12; step = 2; break;
                case 0x2C when family is Family.Lit or Family.MapBulk: n = 4; gouraud = false; vertexBase = 18; normalBase = 16; step = 2; break;
                case 0x34: n = 3; vertexBase = 14; normalBase = 12; step = 4; break;
                case 0x3C when family != Family.MapBulk: n = 4; vertexBase = 18; normalBase = 16; step = 4; break;
                case 0x30 when family == Family.Sky: n = 3; textured = false; vertexBase = 6; normalBase = 4; step = 4; break;
                case 0x38 when family == Family.Sky: n = 4; textured = false; vertexBase = 6; normalBase = 4; step = 4; break;
                default:
                    // The literal family's dispatch deliberately skips this record.
                    // Raw 25/2D records occur in the corpus; they are not world draws
                    // from this assembler. Preserve that decision, with an inventory.
                    mesh.Faces[i] = new(-1, 0, 0, false, command);
                    mesh.IgnoredCommands.TryGetValue(command, out int ignored);
                    mesh.IgnoredCommands[command] = ignored + 1;
                    at = body + bytes; continue;
            }
            // Bulk map GT3 uses contiguous vertex refs but interleaved normals.
            int vertexStep = family == Family.MapBulk && type == 0x34 ? 2 : step;
            int required = Math.Max(vertexBase + (n - 1) * vertexStep, normalBase + (gouraud ? (n - 1) * step : 0)) + 2;
            if (bytes < required) { reason = "short-face-body"; return null; }
            bool semi = textured && (command & 2) != 0;
            uint tpage = textured ? m.ReadU16(body + 6) & 0x1FFu : 0x8000u;
            int mode = (int)(tpage >> 5 & 3);
            int u0 = 255, v0 = 255, u1 = 0, v1 = 0;
            for (int k = 0; k < n; k++)
            {
                uint offset = m.ReadU16(body + (uint)(vertexBase + k * vertexStep));
                if ((offset & 7) != 0) { reason = "unaligned-vertex-index"; return null; }
                vi[k] = offset / 8; mesh.MaxVertex = Math.Max(mesh.MaxVertex, (int)vi[k]);
                ni[k] = m.ReadU16(body + (uint)(normalBase + (gouraud ? k * step : 0)));
                if (!InRam(normals + ni[k], 8)) { reason = "normal-outside-ram"; return null; }
                mesh.NormalBytes = Math.Max(mesh.NormalBytes, ni[k] + 8);
                uv[k] = textured ? (uint)m.ReadU16(body + (uint)k * 4) : 0u;
                int u = (int)(uv[k] & 255), v = (int)(uv[k] >> 8);
                u0 = Math.Min(u0, u); u1 = Math.Max(u1, u); v0 = Math.Min(v0, v); v1 = Math.Max(v1, v);
            }
            var template = new RetainedScene.Vertex
            {
                Clut = textured ? m.ReadU16(body + 2) & 0x7FFFu : 0, Texpage = tpage,
                Dqa = vi[0], Dqb = vi[1], Curve = vi[2], Rgbc = n == 4 ? vi[3] : uint.MaxValue,
                Rect = (uint)(u0 | v0 << 8 | u1 << 16 | v1 << 24),
                Flags = RetainedScene.FlagDots | (textured ? RetainedScene.FlagRect : 0)
                    | (semi ? RetainedScene.FlagSemi | (uint)mode << 8 : 0),
                Light = textured ? 0 : RetainedScene.FaceColour | (m.ReadU32(body) & 0xFFFFFF),
            };
            ReadOnlySpan<int> order = n == 4 ? [0, 1, 2, 1, 3, 2] : [0, 1, 2];
            mesh.Faces[i] = new(0, order.Length, mode, semi, command);
            corners[i] = new RetainedScene.Vertex[order.Length];
            for (int j = 0; j < order.Length; j++)
            {
                int k = order[j]; var v = template; uint normal = normals + ni[k];
                v.X = vi[k]; v.R = (short)m.ReadU16(normal); v.G = (short)m.ReadU16(normal + 2); v.B = (short)m.ReadU16(normal + 4);
                v.U = uv[k] & 255; v.V = uv[k] >> 8;
                if (j >= 3) v.Flags |= RetainedScene.FlagQuadTail;
                corners[i][j] = v;
            }
            at = body + bytes;
        }
        // The assembler links each face at the head of its table slot, so of faces in
        // one slot the walk draws the last built first and the first built last, on
        // top. A coplanar pair goes to the later draw under 0051's tolerance, so the
        // store holds the faces last first: a sign's lettering (face 0 of its mesh)
        // over the plate it shares every corner with (a later face).
        for (int i = (int)count - 1; i >= 0; i--)
        {
            if (corners[i] is not { } face) continue;
            var dst = mesh.Faces[i].Semi ? blended : opaque;
            mesh.Faces[i] = mesh.Faces[i] with { Corner = dst.Count };
            dst.AddRange(face);
        }
        mesh.FaceBytes = at - source;
        mesh.FaceHash = Hash(m.Ram, source, mesh.FaceBytes);
        mesh.NormalHash = Hash(m.Ram, normals, mesh.NormalBytes);
        mesh.Opaque = opaque.Count; mesh.Total = opaque.Count + blended.Count;
        opaque.AddRange(blended);
        mesh.Start = RetainedScene.AddMesh(CollectionsMarshal.AsSpan(opaque));
        for (int i = 0; i < mesh.Faces.Length; i++)
        {
            var f = mesh.Faces[i];
            if (f.Corners == 0) continue;
            mesh.Faces[i] = f with { Corner = f.Corner + mesh.Start + (f.Semi ? mesh.Opaque : 0) };
        }
        return mesh;
    }
    public static int StoreRigid(PSMemory m, uint address, int count)
    {
        CheckGeneration();
        if (count <= 0 || count > 8192 || !InRam(address, (uint)count * 8)) return 0;
        ulong hash = Hash(m.Ram, address, (uint)count * 8);
        var key = (address, count, hash);
        if (Rigid.TryGetValue(key, out int pose)) { RigidHits++; return pose; }
        pose = RetainedScene.AddPose(MemoryMarshal.Cast<byte, short>(m.Ram.Slice((int)(address & 0x1FFFFF), count * 8))) + 1;
        Rigid[key] = pose; RigidBuilds++; return pose;
    }
}
