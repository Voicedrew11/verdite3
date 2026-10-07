using System.Reflection;
using RecompOne.Runtime;
using RecompOne.Runtime.Context;
using RecompOne.Runtime.Events;
using RecompOne.Runtime.Hle;
using RecompOne.Runtime.Memory;
using RecompOne.Runtime.Modding;

namespace Kf3;

/// <summary>
/// Renders the game wider than its 320-pixel screen, Verdite2's widescreen on this
/// disc. The runtime already does the work: a display buffer is drawn into a render
/// target carrying a margin of extra columns either side (<c>GpuHle.WideMargin</c>),
/// only the original columns go back to VRAM, and the widened target is presented
/// at <c>Display.WideAspect</c>. This file is the switch, the latch clear, the
/// full-screen tint stretch and the HUD at the screen edges.
///
///     KF3_WIDESCREEN=16:9       aspect for the run; "1.777" and "off" also parse
///     KF3_WIDESCREEN_PROBE=1    the margin census, on the console
///     KF3_WIDESCREEN_PROBE=2    the census plus every wide primitive, once per shape
///     KF3_WIDESCREEN_EFFECTS=0  leave the death fade and the damage flash 320 wide
///     KF3_WIDESCREEN_HUD=1      move the HUD out to the new edges (0 leaves it), over the kept choice
///
/// The runtime's implement is not a stretch: the projection is untouched, so pixels
/// keep their aspect and the extra picture is only there where the game submits
/// geometry it expects the GPU to clip. <see cref="Census"/> counts that.
/// <see cref="Stretch"/> widens the game's full-screen tints -- the death fade and
/// the damage flash -- which are flat, semi-transparent quads spanning the clip
/// rectangle exactly; keyed on that shape rather than a drawer address. On
/// whenever an aspect is chosen; <c>KF3_WIDESCREEN_EFFECTS=0</c> is the comparison.
///
/// <see cref="AnchorHud"/> is Verdite2's HUD at the screen edges, by the same means:
/// the records each HUD drawer reads have their screen X moved out by the margin
/// for the call and put back after it, so nothing downstream is told and a menu's
/// primitives are never touched. Here there are two drawers of the HUD's, not one:
/// the compass model (<c>func_8003C35C</c>, X at <c>+0x10</c>) and the gauge and
/// digit sprites (<c>func_80041E68</c>, X at <c>+6</c>). The bottom message box's
/// drawer, <c>func_80041D9C</c>, is left alone: the box is centred. Verdite2's
/// DrawOTag replacement is not ported; it fed only the older, guessed anchoring.
///
/// It is a setting, under Testing ▸ Picture. The saved aspect is read on
/// <see cref="RuntimeReadyEvent"/> rather than in <see cref="Configure"/>, since
/// ConfigManager only loads inside HostWindow.Initialize, after Program.cs.
///
/// See "The margin, the latches and the tints" in docs/WIDESCREEN.md.
/// </summary>
public static class Widescreen
{
    /// <summary>Where the choice is kept between runs. Named as Verdite2 named it,
    /// to follow the port's convention.</summary>
    public const string AspectKey = "kf3.widescreen.aspect";

    /// <summary>Whether the HUD is moved out to the new edges; Verdite2's key, under
    /// this port's prefix.</summary>
    public const string HudKey = "kf3.widescreen.hud";

    /// <summary>The game's own aspect, and the one that means "off".</summary>
    public const float FourThree = 4f / 3f;

    /// <summary>As wide as an aspect is allowed to get. Past 3:1 the margin is wider
    /// than the screen and the frame is mostly things the game never meant to draw.</summary>
    public const float Widest = 3f;

    /// <summary>What the settings combo offers. <c>KF3_WIDESCREEN</c> takes any
    /// ratio, and one that is none of these keeps its own entry in the combo.</summary>
    public static readonly (string Name, float Ratio)[] Presets =
    [
        ("4:3 (off)", FourThree),
        ("16:9",      16f / 9f),
        ("16:10",     16f / 10f),
        ("21:9",      64f / 27f),
    ];

