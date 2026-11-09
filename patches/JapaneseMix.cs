using System.Buffers.Binary;
using RecompOne.Runtime.Cdrom;

namespace Kf3;

/// <summary>
/// The Japanese King's Field III's bytes, laid over the US disc's sectors: the pure
/// half of the Japanese layer. JapaneseDisc finds the discs and installs it;
/// MixedDiscImage serves what this builds. Bytes in, bytes out: it keeps no state and
/// logs nothing, and it reads the two discs only through the DiscFs it is given.
///
/// What is replaced, and why:
///
///     CD/COM/TALK.T, STALK.T  the conversation and the talk tables, whole. The US
///                             slot is rewritten from its start with the Japanese
///                             file and the rest is zero.
///     CD/COM/ITEM.T           rebuilt. The first 150 entries (the 3D models) stay
///                             US; each later entry that is a Japanese TIM image
///                             (its first word 0x10) replaces the US one. Entries
///                             are packed sector-aligned in index order.
///     CD/COM/FDAT.T           the area data, entries 3n+1 for n = 0..27, mixed by
///                             MixAreaData. The archive's table and every other
///                             entry stay as they are: the code entries load at
///                             exact sectors.
///     thirteen videos         served whole from the Japanese disc, sector for
///                             sector (BuildStreamRanges). Their frame counts match
///                             the US discs', so the stream lines up.
///
/// An archive (the u16 table format of FDAT, ITEM and the rest): u16 count, then
/// count+1 u16 start sectors, counted from the file's first sector, which holds the
/// table. Entry i is the sectors from start[i] up to start[i+1]; an entry of length
/// zero is empty. Each non-empty entry ends with a checksum word: the seed 0x12345678
/// plus every word before it, wrapping, is the last word.
/// </summary>
internal static class JapaneseMix
{
    const int SectorBytes = 2048;
    const string TalkPath = "CD/COM/TALK.T";
    const string StalkPath = "CD/COM/STALK.T";
    const string ItemPath = "CD/COM/ITEM.T";
    const string FdatPath = "CD/COM/FDAT.T";

    /// <summary>The first ITEM.T entry that may be a Japanese image; the 3D models before it stay US.</summary>
    const int ItemFirstMixed = 150;

    /// <summary>A TIM image starts with this little-endian word, 0x10.</summary>
    static readonly byte[] TimMagic = [0x10, 0x00, 0x00, 0x00];

    /// <summary>The area data entries are 3n+1 for n = 0..27; 3n+2 and 3n+3 are code.</summary>
    const int AreaCount = 28;

    const int AreaBytes = 0x7000;
    const int CreatureCount = 40;
    const int CreatureOffset = 4;
    const int CreatureBytes = 0x78;
    const int PointerTable = 0x38;
    const int PointerCount = 16;

    /// <summary>Pointers are counted from here, the first rule's file offset.</summary>
    const int RuleBase = 0x12C4;
    const uint UnusedPointer = 0xFFFF_FFFF;

    /// <summary>The conversation rule type, the only one whose bytes come from the Japanese disc.</summary>
    const byte ConversationType = 0x70;

    const int ChecksumAt = 0x6FFC;
    const uint ChecksumSeed = 0x1234_5678;

    /// <summary>The 13 videos served from the Japanese disc. S14 and S15 are a little shorter there, but the frame limits GAME reads (683 and 963, in FDAT) stop them before the Japanese files end.</summary>
    static readonly string[] StreamPaths =
    [
        "OP/M0.S", "OP/M1.S", "OP/M3.S", "OP/M5.S", "OP/M6.S",
        "STR/S03.S", "STR/S06.S", "STR/S07.S", "STR/S08.S", "STR/S12.S", "STR/S13.S", "STR/S14.S", "STR/S15.S",
    ];

    /// <summary>One FDAT.T area data entry (28672 bytes), with the Japanese text mixed in.</summary>
    /// <exception cref="InvalidDataException">The entry is not the expected layout.</exception>
    public static byte[] MixAreaData(ReadOnlySpan<byte> us, ReadOnlySpan<byte> jp)
    {
        return MixArea(us, jp).Data;
    }

