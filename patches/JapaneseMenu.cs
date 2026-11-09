using System.Diagnostics;
using System.Reflection;
using System.Text;
using RecompOne.Runtime.Context;
using RecompOne.Runtime.Events;
using RecompOne.Runtime.Memory;
using RecompOne.Runtime.Modding;
using Game = Recompiled.KingsField3_game;

namespace Kf3;

/// <summary>
/// The menu's labels in Japanese, for KF3_LANGUAGE=ja: the words and the places the
/// Japanese program puts them, drawn from the player's own Japanese disc. A label with a
/// Japanese version is drawn here, with the primitives the US label routine would build;
/// every other label, and every English run, goes through the original routine unchanged.
///
///     KF3_LANGUAGE=ja        the Japanese labels on (with the layer, JapaneseDisc)
///     KF3_JP_PROBE=1         log each label that has no Japanese version the first
///                            time it is drawn, what the build left out, and a count
///                            of Japanese draws every 5 s
///
/// The label routine is func_800261DC(template, record): the record is an s16 X, an
/// s16 Y and the string at +4. The US routine draws each character twice: the text at
/// (pen - 1, Y - 1) and the shadow at (pen, Y), the pen 7 px on. The Japanese routine
/// (0x80025DAC in the Japanese GAME.EXE) draws the same pair per code with a 14 px pen,
/// and for a code with 0x1000 or 0x2000 set a dakuten (cell 0x2E) or handakuten (0x2F)
/// at pen + 10 and pen + 11. Both are copied here with the US primitives: the cell is
/// 14 x 15 px and the glyph's cell is in the atlas (<see cref="JapaneseFontAtlas"/>).
/// The text takes the label's own colour from the allocator, the shadow the byte at gp+0x68.
///
/// Which Japanese words a label gets depends on where it is drawn from, because the
/// same US word can mean different things (FLAME is a sword and a spell):
///
///     a record in a list group         that record's Japanese one, by address
///     a call in the site table         the records the Japanese call builds, by call
///     a record of a card message       the Japanese builder's own, already in memory
///     one of a choice's two records    the Japanese pair, by call and argument
///     a row of a list                  by the list: items, spells, and the words
///
/// The card messages also take the Japanese window's line count and size, because the
/// Japanese program splits its sentences over other lines. See docs/JAPANESE.md.
/// </summary>
public static class JapaneseMenu
{
    /// <summary>func_800261DC(template, record), the label routine, in GAME.EXE.</summary>
    const uint LabelRoutine = 0x800261DC;

    /// <summary>func_80026158: the label routine in the unselected colours. Calls the label routine.</summary>
    const uint GreyLabelRoutine = 0x80026158;

    /// <summary>The return address of the label call inside <see cref="GreyLabelRoutine"/>.</summary>
    const uint GreyLabelReturn = 0x800261B4;

    /// <summary>func_80025B24(record, record, selected): draws two choices.</summary>
    const uint ChoiceRoutine = 0x80025B24;

    /// <summary>The return addresses of the list routine's two label calls (func_80025468).</summary>
    const uint ListRowReturn = 0x800255AC, ListSecondColumnReturn = 0x80025600;

    /// <summary>The return address of the list routine's title label, a copy of a list group's record.</summary>
    const uint ListTitleReturn = 0x800254E0;

    /// <summary>func_80027530: submits the sprite the caller built to the ordering table.</summary>
    const uint SubmitSprite = 0x80027530;

    /// <summary>The return addresses of status page 1's five submits of a magic attribute's icon. The Japanese
    /// status page (0x800229EC) draws a kanji at those places instead and has no icon.</summary>
    static readonly HashSet<uint> AttributeIconReturns = [0x80022CA0, 0x80022D54, 0x80022E14, 0x80022EC8, 0x80022F84];

    /// <summary>List kinds (func_80025468's a1 &amp; 0xFFF, live in s4) filled from the spell table.</summary>
    static readonly HashSet<uint> SpellListKinds = [2, 4, 0x10];

    /// <summary>The equipment page: its second column shows a spell in row 1, and in row 9 when the
    /// button's slot holds one (the byte at <see cref="ButtonSlotSpell"/> is not 0xFF).</summary>
    const uint EquipmentKind = 3;
    const uint ButtonSlotSpell = 0x801B25AC;
    const int SpellRow = 1, ButtonRow = 9;

