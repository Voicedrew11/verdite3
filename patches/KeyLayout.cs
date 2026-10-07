using RecompOne.Runtime.Config;
using RecompOne.Runtime.Events;
using RecompOne.Runtime.Hardware;
using RecompOne.Runtime.Host.Window;
using Silk.NET.Input;
using Rt = RecompOne.Runtime.Runtime;

namespace Kf3;

/// <summary>
/// The keyboard layout the port ships, which is not the one RecompOne ships.
/// Ported from Verdite2's Kf2.KeyLayout, with this game's actions.
///
///     KF3_KEYS=fps      force this layout on (the default for a fresh install)
///     KF3_KEYS=stock    leave RecompOne's Z X A S Q W E R F G alone
///
/// RecompOne's defaults are a *console's* defaults spelled on a keyboard — the
/// face buttons on Z X A S, the shoulders on Q W E R, the D-pad on the arrows.
/// That is the right generic answer for a machine that has to run any PS1 game,
/// and it is the wrong answer for a game whose D-pad walks *and turns*.
///
/// Two things about how this is applied are deliberate.
///
/// **A fresh install gets it as a default, not as an override.**
/// <see cref="Configure"/> runs from Program.cs, *before* ConfigManager.Load, and
/// Load writes the object it finds in memory when there is no settings.json to
/// read. So on a first run this is simply what the port's defaults are, and on
/// every run after it the player's own file wins.
///
/// **An existing install is migrated once, and only from stock.** Anyone who
/// already ran the port has a settings.json full of RecompOne's defaults, and a
/// default that only reaches new installs is not much of a default.
/// <see cref="Install"/> therefore rewrites those bindings once — but only if
/// they are *exactly* the stock ones, so a single key someone chose for
/// themselves stops it, and it records that it has run so that deliberately
/// going back to stock is not undone on the next launch.
///
/// The marker lives in interface.ini rather than in settings.json, because
/// settings.json is the thing being migrated and a marker inside it would need
/// the runtime's schema to grow a field.
/// </summary>
public static class KeyLayout
{
    /// <summary>Which version of the layout the config has been migrated to.</summary>
    public const string AppliedKey = "kf3.keys.layout";

    const int Version = 3;

    /// <summary>
    /// Layouts this port has shipped before and has since changed its mind about.
    ///
    /// The migration only rewrites bindings it recognises — stock, or one of
    /// these — because anything else is a choice someone made. That means a change
    /// to <see cref="Layout"/> after release reaches nobody unless the layout it
    /// replaces is recorded here and <see cref="Version"/> is bumped: without both,
    /// an existing config reads as customised and is left alone forever.
    /// </summary>
    static readonly KeyBindings[] Superseded =
    [
        // Version 1 (2026-10-02): the menu and examine crossed, F opening the menu.
        new()
        {
            Up = "W", Down = "S", L1 = "A", R1 = "D",
            Left = "Left", Right = "Right", L2 = "", R2 = "",
            Triangle = "Space", Circle = "F", Square = "Q", Cross = "Tab",
            Start = "Enter", Select = "ShiftRight", L3 = "", R3 = "",
        },
        // Version 2 (2026-10-03): the menu and examine still crossed.
        new()
        {
            Up = "W", Down = "S", L1 = "A", R1 = "D",
            Left = "Left", Right = "Right", L2 = "", R2 = "",
            Square = "Space", Circle = "F", Triangle = "Q", Cross = "Tab",
            Start = "Enter", Select = "ShiftRight", L3 = "", R3 = "",
        },
    ];

    /// <summary>
    /// W A S D and the rest. Only the sixteen pad buttons exist, so this says
    /// which *key* presses each one; what the button then does is the game's own
    /// control configuration, exactly as it is for a pad.
    /// </summary>
    // The actions are a New Game's, the game's face preset 3 (func_8002B64C):
    // see "The pad and the action-mask table" in docs/INPUT.md.
    public static KeyBindings Layout() => new()
    {
        // Move. The strafes are on the shoulder buttons in this game, which is
        // what lets A and D strafe rather than turn.
        Up = "W",
        Down = "S",
        L1 = "A",
        R1 = "D",

        // Turn. Left and Right stay on the arrows, where they have always been,
        // and the arrows go on walking too (see Extras).
        Left = "Left",
        Right = "Right",

        // Pitch is the mouse's, and only the mouse's.
        L2 = "",
        R2 = "",

        // Act: attack, examine, magic, the menu, the card and options menu, the map.
        Square = "Space",
        Cross = "F",
        Triangle = "Q",
        Circle = "Tab",
        Start = "Enter",
        Select = "ShiftRight",

        // The game reads neither.
        L3 = "",
        R3 = "",
    };

    /// <summary>
    /// Install this as the port's default bindings. **Must be called before
    /// ConfigManager.Load**, i.e. from Program.cs: Load either overwrites this
    /// object from settings.json or, when there is no such file, saves it — which
    /// is precisely the behaviour a default wants.
    /// </summary>
    public static void Configure() =>
        KeyLayoutApply.Configure(Layout, Version, Superseded, Announce, GetApplied, SetApplied);

    const string Announce = "WASD layout applied (W/S walk, A/D strafe, arrows walk and turn, " +
                            "Space attack, F examine, Q magic, Tab or Escape menu). Input settings has both layouts.";

    /// <summary>
    /// Migrate an existing settings.json, once, and only if nothing in it was
    /// chosen by hand.
    /// </summary>
    public static void Install()
    {
        KeyLayoutApply.Install();
        Event.AddListener<KeyboardEvent>(OnKey);
        Event.AddListener(_escape);
    }

    // ---- Escape, the menu's second key ----------------------------------------
    //
    // A desktop game's Escape opens its menu and backs out of it, and Circle does
    // both here: preset 3's menu button, and the cancel of every chooser. The
    // runtime's table holds one key a button and Tab has Circle, so Escape presses
    // it inside PAD_dr, as KeyLayoutApply's arrows press Up and Down. Only with
    // this layout in place, and only for a press that began over the game: a popup
    // closes on Escape, and the key still held after it closed is not a menu.

    static bool _escHeld;

    static void OnKey(KeyboardEvent e)
    {
        if (e.Key != (int)Key.Escape) return;
        _escHeld = e.Pressed && !PopupManager.AnyOpen && !MouseLook.Game.TextEditing() &&
                   Mouse.CaptureKey != Key.Escape;
    }

    static readonly Action<PadReadEvent> _escape = e =>
    {
        if (e.Port != 0 || !_escHeld || !IsApplied()) return;
        // Active low, the two button bytes swapped from Controller's layout.
        ushort bit = Controller.Circle;
        e.Buttons &= (ushort)~(ushort)((bit >> 8) | (bit << 8));
    };

    /// <summary>Write the layout and save it. What a settings button calls.</summary>
    public static void Apply() => KeyLayoutApply.Apply();

    /// <summary>Back to RecompOne's own scheme, and remember that it was asked
    /// for, so the migration above does not undo it on the next launch.</summary>
    public static void ApplyStock() => KeyLayoutApply.ApplyStock();

    public static bool IsApplied() => KeyLayoutApply.IsApplied();

    static int GetApplied() => Rt.View.GetInt(AppliedKey, 0);

    static void SetApplied(int version)
    {
        Rt.View.SetInt(AppliedKey, version);
        Rt.SaveView();
    }
}