    /// <summary>What a player who never chose gets: 16:9, the user's choice, 2026-10-06.</summary>
    public const float DefaultAspect = 16f / 9f;

    /// <summary>The target aspect. 4:3 is off.</summary>
    public static float Aspect { get; private set; } = FourThree;

    /// <summary>Widen the game's full-screen tints -- the death fade, the damage
    /// flash -- across the margin. On whenever an aspect is chosen;
    /// <c>KF3_WIDESCREEN_EFFECTS=0</c> is the comparison.</summary>
    public static bool StretchEffects { get; private set; } = true;

    /// <summary>Move the compass and the gauges out to the new edges. Off by default:
    /// everything else widescreen does presents what the game submitted, and this
    /// alone moves something the game placed. Costs nothing at 4:3.</summary>
    public static bool AnchorHud { get; private set; }

    /// <summary>Takes effect on the next frame the HUD is drawn.</summary>
    public static void SetAnchorHud(bool on) => AnchorHud = on;

    /// <summary>Whether the aspect is actually widening anything.</summary>
    public static bool On => Display.WideAspect > 0f;

    /// <summary>Extra columns a side, by the runtime's own sizing rule -- so the
    /// number the settings combo shows and the number the render target is built
    /// with are the same one. 320 is this game's display width.</summary>
    public static int Margin => Display.WideMargin(320);

    /// <summary>KF3_WIDESCREEN: an explicit aspect for the run, which wins over
    /// the saved setting.</summary>
    static float? _forced;

    /// <summary>KF3_WIDESCREEN_PROBE: count primitives reaching the margin and
    /// report to the console.</summary>
    static bool _measure;

    /// <summary>KF3_WIDESCREEN_EFFECTS: the comparison, since there is no check box.</summary>
    static bool? _forcedEffects;

    /// <summary>KF3_WIDESCREEN_HUD: the anchoring for the run, over the kept choice.</summary>
    static bool? _forcedHud;

    /// <summary>KF3_WIDESCREEN_PROBE=2: also list the wide primitives themselves.</summary>
    static bool _listWide;

    // How far off the screen edge a vertex may sit and still count as being on it.
    // The game's own tints land exactly on 0 and 320, so this is only insurance.
    const int EdgeSlack = 2;

    // Full-width tints stretched in the current report window.
    static long _stretched;

    // Per report window, split by whether the primitive crossed into the margin.
    static long _inside, _margin;
    static double _windowStart;

    // The census's second half: every primitive wide enough to be a screen-space
    // overlay, once per distinct shape, so a fade or a flash names itself.
    static readonly HashSet<long> _seenWide = [];

    static double Now => Environment.TickCount64 / 1000.0;

    // HookManager attributes hooks to a mod; this is in-project, so it names itself.
    static readonly ModInfo _self = new()
    {
        Id = "kf3.widescreen",
        Name = "Widescreen",
        Version = "1.0",
        Description = "Renders a margin either side of the game's 320-pixel screen.",
    };

    public static void Configure(string? aspect, string? probe, string? effects = null, string? hud = null)
    {
        if (Parse(aspect) is { } ratio) _forced = ratio;

        if (!string.IsNullOrWhiteSpace(probe) && !probe.Equals("0", StringComparison.Ordinal))
        {
            _measure = true;
            // =2 additionally lists the wide primitives themselves, which is how the
            // full-screen tint was identified. It is a page of output per scene, so
            // it is not what a plain =1 asks for.
            _listWide = probe.Equals("2", StringComparison.Ordinal);
        }

        if (!string.IsNullOrWhiteSpace(effects))
            _forcedEffects = !effects.Equals("0", StringComparison.Ordinal);

        if (!string.IsNullOrWhiteSpace(hud))
            _forcedHud = !hud.Equals("0", StringComparison.Ordinal);
    }