    /// <summary>The US label font's template: its tpage at +0, CLUT at +2, the cell size at +8 and +10 (7 x 15 for US).</summary>
    const uint LabelTemplate = 0x8007E570;

    const int RecordText = 4;             // the string follows the record's s16 X and s16 Y
    const int MaxTextBytes = 24;          // a record's text field; a US string longer than this is not a table's
    const int MessageRecordBytes = 28;
    const int MaxMessageCodes = 12;

    const int GpPrimitive = 356;          // gp+0x164: the primitive the allocator just took
    const int GpShadowColour = 104;       // gp+0x68: the shadow's colour byte

    const int LayerText = 2, LayerShadow = 3;    // the ordering layers func_800275E0 submits to (its a0)
    const int SetupLayer = 3;                    // a0 of func_80027610, the setup before a label's first cell

    const int CellWidth = 14, CellHeight = 15;   // a Japanese cell's drawn size, in px
    const int PenStep = 14;                      // px from one code to the next
    const int TextLeft = -1, TextTop = -1;       // the text pass's offset from the pen and the record's Y
    const int DakutenLeft = 10;                  // the dakuten's text pass, from the pen
    const int AccentShadowLeft = 11;             // the accent's shadow pass, from the pen (one px right of its text)
    const int AccentShadowTop = 0;               // the accent's shadow, from the record's Y

    const int DakutenBit = 0x1000, HandakutenBit = 0x2000, GlyphMask = 0xFFF;
    const int DakutenGlyph = 0x2E, HandakutenGlyph = 0x2F;
    const int BlankGlyph = 0xFF;                 // the empty cell Japan pads a right-aligned word with

    const uint Stack10 = 0x10, Stack14 = 0x14, Stack18 = 0x18;
    const ushort JpEnd = 0xFFFF;
    const int MaxBuffers = 8;

    const int ReportEveryMs = 5000;
    const byte TextEnd = 0xFF;

    enum Pass { Text, Shadow }

    /// <summary>Card message records written in Japanese, with their window's arguments.</summary>
    sealed record MessageBuffer(uint Address, byte[] Bytes, JapaneseMenuText.WindowArgs Window);

    static readonly ModInfo _self = new()
    {
        Id = "kf3.japanesemenu",
        Name = "Japanese menu text",
        Version = "1.0",
        Description = "Draws the menu's labels in Japanese when the Japanese layer is on (KF3_LANGUAGE=ja).",
    };

    static bool _probe, _queued, _built;
    static JapaneseMenuText.Map? _map;
    static JapaneseProgram? _program;
    static JapaneseFontAtlas? _atlas;
    static readonly List<MessageBuffer> _buffers = [];
    static (uint Record0, uint Record1, uint Call)? _choice;
    static uint _greyCall;
    static long _vblank;
    static int _japaneseDraws, _englishDraws, _reportedDraws, _iconsLeftOut;
    static double _nextReportMs;
    static readonly HashSet<string> _untranslated = new(StringComparer.Ordinal);
    static readonly HashSet<string> _translated = new(StringComparer.Ordinal);
    static readonly Stopwatch _clock = Stopwatch.StartNew();

    public static void Install()
    {
        _probe = Verdite.Core.Game.Env("JP_PROBE") == "1";
        HookAttach.OnOverlayLoad("japanese menu", Attach);
    }

    static bool Attach()
    {
        SymbolRegistry.Build();
        var label = SymbolRegistry.Resolve("game", null, LabelRoutine);
        if (label == null) return false;

        var hooks = new List<(MethodInfo Target, string Impl)> { (label, nameof(Label)) };
        var others = new (uint Address, string Impl)[]
        {
            (GreyLabelRoutine, nameof(GreyLabel)),
            (ChoiceRoutine, nameof(Choice)),
            (SubmitSprite, nameof(Sprite)),
            (JapaneseMenuText.UsWindowA, nameof(WindowA)),
            (JapaneseMenuText.UsWindowB, nameof(WindowB)),
        };
        foreach (var (address, impl) in others.Concat(JapaneseMenuText.MessageBuilders.Select(pair => (pair.Us, nameof(MessageBuilder)))))
        {
            var target = SymbolRegistry.Resolve("game", null, address);
            if (target == null) return false;
            hooks.Add((target, impl));
        }

        if (!_queued)
        {
            _queued = true;
            foreach (var (target, impl) in hooks)
                HookManager.AddReplace(_self, target, typeof(JapaneseMenu).GetMethod(impl, BindingFlags.Public | BindingFlags.Static)!);
        }
        HookManager.Commit();
        bool ok = hooks.All(hook => HookAttach.Installed(hook.Target));
        if (!ok) Console.Error.WriteLine("[KF3] japanese menu: a hook did not install; labels stay English");
        return ok;
    }

