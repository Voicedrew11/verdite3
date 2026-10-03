// ModCompiler compiles mods with no implicit usings, so every namespace the
// file needs must be named here -- including System.
using System;
using System.Collections.Generic;
using RecompOne.Runtime.Context;
using RecompOne.Runtime.Memory;
using RecompOne.Runtime.Modding;

namespace Kf3.Mods.Debug;

/// <summary>
/// The spell book: learn, forget and read the player's spells.
///
/// ---- where a spell lives ----
///
/// The records are at <see cref="RecordBase"/>, one every
/// <see cref="RecordStride"/> (0x18) bytes. This table is why the KF2 mod's
/// inventory editor does not extend here: KF3 keeps magic out of the 150-byte
/// item count array. It is its own block, and the block is what the game's own
/// magic screens read.
///
/// The proof is `func_8001C6C0`, the routine every spell list is built by, and
/// it is the whole map in one function:
///
/// <code>
/// int func_8001C6C0(u8 *recs, u8 *rows, u32 *costs, u8 *ids, int first, int last)
/// {
///     n = 0;
///     for (i = first; i &lt;= last; i++)
///         if (recs[i*0x18 + 0] == 1) {                          /* known */
///             memcpy(rows + n*0x18, (u8*)0x80080430 + i*0x18, 0x18);  /* the name */
///             costs[n] = *(u16*)(recs + i*0x18 + 0x16);          /* MP cost */
///             ids[n]   = i;
///             n++;
///         }
///     return n;
/// }
/// </code>
///
/// Three callers hand it `recs = 0x801B77EC`:
///
///   func_8001D0C4   first 0,   last 0x16   (the basic page, 23 spells)
///   func_8001C48C   first 0x17, last 0x1e  (the advanced page, 8 spells)
///   func_8001D3A0   first 0,   last 0x1e   (the whole book, 31 spells)
///
/// so the book is <see cref="Count"/> = 0x1F = 31 spells, records 0..0x1E. The
/// split pages are halves of the same contiguous run; the combined page is the
/// range used here. The name table base `0x80080430` is the item name table at
/// `0x8007F620` shifted by 150 records -- i.e. **spell record `i` is item id
/// `150 + i`**, which is why the names decode as FIRE BALL, FIRE WALL, ... (see
/// the item findings). The names are decoded here live out of memory, by this
/// file's own decoder -- the parallel Items.cs owns the item table, not this.
///
/// ---- what "known" means, and what does not ----
///
/// The only test the game makes is `rec[+0] == 1`. `func_8002CFE0` (the
/// can-cast check) reaches the same byte through the current spell's descriptor
/// at `0x801B2594` (its `+3`/`+4` is the record index). Setting `+0` to 1 is
/// therefore the whole of learning, and 0 the whole of forgetting; there is no
/// second flag consulted, contrary to one note. The parallel record at
/// `0x801E6078 + i*0x20` is equipment/accessories, not the spell book.
///
/// An honest wrinkle, because it will be seen on the screen: the new-game reset
/// `func_8002B468` zeroes every `+0` in the block and then sets `0x801B7AA4 = 1`
/// to mark the list initialised. `0x801B7AA4` *is* record 29's `+0` byte
/// (`0x801B77EC + 29*0x18`), and record 29 is LIGHT -- so a fresh character
/// reads as knowing LIGHT, and `a_idle.bin` shows exactly that one record set.
/// Nothing in the game ever reads `0x801B7AA4` again, so the two readings are
/// indistinguishable from the data. This file treats any non-zero `+0` as known,
/// which is what the magic menu itself does; it writes exactly 1.
///
/// ---- granting ----
///
/// There is no callable "learn a spell" routine in `GAME.EXE`. Spells are handed
/// over by the area modules: using a crystal item reaches `func_8005CBE0`, which
/// dispatches to the area module's own slot, and the script there writes the
/// record. That routine is not in the image, so this file writes `+0` directly --
/// the same reasoning <see cref="Items"/> gives for setting a count directly, and
/// the clean menu/validity path because both readers test the byte itself. The
/// magic pages are rebuilt every time they open, so no menu refresh is needed.
///
/// ---- what "selected" means ----
///
/// `0x801B25AB` is the spell id currently attuned/equipped: `func_8002B928`
/// writes it, `func_8001D0C4` highlights the row that matches it, and the charge
/// path in `func_8002FE1C` reads it. That is what <see cref="Selected"/> shows.
/// `0x801B2590` is a pointer to the record actually being cast, set by
/// `func_8002D130`; it is not exposed because it lives only for the cast. (The
/// notes give `0x801B2594` for "the current spell", but the disassembly shows
/// `func_8002D130` storing to `0x801B2590`; `0x801B2594` is the 0x44-byte item
/// descriptor that `func_8002BDC0` writes, and `func_8002CFE0` reads its `+3`/`+4`.
/// The two words differ by one store.)
/// </summary>
internal static class Magic
{
    /// <summary>The book, one 0x18-byte record per spell. Only <c>+0</c> is player state; the rest is the area's static definition.</summary>
    internal const uint RecordBase = 0x801B77EC;

