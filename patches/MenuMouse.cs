using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using ImGuiNET;
using RecompOne.Runtime.Context;
using RecompOne.Runtime.Events;
using RecompOne.Runtime.Hle;
using RecompOne.Runtime.Host;
using RecompOne.Runtime.Host.Window;
using RecompOne.Runtime.Memory;
using RecompOne.Runtime.Modding;
// Three namespaces in scope define a MouseButton; this alias picks Silk's, which
// HostWindow.IsMouseButtonDown takes.
using HostMouseButton = Silk.NET.Input.MouseButton;
using Game = Recompiled.KingsField3_game;

namespace Kf3;

/// <summary>
/// Point at the game's menus with the mouse: the game's own cursor goes to the item
/// under the pointer, left click confirms, right click backs out, and the wheel
/// pages a list longer than its window. Verdite2's <c>MenuMouse</c> on this game's
/// widgets.
///
///     KF3_MENUMOUSE=0        off -- the menus are pad and keyboard only again
///     KF3_MENUMOUSE_PROBE=1  what was drawn, the pointer's row, and what it did
///
/// A setting on Input's Mouse tab, independent of mouse look: the menus release a
/// captured pointer (<c>Mouse.Suspend</c> from <c>MenuWorld</c>), so the desktop
/// pointer is what the player points with.
///
/// ## The widgets
///
/// | widget | drawn by | stepped by | the cursor is |
/// |---|---|---|---|
/// | fixed list | `func_800252F4(group, count, cursor, mode)` | the chooser `func_800221E8`, or the page's own loop | the chooser's return, or the page's register |
/// | scrolling list | `func_80025468(desc, mode)` | `func_800222FC(desc, items, &confirmed, &cancel)` | `u8[desc+0x21]` in memory |
/// | two-line prompt | `func_80025B24(rec0, rec1, flag)` | the chooser, or the prompt's own loop | the flag |
///
/// Three mechanisms, as in Verdite2, because the three cursors are different kinds
/// of thing:
///
///  * **The chooser** is driven by its return value: a post writes `V0`, and a
///    click writes its out-parameters exactly as its confirm arm does. Its rows are
///    whatever fixed widget was drawn since the last menu pad read, if that widget
///    has `last + 1` rows -- the top menu, SYSTEM, the shop, STAY/DO NOT STAY, the
///    format prompt.
///  * **The scrolling list** keeps its cursor in the descriptor, so a post on its
///    stepper writes `+0x21` and `+0x22` and replays the move arm (the blip and
///    `func_80027A9C(items[cursor])`). The wheel moves the page, `+0x20`, the one
///    byte hover never writes.
///  * **A page with a loop of its own** (OPTION 1, OPTION 2, QUIT's prompt, the
///    item prompt `func_80024C70`, the port's settings page) keeps its cursor in a
///    register, so it goes through the pad: a post on the menu's pad read
///    `func_800279A4` ORs in one Up or Down while the hovered row and the drawn
///    cursor disagree, then the confirm mask (Right on a page of values, mode 1)
///    for a click once they agree. Closed over the state it changes, and abandoned
///    after <see cref="MaxPushes"/> pushes that did not move the drawn cursor.
///
/// **Backing out** is one flag the same pad read spends: right click over the
/// picture, or a left click clear of the widget's boxes, ORs the game's cancel
/// mask into whichever menu reads the pad next.
///
/// **Whichever device moved last owns the cursor**: hover takes it only once the
/// pointer has moved, and hands it back the moment the pad moves the cursor.
///
/// The geometry is the game's own: the list table, the box templates and the
/// stepper's descriptor, read live. See "The menu pointer" in docs/INPUT.md.
/// </summary>
public static class MenuMouse
{
    // ---- The widgets ----
    const uint Chooser = 0x800221E8;          // (cursor, last, &sel, &confirmed), &cancel at sp+0x10 -> cursor
    const uint ChooserReads = 0x80022234;     // its pad read's return address
    const uint Stepper = 0x800222FC;          // (desc, items, &confirmed, &cancel), mode at sp+0x10
    const uint StepperReads = 0x80022384;
    const uint DrawList = 0x800252F4;         // (group, count, cursor, mode)
    const uint DrawPrompt = 0x80025B24;       // (rec0, rec1, flag); an overlay-shared address, called through the dispatcher
    const uint MenuPadRead = 0x800279A4;      // PadRead_game(1), and the repeat gate's flag on any button

