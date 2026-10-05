using ImGuiNET;
using RecompOne.Runtime.Events;
using RecompOne.Runtime.Host.Window;
using Rt = RecompOne.Runtime.Runtime;

namespace Kf3;

/// <summary>
/// A settings tab of the port's own: **Gameplay**, holding the rules of play.
/// Ported from Verdite2's Kf2.Settings.GameplaySection.
///
/// The runtime's five sections are all about the machine, and none of them is a
/// place to put a change to how the *game* behaves. It is registered through
/// <c>SettingsRegistry.Register</c>, so it needs no patch to the RecompOne
/// checkout, and it draws its own content directly rather than extending another
/// section.
///
/// The mouse options moved under Input, to <see cref="MousePage"/>; only
/// "Instant mouse look" stays here, as Verdite2's Gameplay keeps its own
/// MouseLeadPage.
///
/// <see cref="Order"/> 7 puts the tab between Video (5) and Audio (10), so the
/// runtime's own sections keep their order.
/// </summary>
public sealed class GameplaySection : ISettingsSection
{
    public string Id => "gameplay";

    /// <summary>Merged for all three of the runtime's languages — a key with only
    /// English behind it makes the other two warn on every miss and then show the
    /// key itself.</summary>
    public string TitleKey => "settings.gameplay";

    public int Order => 7;

    const string Names = """
    {
      "strings": {
        "settings.gameplay": {
          "en": "Gameplay",
          "pt-BR": "Jogabilidade",
          "es-419": "Jugabilidad"
        }
      }
    }
    """;

    static bool _installed;

    public static void Install()
    {
        if (_installed) return;
        _installed = true;
        Event.AddListener<RuntimeReadyEvent>(_ =>
        {
            Localization.Merge(Names);
            SettingsRegistry.Register(new GameplaySection());
        });
    }

    // interface.ini, saved on the spot: a setting is changed once and then the
    // player goes back to the game, and there is no later moment to write it.

    static void Set(string key, bool value)
    {
        Rt.View.SetInt(key, value ? 1 : 0);
        Rt.SaveView();
    }

    public void Draw()
    {
        // Dimmed rather than hidden while mouse look is off, as Verdite2's
        // AutoReloadPage dims its slot.
        ImGui.BeginDisabled(!Mouse.Enabled);

        bool lead = Mouse.Lead;
        if (ImGui.Checkbox("Instant mouse look", ref lead))
        {
            Mouse.Lead = lead;
            Set(Mouse.LeadKey, lead);
        }
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip("Turns the view the frame you move the mouse, instead of on the game's next tick.");

        ImGui.EndDisabled();

        DrawAutoReload();
    }

    static readonly string[] Slots = ["Last used", "Slot 1", "Slot 2", "Slot 3", "Slot 4", "Slot 5"];

    /// <summary>Verdite2's AutoReloadPage: the switch, and the slot dimmed and
    /// indented under it while it is off.</summary>
    static void DrawAutoReload()
    {
        bool on = AutoReload.Enabled;
        if (ImGui.Checkbox("Reload the last save on death", ref on))
        {
            AutoReload.SetEnabled(on);
            Set(AutoReload.OnKey, on);
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Puts you back at your last save instead of the menus.");

        ImGui.Indent();
        ImGui.BeginDisabled(!AutoReload.Enabled);

        int slot = AutoReload.Slot;
        ImGui.SetNextItemWidth(260);
        if (ImGui.Combo("Save slot", ref slot, Slots, Slots.Length))
        {
            AutoReload.SetSlot(slot);
            Rt.View.SetInt(AutoReload.SlotKey, AutoReload.Slot);
            Rt.SaveView();
        }
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip("Which save to reload. \"Last used\" follows where you saved or loaded.");

        ImGui.EndDisabled();
        ImGui.Unindent();
    }
}
