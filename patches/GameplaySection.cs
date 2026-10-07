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
            MenuWorld.SetFadeVBlanks(int.TryParse(Environment.GetEnvironmentVariable("KF3_MESSAGE_FADE"), out int fade)
                ? fade : Rt.View.GetInt(MenuWorld.FadeKey, 1));
            SettingsRegistry.Register(new GameplaySection());
        });
    }

    // Each row is declared in PortSettings, which the game's own menu draws from
    // too; SettingsStore writes the one key a control changed (docs/SETTINGS.md).

    public void Draw()
    {
        // Dimmed rather than hidden while mouse look is off, as Verdite2's
        // AutoReloadPage dims its slot.
        PortSettings.Draw(PortSettings.InstantMouseLook);

        // Verdite2's AutoReloadPage: the switch, and the slot dimmed and indented
        // under it while it is off.
        PortSettings.Draw(PortSettings.AutoReloadOn);
        ImGui.Indent();
        PortSettings.Draw(PortSettings.AutoReloadSlot);
        ImGui.Unindent();

        // How long a sign's or a message's fade takes (MenuWorld.FadeVBlanks).
        PortSettings.Draw(PortSettings.MessageFade);

        // Turning a picked-up item (ItemTurn), the gyro dimmed under it while it is off.
        PortSettings.Draw(PortSettings.ItemTurnOn);
        ImGui.Indent();
        PortSettings.Draw(PortSettings.ItemTurnGyro);
        ImGui.Unindent();
    }
}