    // ---- The fixed list's table and the box templates ----
    const uint ListTable = 0x8007E660, GroupStride = 0xFC, RecordStride = 0x1C;
    const int MaxGroup = 15;
    const uint BoxTemplate = 0x8007E5A0;      // a list item, 118 x 24
    const uint PromptTemplate = 0x8007E594;   // a prompt's box, 54 x 24
    const int TemplateInset = 6;              // func_80025F38 draws the box at the record's X - 6, Y - 6

    // ---- The scrolling list's descriptor (func_800222FC and func_80025468 both read every one) ----
    const uint DescX = 0x1C, DescY = 0x1D, DescCount = 0x1E, DescVisible = 0x1F;
    const uint DescScroll = 0x20, DescCursor = 0x21, DescRow = 0x22;
    const uint RowTemplate = 0x8007E5E8;      // the highlight, drawn at (X + 6, Y + 5 + 16 r)
    const int RowInsetX = 6, RowInsetY = 5, RowPitch = 16;
    const int MaxScrollRows = 32;

    // ---- The pad and the menu's gp words ----
    const uint PadUp = 0x1000, PadRight = 0x2000, PadDown = 0x4000;
    const uint ConfirmMask = 0x8009C24C;      // gp+0x38
    const uint CancelMask = 0x8009C250;       // gp+0x3C
    const uint BlinkRestart = 0x8009C268;     // gp+0x54, written 0 by every move arm
    const uint SoundMove = 0xC, SoundConfirm = 0xD;

    /// <summary>A gap this long between two menu pad reads is a different menu.</summary>
    const long SessionGapMs = 500;

    /// <summary>How far clear of a widget's boxes a left click backs out.</summary>
    const int EdgeSlack = 8;

    /// <summary>How long a pointer movement keeps the cursor.</summary>
    const long IdleMs = 2000;

    /// <summary>A stepper not stepped for this long has just opened, and a wheel
    /// notch from before it is not a request made of it.</summary>
    const long WidgetGapMs = 250;

    /// <summary>Pushes through the pad that did not move the drawn cursor before
    /// a page's loop is taken not to step that way.</summary>
    const int MaxPushes = 3;

    const int MaxRows = 10;

    public static bool Enabled = true;
    public const string OnKey = "kf3.menumouse.on";

    static bool _probe;
    static readonly HashSet<string> _fromEnv = new(StringComparer.Ordinal);

    static readonly ModInfo _self = new()
    {
        Id = "kf3.menumouse",
        Name = "Menu pointer",
        Version = "1.0",
        Description = "Hover and click the game's menus with the mouse.",
    };

    // ---- The fixed widget last drawn ----

    static readonly (int X, int Y, int W, int H)[] _rows = new (int, int, int, int)[MaxRows];
    static int _rowCount, _drawnCursor = -1;
    static bool _values;
    static string _what = "none";
    static long _drawnAfter = -1;             // the pad read it was drawn after
    static long _reads;                       // menu pad reads so far
    static int _pushes, _pushedFrom = -1;     // pushes through the pad, and the drawn cursor they pushed

    // ---- The pointer ----

    static Vector2 _lastPos = new(float.NaN, float.NaN);
    static long _movedAt;
    static bool _padOwns = true;
    static bool _leftWas, _rightWas, _clickLeft, _clickRight, _inPicture;
    static float _gameX = float.NaN, _gameY = float.NaN;
    static bool _backOut, _cancelled;
    static long _padReadAt, _steppedAt;
    static float _wheel;

    /// <summary>The shell's pointer, in game pixels (<see cref="Shell"/>).</summary>
    static Vector2? _fake;
    static bool _fakeLeft, _fakeRight;

    // ---- The steppers' arguments, stashed by their pres ----

    static bool _haveChooser, _chooserFresh;
    static int _chCursorIn, _chLast;
    static uint _chSel, _chConfirmed, _chCancel;

    static bool _haveStepper;
    static uint _scDesc, _scItems, _scConfirmed;
    static int _scCursorIn;

    // ---- The probe ----

    static readonly Stopwatch _clock = Stopwatch.StartNew();
    static double _windowMs = -1.0;
    static int _hover = -1;
    static string _live = "none";
    static int _samples, _hovers, _moves, _scrolls, _confirms, _cancels, _injects;
    static string _lastDrawn = "";
    static uint _lastDesc;
    static int _lastCount = -1;

