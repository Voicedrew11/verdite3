// ModCompiler compiles mods with no implicit usings, so every namespace the
// file needs must be named here -- including System.
using System;
using RecompOne.Runtime.Context;
using RecompOne.Runtime.Memory;
using RecompOne.Runtime.Modding;
using Recompiled;
// Upstream 0409bc2 emits one class per overlay, because CoreCLR caps a class
// at 65535 methods. Every func_ named here is GAME.EXE's, so the alias names the
// overlay once and the call sites below are unchanged.
using KingsField3 = Recompiled.KingsField3_game;

namespace Kf3.Mods.Debug;

/// <summary>
/// Editing the character: experience, level, vitals, gold, the six base
/// attributes, the five condition timers and the seventeen combat ratings the
/// status screen's second page shows.
///
/// ---- the one thing that makes this more than a memory editor ----
///
/// The player block at 0x801B24E4 is not a flat sheet of stats. Twenty-three of
/// its words are a **cache**: `func_80029500` opens by zeroing the eight OFFENSE
/// and nine DEFENSE words, copying the six base attributes (0x801B2516..2520)
/// into their equipment-adjusted copies (0x801B2524..252E) -- halving them while
/// `CondCurse` is set -- then walks the equipped weapon (`0x801B25AF`, record base
/// `0x801D37A4`, stride 0x44) and the seven accessory slots (`0x801B25D4..25DA`),
/// and applies the condition modifiers. Twenty-three routines call it -- equip,
/// unequip, use an item, level up, load, and several sites inside stage 4 -- so a
/// number typed into one of those twenty-three words survives only until the next
/// of those happens, which in play is seconds.
///
/// So this file does two different things with two different mechanisms:
///
///   * The **character** -- EXP, level, HP/MP and their maxima, gold, the six
///     base attributes and the five condition timers -- is plain memory. Nothing
///     recomputes it; a write sticks, and the save carries it (`func_8005F7BC`
///     packs every one of these addresses).
///
///   * The **ratings** -- the six adjusted attributes and the seventeen
///     OFFENSE/DEFENSE words -- are held by a post hook on `func_80029500`
///     itself, which is the only writer. Writing them from the panel is offered
///     too, and says plainly that it will not last.
///
/// Editing a base attribute is therefore the honest way to be stronger: it is a
/// real attribute, it persists, and the recompute picks it up. Locking the
/// adjusted copies is the blunt way, and the panel says which is which.
///
/// ---- calling the game's own routines ----
///
/// Two buttons run recompiled MIPS rather than writing memory: "Level up" calls
/// `func_8002A310(s16 gain)`, the game's own level-up, so the level, both maxima,
/// the base attributes and the next-level threshold all move by the game's own
/// table (0x8009F114, 12-byte entries, levels 1..99) instead of by a guess;
/// "Recalculate" calls `func_80029500` so an edited base attribute shows on the
/// status screen at once. Both need a CpuContext, so both are queued and run from
/// the stage-4 post hook -- the same rule the other features here follow, and for
/// the same reason.
/// </summary>
internal static class Attributes
{
    /// <summary>The game's own EXP ceiling, from func_8002A310's clamp (0xF423F).</summary>
    internal const int ExpMax = 999_999;

    /// <summary>
    /// The practical level ceiling: the level byte is clamped at 0xFF, but the
    /// growth table ends at 99 and the level-99+ path is inert (its deltas are
    /// zero and its base is 999), so leveling past 99 gains nothing and can spin
    /// func_8002A310's own loop. The notes' ceiling.
    /// </summary>
    internal const int LevelMax = 99;

    // ---- feature-specific addresses, in the player block ----
    //
    // GameState.cs already names EXP/ExpNext/Level/HP/MP and the position/view
    // words; everything below is this file's own.

    internal const uint Gold     = 0x801B2534;   // u32, drawn as 7 digits

