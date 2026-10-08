// ModCompiler compiles mods with no implicit usings, so every namespace the
// file needs must be named here -- including System.
using System;
using RecompOne.Runtime.Context;
using RecompOne.Runtime.Memory;
using RecompOne.Runtime.Modding;

namespace Kf3.Mods.Debug;

/// <summary>
/// Invincibility, infinite MP and the speed multiplier.
///
/// Ported from the KF2 reference Cheats.cs, but every address and fact below is
/// this game's, taken from scratch/findings/damage.md, magic.md and movement.md.
/// KF2's addresses are a different game and are not used.
///
/// ---- invincibility: three HP routines, a latch and a catch-all ----
///
/// KF3 has no single "HP" word worth watching: the things that kill you go
/// through three HP routines plus the death latch, and several of those routes
/// can do it at full HP.
///
///   func_8002A6F4(posPtr, amount, flags)   THE take-damage routine. a1 is the
///                          damage; `if (action == 0x11) return;` then
///                          `if (a1 == 0) goto <epilogue>`. Every weapon, trap
///                          and fall damage goes through it (callers:
///                          func_8002AB18 and func_8002ED60).
///
///   func_8002A6A0(delta)   signed HP add; its <=0 arm clamps to 0 and calls the
///                          death latch. ONE caller, stage 4 at 0x80031EEC, with
///                          a0 = -1 -- the poison/starvation tick.
///
///   func_80030BE0(delta)   signed HP add clamped to [0, maxHP]; the equipment
///                          regen/drain driver call it with +1 and -1. Its -1
///                          arm is a death route that reaches neither of the
///                          other two routines.
///
///   func_80030A6C(posPtr)  the death LATCH and the only writer of state 0x11.
///                          Nine call sites: the three above, plus func_8002D2A0
///                          (drowning), func_8002ECBC and func_8002F320 (falls)
///                          and func_80028D54's three reference-plane warnings
///                          -- all of which can fire at full HP. No amount of
///                          watching the HP word sees them coming.
///
/// So: clamp the argument on the three HP routines so the bar never moves, and
/// refuse the latch so nothing can mark you dead by any route. Neutralising the
/// *argument* rather than skipping the call matters for func_8002A6F4: its own
/// `if (a1 == 0)` branch is an early return to the epilogue, so HP subtraction,
/// the death call, the knockback AND the hurt flash are all skipped together.
/// Unlike the KF2 reference, a blocked hit therefore does NOT flash: the flash
/// word 0x801B2658 is set after that branch (0x8002A8CC) and is never reached.
/// That also means "no knockback" is not a separate option here -- zeroing the
/// amount is the only way to stop knockback, and it necessarily stops the flash
/// and the damage with it, so there is nothing extra to toggle.
///
/// There is deliberately no PSMemory.Freeze on the HP word. It would hold the
/// value, but func_8001B254 alone has half a dozen heal arms, and func_80030BE0
/// (+1) and the level-up refill in func_8002A310 heal too; a frozen word drops
/// all of them silently, which would look like a bug in the game.
///
/// ---- infinite MP: restore, do not freeze ----
///
/// MP is spent by func_8002D130 (the charged cast), func_8002DEEC (the id-driven
/// cast) and func_8002AB18 (the flat "quarter of max MP" arm). All of those run
/// under stage 4, whose own tail is the last player-side code before the camera
/// and the frame, so one restore of MP = maxMP at the end of stage 4 covers every
/// writer -- exactly as the KF2 reference restored at the end of its player
/// stage. It is a restore, not a Freeze: the full-restore paths (func_8002B3A8
/// on area entry, func_8001B254's item ids) and MP regen (func_80030C6C) still
/// work, and the spend itself happens normally on the way in (the cast's
/// `MP < cost` guard therefore passes).
///
/// ---- the catch-all ----
///
/// HP has more writers than the three routines hooked above, so the end of
/// stage 4 also repairs any HP loss it sees. With the latch blocked this is a
/// backstop, not a mechanism. It deliberately does nothing once the state byte
/// says dead: if the flag is switched on mid-death the state-17 handler forces
/// HP to zero every frame, and fighting it would leave the player
/// alive-but-dead rather than either.
///
/// This is "restore losses", not a standalone infinite-HP-regen feature; there
/// is no separate HP-regen switch.
///
/// ---- enemies ignoring you: the picker is told one number ----
///
/// Stage 5 runs a creature's think, func_8004C01C, one tick in four while it
/// is awake. It is the KF2 reference's think instruction for instruction:
///
///     dx = rec[+0x2C] - playerX          (0x801B25F0)
///     dz = rec[+0x34] - playerZ          (0x801B25F8)
///     a0 = func_80016C08(dx, dz)         the horizontal distance
///          func_8004BF1C(a0)             pick a behaviour for that distance
///
/// func_8004BF1C walks the sixteen rule pointers at desc+0x38, scores each
/// against that distance with func_8004B984, and installs the best through
/// func_8004B94C. The scorer's distance gates are u16s (rule+0xC, +0x10, +0x12,
/// +0x14, +0x16, +0x1A) compared as `(int)range < (int)dist`, so **any distance
/// above 65535 fails every "player is near" rule** and passes every "player is
/// far" one. The waker func_8004C1F0 hands the picker the same distance when a
/// creature wakes, and the behaviours ask for a fresh pick through
/// func_8004C104 -> func_8004C01C, so this one entry sees every pick there is.
///
/// So the switch is a pre-hook on func_8004BF1C that overwrites a0, and the
/// original runs. Waking and sleeping (func_8004C1F0, the state byte rec+0x9)
/// are not touched, and must not be: the model walk draws a creature on that
/// byte, so the obvious decoy-player-position version would make enemies
/// vanish rather than ignore you -- the KF2 reference's finding, and the same
/// byte here. Creatures still wake, animate, draw, collide and take damage.
/// What one already mid-swing does, and what a creature with no "far" rule
/// settles on (the picker keeps the first rule when every score is 0), are
/// measured in docs/MODS.md, not assumed here.
///
/// ---- dropped from the reference ----
///
/// * The reference's speed hook skipped scaling while Noclip was on. Noclip is a
///   sibling file this one must not depend on, so the guard is gone and the
///   multiplier simply composes with whatever also writes the two rate words.
/// </summary>
internal static class Cheats
{
    internal static bool Invincible;
    internal static bool InfiniteMp;
    internal static bool SpeedEnabled;
    internal static bool Peaceful;