    public static void Configure()
    {
        Kept.Env("KF3_MENUMOUSE", OnKey, ref Enabled, _fromEnv);
        _probe = Environment.GetEnvironmentVariable("KF3_MENUMOUSE_PROBE")?.Trim() is "1" or "on" or "true";
    }

    /// <summary>Attached whether or not it is on, since the switch is a setting.</summary>
    public static void Install()
    {
        Event.AddListener<RuntimeReadyEvent>(_ => Kept.Saved(OnKey, ref Enabled, _fromEnv));
        HookAttach.OnOverlayLoad("menu pointer", Attach, "The menus stay pad and keyboard only.");
    }

    static bool _chooserHooked, _stepperHooked, _listHooked, _promptHooked, _padHooked;

    static bool Attach()
    {
        SymbolRegistry.Build();
        MethodInfo Own(string name) => typeof(MenuMouse).GetMethod(name, BindingFlags.Public | BindingFlags.Static)!;
        MethodInfo? At(uint addr)
        {
            var mi = SymbolRegistry.Resolve("game", null, addr);
            if (mi == null) Console.Error.WriteLine($"[KF3] menu pointer: no game function at 0x{addr:X8}");
            return mi;
        }

        MethodInfo? chooser = null, stepper = null, list = null, prompt = null, pad = null;

        // SettingsPage raises the top menu's counts in its pres on the list and the
        // chooser, so these pres run after its (order 1), and its post on the
        // chooser opens the page off sel and confirmed, so this post runs before
        // its (order -1): a click on PORT SETTINGS has to be written before that
        // post reads it.
        if (!_chooserHooked && (chooser = At(Chooser)) != null)
        {
            HookManager.AddPre(_self, chooser, Own(nameof(BeforeChooser)), order: 1);
            HookManager.AddPost(_self, chooser, Own(nameof(AfterChooser)), order: -1);
        }
        if (!_stepperHooked && (stepper = At(Stepper)) != null)
        {
            HookManager.AddPre(_self, stepper, Own(nameof(BeforeStepper)));
            HookManager.AddPost(_self, stepper, Own(nameof(AfterStepper)));
        }
        if (!_listHooked && (list = At(DrawList)) != null)
            HookManager.AddPre(_self, list, Own(nameof(BeforeList)), order: 1);
        if (!_promptHooked && (prompt = At(DrawPrompt)) != null)
            HookManager.AddPre(_self, prompt, Own(nameof(BeforePrompt)));
        if (!_padHooked && (pad = At(MenuPadRead)) != null)
            HookManager.AddPost(_self, pad, Own(nameof(AfterPadRead)));

        HookManager.Commit();

        _chooserHooked |= HookAttach.Installed(chooser);
        _stepperHooked |= HookAttach.Installed(stepper);
        _listHooked |= HookAttach.Installed(list);
        _promptHooked |= HookAttach.Installed(prompt);
        _padHooked |= HookAttach.Installed(pad);

        Console.WriteLine($"[KF3] menu pointer: {(Enabled ? "on" : "off")}, " +
                          $"chooser {(_chooserHooked ? "driven" : "NOT driven")}, " +
                          $"lists {(_stepperHooked ? "driven" : "NOT driven")}, " +
                          $"fixed rows {(_listHooked ? "read" : "NOT read")}, " +
                          $"prompts {(_promptHooked ? "read" : "NOT read")}, " +
                          $"pad {(_padHooked ? "driven" : "NOT driven")}");

        return _chooserHooked && _stepperHooked && _listHooked && _promptHooked && _padHooked;
    }

    // ------------------------------------------------------------------------
    // What was drawn
    // ------------------------------------------------------------------------

