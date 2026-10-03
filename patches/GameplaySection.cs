using ImGuiNET;
using RecompOne.Runtime.Events;
using RecompOne.Runtime.Host.Window;
using Rt = RecompOne.Runtime.Runtime;

namespace Kf3;

/// <summary>
/// A settings tab of the port's own: **Gameplay**, holding the mouse look
/// options. Ported from Verdite2's Kf2.Settings.GameplaySection; the controls
/// here are the game-agnostic half of its Input pane, which was not portable
/// because it also carried the twin-stick and map-button pages.
///
/// The runtime's five sections are all about the machine, and none of them is a
/// place to put a change to how the *game* behaves. It is registered through
/// <c>SettingsRegistry.Register</c>, so it needs no patch to the RecompOne
/// checkout, and it draws its own content directly rather than extending another
/// section.
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

    static void Set(string key, int value)
    {
        Rt.View.SetInt(key, value);
        Rt.SaveView();
    }

    static void Set(string key, float value)
    {
        Rt.View.SetFloat(key, value);
        Rt.SaveView();
    }

    public void Draw()
    {
        ImGui.SeparatorText("Mouse look");

        bool on = Mouse.Enabled;
        if (ImGui.Checkbox("Mouse look", ref on))
        {
            Mouse.Enabled = on;
            Set(Mouse.OnKey, on ? 1 : 0);
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Steers with the mouse and presses pad buttons with its buttons.");

        ImGui.BeginDisabled(!Mouse.Enabled);

        float turn = Mouse.TurnSens;
        ImGui.SetNextItemWidth(200);
        if (ImGui.SliderFloat("Turn sensitivity", ref turn, 0.1f, 5f, "%.2f"))
        {
            Mouse.TurnSens = turn;
            Set(Mouse.TurnKey, turn);
        }

        float look = Mouse.LookSens;
        ImGui.SetNextItemWidth(200);
        if (ImGui.SliderFloat("Look sensitivity", ref look, 0.1f, 5f, "%.2f"))
        {
            Mouse.LookSens = look;
            Set(Mouse.LookKey, look);
        }

        bool invert = Mouse.InvertY;
        if (ImGui.Checkbox("Invert look Y", ref invert))
        {
            Mouse.InvertY = invert;
            Set(Mouse.InvertKey, invert ? 1 : 0);
        }

        string[] buttons = Mouse.PadButtons.Select(b => b.Name).ToArray();
        int left = Mouse.LeftButton;
        ImGui.SetNextItemWidth(160);
        if (ImGui.Combo("Left button", ref left, buttons, buttons.Length))
        {
            Mouse.LeftButton = left;
            Set(Mouse.LeftKey, left);
        }

        int right = Mouse.RightButton;
        ImGui.SetNextItemWidth(160);
        if (ImGui.Combo("Right button", ref right, buttons, buttons.Length))
        {
            Mouse.RightButton = right;
            Set(Mouse.RightKey, right);
        }

        int middle = Mouse.MiddleButton;
        ImGui.SetNextItemWidth(160);
        if (ImGui.Combo("Middle button", ref middle, buttons, buttons.Length))
        {
            Mouse.MiddleButton = middle;
            Set(Mouse.MiddleKey, middle);
        }

        string[] keys = Mouse.CaptureKeys.Select(k => k.ToString()).ToArray();
        int key = Array.IndexOf(Mouse.CaptureKeys, Mouse.CaptureKey);
        if (key < 0) key = 0;
        ImGui.SetNextItemWidth(160);
        if (ImGui.Combo("Capture key", ref key, keys, keys.Length))
        {
            Mouse.CaptureKey = Mouse.CaptureKeys[key];
            Set(Mouse.CaptureKeyKey, (int)Mouse.CaptureKey);
        }

        ImGui.Spacing();

        // Dimmed rather than hidden while mouse look is off, as Verdite2's
        // AutoReloadPage dims its slot.
        bool lead = Mouse.Lead;
        if (ImGui.Checkbox("Instant mouse look", ref lead))
        {
            Mouse.Lead = lead;
            Set(Mouse.LeadKey, lead ? 1 : 0);
        }
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip("Turns the view the frame you move the mouse, instead of on the game's next tick.");

        ImGui.EndDisabled();
    }
}