    /// <summary>Apply the aspect and attach the overlay listener. The saved setting
    /// is read on <c>RuntimeReadyEvent</c> -- ConfigManager only loads inside
    /// HostWindow.Initialize, which is after Program.cs, so reading it here would
    /// read an empty config and then write it back over the real one. An aspect from
    /// the environment is applied immediately, so a run started for the picture is
    /// wide from the first frame of the title screen.</summary>
    public static void Install()
    {
        _windowStart = Now;
        if (_forced is { } forced) { Aspect = forced; Apply(); }

        Event.AddListener<RuntimeReadyEvent>(_ =>
        {
            Aspect = _forced ?? RecompOne.Runtime.Runtime.View.GetFloat(AspectKey, DefaultAspect);

            // The comparison, not a saved key: a player who ticked the old option off
            // would otherwise be stuck with it with nothing left to put it back.
            StretchEffects = _forcedEffects ?? true;
            AnchorHud = _forcedHud ?? RecompOne.Runtime.Runtime.View.GetInt(HudKey, 0) != 0;
            Apply();
            Console.WriteLine(On
                ? $"[KF3] widescreen: {Aspect:0.###}:1, margin {Margin} px a side, " +
                  $"HUD {(AnchorHud ? "at the edges" : "in its 4:3 box")}, " +
                  $"screen tints {(StretchEffects ? "stretched across it" : "left 320 wide")}"
                : "[KF3] widescreen: off (4:3)");
        });

        // Attached at every aspect: the aspect and the anchoring change mid-session.
        HookAttach.OnOverlayLoad("widescreen HUD", AttachHud);

        // Attached whether or not an aspect is set: the aspect is a setting that can
        // be changed mid-session, and the latch clear must run under every overlay.
        Event.AddListener<OverlayLoadedEvent>(e =>
        {
            // The margin-content latch lasts for an *executable's* session, not an
            // area's. OPEN.EXE and END.EXE draw pictures that never reach the margin,
            // so a latch carried over from GAME.EXE would present invented sides --
            // that is what the clear is for. The nine fdat area modules are GAME.EXE
            // still running, and clearing on those drops the picture to the 320-wide
            // 4:3 fallback for the length of every area load.
            if (e.Name is "open" or "game" or "end")
                (GpuHle.Backend as GlCore)?.ClearMarginLatches();
        });
    }

    /// <summary>Change the aspect at run time. Safe at any moment -- the render
    /// target is rebuilt when its margin no longer matches.</summary>
    public static void SetAspect(float aspect)
    {
        Aspect = Math.Clamp(aspect, FourThree, Widest);
        Apply();
    }

    /// <summary>The <c>aspect</c> shell verb: the state, or a change through
    /// <see cref="SetAspect"/>, which is what the settings window calls.</summary>
    public static string Shell(string arg)
    {
        if (arg.Length > 0)
        {
            if (Parse(arg) is not { } ratio) return "{\"ok\":false,\"error\":\"aspect [4:3|16:9|16:10|21:9|<ratio>]\"}";
            SetAspect(ratio);
        }
        return $"{{\"ok\":true,\"cmd\":\"aspect\",\"aspect\":{Aspect.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)},\"margin\":{Margin}}}";
    }

    static void Apply()
    {
        // 4:3 means off, and off is 0 rather than 1.333 -- WideMargin returns no
        // margin for a non-positive aspect, and a zero-margin target presents at
        // SourceAspect, which is the untouched path.
        Display.WideAspect = Aspect > FourThree + 0.001f ? Aspect : 0f;
        Listen();
    }

    // One listener serves both jobs, so it is attached exactly when one of them
    // needs it -- the raster skips the whole dispatch when nothing is listening,
    // which is what makes 4:3 free rather than cheap.
    static void Listen()
    {
        Event.RemoveListener<RenderPrimEvent>(OnPrim);
        if (_measure || (On && StretchEffects)) Event.AddListener<RenderPrimEvent>(OnPrim);
    }

