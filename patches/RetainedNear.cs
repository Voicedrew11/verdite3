using RecompOne.Runtime;
using RecompOne.Runtime.Memory;

namespace Kf3;

/// <summary>Read-only near-map depth descriptors; KF3_GPU_NEAR_PROBE=1 enables
/// collection before packet fallback. Facing and screen/subdivision culls remain packet-owned.</summary>
public static class RetainedNear
{
    const uint TablePointer = 0x801A929C;      // map model table (*0x1F800010 source)
    const uint NearLimitPad = 0x1F800066u;     // the walk's per-face near limit (100)

    /// <summary>Off unless KF3_GPU_NEAR_PROBE=1.</summary>
    public static bool Enabled { get; set; } = EnvOn();

    static bool EnvOn() => Environment.GetEnvironmentVariable("KF3_GPU_NEAR_PROBE") == "1";

    // Cumulative; the 5 s report resets its own window only.
    public static long Calls, Faces, DepthEligible, Dropped, ReferenceSkip;
    public static readonly long[] ByCommand = new long[256];
    public static readonly long[] ByLevel = new long[8];
    public static readonly Dictionary<string, long> Rejects = new();

    static double _windowStart = Environment.TickCount64 / 1000.0;

    /// <summary>One original source face. V0..V3 source vertex indices (only
    /// <see cref="Corners"/> used), D0..D3 their exact GTE SZ&gt;&gt;2 depths.
    /// <see cref="DepthEligible"/> means only that the descriptor's depth gates
    /// pass; NCLIP, screen-box and partial-subdivision culls are not modelled.</summary>
    public readonly record struct FaceDescriptor(
        byte Command, int Type, int Corners, int Level, int Otz, int OtKey,
        int V0, int V1, int V2, int V3, int D0, int D1, int D2, int D3,
        bool DepthEligible, string Reason);

    // ---- exact arithmetic, transcribed from func_8003AB04 --------------------

    /// <summary>SatSZ(mac3) &gt;&gt; 2, the near pre-pass's cache otz.</summary>
    public static int Sz2(int mac3)
    {
        int sz = mac3 < 0 ? 0 : mac3 > 0xFFFF ? 0xFFFF : mac3;
        return sz >> 2;
    }

    /// <summary>RTPS's MAC3 &gt;&gt; 12 for a source vertex, from rotation
    /// (controls 3, 4) and translation (control 7).</summary>
    public static int Depth(int x, int y, int z, int r20, int r21, int r22, int trz)
    {
        long m3 = ((long)trz << 12) + (long)r20 * x + (long)r21 * y + (long)r22 * z;
        return Sz2(unchecked((int)(m3 >> 12)));
    }

    public static int Depth(int x, int y, int z) =>
        Depth(x, y, z, (short)Gte.ReadControl(3), (short)(Gte.ReadControl(3) >> 16),
            (short)Gte.ReadControl(4), (int)Gte.ReadControl(7));

    /// <summary>Tri mean: sum * 0x55555556, high word, less the sign (truncating /3).</summary>
    public static int MeanTri(int d0, int d1, int d2)
    {
        int sum = d0 + d1 + d2;
        long product = (long)sum * 0x55555556;
        int hi = (int)(product >> 32);
        return hi - (sum >> 31);
    }

    /// <summary>Quad mean: the four otz summed and shifted right two.</summary>
    public static int MeanQuad(int d0, int d1, int d2, int d3) => (d0 + d1 + d2 + d3) >> 2;

    /// <summary>The routine stores the mean u16, re-reads it sign-extended, and
    /// clamps the upper bound to 0x1F0F (7951).</summary>
    public static int ClampOtz(int mean)
    {
        int stored = (short)(ushort)mean;
        return stored >= 7952 ? 7951 : stored;
    }

    /// <summary>1 at otz &gt;= 1024, else ((0x400 - otz) &gt;&gt; 9) + 1.</summary>
    public static int LevelOf(int otz) => otz >= 1024 ? 1 : ((0x400 - otz) >> 9) + 1;

    /// <summary>OT bucket key: otz; the emitter links at OTbase + (otz &lt;&lt; 2).</summary>
    public static int OtKeyOf(int otz) => otz;

