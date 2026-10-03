// ModCompiler compiles mods with no implicit usings, so every namespace the
// file needs must be named here -- including System.
using System;
using System.Collections.Generic;
using System.Text;
using RecompOne.Runtime.Context;
using RecompOne.Runtime.Memory;
using RecompOne.Runtime.Modding;
using Recompiled;
// Upstream emits one class per overlay, because CoreCLR caps a class at 65535
// methods. The inventory routines are GAME.EXE's, so the alias names the overlay
// once and the call sites below stay short. This is the KF3 port's counterpart
// of the KF2 file's KingsField2 alias.
using KingsField3 = Recompiled.KingsField3_game;

namespace Kf3.Mods.Debug;

/// <summary>
/// The inventory and the equipment slots.
///
/// ---- what the inventory is ----
///
/// `func_8001AEA4(counts, rows, outs, outids, first, last)` is the generic list
/// builder every scrolling item page is made of, and it is the whole map in one
/// function:
///
/// <code>
/// int func_8001AEA4(u8 *counts, u8 *rows, u8 *outCounts, u8 *outIds, int first, int last)
/// {
///     n = 0;
///     for (i = first; i &lt;= last; i++)
///         if (counts[i] != 0) {
///             memcpy(rows + n*0x18, (u8*)0x8007F620 + i*0x18, 0x18);  /* the name */
///             outCounts[n] = counts[i];
///             outIds[n]    = i;
///             n++;
///         }
///     return n;
/// }
/// </code>
///
/// So the inventory is **not** a list of slots: it is a flat array of counts
/// indexed by item id, and "holding" an item is a non-zero byte. The item page
/// `func_8001D3A0` calls it with `counts = 0x800C85E8` and `first/last = 0x65/0x92`
/// (ids 101..146); the equipment screen `func_8001CBB8` and the list page
/// `func_8001DEE4` use the same array over wider ranges.
///
/// There is a **second** 150-byte count array at `0x800C867E` (= base + 150).
/// `func_8005D898` overflows into it when the primary id is full, and one menu
/// page (`func_8001E1B4`) lists it. Its in-fiction meaning is unresolved (see
/// "Open questions" in scratch/findings/items.md); this file reads it only so a
/// held item is not missed and so a count can be set cleanly.
///
/// Every item from id 0 to 149 is in the primary array; the highest id any menu
/// asks for is 0x95 (149), so the array is 150 bytes and the cap per id is 99
/// (`0x63`). The save packs **0x120 bytes** from the base (scratch/findings/
/// items.md flags the 150-vs-288 mismatch as open). KF2's `SavedCount` has no
/// supported KF3 counterpart, so it is dropped here rather than guessed at.
///
/// ---- the names ----
///
/// `GAME.EXE` holds no English UI strings; its text is indices into its own
/// font, one byte a character, in 24-byte records -- `0x00` = `A`, `0x7F` =
/// space, `0xFF` = terminator, plus the punctuation below. Item id `i`'s name is
/// the record at `0x8007F620 + i * 0x18`, and it is decoded here **live out of
/// memory** rather than baked into this file, so the panel is reading the
/// running game's own table and not a copy that could drift from it.
///
/// The *same* table keeps going past the items: ids 150..180 are the spell
/// names (FIRE BALL, FIRE WALL, ... BLESSINGS). The magic feature has its own
/// findings and its own store, but it can decode names with <see cref="Name"/>,
/// so the decoder's bound is the end of the string records (<see
/// cref="NameRecords"/>), not the item count. That the spells sit here is a
/// KF3 observation this file adds; scratch/findings/magic.md says the spell
/// text was not located. The table and its end are both checked by decoding
/// scratch/re/ram/a_idle.bin.
///
/// ---- adding one ----
///
/// `func_8005D898(id)` is the game's own "give item":
///
/// <code>
/// int func_8005D898(int id)
/// {
///     if (inv[id] &lt; 99) { inv[id]++; area->slot6(); return 0; }
///     else if (ovf[id] &lt; 99) { ovf[id]++; area->slot6(); return 0; }  /* the overflow */
///     else { func_80041EEC(0xb); return 1; }                          /* the full chime */
/// }
/// </code>
///
/// That is what the "+1" button runs -- the game's own routine carries the cap,
/// the overflow, the full chime and the area module's hook, where imitating it
/// would carry only the increment. It needs a CpuContext, so it is queued and
/// run from the player-stage hook (see <see cref="AfterPlayerStage"/>).
///
/// `func_8005D7F8(id)` is the mirror image, the game's own "remove one", and it
/// is what the "remove" action runs. Setting a count directly is offered too,
/// because it is the only way to *remove* several or to hold more than the cap:
/// a direct write is also what a count above what the game would ever give you
/// needs.
///
/// ---- equipping (Kf3's addition) ----
///
/// KF2's reference had no equipment, because its slots were not mapped. Here
/// they are: `func_8002BDC0(id)` sets the weapon at `0x801B25AF`, and
/// `func_8002BB84(id, slot)` sets the seven armour/accessory slots by a jump
/// table (slot 0 helm, 1 armour, 2 gauntlets, 3 boots, 4 shield, 5/6 the two
/// rings). Both end by calling `func_80029500`, the full stat recompute -- read
/// off both disassemblies -- so an equip needs no second recompute after it.
///
/// Caution: scratch/findings/magic.md reads the *same* byte 0x801B25AF and the
/// same two setters as the current spell effect and its cast slots, and calls
/// the 0x801D37A4 table's stride 0x50. items.md's reading is kept here because
/// the task and the instructions agree with it: `func_8002BDC0` indexes
/// `id*0x44 + 0x801D37A4` by an id in 0..33, which is the weapon table the
/// equipment screen uses. The two findings should be reconciled before trusting
/// the read-back during a cast.
/// </summary>
internal static class Items
{
    /// <summary>The primary count array. One byte per id; zero means you do not hold it.</summary>
    internal const uint InvBase = 0x800C85E8;