    // 1.0 is the game's own speed. The scale clamps to at least 1 unit, because
    // func_8002F5C0 and func_8002F9BC treat a rate of zero or less as "not
    // controllable" and spring the velocity to zero -- a multiplier that rounded
    // a rate down to zero would silently switch control off.
    internal static float SpeedMultiplier = 2f;

    internal static long BlockedHits;
    internal static long BlockedDeaths;
    internal static long RestoredHp;
    internal static long RestoredMp;
    internal static long IgnoredPicks;

    // ---- the three HP routines ----

    /// <summary>
    /// The take-damage routine. a1 is the damage; zeroing it takes the
    /// function's own `if (a1 == 0) goto <epilogue>` branch at 0x8002A72C past
    /// the HP store, the death call, the knockback and the hurt flash. A blocked
    /// hit is therefore a swung-and-missed hit: no HP loss, no stagger, no
    /// tint. It is the only way to stop knockback, so there is no separate
    /// no-knockback switch.
    /// </summary>
    [PreHook("game", Address = 0x8002A6F4)]
    static void BeforeTakeDamage(CpuContext c, IMemory m)
    {
        if (!Invincible || c.A1 == 0u) return;
        if (!GameState.IsInGame(m)) return;

        c.A1 = 0u;
        BlockedHits++;
    }

    /// <summary>
    /// The poison/starvation tick. Only a negative delta is a loss; a positive
    /// one is a heal and must pass through untouched.
    /// </summary>
    [PreHook("game", Address = 0x8002A6A0)]
    static void BeforeAddHp(CpuContext c, IMemory m) => ClampDelta(c, m);

    /// <summary>
    /// The equipment regen/drain. Same shape as func_8002A6A0 and hooked the
    /// same way -- its -1 arm is a death route that reaches neither of the other
    /// two routines.
    /// </summary>
    [PreHook("game", Address = 0x80030BE0)]
    static void BeforeAdjustHp(CpuContext c, IMemory m) => ClampDelta(c, m);

    static void ClampDelta(CpuContext c, IMemory m)
    {
        if (!Invincible) return;
        if (!GameState.IsInGame(m)) return;

        if ((int)c.A0 < 0)
        {
            c.A0 = 0u;
            BlockedHits++;
        }
    }

    // ---- enemies ignoring you ----

    // The distance handed to the behaviour picker while this is on. The scorer
    // compares against u16 range fields, so anything past 65535 fails every
    // ranged rule; this is twice that, the KF2 reference's value.
    const uint FarAway = 0x20000;

    /// <summary>
    /// The behaviour picker, told the player is across the map.
    ///
    /// a0 is the horizontal distance to the player and is the picker's only
    /// input about them, so overwriting it is the whole cheat. The original
    /// still runs: the creature picks, and keeps picking, whatever it does when
    /// nobody is near.
    /// </summary>
    [PreHook("game", Address = 0x8004BF1C)]
    static void BeforeBehaviourPick(CpuContext c, IMemory m)
    {
        if (!Peaceful) return;
        if (!GameState.IsInGame(m)) return;

        c.A0 = FarAway;
        IgnoredPicks++;
    }