    // Six base attributes 0x801B2516..0x801B2520 (u16). Grown by level-up and the
    // walk/action counters. The exact six names are unresolved (see the comment on
    // BaseAttributes); the notes trace their status labels as PWR ... WIS ... MAG.
    internal const uint BaseStat0 = 0x801B2516;
    internal const uint BaseStat1 = 0x801B2518;
    internal const uint BaseStat2 = 0x801B251A;
    internal const uint BaseStat3 = 0x801B251C;
    internal const uint BaseStat4 = 0x801B251E;
    internal const uint BaseStat5 = 0x801B2520;

    // Their equipment-adjusted copies 0x801B2524..0x801B252E (u16). Derived: the
    // recompute copies base -> here (halved while cursed) and adds equipment.
    internal const uint AdjStat0 = 0x801B2524;
    internal const uint AdjStat1 = 0x801B2526;
    internal const uint AdjStat2 = 0x801B2528;
    internal const uint AdjStat3 = 0x801B252A;
    internal const uint AdjStat4 = 0x801B252C;
    internal const uint AdjStat5 = 0x801B252E;

    // Eight OFFENSE words, 0x801B2538..0x801B2546, then a gap at 0x801B2548 the
    // recompute neither zeroes nor sums, then nine DEFENSE words at
    // 0x801B254A..0x801B255A. All derived.
    internal const uint Offense1 = 0x801B2538;
    internal const uint Offense2 = 0x801B253A;
    internal const uint Offense3 = 0x801B253C;
    internal const uint Offense4 = 0x801B253E;
    internal const uint Offense5 = 0x801B2540;
    internal const uint Offense6 = 0x801B2542;
    internal const uint Offense7 = 0x801B2544;
    internal const uint Offense8 = 0x801B2546;
    internal const uint Defense1 = 0x801B254A;
    internal const uint Defense2 = 0x801B254C;
    internal const uint Defense3 = 0x801B254E;
    internal const uint Defense4 = 0x801B2550;
    internal const uint Defense5 = 0x801B2552;
    internal const uint Defense6 = 0x801B2554;
    internal const uint Defense7 = 0x801B2556;
    internal const uint Defense8 = 0x801B2558;
    internal const uint Defense9 = 0x801B255A;

    // The five conditions. The status screen picks its CONDITION label by reading
    // these words nonzero, in this order: PARALYZE 2568, CURSE 255E, SLOW 2566,
    // POISON 255C, DARK 2562 (func_800227EC at 0x8002302C onward). They are
    // countdown timers, not just flags -- stage 4 drains 255C, for instance -- so
    // they are exposed signed.
    //
    // damage.md reads part of this range differently (255C a stun timer, 255E an
    // "apply HP accumulator" flag, 2562 an MP accumulator, 2564/2566/2568 generic
    // effect counters). The status-screen labels and func_80029500's halving on
    // 255E are explicit, so those five are exposed and the mismatch is recorded;
    // the pair words 2560 (with CURSE) and 2564 (with DARK) and the condition-state
    // words 256A..2578 are left out, since only one note reads them and neither
    // says they are player-visible.
    internal const uint CondPoison   = 0x801B255C;
    internal const uint CondCurse    = 0x801B255E;
    internal const uint CondDark     = 0x801B2562;
    internal const uint CondSlow     = 0x801B2566;
    internal const uint CondParalyze = 0x801B2568;

    // The two recompiled routines this file runs.
    internal const uint RecomputeRoutine = 0x80029500;   // no args, reads the block
    internal const uint LevelUpRoutine   = 0x8002A310;   // s16 gain, in $a0

    /// <summary>
    /// One editable word. Width is 1, 2 or 4 bytes; <see cref="Signed"/> is set
    /// for the condition timers, which the status screen loads with lh.
    /// </summary>
    internal readonly struct Field
    {
        public readonly string Name;
        public readonly uint Address;
        public readonly int Width;
        public readonly bool Signed;
        public readonly string Tip;

        public Field(string name, uint address, int width, string tip = "", bool signed = false)
        {
            Name = name; Address = address; Width = width; Tip = tip; Signed = signed;
        }
    }