    // The draw area is the game's clip rect in VRAM coordinates and the primitive's
    // are already offset into the same space, so "reaches the margin" is just a
    // vertex outside the clip -- no assumption about where the display buffer sits.
    static void OnPrim(RenderPrimEvent e)
    {
        // Measured first, before anything below moves a vertex: the census is about
        // what the game submitted and the listing is about recognising it, and
        // neither is a question about where this patch then put it.
        if (_measure)
        {
            if (_listWide) ProbeWide(e);
            Census(e);
        }

        if (StretchEffects && On) Stretch(e);
    }

    static void Census(RenderPrimEvent e)
    {
        bool outside = false;
        for (int i = 0; i < e.Count; i++)
            if (e.X[i] < e.DrawLeft || e.X[i] > e.DrawRight) { outside = true; break; }

        if (outside) _margin++; else _inside++;

        double window = Now - _windowStart;
        if (window < 2.0) return;

        long total = _inside + _margin;

        // A scene with no margin primitives is one the game clipped itself; widening
        // the target cannot recover it.
        Console.WriteLine(total == 0
            ? "[KF3] widescreen: no primitives in the last window"
            : $"[KF3] widescreen: {_margin * 100.0 / total:F1}% of {total} prims reach the margin " +
              $"({total / window:F0}/s)" +
              (_stretched > 0 ? $"; {_stretched} full-screen tint(s) stretched" : ""));

        _inside = _margin = _stretched = 0;
        _windowStart = Now;
    }

    // A screen-space overlay -- a fade, a flash, a letterbox bar -- is a primitive
    // as wide as the screen, so this lists every primitive that covers most of the
    // clip rectangle, once per distinct shape. What comes out is the rule for
    // widening them.
    static void ProbeWide(RenderPrimEvent e)
    {
        int lo = int.MaxValue, hi = int.MinValue, top = int.MaxValue, bottom = int.MinValue;
        for (int i = 0; i < e.Count; i++)
        {
            lo = Math.Min(lo, e.X[i]); hi = Math.Max(hi, e.X[i]);
            top = Math.Min(top, e.Y[i]); bottom = Math.Max(bottom, e.Y[i]);
        }

        int width = e.DrawRight - e.DrawLeft + 1;
        // The part of it that is actually on screen: a world polygon a thousand
        // pixels wide off to one side is not an overlay, and there are thousands of
        // those a second.
        if (Math.Min(hi, e.DrawRight) - Math.Max(lo, e.DrawLeft) < width * 9 / 10) return;

        lo -= e.DrawLeft; hi -= e.DrawLeft;
        top -= e.DrawTop; bottom -= e.DrawTop;

        long key = ((long)(uint)(lo & 0x3FF) << 40) | ((long)(uint)(hi & 0x3FF) << 30) |
                   ((long)(uint)(top & 0x1FF) << 21) | ((long)(uint)(bottom & 0x1FF) << 12) |
                   ((long)(uint)e.Count << 8) | (e.Textured ? 1L : 0L) | (e.SemiTransparent ? 2L : 0L) |
                   (e.Gouraud ? 4L : 0L) | (e.Raw ? 8L : 0L);
        if (!_seenWide.Add(key)) return;

        Console.WriteLine($"[KF3] widescreen: wide prim x {lo}..{hi} y {top}..{bottom} " +
                          $"verts={e.Count} tex={(e.Textured ? 1 : 0)} semi={(e.SemiTransparent ? 1 : 0)} " +
                          $"gouraud={(e.Gouraud ? 1 : 0)} raw={(e.Raw ? 1 : 0)} clut=0x{e.Clut:X4} " +
                          $"clip={width}x{e.DrawBottom - e.DrawTop + 1}");
    }

