using System.Reflection;
using RecompOne.Runtime.Context;
using RecompOne.Runtime.Memory;
using RecompOne.Runtime.Modding;
using Game = Recompiled.KingsField3_game;

namespace Kf3;

/// <summary>
/// The port's settings as a page of the game's own menu: PORT SETTINGS, a seventh
/// item under SYSTEM in the in-game menu, opens it, drawn with the menu's own boxes,
/// font, hint row and sounds. A page of
/// <see cref="PortSettings"/>' rows, Up/Down for the row, Left/Right (or Cross) for
/// the value, L1/R1 for the page, Circle to leave; leaving with changes asks SAVE
/// CHANGES or DISCARD CHANGES. Everything goes through <see cref="SettingsSession"/>,
/// so the rules in docs/SETTINGS.md hold: nothing is written unless the player
/// changed a value and chose to save.
///
///     KF3_SETTINGSPAGE_PROBE=1   a line for each open, page, step, save and discard
///
/// The item is group 0's empty seventh record, written in the game's list table, and
/// the top menu's two counts raised by one from pres on its own calls: the list
/// drawn with 7 items, the chooser with a last index of 6. The game's dispatch
/// takes only 0..5, so choosing 6 leaves the menu as it was, and a post on the
/// chooser runs the page: a C# loop after OPTION 2 (<c>func_8001F004</c>), between
/// the menu's frames. The page's own records are built on the guest stack. See
/// "The port settings page" in docs/GAME_INTERNALS.md.
/// </summary>
public static class SettingsPage
{
    // ---- The top menu func_8001A774 ----
    const uint Chooser = 0x800221E8;              // func_800221E8(cursor, last, &sel, &confirmed), &cancel at sp+0x10
    const uint MenuChooserReturn = 0x8001A8E4;    // its call in the top menu; the chooser has other callers
    const uint MenuSel = 0x18, MenuConfirmed = 0x1C, MenuCancel = 0x20;   // the top menu's locals at its sp
    const uint NotCancelled = 0xFFFFFF9D;

    // ---- The top menu's list, group 0 ----
    const uint DrawList = 0x800252F4;             // func_800252F4(group, count, cursor, mode)
    const uint MenuListReturn1 = 0x8001A7D8;      // the menu's two opening frames
    const uint MenuListReturn2 = 0x8001A938;      // and its loop
    const uint MenuItems = 6, MenuLast = 5;       // USE ITEM .. SYSTEM
    const uint OurItem = 6;                       // the seventh: PORT SETTINGS
    const uint OurRecord = 0x8007E660 + 7 * 0x1C; // group 0's record 7, zeros in GAME.EXE
    const int OurX = 31, OurY = 32 + 6 * 26;      // under SYSTEM, at the group's spacing
    const string OurLabel = "PORT SETTINGS";

    // ---- Pad bits, as PadRead_game(1) returns them ----
    const uint L1 = 0x0004, R1 = 0x0008;
    const uint Up = 0x1000, Right = 0x2000, Down = 0x4000, Left = 0x8000;

    // ---- The menu's gp words (gp = 0x8009C214) ----
    const uint ConfirmMask  = 0x8009C24C;   // gp+0x38: Cross, or Circle with the swapped config
    const uint CancelMask   = 0x8009C250;   // gp+0x3C
    const uint ConfirmIcon  = 0x8009C254;   // gp+0x40, the hint row's icons
    const uint CancelIcon   = 0x8009C258;   // gp+0x44
    const uint ListBlink    = 0x8009C260;   // gp+0x4C, the selected box's pulse
    const uint BlinkRestart = 0x8009C268;   // gp+0x54: 0 restarts the pulse at the next frame head
    const uint TextRgb      = 0x8009C270;   // gp+0x5C, +0x60, +0x64: the label colour, a word each

    // ---- Templates and strings in GAME.EXE's data ----
    const uint BoxTemplate  = 0x8007E5A0;   // 118 x 24, drawn at the record's X - 6, Y - 6
    const uint FontTemplate = 0x8007E570;
    const uint SelectText   = 0x8009C290;   // "select", in the hint font
    const uint ReturnText   = 0x8009C288;   // "return"
    const uint PageArrows   = 9;            // the hint icon OPTION 2 shows for Left/Right

    // ---- Sounds (func_8002792C) ----
    const uint SoundMove = 0xC, SoundChange = 0xD, SoundBack = 0xE;

    // ---- Layout: the menu's own places ----
    const int HeaderX = 31, HeaderY = 32;   // a list's header
    const int RowX = 45, FirstRowY = 58;    // its first item under a header
    const int RowStep = 26;
    const int ValueX = 171;                 // OPTION 2's value column
    const int HintY = 0xD6;
    const int AskX = 101, AskY = 94, AskStep = 28;   // the start menu's STAY / DO NOT STAY (group 7)