    /// <summary>
    /// The overflow array, `func_8005D898`'s fallback when the primary id is
    /// full. It is <see cref="Count"/> bytes too, immediately after
    /// <see cref="InvBase"/>.
    /// </summary>
    internal const uint OverflowBase = 0x800C867E;

    /// <summary>
    /// Ids 0..149. `func_8005EA64`, the new-game init, sets `[0]=1` (the start
    /// weapon), `[0x2a]=1` (LEATHER PLATE), `[0x68]=2` (EARTH HERB) and
    /// `[0x69]=1` (ANTIDOTE), and the highest id any menu asks for is 0x95, so
    /// the array ends at 150. Spells start at id 150 (FIRE BALL) and belong to
    /// the magic feature, so the item list stops before them.
    /// </summary>
    internal const int Count = 150;

    /// <summary>Item id 0's name record. Stride 0x18, same as every other string here.</summary>
    internal const uint NameTable = 0x8007F620;
    internal const int NameStride = 0x18;

    /// <summary>
    /// How many string records the table holds: ids 0..180, the last being the
    /// last spell, BLESSINGS. Id 181 onward is zero padding and then binary
    /// data, not names (checked in scratch/re/ram/a_idle.bin), so the decoder
    /// stops here rather than rendering padding as "AAAA...". This is larger
    /// than <see cref="Count"/> on purpose: the item list stops before the
    /// spells, but the decoder other files share does not.
    /// </summary>
    internal const int NameRecords = 181;

    /// <summary>func_8005D898's own ceiling: it refuses to add at 99.</summary>
    internal const int MaxHeld = 99;

    /// <summary>GAME.EXE's pointer to the loaded area module, whose slot 6 the give routine calls.</summary>
    const uint ModulePtr = 0x8018FAE0;