    /// <summary>
    /// func_800261DC with the Japanese layer on: drawn in Japanese when the label has a
    /// version, through the original otherwise.
    /// </summary>
    public static void Label(Action<CpuContext, IMemory> orig, CpuContext c, IMemory m)
    {
        if (!JapaneseDisc.Active || c.A0 != LabelTemplate || !Ready()) { orig(c, m); return; }

        if (MessageRecord(m, c.A1) is { } message)
        {
            DrawLabels(c, m, [message]);
            return;
        }

        string text = ReadText(m, c.A1 + RecordText);
        if (text.Length == 0) { orig(c, m); return; }

        var labels = Lookup(c, m, text);
        if (labels is null)
        {
            NoteUntranslated(c, text);
            orig(c, m);
            return;
        }

        DrawLabels(c, m, labels);
        NoteTranslated(text);
    }

    /// <summary>func_80026158: remembers who called it, so the label inside can be found by that call.</summary>
    public static void GreyLabel(Action<CpuContext, IMemory> orig, CpuContext c, IMemory m)
    {
        uint previous = _greyCall;
        _greyCall = c.RA;
        orig(c, m);
        _greyCall = previous;
    }

    /// <summary>func_80025B24: remembers its two records and its caller for the labels inside.</summary>
    public static void Choice(Action<CpuContext, IMemory> orig, CpuContext c, IMemory m)
    {
        var previous = _choice;
        _choice = (c.A0, c.A1, c.RA - 8);
        orig(c, m);
        _choice = previous;
    }

    /// <summary>func_80027530: leaves out the status page's attribute icons when their places hold the Japanese kanji.</summary>
    public static void Sprite(Action<CpuContext, IMemory> orig, CpuContext c, IMemory m)
    {
        if (AttributeIconReturns.Contains(c.RA) && JapaneseDisc.Active && Ready())
        {
            _iconsLeftOut++;
            return;
        }
        orig(c, m);
    }

    /// <summary>func_8001EB3C: the card window with its records in a1.</summary>
    public static void WindowA(Action<CpuContext, IMemory> orig, CpuContext c, IMemory m)
    {
        if (JapaneseDisc.Active && Ready()) PrepareWindow(c, m, c.A1);
        orig(c, m);
    }

    /// <summary>func_80020FE8: the card window with its records in a0.</summary>
    public static void WindowB(Action<CpuContext, IMemory> orig, CpuContext c, IMemory m)
    {
        if (JapaneseDisc.Active && Ready()) PrepareWindow(c, m, c.A0);
        orig(c, m);
    }

    /// <summary>A card message routine: its records are the Japanese builder's, written where the US ones would go.</summary>
    public static void MessageBuilder(Action<CpuContext, IMemory> orig, CpuContext c, IMemory m)
    {
        if (!JapaneseDisc.Active || !Ready()) { orig(c, m); return; }
        uint usCall = c.RA - 8;
        if (!_map!.BuilderCalls.TryGetValue(usCall, out uint builder)) { orig(c, m); return; }

        var (bytes, v0) = JapaneseMenuText.BuildMessage(_program!, builder, c.A1, c.A2, c.A3);
        if (bytes.Count == 0) { orig(c, m); return; }

        foreach (var (offset, value) in bytes) m.WriteU8(c.A0 + (uint)offset, value);
        if (v0 is { } result) c.V0 = result;
        if (_probe) Console.WriteLine($"[KF3] japanese menu: card message from call 0x{usCall:X8}, {bytes.Keys.Max() + 1} bytes at 0x{c.A0:X8}");
        RegisterBuffer(m, c.A0, bytes.Keys.Max() + 1, _map.BuilderWindows[usCall]);
    }

    static void PrepareWindow(CpuContext c, IMemory m, uint records)
    {
        if (BufferAt(m, records) is not { } buffer && (buffer = InlineWindow(c, m, records)) is null) return;
        ApplyWindow(c, m, buffer.Window);
    }

