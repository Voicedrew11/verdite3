// ModCompiler compiles mods with no implicit usings, so every namespace the
// file needs must be named here -- including System.
using System;
using ImGuiNET;
using RecompOne.Runtime.Context;
using RecompOne.Runtime.Events;
using RecompOne.Runtime.Host.Window;
using RecompOne.Runtime.Memory;
using RecompOne.Runtime.Modding;

namespace Kf3.Mods.Debug;

/// <summary>
/// Debug tools: noclip flight, invincibility, infinite MP, a speed multiplier,
/// position bookmarks, area warp, editors for the character's attributes and the
/// inventory and equipment, and the spell book, with a live readout of the player
/// state.
///
/// Ported from the KF2 reference DebugMod.cs; every address and fact lives in the
/// feature files, and those are this game's (scratch/findings/*). Everything here
/// is off until it is turned on, and every hook returns on a bool test plus one
/// memory read when it is off.
///
/// The state this reads is not new. docs/GAME_INTERNALS.md "The player" gives the
/// block GameState.cs names, and the feature files give the rest; what this mod
/// adds is a place to see them and a set of switches.
/// </summary>
public sealed class DebugMod : IMod
{
    const string NoclipSpeedKey = "kf3.debug.noclip.speed";
    const string NoclipFastKey  = "kf3.debug.noclip.fast";
    const string InvertYKey     = "kf3.debug.noclip.inverty";
    const string InvertSKey     = "kf3.debug.noclip.invertstrafe";
    const string SpeedMultKey   = "kf3.debug.speed.multiplier";
    internal const string FlyAfterWarpKey = "kf3.debug.warp.fly";
    const string HotkeysKey     = "kf3.debug.hotkeys";

    // The panel has to be registered from the UI thread. On first boot OnLoad
    // runs on a worker while the main thread pumps the loading popup, so
    // registration is deferred to the first frame instead.
    static bool _panelRegistered;
    static Action<VSyncEvent>? _onVSync;

    public void OnLoad()
    {
        var view = RecompOne.Runtime.Runtime.View;

        Noclip.Speed           = view.GetFloat(NoclipSpeedKey, 7000f);
        Noclip.FastMultiplier  = view.GetFloat(NoclipFastKey, 4f);
        Noclip.InvertVertical  = view.GetBool(InvertYKey, false);
        Noclip.InvertStrafe    = view.GetBool(InvertSKey, false);
        Cheats.SpeedMultiplier = view.GetFloat(SpeedMultKey, 2f);
        Hotkeys.Enabled        = view.GetBool(HotkeysKey, true);
        Warp.FlyAfterWarp      = view.GetBool(FlyAfterWarpKey, true);

        ReadEnv("KF3_DEBUG_NOCLIP", ref Noclip.Enabled);
        ReadEnv("KF3_DEBUG_GODMODE", ref Cheats.Invincible);
        ReadEnv("KF3_DEBUG_INFINITEMP", ref Cheats.InfiniteMp);
        ReadEnv("KF3_DEBUG_PEACEFUL", ref Cheats.Peaceful);
        ReadEnv("KF3_DEBUG_HOTKEYS", ref Hotkeys.Enabled);
        ReadEnv("KF3_DEBUG_NOCLIP_SPEED", ref Noclip.Speed);
        ReadEnv("KF3_DEBUG_SPEED", ref Cheats.SpeedMultiplier);

        Warp.LoadPersisted();

        _onVSync = _ => RegisterPanel();
        Event.AddListener(_onVSync);

        Console.WriteLine("[kf3debug] loaded; F2 opens the panel, F3 noclip, F4 invincibility");
    }

    public void OnUnload()
    {
        if (_onVSync != null)
        {
            // Listeners are static and outlive the collectible load context, so a
            // missed removal leaks a handler into a dead assembly.
            Event.RemoveListener(_onVSync);
            _onVSync = null;
        }

        // Leave nothing switched on behind us. The speed multiplier in
        // particular is applied to a word the game rewrites every frame, so it
        // needs no restore -- but the flags do, or a reload comes back flying.
        Noclip.Reset();
        Cheats.Reset();
        Hotkeys.Reset();
        Warp.Reset();
        Attributes.Reset();
        Items.Reset();
        Magic.Reset();

        DebugPanel.Instance.IsOpen = false;
        _panelRegistered = false;

        Console.WriteLine("[kf3debug] unloaded");
    }