    /// <summary>Classify one original face of a cached source mesh. Reads only;
    /// source vertices follow the header's vertex array.</summary>
    public static FaceDescriptor Describe(PSMemory m, RetainedAssets.Mesh mesh, int faceIndex, uint vertices, int nearLimit)
    {
        var face = mesh.Faces[faceIndex];
        int type = face.Command & 0xFD;
        if (face.Corners == 0 || type is not (0x24 or 0x2C or 0x34))
            return new(face.Command, type, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, false, "reference-command-skip");

        bool quad = face.Corners == 6;
        int n = quad ? 4 : 3;
        var store = RetainedScene.MeshCorners;
        int r20 = (short)Gte.ReadControl(3), r21 = (short)(Gte.ReadControl(3) >> 16);
        int r22 = (short)Gte.ReadControl(4), trz = (int)Gte.ReadControl(7);
        int v0 = 0, v1 = 0, v2 = 0, v3 = 0, d0 = 0, d1 = 0, d2 = 0, d3 = 0;
        for (int j = 0; j < n; j++)
        {
            int mi = quad && j == 3 ? 4 : j;
            int vi = (int)store[face.Corner + mi].X;
            uint p = vertices + (uint)vi * 8;
            if (!RetainedAssets.InRam(p, 6))
                return new(face.Command, type, n, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, false, "source-vertex-outside-ram");
            int d = Depth((short)m.ReadU16(p), (short)m.ReadU16(p + 2), (short)m.ReadU16(p + 4), r20, r21, r22, trz);
            switch (j)
            {
                case 0: v0 = vi; d0 = d; break;
                case 1: v1 = vi; d1 = d; break;
                case 2: v2 = vi; d2 = d; break;
                default: v3 = vi; d3 = d; break;
            }
        }
        int otz = ClampOtz(quad ? MeanQuad(d0, d1, d2, d3) : MeanTri(d0, d1, d2));
        int level = LevelOf(otz);
        // The near routine's own gate: every corner below the walk's near limit.
        bool nearDrop = d0 < nearLimit && d1 < nearLimit && d2 < nearLimit && (n == 3 || d3 < nearLimit);
        // The division bodies' entry test: every depth below (uint)control26 >> 1.
        int depthLimit = (int)(Gte.ReadControl(26) >> 1);
        bool depthDrop = d0 < depthLimit && d1 < depthLimit && d2 < depthLimit && (n == 3 || d3 < depthLimit);
        string reason = nearDrop ? "near-limit-all-corners" : depthDrop ? "division-depth-limit" : "";
        return new(face.Command, type, n, level, otz, OtKeyOf(otz), v0, v1, v2, v3, d0, d1, d2, d3,
            reason.Length == 0, reason);
    }

    /// <summary>Called from the near branch of <see cref="RetainedMap.Submit"/>
    /// before the attributed fallback. Never suppresses anything.</summary>
    public static void Probe(PSMemory m, RetainedAssets.Mesh mesh, uint half)
    {
        if (!Enabled) return;
        Calls++;
        uint kind = m.ReadU8(half);
        uint table = m.ReadU32(TablePointer);
        if (!RetainedAssets.InRam(table, 12)) { Reject("table-outside-ram"); return; }
        uint header = table + 12 + kind * 28;
        if (!RetainedAssets.InRam(header, 28)) { Reject("header-outside-ram"); return; }
        uint vertices = table + 12 + m.ReadU32(header);
        int nearLimit = (short)m.ReadU16(NearLimitPad);
        for (int i = 0; i < mesh.Faces.Length; i++)
        {
            var d = Describe(m, mesh, i, vertices, nearLimit);
            Faces++;
            ByCommand[d.Command]++;
            if (d.Reason == "reference-command-skip") { ReferenceSkip++; Reject(d.Reason); continue; }
            if (d.Reason == "source-vertex-outside-ram") { Reject(d.Reason); continue; }
            ByLevel[Math.Clamp(d.Level, 0, ByLevel.Length - 1)]++;
            if (!d.DepthEligible) { Dropped++; Reject(d.Reason); continue; }
            DepthEligible++;
        }
        Report();
    }

    static void Reject(string reason)
    {
        Rejects.TryGetValue(reason, out long n);
        Rejects[reason] = n + 1;
    }

    static void Report()
    {
        double now = Environment.TickCount64 / 1000.0;
        if (now - _windowStart < 5.0) return;
        _windowStart = now;
        string top = Rejects.Count == 0 ? "none" : Rejects.OrderByDescending(p => p.Value).First().Key;
        Console.WriteLine($"[KF3] near descriptors: calls {Calls} faces {Faces} depth-eligible {DepthEligible} " +
            $"dropped {Dropped} ref-skip {ReferenceSkip} tri {ByCommand[0x24]} quad {ByCommand[0x2C]} gtri {ByCommand[0x34]} " +
            $"gt4 {ByCommand[0x3C]} L1 {ByLevel[1]} L2 {ByLevel[2]} L3 {ByLevel[3]} reject {top}");
    }
}