    /// <summary>The replacement bytes for each overridden US sector, keyed by US LBA, each 2048 bytes.</summary>
    /// <exception cref="InvalidDataException">A file is missing or not the layout this expects.</exception>
    public static Dictionary<int, byte[]> BuildSectorOverrides(DiscFs us, DiscFs jp)
    {
        var overrides = new Dictionary<int, byte[]>();
        foreach (var edit in PlanFiles(us, jp))
        {
            foreach (var k in ChangedSectorIndices(us, edit))
            {
                var at = k * SectorBytes;
                overrides[edit.File.Lba + k] = edit.NewBytes.AsSpan(at, SectorBytes).ToArray();
            }
        }

        return overrides;
    }

    /// <summary>The videos served whole from the Japanese disc, keyed by the US file.</summary>
    /// <exception cref="InvalidDataException">A file is missing, or the Japanese one is longer than the US one.</exception>
    public static IReadOnlyList<StreamRange> BuildStreamRanges(DiscFs us, DiscFs jp)
    {
        var ranges = new List<StreamRange>(StreamPaths.Length);
        foreach (var path in StreamPaths)
        {
            var usFile = Find(us, path);
            var jpFile = Find(jp, path);
            if (jpFile.Sectors > usFile.Sectors)
                throw new InvalidDataException($"{path}: the Japanese file has {jpFile.Sectors} sectors, more than the US file's {usFile.Sectors}");
            ranges.Add(new StreamRange(usFile.Lba, usFile.Sectors, jpFile.Lba, jpFile.Sectors));
        }

        return ranges;
    }

    /// <summary>
    /// The per-file plan as text, for the probe and the self-check: each area's rule
    /// region before and after, and each file's sectors changed. A failure is one
    /// line, so every area is reported even when one will not mix.
    /// </summary>
    internal static IReadOnlyList<string> Describe(DiscFs us, DiscFs jp)
    {
        var lines = new List<string>();
        lines.AddRange(DescribeAreas(us, jp));
        try
        {
            foreach (var edit in PlanFiles(us, jp))
                lines.Add($"{edit.File.Path}: {edit.Note}, {ChangedSectorIndices(us, edit).Count()} sector(s) changed");
        }
        catch (InvalidDataException e)
        {
            lines.Add($"files: FAILED: {e.Message}");
        }

        return lines;
    }

    /// <summary>A file's new bytes, padded to whole sectors, and what was done to it.</summary>
    private sealed record FileEdit(DiscFile File, byte[] NewBytes, string Note);

    /// <summary>A file on the disc: where it starts and how many bytes it holds.</summary>
    private readonly record struct DiscFile(string Path, int Lba, int Bytes)
    {
        public int Sectors => (Bytes + SectorBytes - 1) / SectorBytes;
    }

    /// <summary>What MixArea made of one area entry: its bytes and the rule region's size before and after.</summary>
    private readonly record struct AreaMix(byte[] Data, int UsRegionBytes, int NewRegionBytes, int Rules);

    /// <summary>A pointer field: the creature, its slot 0..15, and the rule's file offset it points at.</summary>
    private readonly record struct Slot(int Creature, int Index, int Target);

    /// <summary>A rule: its file offset and its length in bytes.</summary>
    private readonly record struct Rule(int Start, int Length);

    /// <summary>The edits for TALK, STALK, ITEM and FDAT, in that order.</summary>
    private static List<FileEdit> PlanFiles(DiscFs us, DiscFs jp)
    {
        return
        [
            WholeFileEdit(us, jp, TalkPath),
            WholeFileEdit(us, jp, StalkPath),
            ItemEdit(us, jp),
            AreaEdit(us, jp),
        ];
    }

