using System.Security.Cryptography;
using System.Text;

namespace Kf3;

/// <summary>
/// The menu's words and where they go, from the two GAME.EXE files. Only addresses,
/// strides and counts are in the code; every word and position is read from the player's
/// own discs when the map is built, partly from tables and partly by reading what the
/// Japanese menu code would build (<see cref="JapaneseProgram"/>).
///
/// A US string is bytes ending in 0xFF; a Japanese one is u16 codes ending in 0xFFFF,
/// each code a cell of the label font (<see cref="JapaneseFontAtlas"/>). A list record
/// holds its X and Y first, so its text is at +4; an item or spell name is at +0.
///
/// The US and Japanese programs have the same menu routines in the same order, so a call
/// in one has a counterpart in the other. Those pairs are the tables below: a label drawn
/// from a call in <see cref="SiteGroups"/> takes the Japanese record its counterpart builds
/// (docs/JAPANESE.md, "The menu's text").
/// </summary>
internal static class JapaneseMenuText
{
    /// <summary>GAME.EXE's file offset 0 is this address: the start of the PS-X header.</summary>
    const uint ExeBaseAddress = 0x80010800;
    const byte UsEnd = 0xFF;
    const ushort JpEnd = 0xFFFF;

    /// <summary>The last cell a Japanese code may name: 16 rows of 15 px in the 256-px font page.</summary>
    internal const int MaxGlyph = 0x10F;

    /// <summary>SHA-256 of the two GAME.EXE files the tables were measured in: the US program, and
    /// Japanese Rev 1. Rev 0 keeps its text tables at other addresses (its records read as other
    /// text), so Rev 0 is refused here rather than read at these addresses.</summary>
    internal const string UsExeSha256 = "7556ec7fa3324f39df483f5383bb8f6fcef5ba3b88a6dd4e895e366edf6b83d9";
    internal const string JapaneseRev1ExeSha256 = "9d9fb8d2b1e6d8f7ccce7af4a17e951e48d684ca89d73deca08a548e5ae70948";

    /// <summary>One table: where its US and Japanese records start, their strides in bytes, and how many records.</summary>
    internal readonly record struct Table(string Name, uint UsAddress, int UsStride, uint JpAddress, int JpStride, int Count, int TextAt);

    /// <summary>Every table with both addresses known. A list group's nine records are the same shape at a 252-byte step.</summary>
    internal static readonly Table[] Tables =
    [
        new("list group 0, main page", 0x8007E660, 28, 0x8007E324, 28, 9, 4),
        new("list group 1, status and records", 0x8007E75C, 28, 0x8007E420, 28, 9, 4),
        new("list group 2, storage", 0x8007E858, 28, 0x8007E51C, 28, 9, 4),
        new("list group 3, system", 0x8007E954, 28, 0x8007E618, 28, 9, 4),
        new("list group 4, option 1", 0x8007EA50, 28, 0x8007E714, 28, 9, 4),
        new("list group 5, option 2", 0x8007EB4C, 28, 0x8007E810, 28, 9, 4),
        new("list group 6, shop", 0x8007EC48, 28, 0x8007E90C, 28, 9, 4),
        new("list group 7, stay question", 0x8007ED44, 28, 0x8007EA08, 28, 9, 4),
        new("list group 8, blacksmith and appraisal", 0x8007EE40, 28, 0x8007EB04, 28, 9, 4),
    ];

    /// <summary>The item names, whose US strings are also two spells' names.</summary>
    static readonly Table ItemTable = new("item names", 0x8007F620, 24, 0x8007F214, 24, 154, 0);

    /// <summary>The spell names.</summary>
    static readonly Table SpellTable = new("spell names", 0x80080430, 24, 0x80080024, 24, 32, 0);

    /// <summary>Equipment slots, then TAKE OFF through the conditions, NORMAL and GOLD. The US field is padded
    /// with spaces and the Japanese one with code 0xFF to right-align, so the US spaces are part of the key.</summary>
    static readonly Table[] WordTables =
    [
        new("equipment slots", 0x8007F230, 24, 0x8007EEF4, 20, 10, 0),
        new("status words", 0x8007F338, 24, 0x8007EFD0, 20, 13, 0),
    ];