    // ---- reading the array ----

    internal static int Held(IMemory m, int id) =>
        (uint)id < Count ? m.ReadU8(InvBase + (uint)id) : 0;

    /// <summary>The overflow count for an id; `func_8005D898`'s second home for it.</summary>
    internal static int Overflow(IMemory m, int id) =>
        (uint)id < Count ? m.ReadU8(OverflowBase + (uint)id) : 0;

    /// <summary>
    /// `func_8005D7BC`'s rule, read directly rather than called: held if either
    /// array is non-zero.
    /// </summary>
    internal static bool Has(IMemory m, int id) => Held(m, id) != 0 || Overflow(m, id) != 0;

    /// <summary>
    /// Set an exact count. The reference wrote only the primary byte, but for
    /// KF3 that can leave a hidden overflow behind, so the overflow byte is
    /// zeroed too unless the caller asks to keep it -- a "5" that reads back as
    /// 5 has to clear both. The write is clamped to a byte; the game's own cap
    /// of 99 applies only to the give routine, not to a debug write.
    /// </summary>
    internal static void SetHeld(IMemory m, int id, int count, bool keepOverflow = false)
    {
        if ((uint)id >= Count) return;
        m.WriteU8(InvBase + (uint)id, (byte)Math.Clamp(count, 0, 255));
        if (!keepOverflow) m.WriteU8(OverflowBase + (uint)id, 0);
    }

    /// <summary>How many distinct ids are held, which is the length of the in-game list.</summary>
    internal static int DistinctHeld(IMemory m)
    {
        int n = 0;
        for (int i = 0; i < Count; i++) if (Has(m, i)) n++;
        return n;
    }

    // ---- the names ----

    // The codes the notes give for KF3's font: A..Z, space and terminator, plus
    // these five punctuation codes. Only `0x32` and `0x33` actually occur in the
    // item records (apostrophe and hyphen); `0x3A` is confirmed as '?' by
    // "FORMAT IT?" in the memory-card text in the dump. There are no digit codes
    // in the item table -- no name contains a number -- and a code that is not
    // listed renders as its hex rather than being guessed at, so a wrong reading
    // shows as a wrong reading. Codes 0x30 and 0x39 do appear in the UI strings
    // (":" and "/") but the findings do not decode them, so they are left out.
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

    /// <summary>
    /// Decode an id's name (item or spell) out of the running image. Twenty-four
    /// bytes is the record, and a record with no terminator in it is not a
    /// string -- so the loop stops at the stride as well as at 0xFF. Exposed so
    /// the magic feature and the panel decode spells and equipment with the same
    /// table.
    /// </summary>
    internal static string Name(IMemory m, int id)
    {
        if ((uint)id >= NameRecords) return "";

        uint rec = NameTable + (uint)id * NameStride;
        var sb = new StringBuilder(NameStride);

        for (int i = 0; i < NameStride; i++)
        {
            byte c = m.ReadU8(rec + (uint)i);
            if (c == 0xFF) break;
            sb.Append(c < 26 ? (char)('A' + c) : Punctuation(c));
        }

        return sb.ToString();
    }

    /// <summary>
    /// The id shown beside the name. Ids 0, 1 and 2 are all named EXCELLECTOR --
    /// the starting sword's three tiers -- so a panel needs the number to tell
    /// them apart.
    /// </summary>
    internal static string Label(IMemory m, int id) => $"{id}: {Name(m, id)}";

    /// <summary>
    /// An id the game does not use. Its record is `00 FF` -- the letter A and a
    /// terminator. The reference read the categories off these separators; KF3's
    /// placeholders do not fall at every category boundary (there is none between
    /// helms and armour, for instance), so they are only used to skip dead ids.
    /// </summary>
    internal static bool IsUnused(IMemory m, int id)
    {
        if ((uint)id >= Count) return true;
        uint rec = NameTable + (uint)id * NameStride;
        return m.ReadU8(rec) == 0x00 && m.ReadU8(rec + 1) == 0xFF;
    }