    /// <summary>The one window whose record the caller builds in line: the Japanese record goes over it.</summary>
    static MessageBuffer? InlineWindow(CpuContext c, IMemory m, uint records)
    {
        if (c.RA - 8 != JapaneseMenuText.UsInlineWindowCall || _map!.InlineWindow is not { } inline) return null;

        var record = inline.Record;
        m.WriteU16(records, unchecked((ushort)(record.X ?? (short)m.ReadU16(records))));
        m.WriteU16(records + 2, unchecked((ushort)(record.Y ?? (short)m.ReadU16(records + 2))));
        uint at = records + RecordText;
        foreach (ushort code in record.Codes)
        {
            m.WriteU16(at, code);
            at += 2;
        }
        m.WriteU16(at, JpEnd);
        return RegisterBuffer(m, records, MessageRecordBytes, inline.Window);
    }

    static void ApplyWindow(CpuContext c, IMemory m, JapaneseMenuText.WindowArgs window)
    {
        if (window.A1 is { } a1) c.A1 = a1;
        if (window.A2 is { } a2) c.A2 = a2;
        if (window.A3 is { } a3) c.A3 = a3;
        if (window.Stack10 is { } s10) m.WriteU32(c.SP + Stack10, s10);
        if (window.Stack14 is { } s14) m.WriteU32(c.SP + Stack14, s14);
        if (window.Stack18 is { } s18) m.WriteU32(c.SP + Stack18, s18);
    }

    static MessageBuffer RegisterBuffer(IMemory m, uint address, int length, JapaneseMenuText.WindowArgs window)
    {
        var bytes = new byte[length];
        for (int i = 0; i < length; i++) bytes[i] = m.ReadU8(address + (uint)i);
        _buffers.RemoveAll(buffer => buffer.Address == address);
        var buffer = new MessageBuffer(address, bytes, window);
        _buffers.Add(buffer);
        if (_buffers.Count > MaxBuffers) _buffers.RemoveAt(0);
        return buffer;
    }

    /// <summary>The Japanese records at an address, if they are still what was written: a stack
    /// address is reused, so an old registration must not match another caller's records.</summary>
    static MessageBuffer? BufferAt(IMemory m, uint address)
    {
        foreach (var buffer in _buffers)
            if (buffer.Address == address && StillHolds(m, buffer)) return buffer;
        return null;
    }

    static bool StillHolds(IMemory m, MessageBuffer buffer)
    {
        for (int i = 0; i < buffer.Bytes.Length; i++)
            if (m.ReadU8(buffer.Address + (uint)i) != buffer.Bytes[i]) return false;
        return true;
    }

    /// <summary>The Japanese label at a record inside a card message's records, or null.</summary>
    static JapaneseMenuText.Label? MessageRecord(IMemory m, uint record)
    {
        foreach (var buffer in _buffers)
        {
            if (record < buffer.Address || record >= buffer.Address + (uint)buffer.Bytes.Length) continue;
            if (!StillHolds(m, buffer)) return null;
            return ReadMessageRecord(m, record);
        }
        return null;
    }

    static JapaneseMenuText.Label? ReadMessageRecord(IMemory m, uint record)
    {
        var codes = new List<ushort>();
        for (uint i = 0; i < MaxMessageCodes; i++)
        {
            ushort code = m.ReadU16(record + RecordText + i * 2);
            if (code == JpEnd) break;
            codes.Add(code);
        }
        return codes.Count == 0 ? null : new JapaneseMenuText.Label((short)m.ReadU16(record), (short)m.ReadU16(record + 2), codes.ToArray());
    }

    /// <summary>The Japanese labels for this draw, or null when it has none. The first holds the label's own place.</summary>
    static JapaneseMenuText.Label[]? Lookup(CpuContext c, IMemory m, string text)
    {
        var map = _map!;
        if (map.Records.TryGetValue(c.A1, out var record)) return [record];
        if (ChoiceLabel(c, text) is { } choice) return [choice];

        uint call = c.RA == GreyLabelReturn ? _greyCall : c.RA;
        if (map.Sites.TryGetValue(call - 8, out var sited)) return sited;
        if (c.RA == ListTitleReturn && map.Titles.TryGetValue(text, out var title)) return [new JapaneseMenuText.Label(null, null, title)];
        if (ListCodes(c, m, text) is { } listed) return [new JapaneseMenuText.Label(null, null, listed)];
        return map.Words.TryGetValue(text, out var word) ? [new JapaneseMenuText.Label(null, null, word)] : null;
    }