    /// <summary>A label call of the US program and the Japanese calls that draw the same words: the
    /// first in its place, the others (where Japan splits a line) beside it.</summary>
    internal readonly record struct Site(uint UsCall, uint[] JpCalls);

    /// <summary>The label calls of one menu routine, with the Japanese routine holding their counterparts.</summary>
    internal readonly record struct SiteGroup(uint JpFunction, Site[] Sites);

    /// <summary>Every label whose record the code builds in line. The condition word (the status page's
    /// 0x80023214) is not here: its record is copied from the status words by branch, and the words map serves it.</summary>
    internal static readonly SiteGroup[] SiteGroups =
    [
        new(0x800204FC,
        [
            new(0x80020834, [0x8002085C]),
            new(0x80020840, [0x80020868, 0x80020874]),
            new(0x8002084C, [0x80020880, 0x8002088C]),
        ]),
        new(0x800229EC,
        [
            new(0x8002288C, [0x80022A58]), new(0x80022948, [0x80022AE4]), new(0x800229AC, [0x80022B48]),
            new(0x80022A64, [0x80022C00]), new(0x80022B44, [0x80022CD4]), new(0x80022BE0, [0x80022D70]),
            new(0x80022C14, [0x80022D9C, 0x80022DC0, 0x80022E1C, 0x80022E84, 0x80022EE0, 0x80022F44]),
            new(0x8002300C, [0x80022FC0]), new(0x8002326C, [0x80023214]),
        ]),
        new(0x800232A4,
        [
            new(0x80023384, [0x80023314]), new(0x800233D4, [0x8002334C]), new(0x80023458, [0x800233BC]),
            new(0x800234C0, [0x80023414]), new(0x80023554, [0x80023480]), new(0x800235D8, [0x800234D0]),
            new(0x80023664, [0x80023520]), new(0x800236E8, [0x80023570]), new(0x80023774, [0x800235C0]),
            new(0x800237F8, [0x80023634]), new(0x80023840, [0x80023668]), new(0x800238B8, [0x800236C4]),
            new(0x80023920, [0x80023718]), new(0x80023998, [0x8002376C]), new(0x80023A1C, [0x800237D0]),
            new(0x80023AA0, [0x80023820]), new(0x80023B2C, [0x80023870]), new(0x80023BB0, [0x800238C0]),
            new(0x80023C3C, [0x80023910]),
        ]),
        new(0x80024390,
        [
            new(0x8002477C, [0x80024448]), new(0x800247A0, [0x80024460]), new(0x800247D0, [0x80024484]),
            new(0x800247F8, [0x800244A0]), new(0x8002481C, [0x800244B8]), new(0x80024844, [0x800244D4]),
            new(0x80024868, [0x800244EC]), new(0x80024890, [0x80024508]), new(0x800249BC, [0x800245EC]),
            new(0x800249D4, [0x80024604]), new(0x800249F0, [0x80024620]), new(0x80024A0C, [0x8002463C]),
        ]),
        new(0x80025998, [new(0x80025E28, [0x80025A00])]),
        new(0x80025A58, [new(0x80025EDC, [0x80025AAC])]),
    ];

    /// <summary>A call of the two-word choice routine (func_80025B24, records in a0 and a1). <paramref name="SeedRange"/> is
    /// how many values of a1 at the routine's entry select the words; 0 when the words do not depend on it.</summary>
    internal readonly record struct ChoiceCall(uint UsCall, uint UsFunction, uint JpFunction, uint JpCall, int SeedRange);

    internal static readonly ChoiceCall[] ChoiceCalls =
    [
        new(0x8001FA1C, 0x8001F8B4, 0x8001F7F4, 0x8001F93C, 0),
        new(0x80020810, 0x80020560, 0x800204FC, 0x80020838, 0),
        new(0x80024F3C, 0x80024C70, 0x800248A0, 0x80024B20, 10),
    ];