    // ---- the death latch ----

    /// <summary>
    /// The only writer of the dead state, 0x11, anywhere in the game. Skipping
    /// it is what makes "invincible" mean it: drowning (func_8002D2A0), the two
    /// falls (func_8002ECBC, func_8002F320) and the below-the-floor check
    /// (func_80028D54) all call this at full HP, and nothing that watches the HP
    /// word can see them coming.
    ///
    /// Returning false skips the original entirely, which is right here -- there
    /// is nothing in the routine but the latch, the death sound and two timer
    /// resets, and none of it should happen.
    /// </summary>
    [PreHook("game", Address = 0x80030A6C)]
    static bool BeforeDeathLatch(CpuContext c, IMemory m)
    {
        if (!Invincible) return true;
        if (!GameState.IsInGame(m)) return true;

        BlockedDeaths++;
        return false;
    }

    // ---- per-frame ----

    /// <summary>
    /// End of stage 4 func_80030FCC, the player's tick. The catch-all for HP and
    /// the whole of infinite MP.
    ///
    /// The catch-all earns its place because HP has more writers than the three
    /// routines hooked above. Restoring here is what keeps "invincible" true
    /// against a writer nobody has classified yet.
    ///
    /// It deliberately does nothing once the state byte says dead. With the
    /// latch hooked that should be unreachable while invincible, but if the flag
    /// is switched on mid-death, the state-17 handler forces HP to zero every
    /// frame, and fighting it would leave the player alive-but-dead rather than
    /// either.
    ///
    /// MP is restored here rather than frozen, so the cast cost is charged
    /// normally and the `MP < cost` guard still passes; only the resulting bar is
    /// put back.
    /// </summary>
    [PostHook("game", Address = GameState.PlayerStage)]
    static void AfterPlayerStage(CpuContext c, IMemory m)
    {
        if (!GameState.IsInGame(m)) return;
        if (GameState.IsDead(m)) return;

        if (Invincible)
        {
            ushort hp = m.ReadU16(GameState.Hp);
            ushort maxHp = m.ReadU16(GameState.MaxHp);
            if (hp < maxHp)
            {
                m.WriteU16(GameState.Hp, maxHp);
                RestoredHp++;
            }
        }

        if (InfiniteMp)
        {
            ushort mp = m.ReadU16(GameState.Mp);
            ushort maxMp = m.ReadU16(GameState.MaxMp);
            if (mp < maxMp)
            {
                m.WriteU16(GameState.Mp, maxMp);
                RestoredMp++;
            }
        }
    }

    // ---- speed ----

    /// <summary>
    /// Scale the turn rate word 0x801B2668 on the way into the look routine.
    ///
    /// Stage 4 rewrites the two rate words *every frame* (walk 0xC8 at
    /// 0x80031188; turn 0x20 moving / 0x28 standing at 0x80031194/0x800311B0),
    /// before it dispatches to func_8002F5C0 (look) and then func_8002F9BC
    /// (walk). So a pre-hook on each routine sees the fresh value; scaling the
    /// word instead after the routine had read it would be overwritten before the
    /// next tick.
    ///
    /// The two words share nothing, so unlike the KF2 reference -- where one hook
    /// on the look routine covered both consumers because stage 3 wrote them
    /// first -- KF3 needs one pre-hook per word.
    /// </summary>
    [PreHook("game", Address = 0x8002F5C0)]
    static void BeforeLook(CpuContext c, IMemory m)
    {
        if (!SpeedEnabled) return;
        if (!GameState.IsInGame(m)) return;

        Scale(m, GameState.TurnRate);
    }

    /// <summary>Scale the walk speed word 0x801B2664 on the way into the walk routine.</summary>
    [PreHook("game", Address = 0x8002F9BC)]
    static void BeforeWalk(CpuContext c, IMemory m)
    {
        if (!SpeedEnabled) return;
        if (!GameState.IsInGame(m)) return;

        Scale(m, GameState.MoveSpeed);
    }

    static void Scale(IMemory m, uint addr)
    {
        int value = (int)m.ReadU32(addr);
        if (value <= 0) return;

        int scaled = Math.Clamp((int)MathF.Round(value * SpeedMultiplier), 1, 0x7FFF);
        m.WriteU32(addr, (uint)scaled);
    }

    internal static void Reset()
    {
        Invincible = false;
        InfiniteMp = false;
        SpeedEnabled = false;
        Peaceful = false;
    }
}
