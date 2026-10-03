using System.Numerics;
using ImGuiNET;
using RecompOne.Runtime.Config;
using RecompOne.Runtime.Events;
using RecompOne.Runtime.Host;
using RecompOne.Runtime.Host.Window;

namespace Kf3;

/// <summary>
/// **The Input pane, drawn by the port rather than extended by it.**
/// Ported from Verdite2's Kf2.Settings.InputSection; MapButtonPage is dropped,
/// because this game has no map button, and the page classes are the flat
/// statics under <c>patches/</c>.
///
/// The runtime's own pane is two tab bars, a sixteen-row binding table and a
/// reset button — about 450px of a 500px popup — so the port's pages would land
/// below the fold, and the two keyboard-layout buttons write a table a screen
/// above them. See "The Input pane is the port's" in docs/INPUT.md.
///
/// **`Register` replaces by id, and that is the whole mechanism.**
/// <c>SettingsRegistry.Register</c> does <c>RemoveAll(s =&gt; s.Id == section.Id)</c>
/// and then adds, so registering this on <c>RuntimeReadyEvent</c> — after
/// <c>HostWindow.Load</c> has registered the runtime's five — takes the pane
/// over. Do **not** <c>Unregister("input")</c> first: it states removal where the
/// intent is substitution, and it hides the one failure that matters — if
/// upstream ever renames the id, an unregister no-ops silently and the register
/// adds a *second* Input tab. <see cref="Install"/> warns on that directly.
///
/// <c>IPatchPage.Order</c> and <c>Title</c> are not needed here: the sequence is
/// written out in the draw methods below, so there is no list to sort.
/// </summary>
public sealed class InputSection : ISettingsSection
{
    /// <summary>The runtime's own id: this replaces its section rather than
    /// joining the sidebar beside it.</summary>
    public string Id => "input";

    /// <summary>The runtime's own key too — "Input", "Controles", "Controles" —
    /// so nothing is re-translated and the sidebar entry does not move.</summary>
    public string TitleKey => "settings.input";

    /// <summary>The runtime's own order, so the sidebar keeps its shape:
    /// interface -10, input 0, display 5, gameplay 7, audio 10, paths 20.</summary>
    public int Order => 0;

    // One capture per device rather than one index shared by both. The runtime
    // had a single _remapRow serving two devices and two pad slots and had to
    // clear it in four places; two fields make switching tab mid-capture a
    // non-event by construction instead of by remembering.
    int _keyRow = -1;
    int _padRow = -1;
    bool _padAdd;

    /// <summary>"Mouse" is the one label the runtime's table has no key for, so
    /// the port supplies all three of its languages rather than hardcode it.</summary>
    const string Names = """
    {
      "strings": {
        "settings.input.mouse": {
          "en": "Mouse",
          "pt-BR": "Mouse",
          "es-419": "Ratón"
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

            // Input is a *replacement*, not an addition: Register removes by id,
            // so this takes the runtime's own pane over. If upstream ever renames
            // that id we would silently add a second Input tab rather than
            // replacing the first, which is the one failure worth naming.
            if (!SectionExists("input"))
                Console.Error.WriteLine("[KF3] settings: no \"input\" section to replace; " +
                                        "the port's Input pane will be a second tab");

            SettingsRegistry.Register(new InputSection());
        });
    }

    static bool SectionExists(string sectionId)
    {
        foreach (var section in SettingsRegistry.Sections)
            if (string.Equals(section.Id, sectionId, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    public void Draw()
    {
        if (!ImGui.BeginTabBar("##kf3-input")) return;

        Tab(Localization.T("settings.input.keyboard"), DrawKeyboard);
        Tab(Localization.T("settings.input.gamepad"), DrawGamepad);
        Tab(Localization.T("settings.input.mouse"), DrawMouse);

        ImGui.EndTabBar();
    }

    /// <summary>
    /// One tab, with its body in a scrolling child of its own.
    ///
    /// **The tab bar has to stay out of the scroll.** The table alone is about
    /// 490px against roughly 365px of tab body in a 500px popup, so every tab
    /// scrolls whatever is above it. Drawn in the flow of the settings content
    /// child, the bar would scroll off the top with everything else. The child
    /// takes the remaining height and owns the scrollbar; the padding push is
    /// around <c>BeginChild</c> only, so the body is not inset a second time
    /// inside a content child that has already padded it.
    /// </summary>
    static void Tab(string title, Action body)
    {
        if (!ImGui.BeginTabItem(title)) return;

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        bool open = ImGui.BeginChild("##body", Vector2.Zero, ImGuiChildFlags.None);
        ImGui.PopStyleVar();

        if (open) body();

        ImGui.EndChild();
        ImGui.EndTabItem();
    }

    void DrawKeyboard()
    {
        KeyLayoutPage.Draw();
        ImGui.Spacing();

        BindingTable.Draw(gamepad: false, ref _keyRow, ref _padAdd);
        ActionNote();

        ImGui.Spacing();

        // KeyLayout.ApplyStock, not `Keys = new KeyBindings()` as the runtime
        // did: the same object, plus the marker that stops KeyLayout.Install's
        // next-launch migration putting the port's layout back over it. The
        // "RecompOne layout" button above does exactly this, and two buttons that
        // agree on screen have to agree in code.
        if (ImGui.Button(Localization.T("settings.input.reset_defaults")))
        {
            KeyLayout.ApplyStock();
            _keyRow = -1;
        }
    }

    void DrawGamepad()
    {
        // Above the pages, not below the table where the runtime had it: this is
        // the answer to "why is none of this doing anything", so it has to be met
        // before the twin-stick block rather than after the sixteen rows.
        if (!HostWindow.IsPadConnected(0))
        {
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0.75f, 0.3f, 1f));
            ImGui.TextWrapped(Localization.T("settings.input.no_gamepad"));
            ImGui.PopStyleColor();
            ImGui.Spacing();
        }

        AnalogPage.Draw();
        ImGui.Spacing();

        BindingTable.Draw(gamepad: true, ref _padRow, ref _padAdd);
        ActionNote();

        ImGui.Spacing();

        // Nothing migrates pad bindings, so this is the plain reset the runtime
        // had. Pad2 is deliberately left alone — see BindingTable.
        if (ImGui.Button(Localization.T("settings.input.reset_defaults")))
        {
            ConfigManager.Game.Pad = new GamepadBindings();
            ConfigManager.SaveGame();
            _padRow = -1;
        }
    }

    void DrawMouse() => MousePage.Draw();

    /// <summary>The qualification the action column needs, said once under the
    /// table rather than sixteen times in it. See <see cref="BindingTable"/>.</summary>
    static void ActionNote() =>
        Note("That middle column is what the buttons do in a New Game. King's Field has a control " +
             "configuration screen of its own: it swaps attack with magic and the menu with examine, " +
             "and its direction presets rewrite the movement buttons.");

    /// <summary>A dimmed, wrapped line under a control: TextDisabled does not wrap
    /// and unwrapped prose runs out of the settings window.</summary>
    static void Note(string text)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]);
        ImGui.TextWrapped(text);
        ImGui.PopStyleColor();
    }
}