    /// <summary>Record stride, written by every reader as <c>id*3*8</c>.</summary>
    internal const int RecordStride = 0x18;

    /// <summary>
    /// Records 0..0x1E. The bound is the widest range the game's own list
    /// builders ask for, `func_8001D3A0` (`first 0, last 0x1e`), and the name
    /// table has a spell record for each of those 31 ids.
    /// </summary>
    internal const int Count = 0x1F;

    /// <summary>Byte 0 of a record: 1 when known. Everything else is area data.</summary>
    internal const int KnownOffset = 0x00;

    /// <summary>u16 at record +0x16, read as the cast cost by `func_8002D130` and `func_8002DEEC`.</summary>
    internal const int CostOffset = 0x16;

    /// <summary>Item name table, 24-byte font records. Spell `i` is item id <c>150 + i</c>.</summary>
    internal const uint ItemNameTable = 0x8007F620;
    internal const int NameStride = 0x18;
    internal const int FirstSpellItemId = 150;

    /// <summary>The attuned/equipped spell id: u8, 0xFF = none.</summary>
    internal const uint SelectedSpell = 0x801B25AB;

    /// <summary>`func_8002B928`'s "no spell" sentinel, read back from <see cref="SelectedSpell"/>.</summary>
    internal const int NoSpell = 0xFF;

    /// <summary>The settings prefix, for anything a panel wants to persist.</summary>
    internal const string SettingsPrefix = "kf3.debug.";

    // ---- reading ----

    /// <summary>A spell record's address, or <see cref="RecordBase"/> for an out-of-range id.</summary>
    static uint Record(int i) => RecordBase + (uint)i * RecordStride;

    internal static bool Known(IMemory m, int i)
    {
        if ((uint)i >= Count) return false;
        return m.ReadU8(Record(i) + KnownOffset) != 0;
    }

    internal static int Cost(IMemory m, int i)
    {
        if ((uint)i >= Count) return 0;
        return m.ReadU16(Record(i) + CostOffset);
    }

    /// <summary>
    /// Decode spell `i`'s name out of the running image. The record is 24 bytes
    /// and a record with no terminator is not a string, so the loop stops at the
    /// stride as well as at 0xFF. An id outside the book decodes to the empty
    /// string.
    /// </summary>
    internal static string Name(IMemory m, int i)
    {
        if ((uint)i >= Count) return "";

        uint rec = ItemNameTable + (uint)(FirstSpellItemId + i) * NameStride;
        var sb = new System.Text.StringBuilder(NameStride);

        for (int b = 0; b < NameStride; b++)
        {
            byte c = m.ReadU8(rec + (uint)b);
            if (c == 0xFF) break;
            sb.Append(c < 26 ? (char)('A' + c) : Punctuation(c));
        }

        return sb.ToString();
    }

    // KF3's item font: 0x00..0x19 are A..Z, 0x7F is space, and these are the
    // only non-letter codes any record uses. An unexpected code renders as its
    // hex rather than a guess, so a wrong reading shows as a wrong reading.
    static string Punctuation(byte c) => c switch
    {
        0x31 => ",",
        0x32 => "'",
        0x33 => "-",
        0x38 => "!",
        0x3A => "?",
        0x7F => " ",
        _ => $"{{{c:X2}}}",
    };

    // ---- the currently selected spell ----

    /// <summary>The attuned spell id, or <see cref="NoSpell"/> when none is selected.</summary>
    internal static int SelectedId(IMemory m) => m.ReadU8(SelectedSpell);

    /// <summary>The attuned spell's name, or "none" when nothing is selected.</summary>
    internal static string Selected(IMemory m)
    {
        int id = SelectedId(m);
        return id == NoSpell ? "none" : Name(m, id);
    }

    // ---- learning and forgetting ----
    //
    // Queued, not applied: the panel draws inside Present, which is inside
    // VSync, and the game thread is at stage 4. The writes themselves need no
    // CpuContext, but they are still done in the hook so a panel can call these
    // from the UI thread without touching live state mid-frame.

