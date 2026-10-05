using RecompOne.Runtime;
using RecompOne.Runtime.Memory;

namespace Kf3;

public static partial class MoPose
{
    public static bool Pending { get; private set; }
    static uint _key, _stream, _weight, _count;
    public static long Deferred, Materialized, PoseBuilds, PoseHits, PoseRefused;
    readonly record struct PoseEntry(int Pose, uint Bytes, ulong Hash);
    static readonly Dictionary<(ulong Key, uint Stream, uint Count), PoseEntry> Poses = new();
    static int _poseGeneration = -1;
    static short[] _texels = [];
    static bool[] _touched = [];

    /// <summary>Only reference/fallback consumers need the guest posed buffer.</summary>
    public static void Materialize(PSMemory m)
    {
        if (!Pending) return;
        Pending = false; Materialized++;
        // A zero low-half count is 65536 in the literal copy loop.
        uint n = _count == 0 ? 65536u : _count;
        for (uint i = 0; i < n; i++)
        {
            m.WriteU32(Posed + i * 8, m.ReadU32(_key + i * 8));
            m.WriteU32(Posed + i * 8 + 4, m.ReadU32(_key + i * 8 + 4));
        }
        uint s = _stream, at = Posed;
        int entries = (short)m.ReadU16(s); s += 2;
        for (int i = 0; i < entries; i++)
        {
            ushort x = m.ReadU16(s);
            if (x == 0x8000) { at += (uint)((short)m.ReadU16(s + 2) << 3); s += 4; continue; }
            for (uint k = 0; k < 3; k++)
            {
                ushort p = m.ReadU16(at + k * 2), target = m.ReadU16(s + k * 2);
                short delta = unchecked((short)(target - p));
                int scaled = unchecked((int)((uint)delta * _weight)) >> 12;
                m.WriteU16(at + k * 2, unchecked((ushort)(p + scaled)));
            }
            s += 6; at += 8;
        }
    }
    /// <summary>The final retained key/delta representation, cached across weights.</summary>
    public static int Store(PSMemory m, uint vertices, out int weight)
    {
        weight = (int)_weight;
        if (!Pending || _count == 0 || _count != vertices || vertices > 8192
            || !RetainedAssets.InRam(_key, vertices * 8) || !RetainedAssets.InRam(_stream, 2)) return 0;
        if (_poseGeneration != RetainedScene.MeshGeneration)
        { Poses.Clear(); _poseGeneration = RetainedScene.MeshGeneration; }
        ulong hash = RetainedAssets.Hash(m.Ram, _key, vertices * 8);
        var key = (hash, _stream, vertices);
        if (Poses.TryGetValue(key, out var found) && RetainedAssets.InRam(_stream, found.Bytes)
            && RetainedAssets.Hash(m.Ram, _stream, found.Bytes) == found.Hash)
        { PoseHits++; return found.Pose; }
        int shorts = checked((int)vertices * 8);
        if (_texels.Length < shorts) _texels = new short[shorts];
        if (_touched.Length < vertices) _touched = new bool[vertices];
        Array.Clear(_touched); Array.Clear(_texels, 0, shorts);
        for (uint i = 0; i < vertices; i++)
            for (uint k = 0; k < 4; k++) _texels[i * 8 + k] = (short)m.ReadU16(_key + i * 8 + k * 2);
        uint s = _stream; int count = (short)m.ReadU16(s); s += 2;
        long at = 0;
        if (count < 0) { PoseRefused++; return 0; }
        for (int i = 0; i < count; i++)
        {
            if (!RetainedAssets.InRam(s, 4)) { PoseRefused++; return 0; }
            ushort x = m.ReadU16(s);
            if (x == 0x8000) { at += (short)m.ReadU16(s + 2); s += 4; continue; }
            if (!RetainedAssets.InRam(s, 6) || at < 0 || at >= vertices || _touched[at]) { PoseRefused++; return 0; }
            _touched[at] = true; int offset = (int)at * 8;
            for (uint k = 0; k < 3; k++) _texels[offset + 4 + k] = unchecked((short)(m.ReadU16(s + k * 2) - _texels[offset + k]));
            s += 6; at++;
        }
        int pose = RetainedScene.AddPose(_texels.AsSpan(0, shorts)) + 1;
        Poses[key] = new(pose, s - _stream, RetainedAssets.Hash(m.Ram, _stream, s - _stream));
        PoseBuilds++; return pose;
    }
    /// <summary>A GPU consumer owns the pending pose; do not decode it next time.</summary>
    public static void Consume() => Pending = false;
}