    /// <summary>
    /// A fixed widget was drawn: its boxes in game pixels, the row drawn selected,
    /// and whether a click on the selected row steps a value (Right) rather than
    /// confirming. The game's lists and prompts arrive here from their drawers; the
    /// port's settings page calls it itself, since it draws its rows box by box.
    /// </summary>
    public static void Rows(ReadOnlySpan<(int X, int Y, int W, int H)> rows, int cursor, bool values, string what)
    {
        int n = Math.Min(rows.Length, MaxRows);
        rows[..n].CopyTo(_rows);
        _rowCount = n;
        _drawnCursor = cursor;
        _values = values;
        _what = what;
        _drawnAfter = _reads;

        if (_probe && what != _lastDrawn)
        {
            _lastDrawn = what;
            var ys = string.Join(",", _rows.Take(n).Select(r => r.Y));
            Console.WriteLine($"[KF3] menu pointer: {what}, {n} rows, x {(n > 0 ? _rows[0].X : 0)}, y {ys}, " +
                              $"box {(n > 0 ? _rows[0].W : 0)}x{(n > 0 ? _rows[0].H : 0)}, cursor {cursor}" +
                              (values ? ", values" : ""));
            Console.Out.Flush();
        }
    }

    /// <summary>The template's drawn size: <c>func_80025F38</c> passes <c>+0x8</c>
    /// and <c>+0xA</c> as the quad's width and height.</summary>
    public static (int W, int H) BoxSize(IMemory m, uint template) =>
        ((short)m.ReadU16(template + 0x8), (short)m.ReadU16(template + 0xA));

    public static bool BeforeList(CpuContext c, IMemory m)
    {
        if (!Enabled) return true;
        int group = (int)c.A0, count = (int)c.A1, cursor = (int)c.A2, mode = (int)c.A3;
        if (group < 0 || group > MaxGroup || count <= 0 || count > MaxRows) return true;

        var (w, h) = BoxSize(m, BoxTemplate);
        Span<(int, int, int, int)> rows = stackalloc (int, int, int, int)[count];
        uint bas = ListTable + (uint)group * GroupStride;
        for (int i = 0; i < count; i++)
        {
            uint rec = bas + RecordStride * (uint)(i + 1);
            rows[i] = ((short)m.ReadU16(rec) - TemplateInset, (short)m.ReadU16(rec + 2) - TemplateInset, w, h);
        }
        // Mode 1 is OPTION 1 and 2, whose rows are values: Left/Right change them.
        Rows(rows, cursor, values: mode == 1, what: $"list group {group}");
        return true;
    }

    public static bool BeforePrompt(CpuContext c, IMemory m)
    {
        if (!Enabled) return true;
        var (w, h) = BoxSize(m, PromptTemplate);
        Span<(int, int, int, int)> rows = stackalloc (int, int, int, int)[2];
        for (int i = 0; i < 2; i++)
        {
            uint rec = i == 0 ? c.A0 : c.A1;
            rows[i] = ((short)m.ReadU16(rec) - TemplateInset, (short)m.ReadU16(rec + 2) - TemplateInset, w, h);
        }
        Rows(rows, c.A2 != 0 ? 1 : 0, values: false, what: "prompt");
        return true;
    }

    /// <summary>Drawn since the last menu pad read: the widget the loop reading the
    /// pad now is showing, not one a page before it left behind.</summary>
    static bool Fresh => _rowCount > 0 && _drawnAfter == _reads;

    // ------------------------------------------------------------------------
    // The chooser
    // ------------------------------------------------------------------------

    public static bool BeforeChooser(CpuContext c, IMemory m)
    {
        if (!Enabled) return true;
        _chCursorIn = (int)c.A0;
        _chLast = (int)c.A1;
        _chSel = c.A2;
        _chConfirmed = c.A3;
        _chCancel = m.ReadU32(c.SP + 0x10u);
        // Before its own pad read, which starts the next read's window.
        _chooserFresh = Fresh && _rowCount == _chLast + 1;
        _haveChooser = true;
        return true;
    }

    public static void AfterChooser(CpuContext c, IMemory m)
    {
        if (!_haveChooser) return;
        _haveChooser = false;
        if (!Enabled) return;

        int cursor = (int)c.V0;
        bool gameMoved = cursor != _chCursorIn;
        bool gameConfirmed = m.ReadU32(_chConfirmed) != 0;
        if (gameMoved) _padOwns = true;

        Sample();
        TakeWheel();          // nothing to page here
        _live = _chooserFresh ? $"chooser over {_what}" : "chooser, rows unknown";
        int hover = _hover = _chooserFresh && HoverLive() ? Hit() : -1;

        if (gameMoved || gameConfirmed || _cancelled) { Report(); return; }

        int target = hover >= 0 && hover <= _chLast ? hover : cursor;
        bool moved = target != cursor;
        // The blip is a call into the game, which clobbers V0: before V0 is written.
        if (moved) Sound(c, m, SoundMove);

        if (_clickLeft)
        {
            _clickLeft = false;
            if (hover >= 0)
            {
                // The confirm arm, as the chooser runs it.
                Sound(c, m, SoundConfirm);
                m.WriteU32(_chConfirmed, 1u);
                m.WriteU32(_chSel, (uint)target);
                _confirms++;
            }
            else if (_chooserFresh && Off(Bounds())) _backOut = true;
        }

        if (moved)
        {
            m.WriteU32(BlinkRestart, 0u);   // as the Up/Down arms do
            _moves++;
        }

        c.V0 = (uint)target;
        Report();
    }