    // ---- the categories ----

    internal readonly struct Group
    {
        public readonly string Name;
        public readonly int First;
        public readonly int Last;

        public Group(string name, int first, int last)
        {
            Name = name; First = first; Last = last;
        }
    }

    // KF2 scanned runs of named ids between `00 FF` placeholders. KF3's
    // placeholders do not line up with the menu's own categories, so the ranges
    // are taken instead from the `first`/`last` pairs the list callers pass --
    // the item page asks for 0x65..0x92 and the equipment screen for 0x22..0x5e
    // among others -- with our labels, not the game's (GAME.EXE has no category
    // strings). A group's range can still contain placeholder ids, which a panel
    // should skip with <see cref="IsUnused"/>; the probe below does.
    static readonly Group[] _groups =
    [
        new Group("Weapons",                 0,  33),  // ids 0..0x21
        new Group("Helms",                  34,  41),  // 0x22..0x29
        new Group("Armour",                 42,  50),  // 0x2a..0x32
        new Group("Shields",                51,  62),  // 0x33..0x3e
        new Group("Gauntlets",              63,  71),  // 0x3f..0x47
        new Group("Boots",                  72,  80),  // 0x48..0x50
        new Group("Accessories",            81,  94),  // 0x51..0x5e
        new Group("Maps and notes",         95, 100),  // 0x5f..0x64 (95,96,99,100 named)
        new Group("Items and keys",        101, 146),  // 0x65..0x92, the item page's range
        new Group("Ammunition and money",  147, 149),  // ARROWS, LIGHT ARROWS, GOLD COIN
    ];

    /// <summary>
    /// The categories. The ranges are static, so the <paramref name="m"/> is
    /// unused and only kept so a panel written against KF2's `Groups(m)` compiles
    /// unchanged.
    /// </summary>
    internal static Group[] Groups(IMemory m) => _groups;

    // ---- equipment slots (Kf3's addition) ----

    /// <summary>
    /// The five addresses the findings pin down, plus the two rings. The enum
    /// values for the armour/accessory slots are exactly the `slot` argument
    /// `func_8002BB84` wants (its jump table at 0x800117C8 is 0->D4, 1->D5,
    /// 2->D7, 3->D8, 4->D6, 5->D9, 6->DA), so they can be passed straight
    /// through. The weapon has its own setter and is named -1 so a switch cannot
    /// confuse it with a slot.
    ///
    /// 0x801B25AD is treated as an equipped id by `func_8001AF88` but is never
    /// written by the seven-slot setter; the findings only guess it is the
    /// off-hand. It is not exposed here.
    /// </summary>
    internal enum EquipSlot
    {
        Weapon     = -1,
        Helm       =  0,
        Armour     =  1,
        Gauntlets  =  2,
        Boots      =  3,
        Shield     =  4,
        Ring1      =  5,
        Ring2      =  6,
    }

    internal const uint WeaponSlot  = 0x801B25AF;  // u8 id, 0xFF empty
    internal const uint HelmSlot    = 0x801B25D4;
    internal const uint ArmourSlot  = 0x801B25D5;
    internal const uint ShieldSlot  = 0x801B25D6;
    internal const uint GauntletSlot = 0x801B25D7;
    internal const uint BootsSlot   = 0x801B25D8;
    internal const uint Ring1Slot   = 0x801B25D9;
    internal const uint Ring2Slot   = 0x801B25DA;

    /// <summary>An empty equipment slot.</summary>
    internal const int EmptySlot = 0xFF;

    internal static readonly EquipSlot[] EquipSlots =
    [
        EquipSlot.Weapon, EquipSlot.Helm, EquipSlot.Armour, EquipSlot.Shield,
        EquipSlot.Gauntlets, EquipSlot.Boots, EquipSlot.Ring1, EquipSlot.Ring2,
    ];