    // ---- the character: plain memory, nothing recomputes it ----

    internal static readonly Field[] Progress =
    [
        new("Experience", GameState.Exp, 4, "Capped at 999999 by the game's own level-up routine."),
        new("Next level at", GameState.ExpNext, 4,
            "The EXP the next level needs. func_8002A310 compares against this word and "
          + "rewrites it from the game's table each time you level."),
        new("Level", GameState.Level, 1,
            "The byte alone. Typing a number here does NOT grant the HP, MP and attribute "
          + "increases that levelling gives -- use the Level up button for that."),
    ];

    internal static readonly Field[] Vitals =
    [
        new("HP", GameState.Hp, 2),
        new("Max HP", GameState.MaxHp, 2, "Zero here is how everything in this mod tells "
                                        + "\"no character\" from \"a character at 0 HP\"."),
        new("MP", GameState.Mp, 2),
        new("Max MP", GameState.MaxMp, 2),
        new("Gold", Gold, 4),
    ];

    // The six base attributes. The notes name none of them: the status-screen
    // labels are pre-rendered sprites, and the only glyph text traced reads
    // PWR ... WIS ... MAG, so the mapping is a guess. What is certain is their
    // growth: stat 0 starts at 20 and is the one the level table advances (+20 at
    // level 1) and the walk counter raises every 100 steps; stat 5 starts at 10;
    // stats 1..4 start at 0 and grow ~40% per level and by 400 action-uses each.
    internal static readonly Field[] BaseAttributes =
    [
        new("Base stat 0 (PWR?)", BaseStat0, 2,
            "Starts at 20. The level table advances this one (+20 at level 1) and every "
          + "100 steps add 1. Notes trace a PWR label; likely strength. Guess."),
        new("Base stat 1", BaseStat1, 2, "Starts at 0; ~40% chance of +1 per level and "
                                        + "+1 per 400 action-uses. Name unresolved."),
        new("Base stat 2", BaseStat2, 2, "Starts at 0; ~40% chance of +1 per level and "
                                        + "+1 per 400 action-uses. Name unresolved."),
        new("Base stat 3", BaseStat3, 2, "Starts at 0; ~40% chance of +1 per level and "
                                        + "+1 per 400 action-uses. Name unresolved."),
        new("Base stat 4", BaseStat4, 2, "Starts at 0; ~40% chance of +1 per level and "
                                        + "+1 per 400 action-uses. Name unresolved."),
        new("Base stat 5 (MAG?)", BaseStat5, 2,
            "Starts at 10. Notes trace WIS and MAG labels near here; likely magic. Guess."),
    ];

    internal static readonly Field[] Conditions =
    [
        new("Poison",   CondPoison,   2, "Countdown timer; stage 4 drains 1 HP per 15 while set. "
                                        + "Name confirmed by the status screen; damage.md read it "
                                        + "as a stun timer. Marked certain from the label.", true),
        new("Curse",    CondCurse,    2, "Also halves the six adjusted attributes while set "
                                        + "(func_80029500).", true),
        new("Dark",     CondDark,     2, "Name confirmed by the status screen; damage.md read "
                                        + "this word as an MP accumulator. Uncertain.", true),
        new("Slow",     CondSlow,     2, "Name confirmed by the status screen.", true),
        new("Paralyze", CondParalyze, 2, "Name confirmed by the status screen.", true),
    ];

    // ---- the adjusted attributes: func_80029500 owns these ----

    internal static readonly Field[] Adjusted =
    [
        new("Adj stat 0", AdjStat0, 2, "Base stat 0, halved while cursed, plus equipment."),
        new("Adj stat 1", AdjStat1, 2, "Base stat 1, halved while cursed, plus equipment."),
        new("Adj stat 2", AdjStat2, 2, "Base stat 2, halved while cursed, plus equipment."),
        new("Adj stat 3", AdjStat3, 2, "Base stat 3, halved while cursed, plus equipment."),
        new("Adj stat 4", AdjStat4, 2, "Base stat 4, halved while cursed, plus equipment."),
        new("Adj stat 5", AdjStat5, 2, "Base stat 5, halved while cursed, plus equipment."),
    ];