    /// <summary>The routines that fill a card message's records, each US one with the Japanese one that replaces it.</summary>
    internal static readonly (uint Us, uint Jp)[] MessageBuilders =
    [
        (0x8001FE9C, 0x8001FDC0), (0x8001FF94, 0x8001FED4), (0x8002090C, 0x8002094C), (0x8002098C, 0x80020A28),
        (0x80020A84, 0x80020B6C), (0x80020B7C, 0x80020CB0), (0x80020C74, 0x80020DF0), (0x80020D6C, 0x80020F34),
        (0x80020DEC, 0x80021014), (0x80020E6C, 0x80021084),
    ];

    /// <summary>Each call of a message builder with its Japanese counterpart: the window's size is
    /// read from the code after the Japanese call, up to the window routine.</summary>
    internal static readonly (uint Us, uint Jp)[] MessageBuilderCalls =
    [
        (0x8001E774, 0x8001E724), (0x8001E92C, 0x8001E8DC), (0x8001E978, 0x8001E928), (0x8001FA90, 0x8001F9B0),
        (0x8001FAFC, 0x8001FA1C), (0x8001FB8C, 0x8001FAAC), (0x8001FDDC, 0x8001FCFC), (0x8001FE28, 0x8001FD48),
        (0x800200F8, 0x800200A8), (0x80020400, 0x800203B0), (0x80020410, 0x800203C0), (0x80020454, 0x80020404),
        (0x80020490, 0x80020440), (0x800204DC, 0x8002048C), (0x800204FC, 0x8002049C),
    ];

    /// <summary>The two window routines in each program. The first takes its records in a1, the second in a0.</summary>
    internal const uint UsWindowA = 0x8001EB3C, UsWindowB = 0x80020FE8, JpWindowA = 0x8001EAEC, JpWindowB = 0x80021214;

    /// <summary>The one window call whose single record is built in line, not by a message builder.</summary>
    internal const uint UsInlineWindowCall = 0x8001E6BC, JpInlineWindowCall = 0x8001E66C, JpInlineWindowFunction = 0x8001E448;

    /// <summary>The label's text is at +4 of a record, after its s16 X and Y.</summary>
    const int RecordTextAt = 4;

    const int RegisterV0 = 2, RegisterA0 = 4, RegisterA1 = 5, RegisterA2 = 6, RegisterA3 = 7;
    const int StackArgument0 = 0x10;
    const int MaxRecordCodes = 12;

    /// <summary>A label's Japanese codes and position. A null position keeps the record's own.</summary>
    internal readonly record struct Label(short? X, short? Y, ushort[] Codes);

    /// <summary>A window call's arguments as the Japanese call sets them; null keeps the US value.</summary>
    internal readonly record struct WindowArgs(uint? A1, uint? A2, uint? A3, uint? Stack10, uint? Stack14, uint? Stack18);

    /// <summary>Everything the menu hooks look up. Duplicates names each US string a table left out because an earlier one held it.</summary>
    internal sealed class Map
    {
        public Dictionary<uint, Label> Records { get; } = new();