    internal static uint SlotAddress(EquipSlot slot) => slot switch
    {
        EquipSlot.Weapon    => WeaponSlot,
        EquipSlot.Helm      => HelmSlot,
        EquipSlot.Armour    => ArmourSlot,
        EquipSlot.Shield    => ShieldSlot,
        EquipSlot.Gauntlets => GauntletSlot,
        EquipSlot.Boots     => BootsSlot,
        EquipSlot.Ring1     => Ring1Slot,
        EquipSlot.Ring2     => Ring2Slot,
        _ => 0,
    };

    internal static string SlotName(EquipSlot slot) => slot switch
    {
        EquipSlot.Weapon    => "Weapon",
        EquipSlot.Helm      => "Helm",
        EquipSlot.Armour    => "Armour",
        EquipSlot.Shield    => "Shield",
        EquipSlot.Gauntlets => "Gauntlets",
        EquipSlot.Boots     => "Boots",
        EquipSlot.Ring1     => "Ring 1",
        EquipSlot.Ring2     => "Ring 2",
        _ => "?",
    };

    /// <summary>What is equipped in a slot: the id, or -1 when the slot is empty.</summary>
    internal static int Equipped(IMemory m, EquipSlot slot)
    {
        uint a = SlotAddress(slot);
        if (a == 0) return -1;
        int id = m.ReadU8(a);
        return id == EmptySlot ? -1 : id;
    }

    /// <summary>The equipped item's name, with its id, or "" when the slot is empty.</summary>
    internal static string EquippedLabel(IMemory m, EquipSlot slot)
    {
        int id = Equipped(m, slot);
        return id < 0 ? "" : Label(m, id);
    }

    // The id range each slot's tab in the equipment screen lists (findings
    // "Equipment slots"), used only to reject an equip the game would not offer.
    static (int First, int Last) SlotRange(EquipSlot slot) => slot switch
    {
        EquipSlot.Weapon    => (0, 33),
        EquipSlot.Helm      => (34, 41),
        EquipSlot.Armour    => (42, 50),
        EquipSlot.Shield    => (51, 62),
        EquipSlot.Gauntlets => (63, 71),
        EquipSlot.Boots     => (72, 80),
        EquipSlot.Ring1     => (81, 94),
        EquipSlot.Ring2     => (81, 94),
        _ => (1, 0),
    };

    // ---- queued work ----
    //
    // Queued, not called: the panel draws inside Present, which is inside VSync,
    // and a recompiled routine wants the game thread at a point where it is safe
    // to run. The player-stage post hook is that point.

    static readonly List<int> _pendingGive = [];
    static readonly List<int> _pendingRemove = [];
    static readonly List<(int Id, EquipSlot Slot)> _pendingEquip = [];

    internal static string Status = "";

    internal static void QueueGive(int id, int times = 1)
    {
        if ((uint)id >= Count) return;
        for (int i = 0; i < times; i++) _pendingGive.Add(id);
        Status = $"queued {_pendingGive.Count} item(s) to give";
    }

    /// <summary>Queue one of every named, not-yet-held item up to the spells.</summary>
    internal static void QueueGiveAll(IMemory m)
    {
        int n = 0;
        for (int id = 0; id < Count; id++)
        {
            if (IsUnused(m, id) || Has(m, id)) continue;
            _pendingGive.Add(id);
            n++;
        }
        Status = n == 0 ? "you already hold one of everything" : $"queued {n} item(s)";
    }

    /// <summary>Queue removals through `func_8005D7F8`, one at a time.</summary>
    internal static void QueueRemove(int id, int times = 1)
    {
        if ((uint)id >= Count) return;
        for (int i = 0; i < times; i++) _pendingRemove.Add(id);
        Status = $"queued {_pendingRemove.Count} item(s) to remove";
    }

