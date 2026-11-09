using System.Reflection;
using RecompOne.Runtime.Context;
using RecompOne.Runtime.Events;
using RecompOne.Runtime.Hardware;
using RecompOne.Runtime.Host;
using RecompOne.Runtime.Memory;
using RecompOne.Runtime.Modding;
using Silk.NET.Input;

namespace Kf3;

/// <summary>
/// The Japanese release's buttons: Circle confirms and Cross cancels, whichever control
/// preset the save holds, in the menus and in play, where Circle examines and Cross opens
/// the menu. The Japanese program tests the menu's two buttons directly; the US program
/// reads four words the menu's entry sets from the face preset, the same four the hint at
/// the bottom of a menu draws its icons from, and two entries of the action-mask table.
/// Attack and magic stay the preset's. Keys and mouse buttons keep their verbs, so F still
/// examines and Tab still opens the menu. There is no setting of its own: they are on
/// exactly when the Japanese layer is (<see cref="JapaneseDisc.Active"/>), so a Japanese
/// language with no Japanese disc, which plays in English, keeps the English buttons. See
/// "Japanese buttons" in docs/INPUT.md. Everything here runs on the game thread.
/// </summary>
public static class JapaneseButtons
{
    /// <summary>func_80027198: every menu's entry, which sets the four words from the preset.</summary>
    const uint MenuEnter = 0x80027198;

    /// <summary>func_800279A4: the menu's pad read.</summary>
    const uint MenuPadRead = 0x800279A4;

    /// <summary>func_80030FCC: stage 4, the player's tick, before it tests the pad.</summary>
    const uint PlayerTick = 0x80030FCC;

    const uint MenuActionMask = 0x80081872;     // u16, entry 5: opens the in-game menu
    const uint ExamineActionMask = 0x80081876;  // u16, entry 7: examine, talk, open

    const uint ConfirmMask = 0x8009C24C;   // gp+0x38
    const uint CancelMask = 0x8009C250;    // gp+0x3C
    const uint ConfirmIcon = 0x8009C254;   // gp+0x40, beside "select"
    const uint CancelIcon = 0x8009C258;    // gp+0x44, beside "return"

    const uint PadCircle = 0x20, PadCross = 0x40;
    const uint IconCircle = 1, IconCross = 3;

    static readonly ModInfo _self = new()
    {
        Id = "kf3.japanesebuttons",
        Name = "Japanese buttons",
        Version = "1.0",
        Description = "Circle confirms and examines, and Cross cancels and opens the menu, as in the Japanese release.",
    };

    static bool _queued;
    static bool _mouseWasCaptured;

    public static bool Enabled => JapaneseDisc.Active;

    public static void Install()
    {
        Event.AddListener(_keepDeviceVerbs);
        HookAttach.OnOverlayLoad("japanese buttons", Attach, "The game keeps the save's confirm and examine buttons.");
    }

    static bool Attach()
    {
        SymbolRegistry.Build();
        var enter = SymbolRegistry.Resolve("game", null, MenuEnter);
        var pad = SymbolRegistry.Resolve("game", null, MenuPadRead);
        var tick = SymbolRegistry.Resolve("game", null, PlayerTick);
        if (enter == null || pad == null || tick == null) return false;

        if (!_queued)
        {
            _queued = true;
            HookManager.AddPost(_self, enter, Own(nameof(AfterMenuEnter)));
            HookManager.AddPre(_self, pad, Own(nameof(BeforePadTest)));
            HookManager.AddPre(_self, tick, Own(nameof(BeforePadTest)));
        }
        HookManager.Commit();
        return HookAttach.Installed(enter) && HookAttach.Installed(pad) && HookAttach.Installed(tick);
    }

    static MethodInfo Own(string name) => typeof(JapaneseButtons).GetMethod(name, BindingFlags.Public | BindingFlags.Static)!;

    /// <summary>The entry has just set the words from the preset: ours go over them before the first hint is drawn.</summary>
    public static void AfterMenuEnter(CpuContext c, IMemory m)
    {
        if (Enabled) WriteWords(m);
    }

    /// <summary>Catches func_8001F4C0's rewrite of the words and the masks on a New Game,
    /// a load and the control-config screen.</summary>
    public static void BeforePadTest(CpuContext c, IMemory m)
    {
        if (Enabled) WriteWords(m);
    }

    static void WriteWords(IMemory m)
    {
        m.WriteU32(ConfirmMask, PadCircle);
        m.WriteU32(CancelMask, PadCross);
        m.WriteU32(ConfirmIcon, IconCircle);
        m.WriteU32(CancelIcon, IconCross);
        m.WriteU16(ExamineActionMask, (ushort)PadCircle);
        m.WriteU16(MenuActionMask, (ushort)PadCross);
    }

    /// <summary>
    /// Game thread, inside PAD_dr. With the buttons swapped, a key or mouse button bound
    /// to Cross or Circle presses the other, so it keeps its verb: only a real pad moves.
    /// </summary>
    static void KeepDeviceVerbs(PadReadEvent e)
    {
        // The mouse attaches its listener on capture, and this one must run after it.
        if (Mouse.Captured != _mouseWasCaptured)
        {
            _mouseWasCaptured = Mouse.Captured;
            Event.RemoveListener(_keepDeviceVerbs);
            Event.AddListener(_keepDeviceVerbs);
        }
        if (e.Port != 0 || !Enabled) return;

        bool cross = DeviceHolds(Controller.Cross, Key.F) && Pressed(e.Buttons, Controller.Cross);
        bool circle = DeviceHolds(Controller.Circle, Key.Tab) && Pressed(e.Buttons, Controller.Circle);
        if (cross == circle) return;

        e.Buttons |= OnWire(cross ? Controller.Cross : Controller.Circle);
        e.Buttons &= (ushort)~OnWire(cross ? Controller.Circle : Controller.Cross);
    }

    static readonly Action<PadReadEvent> _keepDeviceVerbs = KeepDeviceVerbs;

    static bool DeviceHolds(ushort button, Key layoutKey) =>
        (KeyLayout.IsApplied() && HostWindow.IsKeyDown(layoutKey)) || MouseHolds(button);

    static bool MouseHolds(ushort button) =>
        Mouse.Enabled && Mouse.Captured && (
            (Mouse.PadButtons[Mouse.LeftButton].Bit == button && HostWindow.IsMouseButtonDown(Silk.NET.Input.MouseButton.Left)) ||
            (Mouse.PadButtons[Mouse.RightButton].Bit == button && HostWindow.IsMouseButtonDown(Silk.NET.Input.MouseButton.Right)) ||
            (Mouse.PadButtons[Mouse.MiddleButton].Bit == button && HostWindow.IsMouseButtonDown(Silk.NET.Input.MouseButton.Middle)));

    /// <summary>PAD_dr's buffer is active low, with the two button bytes swapped from Controller's layout.</summary>
    static ushort OnWire(ushort button) => (ushort)((button >> 8) | (button << 8));

    static bool Pressed(ushort buttons, ushort button) => (buttons & OnWire(button)) == 0;
}