    /// <summary>What fits the screen: six rows under a header at the game's spacing,
    /// the last box ending at Y 206, above the hint row at 214.</summary>
    public const int MaxRows = 6;

    /// <summary>A label fits the 118-wide box: 16 cells of 7 pixels from X 45.</summary>
    public const int LabelChars = 16;

    /// <summary>A value from X 171 to the frame's right edge, less a margin.</summary>
    public const int ValueChars = 18;

    // The label colour for a row that cannot change: darker than the game's
    // unselected 0x47/0x57 (func_80026158).
    const uint DimRg = 0x28, DimB = 0x30;

    // A frame of guest stack for the records and the chooser's words.
    const uint Frame = 0x100;
    const uint Record = 0x40;               // one 0x1C-byte record, rebuilt before each draw
    const uint AskWords = 0x80;             // sel, confirmed, cancel

    static readonly ModInfo _self = new()
    {
        Id = "kf3.settingspage",
        Name = "Settings page",
        Version = "1.0",
        Description = "The port's settings in the game's own menu, under SYSTEM.",
    };

    static bool _probe;
    static bool _open;
    static int _page;                       // kept between opens
    static readonly Dictionary<string, byte[]> _encoded = [];

    public static long Opened { get; private set; }

    public static void Configure(string? probe) => _probe = probe?.Trim() is "1" or "on" or "true";

    public static void Install() =>
        HookAttach.OnOverlayLoad("settings page", Attach,
            "The menu will have no PORT SETTINGS. See \"The port settings page\" in docs/GAME_INTERNALS.md.");

    static MethodInfo M(string name) => typeof(SettingsPage).GetMethod(name, BindingFlags.Public | BindingFlags.Static)!;

    static bool Attach()
    {
        SymbolRegistry.Build();
        var chooser = SymbolRegistry.Resolve("game", null, Chooser);
        var list = SymbolRegistry.Resolve("game", null, DrawList);
        if (chooser == null || list == null) return false;
        // All three or none: an item drawn that cannot be chosen, or chosen and not drawn, is worse than no item.
        if (!HookManager.AddPre(_self, list, M(nameof(BeforeList)))
            || !HookManager.AddPre(_self, chooser, M(nameof(BeforeChooser)))
            || !HookManager.AddPost(_self, chooser, M(nameof(AfterChooser))))
        {
            HookManager.RemoveMod(_self);
            return false;
        }
        HookManager.Commit();
        bool ok = HookAttach.Installed(chooser) && HookAttach.Installed(list);
        Console.WriteLine(ok ? "[KF3] settings page: PORT SETTINGS under SYSTEM"
                             : "[KF3] settings page: the menu hooks did not install");
        return ok;
    }

    /// <summary>The top menu's list: one more item, PORT SETTINGS, written into the
    /// group's empty seventh record each time (the table comes back with GAME.EXE).</summary>
    public static bool BeforeList(CpuContext c, IMemory m)
    {
        if (c.A0 != 0u || c.A1 != MenuItems || (c.RA != MenuListReturn1 && c.RA != MenuListReturn2)) return true;
        Put(m, OurRecord, OurX, OurY, OurLabel);
        c.A1 = MenuItems + 1u;
        return true;
    }

    /// <summary>The top menu's chooser: Up and Down reach the seventh item.</summary>
    public static bool BeforeChooser(CpuContext c, IMemory m)
    {
        if (!_open && c.RA == MenuChooserReturn && c.A1 == MenuLast) c.A1 = OurItem;
        return true;
    }

    /// <summary>The top menu's chooser has returned: no frame is open. PORT SETTINGS
    /// confirmed opens the page; the game's dispatch ignores the index.</summary>
    public static void AfterChooser(CpuContext c, IMemory m)
    {
        if (_open || c.RA != MenuChooserReturn) return;
        if (m.ReadU32(c.SP + MenuSel) != OurItem || m.ReadU32(c.SP + MenuConfirmed) == 0
            || m.ReadU32(c.SP + MenuCancel) != NotCancelled) return;
        Run(c, m);
    }

    static void Run(CpuContext c, IMemory m)
    {
        var saved = c.Snapshot();
        _open = true;
        Opened++;
        var session = SettingsSession.Open(PortSettings.All);
        try
        {
            c.RA = 0u;                      // not the top menu's call, for the hook above
            c.SP -= Frame;
            Game.func_80027A40(c, m);       // every button up, as the game waits before a page
            if (_probe) Console.WriteLine($"[KF3] settings page: open on {PortSettings.Pages[_page]}");
            Loop(c, m, session);
            Game.func_80027A40(c, m);       // so the Circle that left does not close the menu
        }
        finally
        {
            // Never leave the session open, whatever happened.
            if (SettingsSession.Current == session) session.Discard();
            c.Restore(saved);
            _open = false;
        }
    }