    private static FileEdit WholeFileEdit(DiscFs us, DiscFs jp, string path)
    {
        var usFile = Find(us, path);
        var jpFile = Find(jp, path);
        if (jpFile.Bytes > usFile.Sectors * SectorBytes)
            throw new InvalidDataException($"{path}: the Japanese file is {jpFile.Bytes} bytes, larger than the US slot of {usFile.Sectors} sectors");

        var bytes = Padded(Read(jp, jpFile), usFile.Sectors);
        return new FileEdit(usFile, bytes, $"Japanese file of {jpFile.Bytes} bytes written over {usFile.Sectors} sectors");
    }

    private static FileEdit ItemEdit(DiscFs us, DiscFs jp)
    {
        var usFile = Find(us, ItemPath);
        var jpFile = Find(jp, ItemPath);
        var usArchive = Archive.Read(ItemPath, Read(us, usFile));
        var jpArchive = Archive.Read(ItemPath, Read(jp, jpFile));
        var choice = ChooseItems(usArchive, jpArchive);
        var bytes = PackArchive(choice.Entries, usArchive.FirstStart, usFile.Sectors);
        var note = $"{usArchive.Count} entries, {ItemFirstMixed} kept US, entries from {ItemFirstMixed} on: {choice.Identical} identical, {choice.Replaced} replaced";
        return new FileEdit(usFile, bytes, note);
    }

    /// <summary>The item table's entries: the US ones, except that a later entry takes the Japanese TIM image.</summary>
    private static ItemChoice ChooseItems(Archive us, Archive jp)
    {
        if (us.Count != jp.Count)
            throw new InvalidDataException($"{ItemPath}: the Japanese file has {jp.Count} entries, the US file {us.Count}");

        var entries = new List<byte[]>(us.Count);
        int identical = 0;
        int replaced = 0;
        for (int i = 0; i < us.Count; i++)
        {
            var usEntry = us.Entry(i);
            if (i < ItemFirstMixed)
            {
                entries.Add(usEntry.ToArray());
                continue;
            }

            var chosen = IsTim(jp.Entry(i)) ? jp.Entry(i).ToArray() : usEntry.ToArray();
            entries.Add(chosen);
            if (chosen.AsSpan().SequenceEqual(usEntry)) identical++;
            else replaced++;
        }

        return new ItemChoice(entries, identical, replaced);
    }

    private sealed record ItemChoice(List<byte[]> Entries, int Identical, int Replaced);

    private static bool IsTim(ReadOnlySpan<byte> entry)
    {
        return entry.Length >= TimMagic.Length && entry[..TimMagic.Length].SequenceEqual(TimMagic.AsSpan());
    }