    static JapaneseMenuText.Label? ChoiceLabel(CpuContext c, string text)
    {
        if (_choice is not { } choice) return null;
        int argument = c.A1 == choice.Record0 ? 0 : c.A1 == choice.Record1 ? 1 : -1;
        if (argument < 0 || !_map!.Choices.TryGetValue((choice.Call, argument), out var words)) return null;
        if (words.TryGetValue(text, out var label) || words.TryGetValue("", out label)) return label;
        return null;
    }

    /// <summary>A list row's name: spells first when this row of this list holds a spell, else items.</summary>
    static ushort[]? ListCodes(CpuContext c, IMemory m, string text)
    {
        if (c.RA != ListRowReturn && c.RA != ListSecondColumnReturn) return null;
        var map = _map!;
        var (first, second) = RowHoldsSpell(c, m) ? (map.Spells, map.Items) : (map.Items, map.Spells);
        if (first.TryGetValue(text, out var codes) || second.TryGetValue(text, out codes)) return codes;
        return null;
    }

    static bool RowHoldsSpell(CpuContext c, IMemory m)
    {
        uint kind = c.S4;
        if (kind != EquipmentKind) return SpellListKinds.Contains(kind);
        if (c.RA != ListSecondColumnReturn) return false;
        int row = (int)c.S5;
        return row == SpellRow || row == ButtonRow && m.ReadU8(ButtonSlotSpell) != 0xFF;
    }

    /// <summary>Builds the map and the atlas the first time a label is drawn in Japanese.
    /// True when both are ready; a failure is logged once and the labels stay English.</summary>
    static bool Ready()
    {
        if (_built) return _atlas is not null;
        _built = true;
        Build();
        return _atlas is not null;
    }

    static void Build()
    {
        try
        {
            var usExe = ReadRequired(JapaneseDisc.ReadUsFile, "GAME.EXE", "US");
            var jpExe = ReadRequired(JapaneseDisc.ReadJapaneseFile, "GAME.EXE", "Japanese");
            var usFdat = ReadRequired(JapaneseDisc.ReadUsFile, "CD/COM/FDAT.T", "US");
            var jpFdat = ReadRequired(JapaneseDisc.ReadJapaneseFile, "CD/COM/FDAT.T", "Japanese");

            JapaneseMenuText.RequireMeasuredExes(usExe, jpExe);
            _map = JapaneseMenuText.Build(usExe, jpExe);
            _program = new JapaneseProgram(jpExe);
            _atlas = JapaneseFontAtlas.Plan(usFdat, jpFdat);
            Event.AddListener<VSyncEvent>(vsync => _vblank = vsync.Frame);
            Console.WriteLine($"[KF3] japanese menu: {_map.LabelCount} label(s) in Japanese, {_map.Skipped.Count} left out; {_atlas.FreeCount} free atlas cell(s)");
            if (!_probe) return;
            foreach (var duplicate in _map.Duplicates)
                Console.WriteLine($"[KF3] japanese menu: duplicate US text {duplicate}; the first Japanese kept");
            foreach (var skipped in _map.Skipped)
                Console.WriteLine($"[KF3] japanese menu: left out: {skipped}");
            foreach (var line in JapaneseMenuText.Describe(_map))
                Console.WriteLine($"[KF3] japanese menu: {line}");
        }
        catch (Exception e) when (e is InvalidDataException or IOException)
        {
            _map = null;
            _atlas = null;
            Console.WriteLine($"[KF3] japanese: menu text stays English: {e.Message}");
        }
    }

    static byte[] ReadRequired(Func<string, byte[]?> read, string path, string disc) =>
        read(path) ?? throw new InvalidDataException($"the {disc} disc's {path} cannot be read");

    static void DrawLabels(CpuContext c, IMemory m, JapaneseMenuText.Label[] labels)
    {
        uint template = c.A0, record = c.A1;
        short recordX = (short)m.ReadU16(record), recordY = (short)m.ReadU16(record + 2);
        foreach (var label in labels)
        {
            c.A0 = template;
            Draw(c, m, template, label.X ?? recordX, label.Y ?? recordY, label.Codes);
        }
        _japaneseDraws++;
        ReportEvery();
    }

    /// <summary>The label routine's primitives for a string of Japanese codes: each code's
    /// text and shadow, and the accent passes.</summary>
    static void Draw(CpuContext c, IMemory m, uint template, int x, int y, ushort[] codes)
    {
        c.A0 = SetupLayer;
        c.A1 = (uint)(short)m.ReadU16(template);
        Game.func_80027610(c, m);

        for (int i = 0; i < codes.Length; i++)
            DrawCode(c, m, template, x + i * PenStep, y, codes[i]);
    }