    static void Loop(CpuContext c, IMemory m, SettingsSession session)
    {
        int row = 0;
        while (true)
        {
            var rows = PortSettings.OnPage(PortSettings.Pages[_page]).ToArray();
            Game.func_800279D8(c, m);       // the repeat gate
            Game.func_800279A4(c, m);       // the pad, setting the gate's flag on any button
            uint pad = c.V0;

            if ((pad & Up) != 0) { row = row == 0 ? rows.Length - 1 : row - 1; Moved(c, m); }
            else if ((pad & Down) != 0) { row = row == rows.Length - 1 ? 0 : row + 1; Moved(c, m); }
            else if ((pad & (L1 | R1)) != 0)
            {
                int n = PortSettings.Pages.Length;
                _page = (_page + ((pad & R1) != 0 ? 1 : n - 1)) % n;
                row = 0;
                Moved(c, m);
                if (_probe) Console.WriteLine($"[KF3] settings page: page {PortSettings.Pages[_page]}");
            }
            else if ((pad & (m.ReadU32(ConfirmMask) | Right | Left)) != 0)
            {
                var s = rows[row];
                int dir = (pad & Left) != 0 ? -1 : 1;
                if (session.Step(s, dir) is { } why)
                    Console.WriteLine($"[KF3] settings page: {s.Key} not changed: {why}");
                else
                {
                    Sound(c, m, SoundChange);
                    if (_probe) Console.WriteLine($"[KF3] settings page: {s.Key} -> {s.MenuValue(session.Shown(s))}" +
                                                  (session.IsChanged(s) ? "" : " (as it was)"));
                }
            }
            else if ((pad & m.ReadU32(CancelMask)) != 0)
            {
                Sound(c, m, SoundBack);
                if (!session.Dirty)
                {
                    session.Discard();
                    if (_probe) Console.WriteLine("[KF3] settings page: left, nothing changed");
                    return;
                }
                switch (Ask(c, m))
                {
                    case 0:
                        int count = session.Changed.Count();
                        session.Save();
                        if (_probe) Console.WriteLine($"[KF3] settings page: saved {count} change(s)");
                        return;
                    case 1:
                        session.Discard();
                        if (_probe) Console.WriteLine("[KF3] settings page: discarded");
                        return;
                }
                // Circle at the question: back to the page.
            }

            for (int f = 0; f < 2; f++) DrawPage(c, m, session, rows, row);
        }
    }

    static void Moved(CpuContext c, IMemory m)
    {
        m.WriteU32(BlinkRestart, 0u);
        Sound(c, m, SoundMove);
    }

    /// <summary>SAVE CHANGES (0) or DISCARD CHANGES (1), with the game's chooser;
    /// -1 when cancelled.</summary>
    static int Ask(CpuContext c, IMemory m)
    {
        uint sel = c.SP + AskWords, confirmed = sel + 4u, cancel = sel + 8u;
        m.WriteU32(cancel, NotCancelled);
        uint cursor = 0;
        Game.func_80027A40(c, m);
        while (true)
        {
            m.WriteU32(c.SP + 0x10u, cancel);
            c.A0 = cursor; c.A1 = 1u; c.A2 = sel; c.A3 = confirmed;
            Game.func_800221E8(c, m);
            cursor = c.V0;
            if (m.ReadU32(confirmed) != 0) return (int)m.ReadU32(sel);
            if (m.ReadU32(cancel) != NotCancelled)
            {
                Game.func_80027A40(c, m);
                return -1;
            }
            for (int f = 0; f < 2; f++)
            {
                Game.func_80026FE4(c, m);
                Item(c, m, AskX, AskY, "SAVE CHANGES", cursor == 0, usable: true);
                Item(c, m, AskX, AskY + AskStep, "DISCARD CHANGES", cursor == 1, usable: true);
                Hint(c, m, choose: true);
                Pointed(m, AskX, AskY, AskStep, 2, (int)cursor, values: false, "settings question");
                Game.func_800270F8(c, m);
            }
        }
    }

    static void DrawPage(CpuContext c, IMemory m, SettingsSession session, PortSetting[] rows, int row)
    {
        Game.func_80026FE4(c, m);           // the frame head

        string header = $"{PortSettings.Pages[_page]} {_page + 1}/{PortSettings.Pages.Length}";
        Box(c, m, Put(m, c.SP + Record, HeaderX, HeaderY, header), selected: false);
        Text(c, m, c.SP + Record, TextShade.Bright);

        for (int i = 0; i < rows.Length; i++)
        {
            var s = rows[i];
            int y = FirstRowY + i * RowStep;
            bool usable = s.LockedBy is null && s.IsUsable;
            Item(c, m, RowX, y, s.MenuLabel!, i == row, usable);

            string value = s.MenuValue(session.Shown(s));
            if (MenuFont.Check(value) is not null || value.Length > ValueChars) value = "?";
            Put(m, c.SP + Record, ValueX, y, value);
            Text(c, m, c.SP + Record, !usable ? TextShade.Dim : i == row ? TextShade.Bright : TextShade.Plain);
        }

        Hint(c, m, choose: false);
        Pointed(m, RowX, FirstRowY, RowStep, rows.Length, row, values: true, $"settings page {_page}");
        Game.func_800270F8(c, m);           // the presenter
    }

