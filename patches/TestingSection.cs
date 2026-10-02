using ImGuiNET;
using RecompOne.Runtime.Events;
using RecompOne.Runtime.Host.Window;
using Rt = RecompOne.Runtime.Runtime;

namespace Kf3;

/// <summary>
/// A settings tab of the port's own, **Testing**: every switch the port has, live,
/// so a change can be compared without a restart. Each control writes the same
/// state its `KF3_*` variable sets at boot.
///
/// The on/off choices and the frame rate are kept in interface.ini (`kf3.*`) and
/// put back at the next boot, **unless the variable is set**, which wins. The
/// routines' recompiled/C#/verify choice is not kept: verify is a comparison for
/// one session. See "The Testing tab" in docs/DEVELOPMENT.md.
/// </summary>
public sealed class TestingSection : ISettingsSection
{
    public string Id => "kf3testing";
    public string TitleKey => "settings.kf3testing";

    // After Audio (10), before Paths (20).
    public int Order => 15;

    const string Names = """
    {
      "strings": {
        "settings.kf3testing": { "en": "Testing", "pt-BR": "Testes", "es-419": "Pruebas" }
      }
    }
    """;

    static bool _installed;

    // A kept setting: its key, its variable, how to read it and how to apply it.
    sealed record Kept(string Key, string Env, Func<bool> Get, Action<bool> Set);

    static readonly Kept[] Switches =
    [
        new("kf3.pacing", "KF3_FPS", () => FramePacing.Enabled, FramePacing.SetEnabled),
        new("kf3.smooth", "KF3_SMOOTH", () => ViewSmoothing.Enabled, v => ViewSmoothing.Enabled = v),
        new("kf3.smooth_models", "KF3_SMOOTH_MODELS", () => ModelSmoothing.Enabled, v => ModelSmoothing.Enabled = v),
        new("kf3.needle_hold", "KF3_STAGE15_NEEDLE", () => Stage15.NeedleHeld, v => Stage15.NeedleHeld = v),
        new("kf3.sprite_hold", "KF3_SPRITEANIM", () => SpriteAnim.Enabled, v => SpriteAnim.Enabled = v),
    ];

    const string FpsKey = "kf3.fps", TexKey = "kf3.texscroll";

    public static void Install()
    {
        if (_installed) return;
        _installed = true;
        Event.AddListener<RuntimeReadyEvent>(_ => Ready());
    }

    /// <summary>The config is loaded inside the host window's start-up, after Program.cs,
    /// so kept values go back here; a set variable is left as it chose.</summary>
    static void Ready()
    {
        Localization.Merge(Names);
        SettingsRegistry.Register(new TestingSection());

        if (Unset("KF3_FPS"))
        {
            int fps = Rt.View.GetInt(FpsKey, -1);
            FramePacing.SetTarget(fps >= 0 ? fps : FramePacing.DefaultFps);
        }
        foreach (var k in Switches)
            if (Unset(k.Env) && Rt.View.GetInt(k.Key, -1) is >= 0 and var v) k.Set(v != 0);
        if (Unset("KF3_TEXSCROLL") && Rt.View.GetInt(TexKey, -1) is >= 0 and var t) TextureScroll.Setting = t;
    }