    // ------------------------------------------------------------------------
    // The scrolling list
    // ------------------------------------------------------------------------

    public static bool BeforeStepper(CpuContext c, IMemory m)
    {
        if (!Enabled) return true;
        _scDesc = c.A0;
        _scItems = c.A1;
        _scConfirmed = c.A2;
        _scCursorIn = m.ReadU8(_scDesc + DescCursor);
        _haveStepper = true;
        return true;
    }

    public static void AfterStepper(CpuContext c, IMemory m)
    {
        if (!_haveStepper) return;
        _haveStepper = false;
        if (!Enabled) return;

        int cursor = m.ReadU8(_scDesc + DescCursor);
        int scroll = m.ReadU8(_scDesc + DescScroll);
        int count = m.ReadU8(_scDesc + DescCount);
        int visible = m.ReadU8(_scDesc + DescVisible);

        bool gameMoved = cursor != _scCursorIn;
        bool gameConfirmed = _scConfirmed != 0 && m.ReadU32(_scConfirmed) != 0;
        if (gameMoved) _padOwns = true;

        Sample();
        int delta = gameMoved || gameConfirmed ? 0 : Page(m, TakeWheel(), ref scroll, count, visible);

        int rows = Math.Min(Math.Min(visible, MaxScrollRows), count - scroll);
        int hover = _hover = HoverLive() ? HitList(m, rows) : -1;
        _live = $"list, rows {scroll}-{scroll + Math.Max(rows, 1) - 1} of {count}";

        if (_probe && (_scDesc != _lastDesc || count != _lastCount))
        {
            (_lastDesc, _lastCount) = (_scDesc, count);
            var (w, h) = BoxSize(m, RowTemplate);
            Console.WriteLine($"[KF3] menu pointer: list at 0x{_scDesc:X8}, {count} entries, {visible} visible from {scroll}, " +
                              $"cursor {cursor}, rows at x {m.ReadU8(_scDesc + DescX) + RowInsetX} " +
                              $"y {m.ReadU8(_scDesc + DescY) + RowInsetY}, {w}x{RowPitch} (highlight {w}x{h})");
            Console.Out.Flush();
        }

        if (gameMoved || gameConfirmed || _cancelled) { Report(); return; }

        int target = hover >= 0 ? scroll + hover : cursor + delta;
        if (target < 0 || target >= count) target = cursor;
        bool moved = target != cursor;

        // The move arm: the blip, the two cursor bytes, the preview.
        if (moved) Sound(c, m, SoundMove);
        if (moved || delta != 0)
        {
            m.WriteU8(_scDesc + DescCursor, (byte)target);
            m.WriteU8(_scDesc + DescRow, (byte)(target - scroll));
        }
        if (moved)
        {
            if (_scItems != 0) Preview(c, m, m.ReadU8(_scItems + (uint)target));
            _moves++;
        }

        if (_clickLeft)
        {
            _clickLeft = false;
            if (hover >= 0 && _scConfirmed != 0)
            {
                // The stepper's confirm arm writes the flag and nothing else; the
                // caller blips.
                m.WriteU32(_scConfirmed, 1u);
                _confirms++;
            }
            else if (Off(ListBounds(m, rows))) _backOut = true;
        }

        Report();
    }

    /// <summary>Whole notches on the page, clamped to the game's own window and
    /// not wrapped; how far it moved.</summary>
    static int Page(IMemory m, int notches, ref int scroll, int count, int visible)
    {
        if (notches == 0 || count <= 0 || visible <= 0) return 0;
        int max = count - visible;
        if (max <= 0) return 0;
        // Positive is away from the user: toward the top of the list.
        int want = Math.Clamp(scroll - notches, 0, max);
        int delta = want - scroll;
        if (delta == 0) return 0;
        m.WriteU8(_scDesc + DescScroll, (byte)want);
        scroll = want;
        _scrolls++;
        return delta;
    }