    // Stretch a full-screen tint across the margin.
    //
    // The game paints the death fade, the damage flash and every other whole-screen
    // effect the same way: one flat, textured, semi-transparent quad from (0,0) to
    // (320,240), submitted at the front of the ordering table from a colour and a
    // blend mode left in a request block. Semi-transparent and flat is what
    // separates the tint from the world -- one colour laid over the whole frame is
    // exactly what an effect is, while world geometry is Gouraud-shaded to the last
    // polygon. A 2D picture authored 320 wide -- a title, a menu -- is opaque and is
    // deliberately left where it is: stretching a picture distorts it.
    static void Stretch(RenderPrimEvent e)
    {
        if (!e.SemiTransparent || e.Gouraud) return;

        int width = e.DrawRight - e.DrawLeft + 1;
        int margin = Display.WideMargin(width);
        if (margin <= 0) return;

        // Right is exclusive here: the game's own tint runs 0..320 over a 320-pixel
        // screen, which is the last column plus one, and a rectangle's second point
        // is x + w for the same reason.
        int left = e.DrawLeft, right = e.DrawRight + 1;

        int lo = int.MaxValue, hi = int.MinValue;
        for (int i = 0; i < e.Count; i++) { lo = Math.Min(lo, e.X[i]); hi = Math.Max(hi, e.X[i]); }
        if (lo > left + EdgeSlack || hi < right - EdgeSlack) return;

        // Snapped to the new edges rather than shifted by the margin, so a tint that
        // was a pixel short of the screen still covers all of it.
        for (int i = 0; i < e.Count; i++)
        {
            if (e.X[i] <= left + EdgeSlack) e.X[i] = left - margin;
            else if (e.X[i] >= right - EdgeSlack) e.X[i] = right + margin;
        }

        // The latch in the backend must not mistake this manufactured width for
        // margin content the game drew -- a stretched splash fade would otherwise
        // latch its buffer and the present would flap between widths again.
        GpuHle.PortWidenedPrim = true;
        _stretched++;
    }

    // ---- the HUD, moved by its records -----------------------------------------

    /// <summary>One table of HUD records and the routine that draws it: records of
    /// <paramref name="Stride"/> bytes from <paramref name="Table"/> until a first byte
    /// of 0xFF, a first byte of 0 hidden, the screen X a halfword at
    /// <paramref name="X"/> and, for a sprite, its width a byte at <paramref name="W"/>.</summary>
    sealed record HudTable(string Name, uint Drawer, uint Table, uint Stride, uint X, uint W, int Max)
    {
        public readonly short[] Saved = new short[Max];
        public int Moved;
        public long Records, Calls;
        public readonly HashSet<string> Seen = [];
    }

    static readonly HudTable[] HudTables =
    [
        // The compass, a model placed by its translation; the transform has no
        // divide, so the translation's X is where it lands. Width 0: a model's X is
        // its middle.
        new("models", 0x8003C35C, 0x80081C20, 0x24, 0x10, 0, 4),
        // The gauges and digits, SPRT from the records' X, Y and width.
        new("sprites", 0x80041E68, 0x800819B4, 0x14, 0x06, 0x04, 32),
    ];

    /// <summary>The middle of the 320-pixel screen: a record left of it belongs to
    /// the left edge.</summary>
    const int ScreenCentre = 160;

    static bool _hudQueued;

    static bool AttachHud()
    {
        SymbolRegistry.Build();
        var targets = HudTables.Select(t => SymbolRegistry.Resolve("game", null, t.Drawer)).ToArray();
        if (targets.Any(t => t == null)) return false;
        const BindingFlags flags = BindingFlags.Public | BindingFlags.Static;
        if (!_hudQueued)
        {
            for (int i = 0; i < HudTables.Length; i++)
            {
                HookManager.AddPre(_self, targets[i]!, typeof(Widescreen).GetMethod(i == 0 ? nameof(BeforeModels) : nameof(BeforeSprites), flags)!);
                HookManager.AddPost(_self, targets[i]!, typeof(Widescreen).GetMethod(i == 0 ? nameof(AfterModels) : nameof(AfterSprites), flags)!);
            }
            _hudQueued = true;
        }
        HookManager.Commit();
        return targets.All(HookAttach.Installed);
    }