    /// <summary>Packs entries back to back from <paramref name="firstStart"/>, sector-aligned, behind a new table.</summary>
    private static byte[] PackArchive(IReadOnlyList<byte[]> entries, int firstStart, int sectors)
    {
        var starts = new int[entries.Count + 1];
        int cursor = firstStart;
        for (int i = 0; i < entries.Count; i++)
        {
            starts[i] = cursor;
            cursor += entries[i].Length / SectorBytes;
        }

        starts[entries.Count] = cursor;
        if (2 + 2 * starts.Length > firstStart * SectorBytes)
            throw new InvalidDataException($"{ItemPath}: the table of {entries.Count} entries does not fit in {firstStart} sector(s)");
        if (cursor > sectors)
            throw new InvalidDataException($"{ItemPath}: the rebuilt entries need {cursor} sectors; the US file has {sectors}");

        var file = new byte[sectors * SectorBytes];
        BinaryPrimitives.WriteUInt16LittleEndian(file, (ushort)entries.Count);
        for (int i = 0; i < starts.Length; i++)
            BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(2 + 2 * i), (ushort)starts[i]);
        for (int i = 0; i < entries.Count; i++)
            entries[i].CopyTo(file, starts[i] * SectorBytes);
        return file;
    }

    private static FileEdit AreaEdit(DiscFs us, DiscFs jp)
    {
        var usFile = Find(us, FdatPath);
        var jpFile = Find(jp, FdatPath);
        var usBytes = Read(us, usFile);
        var usArchive = Archive.Read(FdatPath, usBytes);
        var jpArchive = Archive.Read(FdatPath, Read(jp, jpFile));
        var bytes = Padded(usBytes, usFile.Sectors);
        int changed = 0;
        for (int n = 0; n < AreaCount; n++)
        {
            int entry = 3 * n + 1;
            if (MixEntry(usArchive, jpArchive, entry) is not { } mixed) continue;
            mixed.CopyTo(bytes, usArchive.ByteStart(entry));
            changed++;
        }

        return new FileEdit(usFile, bytes, $"{AreaCount} area entries mixed, {changed} changed");
    }

    /// <summary>The mixed bytes of FDAT entry <paramref name="entry"/>, or null when they are the US ones.</summary>
    private static byte[]? MixEntry(Archive us, Archive jp, int entry)
    {
        var usEntry = us.Entry(entry);
        var jpEntry = jp.Entry(entry);
        if (jpEntry.Length != usEntry.Length)
            throw new InvalidDataException($"{FdatPath} entry {entry}: the Japanese entry is {jpEntry.Length} bytes, the US one {usEntry.Length}");

        try
        {
            var mixed = MixAreaData(usEntry, jpEntry);
            return mixed.AsSpan().SequenceEqual(usEntry) ? null : mixed;
        }
        catch (InvalidDataException e)
        {
            throw new InvalidDataException($"{FdatPath} entry {entry}: {e.Message}", e);
        }
    }

    private static List<string> DescribeAreas(DiscFs us, DiscFs jp)
    {
        var lines = new List<string>();
        var usFile = Find(us, FdatPath);
        var usArchive = Archive.Read(FdatPath, Read(us, usFile));
        var jpArchive = Archive.Read(FdatPath, Read(jp, Find(jp, FdatPath)));
        for (int n = 0; n < AreaCount; n++)
        {
            int entry = 3 * n + 1;
            lines.Add(DescribeArea(n, entry, usArchive, jpArchive));
        }

        return lines;
    }

    private static string DescribeArea(int n, int entry, Archive us, Archive jp)
    {
        try
        {
            var mix = MixArea(us.Entry(entry), jp.Entry(entry));
            var changed = mix.Data.AsSpan().SequenceEqual(us.Entry(entry)) ? "unchanged" : "changed";
            var delta = mix.NewRegionBytes - mix.UsRegionBytes;
            return $"area {n} (entry {entry}): {mix.Rules} rules, rule region {mix.UsRegionBytes} -> {mix.NewRegionBytes} bytes ({delta:+#;-#;0} bytes), {changed}";
        }
        catch (InvalidDataException e)
        {
            return $"area {n} (entry {entry}): FAILED: {e.Message}";
        }
    }

    /// <summary>The creature rules of one area entry, with each conversation taken from the Japanese entry.</summary>
    private static AreaMix MixArea(ReadOnlySpan<byte> us, ReadOnlySpan<byte> jp)
    {
        RequireArea(us, "US");
        RequireArea(jp, "Japanese");
        var usSlots = ReadSlots(us);
        var jpSlots = ReadSlots(jp);
        RequireSameSlots(usSlots, jpSlots);

        var usRules = ReadRules(us, jp, usSlots);
        var jpRules = ReadRules(jp, us, jpSlots);
        var jpTargetBySlot = jpSlots.ToDictionary(s => (s.Creature, s.Index), s => s.Target);
        var jpRuleAt = jpRules.ToDictionary(r => r.Start);

        int regionStart = usRules[0].Start;
        int usRegionEnd = usRules[^1].Start + usRules[^1].Length;
        var region = new List<byte>(usRegionEnd - regionStart);
        var newStartOf = new Dictionary<int, int>();
        foreach (var rule in usRules)
        {
            newStartOf[rule.Start] = regionStart + region.Count;
            region.AddRange(RuleSource(rule, us, jp, usSlots, jpTargetBySlot, jpRuleAt));
        }

        var result = us.ToArray();
        PlaceRegion(result, region.ToArray(), regionStart, usRegionEnd);
        RewritePointers(result, usSlots, newStartOf);
        WriteChecksum(result);
        return new AreaMix(result, usRegionEnd - regionStart, region.Count, usRules.Length);
    }

    private static void RequireArea(ReadOnlySpan<byte> area, string which)
    {
        if (area.Length != AreaBytes)
            throw new InvalidDataException($"the {which} area data is {area.Length} bytes, not {AreaBytes}");
        if (!ChecksumOk(area))
            throw new InvalidDataException($"the {which} area data's checksum fails");
    }

    /// <summary>The pointer fields that are not unused, with the rule each points at.</summary>
    private static List<Slot> ReadSlots(ReadOnlySpan<byte> area)
    {
        var slots = new List<Slot>();
        for (int c = 0; c < CreatureCount; c++)
        {
            for (int i = 0; i < PointerCount; i++)
            {
                uint pointer = BinaryPrimitives.ReadUInt32LittleEndian(area[PointerAt(c, i)..]);
                if (pointer == UnusedPointer) continue;
                long target = RuleBase + (long)pointer;
                if (target >= area.Length)
                    throw new InvalidDataException($"creature {c} slot {i} points at 0x{target:X}, past the entry's end");
                slots.Add(new Slot(c, i, (int)target));
            }
        }

        return slots;
    }

    private static int PointerAt(int creature, int index)
    {
        return CreatureOffset + creature * CreatureBytes + PointerTable + 4 * index;
    }

    private static void RequireSameSlots(List<Slot> us, List<Slot> jp)
    {
        var usKeys = us.Select(s => (s.Creature, s.Index)).ToHashSet();
        var jpKeys = jp.Select(s => (s.Creature, s.Index)).ToHashSet();
        if (!usKeys.SetEquals(jpKeys))
            throw new InvalidDataException($"the pointer fields differ: {usKeys.Count} in US, {jpKeys.Count} in Japanese");
    }

    /// <summary>The distinct rules the pointers name, in ascending order, each with its length.</summary>
    private static Rule[] ReadRules(ReadOnlySpan<byte> area, ReadOnlySpan<byte> other, List<Slot> slots)
    {
        if (slots.Count == 0)
            throw new InvalidDataException("no creature has a rule");

        var starts = slots.Select(s => s.Target).Distinct().Order().ToArray();
        var rules = new Rule[starts.Length];
        for (int i = 0; i < starts.Length; i++)
        {
            var length = RuleLength(area, other, starts, i);
            if (starts[i] + length > area.Length)
                throw new InvalidDataException($"the rule at 0x{starts[i]:X} runs past the entry's end");
            rules[i] = new Rule(starts[i], length);
        }

        return rules;
    }

    /// <summary>A rule runs to the next rule's start; only the last rule needs another rule's length.</summary>
    private static int RuleLength(ReadOnlySpan<byte> area, ReadOnlySpan<byte> other, int[] starts, int i)
    {
        if (i + 1 < starts.Length) return starts[i + 1] - starts[i];
        return LastRuleLength(area, other, starts);
    }

    /// <summary>
    /// The last rule has no next rule to end it, so its length is the length of another
    /// rule of its type byte in this entry. With none, it runs as far as the US and the
    /// Japanese bytes agree.
    /// </summary>
    private static int LastRuleLength(ReadOnlySpan<byte> area, ReadOnlySpan<byte> other, int[] starts)
    {
        int start = starts[^1];
        byte type = area[start];
        var lengths = new HashSet<int>();
        for (int i = 0; i + 1 < starts.Length; i++)
            if (area[starts[i]] == type) lengths.Add(starts[i + 1] - starts[i]);

        return lengths.Count switch
        {
            1 => lengths.Single(),
            0 => AgreementLength(area, other, start),
            _ => throw new InvalidDataException($"rules of type 0x{type:X2} differ in length: {string.Join(", ", lengths)}"),
        };
    }

    private static int AgreementLength(ReadOnlySpan<byte> area, ReadOnlySpan<byte> other, int start)
    {
        int n = 0;
        while (start + n < area.Length && start + n < other.Length && area[start + n] == other[start + n])
            n++;
        if (n == 0)
            throw new InvalidDataException($"the rule at 0x{start:X} has no rule of its type to take its length from");
        return n;
    }

    /// <summary>
    /// The bytes a US rule is written as. A conversation takes the Japanese rule its
    /// pointers name; every other rule keeps its US bytes.
    /// </summary>
    private static byte[] RuleSource(Rule rule, ReadOnlySpan<byte> us, ReadOnlySpan<byte> jp, List<Slot> usSlots,
        Dictionary<(int, int), int> jpTargetBySlot, Dictionary<int, Rule> jpRuleAt)
    {
        if (us[rule.Start] != ConversationType)
            return us.Slice(rule.Start, rule.Length).ToArray();

        var jpStart = JapaneseTargetOf(rule, usSlots, jpTargetBySlot);
        var jpRule = jpRuleAt[jpStart];
        if (jp[jpRule.Start] != ConversationType)
            throw new InvalidDataException($"the rule at 0x{rule.Start:X} is a conversation, but the Japanese rule at 0x{jpRule.Start:X} is type 0x{jp[jpRule.Start]:X2}");
        return jp.Slice(jpRule.Start, jpRule.Length).ToArray();
    }

    /// <summary>The Japanese rule for a US rule: the one every pointer to it names. The rule is written once, so those must agree.</summary>
    private static int JapaneseTargetOf(Rule rule, List<Slot> usSlots, Dictionary<(int, int), int> jpTargetBySlot)
    {
        var targets = usSlots
            .Where(s => s.Target == rule.Start)
            .Select(s => jpTargetBySlot[(s.Creature, s.Index)])
            .Distinct()
            .ToArray();
        if (targets.Length != 1)
            throw new InvalidDataException($"the rule at 0x{rule.Start:X} is pointed at by fields whose Japanese rules differ");
        return targets[0];
    }

    /// <summary>
    /// Writes the new rule region over the US one. A shorter region frees its tail,
    /// which becomes 0xFF; a longer one may take over the US bytes after it only when
    /// those are all 0xFF or all 0x00 filler.
    /// </summary>
    private static void PlaceRegion(byte[] area, byte[] region, int regionStart, int usRegionEnd)
    {
        int newEnd = regionStart + region.Length;
        if (newEnd > usRegionEnd)
            RequireFiller(area, usRegionEnd, newEnd - usRegionEnd);
        region.CopyTo(area, regionStart);
        if (newEnd < usRegionEnd)
            area.AsSpan(newEnd, usRegionEnd - newEnd).Fill(0xFF);
    }

    private static void RequireFiller(byte[] area, int at, int count)
    {
        if (at + count > area.Length)
            throw new InvalidDataException($"the rule region needs {count} more bytes past 0x{at:X}, beyond the entry's end");
        var bytes = area.AsSpan(at, count);
        bool allFF = bytes.IndexOfAnyExcept((byte)0xFF) < 0;
        bool all00 = bytes.IndexOfAnyExcept((byte)0x00) < 0;
        if (!allFF && !all00)
            throw new InvalidDataException($"the rule region grows by {count} bytes into 0x{at:X}, which is not filler");
    }

    /// <summary>Each pointer field now names its rule's new place.</summary>
    private static void RewritePointers(byte[] area, List<Slot> slots, Dictionary<int, int> newStartOf)
    {
        foreach (var slot in slots)
        {
            uint pointer = (uint)(newStartOf[slot.Target] - RuleBase);
            BinaryPrimitives.WriteUInt32LittleEndian(area.AsSpan(PointerAt(slot.Creature, slot.Index)), pointer);
        }
    }

    private static void WriteChecksum(byte[] area)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(area.AsSpan(ChecksumAt), ChecksumOf(area.AsSpan(0, ChecksumAt)));
    }

    /// <summary>Whether the last word of the data is the checksum of the words before it.</summary>
    private static bool ChecksumOk(ReadOnlySpan<byte> data)
    {
        if (data.Length < 8 || data.Length % 4 != 0) return false;
        return ChecksumOf(data[..^4]) == BinaryPrimitives.ReadUInt32LittleEndian(data[^4..]);
    }

    /// <summary>The seed plus every word of <paramref name="words"/>, wrapping.</summary>
    private static uint ChecksumOf(ReadOnlySpan<byte> words)
    {
        uint sum = ChecksumSeed;
        for (int at = 0; at + 4 <= words.Length; at += 4)
            sum += BinaryPrimitives.ReadUInt32LittleEndian(words[at..]);
        return sum;
    }

    private static IEnumerable<int> ChangedSectorIndices(DiscFs us, FileEdit edit)
    {
        var usBytes = us.ReadSectors(edit.File.Lba, edit.NewBytes.Length);
        for (int k = 0; k < edit.NewBytes.Length / SectorBytes; k++)
        {
            int at = k * SectorBytes;
            if (!edit.NewBytes.AsSpan(at, SectorBytes).SequenceEqual(usBytes.AsSpan(at, SectorBytes)))
                yield return k;
        }
    }

    private static DiscFile Find(DiscFs disc, string path)
    {
        if (!disc.Locate(path, out int lba, out uint size))
            throw new InvalidDataException($"{path} is not on the disc");
        return new DiscFile(path, lba, (int)size);
    }

    private static byte[] Read(DiscFs disc, DiscFile file)
    {
        return disc.ReadSectors(file.Lba, file.Bytes);
    }

    private static byte[] Padded(byte[] bytes, int sectors)
    {
        var padded = new byte[sectors * SectorBytes];
        bytes.CopyTo(padded, 0);
        return padded;
    }

    /// <summary>An archive read from its bytes: its table, and each entry's bytes as they lie in the file.</summary>
    private sealed class Archive
    {
        readonly byte[] _file;
        readonly int[] _starts;

        private Archive(byte[] file, int[] starts)
        {
            _file = file;
            _starts = starts;
        }

        public int Count => _starts.Length - 1;

        public int FirstStart => _starts[0];

        public int ByteStart(int i) => _starts[i] * SectorBytes;

        public ReadOnlySpan<byte> Entry(int i)
        {
            return _file.AsSpan(ByteStart(i), (_starts[i + 1] - _starts[i]) * SectorBytes);
        }

        /// <exception cref="InvalidDataException">The table, an entry's order, or an entry's checksum is wrong.</exception>
        public static Archive Read(string path, byte[] file)
        {
            if (file.Length < SectorBytes)
                throw new InvalidDataException($"{path} is {file.Length} bytes, shorter than its table");

            int count = BinaryPrimitives.ReadUInt16LittleEndian(file);
            var starts = new int[count + 1];
            for (int i = 0; i < starts.Length; i++)
                starts[i] = BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(2 + 2 * i));
            if (2 + 2 * starts.Length > starts[0] * SectorBytes)
                throw new InvalidDataException($"{path}: the table of {count} entries is longer than its first sector");
            for (int i = 0; i < count; i++)
                if (starts[i + 1] < starts[i])
                    throw new InvalidDataException($"{path}: entry {i} ends before it starts");
            if (starts[count] * SectorBytes > file.Length)
                throw new InvalidDataException($"{path}: the table ends at sector {starts[count]}, past the file");

            var archive = new Archive(file, starts);
            for (int i = 0; i < count; i++)
                if (archive.Entry(i).Length > 0 && !ChecksumOk(archive.Entry(i)))
                    throw new InvalidDataException($"{path} entry {i}: checksum fails");
            return archive;
        }
    }
}

/// <summary>
/// A video's sectors on the US disc and where the same video lies on the Japanese one.
/// The Japanese file may be shorter; the sectors past its end read as zeros.
/// </summary>
internal readonly record struct StreamRange(int UsLba, int Sectors, int JpLba, int JpSectors);