    /// <summary>The row bands <c>func_80025468</c> lays out: the highlight's width
    /// from <c>X + 6</c>, 16 tall and packed from <c>Y + 5</c>.</summary>
    static (int X, int Y, int W, int H)? ListBounds(IMemory m, int rows)
    {
        if (rows <= 0) return null;
        int w = BoxSize(m, RowTemplate).W;
        if (w <= 0) return null;
        return (m.ReadU8(_scDesc + DescX) + RowInsetX, m.ReadU8(_scDesc + DescY) + RowInsetY, w, RowPitch * rows);
    }

    static int HitList(IMemory m, int rows)
    {
        if (ListBounds(m, rows) is not { } b) return -1;
        for (int r = 0; r < rows; r++)
            if (In(b.X, b.Y + RowPitch * r, b.W, RowPitch)) { _hovers++; return r; }
        return -1;
    }

    // ------------------------------------------------------------------------
    // The menu's pad read: every back-out, and the pages with loops of their own
    // ------------------------------------------------------------------------

    public static void AfterPadRead(CpuContext c, IMemory m)
    {
        if (!Enabled) return;

        long now = Environment.TickCount64;
        bool opening = now - _padReadAt > SessionGapMs;
        _padReadAt = now;
        _cancelled = false;
        bool fresh = Fresh;
        _reads++;

        if (opening)
        {
            // Not the previous menu's buttons, nor a back-out asked of it.
            _leftWas = Down(HostMouseButton.Left);
            _rightWas = Down(HostMouseButton.Right);
            _clickLeft = _clickRight = _backOut = false;
            _padOwns = true;
        }

        Sample();

        if (_clickRight)
        {
            _clickRight = false;
            if (_inPicture) _backOut = true;
        }

        if (_backOut)
        {
            // The game's own cancel arm does the rest, on every screen with one.
            _backOut = false;
            _cancelled = true;
            c.V0 |= m.ReadU32(CancelMask);
            _cancels++;
            _injects++;
            Report();
            return;
        }

        // The chooser and the list stepper read the pad inside themselves and are
        // driven from their own posts.
        if (c.RA == ChooserReads || c.RA == StepperReads) return;

        TakeWheel();
        if (!fresh) { _clickLeft = false; Report(); return; }
        if (_drawnCursor != _pushedFrom) _pushes = 0;   // the last push moved it

        _live = $"page over {_what}";
        int hover = _hover = HoverLive() ? Hit() : -1;

        uint add = 0;
        if (hover < 0)
        {
            if (_clickLeft && Off(Bounds())) _backOut = true;
            _clickLeft = false;
        }
        else if (hover != _drawnCursor)
        {
            // One row a read, toward the pointer. A click waits for the cursor to
            // arrive and confirms there.
            if (_pushes < MaxPushes)
            {
                add = hover > _drawnCursor ? PadDown : PadUp;
                _pushedFrom = _drawnCursor;
                _pushes++;
            }
            else _clickLeft = false;
        }
        else if (_clickLeft)
        {
            _clickLeft = false;
            add = _values ? PadRight : m.ReadU32(ConfirmMask);
            _confirms++;
        }

        if (add != 0)
        {
            c.V0 |= add;
            _injects++;
        }
        Report();
    }

    // ------------------------------------------------------------------------
    // Calling back into the game
    // ------------------------------------------------------------------------

    /// <summary>The menu's own sound, <c>func_8002792C</c>. It clobbers the
    /// argument registers, V0 and RA, which are put back.</summary>
    static void Sound(CpuContext c, IMemory m, uint which)
    {
        uint v0 = c.V0, a0 = c.A0, a1 = c.A1, a2 = c.A2, a3 = c.A3, ra = c.RA;
        c.A0 = which;
        Game.func_8002792C(c, m);
        (c.V0, c.A0, c.A1, c.A2, c.A3, c.RA) = (v0, a0, a1, a2, a3, ra);
    }

