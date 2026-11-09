using System.Reflection;
using RecompOne.Runtime.Context;
using RecompOne.Runtime.Events;
using RecompOne.Runtime.Memory;
using RecompOne.Runtime.Modding;
using Rt = RecompOne.Runtime.Runtime;

namespace Kf3;

/// <summary>
/// The Japanese release's menu buttons: Circle confirms and Cross cancels, whichever
/// control preset the save holds. The Japanese program tests those two buttons directly;
/// the US program reads four words the menu's entry sets from the face preset, the same
/// four the hint at the bottom of a menu draws its icons from, so writing them moves the
/// buttons and the hint together. Play's own buttons (attack, examine, the rest) stay
/// the preset's. Independent of the language. See "Japanese menu buttons" in docs/INPUT.md.
///
///     KF3_JP_BUTTONS=1|0     on or off; a set variable wins over the stored setting
///
/// A change takes effect at the next read of the menu's pad, so a change made on the
/// settings page applies on that page. Everything but <see cref="SetEnabled"/> runs on
/// the game thread.
/// </summary>
public static class JapaneseButtons
{
    /// <summary>func_80027198: every menu's entry, which sets the four words from the preset.</summary>
    const uint MenuEnter = 0x80027198;

    /// <summary>func_800279A4: the menu's pad read.</summary>
    const uint MenuPadRead = 0x800279A4;

    const uint ConfirmMask = 0x8009C24C;   // gp+0x38
    const uint CancelMask = 0x8009C250;    // gp+0x3C
    const uint ConfirmIcon = 0x8009C254;   // gp+0x40, beside "select"
    const uint CancelIcon = 0x8009C258;    // gp+0x44, beside "return"

    /// <summary>The save's face preset; the game gives presets below 2 the Circle-confirm words.</summary>
    const uint FacePreset = 0x801B25E2;
    const byte FirstCrossConfirmPreset = 2;

    const uint PadCircle = 0x20, PadCross = 0x40;
    const uint IconCircle = 1, IconCross = 3;

    /// <summary>interface.ini key, read and written by the Gameplay tab and the game's page.</summary>
    public const string OnKey = "kf3.japanesebuttons.on";

    static readonly ModInfo _self = new()
    {
        Id = "kf3.japanesebuttons",
        Name = "Japanese menu buttons",
        Version = "1.0",
        Description = "Circle confirms and Cross cancels in the menus, as in the Japanese release.",
    };

    static volatile bool _on;
    static bool _fromEnv, _queued;
    static bool _written;                  // the words hold ours, so turning off must put the preset's back

    public static bool Enabled => _on;

    /// <summary>Any thread: the words are written by the game thread at its next menu pad read.</summary>
    public static void SetEnabled(bool on) => _on = on;

    public static void Install()
    {
        string? env = Verdite.Core.Game.Env("JP_BUTTONS");
        if (env is "0" or "1")
        {
            _on = env == "1";
            _fromEnv = true;
        }

        Event.AddListener<RuntimeReadyEvent>(_ =>
        {
            if (!_fromEnv) _on = Rt.View.GetInt(OnKey, 0) != 0;
        });
        HookAttach.OnOverlayLoad("japanese buttons", Attach, "The menus keep the save's confirm and cancel buttons.");
    }

    static bool Attach()
    {
        SymbolRegistry.Build();
        var enter = SymbolRegistry.Resolve("game", null, MenuEnter);
        var pad = SymbolRegistry.Resolve("game", null, MenuPadRead);
        if (enter == null || pad == null) return false;

        if (!_queued)
        {
            _queued = true;
            HookManager.AddPost(_self, enter, Own(nameof(AfterMenuEnter)));
            HookManager.AddPre(_self, pad, Own(nameof(BeforeMenuPadRead)));
        }
        HookManager.Commit();
        return HookAttach.Installed(enter) && HookAttach.Installed(pad);
    }

    static MethodInfo Own(string name) => typeof(JapaneseButtons).GetMethod(name, BindingFlags.Public | BindingFlags.Static)!;

    /// <summary>The entry has just set the words from the preset: ours go over them before the first hint is drawn.</summary>
    public static void AfterMenuEnter(CpuContext c, IMemory m)
    {
        if (_on) WriteJapanese(m);
    }

    /// <summary>Catches a change of the switch, and the control-config screen's own rewrite of the words.</summary>
    public static void BeforeMenuPadRead(CpuContext c, IMemory m)
    {
        if (_on)
        {
            WriteJapanese(m);
            return;
        }
        if (!_written) return;
        _written = false;
        WriteWords(m, circleConfirms: m.ReadU8(FacePreset) < FirstCrossConfirmPreset);
    }

    static void WriteJapanese(IMemory m)
    {
        _written = true;
        WriteWords(m, circleConfirms: true);
    }

    static void WriteWords(IMemory m, bool circleConfirms)
    {
        m.WriteU32(ConfirmMask, circleConfirms ? PadCircle : PadCross);
        m.WriteU32(CancelMask, circleConfirms ? PadCross : PadCircle);
        m.WriteU32(ConfirmIcon, circleConfirms ? IconCircle : IconCross);
        m.WriteU32(CancelIcon, circleConfirms ? IconCross : IconCircle);
    }
}
