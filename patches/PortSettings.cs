using System.Globalization;
using ImGuiNET;
using RecompOne.Runtime;
using RecompOne.Runtime.Events;
using Rt = RecompOne.Runtime.Runtime;

namespace Kf3;

/// <summary>
/// Every port setting a player sees, declared once for the Settings window and the
/// game's own menu. See docs/SETTINGS.md for the rules and what each piece does.
///
/// This list does not read the saved values at boot: each feature's own start-up
/// still does, as before, so moving a setting here changes nothing a player has.
/// What this adds at boot is a check that every declared default is what the
/// feature really starts at, which <see cref="SettingsSession.Reset"/> relies on.
/// </summary>
public static class PortSettings
{
    public const int MaxRows = SettingsPage.MaxRows;   // what the frame fits at the menu's spacing

    static string Number(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    static Func<double, string> Named(double[] steps, params string[] names) => v =>
        Array.FindIndex(steps, s => PortSetting.Same(s, v)) is >= 0 and var i ? names[i] : Number(v);

    static PortSetting Switch(string key, string env, string label, string? page, string? menu,
        Func<bool> get, Action<bool> set, bool on, bool localized = false, string? tip = null,
        Func<bool>? usable = null, Ui ui = Ui.Checkbox) => new()
    {
        Key = key, Envs = [env], Label = label, Localized = localized, Tip = tip,
        Page = page, MenuLabel = menu, Steps = [0, 1], MenuValue = v => v != 0 ? "ON" : "OFF",
        Default = on ? 1 : 0, Live = () => get() ? 1 : 0, Apply = v => set(v != 0),
        Usable = usable, Ui = ui,
    };

    /// <summary>A row of the game's page standing for several settings (see
    /// <see cref="PortSetting.Parts"/>); declared after its parts, whose defaults it reads.</summary>
    static PortSetting Combine(string key, string page, string menu, PortSetting[] parts, double[] steps,
        Func<double, string> text, Func<Func<PortSetting, double>, double> join,
        Func<double, Func<PortSetting, double>, double[]> split) => new()
    {
        Key = key, Envs = [.. parts.SelectMany(p => p.Envs).Distinct()], Label = menu,
        Page = page, MenuLabel = menu, Steps = steps, MenuValue = text,
        Parts = parts, Join = join, Split = split,
        Default = join(p => p.Default), Live = () => join(p => p.Live()),
        Apply = v =>
        {
            var values = split(v, p => p.Live());
            for (int i = 0; i < parts.Length; i++)
                if (!double.IsNaN(values[i])) parts[i].Apply(values[i]);
        },
        Ui = Ui.None,
    };

    /// <summary>A combined row's value when its parts match none of its steps (set in the
    /// Settings window or by a variable): between the first two steps, so Left and Right
    /// both reach one.</summary>
    const double Custom = 0.5;

    /// <summary>A part a combined row's step leaves where it was when the page opened.</summary>
    public const double Unchanged = double.NaN;

    // The pages, in the order the game's menu shows them, and their rows in
    // Menu below. Everything else (the renderer's own switches, the smoothers, the
    // mouse, the fog, the parts of a combined row) stays in the Settings window:
    // a player who raises the frame rate wants the smoothing, and so on.
    public const string DisplayPage = "DISPLAY", Graphics = "GRAPHICS", World = "WORLD", Gameplay = "GAMEPLAY";
    public static readonly string[] Pages = [DisplayPage, Graphics, World, Gameplay];

    public static readonly PortSetting Display = new()
    {
        Key = WindowMode.FullscreenKey, Label = "Display mode",
        Page = DisplayPage, MenuLabel = "DISPLAY", Steps = [0, 1, 2], MenuValue = Named([0, 1, 2], "WINDOWED", "FULLSCREEN", "BORDERLESS"),
        Default = 0, Live = () => WindowMode.Mode, Apply = v => WindowMode.Set((int)Math.Round(v)),
        Keys = WindowMode.Keys, Ui = Ui.None,
    };

    static readonly double[] Scales = [1, 2, 3, 4, 5, 6, 7, 8];

    /// <summary>The render scale, named by the lines it draws: the game's 240 times the
    /// scale. The backend takes it at the next present (0097), at most its MaxScale.</summary>
    public static readonly PortSetting Resolution = new()
    {
        Key = "RenderScale", Label = "Render scale",
        Page = DisplayPage, MenuLabel = "RESOLUTION", Steps = Scales, MenuValue = v => Number(v * 240) + "P",
        Default = 4, Live = () => RecompOne.Runtime.Hle.GlVram.Requested is > 0 and var r ? r : RecompOne.Runtime.Hle.GlVram.Scale,
        Apply = v => RecompOne.Runtime.Hle.GlVram.Requested = (int)Math.Clamp(Math.Round(v), 1, 8),
        Ui = Ui.None,
    };

    // ---- Display: the aspect is the Testing tab's, drawn by its own code ----

    static readonly double[] Aspects = [.. Widescreen.Presets.Select(p => (double)p.Ratio)];

    public static readonly PortSetting Aspect = new()
    {
        Key = Widescreen.AspectKey, Envs = ["KF3_WIDESCREEN"], Label = "kf3testing.widescreen.aspect", Localized = true,
        Page = DisplayPage, MenuLabel = "ASPECT", Steps = Aspects, MenuValue = Named(Aspects, "4/3", "16/9", "16/10", "21/9"),
        Default = Widescreen.DefaultAspect, Live = () => Widescreen.Aspect, Apply = v => Widescreen.SetAspect((float)v),
        Stored = Stored.Float, Ui = Ui.None,
    };

    /// <summary>Verdite2's HUD at the screen edges: the compass and the gauges moved out
    /// by the margin. Under the aspect, dimmed at 4:3.</summary>
    public static readonly PortSetting HudAnchor = Switch(Widescreen.HudKey, "KF3_WIDESCREEN_HUD", "HUD at the screen edges",
        DisplayPage, "HUD AT EDGES", () => Widescreen.AnchorHud, Widescreen.SetAnchorHud, false,
        tip: "Moves the compass and the gauges out to the edges of a wide picture instead of the 4:3 box they were drawn for.",
        usable: () => Widescreen.On);

    public static readonly PortSetting Shading = new()
    {
        Key = "kf3.shading", Envs = ["KF3_TRUECOLOR", "KF3_NODITHER"], Label = "Shading", Steps = [0, 1, 2], MenuValue = Named([0, 1, 2], "DITHER", "NONE", "SMOOTH"),
        Default = 2, Live = () => TestingSection.Shading, Apply = v => TestingSection.SetShading((int)v), Ui = Ui.None,
    };

    public static readonly PortSetting Perspective = Switch("kf3.perspective", "KF3_PERSPECTIVE", "Perspective-correct textures",
        null, null, () => Kf3.Perspective.Enabled, v => Kf3.Perspective.Enabled = v, true, ui: Ui.None);
    public static readonly PortSetting Subpixel = Switch("kf3.subpixel", "KF3_SUBPIXEL", "Sub-pixel vertices",
        null, null, () => Kf3.Subpixel.Enabled, v => Kf3.Subpixel.Enabled = v, true, ui: Ui.None);
    public static readonly PortSetting ZBuffer = Switch("kf3.zbuffer", "KF3_ZBUFFER", "Z-buffer",
        null, null, () => Kf3.ZBuffer.Enabled, v => Kf3.ZBuffer.Enabled = v, true, ui: Ui.None);
    public static readonly PortSetting MenuWorld = Switch("kf3.menuworld", "KF3_MENUWORLD", "The world live behind menus and messages",
        null, null, () => Kf3.MenuWorld.Enabled, v => Kf3.MenuWorld.Enabled = v, true, ui: Ui.None);

    // ---- Motion: also the Testing tab's ----

    public static readonly PortSetting Pacing = Switch("kf3.pacing", "KF3_FPS", "Frame pacing",
        null, null, () => FramePacing.Enabled, FramePacing.SetEnabled, true, ui: Ui.None);

    static readonly double[] Rates = [30, 60, 72, 75, 90, 100, 120, 144, 165, 180, 240, 360, 0];

    public static readonly PortSetting FrameRate = new()
    {
        Key = "kf3.fps", Envs = ["KF3_FPS"], Label = "Frame rate",
        Steps = Rates, MenuValue = v => v <= 0 ? "UNCAPPED" : Number(v),
        Default = FramePacing.DefaultFps, Live = () => Math.Max(0, FramePacing.TargetFps), Apply = FramePacing.SetTarget,
        Usable = () => FramePacing.Enabled, Ui = Ui.None,
    };

    public static readonly PortSetting SmoothCamera = Switch("kf3.smooth", "KF3_SMOOTH", "Camera, compass needle and gauges",
        null, null, () => ViewSmoothing.Enabled, v => ViewSmoothing.Enabled = v, true,
        usable: () => FramePacing.Enabled, ui: Ui.None);
    public static readonly PortSetting SmoothModels = Switch("kf3.smooth_models", "KF3_SMOOTH_MODELS", "Creatures and objects",
        null, null, () => ModelSmoothing.Enabled, v => ModelSmoothing.Enabled = v, true,
        usable: () => FramePacing.Enabled, ui: Ui.None);

    public static readonly PortSetting TexScroll = new()
    {
        Key = "kf3.texscroll", Envs = ["KF3_TEXSCROLL"], Label = "Scrolling textures",
        Steps = [0, 1, 2], MenuValue = Named([0, 1, 2], "EVERY FRAME", "HELD", "CARRIED"),
        Default = 2, Live = () => TextureScroll.Setting, Apply = v => TextureScroll.Setting = (int)v,
        Usable = () => FramePacing.Enabled, Ui = Ui.None,
    };

    // ---- The Video tab's world enhancements (SceneFeatures), on Graphics ----

    public static readonly PortSetting PerPixel = Switch("kf3.perpixel", "KF3_PERPIXEL", "kf3scene.perpixel",
        Graphics, "PER-PIXEL LIGHT", () => GteLightMap.Enabled, v => GteLightMap.Enabled = v, true, localized: true);
    /// <summary>The fog, one choice of three: the game's, worked out at a face's corners
    /// and spread across it; by each pixel's view depth; or by each pixel's distance from
    /// the eye (runtime 0100), the same at the picture's centre, more at its edges and
    /// still as the view turns. Distance is per pixel too, so the two switches this
    /// replaced (<c>kf3.fogdepth</c>, <c>kf3.radialfog</c>) had a combination that half
    /// worked. Applied at boot by <see cref="SceneFeatures"/>.</summary>
    public static readonly PortSetting Fog = new()
    {
        Key = "kf3.fog", Envs = ["KF3_FOG", "KF3_FOG_DEPTH"], Label = "kf3scene.fog", Localized = true,
        Steps = [0, 1, 2], MenuValue = Named([0, 1, 2], "CORNERS", "DEPTH", "DISTANCE"),
        Names = ["kf3scene.fogcorners", "kf3scene.fogdepth", "kf3scene.fogdistance"],
        Default = 1, Live = () => SceneFeatures.Fog, Apply = v => SceneFeatures.SetFog((int)v), Ui = Ui.Combo,
    };
    public static readonly PortSetting Ao = Switch("kf3.ao", "KF3_AO", "kf3scene.ao",
        null, null, () => GteDepth.AmbientOcclusion, v => GteDepth.AmbientOcclusion = v, true, localized: true);
    public static readonly PortSetting AoNormals = Switch("kf3.ao.normals", "KF3_AO_NORMALS", "kf3scene.normals",
        null, null, () => AoGeometry.Enabled, v => { AoGeometry.Enabled = v; GteDepth.AoNormals = v; }, true, localized: true);
    public static readonly PortSetting Mipmaps = Switch("kf3.mipmaps", "KF3_MIPMAPS", "kf3scene.mips",
        null, null, () => GteDepth.Mipmaps, v => GteDepth.Mipmaps = v, true, localized: true);
    public static readonly PortSetting NeighbourBlend = Switch("kf3.neighbourblend", "KF3_NEIGHBOUR_BLEND", "kf3scene.blend",
        null, null, () => RecompOne.Runtime.NeighbourBlend.Mode != 0,
        v => RecompOne.Runtime.NeighbourBlend.Mode = v ? RecompOne.Runtime.NeighbourBlend.Fog | RecompOne.Runtime.NeighbourBlend.Light : 0,
        true, localized: true);

    public static readonly PortSetting AoQuality = new()
    {
        Key = "kf3.ao.quality", Envs = ["KF3_AO_QUALITY"], Label = "kf3scene.quality", Localized = true,
        Steps = [0, 1, 2], MenuValue = Named([0, 1, 2], "LOW", "MEDIUM", "HIGH"), Names = ["kf3scene.low", "kf3scene.medium", "kf3scene.high"],
        Default = 1, Live = () => SceneFeatures.Quality, Apply = v => SceneFeatures.SetQuality((int)v), Ui = Ui.Combo,
    };

    static readonly double[] Taps = [1, 2, 4, 8, 16];

    public static readonly PortSetting Anisotropy = new()
    {
        Key = "kf3.aniso", Envs = ["KF3_ANISO"], Label = "kf3scene.aniso", Localized = true,
        Steps = Taps, MenuValue = v => v <= 1 ? "OFF" : Number(v) + "X",
        Default = 16, Live = () => GteDepth.Anisotropy, Apply = v => GteDepth.Anisotropy = (int)Math.Clamp(Math.Round(v), 1, 16),
        Stored = Stored.Float, Ui = Ui.SliderInt, Min = 1, Max = 16,
    };

    public static readonly PortSetting EnhanceDistance = new()
    {
        Key = "kf3.enhancedistance", Envs = ["KF3_ENHANCEDIST"], Label = "kf3scene.distance", Localized = true,
        Steps = [0, 4, 8, 12, 16], MenuValue = v => v <= 0 ? "EVERYWHERE" : Number(v),
        Default = 0, Live = () => GteDepth.PlainDepth > 0 ? GteDepth.PlainDepth / 2048 : 0,
        Apply = v => SceneFeatures.SetDistance((float)v), Stored = Stored.Float, Ui = Ui.None,
    };

    static readonly double[] Reaches = [0, 8, 12, 16, 20, 24, RenderDistance.MaxTiles];

    public static readonly PortSetting RenderDist = new()
    {
        Key = "kf3.renderdistance", Envs = ["KF3_RENDERDIST"], Label = "kf3scene.fartiles", Localized = true,
        Steps = Reaches, MenuValue = v => v <= 0 ? "GAME'S" : Number(v),
        Default = EnhancedTiles, Live = () => RenderDistance.Tiles, Apply = v => RenderDistance.SetTiles((float)v),
        Stored = Stored.Float, Ui = Ui.None,
    };

    static readonly double[] Bands = [0, 1, 2, 3, 4, 6, RenderDistance.MaxFade];

    public static readonly PortSetting RenderFade = new()
    {
        Key = "kf3.renderdistance.fade", Envs = ["KF3_RENDERDIST_FADE"], Label = "kf3scene.fadetiles", Localized = true,
        Steps = Bands, MenuValue = v => v <= 0 ? "OFF" : Number(v),
        Default = EnhancedFade, Live = () => RenderDistance.FadeTiles, Apply = v => RenderDistance.SetFade((float)v),
        Stored = Stored.Float, Ui = Ui.None,
    };

    // ---- Water: the Video tab's too, on World ----

    public static readonly PortSetting Planar = Switch("kf3.planar", "KF3_PLANAR", "kf3scene.planar",
        null, null, () => PlanarMirror.Enabled, PlanarMirror.SetEnabled, true, localized: true);
    public static readonly PortSetting MurkyWater = Switch("kf3.murk", "KF3_MURK", "kf3scene.murk",
        null, null, () => Murk.Enabled, Murk.SetEnabled, true, localized: true);
    public static readonly PortSetting WaterWaves = Switch("kf3.waves", "KF3_WAVES", "kf3scene.waves",
        null, null, () => Waves.Enabled, Waves.SetEnabled, true, localized: true);

    /// <summary>The Video tab's switches, in its order; it applies their saved values at boot.</summary>
    public static readonly PortSetting[] SceneSwitches = [PerPixel, Ao, AoNormals, Mipmaps, NeighbourBlend, Planar, MurkyWater, WaterWaves];

    // ---- Gameplay: the Gameplay tab ----

    public static readonly PortSetting AutoReloadOn = Switch(AutoReload.OnKey, "KF3_AUTORELOAD", "Reload the last save on death",
        Gameplay, "RELOAD ON DEATH", () => AutoReload.Enabled, AutoReload.SetEnabled, true,
        tip: "Puts you back at your last save instead of the menus.");

    static readonly double[] SlotSteps = [0, 1, 2, 3, 4, 5];

    public static readonly PortSetting AutoReloadSlot = new()
    {
        Key = AutoReload.SlotKey, Envs = ["KF3_AUTORELOAD_SLOT"], Label = "Save slot",
        Tip = "Which save to reload. \"Last used\" follows where you saved or loaded.",
        Steps = SlotSteps,
        MenuValue = Named(SlotSteps, "LAST USED", "SLOT 1", "SLOT 2", "SLOT 3", "SLOT 4", "SLOT 5"),
        Names = ["Last used", "Slot 1", "Slot 2", "Slot 3", "Slot 4", "Slot 5"],
        Default = 0, Live = () => AutoReload.Slot, Apply = v => AutoReload.SetSlot((int)v),
        Usable = () => AutoReload.Enabled, Ui = Ui.Combo,
    };

    public static readonly PortSetting MessageFade = new()
    {
        Key = Kf3.MenuWorld.FadeKey, Envs = ["KF3_MESSAGE_FADE"], Label = "Message fade length",
        Tip = "How long signs and messages take to fade in and out. x1 is the game's own speed.",
        Steps = [1, 2, 3, 4], MenuValue = v => "X" + Number(v),
        Default = 1, Live = () => Kf3.MenuWorld.FadeVBlanks, Apply = v => Kf3.MenuWorld.SetFadeVBlanks((int)v),
        Ui = Ui.SliderInt, Min = 1, Max = Kf3.MenuWorld.MaxFadeVBlanks, SliderText = v => v <= 1 ? "x1 (original)" : "x%d",
    };

    public static readonly PortSetting MouseLook = Switch(Mouse.OnKey, "KF3_MOUSE", "Mouse look",
        null, null, () => Mouse.Enabled, v => Mouse.Enabled = v, true, ui: Ui.None);
    public static readonly PortSetting InstantMouseLook = Switch(Mouse.LeadKey, "KF3_MOUSE_LEAD", "Instant mouse look",
        null, null, () => Mouse.Lead, v => Mouse.Lead = v, true,
        tip: "Turns the view the frame you move the mouse, instead of on the game's next tick.", usable: () => Mouse.Enabled);

    // ---- The combined rows ----

    static readonly double[] RateRows = [-1, .. Rates];

    /// <summary>ORIGINAL is no pacing; any rate paces, and the smoothers follow pacing.
    /// ORIGINAL leaves the kept rate as it was, not at the 30 walked through to reach it.</summary>
    public static readonly PortSetting FrameRateRow = Combine("row.framerate", DisplayPage, "FRAME RATE", [Pacing, FrameRate],
        RateRows, v => v < 0 ? "ORIGINAL" : FrameRate.MenuValue(v),
        get => get(Pacing) == 0 ? -1 : get(FrameRate),
        (v, _) => v < 0 ? [0, Unchanged] : [1, v]);

    /// <summary>Off, or on at one of the three qualities; OFF leaves the kept quality.</summary>
    public static readonly PortSetting AmbientOcclusion = Combine("row.ao", Graphics, "AMB. OCCLUSION", [Ao, AoQuality],
        [0, 1, 2, 3], v => v <= 0 ? "OFF" : AoQuality.MenuValue(v - 1),
        get => get(Ao) == 0 ? 0 : get(AoQuality) + 1,
        (v, _) => v <= 0 ? [0, Unchanged] : [1, v - 1]);

    static readonly double[] FilterRows = [0, 1, 2, 4, 8, 16];

    /// <summary>Anisotropic taps walk the mip chain, so any of them turns the mipmaps on.
    /// Taps without mipmaps (the Settings window can still set it) show half a step below.</summary>
    public static readonly PortSetting TextureFilter = Combine("row.texturefilter", Graphics, "TEXTURE FILTER", [Mipmaps, Anisotropy],
        FilterRows, v => v <= 0 ? "OFF" : v == 1 ? "MIPMAPS" : v % 1 != 0 ? Number(v + .5) + "X NO MIPMAPS" : Number(v) + "X",
        get => get(Mipmaps) != 0 ? Math.Max(1, get(Anisotropy)) : get(Anisotropy) <= 1 ? 0 : get(Anisotropy) - .5,
        (v, _) => v <= 0 ? [0, 1] : [1, v]);

    /// <summary>ENHANCED is the reach and fade the user chose, 2026-10-06.</summary>
    public const double EnhancedTiles = 16, EnhancedFade = 3;

    public static readonly PortSetting RenderDistanceRow = Combine("row.renderdistance", World, "RENDER DISTANCE", [RenderDist, RenderFade],
        [0, 1], v => v == 0 ? "ORIGINAL" : v == 1 ? "ENHANCED" : "CUSTOM",
        get => (get(RenderDist), get(RenderFade)) switch
        {
            (0, 0) => 0,
            (EnhancedTiles, EnhancedFade) => 1,
            _ => Custom,
        },
        (v, _) => v >= 1 ? [EnhancedTiles, EnhancedFade] : [0, 0]);

    /// <summary>ENHANCED is the surface (murk and the swell); FULL adds the reflections,
    /// the one that costs a second view.</summary>
    public static readonly PortSetting Water = Combine("row.water", World, "WATER", [MurkyWater, WaterWaves, Planar],
        [0, 1, 2], v => v == 0 ? "ORIGINAL" : v == 1 ? "ENHANCED" : v == 2 ? "FULL" : "CUSTOM",
        get => (get(MurkyWater), get(WaterWaves), get(Planar)) switch
        {
            (0, 0, 0) => 0,
            (1, 1, 0) => 1,
            (1, 1, 1) => 2,
            _ => Custom,
        },
        (v, _) => v >= 2 ? [1, 1, 1] : v >= 1 ? [1, 1, 0] : [0, 0, 0]);

    /// <summary>The game's page, in order.</summary>
    public static readonly PortSetting[] Menu =
    [
        Display, Resolution, Aspect, HudAnchor, FrameRateRow,
        TextureFilter, PerPixel, AmbientOcclusion,
        RenderDistanceRow, Water,
        AutoReloadOn,
    ];

    /// <summary>Every kept setting; the combined rows keep nothing of their own.</summary>
    public static readonly PortSetting[] All =
    [
        Display, Resolution, Aspect, HudAnchor, Anisotropy, Mipmaps, PerPixel, Ao, NeighbourBlend, Shading, Perspective, Subpixel, ZBuffer, MenuWorld,
        Pacing, FrameRate, SmoothCamera, SmoothModels, TexScroll,
        Fog, RenderDist, RenderFade, Planar, MurkyWater, WaterWaves, AoNormals, AoQuality, EnhanceDistance,
        AutoReloadOn, AutoReloadSlot, MessageFade, MouseLook, InstantMouseLook,
    ];

    /// <summary>Every setting and every combined row, for the shell's <c>settings</c> verb.</summary>
    public static IEnumerable<PortSetting> Listed => All.Concat(Menu.Where(s => s.Parts is not null));

    public static IEnumerable<PortSetting> OnPage(string page) => Menu.Where(s => s.Page == page);

    public static PortSetting? Find(string key) =>
        Listed.FirstOrDefault(s => string.Equals(s.Key, key, StringComparison.OrdinalIgnoreCase));

    static bool _installed;

    /// <summary>After every feature's Install, so the boot check runs after their own
    /// start-up has read the saved values.</summary>
    public static void Install()
    {
        if (_installed) return;
        _installed = true;
        Validate();
        SettingsStore.Install();
        Event.AddListener<RuntimeReadyEvent>(_ => CheckDefaults());
    }

    /// <summary>A mistake in the list is the programmer's, so it stops the boot.</summary>
    static void Validate()
    {
        var problems = new List<string>();
        foreach (var dup in Listed.GroupBy(s => s.Key, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            problems.Add($"{dup.Key} declared {dup.Count()} times");
        foreach (var page in Pages)
        {
            if (MenuFont.Check($"{page} 9/9", SettingsPage.LabelChars) is { } why) problems.Add($"page {page}: {why}");
            if (OnPage(page).Count() is var n && (n == 0 || n > MaxRows)) problems.Add($"page {page}: {n} rows, 1 to {MaxRows}");
        }
        foreach (var s in Listed)
        {
            if (s.Page is null) continue;
            if (!Menu.Contains(s)) problems.Add($"{s.Key}: has a page but is not in Menu");
            if (!Pages.Contains(s.Page)) problems.Add($"{s.Key}: page {s.Page} is not in Pages");
            if ((s.MenuLabel is null ? "missing" : MenuFont.Check(s.MenuLabel, SettingsPage.LabelChars)) is { } why)
                problems.Add($"{s.Key}: label {s.MenuLabel}: {why}");
            foreach (double v in s.Steps)
                if (MenuFont.Check(s.MenuValue(v), SettingsPage.ValueChars) is { } bad) problems.Add($"{s.Key}: value {s.MenuValue(v)}: {bad}");
        }
        if (problems.Count > 0)
            throw new InvalidOperationException("port settings: " + string.Join("; ", problems));
    }

    /// <summary>A setting with no key and no variable must boot at its declared default,
    /// or a reset would not put back what the player had.</summary>
    static void CheckDefaults()
    {
        int kept = 0, wrong = 0;
        foreach (var s in All)
        {
            if (Rt.View.Has(s.Key)) { kept++; continue; }
            if (s.LockedBy is not null || PortSetting.Same(s.Live(), s.Default)) continue;
            wrong++;
            Console.WriteLine($"[KF3] settings: {s.Key} boots at {Number(s.Live())}, declared default {Number(s.Default)}");
        }
        Console.WriteLine($"[KF3] settings: {All.Length} declared on {Pages.Length} pages, {kept} kept in interface.ini, {wrong} default(s) wrong");
    }

    // ---- The Settings window's controls ----

    /// <summary>One row of a Settings tab. A slider applies while it is dragged and
    /// is written once, when it is let go.</summary>
    public static void Draw(PortSetting s)
    {
        ImGui.BeginDisabled(!s.IsUsable);
        string label = s.ImGuiLabel;
        switch (s.Ui)
        {
            case Ui.Checkbox:
            {
                bool on = s.Live() != 0;
                if (ImGui.Checkbox(label, ref on)) { s.Apply(on ? 1 : 0); SettingsStore.Write(s, s.Live()); }
                break;
            }
            case Ui.Combo:
            {
                string[] names = [.. (s.Names ?? []).Select(n => s.Localized ? RecompOne.Runtime.Host.Window.Localization.T(n) : n)];
                int i = s.StepIndex(s.Live());
                if (i < 0) { names = [.. names, $"Custom ({Number(s.Live())})"]; i = names.Length - 1; }
                ImGui.SetNextItemWidth(260);
                if (ImGui.Combo(label, ref i, names, names.Length) && i < s.Steps.Length)
                {
                    s.Apply(s.Steps[i]);
                    SettingsStore.Write(s, s.Live());
                }
                break;
            }
            case Ui.SliderInt:
            {
                int n = (int)Math.Round(s.Live());
                ImGui.SetNextItemWidth(260);
                if (ImGui.SliderInt(label, ref n, (int)s.Min, (int)s.Max, s.SliderText?.Invoke(n) ?? "%d")) s.Apply(n);
                if (ImGui.IsItemDeactivatedAfterEdit()) SettingsStore.Write(s, s.Live());
                break;
            }
            case Ui.SliderFloat:
            {
                float f = (float)s.Live();
                ImGui.SetNextItemWidth(260);
                if (ImGui.SliderFloat(label, ref f, (float)s.Min, (float)s.Max, s.SliderText?.Invoke(f) ?? "%.1f")) s.Apply(f);
                if (ImGui.IsItemDeactivatedAfterEdit()) SettingsStore.Write(s, s.Live());
                break;
            }
        }
        if (s.Tip is { } tip && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip(tip);
        ImGui.EndDisabled();
    }
}