    public static void BeforeModels(CpuContext c, IMemory m) => MoveOut(HudTables[0], m);
    public static void AfterModels(CpuContext c, IMemory m) => PutBack(HudTables[0], m);
    public static void BeforeSprites(CpuContext c, IMemory m) => MoveOut(HudTables[1], m);
    public static void AfterSprites(CpuContext c, IMemory m) => PutBack(HudTables[1], m);

    /// <summary>Move every record out to its edge for the drawer: left of the screen's
    /// middle by the margin to the left, right of it to the right. Hidden records
    /// are moved too, so the put-back needs no record of which were drawn.</summary>
    static void MoveOut(HudTable t, IMemory m)
    {
        t.Moved = 0;
        if (_measure) ProbeHud(t, m);
        if (!(AnchorHud && On)) return;
        int margin = Margin;
        if (margin <= 0) return;

        for (int i = 0; i < t.Max; i++)
        {
            uint rec = t.Table + (uint)i * t.Stride;
            if (m.ReadU8(rec) == 0xFF) break;
            short x = (short)m.ReadU16(rec + t.X);
            int middle = x + (t.W != 0 ? m.ReadU8(rec + t.W) / 2 : 0);
            t.Saved[i] = x;
            m.WriteU16(rec + t.X, (ushort)(x + (middle < ScreenCentre ? -margin : margin)));
            t.Moved = i + 1;
        }
        t.Records += t.Moved;
        t.Calls++;
    }

    /// <summary>Put every record's X back the moment the drawer has drawn it.</summary>
    static void PutBack(HudTable t, IMemory m)
    {
        for (int i = 0; i < t.Moved; i++)
            m.WriteU16(t.Table + (uint)i * t.Stride + t.X, (ushort)t.Saved[i]);
        t.Moved = 0;
    }

    /// <summary>KF3_WIDESCREEN_PROBE: each distinct set of drawn records' positions
    /// (index@x,y), once per table, up to 24 sets. Not their widths: a gauge's fill
    /// changes width as it moves.</summary>
    static void ProbeHud(HudTable t, IMemory m)
    {
        var line = new System.Text.StringBuilder();
        for (int i = 0; i < t.Max; i++)
        {
            uint rec = t.Table + (uint)i * t.Stride;
            byte on = m.ReadU8(rec);
            if (on == 0xFF) break;
            if (on == 0) continue;
            line.Append($" {i}@{(short)m.ReadU16(rec + t.X)},{(short)m.ReadU16(rec + t.X + 2)}");
        }
        string s = line.ToString();
        if (t.Seen.Count < 24 && t.Seen.Add(s))
        {
            Console.WriteLine($"[KF3] widescreen: HUD {t.Name} drawn at{(s.Length == 0 ? " (none)" : s)}" +
                              $"; {t.Records} record(s) moved in {t.Calls} call(s) so far");
            Console.Out.Flush();
        }
    }

    /// <summary>"16:9" and "1.777" both, since the environment variable is typed by
    /// hand; "off" is 4:3. Null for anything else, which leaves the saved setting
    /// in charge rather than silently picking a ratio.</summary>
    static float? Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        value = value.Trim();

        if (value.Equals("off", StringComparison.OrdinalIgnoreCase)) return FourThree;

        int colon = value.IndexOf(':');
        if (colon > 0)
        {
            if (float.TryParse(value[..colon], out float w) &&
                float.TryParse(value[(colon + 1)..], out float h) && h > 0f)
                return w / h;
            return null;
        }

        return float.TryParse(value, out float ratio) && ratio > 0f ? ratio : null;
    }
}