    /// <summary><c>func_80027A9C(item)</c>, what the stepper's move arm calls:
    /// the item shown beside the list, and the quantity reset to 1.</summary>
    static void Preview(CpuContext c, IMemory m, uint item)
    {
        uint v0 = c.V0, a0 = c.A0, a1 = c.A1, a2 = c.A2, a3 = c.A3, ra = c.RA;
        c.A0 = item;
        Game.func_80027A9C(c, m);
        (c.V0, c.A0, c.A1, c.A2, c.A3, c.RA) = (v0, a0, a1, a2, a3, ra);
    }

    // ------------------------------------------------------------------------
    // The pointer
    // ------------------------------------------------------------------------

    /// <summary>Where the pointer is, whether it moved, and what is pressed.
    /// ImGui's position is the last presented frame's, in OutputView's space.
    /// Nothing while the pointer is captured: its position is then a virtual
    /// one.</summary>
    static void Sample()
    {
        _samples++;
        _hover = -1;
        _inPicture = false;
        _gameX = _gameY = float.NaN;

        if (_fake is { } fake)
        {
            if (fake != _lastPos) { _movedAt = Environment.TickCount64; _padOwns = false; _lastPos = fake; _pushes = 0; }
            if (_fakeLeft) { _clickLeft = true; _fakeLeft = false; _movedAt = Environment.TickCount64; _padOwns = false; }
            if (_fakeRight) { _clickRight = true; _fakeRight = false; }
            _inPicture = true;
            (_gameX, _gameY) = (fake.X, fake.Y);
            return;
        }

        if (ImGui.GetCurrentContext() == IntPtr.Zero || PopupManager.AnyOpen || Mouse.Captured) return;

        var pos = ImGui.GetIO().MousePos;
        if (pos != _lastPos && !float.IsNaN(pos.X))
        {
            if (!float.IsNaN(_lastPos.X)) { _movedAt = Environment.TickCount64; _padOwns = false; _pushes = 0; }
            _lastPos = pos;
        }

        bool left = Down(HostMouseButton.Left), right = Down(HostMouseButton.Right);
        if (left && !_leftWas) { _clickLeft = true; _movedAt = Environment.TickCount64; _padOwns = false; }
        if (right && !_rightWas) _clickRight = true;
        _leftWas = left;
        _rightWas = right;

        Point(pos);
    }

    /// <summary>The pointer in the game's own pixels. The presented picture is
    /// <c>GameW + 2*margin</c> game pixels wide with the game's column 0 at the
    /// margin, so the margin comes off after the scale.</summary>
    static void Point(Vector2 pos)
    {
        if (!OutputView.Valid) return;
        var g0 = OutputView.Min;
        var size = OutputView.Size;
        int gameW = OutputView.GameW, gameH = OutputView.GameH;
        if (size.X < 32f || size.Y < 32f || gameW <= 0 || gameH <= 0) return;
        if (pos.X < g0.X || pos.X > OutputView.Max.X || pos.Y < g0.Y || pos.Y > OutputView.Max.Y) return;
        // A panel floating in front of the picture has the pointer: its clicks
        // are not the menu's, which would confirm a row or back out under it.
        if (OutputView.Covered) return;

        int margin = Display.WideMargin(gameW);
        _inPicture = true;
        _gameX = (pos.X - g0.X) / size.X * (gameW + 2 * margin) - margin;
        _gameY = (pos.Y - g0.Y) / size.Y * gameH;
        if (_gameY < 0f || _gameY > gameH) { _inPicture = false; _gameX = _gameY = float.NaN; }
    }

    static bool In(int x, int y, int w, int h) =>
        _inPicture && _gameX >= x && _gameX < x + w && _gameY >= y && _gameY < y + h;

    /// <summary>The fixed widget's row under the pointer, or -1; the gutters between
    /// boxes are no row.</summary>
    static int Hit()
    {
        for (int i = 0; i < _rowCount; i++)
            if (In(_rows[i].X, _rows[i].Y, _rows[i].W, _rows[i].H)) { _hovers++; return i; }
        return -1;
    }

    static (int X, int Y, int W, int H)? Bounds()
    {
        if (_rowCount == 0) return null;
        int x0 = int.MaxValue, y0 = int.MaxValue, x1 = int.MinValue, y1 = int.MinValue;
        for (int i = 0; i < _rowCount; i++)
        {
            var r = _rows[i];
            x0 = Math.Min(x0, r.X); y0 = Math.Min(y0, r.Y);
            x1 = Math.Max(x1, r.X + r.W); y1 = Math.Max(y1, r.Y + r.H);
        }
        return (x0, y0, x1 - x0, y1 - y0);
    }