        /// <summary>A list page's title, by its US text: the list routine draws a copy of the group's
        /// record. A group's first record is its title, so those win over the same words further down a list.</summary>
        public Dictionary<string, ushort[]> Titles { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, ushort[]> Items { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, ushort[]> Spells { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, ushort[]> Words { get; } = new(StringComparer.Ordinal);
        public Dictionary<uint, Label[]> Sites { get; } = new();
        public Dictionary<(uint Call, int Argument), Dictionary<string, Label>> Choices { get; } = new();
        public Dictionary<uint, uint> BuilderCalls { get; } = new();
        public Dictionary<uint, WindowArgs> BuilderWindows { get; } = new();
        public (Label Record, WindowArgs Window)? InlineWindow { get; set; }
        public List<string> Duplicates { get; } = [];
        public List<string> Skipped { get; } = [];
        public int LabelCount => Records.Count + Items.Count + Spells.Count + Words.Count + Sites.Count + Choices.Count + BuilderCalls.Count;
    }

    /// <summary>Throws InvalidDataException unless both files are the ones the tables were measured in.</summary>
    internal static void RequireMeasuredExes(byte[] us, byte[] jp)
    {
        if (Hash(us) != UsExeSha256)
            throw new InvalidDataException("the US GAME.EXE is not the one the menu tables were measured in");
        if (Hash(jp) != JapaneseRev1ExeSha256)
            throw new InvalidDataException("the Japanese GAME.EXE is not Rev 1, whose menu tables are at these addresses (Rev 0 keeps them elsewhere)");
    }

    static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    internal static Map Build(byte[] us, byte[] jp)
    {
        var map = new Map();
        foreach (var table in Tables) AddRecords(table, us, jp, map);
        foreach (var table in Tables) AddTitles(table, us, jp, map, 0, 1);
        foreach (var table in Tables) AddTitles(table, us, jp, map, 1, table.Count);
        AddNames(ItemTable, us, jp, map.Items, map.Duplicates);
        AddNames(SpellTable, us, jp, map.Spells, map.Duplicates);
        foreach (var table in WordTables) AddNames(table, us, jp, map.Words, map.Duplicates);
        AddBuiltWords(jp, map);

        var japanese = new JapaneseProgram(jp);
        var american = new JapaneseProgram(us);
        foreach (var group in SiteGroups) AddSites(group, japanese, map);
        foreach (var choice in ChoiceCalls) AddChoice(choice, japanese, american, map);
        AddMessages(japanese, map);
        return map;
    }

    /// <summary>A list group's records, by the address of each US record: two tables can hold the same US word.</summary>
    static void AddRecords(Table table, byte[] us, byte[] jp, Map map)
    {
        for (int i = 0; i < table.Count; i++)
        {
            int usAt = Offset(table.UsAddress) + i * table.UsStride;
            int jpAt = Offset(table.JpAddress) + i * table.JpStride;
            if (UsText(us, usAt + table.TextAt, table.UsStride - table.TextAt) is null) continue;
            if (JpCodes(jp, jpAt + table.TextAt, table.JpStride - table.TextAt) is not { } codes) continue;
            short x = BitConverter.ToInt16(jp, jpAt), y = BitConverter.ToInt16(jp, jpAt + 2);
            map.Records[table.UsAddress + (uint)(i * table.UsStride)] = new Label(x, y, codes);
        }
    }

    static void AddTitles(Table table, byte[] us, byte[] jp, Map map, int first, int end)
    {
        for (int i = first; i < end; i++)
        {
            string? key = UsText(us, Offset(table.UsAddress) + i * table.UsStride + table.TextAt, table.UsStride - table.TextAt);
            if (key is null) continue;
            if (JpCodes(jp, Offset(table.JpAddress) + i * table.JpStride + table.TextAt, table.JpStride - table.TextAt) is { } codes)
                map.Titles.TryAdd(key, codes);
        }
    }

    static void AddNames(Table table, byte[] us, byte[] jp, Dictionary<string, ushort[]> names, List<string> duplicates)
    {
        for (int i = 0; i < table.Count; i++)
        {
            string? key = UsText(us, Offset(table.UsAddress) + i * table.UsStride + table.TextAt, table.UsStride - table.TextAt);
            if (key is null) continue;
            ushort[]? codes = JpCodes(jp, Offset(table.JpAddress) + i * table.JpStride + table.TextAt, table.JpStride - table.TextAt);
            if (codes is null) continue;
            if (!names.TryAdd(key, codes)) duplicates.Add($"{table.Name}: \"{LabelText(key)}\"");
        }
    }

    /// <summary>US "DATA n" and "NEW", which the slot list builds byte by byte. The Japanese codes are
    /// the immediates of the instructions that build them there.</summary>
    static void AddBuiltWords(byte[] jp, Map map)
    {
        ushort[] data = [Immediate(jp, 0x8001EA44), Immediate(jp, 0x8001EA50), Immediate(jp, 0x8001EA5C)];
        ushort digitBase = Immediate(jp, 0x8001EA70);
        for (int digit = 0; digit <= 9; digit++)
        {
            string key = new([(char)3, (char)0, (char)0x13, (char)0, (char)(0x20 + digit)]);
            map.Words[key] = [.. data, (ushort)(digitBase + digit)];
        }
        map.Words[new string([(char)0x0D, (char)4, (char)0x16])] =
            [Immediate(jp, 0x80020190), Immediate(jp, 0x80020198), Immediate(jp, 0x800201A0)];
    }

    /// <summary>The low 16 bits of the instruction at an address.</summary>
    static ushort Immediate(byte[] exe, uint address) => BitConverter.ToUInt16(exe, Offset(address));

    static void AddSites(SiteGroup group, JapaneseProgram japanese, Map map)
    {
        foreach (var site in group.Sites)
        {
            var labels = site.JpCalls.Select(call => RecordAtCall(japanese, group.JpFunction, call, RegisterA1)).ToList();
            if (labels.Any(label => label is null))
            {
                map.Skipped.Add($"label call 0x{site.UsCall:X8}: the Japanese record is not constant");
                continue;
            }
            map.Sites[site.UsCall] = labels.Select(label => label!.Value).ToArray();
        }
    }

    /// <summary>The record a call's a1 (or a0) points at, read straight through the Japanese routine.</summary>
    static Label? RecordAtCall(JapaneseProgram program, uint function, uint call, int register)
    {
        var trace = program.Run(function, (pc, _) => pc == call, straight: true);
        return trace.Reached && trace.Get(register) is { } address ? ReadJapaneseRecord(trace, address) : null;
    }

    /// <summary>A Japanese record: s16 X and Y, then u16 codes to 0xFFFF.</summary>
    static Label? ReadJapaneseRecord(JapaneseProgram.Trace trace, uint address)
    {
        var codes = new List<ushort>();
        for (uint i = 0; i < MaxRecordCodes; i++)
        {
            if (trace.HalfAt(address + RecordTextAt + i * 2) is not { } code) return null;
            if (code == JpEnd) break;
            codes.Add(code);
        }
        if (codes.Count == 0) return null;
        return new Label((short?)trace.HalfAt(address), (short?)trace.HalfAt(address + 2), codes.ToArray());
    }

    /// <summary>A US record's text, read the same way, for a call whose words depend on a value.</summary>
    static string? ReadUsText(JapaneseProgram.Trace trace, uint address)
    {
        var text = new StringBuilder();
        for (uint i = 0; i < 24; i++)
        {
            if (trace.ByteAt(address + RecordTextAt + i) is not { } b) return null;
            if (b == UsEnd) return text.ToString();
            text.Append((char)b);
        }
        return null;
    }

    /// <summary>The two records of a choice. With no seed the words are fixed and an empty key stands for
    /// any US text; with a seed each value of a1 gives one pair, keyed by the US words it picks.</summary>
    static void AddChoice(ChoiceCall choice, JapaneseProgram japanese, JapaneseProgram american, Map map)
    {
        var first = new Dictionary<string, Label>(StringComparer.Ordinal);
        var second = new Dictionary<string, Label>(StringComparer.Ordinal);
        map.Choices[(choice.UsCall, 0)] = first;
        map.Choices[(choice.UsCall, 1)] = second;

        if (choice.SeedRange == 0)
        {
            var trace = japanese.Run(choice.JpFunction, (pc, _) => pc == choice.JpCall, straight: true);
            if (trace.Get(RegisterA0) is { } a0 && ReadJapaneseRecord(trace, a0) is { } a) first[""] = a;
            if (trace.Get(RegisterA1) is { } a1 && ReadJapaneseRecord(trace, a1) is { } b) second[""] = b;
            return;
        }

        for (uint value = 0; value < choice.SeedRange; value++)
        {
            var jp = japanese.Run(choice.JpFunction, (pc, _) => pc == choice.JpCall, straight: false, (RegisterA1, value));
            var us = american.Run(choice.UsFunction, (pc, _) => pc == choice.UsCall, straight: false, (RegisterA1, value));
            AddChoiceWord(jp, us, RegisterA0, first);
            AddChoiceWord(jp, us, RegisterA1, second);
        }
    }

    static void AddChoiceWord(JapaneseProgram.Trace jp, JapaneseProgram.Trace us, int register, Dictionary<string, Label> words)
    {
        if (jp.Get(register) is not { } jpAt || us.Get(register) is not { } usAt) return;
        if (ReadJapaneseRecord(jp, jpAt) is { } label && ReadUsText(us, usAt) is { } key) words.TryAdd(key, label);
    }

    static void AddMessages(JapaneseProgram japanese, Map map)
    {
        var builderOf = MessageBuilders.ToDictionary(pair => pair.Us, pair => pair.Jp);
        foreach (var (usCall, jpCall) in MessageBuilderCalls)
        {
            uint? target = JapaneseCallTarget(japanese, jpCall);
            if (target is null || !builderOf.ContainsValue(target.Value))
            {
                map.Skipped.Add($"message builder call 0x{usCall:X8}: 0x{jpCall:X8} does not call a builder");
                continue;
            }
            map.BuilderCalls[usCall] = target.Value;
            map.BuilderWindows[usCall] = WindowArguments(japanese.Run(jpCall + 8, (_, t) => t == JpWindowA || t == JpWindowB, straight: false));
        }

        var inline = japanese.Run(JpInlineWindowFunction, (pc, _) => pc == JpInlineWindowCall, straight: true);
        if (inline.Reached && inline.Get(RegisterA1) is { } address && ReadJapaneseRecord(inline, address) is { } record)
            map.InlineWindow = (record, WindowArguments(inline));
        else
            map.Skipped.Add("the inline window's record is not constant");
    }

    static uint? JapaneseCallTarget(JapaneseProgram program, uint call)
    {
        uint found = 0;
        program.Run(call, (pc, target) => { found = pc == call ? target : 0; return true; }, straight: true);
        return found == 0 ? null : found;
    }

    /// <summary>A window call's arguments from a run stopped at it; only small constants count, not addresses.</summary>
    static WindowArgs WindowArguments(JapaneseProgram.Trace trace)
    {
        uint? Stack(int offset) => Small(WordAt(trace, JapaneseProgram.StackAddress + (uint)offset));
        return new WindowArgs(Small(trace.Get(RegisterA1)), Small(trace.Get(RegisterA2)), Small(trace.Get(RegisterA3)),
            Stack(StackArgument0), Stack(StackArgument0 + 4), Stack(StackArgument0 + 8));
    }

    static uint? WordAt(JapaneseProgram.Trace trace, uint address)
    {
        return trace.HalfAt(address) is { } low && trace.HalfAt(address + 2) is { } high ? (uint)(low | high << 16) : null;
    }

    static uint? Small(uint? value) => value is < 0x10000 ? value : null;

    /// <summary>Runs a Japanese message builder against <see cref="JapaneseProgram.DestinationAddress"/>: the bytes
    /// it writes, by offset from there, and v0 when known.</summary>
    internal static (SortedDictionary<int, byte> Bytes, uint? V0) BuildMessage(JapaneseProgram program, uint builder, uint a1, uint a2, uint a3)
    {
        var trace = program.Run(builder, null, straight: false,
            (RegisterA0, JapaneseProgram.DestinationAddress), (RegisterA1, a1), (RegisterA2, a2), (RegisterA3, a3));
        var bytes = new SortedDictionary<int, byte>();
        foreach (var (address, value) in trace.Written)
            if (address >= JapaneseProgram.DestinationAddress && address < JapaneseProgram.DestinationAddress + 0x1000)
                bytes[(int)(address - JapaneseProgram.DestinationAddress)] = value;
        return (bytes, trace.Get(RegisterV0));
    }

    /// <summary>What the map holds, for the probe's log: each resolved place with its codes.</summary>
    internal static IEnumerable<string> Describe(Map map)
    {
        static string Show(Label label) => $"({label.X},{label.Y}) {string.Join(' ', label.Codes.Select(code => code.ToString("x")))}";

        yield return $"{map.Records.Count} list record(s), {map.Items.Count} item(s), {map.Spells.Count} spell(s), {map.Words.Count} word(s), {map.Sites.Count} site(s), {map.BuilderCalls.Count} message call(s)";
        foreach (var (call, labels) in map.Sites.OrderBy(pair => pair.Key))
            yield return $"site 0x{call:X8}: {string.Join(" + ", labels.Select(Show))}";
        foreach (var (key, words) in map.Choices.OrderBy(pair => pair.Key))
            foreach (var (text, label) in words)
                yield return $"choice 0x{key.Call:X8}/{key.Argument} \"{LabelText(text)}\": {Show(label)}";
        foreach (var (call, window) in map.BuilderWindows.OrderBy(pair => pair.Key))
            yield return $"message call 0x{call:X8}: builder 0x{map.BuilderCalls[call]:X8}, window {window}";
        if (map.InlineWindow is { } inline) yield return $"inline window: {Show(inline.Record)}, {inline.Window}";
    }

    /// <summary>The US string at a field, or null when the field is empty, has no 0xFF in it, or is
    /// a lone zero byte. A lone zero is the letter A to the font, but in these tables it is an
    /// unused record (ids 18, 19, 25, 26; spell slots 3, 10, 14 read 00 FF), so it is not a name.</summary>
    static string? UsText(byte[] file, int at, int field)
    {
        var bytes = Field(file, at, field);
        int end = bytes.IndexOf(UsEnd);
        if (end <= 0 || (end == 1 && bytes[0] == 0)) return null;
        return Encoding.Latin1.GetString(bytes[..end]);
    }

    /// <summary>The Japanese codes at a field, or null when it is empty, has no 0xFFFF, or
    /// names a cell past the font page.</summary>
    static ushort[]? JpCodes(byte[] file, int at, int field)
    {
        var bytes = Field(file, at, field);
        var codes = new List<ushort>();
        for (int i = 0; i + 1 < bytes.Length; i += 2)
        {
            ushort code = (ushort)(bytes[i] | bytes[i + 1] << 8);
            if (code == JpEnd) return codes.Count == 0 ? null : codes.ToArray();
            if ((code & 0xFFF) > MaxGlyph) return null;
            codes.Add(code);
        }
        return null;
    }

    static ReadOnlySpan<byte> Field(byte[] file, int at, int length)
    {
        if (at < 0 || length <= 0 || at + length > file.Length)
            throw new InvalidDataException($"a menu text field at file offset 0x{at:X} is outside GAME.EXE");
        return file.AsSpan(at, length);
    }

    static int Offset(uint address)
    {
        if (address < ExeBaseAddress)
            throw new InvalidDataException($"menu text at 0x{address:X} is below GAME.EXE");
        return checked((int)(address - ExeBaseAddress));
    }

    /// <summary>The label font's letters for a US string, for the probe's log: A to Z,
    /// 0 to 9, the marks of docs/GAME_INTERNALS.md, and [hex] for anything else.</summary>
    internal static string LabelText(string key)
    {
        var text = new StringBuilder(key.Length);
        foreach (char code in key)
            text.Append(LabelGlyph(code));
        return text.ToString();
    }

    static string LabelGlyph(char code) => code switch
    {
        >= '\x00' and <= '\x19' => ((char)('A' + code)).ToString(),
        >= '\x20' and <= '\x29' => ((char)('0' + code - 0x20)).ToString(),
        '\x30' => ".",
        '\x31' => ",",
        '\x32' => "'",
        '\x33' => "-",
        '\x34' => "=",
        '\x35' => "/",
        '\x36' => "*",
        '\x37' => "#",
        '\x38' => "!",
        '\x39' => "•",
        '\x3A' => "?",
        '\x3B' => "·",
        '\x7F' => " ",
        _ => $"[{(int)code:X2}]",
    };
}