    // ---- the ratings: func_80029500 owns these ----

    // The element names are the notes' best guess from the status-page order; only
    // the OFFENSE/DEFENSE grouping and the count (8 and 9) are certain. damage.md
    // did not touch this page, so there is no second reading to reconcile.
    internal static readonly Field[] Offense =
    [
        new("Offense 1", Offense1, 2),
        new("Offense 2", Offense2, 2),
        new("Offense 3", Offense3, 2),
        new("Offense 4", Offense4, 2),
        new("Offense 5", Offense5, 2),
        new("Offense 6", Offense6, 2),
        new("Offense 7", Offense7, 2),
        new("Offense 8", Offense8, 2),
    ];

    internal static readonly Field[] Defense =
    [
        new("Defense 1", Defense1, 2),
        new("Defense 2", Defense2, 2),
        new("Defense 3", Defense3, 2),
        new("Defense 4", Defense4, 2),
        new("Defense 5", Defense5, 2),
        new("Defense 6", Defense6, 2),
        new("Defense 7", Defense7, 2),
        new("Defense 8", Defense8, 2),
        new("Defense 9", Defense9, 2),
    ];

    /// <summary>
    /// Every word func_80029500 rebuilds, in one array so the lock is one loop.
    /// </summary>
    internal static readonly Field[] Derived =
        [.. Adjusted, .. Offense, .. Defense];

    /// <summary>
    /// Hold the derived words at <see cref="Held"/> against the recompute.
    /// Off by default: the values the game computes are the honest ones, and the
    /// base attributes above are the place to change them from.
    /// </summary>
    internal static bool LockDerived;

    internal static readonly int[] Held = new int[Derived.Length];

    // ---- typed access ----

    internal static int Read(IMemory m, in Field f) => f.Width switch
    {
        1 => m.ReadU8(f.Address),
        2 => f.Signed ? (short)m.ReadU16(f.Address) : m.ReadU16(f.Address),
        _ => (int)m.ReadU32(f.Address),
    };

    internal static void Write(IMemory m, in Field f, int value)
    {
        switch (f.Width)
        {
            case 1: m.WriteU8(f.Address, (byte)Math.Clamp(value, 0, 255)); break;
            case 2:
                m.WriteU16(f.Address, f.Signed
                    ? (ushort)(short)Math.Clamp(value, short.MinValue, short.MaxValue)
                    : (ushort)Math.Clamp(value, 0, ushort.MaxValue));
                break;
            default: m.WriteU32(f.Address, (uint)Math.Max(value, 0)); break;
        }
    }

    /// <summary>
    /// The index into <see cref="Held"/> for an address, or -1. Keyed on the
    /// address rather than on the struct, so it needs no equality on Field.
    /// </summary>
    internal static int HeldIndex(uint address)
    {
        for (int i = 0; i < Derived.Length; i++)
            if (Derived[i].Address == address) return i;
        return -1;
    }

    /// <summary>Copy the live derived words into the hold, so switching the lock on changes nothing.</summary>
    internal static void PrimeHold(IMemory m)
    {
        for (int i = 0; i < Derived.Length; i++) Held[i] = Read(m, Derived[i]);
    }

    // ---- quick actions that are only memory ----

    internal static void FullHeal(IMemory m)
    {
        m.WriteU16(GameState.Hp, m.ReadU16(GameState.MaxHp));
        m.WriteU16(GameState.Mp, m.ReadU16(GameState.MaxMp));
    }

    internal static void CureConditions(IMemory m)
    {
        foreach (var f in Conditions) m.WriteU16(f.Address, 0);
    }

