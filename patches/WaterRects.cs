using RecompOne.Runtime;
using RecompOne.Runtime.Events;
using RecompOne.Runtime.Memory;

namespace Kf3;

/// <summary>
/// Which faces are water: the one fact the runtime's murk, ripples, swell and mirror
/// cannot know. The game's scrolling textures (<see cref="TextureScroll"/>: two records
/// of 0x18 bytes at <c>0x801AEB1C</c>, each copied into a fixed dest rect every tick)
/// are its liquids. Verdite2's <c>Reflections</c> and <c>Waves</c> read its fluid slots
/// the same way; the record's layout here is this game's own (dest at <c>+8</c>/<c>+0xA</c>,
/// size at <c>+0x14</c>/<c>+0x16</c>).
///
/// A face is water when it is blended in an averaging mode (0 or 3) and its texels lie
/// in one of the rects, as Verdite2 and the runtime's shaders take it. The additive
/// liquid of areas 1, 2, 6 and 17 samples the same rect and is left out, as Verdite2
/// leaves out its additive fire. The saved 28-area RAM corpus, read offline: one
/// record live in every area, dest (1016, 96), 8x32 halfwords; every averaging face on
/// it is a map face, and no object mesh in the area table samples it. See "Which faces
/// are water" in docs/WATER.md.
/// </summary>
public static class WaterRects
{
    const uint Table = 0x801AEB1C;
    const int Stride = 0x18, Count = 2;

    /// <summary>The rects, in VRAM halfwords: x, y, w, h.</summary>
    static readonly short[] _rects = new short[Count * 4];

    public static int N { get; private set; }

    /// <summary>Changes whenever the rects do: <see cref="RetainedMap"/> keys its chunks
    /// on it, since a chunk's corners carry which faces are water.</summary>
    public static ulong Key { get; private set; }

    public static void Install()
    {
        // Water reflects as Verdite2's does: 0.6 at a grazing angle, 0.12 looking down.
        SurfaceMaterial.Reflectivity[SurfaceMaterial.Water] = 0.6f;
        SurfaceMaterial.F0[SurfaceMaterial.Water] = 0.12f;
        SurfaceMaterial.Changed();
        Event.AddListener<OverlayLoadedEvent>(_ => { N = 0; SurfaceMaterial.RectN = 0; WaterWaves.RectN = 0; });
    }

    /// <summary>Once a frame, from GpuWorld.Begin before the map is updated: the rects,
    /// published to the surface classifier (only while the reflection pass runs, as
    /// Verdite2 publishes them) and to the ripples.</summary>
    public static void Read(IMemory m)
    {
        int n = 0;
        ulong key = 14695981039346656037ul;
        for (int i = 0; i < Count; i++)
        {
            uint rec = Table + (uint)(i * Stride);
            if (m.ReadU8(rec) != 1) continue;
            short w = (short)m.ReadU16(rec + 0x14), h = (short)m.ReadU16(rec + 0x16);
            if (w <= 0 || h <= 0) continue;
            _rects[n * 4] = (short)m.ReadU16(rec + 8);
            _rects[n * 4 + 1] = (short)m.ReadU16(rec + 0xA);
            _rects[n * 4 + 2] = w;
            _rects[n * 4 + 3] = h;
            for (int k = 0; k < 4; k++) key = (key ^ (ushort)_rects[n * 4 + k]) * 1099511628211ul;
            n++;
        }
        N = n;
        Key = key ^ (ulong)n;

        var waves = WaterWaves.Rects;
        for (int i = 0; i < n * 4; i++) waves[i] = _rects[i];
        WaterWaves.RectN = Math.Min(n, WaterWaves.MaxRects);

        if (!GteDepth.Reflections) { SurfaceMaterial.RectN = 0; return; }
        int s = 0;
        for (int i = 0; i < n && s < SurfaceMaterial.RectSlots; i++)
        {
            ref var r = ref SurfaceMaterial.Rects[s++];
            r.X = _rects[i * 4]; r.Y = _rects[i * 4 + 1]; r.W = _rects[i * 4 + 2]; r.H = _rects[i * 4 + 3];
            r.Material = SurfaceMaterial.Water;
            r.TranslucentOnly = true;
        }
        SurfaceMaterial.RectN = s;
    }

    /// <summary>Whether a face is water: blended, in an averaging mode, its texels (the
    /// UV box <paramref name="rect"/> packs as u0, v0, u1, v1 bytes) in one of the rects.</summary>
    public static bool IsWater(bool semi, uint tpage, uint rect)
    {
        if (!semi || N == 0) return false;
        uint blend = (tpage >> 5) & 3u;
        if (blend is not (0u or 3u)) return false;
        int mode = (int)(tpage >> 7) & 3;
        int div = mode == 0 ? 4 : mode == 1 ? 2 : 1;
        int bx = (int)(tpage & 0xF) * 64, by = (int)((tpage >> 4) & 1) * 256;
        int x0 = bx + (int)(rect & 0xFF) / div, x1 = bx + (int)((rect >> 16) & 0xFF) / div + 1;
        int y0 = by + (int)((rect >> 8) & 0xFF), y1 = by + (int)(rect >> 24) + 1;
        for (int i = 0; i < N; i++)
        {
            int rx = _rects[i * 4], ry = _rects[i * 4 + 1], rw = _rects[i * 4 + 2], rh = _rects[i * 4 + 3];
            if (x0 < rx + rw && x1 > rx && y0 < ry + rh && y1 > ry) return true;
        }
        return false;
    }

    public static string Describe()
    {
        var parts = new List<string>();
        for (int i = 0; i < N; i++) parts.Add($"({_rects[i * 4]},{_rects[i * 4 + 1]}) {_rects[i * 4 + 2]}x{_rects[i * 4 + 3]}");
        return N == 0 ? "no water rect" : string.Join(", ", parts);
    }
}
