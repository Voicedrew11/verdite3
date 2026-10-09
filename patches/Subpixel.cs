using System.Reflection;
using RecompOne.Runtime;
using RecompOne.Runtime.Context;
using RecompOne.Runtime.Memory;
using RecompOne.Runtime.Modding;

namespace Kf3;

/// <summary>
/// Sub-pixel vertex positioning, Verdite2's Subpixel on this disc. The fraction the
/// GTE truncates is carried by the same address map as the depth; the work is in
/// the fork and <see cref="GteDepth.Subpixel"/> switches it on. This file is the
/// switch, the fractional cull's switch and the probe. Off until judged.
///
///     KF3_SUBPIXEL=1          on; 0 or unset snaps every vertex to a whole pixel
///     KF3_SUBPIXEL_PROBE=1    a line every 2 s: the map's counters and how far
///                             the recovered vertices actually moved, in pixels
///     KF3_SUBPIXEL_CULL=0     the C# assemblers cull on whole pixels, to compare
///
/// See "Unit 2" in docs/PICTURE.md.
/// </summary>
public static class Subpixel
{
    // libgpu DrawOTag, per overlay, for the probe's frame boundary.
    static readonly (string Overlay, uint Addr)[] DrawOTag =
        [("open", 0x800166CC), ("game", 0x8007A104), ("end", 0x80014428)];

    /// <summary>False snaps every vertex to a whole pixel, as the console does. Live.</summary>
    public static bool Enabled
    {
        get => GteDepth.Subpixel;
        set { GteDepth.Subpixel = value; GteVertexMap.SetActive(GteDepth.Active); }
    }

    /// <summary>0052: cull the C# assemblers' faces at their fractional corners while
    /// sub-pixel is on. KF3_SUBPIXEL_CULL=0 turns it off to compare.</summary>
    public static bool Cull = true;

    static bool _probe;

    /// <summary>KF3_SUBPIXEL_PROBE: count the map and the moved vertices. Live.</summary>
    public static bool ProbeOn
    {
        get => _probe;
        set
        {
            if (_probe == value) return;
            _probe = value;
            GteDepth.Probe = value;
            _frames = 0;
            _windowStart = Now;
            Baseline();
        }
    }

    /// <summary>KF3_SUBPIXEL: an explicit command-line choice, which wins for the run.</summary>
    static bool? _forced;

    static long _frames;
    static double _windowStart = Now;

    // The map's counters are lifetime totals, so each window is a difference.
    static long _hits, _misses, _roots, _copied;

    static double Now => Environment.TickCount64 / 1000.0;

    internal static ModInfo Mod => _self;
    static readonly ModInfo _self = new()
    {
        Id = "kf3.subpixel",
        Name = "Sub-pixel vertex positioning",
        Version = "1.0",
        Description = "Recovers the fraction of a pixel the GTE truncates, so vertices stop snapping.",
    };

    public static void Configure(string? on, string? probe, string? cull = null)
    {
        if (cull?.Trim() == "0") Cull = false;
        if (!string.IsNullOrWhiteSpace(on)) _forced = on.Trim() is not ("0" or "off");
        ProbeOn = probe?.Trim() == "1";
    }

    public static void Install()
    {
        Enabled = _forced ?? true;
        GteDepth.Probe = _probe;
        Baseline();
        // Attached in every state, so the probe can be switched on live.
        HookAttach.OnOverlayLoad("subpixel", Attach);
    }

    static bool Attach()
    {
        SymbolRegistry.Build();
        var after = typeof(Subpixel).GetMethod(nameof(AfterDrawOTag), BindingFlags.Public | BindingFlags.Static)!;

        int n = 0;
        foreach (var (overlay, addr) in DrawOTag)
        {
            var target = SymbolRegistry.Resolve(overlay, null, addr);
            if (target == null) { Console.Error.WriteLine($"[KF3] subpixel: no function at {overlay}/0x{addr:X8}"); continue; }
            if (HookManager.AddPost(_self, target, after)) n++;
        }

        HookManager.Commit();
        Console.WriteLine($"[KF3] subpixel: {(Enabled ? "on" : "off (whole pixels)")}, probe {(_probe ? "on" : "off")}, {n}/3 function(s) hooked");
        return n == 3;
    }

    static void Baseline()
    {
        _hits = GteVertexMap.Hits;
        _misses = GteVertexMap.Misses;
        _roots = GteVertexMap.Roots;
        _copied = GteVertexMap.Propagated;
    }

    public static void AfterDrawOTag(CpuContext c, IMemory m)
    {
        if (!_probe) return;

        _frames++;
        double window = Now - _windowStart;
        if (window < 2.0) return;

        long hits = GteVertexMap.Hits - _hits, misses = GteVertexMap.Misses - _misses;
        long roots = GteVertexMap.Roots - _roots, copied = GteVertexMap.Propagated - _copied;
        long asked = hits + misses;

        Console.WriteLine($"[KF3] subpixel: {roots / window:F0} roots/s, {copied / window:F0} propagated/s, " +
                          $"{hits / window:F0} hits/s, {misses / window:F0} misses/s " +
                          $"({(asked == 0 ? 0.0 : 100.0 * hits / asked):F1}% hit), over {_frames / window:F0} frames/s");

        long n = GteDepth.OffsetCount;
        double mean = n == 0 ? 0.0 : GteDepth.Offset / n;
        Console.WriteLine($"[KF3] subpixel: {n / window:F0} vertices/s carrying a fraction, " +
                          $"mean offset {mean:F3} px, max {GteDepth.OffsetMax:F3} px, " +
                          $"cull {(Cull ? "fractional" : "whole pixels")} " +
                          $"({PolyAssembler.CullKept / window:F0} kept, {PolyAssembler.CullDropped / window:F0} dropped/s)");
        PolyAssembler.CullKept = PolyAssembler.CullDropped = 0;
        Console.WriteLine($"[KF3] subpixel: HUD {PolyAssembler.ScreenVertices / window:F0} vertices/s placed on the screen, " +
                          $"{PolyAssembler.ScreenFractional / window:F0} with a fraction, " +
                          $"{PolyAssembler.ScreenAligned / window:F0} on unturned pieces kept whole");
        PolyAssembler.ScreenVertices = PolyAssembler.ScreenFractional = PolyAssembler.ScreenAligned = 0;

        Baseline();
        GteDepth.ResetOffsets();
        _frames = 0;
        _windowStart = Now;
    }
}