    readonly struct Request
    {
        public readonly int Id;
        public readonly bool Learn;
        public Request(int id, bool learn) { Id = id; Learn = learn; }
    }

    static readonly List<Request> _pending = [];

    internal static string Status = "";

    internal static void QueueLearn(int id)
    {
        if ((uint)id >= Count) return;
        _pending.Add(new Request(id, true));
        Status = $"{_pending.Count} spell change(s) queued";
    }

    internal static void QueueForget(int id)
    {
        if ((uint)id >= Count) return;
        _pending.Add(new Request(id, false));
        Status = $"{_pending.Count} spell change(s) queued";
    }

    /// <summary>
    /// Queue every record in the book. Known ones are counted and skipped when
    /// the queue runs, so this is idempotent -- the "learn all" button.
    /// </summary>
    internal static void QueueLearnAll()
    {
        for (int i = 0; i < Count; i++) _pending.Add(new Request(i, true));
        Status = $"queued all {Count} spells";
    }

    /// <summary>
    /// End of main-loop stage 4, the player's tick (<see cref="GameState.PlayerStage"/>,
    /// func_80030FCC). Stage 4 is where the spell state machine and the cast
    /// routines run, so it is where a queued change can land without a reader
    /// seeing a half-written record.
    /// </summary>
    [PostHook("game", Address = GameState.PlayerStage)]
    static void AfterPlayerStage(CpuContext c, IMemory m)
    {
        // The attract demo never has a character; leave its memory alone.
        if (!GameState.IsInGame(m)) return;

        RunProbe(m);

        if (_pending.Count == 0) return;
        Apply(m);
    }

    static void Apply(IMemory m)
    {
        int learned = 0, forgotten = 0, unchanged = 0;

        foreach (var req in _pending)
        {
            uint rec = Record(req.Id);
            bool known = m.ReadU8(rec + KnownOffset) != 0;

            if (req.Learn)
            {
                if (known) { unchanged++; continue; }
                m.WriteU8(rec + KnownOffset, 1);
                learned++;
            }
            else
            {
                if (!known) { unchanged++; continue; }
                m.WriteU8(rec + KnownOffset, 0);
                forgotten++;
            }
        }

        _pending.Clear();

        if (learned == 0 && forgotten == 0)
            Status = "no spell changed";
        else if (forgotten == 0)
            Status = $"learned {learned} spell(s)";
        else if (learned == 0)
            Status = $"forgot {forgotten} spell(s)";
        else
            Status = $"learned {learned}, forgot {forgotten} spell(s)";

        Console.WriteLine($"[kf3debug] {Status}");
        if (unchanged > 0) Console.WriteLine($"[kf3debug]   ({unchanged} already in that state)");
    }

    // ---- the probe ----
    //
    // `KF3_DEBUG_MAGIC_PROBE=1` prints the book once, the first time a character
    // is up. It is what says the addresses above are right without anyone
    // looking at a screen: names that decode as English in the order the game's
    // own pages list them, with costs that match the cast sites, is the reading
    // being correct; a wrong base prints hex escapes and nonsense. Level 2 also
    // queues learn-all, which is the acceptance test for the write path.

    static bool _probeDone;

    internal static readonly int ProbeLevel =
        int.TryParse(Environment.GetEnvironmentVariable("KF3_DEBUG_MAGIC_PROBE"), out int lv) ? lv : 0;

    static void RunProbe(IMemory m)
    {
        if (_probeDone || ProbeLevel <= 0) return;
        _probeDone = true;

        Console.WriteLine($"[kf3debug] spells at 0x{RecordBase:X8}, {Count} records, "
                        + $"names as item ids {FirstSpellItemId}..{FirstSpellItemId + Count - 1} "
                        + $"in 0x{ItemNameTable:X8}");

        for (int i = 0; i < Count; i++)
            Console.WriteLine($"[kf3debug]   {i,2}  id {FirstSpellItemId + i,3}  "
                            + $"{(Known(m, i) ? "known" : "     ")}  {Cost(m, i),3} mp  {Name(m, i)}");

        Console.WriteLine($"[kf3debug] selected spell: {Selected(m)} "
                        + $"(id 0x{SelectedId(m):X2})");

        if (ProbeLevel >= 2) QueueLearnAll();
    }

    internal static void Reset()
    {
        _pending.Clear();
        _probeDone = false;
        Status = "";
    }
}