    // ---- quick actions that run the game's own code ----
    //
    // Queued, not called: the panel draws inside Present, which is inside VSync,
    // and a recompiled routine wants the game thread at a point where it is safe
    // to run. Main-loop stage 4 (func_80030FCC) is that point, and it is where
    // every other feature here does its work.

    static int _pendingLevelUps;
    static bool _pendingRecalc;

    internal static string Status = "";

    /// <summary>
    /// Queue one run of the game's own level-up. EXP is topped up to the
    /// threshold first, since func_8002A310 returns early below it -- so the
    /// button means "level up now" rather than "grant some experience".
    /// </summary>
    internal static void QueueLevelUp(int times = 1)
    {
        _pendingLevelUps += Math.Max(times, 0);
        Status = $"queued {_pendingLevelUps} level-up(s)";
    }

    /// <summary>Queue func_80029500 so an edited base attribute shows at once.</summary>
    internal static void QueueRecalculate()
    {
        _pendingRecalc = true;
        Status = "queued a recalculation";
    }

    /// <summary>
    /// End of main-loop stage 4, the player's own tick (GameState.PlayerStage,
    /// 0x80030FCC) -- the same site the other features here use.
    ///
    /// Order matters: the queued work runs first, then the lock, so a lock that
    /// is on still wins over a recompute this call triggered.
    /// </summary>
    [PostHook("game", Address = 0x80030FCC)]
    static void AfterPlayerStage(CpuContext c, IMemory m)
    {
        if (!GameState.IsInGame(m)) return;

        if (_pendingLevelUps > 0) RunLevelUps(c, m);
        if (_pendingRecalc) RunRecalculate(c, m);

        if (LockDerived) ApplyHold(m);
    }

    /// <summary>
    /// The recompute itself. A post rather than a pre, because the point is to
    /// overwrite what it just wrote; every other writer of these words goes
    /// through here, so this one hook covers equip, unequip, load and level-up
    /// alike.
    /// </summary>
    [PostHook("game", Address = 0x80029500)]
    static void AfterRecalculate(CpuContext c, IMemory m)
    {
        if (LockDerived && GameState.IsInGame(m)) ApplyHold(m);
    }

    static void ApplyHold(IMemory m)
    {
        for (int i = 0; i < Derived.Length; i++) Write(m, Derived[i], Held[i]);
    }

    static void RunLevelUps(CpuContext c, IMemory m)
    {
        int want = _pendingLevelUps;
        _pendingLevelUps = 0;

        var saved = c.Snapshot();
        int before = m.ReadU8(GameState.Level);

        for (int i = 0; i < want; i++)
        {
            if (m.ReadU8(GameState.Level) >= LevelMax) break;

            // func_8002A310(s16 gain): EXP += gain, clamped at 999999, and it
            // returns without doing anything while EXP is below the threshold
            // word. Topping EXP up to the threshold first and passing zero makes
            // it level exactly once, by its own table.
            uint need = m.ReadU32(GameState.ExpNext);
            if (m.ReadU32(GameState.Exp) < need)
                m.WriteU32(GameState.Exp, Math.Min(need, (uint)ExpMax));

            c.A0 = 0u;
            KingsField3.func_8002A310(c, m);
        }

        c.Restore(saved);

        int after = m.ReadU8(GameState.Level);
        Status = after == before
            ? $"no level gained (level {after}; the table ends at {LevelMax})"
            : $"level {before} -> {after}";
        Console.WriteLine($"[kf3debug] {Status}");
    }

    static void RunRecalculate(CpuContext c, IMemory m)
    {
        _pendingRecalc = false;

        var saved = c.Snapshot();
        KingsField3.func_80029500(c, m);
        c.Restore(saved);

        Status = $"recalculated: adj stat 0 {m.ReadU16(AdjStat0)}, "
               + $"adj stat 5 {m.ReadU16(AdjStat5)}";
    }

    internal static void Reset()
    {
        LockDerived = false;
        _pendingLevelUps = 0;
        _pendingRecalc = false;
        Status = "";
    }
}
