using System.Globalization;
using System.Reflection;
using RecompOne.Runtime;
using RecompOne.Runtime.Context;
using RecompOne.Runtime.Memory;
using RecompOne.Runtime.Modding;

namespace Kf3;

/// <summary>
/// The Z-buffer switch: per-pixel occlusion from the view depth the C# bulk
/// assemblers record beside each packet, instead of the console's ordering table.
/// A packet with no record keeps painter's order, so a miss is exactly the old
/// picture. Only the switch, the tolerance and the report are here; the test is
/// the fork's (<see cref="GteDepth"/>, <see cref="GtePacketDepth"/>,
/// <see cref="BlendOrder"/>) and the records are written by PolyAssemblerDepth.cs.
///
///     KF3_ZBUFFER=1             on; 0 or unset leaves the ordering table
///     KF3_ZBUFFER_PROBE=1       a line every 2 s: records, hits, tested, unmatched
///     KF3_BLENDORDER=0          blended surfaces in table order again (0079)
///     KF3_ZBUFFER_THRESHOLD=N   restart the depth buffer on an N-unit step (0051)
///
/// Off until judged. See "Unit 3" in docs/PICTURE.md.
/// </summary>
public static class ZBuffer
{
    // libgpu DrawOTag, as bound in config/kf3.json: a frame boundary to count on.
    static readonly (string Overlay, uint Addr)[] DrawOTag =
        [("open", 0x800166CC), ("game", 0x8007A104), ("end", 0x80014428)];

    /// <summary>Verdite2's coplanar tolerance (0051): SZ units of constant, then
    /// pixels of depth slope, so two coplanar surfaces go to the later table entry.</summary>
    const float DefaultBias = 1f, DefaultSlope = 0.5f;

    internal static ModInfo Mod => _self;
    static readonly ModInfo _self = new()
    {
        Id = "kf3.zbuffer",
        Name = "Z-buffer",
        Version = "1.0",
        Description = "Per-pixel occlusion from packet depth records, instead of the ordering table.",
    };

    /// <summary>Live: the next triangle starts or stops testing.</summary>
    public static bool Enabled
    {
        get => GteDepth.ZBuffer;
        set { GteDepth.ZBuffer = value; GteVertexMap.SetActive(GteDepth.Active); }
    }

    /// <summary>Live: write the report to the console, once every two seconds.</summary>
    public static bool ProbeOn { get; set; }

    /// <summary>KF3_ZBUFFER, or on when it is unset.</summary>
    static bool _forced;

    static float _threshold;

    static long _frames;
    static double _windowStart = Now;

    static double Now => Environment.TickCount64 / 1000.0;

    public static void Configure(string? on, string? probe)
    {
        _forced = string.IsNullOrWhiteSpace(on) || on.Trim() is not ("0" or "off");
        ProbeOn = !string.IsNullOrWhiteSpace(probe) && probe.Trim() is not ("0" or "off");

        // 0079: on by default, since it is what the depth test needs; KF3_BLENDORDER=0 compares.
        BlendOrder.Enabled = (Environment.GetEnvironmentVariable("KF3_BLENDORDER") ?? "1").Trim() != "0";

        // The restart threshold (0051). Zero, the default, is one buffer for the frame.
        _threshold = ParseFloat(Environment.GetEnvironmentVariable("KF3_ZBUFFER_THRESHOLD")) ?? 0f;
    }

    /// <summary>The records only exist while the bulk assemblers are in C#; the
    /// Testing tab moves that at run time, so ask again every frame.</summary>
    public static void SyncSource() => GtePacketDepth.Enabled = Enabled && (PolyAssembler.Setting == 1 || NearPath.Setting == 1);

    public static void Install()
    {
        _windowStart = Now;
        GteDepth.DepthBias = DefaultBias;
        GteDepth.DepthSlope = DefaultSlope;
        GteDepth.DepthClearThreshold = _threshold;
        Enabled = _forced;
        SyncSource();

        HookAttach.OnOverlayLoad("zbuffer", Attach);
    }

    static float? ParseFloat(string? s) =>
        float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float f) && f >= 0f ? f : null;

    static bool Attach()
    {
        SymbolRegistry.Build();
        var after = typeof(ZBuffer).GetMethod(nameof(AfterDrawOTag), BindingFlags.Public | BindingFlags.Static)!;

        int n = 0;
        foreach (var (overlay, addr) in DrawOTag)
        {
            var target = SymbolRegistry.Resolve(overlay, null, addr);
            if (target == null)
            {
                Console.Error.WriteLine($"[KF3] zbuffer: no function at {overlay}/0x{addr:X8}");
                continue;
            }
            if (HookManager.AddPost(_self, target, after)) n++;
        }

        HookManager.Commit();
        int hooked = DrawOTag.Count(s => HookAttach.Installed(SymbolRegistry.Resolve(s.Overlay, null, s.Addr)));
        Console.WriteLine($"[KF3] zbuffer: {(Enabled ? "on" : "off (ordering table)")}, " +
                          $"{hooked}/{DrawOTag.Length} DrawOTag hooked");
        return hooked == DrawOTag.Length;
    }

    public static void AfterDrawOTag(CpuContext c, IMemory m)
    {
        SyncSource();
        _frames++;
        double window = Now - _windowStart;
        if (window < 2.0) return;

        if (ProbeOn) Report(window);
        GtePacketDepth.ResetCounters();
        GteDepth.ResetZCounters();
        GteDepth.ZPrepasses = 0;
        _frames = 0;
        _windowStart = Now;
    }

    static string Pct(long n, long total) => total == 0 ? "0.0%" : $"{100.0 * n / total:F1}%";

    static void Report(double window)
    {
        long tested = GteDepth.ZTris, skipped = GteDepth.ZSkipped;
        long total = tested + skipped;
        long hits = GtePacketDepth.Hits, misses = GtePacketDepth.Misses;

        Console.WriteLine($"[KF3] zbuffer: {GtePacketDepth.Recorded / window:F0} packet depths recorded/s, " +
                          $"{hits / window:F0} polygons found theirs/s, {misses / window:F0} unmatched/s " +
                          $"({Pct(hits, hits + misses)}), {tested / window:F0} triangles tested/s, " +
                          $"{skipped / window:F0} kept painter's order" +
                          $"{(total == 0 ? "" : $" ({Pct(tested, total)} of submitted)")}, " +
                          $"over {_frames / window:F0} frames/s");

        var drops = GteDepth.ZDrops;
        long dropTotal = 0;
        foreach (var n in drops) dropTotal += n;
        if (dropTotal > 0)
            Console.WriteLine($"[KF3] zbuffer: {GteDepth.ZClears / window:F0} depth clear(s)/s at threshold " +
                              $"{(GteDepth.DepthClearThreshold <= 0f ? "off" : GteDepth.DepthClearThreshold.ToString("0"))}; " +
                              $"forward steps {dropTotal / window:F0}/s: " +
                              $"{Pct(drops[0], dropTotal)} under 10, {Pct(drops[1], dropTotal)} under 50, " +
                              $"{Pct(drops[2], dropTotal)} under 150, {Pct(drops[3], dropTotal)} under 300, " +
                              $"{Pct(drops[4], dropTotal)} under 1000, {Pct(drops[5], dropTotal)} beyond; " +
                              $"widest {GteDepth.ZDropMax:F0}");
    }
}