    /// <summary>
    /// Register the dockable panel and its menu entry, once, on the UI thread.
    ///
    /// Panels do not auto-populate the menu bar -- MainMenuBar declares every
    /// built-in one by hand -- so without the menu entry the panel would be
    /// reachable only by hotkey.
    /// </summary>
    static void RegisterPanel()
    {
        if (_panelRegistered) return;
        _panelRegistered = true;

        foreach (var p in PanelManager.Panels)
            if (ReferenceEquals(p, DebugPanel.Instance)) return;

        PanelManager.Register(DebugPanel.Instance);
        // Anchored after the runtime's own Debug menu, where this sat when the
        // order was a number.
        MenuRegistry.Menu("menu.debug")
                    .Panel<DebugPanel>("KF3 Debug")
                    .End();
    }

    /// <summary>
    /// End of main-loop stage 4, the player's tick (GameState.PlayerStage,
    /// 0x80030FCC). One poll a frame for the hotkeys; the features themselves
    /// hook from their own files. The KF2 reference polled its stage 3; this
    /// game's findings put the player here, and the features all already post
    /// here, so the poll rides the same tick.
    /// </summary>
    [PostHook("game", Address = GameState.PlayerStage)]
    static void AfterPlayerStage(CpuContext c, IMemory m) => Hotkeys.Poll();

    public void DrawSettings()
    {
        ImGui.TextWrapped("Noclip flight, invincibility, infinite MP, enemies that ignore you, a "
                        + "speed multiplier, position bookmarks, area warp and editors for the "
                        + "character's attributes, the "
                        + "inventory and equipment and the spell book, plus a live readout of the "
                        + "player state. Everything is off until you switch it on.");
        ImGui.Separator();

        if (ImGui.Button("Open the debug panel"))
        {
            RegisterPanel();
            DebugPanel.Instance.IsOpen = true;
        }
        ImGui.SameLine();
        ImGui.TextDisabled("or press F2");

        ImGui.Separator();
        ImGui.TextWrapped("Settings that persist live here; the switches live in the panel, since "
                        + "they are things you flip while playing.");

        if (ImGui.SliderFloat("Noclip speed", ref Noclip.Speed, 20f, 8000f, "%.0f"))
            Persist(NoclipSpeedKey, Noclip.Speed);
        if (ImGui.SliderFloat("Noclip fast multiplier", ref Noclip.FastMultiplier, 1f, 10f, "x%.1f"))
            Persist(NoclipFastKey, Noclip.FastMultiplier);
        if (ImGui.SliderFloat("Speed multiplier", ref Cheats.SpeedMultiplier, 0.25f, 8f, "x%.2f"))
            Persist(SpeedMultKey, Cheats.SpeedMultiplier);

        bool invY = Noclip.InvertVertical;
        if (ImGui.Checkbox("Invert noclip up/down", ref invY))
        {
            Noclip.InvertVertical = invY;
            Persist(InvertYKey, invY);
        }

        bool invS = Noclip.InvertStrafe;
        if (ImGui.Checkbox("Invert noclip strafe", ref invS))
        {
            Noclip.InvertStrafe = invS;
            Persist(InvertSKey, invS);
        }

        bool keys = Hotkeys.Enabled;
        if (ImGui.Checkbox("Hotkeys enabled", ref keys))
        {
            Hotkeys.Enabled = keys;
            Persist(HotkeysKey, keys);
        }
    }

    static void Persist(string key, float value)
    {
        RecompOne.Runtime.Runtime.View.SetFloat(key, value);
        RecompOne.Runtime.Runtime.SaveView();
    }

    internal static void Persist(string key, bool value)
    {
        RecompOne.Runtime.Runtime.View.SetBool(key, value);
        RecompOne.Runtime.Runtime.SaveView();
    }

    static void ReadEnv(string name, ref bool value)
    {
        string? v = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(v)) return;
        v = v.Trim().ToLowerInvariant();
        value = v is "1" or "on" or "true" or "yes";
    }

    static void ReadEnv(string name, ref float value)
    {
        string? v = Environment.GetEnvironmentVariable(name);
        if (float.TryParse(v, System.Globalization.NumberStyles.Float,
                           System.Globalization.CultureInfo.InvariantCulture, out float f))
            value = f;
    }
}
