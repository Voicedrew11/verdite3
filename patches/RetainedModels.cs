using RecompOne.Runtime;
using RecompOne.Runtime.Memory;

namespace Kf3;

/// <summary>Final mesh/pose instances, submitted before any legacy vertex loop.</summary>
public static class RetainedModels
{
    const uint Pad = 0x1F800000;
    public static bool Submit(PSMemory m, uint sub, int bias, uint flags, bool perspective,
        uint caller, uint routine = 0x8003E34C, bool sky = false)
    {
        if (!GpuWorld.Capture) return false;
        GpuWorld.Submissions++;
        string? reason = GpuWorld.Domain is "hud" or "preview" ? "packet-owned-presentation"
            : !perspective ? "orthographic-policy-pending"
            : (flags & 0x40) != 0 ? "near-subdivision-pending" : null;
        if (reason != null) { GpuWorld.Fallback(routine, caller, reason); MoPose.Materialize(m); return false; }
        uint table = m.ReadU32(Pad + 0x10), header = table + 12 + (sub & 0xFFFF) * 28;
        var mesh = RetainedAssets.Get(m, table, header, sky ? RetainedAssets.Family.Sky : RetainedAssets.Family.Lit, out reason!);
        if (mesh == null) { GpuWorld.Fallback(routine, caller, reason!); MoPose.Materialize(m); return false; }
        uint count = m.ReadU32(header + 4), vertices = m.ReadU32(Pad + 0x4C);
        if (count == 0 || count > 8192 || mesh.MaxVertex >= count)
        { GpuWorld.Fallback(routine, caller, "vertex-range"); MoPose.Materialize(m); return false; }
        int weight = 0;
        int pose = MoPose.Pending ? MoPose.Store(m, count, out weight) : RetainedAssets.StoreRigid(m, vertices, (int)count);
        bool morph = MoPose.Pending && pose != 0;
        if (pose == 0) { GpuWorld.Fallback(routine, caller, "pose-not-expressible"); MoPose.Materialize(m); return false; }
        bool arm = GpuWorld.Domain == "arm", forced = (flags & 4) != 0;
        var instance = new RetainedScene.ModelInstance
        {
            MeshStart = mesh.Start, MeshCount = forced ? 0 : mesh.Opaque, MeshAll = mesh.Total,
            Pose = pose, PoseWeight = weight, PoseMorph = morph,
            Dqa = m.ReadU32(0x801AEC7C), Dqb = m.ReadU32(0x801AEC80), Curve = sky ? 0 : LinearDepthCue.Curve,
            Far = sky ? 1e30f : 8192 - bias, Near = sky ? -1e30f : -bias,
            Rgbc = m.ReadU32(Pad + 0x64) & 0xFFFFFF, Sky = sky, ViewSpace = arm,
            TwinMode = forced ? (int)(flags & 3) + 1 : 0,
        };
        ReadMatrix(ref instance);
        Place(ref instance, RetainedScene.Find(RetainedScene.Serial)!.View);
        ReadLight(ref instance);
        if (sky)
        {
            var faces = mesh.Faces.Where(f => f.Corners != 0).Select(f => (f.Corner, f.Corners, bias, f.Semi ? (int)(flags & 3) : -1)).ToArray();
            RetainedScene.AddSky(instance, faces);
        }
        else if (arm)
        {
            if (mesh.Total != mesh.Opaque) { GpuWorld.Fallback(routine, caller, "arm-blend-policy-pending"); MoPose.Materialize(m); return false; }
            Arm(mesh, instance, bias);
        }
        else
        {
            RetainedScene.AddInstance(instance);
            foreach (var face in mesh.Faces)
            {
                if (face.Corners == 0) continue;
                if (!forced && !face.Semi) continue;
                int key = Key(face, instance, bias);
                if ((uint)key >= 8192) continue;
                // Bounds are conservative; all faces retain table ordering. The
                // backend can narrow intersections without changing the contract.
                RetainedScene.AddBlendFace(key, face.Corner, face.Corners, forced ? (int)(flags & 3) : face.Mode,
                    -1024, -1024, 1023, 1023);
            }
        }
        GpuWorld.Retained++;
        if (!GpuWorld.Drawing) return false;
        MoPose.Consume();
        // Preserve the assembler's published header/normal/cursor and examined-face
        // counter; its legacy packet count stays a count of actual packets.
        m.WriteU32(Pad + 0x24, header);
        m.WriteU32(Pad + 0x28, table + 12 + m.ReadU32(header + 8));
        m.WriteU32(Pad + 0x20, table + 12 + m.ReadU32(header + 16) + mesh.FaceBytes);
        m.WriteU32(Pad + 0x78, m.ReadU32(Pad + 0x78) + m.ReadU32(header + 20));
        return true;
    }
    static void ReadMatrix(ref RetainedScene.ModelInstance m)
    {
        uint a = Gte.ReadControl(0), b = Gte.ReadControl(1), c = Gte.ReadControl(2), d = Gte.ReadControl(3);
        m.V00 = (short)a; m.V01 = (short)(a >> 16); m.V02 = (short)b;
        m.V10 = (short)(b >> 16); m.V11 = (short)c; m.V12 = (short)(c >> 16);
        m.V20 = (short)d; m.V21 = (short)(d >> 16); m.V22 = (short)Gte.ReadControl(4);
        m.Vtx = (int)Gte.ReadControl(5); m.Vty = (int)Gte.ReadControl(6); m.Vtz = (int)Gte.ReadControl(7);
    }
    static void Place(ref RetainedScene.ModelInstance m, in RetainedScene.View v)
    {
        m.R00 = (v.R00 * m.V00 + v.R10 * m.V10 + v.R20 * m.V20) / 4096;
        m.R01 = (v.R00 * m.V01 + v.R10 * m.V11 + v.R20 * m.V21) / 4096;
        m.R02 = (v.R00 * m.V02 + v.R10 * m.V12 + v.R20 * m.V22) / 4096;
        m.R10 = (v.R01 * m.V00 + v.R11 * m.V10 + v.R21 * m.V20) / 4096;
        m.R11 = (v.R01 * m.V01 + v.R11 * m.V11 + v.R21 * m.V21) / 4096;
        m.R12 = (v.R01 * m.V02 + v.R11 * m.V12 + v.R21 * m.V22) / 4096;
        m.R20 = (v.R02 * m.V00 + v.R12 * m.V10 + v.R22 * m.V20) / 4096;
        m.R21 = (v.R02 * m.V01 + v.R12 * m.V11 + v.R22 * m.V21) / 4096;
        m.R22 = (v.R02 * m.V02 + v.R12 * m.V12 + v.R22 * m.V22) / 4096;
        double x = m.Vtx - v.Tx, y = m.Vty - v.Ty, z = m.Vtz - v.Tz;
        m.Tx = (float)(v.R00 * x + v.R10 * y + v.R20 * z + v.CamX);
        m.Ty = (float)(v.R01 * x + v.R11 * y + v.R21 * z + v.CamY);
        m.Tz = (float)(v.R02 * x + v.R12 * y + v.R22 * z + v.CamZ);
    }
    static void ReadLight(ref RetainedScene.ModelInstance m)
    {
        uint a = Gte.ReadControl(8), b = Gte.ReadControl(9), c = Gte.ReadControl(10), d = Gte.ReadControl(11);
        m.Llm0 = (short)a / 4096f; m.Llm1 = (short)(a >> 16) / 4096f; m.Llm2 = (short)b / 4096f;
        m.Llm3 = (short)(b >> 16) / 4096f; m.Llm4 = (short)c / 4096f; m.Llm5 = (short)(c >> 16) / 4096f;
        m.Llm6 = (short)d / 4096f; m.Llm7 = (short)(d >> 16) / 4096f; m.Llm8 = (short)Gte.ReadControl(12) / 4096f;
        m.Bk0 = (int)Gte.ReadControl(13); m.Bk1 = (int)Gte.ReadControl(14); m.Bk2 = (int)Gte.ReadControl(15);
        a = Gte.ReadControl(16); b = Gte.ReadControl(17); c = Gte.ReadControl(18); d = Gte.ReadControl(19);
        m.L0 = (short)a; m.L1 = (short)(a >> 16); m.L2 = (short)b; m.L3 = (short)(b >> 16);
        m.L4 = (short)c; m.L5 = (short)(c >> 16); m.L6 = (short)d; m.L7 = (short)(d >> 16); m.L8 = (short)Gte.ReadControl(20);
    }
    static int Key(in RetainedAssets.Face f, in RetainedScene.ModelInstance m, int bias)
    {
        var corner = RetainedScene.MeshCorners[f.Corner];
        int sum = Depth((int)corner.Dqa, m) + Depth((int)corner.Dqb, m) + Depth((int)corner.Curve, m);
        int depth = f.Corners == 6 ? (sum + Depth((int)corner.Rgbc, m)) >> 2 : sum / 3;
        return depth <= 0 ? -1 : depth + bias;
    }
    static int Depth(int vertex, in RetainedScene.ModelInstance m)
    {
        GpuWorld.OrderVertices++;
        int at = (m.Pose - 1 + vertex * (m.PoseMorph ? 2 : 1)) * 4;
        var store = RetainedScene.PoseStore;
        int x = store[at], y = store[at + 1], z = store[at + 2];
        if (m.PoseMorph)
        {
            x = (short)(x + (short)(store[at + 4] * m.PoseWeight >> 12));
            y = (short)(y + (short)(store[at + 5] * m.PoseWeight >> 12));
            z = (short)(z + (short)(store[at + 6] * m.PoseWeight >> 12));
        }
        long mac = (long)m.V20 * x + (long)m.V21 * y + (long)m.V22 * z;
        return Math.Clamp((int)(mac >> 12) + m.Vtz, 0, 65535) >> 2;
    }
    static void Arm(RetainedAssets.Mesh mesh, in RetainedScene.ModelInstance instance, int bias)
    {
        var faces = new List<(int Key, int Seq, RetainedAssets.Face Face)>();
        for (int i = 0; i < mesh.Faces.Length; i++)
        {
            var face = mesh.Faces[i];
            if (face.Corners == 0) continue;
            int key = Key(face, instance, bias);
            if ((uint)key < 8192) faces.Add((key, i, face));
        }
        faces.Sort((a, b) => a.Key == b.Key ? b.Seq.CompareTo(a.Seq) : b.Key.CompareTo(a.Key));
        var order = new List<int>(); var keys = new List<int>(); var starts = new List<int>();
        foreach (var f in faces)
        {
            if (keys.Count == 0 || keys[^1] != f.Key) { keys.Add(f.Key); starts.Add(order.Count); }
            for (int i = 0; i < f.Face.Corners; i++) order.Add(f.Face.Corner + i);
        }
        if (order.Count > 0) RetainedScene.SetArm(instance, order.ToArray(), keys.ToArray(), starts.ToArray(), [-1024, -1024, 1023, 1023]);
    }
}