    /// <summary>
    /// Queue an equip. The id must belong to the slot's own range, which keeps
    /// `func_8002BB84` from indexing its 0x20-stride table out of bounds; the
    /// weapon takes its own setter.
    /// </summary>
    internal static void QueueEquip(int id, EquipSlot slot)
    {
        var (first, last) = SlotRange(slot);
        if (id < first || id > last)
        {
            Status = $"id {id} does not belong in {SlotName(slot)}";
            return;
        }
        _pendingEquip.Add((id, slot));
        Status = $"queued {SlotName(slot)} = id {id}";
    }

    /// <summary>
    /// Zero every count, both arrays. A direct write rather than the game's own
    /// consume, which takes one at a time and would be 150 * 99 calls. Equipment
    /// is left where it is; the panel has the equip action for that.
    /// </summary>
    internal static void ClearAll(IMemory m)
    {
        for (int id = 0; id < Count; id++)
        {
            m.WriteU8(InvBase + (uint)id, 0);
            m.WriteU8(OverflowBase + (uint)id, 0);
        }
        Status = "inventory cleared";
    }

    /// <summary>
    /// Post-hook on main-loop stage 4, the player's tick (GameState.PlayerStage,
    /// 0x80030FCC): the last word before stage 10 copies the player into the
    /// camera. The feed queue and the probe run here.
    /// </summary>
    [PostHook("game", Address = GameState.PlayerStage)]
    static void AfterPlayerStage(CpuContext c, IMemory m)
    {
        if (!GameState.IsInGame(m)) return;

        RunProbe(m);

        if (_pendingGive.Count == 0 && _pendingRemove.Count == 0 && _pendingEquip.Count == 0) return;
        RunQueued(c, m);
    }

    static void RunQueued(CpuContext c, IMemory m)
    {
        // func_8005D898 ends in a call through the area module's dispatch slot 6
        // (u32[u32[0x8018FAE0] + 0x18]); the remove routine uses slot 0x2c. In an
        // area those are filled -- the module calls them itself -- but a null
        // would be a jump to zero, so they are checked rather than assumed, and a
        // direct write is what a missing hook falls back to.
        bool useGameRoutine = HasModuleHook(m);

        int given = 0, refused = 0, removed = 0, empty = 0, equipped = 0;
        var saved = c.Snapshot();

        foreach (int id in _pendingGive)
        {
            if (useGameRoutine)
            {
                c.A0 = (uint)id;
                KingsField3.func_8005D898(c, m);
                if (c.V0 != 0) refused++; else given++;
            }
            else if (Held(m, id) < MaxHeld)
            {
                m.WriteU8(InvBase + (uint)id, (byte)(Held(m, id) + 1));
                given++;
            }
            else if (Overflow(m, id) < MaxHeld)
            {
                m.WriteU8(OverflowBase + (uint)id, (byte)(Overflow(m, id) + 1));
                given++;
            }
            else refused++;
        }

        foreach (int id in _pendingRemove)
        {
            if (useGameRoutine)
            {
                c.A0 = (uint)id;
                KingsField3.func_8005D7F8(c, m);
                if (c.V0 != 0) empty++; else removed++;
            }
            else if (Held(m, id) != 0)
            {
                m.WriteU8(InvBase + (uint)id, (byte)(Held(m, id) - 1));
                removed++;
            }
            else if (Overflow(m, id) != 0)
            {
                m.WriteU8(OverflowBase + (uint)id, (byte)(Overflow(m, id) - 1));
                removed++;
            }
            else empty++;
        }

        foreach (var (id, slot) in _pendingEquip)
        {
            if (slot == EquipSlot.Weapon)
            {
                c.A0 = (uint)id;
                KingsField3.func_8002BDC0(c, m);
            }
            else
            {
                c.A0 = (uint)id;
                c.A1 = (uint)(int)slot;
                KingsField3.func_8002BB84(c, m);
            }
            equipped++;
        }

        c.Restore(saved);

        _pendingGive.Clear();
        _pendingRemove.Clear();
        _pendingEquip.Clear();

        Status = $"gave {given}, removed {removed}, equipped {equipped}"
               + (refused != 0 ? $"; {refused} already full" : "")
               + (empty != 0 ? $"; {empty} already empty" : "");
        Console.WriteLine($"[kf3debug] {Status}"
                        + (useGameRoutine ? "" : " (direct write: the area module has no hook)"));

        if (ProbeLevel >= 2) ProbeAfterChange(m);
    }