    /// <summary>The rows just drawn, for the menu pointer: the boxes <see cref="Box"/>
    /// draws, at X - 6, Y - 6. A click on the selected row of the page steps its value,
    /// as Cross and Right do.</summary>
    static void Pointed(IMemory m, int x, int y, int step, int count, int cursor, bool values, string what)
    {
        var (w, h) = MenuMouse.BoxSize(m, BoxTemplate);
        Span<(int, int, int, int)> rows = stackalloc (int, int, int, int)[count];
        for (int i = 0; i < count; i++) rows[i] = (x - 6, y + i * step - 6, w, h);
        MenuMouse.Rows(rows, cursor, values, what);
    }

    /// <summary>One list item as <c>func_800252F4</c> draws it: the selected one in the
    /// pulsing box and bright, the others plain; one that cannot change, darker.</summary>
    static void Item(CpuContext c, IMemory m, int x, int y, string text, bool selected, bool usable)
    {
        uint rec = Put(m, c.SP + Record, x, y, text);
        Box(c, m, rec, selected);
        Text(c, m, rec, !usable ? TextShade.Dim : selected ? TextShade.Bright : TextShade.Plain);
    }

    enum TextShade { Bright, Plain, Dim }

    static void Box(CpuContext c, IMemory m, uint rec, bool selected)
    {
        c.A0 = BoxTemplate; c.A1 = rec;
        if (selected)
        {
            c.A2 = m.ReadU32(ListBlink);
            Game.func_80026020(c, m);
        }
        else Game.func_80025F38(c, m);
    }

    static void Text(CpuContext c, IMemory m, uint rec, TextShade shade)
    {
        c.A0 = FontTemplate; c.A1 = rec;
        switch (shade)
        {
            case TextShade.Bright: Game.func_800261DC(c, m); break;
            case TextShade.Plain: Game.func_80026158(c, m); break;
            default:
                uint r = m.ReadU32(TextRgb), g = m.ReadU32(TextRgb + 4u), b = m.ReadU32(TextRgb + 8u);
                m.WriteU32(TextRgb, DimRg); m.WriteU32(TextRgb + 4u, DimRg); m.WriteU32(TextRgb + 8u, DimB);
                Game.func_800261DC(c, m);
                m.WriteU32(TextRgb, r); m.WriteU32(TextRgb + 4u, g); m.WriteU32(TextRgb + 8u, b);
                break;
        }
    }

    /// <summary>The bottom row <c>func_800252F4</c> draws: mode 1 (OPTION 2's) on a
    /// page, the Left/Right icon and "select"; mode 0 at the question, the confirm
    /// button's icon. Both end with the cancel icon and "return".</summary>
    static void Hint(CpuContext c, IMemory m, bool choose)
    {
        if (choose) Icon(c, m, 0x5F, m.ReadU32(ConfirmIcon));
        else Icon(c, m, 0x5B, PageArrows);
        Small(c, m, 0x69, SelectText);
        Icon(c, m, 0xA0, m.ReadU32(CancelIcon));
        Small(c, m, 0xAA, ReturnText);
    }

    static void Icon(CpuContext c, IMemory m, uint x, uint icon)
    {
        c.A0 = x; c.A1 = HintY; c.A2 = icon;
        Game.func_800269C0(c, m);
    }

    static void Small(CpuContext c, IMemory m, uint x, uint text)
    {
        m.WriteU32(c.SP + 0x10u, 0u);
        c.A0 = x; c.A1 = HintY; c.A2 = text; c.A3 = 0u;
        Game.func_80026570(c, m);
    }

    /// <summary>A list record, <c>s16 X, s16 Y</c> and the string ending 0xFF.</summary>
    static uint Put(IMemory m, uint at, int x, int y, string text)
    {
        if (!_encoded.TryGetValue(text, out var bytes)) _encoded[text] = bytes = MenuFont.Encode(text);
        m.WriteU16(at, (ushort)x);
        m.WriteU16(at + 2u, (ushort)y);
        for (int i = 0; i < bytes.Length; i++) m.WriteU8(at + 4u + (uint)i, bytes[i]);
        return at;
    }

    static void Sound(CpuContext c, IMemory m, uint sound)
    {
        c.A0 = sound;
        Game.func_8002792C(c, m);
    }
}