    static bool Unset(string env) => string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(env));

    static void Keep(string key, int value)
    {
        Rt.View.SetInt(key, value);
        Rt.SaveView();
    }

    static void Toggle(string label, Kept k, string tip)
    {
        bool v = k.Get();
        if (ImGui.Checkbox(label, ref v))
        {
            k.Set(v);
            Keep(k.Key, k.Get() ? 1 : 0);
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(tip);
    }

    static Kept K(string key) => Switches.First(s => s.Key == key);

    static readonly string[] Routine = ["Recompiled", "C#", "Verify"];

    static void RoutineCombo(string label, Func<int> get, Action<int> set, string tip)
    {
        int v = get();
        ImGui.SetNextItemWidth(160);
        if (ImGui.Combo(label, ref v, Routine, Routine.Length)) set(v);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(tip);
    }

    // The readout, measured from the frame and tick counters every half second.
    static long _frames0, _ticks0;
    static double _at0 = -1.0, _fps, _tps;

    static void Rates()
    {
        double now = Environment.TickCount64 / 1000.0;
        if (_at0 < 0.0 || now - _at0 > 2.0) { _at0 = now; _frames0 = FramePacing.Frames; _ticks0 = FramePacing.Ticks; return; }
        if (now - _at0 < 0.5) return;
        _fps = (FramePacing.Frames - _frames0) / (now - _at0);
        _tps = (FramePacing.Ticks - _ticks0) / (now - _at0);
        _at0 = now; _frames0 = FramePacing.Frames; _ticks0 = FramePacing.Ticks;
    }

    static void Note(string text)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]);
        ImGui.TextWrapped(text);
        ImGui.PopStyleColor();
    }

    public void Draw()
    {
        ImGui.SeparatorText("Frame pacing");
        Toggle("Frame pacing", K("kf3.pacing"),
            "Runs the world at its own 15 ticks a second and draws as often as the rate below.");

        bool uncapped = FramePacing.TargetFps <= 0.0;
        if (!FramePacing.Enabled) ImGui.BeginDisabled();
        int fps = uncapped ? (int)FramePacing.DefaultFps : (int)FramePacing.TargetFps;
        if (uncapped) ImGui.BeginDisabled();
        ImGui.SetNextItemWidth(240);
        if (ImGui.SliderInt("Frame rate", ref fps, 30, 360))
        {
            FramePacing.SetTarget(fps);
            Keep(FpsKey, fps);
        }
        if (uncapped) ImGui.EndDisabled();
        ImGui.SameLine();
        if (ImGui.Checkbox("Uncapped", ref uncapped))
        {
            FramePacing.SetTarget(uncapped ? 0 : fps);
            Keep(FpsKey, uncapped ? 0 : fps);
        }
        if (FramePacing.Enabled) { Rates(); Note($"Drawing {_fps:0.0} fps at {_tps:0.0} ticks a second."); }
        if (!FramePacing.Enabled) ImGui.EndDisabled();

        ImGui.SeparatorText("Smoothing");
        if (!FramePacing.Enabled)
        {
            Note("Everything here acts only with frame pacing on.");
            ImGui.BeginDisabled();
        }
        Toggle("Camera, compass needle and gauges", K("kf3.smooth"),
            "Draws the view, the needle and the HP/MP bars between the world's ticks.");
        Toggle("Creatures and objects", K("kf3.smooth_models"),
            "Draws creatures, objects, effects and their animation between the world's ticks.");
        if (ModelSmoothing.Enabled && ModelWalk.Setting != 1)
            Note("Needs the model walk in C#.");
        else if (ModelSmoothing.Enabled && MoPose.Setting != 1)
            Note("Positions only: the animation needs the MO pose blender in C#.");

        Toggle("Hold the compass needle's spring to the tick", K("kf3.needle_hold"),
            "Off lets the needle swing at the drawn rate, as the game would.");
        Toggle("Hold billboard animation to the tick", K("kf3.sprite_hold"),
            "Off lets the sprites animate at the drawn rate, as the game would.");

        string[] tex = ["Every drawn frame", "Held to the tick", "Carried between ticks"];
        int t = TextureScroll.Setting;
        ImGui.SetNextItemWidth(200);
        if (ImGui.Combo("Scrolling textures", ref t, tex, tex.Length))
        {
            TextureScroll.Setting = t;
            Keep(TexKey, t);
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Every drawn frame is the game's own code, too fast above 15 fps.");
        if (!FramePacing.Enabled) ImGui.EndDisabled();

        ImGui.SeparatorText("Routines in C#");
        Note("Verify runs both versions every call and prints mismatches to the console; it is slow.");
        RoutineCombo("Stage 15", () => Stage15.Setting, v => Stage15.Setting = v,
            "The frame builder. Smoothing needs it in C#.");
        RoutineCombo("Camera block", () => CameraBlock.Setting, v => CameraBlock.Setting = v,
            "The view matrices from the camera.");
        RoutineCombo("Polygon assemblers", () => PolyAssembler.Setting, v => PolyAssembler.Setting = v,
            "The map's and the models' bulk polygons.");
        RoutineCombo("Model walk", () => ModelWalk.Setting, v => ModelWalk.Setting = v,
            "Creatures, objects, effects and billboards. Model smoothing needs it in C#.");
        RoutineCombo("MO pose blender", () => MoPose.Setting, v => MoPose.Setting = v,
            "Animated models' poses. Animation smoothing needs it in C#.");

        ImGui.SeparatorText("Console probes");
        bool p = FramePacing.ProbeOn;
        if (ImGui.Checkbox("Pacing (KF3_FPS_PROBE)", ref p)) FramePacing.ProbeOn = p;
        bool s = ViewSmoothing.ProbeOn;
        if (ImGui.Checkbox("Smoothing (KF3_SMOOTH_PROBE)", ref s)) { ViewSmoothing.ProbeOn = s; ModelSmoothing.ProbeOn = s; }
    }
}