    static void DrawCode(CpuContext c, IMemory m, uint template, int pen, int top, ushort code)
    {
        int glyph = code & GlyphMask;
        if (glyph == BlankGlyph) return;
        DrawCell(c, m, template, Pass.Text, glyph, pen + TextLeft, top + TextTop);
        DrawCell(c, m, template, Pass.Shadow, glyph, pen, top);

        bool dakuten = (code & DakutenBit) != 0;
        if (!dakuten && (code & HandakutenBit) == 0) return;
        int accent = dakuten ? DakutenGlyph : HandakutenGlyph;
        DrawCell(c, m, template, Pass.Text, accent, pen + DakutenLeft, top + TextTop);
        DrawCell(c, m, template, Pass.Shadow, accent, pen + AccentShadowLeft, top + AccentShadowTop);
    }

    /// <summary>One cell: the allocator takes a primitive, the cell's fields are set, and it is submitted to
    /// the pass's layer. The text's colour comes from the allocator; the shadow's from gp+0x68.</summary>
    static void DrawCell(CpuContext c, IMemory m, uint template, Pass pass, int glyph, int x, int y)
    {
        var gpu = RecompOne.Runtime.Runtime.Gpu;
        if (gpu is null || _atlas is null) return;
        var (u, v) = _atlas.Cell(glyph, _vblank, gpu.Vram, gpu.WriteGp0);

        Game.func_80027560(c, m);
        uint prim = m.ReadU32(c.GP + GpPrimitive);
        m.WriteU16(prim + 14, m.ReadU16(template + 2));
        m.WriteU16(prim + 8, unchecked((ushort)x));
        m.WriteU16(prim + 10, unchecked((ushort)y));
        m.WriteU8(prim + 12, u);
        m.WriteU8(prim + 13, v);
        m.WriteU16(prim + 16, (ushort)CellWidth);
        m.WriteU16(prim + 18, (ushort)CellHeight);
        if (pass == Pass.Shadow)
        {
            byte shade = m.ReadU8(c.GP + GpShadowColour);
            m.WriteU8(prim + 4, shade);
            m.WriteU8(prim + 5, shade);
            m.WriteU8(prim + 6, shade);
        }
        c.A0 = pass == Pass.Text ? (uint)LayerText : LayerShadow;
        Game.func_800275E0(c, m);
    }

    /// <summary>A label's US string at a record: its bytes to 0xFF, at most <see cref="MaxTextBytes"/>.</summary>
    static string ReadText(IMemory m, uint at)
    {
        Span<byte> bytes = stackalloc byte[MaxTextBytes];
        int length = 0;
        while (length < MaxTextBytes)
        {
            byte b = m.ReadU8(at + (uint)length);
            if (b == TextEnd) break;
            bytes[length++] = b;
        }
        return Encoding.Latin1.GetString(bytes[..length]);
    }

    static void NoteUntranslated(CpuContext c, string text)
    {
        _englishDraws++;
        if (!_probe || !_untranslated.Add(text)) return;
        Console.WriteLine($"[KF3] japanese menu: no Japanese for \"{JapaneseMenuText.LabelText(text)}\" (first draw, return 0x{c.RA:X8}, record 0x{c.A1:X8}, list kind {c.S4})");
    }

    static void NoteTranslated(string text)
    {
        if (!_probe || !_translated.Add(text)) return;
        Console.WriteLine($"[KF3] japanese menu: drawn in Japanese \"{JapaneseMenuText.LabelText(text)}\" (first draw)");
    }

    /// <summary>Under the probe, a line every 5 s while the Japanese draw count moves.</summary>
    static void ReportEvery()
    {
        if (!_probe) return;
        double now = _clock.Elapsed.TotalMilliseconds;
        if (now < _nextReportMs || _japaneseDraws == _reportedDraws) return;
        _nextReportMs = now + ReportEveryMs;
        _reportedDraws = _japaneseDraws;
        Console.WriteLine($"[KF3] japanese menu: {_japaneseDraws} label(s) drawn in Japanese, {_englishDraws} through the original, " +
                          $"{_atlas?.Writes} atlas cell write(s), {_atlas?.ForcedEvictions} forced, {_untranslated.Count} distinct without Japanese, {_iconsLeftOut} attribute icon(s) left out");
    }
}
