using RecompOne.Runtime;
using RecompOne.Runtime.Memory;

namespace Kf3;

/// <summary>Final mesh/pose instances, submitted before any legacy vertex loop.</summary>
public static class RetainedModels
{
    const uint Pad = 0x1F800000;
    /// <summary><c>KF3_GPU_CLIP_PROBE=1</c>: of the models' faces kept by depth, those
    /// with a corner the GTE cannot project, which runtime 0103 clips at the near plane
    /// (they were placed at the divide's saturated ends), taken on the submit's own GTE;
    /// the billboards (the walk's call at <c>0x800419EC</c>) apart.</summary>
    public static bool ClipProbe;
    public static long ClipFaces, Clipped, ClipBillboardFaces, ClipBillboardClipped;
    public static bool Submit(PSMemory m, uint sub, int bias, uint flags, bool perspective,
        uint caller, uint routine = 0x8003E34C, bool sky = false)
    {
        if (!GpuWorld.Capture) return false;
        GpuWorld.Submissions++;
        string? reason = GpuWorld.Domain is "hud" or "preview" ? "packet-owned-presentation"
            : !perspective ? "orthographic-policy-pending" : null;
        if (reason != null) { GpuWorld.Fallback(routine, caller, reason); MoPose.Materialize(m); return false; }
        // Capture implies a begun frame, so this is not expected; a frame that has
        // left the ring is the packets' to draw, not a null to place a model with.
        if (RetainedScene.Find(RetainedScene.Serial) is not { } frame)
        { GpuWorld.Fallback(routine, caller, "no-retained-frame"); MoPose.Materialize(m); return false; }
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
        // The submitter tests 0x40 first: such a model goes to the near path, whose
        // libgte division keeps a face reaching past the eye. A Tile instance is that
        // face clipped at the GPU's near plane, its facing taken against the eye, and
        // its depth the true one, where the near packets' corners at or behind the eye
        // had no depth and drew in painter's order over everything.
        bool arm = GpuWorld.Domain == "arm", near = (flags & 0x40) != 0, forced = !near && (flags & 4) != 0;
        // The model walk's, with the render distance past the game's edge: the ordering
        // table's end (about 16 tiles of view depth) no longer drops a face.
        bool far = !sky && !arm && RenderDistance.InWalk && RenderDistance.ModelsExtended;
        var instance = new RetainedScene.ModelInstance
        {
            MeshStart = mesh.Start, MeshCount = forced ? 0 : mesh.Opaque, MeshAll = mesh.Total,
            Pose = pose, PoseWeight = weight, PoseMorph = morph,
            Dqa = m.ReadU32(0x801AEC7C), Dqb = m.ReadU32(0x801AEC80), Curve = sky ? 0 : LinearDepthCue.Curve,
            Far = sky || far ? 1e30f : 8192 - bias, Near = sky ? -1e30f : -bias,
            Rgbc = m.ReadU32(Pad + 0x64) & 0xFFFFFF, Sky = sky, ViewSpace = arm,
            TwinMode = forced ? (int)(flags & 3) + 1 : 0, Tile = near && !sky && !arm,
            // Placed in the world, so the planar mirror draws it too (PlanarMirror).
            Mirrored = !sky && !arm,
            FadeOut = sky || arm ? 0 : RenderDistance.ModelFadeOut,
        };
        ReadMatrix(ref instance);
        Place(ref instance, frame.View);
        ReadLight(ref instance);
        if (ClipProbe && !sky && !arm && !instance.Tile) CountClips(mesh, instance, bias, caller == 0x800419ECu);
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
                if (far && key >= 8192) key = 8191;
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
    static void CountClips(RetainedAssets.Mesh mesh, in RetainedScene.ModelInstance m, int bias, bool billboard)
    {
        float h = (ushort)Gte.ReadControl(26), cx = (int)Gte.ReadControl(24) / 65536f, cy = (int)Gte.ReadControl(25) / 65536f;
        foreach (var f in mesh.Faces)
        {
            if (f.Corners == 0 || (uint)Key(f, m, bias) >= 8192) continue;
            var corner = RetainedScene.MeshCorners[f.Corner];
            int n = f.Corners == 6 ? 4 : 3;
            bool clipped = false;
            for (int k = 0; k < n; k++)
            {
                int vertex = (int)(k switch { 0 => corner.Dqa, 1 => corner.Dqb, 2 => corner.Curve, _ => corner.Rgbc });
                clipped |= !Projects(vertex, m, h, cx, cy);
            }
            if (billboard) { ClipBillboardFaces++; if (clipped) ClipBillboardClipped++; }
            else { ClipFaces++; if (clipped) Clipped++; }
        }
    }
    // modelProjects, from the GTE matrix the submit loaded: in front of H/2 and on the
    // divide's range.
    static bool Projects(int vertex, in RetainedScene.ModelInstance m, float h, float cx, float cy)
    {
        int at = (m.Pose - 1 + vertex * (m.PoseMorph ? 2 : 1)) * 4;
        var store = RetainedScene.PoseStore;
        int px = store[at], py = store[at + 1], pz = store[at + 2];
        if (m.PoseMorph)
        {
            px = (short)(px + (short)(store[at + 4] * m.PoseWeight >> 12));
            py = (short)(py + (short)(store[at + 5] * m.PoseWeight >> 12));
            pz = (short)(pz + (short)(store[at + 6] * m.PoseWeight >> 12));
        }
        float ex = (int)(((long)m.V00 * px + (long)m.V01 * py + (long)m.V02 * pz) >> 12) + m.Vtx;
        float ey = (int)(((long)m.V10 * px + (long)m.V11 * py + (long)m.V12 * pz) >> 12) + m.Vty;
        float ez = (int)(((long)m.V20 * px + (long)m.V21 * py + (long)m.V22 * pz) >> 12) + m.Vtz;
        if (ez <= h * 0.5f || ez > 32767f) return false;
        float x = cx + h * ex / ez, y = cy + h * ey / ez;
        return x >= -1024f && x <= 1023f && y >= -1024f && y <= 1023f;
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