    static bool HasModuleHook(IMemory m)
    {
        uint module = m.ReadU32(ModulePtr);
        if (module < 0x80010000u || module >= 0x80200000u) return false;
        uint slot = m.ReadU32(module + 0x18);
        return slot >= 0x80010000u && slot < 0x80200000u;
    }

    // ---- the probe ----
    //
    // `KF3_DEBUG_ITEMS_PROBE=1` prints the table once, the first time an area is
    // up. It is what says the addresses above are right without anyone looking at
    // a screen: a run of names that decode as English, in groups whose boundaries
    // fall where the findings' menu callers say they do, is the reading being
    // correct; a wrong base would print hex escapes and nonsense. It also prints
    // the equipment read-back, which is the check on the slot addresses.

    static bool _probeDone;

    /// <summary>
    /// 0 off, 1 print the table, 2 also queue one of everything -- which is the
    /// acceptance test for the give path, since the count beside every name on
    /// the next print is what `func_8005D898` actually did.
    /// </summary>
    internal static readonly int ProbeLevel =
        int.TryParse(Environment.GetEnvironmentVariable("KF3_DEBUG_ITEMS_PROBE"), out int lv) ? lv : 0;

    internal static void RunProbe(IMemory m)
    {
        if (_probeDone || ProbeLevel <= 0) return;
        _probeDone = true;

        Console.WriteLine($"[kf3debug] inventory at 0x{InvBase:X8}/0x{OverflowBase:X8}, {Count} ids, "
                        + $"names at 0x{NameTable:X8} stride 0x{NameStride:X}");

        foreach (var g in Groups(m))
        {
            Console.WriteLine($"[kf3debug]   {g.First,3}-{g.Last,3}  {g.Name}");
            for (int id = g.First; id <= g.Last; id++)
            {
                if (IsUnused(m, id)) continue;   // KF3's fixed ranges span dead ids
                Console.WriteLine($"[kf3debug]     {id,3}  x{Held(m, id),-3} {Label(m, id)}");
            }
        }

        Console.WriteLine($"[kf3debug] {DistinctHeld(m)} of {Count} ids held");

        Console.WriteLine("[kf3debug] equipment:");
        foreach (var slot in EquipSlots)
        {
            int id = Equipped(m, slot);
            Console.WriteLine(id < 0
                ? $"[kf3debug]   {SlotName(slot),-9} (empty)"
                : $"[kf3debug]   {SlotName(slot),-9} x{Held(m, id)} {Label(m, id)}");
        }

        if (ProbeLevel >= 2) QueueGiveAll(m);
    }

    /// <summary>
    /// Re-print the counts after the queue has run. Only at probe level 2, and
    /// only once -- the "did the give path work" half of the probe.
    /// </summary>
    static void ProbeAfterChange(IMemory m)
    {
        Console.WriteLine($"[kf3debug] after the change: {DistinctHeld(m)} of {Count} ids held");
        for (int id = 0; id < Count; id++)
            if (Has(m, id))
                Console.WriteLine($"[kf3debug]     {id,3}  x{Held(m, id),-3} {Label(m, id)}");
    }

    internal static void Reset()
    {
        _pendingGive.Clear();
        _pendingRemove.Clear();
        _pendingEquip.Clear();
        _probeDone = false;
        Status = "";
    }
}