    /// <summary>Clear of a widget by <see cref="EdgeSlack"/> on every side: on a
    /// row confirms, near the rows does nothing, clear of them backs out.</summary>
    static bool Off((int X, int Y, int W, int H)? r) =>
        _inPicture && r is { } b &&
        (_gameX < b.X - EdgeSlack || _gameX >= b.X + b.W + EdgeSlack ||
         _gameY < b.Y - EdgeSlack || _gameY >= b.Y + b.H + EdgeSlack);

    /// <summary>The whole notches scrolled since the last call. Every stepper
    /// takes them, so a notch over a widget with no page is spent rather than
    /// fired at the next list to open.</summary>
    static int TakeWheel()
    {
        if (!HostWindow.MouseAvailable) return 0;
        long now = Environment.TickCount64;
        bool opening = now - _steppedAt > WidgetGapMs;
        _steppedAt = now;

        _wheel += HostWindow.TakeMouseWheel();
        if (opening) { _wheel = 0f; return 0; }

        int notches = (int)_wheel;
        _wheel -= notches;
        if (notches != 0) { _movedAt = now; _padOwns = false; }
        return notches;
    }

    static bool HoverLive() => !_padOwns && Environment.TickCount64 - _movedAt < IdleMs;

    static bool Down(HostMouseButton b) => HostWindow.MouseAvailable && HostWindow.IsMouseButtonDown(b);

    // ------------------------------------------------------------------------
    // The shell and the probe
    // ------------------------------------------------------------------------

    /// <summary>
    /// The shell's <c>point</c>: a pointer in game pixels instead of the host's, so
    /// a script can measure hover, a click and a back-out without a person.
    /// <c>point &lt;x&gt; &lt;y&gt;</c>, <c>point left</c>, <c>point right</c>,
    /// <c>point off</c> (the host's pointer again); alone, what the last sample saw.
    /// </summary>
    public static string Shell(string[] args)
    {
        static string Ok(string what) =>
            "{\"ok\":true,\"cmd\":\"point\",\"pointer\":" + (_fake is { } f ? $"[{f.X:0.#},{f.Y:0.#}]" : "\"host\"") +
            ",\"what\":\"" + what + "\",\"live\":\"" + _live + "\",\"row\":" + _hover + ",\"drawnCursor\":" + _drawnCursor +
            ",\"padOwns\":" + (_padOwns ? "true" : "false") + "}";

        if (args.Length == 0) return Ok("status");
        switch (args[0])
        {
            case "off": _fake = null; _lastPos = new(float.NaN, float.NaN); return Ok("off");
            case "left": _fakeLeft = true; return Ok("left");
            case "right": _fakeRight = true; return Ok("right");
        }
        if (args.Length >= 2 && float.TryParse(args[0], System.Globalization.CultureInfo.InvariantCulture, out float x)
            && float.TryParse(args[1], System.Globalization.CultureInfo.InvariantCulture, out float y))
        {
            _fake = new Vector2(x, y);
            return Ok("moved");
        }
        return "{\"ok\":false,\"cmd\":\"point\",\"error\":\"point [<x> <y>|left|right|off]\"}";
    }

    static void Report()
    {
        if (!_probe) return;
        double now = _clock.Elapsed.TotalMilliseconds;
        if (_windowMs < 0.0) { _windowMs = now; return; }
        double elapsed = now - _windowMs;
        if (elapsed < 1000.0) return;

        Console.WriteLine($"[KF3] menu pointer: {_samples * 1000.0 / elapsed:0.#} samples/s, " +
                          $"{(_inPicture ? $"game ({_gameX:0.#},{_gameY:0.#})" : "outside the picture")} " +
                          $"of {OutputView.GameW}x{OutputView.GameH} +{Display.WideMargin(Math.Max(OutputView.GameW, 1))} " +
                          $"-> {_live} row {_hover}, {(_padOwns ? "pad owns" : "pointer owns")}, " +
                          $"hovered {_hovers}, moved {_moves}, scrolled {_scrolls}, " +
                          $"confirmed {_confirms}, cancelled {_cancels}, injected {_injects}");
        Console.Out.Flush();
        _windowMs = now;
        _samples = _hovers = _moves = _scrolls = _confirms = _cancels = _injects = 0;
    }
}
