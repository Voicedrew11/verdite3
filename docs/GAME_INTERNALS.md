# Game internals

The reverse-engineered game: the main loop and its stages, player state,
movement, areas, saves and the boot stub — every address and routine this project
learns, written here rather than left in the commit that found it. Verdite2's
`docs/GAME_INTERNALS.md` is the model, but none of its addresses apply.

## Status

The main loop, its frame gate and vblank handler, the player block, the card
loader and the start menu (2026-10-02, for the agent harness and frame pacing).
The rest of the player block, movement and the view, collision, damage and
death, the statistics, inventory and equipment, magic and the area change were
read from the disassembly and the RAM dumps on 2026-10-03 (static), for the
debug mod (`docs/MODS.md`); what its run measured is marked in each section.

## Area code modules

`GAME.EXE` keeps the resident area module's pointer at `0x8018FAE0` and calls
through its slots: `func_80044D9C` calls slot 8 (`+0x20`), the first call that
reached one. A module is loaded at `0x801E8308` from `CD/COM/FDAT.T` entry
`3n+2`, and the pointer is the base plus 4 (past a count word). Area n's data are
entries `3n` and `3n+1`. See "GAME.EXE loads code" in `docs/RECOMPILATION.md`.

### Changing area

The area descriptor is five bytes: the pending/target copy at
`0x8018FAE4..FAE8` and the loaded copy at `0x8018FAD8..FADC`, byte 0 the area
index. `0x8018FAD4` is the pending-change flag (nonzero means a load is in
flight) and `0x8018FAD6` the load sub-state. `func_80018358`, **main-loop stage
8**, is the loader: it runs the CD reads of FDAT entries `3n` (area data),
`3n+1` (the module) and `3n+2` (the resident code), and clears `0x8018FAD4`
when it completes. `func_80017C78(a0..a7)` is the game's own warp primitive:
five descriptor bytes (0xFF keeps the pending one) and three entrance-offset
bytes (0x7F none), then the release and the visited-area bookkeeping that set
`0x8018FAD4` to 1. The doors are object kinds 0xE0 (the common transition,
`func_80047010` at `0x8004A7F8`, which calls `func_80017C78` with the
destination from the object record) and 0xEB (which also writes the player block
itself); both run inside stage 3 `func_80047010`. A mod issues
`func_80017C78(N, N, N, 0xFF, 0xFF, 0x7F, 0x7F, 0x7F)` from a hook on stage 3,
waits for `0x8018FAD4` to clear, then writes the position.

**Measured 2026-10-03** (the debug mod, seven warps from `fdat02`): the call
loads any area and the loop carries on in it. It does not place the player:
with entrance bytes 0x7F the X/Z carry over, and since areas do not share a
coordinate frame they can sit over nothing. A tile with neither half's mesh
(both 255) answers the floor query with -44800; the player hangs there and the
first step onto a real tile is a fall (16 HP in `fdat32`, fatal in `fdat62`).
`func_8002B760` is the game's own placement on entry (the doors at `0x8004A244`
and `0x8004A77C`, the respawn `func_80029188` and the session start call it):
no arguments, it snaps Y to the floor under X/Z through `func_80033B8C`, clears
the fall state, the action byte and the play-tick counter `0x801B2580`. A warp
to `fdat08` was sent straight back to `fdat02`, which looks like an exit under
the carried position; not followed up.

## Saves

The save is one 3-block file, `BASLUS-002551`, on card A. Its title block reads
`KING'S FIELD 2-1 EXP 0 LV 1` in full-width Shift-JIS (a new game's first
save). The game formats an empty card on the memory card screen.

The area index is the byte at `0x8018FAE4` (0 for `fdat02`); the loader at
`0x80018580` reads FDAT entry `3n+1` from it, and `0x8018FADD` keeps a copy.

## The session and the main loop

Read 2026-10-02, from a managed stack of the live game and the disassembly.
`func_80014B48` (GAME.EXE's `main`) initialises the libraries and calls
`func_80014BD4`, which is the whole session:

1. Clears the game's state blocks (`func_80018FBC`, memset, thirteen of them; the
   player block `0x801B24E4` is one), opens the vblank event (`func_8001A438`,
   below), initialises graphics, sound and the card.
2. Calls the start menu `func_8001FA60` (the memory card screen): -1 is a New
   Game, which plays the opening movie (`func_80061BC4`, about 30 s, not skipped
   by Start); anything else is a loaded slot, and `func_80028F90` runs.
3. Enters the area (`func_8001796C`) and runs **the main loop at
   `0x80014F24`**, fifteen calls a frame:

| # | function | what is known |
|---|---|---|
| 1 | `func_800341E8` | |
| 2 | `func_80034180` | |
| 3 | `func_80047010` | 17.7 KB |
| 4 | `func_80030FCC` | **the player's tick**: `PadRead(1)`, look, walk, gravity, the action state machine (see "The player"); it does not call the in-game menu |
| 5 | `func_80052E5C` | |
| 6 | `func_8005BC50` | |
| | (inline) | `sb 0 → 0x801B24F2` |
| 7 | `func_8005EB20` | |
| 8 | `func_80018358` | also called by the card loader's wait loop |
| 9 | `func_80061940` | |
| 10 | `func_8002B330(sp+0x18, sp+0x28)` | **fills the camera stage 15 draws with**: the player's x, y + `s16[0x801B2650]` + `s16[0x801B2654]` - `0x640`, z, and the angles at `0x801B2608` |
| 11 | `func_800156BC(sp+0x18, sp+0x28)` | |
| 12 | `func_80034300` | |
| 13 | `func_80018CD0` | also called by `func_80019538` |
| 14 | `func_80015A48` | also called by `func_80019538` |
| 15 | `func_800422B8(sp+0x18, sp+0x28)` | builds and draws the frame: the HUD's gauges from the player block, the scene, the frame swap `func_80035700`, then the frame gate `func_80019614` |

The loop repeats while the word at `0x8009C3F8` (`gp+0x1E4`) is 0; 2, 3 and 4
leave it, set the boot stub's selectors at `0x800102F0`/`0x800102F8` and return
to the stub, which loads the next executable.

**Only stage 15 writes the ordering table.** Measured with `KF3_STAGEPROBE=1`
(each stage's 8192 table words diffed across its call), in `fdat02`, 30 calls a
second each: stages 1-14 changed 0 words, stage 15 all 8191 (it clears and
fills the table). Stage 4 writes it only while the menu's loop runs inside it.
`gp` is `0x8009C214` throughout GAME.EXE.

### The frame swap and the frame gate

`func_80035700` is the swap: `DrawSync(0)`, `VSync(0)`, `PutDispEnv` and
`PutDrawEnv` for buffer `*(u8*)0x801AEAE8`, then `DrawOTag(*(u32*)0x801A9174 +
0x7FFC)` (the table is drawn from its last entry).

**`func_80019614` is the frame gate**: while the u32 at `0x801C12EC` is below
**4**, `VSync(0)`; then it writes 0. So the game asks for at most **15 frames a
second**, and since every stage runs once a frame, 15 Hz is also its world's
speed (a ceiling, as Verdite2's literal 2 is: a heavier frame is slower).

The count is bumped by **`func_80019570`, the vblank event handler**, which
`func_8001A438` registers: `OpenEvent(0xF2000003 RCntCNT3, EvSpINT, 0x1000,
func_80019570)`, `EnableEvent`, `SetRCnt`, `StartRCnt`. Each call increments
`0x801C12EC` (the gate's count), `0x801C12E8` (vblanks in all) and `0x801C12F0`,
and every 3600th vblank increments the u32 at `0x801B2588`, a play-time minute.

**The port ran the handler 120 times a second until fork commit `2013e51`**
(measured, `0x801C12E8` advancing 119.9997/s): `LibEtc.TickVBlank` delivered
`0xF2000003` and then raised IRQ 0, whose `ServiceIrq` delivered it again. The
gate then passed every two vblanks, and without frame pacing the main loop ran at
30.0 a second, twice the game's 15 (holding Left turned 1200 units of yaw a
second), and the play-time minute ran twice as fast. Since `2013e51` (an
amendment of `0021`) it is delivered once: 60.0 a second, and the game's own
gate holds the loop to 15 (600 units a second).

## The player

The block at `0x801B24E4` (cleared, `0x67` words, when a session starts). Found
by matching a save's payload against RAM, walking and turning while diffing
RAM, and from the code that reads it: the save-title filler `func_80028B70`
reads EXP and level, and `func_8001B254` copies max HP into HP (a full heal).
The table is the whole block `0x801B24E4..0x801B2680`, read 2026-10-03 from the
disassembly and the five `scratch/re/ram` dumps of a level-1 character standing
in `fdat02` (static, not measured in a running game). Where the reports
disagreed, the field is given as the code and the status screen show it.

| address | type | what |
|---|---|---|
| `0x801B24E4` | u32 | EXP |
| `0x801B24E8` | u32 | EXP for the next level (50 at level 1) |
| `0x801B24EC` | s32 | EXP checkpoint, -1 unset; an event fires every +13000 EXP |
| `0x801B24F0` | u8 | level |
| `0x801B24F1` | u8 | unknown, saved |
| `0x801B24F2` | u8 | UI/flag byte, cleared every frame |
| `0x801B24F3` | u8 | cast/attack animation gate (0xA while kicking or channelling); saved |
| `0x801B24F4` | u8 | cast path secondary state; saved |
| `0x801B24F5` | u8 | unknown, condition-related; read by `func_8002FE1C` |
| `0x801B24F6` | s16 | speed percent: walk and turn rates multiplied by `(0x1000 + v)/0x1000` |
| `0x801B24F8` | u16 | 0x1000 at spawn; saved |
| `0x801B24FA` | u16 | max HP |
| `0x801B24FC` | u16 | HP |
| `0x801B24FE` | u16 | max MP |
| `0x801B2500` | u16 | MP |
| `0x801B2502` | u16 | spell charge gauge, 0..0x1388 (5000) |
| `0x801B2504` | u16 | previous frame's spell charge |
| `0x801B2506` | u16 | cast lock/cooldown (0x1388 while a cast resolves) |
| `0x801B2508` | u16 | steps counter; every 100 steps base stat 0 +1, and the condition tick |
| `0x801B250A` | u16 | action-use counter for base stat 1 |
| `0x801B250C` | u16 | action-use counter for base stat 2 |
| `0x801B250E` | u16 | action-use counter for base stat 3 |
| `0x801B2510` | u16 | action-use counter for base stat 4 |
| `0x801B2512` | u16 | action-use counter for base stat 5 |
| `0x801B2514` | u16 | unknown, saved |
| `0x801B2516` | u16 | base stat 0 (20 at level 1; the level table and the steps grow it) |
| `0x801B2518` | u16 | base stat 1 (0 at level 1) |
| `0x801B251A` | u16 | base stat 2 |
| `0x801B251C` | u16 | base stat 3 |
| `0x801B251E` | u16 | base stat 4 |
| `0x801B2520` | u16 | base stat 5 (10 at level 1) |
| `0x801B2522` | u16 | unknown, saved |
| `0x801B2524` | u16 | adjusted stat 0 (= `0x2516`; halved by a curse; the magic-attack base) |
| `0x801B2526` | u16 | adjusted stat 1 |
| `0x801B2528` | u16 | adjusted stat 2 |
| `0x801B252A` | u16 | adjusted stat 3 |
| `0x801B252C` | u16 | adjusted stat 4 |
| `0x801B252E` | u16 | adjusted stat 5 |
| `0x801B2530` | u16 | unknown stat, drawn as value + 1; saved |
| `0x801B2534` | u32 | GOLD |
| `0x801B2538`..`0x801B2546` | 8 x u16 | OFFENSE 1..8 |
| `0x801B2548` | u16 | gap, never written by the recompute |
| `0x801B254A`..`0x801B255A` | 9 x u16 | DEFENSE 1..9 |
| `0x801B255C` | s16 | POISON condition/timer: every 15 ticks HP -1, then it counts down |
| `0x801B255E` | s16 | CURSE condition flag (halves the adjusted stats) |
| `0x801B2560` | s16 | curse pair (magnitude), cleared with `0x255E` |
| `0x801B2562` | s16 | DARK condition flag |
| `0x801B2564` | s16 | dark pair, cleared with `0x2562` |
| `0x801B2566` | s16 | SLOW condition; stage 4 halves the turn rate while set |
| `0x801B2568` | s16 | PARALYZE condition; stage 4 damps movement while set |
| `0x801B256A` | s16 | timed stat-effect timer; expiry calls `func_80029500` |
| `0x801B256C` | s16 | timed stat-effect timer |
| `0x801B256E` | s16 | timed stat-effect timer |
| `0x801B2570` | s16 | condition state; read by `func_80030E14`; saved |
| `0x801B2572` | s16 | MP-drain tick timer (0x384) |
| `0x801B2574` | s16 | condition state; read by `func_80044B40`; saved |
| `0x801B2576` | s16 | MP-restore timer; while set, stage 4 writes MP = max MP |
| `0x801B2578` | s16 | timed-effect timer; expiry calls `func_80029500` |
| `0x801B2580` | u32 | play-tick counter, incremented once at the end of stage 4 |
| `0x801B2588` | u32 | play time, minutes (from the vblank handler) |
| `0x801B2590` | u32 ptr | current spell record (`0x801B77EC + id*0x18`), set at a cast |
| `0x801B2594` | u32 ptr | current weapon record (`0x801D37A4 + id*0x44`), set on equip |
| `0x801B259C` | u32 | the first-person arm's swing blender slot |
| `0x801B25A0` | u32 | the bow's nocked arrow projectile, set by `func_80053C84` at the nock, cleared when the arrow count is 0 |
| `0x801B25A4` | s16 | arm swing/cast clock, -1 idle, stepped during a swing |
| `0x801B25A6` | s16 | arm swing window |
| `0x801B25A8` | s16 | arm swing window |
| `0x801B25AA` | u8 | arm/spell runtime state; read by `func_8002FE1C` |
| `0x801B25AB` | u8 | charging spell id |
| `0x801B25AC` | u8 | committed spell id |
| `0x801B25AD` | u8 | committed spell id (second slot) |
| `0x801B25AE` | u8 | arm clip byte / cast phase; for a bow 0 drawn, 1 loosed |
| `0x801B25AF` | u8 | equipped weapon id and current arm-effect id (0xFF none); 27 and 28 are the bows, LARGE BOW and ELCHRIS BOW (names at `0x8007F620`) |
| `0x801B25B2` | u8 | queued-cast counter |
| `0x801B25B3` | u8 | cast-ready latch |
| `0x801B25B4` | u8 | flag: stage 4 halves the walk and turn rates while nonzero |
| `0x801B25B8`..`0x801B25D0` | 7 x u32 ptr | resolved equipment records (`0x801E6078 + id*0x20`) |
| `0x801B25D4`..`0x801B25DA` | 7 x u8 | equipment slots: helm, armor, gauntlets, boots, shield, ring, ring (0xFF empty) |
| `0x801B25DB`..`0x801B25E4` | 10 x u8 | other owned/equipped item ids |
| `0x801B25E0` | u8 | moving/on-ground flag (enables the bob) |
| `0x801B25E5` | u8 | player action state (jump table `0x80011AC0`; 0x11 dead) |
| `0x801B25E8` | u8 | vertical state: 0 ground, 0x10 fall, 0x20, 0x40, 0x50 |
| `0x801B25E9` | u8 | timed-status id (0xFF idle) |
| `0x801B25EA`/`EB` | u8 | condition flags set by `func_8002DEEC` |
| `0x801B25ED` | u8 | falling-fast flag |
| `0x801B25F0` | s32 | position x |
| `0x801B25F4` | s32 | position y, height (more negative is up; -12800 standing in `fdat02`) |
| `0x801B25F8` | s32 | position z |
| `0x801B25FC` | s32 | fourth saved position word (0 in `fdat02`) |
| `0x801B2600` | s16 | accepted X move this tick |
| `0x801B2602` | s16 | accepted Y move this tick |
| `0x801B2604` | s16 | accepted Z move this tick |
| `0x801B2608` | s16 | view/composed pitch, to the camera |
| `0x801B260A` | s16 | view/composed yaw (heading), to the camera |
| `0x801B260C` | s16 | view/composed roll, to the camera |
| `0x801B2610` | s16 | base pitch |
| `0x801B2612` | s16 | base yaw (0x1000 a turn) |
| `0x801B2614` | s16 | base roll |
| `0x801B2618` | s16 | pitch delta A |
| `0x801B261A` | s16 | yaw delta A |
| `0x801B261C` | s16 | roll delta A |
| `0x801B261E` | s16 | death-sequence timer, +1 a frame in the state-17 handler |
| `0x801B2620` | s16 | pitch delta B |
| `0x801B2622` | s16 | yaw delta B |
| `0x801B2624` | s16 | roll delta B |
| `0x801B2628` | s16 | pitch delta C |
| `0x801B262A` | s16 | yaw delta C |
| `0x801B262C` | s16 | roll delta C |
| `0x801B2630` | s16 | knockback/displacement velocity x |
| `0x801B2632` | s16 | knockback/displacement velocity z |
| `0x801B2634` | s16 | knockback/displacement velocity y |
| `0x801B2638` | s32 | derived depth cache (the eye against a reference plane) |
| `0x801B263C` | s32 | derived depth cache |
| `0x801B2640` | s32 | derived depth cache |
| `0x801B2644` | s16 | surface half id of the tile stood on (0 lower, 5 upper) |
| `0x801B2646` | s16 | strafe velocity (R1/L1) |
| `0x801B2648` | s16 | forward velocity (Up/Down) |
| `0x801B264A` | s16 | applied walk speed, normalised |
| `0x801B264C` | s16 | yaw turn velocity (Left/Right) |
| `0x801B264E` | s16 | pitch velocity (R2/L2) |
| `0x801B2650` | s16 | walk bob vertical offset (the camera adds it) |
| `0x801B2652` | s16 | bob phase accumulator |
| `0x801B2654` | s16 | landing-dip offset (the camera adds it) |
| `0x801B2656` | s16 | fall velocity (gravity) |
| `0x801B2658` | s16 | camera-shake/hurt countdown (0xDAC on a hit) |
| `0x801B265A` | s16 | camera-shake step / hurt intensity |
| `0x801B265C` | u16 | pad word, current frame (active HIGH, byte-swapped) |
| `0x801B265E` | u16 | pad word, previous frame |
| `0x801B2664` | s32 | walk max speed (200 / 0xC8), re-derived every frame |
| `0x801B2668` | s32 | turn max rate (40 standing / 32 moving) |
| `0x801B266C` | s16 | root-motion delta x |
| `0x801B266E` | s16 | root-motion delta y |
| `0x801B2670` | s16 | root-motion delta z |
| `0x801B2674` | s16 | death-slide horizontal offset |

A New Game starts at `[126976, -15360, 16384]` heading 0 in `fdat02`, with HP
50/50, MP 30/30, level 1 and gold 0. The camera's copy of the position is at
`0x801AEC4C` and of the angles at `0x801AEC5C`.

### Movement and the view

Stage 4 `func_80030FCC` (the main-loop call at `0x80014F3C`) is the player's
tick, not the menu. It calls `PadRead(1)` at `0x80031120` and stores the pad word
in `0x801B265C`, re-derives the walk and turn maxima `0x801B2664`/`0x801B2668`
every frame, and dispatches on the action byte `0x801B25E5` through the jump
table at `0x80011AC0` (19 entries; 0 normal). The normal state runs, in order,
`func_8002FE1C` (buttons, action, poison/regeneration and cast input),
`func_8002F5C0` (look), `func_8002F9BC` (walk) and `func_8002ED60` (gravity),
then the pose tail and the composed-angle block at `0x8003202C`. It runs before
stage 10 `func_8002B330`, which copies the player into the camera arguments, so a
write at the end of stage 4 reaches the same frame.

Base angles are pitch `0x801B2610`, yaw `0x801B2612` and roll `0x801B2614`
(s16, 12-bit, 0x1000 a turn). The view triple the camera reads is the base plus
three deltas each: pitch `0x801B2608` = `0x2610 + 0x2618 + 0x2620 + 0x2628`, yaw
`0x801B260A` = `0x2612 + 0x261A + 0x2622 + 0x262A`, roll `0x801B260C` =
`0x2614 + 0x261C + 0x2624 + 0x262C`. The look routine `func_8002F5C0` clamps the
pitch to ±0x2BC (700) with the wrap-aware test `func_80016A78`; the walk routine
`func_8002F9BC` integrates the strafe and forward velocities through
`func_8002E3F8`, and gravity `func_8002ED60` integrates the fall velocity
`0x801B2656` through the vertical state `0x801B25E8` and computes the bob
`0x801B2650` and the landing dip `0x801B2654` the camera adds. The eye is 0x640
(1600) above the feet; standing Y in `fdat02` is -12800.

The pad word `0x801B265C` is active HIGH and is the standard PSX word with its
two bytes swapped (Up 0x1000, Right 0x2000, Down 0x4000, Left 0x8000, Cross
0x0040, ...); `0x801B265E` is the previous frame's copy. A mod hooks stage 4 to
write the position each frame, and pre-hooks `func_8002F9BC`/`func_8002F5C0` to
scale `0x801B2664`/`0x801B2668` for a speed multiplier.

### Collision

The master test is `func_80033F38(x, y, z, radius, height, flags)`, which
returns a bitmask, 0 when the body fits. `flags` bit 0x01 is the map and walls
(`func_80033D08`), 0x10 and 0x40 the creatures (`func_8004D644`, `func_8004D838`),
0x20 the objects (`func_80045AC8`) and 0x80 a map/event query (`func_80028E48`).
The player calls it with radius 0x320, height 0x6A4 and flags 0x31; creature and
object AI call the same routines from about thirty sites, so none of them may be
disabled. The map side is the tile lookup `func_800324F0` and the
cylinder-vs-map test `func_8003260C`; `func_80033B8C` chooses the lower half
(mode 1) or the upper (mode 2) and writes its floor `-(h << 7)` to `0x801E6474`.
The map confirms it: the standing tile's upper height byte 0x64 gives -12800,
the standing Y.

Three routines commit a player move, all called only by stage 4:
`func_8002E3F8(angle, distance)` tests the horizontal step and writes X and Z,
latching the surface half id `0x801B2644` from `0x801E646E`; `func_8002ED60`
writes Y (gravity, landing and floor clamp); and `func_8002F320` applies the
root-motion delta `0x801B266C/6E/70` and writes X, Y and Z. The explicit floor
snap on area or event entry is `func_8002B760`. A mod hooks stage 4 and
overwrites the committed position rather than touching the shared queries.

### Creatures wake and sleep by distance

Read 2026-10-06 from stage 5 and checked against the 28-area RAM corpus. **A
creature is ticked and drawn only while it is awake, and it wakes and sleeps by its
horizontal distance from the player.**

- **Stage 5 `func_80052E5C`** walks all 200 creature records (`0x80185DA8`, `0x88`
  apart; `u8[+0] == 0xFF` is an empty slot). Per record: `func_8004DA2C` points the
  globals at it (`0x8018FAB4` the record, `0x8018FAB0` its definition, `0x8018FAC4`
  its type), then **`func_8004C1F0`, the waker**, but only on one tick in four
  (`(0x8018FACC & 3) == (index & 3)`), or every tick while `0x801B24F2` is set or the
  player's action byte `0x801B25E5` is 1. Then, if the state byte `u8[+9]` is 1, its
  tick `func_800500A8` (and `func_8004C01C` one tick in four, staggered by the count
  of awake ones at `0x8018FAC8`).
- **The state byte `u8[+9]`**: 0 asleep, 1 awake (the AI runs, and the model walk
  draws it: its live test is this byte), 2 asleep and not to wake until the player
  has gone away again.
- **The distances are per type**, in the 120-byte definition at `0x8018C7E8 + type ·
  120` (`type = u8[+2]`): **`+0xA` the wake distance and `+0xB` the sleep distance, in
  tiles**. `func_80016EC8(pos, x, 0xFFFF, z, range)` is the test: a square
  pre-check, then the horizontal distance from `>> 3` coordinates, returned, or -1
  past `range`.
  - Asleep (0): if the player is within `(+0xA + 1)` tiles, the kind `u8[+0]`
    decides. Kinds 3 and 4 follow a leader record (`s16[+0x22]`) and wake when it
    is awake. Kind 2 wakes on a random draw against the record's `u8[+0xA]` (0xFF:
    always). Any other kind: **nearer than `+0xA` tiles (and `0x801B24F2` clear) it
    goes to 2 instead of waking**; between `+0xA` and `+0xA + 1` kind 1 wakes, kind 0
    wakes on a random draw against the record's `u8[+0xA]` and goes to 2 when it
    fails, and the rest go to 2. Waking needs the spot free of other creatures
    (`func_8004D644`), or it goes to 2.
  - Awake (1): past `+0xB` tiles it sleeps (0) and is reset (`func_8004B560`).
  - Waiting (2): past `+0xB` tiles it goes back to 0.
- **Values**: every type in the corpus is **16/17** but one in area 17 (18/19)
  and the single creature of areas 25 and 27 (48/50). So **creatures exist only
  within about 17 tiles of the player and appear 16 to 17 tiles away**: past the
  draw radius (13, or 9 in areas 3 and 12-27), which is why the game never shows one
  appearing. With the render distance past 16 tiles they would; `RenderDistance`
  fades a creature out at its own `+0xA` (see "Render distance" in `WIDESCREEN.md`).

### The creature AI is a rule table scored on one number

Read 2026-10-08 from `GAME.EXE`; it is Verdite2's think (`func_8003A3FC`,
`func_8003A300`, `func_80039E40`) under other addresses, and the same one-register
change makes enemies ignore the player.

- **The think `func_8004C01C`**, run by stage 5 for an awake creature one tick in
  four: `func_80016C08(rec+0x2C − playerX 0x801B25F0, rec+0x34 − playerZ
  0x801B25F8)`, the horizontal distance, handed straight to the picker. The
  behaviours ask for a fresh pick through `func_8004C104` (which sets `u8[+0xF]` to
  `0xFF`, forcing the reinstall, and calls the think); about thirty call sites.
- **The picker `func_8004BF1C(dist)`**: returns at once when `u8[+0xF]` is `0xF0` or
  0; otherwise scores the sixteen rule pointers at definition `+0x38` with
  `func_8004B984` and installs the best (strictly greater than the running best,
  which starts at -2, so a table whose rules all score 0 keeps its first) through
  `func_8004B94C`, unless it is the current rule (`rec+0x60`) and `u8[+0xF]` is not
  `0xFF`. The waker `func_8004C1F0` calls it too, with the distance it measured, after
  installing a wake rule (ids `0x15`, `0x06`, `0x1A` looked up by `func_8004C068`).
- **The scorer `func_8004B984(rule, dist)`** switches on the rule's type byte
  `u8[+0]` through the jump table at `0x800124D0` (types below `0x85`; higher ones
  call the per-type function at `[0x8018FAE0]+0x40`). Every distance gate is a `u16`
  of the rule (`+0xC`, `+0x10`, `+0x12`, `+0x14`, `+0x16`, `+0x1A`) compared
  `(int)range < (int)dist`, and a passing rule scores `func_800170C0(u8[+2])`
  (`u8[+3]` when it is the current rule; not read). In `fdat17`'s tables, read
  rather than watched: type `0x00` passes when the player is **farther** than
  `+0xC` (the far behaviour), `0x05` when nearer than `+0x12` (20000-30000; read as
  the approach), `0x04` within `+0x1A` (3300-5800) and inside a cone of the
  creature's facing `rec+0x42` (read as the melee), and `0x19` in a
  `+0x14`..`+0x16` band and a cone.

**So a distance above 65535 fails every "near" rule and passes every "far" one.**
`mods/kf3debug`'s Enemies ignore you pre-hooks the picker and writes `0x20000` into
`a0`; nothing on the waking path (`u8[+9]`, which the model walk draws on) is
touched. Measured 2026-10-08 in `fdat17` with the player held 5000 units from
creature 14 (type 5) for 30 s: switched off, the creature went back and forth
between its `0x00` and `0x05` rules every 2 s; switched on, it took `0x00` and kept
it. Every type in that area has a `0x00` rule. Putting the player 3000 units from
the same creature with a poke hung the game thread (100% CPU, no reply), with the
switch off; that is the teleport, not the switch, and the routine it spins in was
not read.

### Damage and death

`func_8002A6F4(sourcePos, amount, flags)` is the take-damage routine. It returns
at once while the action byte `0x801B25E5` is 0x11 (dead) or the amount is 0;
otherwise it subtracts from HP `0x801B24FC`, clamps at 0, computes the hurt
fraction `(amount << 12) / maxHP`, writes the camera-shake/hurt countdown
`0x801B2658` (0xDAC) and the intensity `0x801B265A`, pushes the player with the
knockback velocity `0x801B2630/2632/2634`, and on HP 0 calls the death latch.

The HP adders are `func_8002A6A0` (signed, no max clamp, calls the latch at ≤ 0:
the poison and starvation tick from stage 4), `func_80030BE0` (signed, clamps to
[0, maxHP]: the equipment regeneration +1 and drain -1) and `func_80030C6C` (the
MP twin). The heals run through `func_8001B254(type)`.

`func_80030A6C(posPtr)` is the death latch and the only writer of the dead state:
if the action byte is not already 0x11 it sets it to 0x11, plays sound 0x6E,
copies 8 bytes of `posPtr` into `0x801B266C` and clears the death timer
`0x801B261E` and the slide `0x801B2674`. The state-17 handler in stage 4 then
forces HP to 0 every frame. A mod blocks `func_80030A6C` and zeroes the negative
amounts into `func_8002A6F4`, `func_8002A6A0` and `func_80030BE0`.

**Measured 2026-10-03**: `func_8002A6F4(0, 10, 0)` called from a hook took HP
50 to 40; `func_80030A6C(0)` set the action byte to 0x11 and the death timer
counted (0x20 a moment later). With the mod's hooks on, the same calls left HP
at 50 and the byte at 0.

### The character's statistics

Two status screens read this block. Page 1 `func_800227EC` draws EXP, level,
HP and MP, the six adjusted stats and GOLD, and chooses the condition name from
`0x801B255C` (POISON), `0x801B255E` (CURSE), `0x801B2562` (DARK), `0x801B2566`
(SLOW) and `0x801B2568` (PARALYZE), checked in that order; page 2 `func_80023300`
draws the eight OFFENSE and nine DEFENSE ratings. Their labels are the item
font's glyph-index alphabet, A = 0x00 .. Z = 0x19 (CONDITION at `0x8007F3B0` is
`02 0e 0d 03 08 13 08 0e 0d`). That and `func_8002AB18`, which arms each
condition, settle the condition fields above.

The names on the status screens are pictures in `GAME.EXE`, not text, so the
labels were read off the screen by the user (2026-10-08) and the drawing calls
off the recompiled code. Page 1's frame is `func_80026ACC(155, 26, 141, 180)`.
Stat 0 `0x801B2524` is PWR, drawn at (233, 104), and `0x801B2530` (+1) is WIS
at (261, 104), beside it as "PWR·WIS 47·1". The five MAG words are drawn as
icons: `0x801B252E` HOLY (the sun) at (198, 121), `0x801B2526` FIRE at (247,
121), `0x801B2528` EARTH at (198, 136), `0x801B252C` WIND at (247, 136) and
`0x801B252A` WATER at (198, 151).

Page 2 `func_80023300` draws in the frame `func_80026ACC(20, 26, 276, 180)`.
OFFENSE is at (30, 34), eight rows from Y 50 at X 30, 16 apart: SLASH `0x2538`,
BLOW `0x253A`, STAB `0x253C`, HOLY MAGIC `0x253E`, FIRE MAGIC `0x2540`, EARTH
MAGIC `0x2542`, WIND MAGIC `0x2544`, WATER MAGIC `0x2546`. DEFENSE is at (164,
34), nine rows from Y 51, 17 apart: SLASH `0x254A`, BLOW `0x254C`, STAB `0x254E`,
POISON `0x2550`, DARK MAGIC `0x2552`, FIRE MAGIC `0x2554`, EARTH MAGIC `0x2556`,
WIND MAGIC `0x2558`, WATER MAGIC `0x255A`. Each label is drawn by
`func_800261DC(0x8007E570, record)`; each number is formatted by
`func_800277C0(value, 6, 0, 0, buffer)` into `[sp+0x10]` and drawn by
`func_8002636C(0x8007E564, record)` at the label's X + 77. The frame is drawn
last.

`func_80029500` is the recompute every equip, level-up, load and condition
change calls. It copies the six base stats `0x801B2516..2520` into the adjusted
`0x801B2524..252E`, halves them while the curse flag `0x801B255E` is set, zeroes
the 17 ratings `0x801B2538..255A`, and sums in the equipped weapon
(`0x801B25AF`, record `0x801D37A4`, stride 0x44) and the accessory records
(`func_800293E4`). Anything written straight to `0x801B2524..255A` lasts only
until the next call. A mod hooks the recompute and overwrites the derived words.

The recompute reads its inputs directly, not through the cached pointers: the
weapon id `0x801B25AF` and the seven slot bytes `0x801B25D4..25DA`, the weapon
record `0x801D37A4 + id × 0x44` (+6 to +0x14), and, through `func_800293E4(id)`,
the armour record `0x801E6078 + id × 0x20` (the raw id; its +2 to +0x12 are nine
u16 bonuses) added to the DEFENSE words `0x254A, 254C, 254E, 255A, 2550, 2552,
2554, 2556, 2558` in that field order. It adds fixed bonuses for particular
ids: FLAME ROD (0x21) +20 FIRE `0x2526`, GROUNDAL CROWN (0x26) +20 PWR, WIND
NECKLACE (0x59) +20 WIND `0x252C`, EVIL RING (0x52) +8 to the five MAG words and
-30 PWR. The condition words `0x801B256A`, `0x801B256E` and `0x801B2578` and an
area test (`0x801BA9E9` = 1 and `0x8018FAD8` = 0x1A: +50 to `0x252C`) add
their own. The adjusted stats are clamped to 0..999. It writes only
`0x2524..0x252E`, the 17 ratings and `0x801C12F0` (the u32 at `0x801B2584`,
when non-zero), and calls nothing but `func_800293E4` and the runtime's
interrupt poll.

The weapon setter `func_8002BDC0` does more than the recompute: it writes
`0x801B24F3` = 0xA, clears the charge `0x801B2502/2504`, writes the record
pointer `0x801B2594`, loads the weapon's model (`func_8001A154(4, id + 0x62)`)
and calls `func_80042C64`, `func_80015CE0` and others before the recompute.
`func_8002BB84` only writes the slot byte, rebuilds the seven cached pointers
and recomputes.

`func_8002A310(gain)` is EXP gain and level-up. It adds to EXP `0x801B24E4`,
caps it at 0xF423F, and while EXP ≥ `0x801B24E8` and the level is below 0xFF
increments the level `0x801B24F0` and reads the 12-byte table at `0x8009F114`
(index level - 1, 99 entries; `+0` max HP, `+2` max MP, `+4` the stat-0 growth,
`+8` EXP next). Entry 0 is max HP 50, max MP 30, stat 0 +20, EXP next 50; level
99 is max HP and MP 999, EXP next 1,000,000. The save (`func_8005F7BC` packs,
`func_8005FFD0` unpacks) carries EXP, EXP next, the checkpoint, the level, the
vitals, gold, the growth counters and base stats, the 15 condition words
`0x801B255C..2578`, the equipment ids, and the world position and angles, but
not the adjusted stats or the 17 ratings, which are rebuilt on load.

### Inventory and equipment

Inventory is a flat byte-per-id count array at `0x800C85E8` (ids 0..149, cap 99),
with a second 150-byte array at `0x800C867E` that the add path overflows into;
both are saved. `func_8005D898(id)` adds one, `func_8005D7F8(id)` removes one and
`func_8005D7BC(id)` reports held; `func_8005EA64` is the new-game init. Names are
24-byte records at `0x8007F620`, id `i` at `+ i*0x18`, in the font encoding of
the labels (0x00..0x19 = A..Z, 0x7F space, 0xFF terminator). Weapons 0..33 use
the table at `0x801D37A4` (stride 0x44; ids 27 and 28 are the LARGE BOW and the
ELCHRIS BOW, read off the name table at `0x8007F620`); armor and accessories 34..94 use
`0x801E6078` (stride 0x20: a category byte, a model byte and nine u16 bonuses).
The seven equipment slots are bytes at `0x801B25D4..25DA` (0xFF empty): helm
`D4`, armor `D5`, shield `D6`, gauntlets `D7`, boots `D8`, two rings `D9`/`DA`.
`func_8002BB84(id, slot)` sets them through a jump table at `0x800117C8` whose
slot index is not the address order: 0..6 land on `D4`, `D5`, `D7`, `D8`, `D6`,
`D9`, `DA`. The weapon `0x801B25AF` is set by `func_8002BDC0(id)`; both setters
call the recompute and cache the record pointers at `0x801B25B8..25D0`.
**Measured 2026-10-03**: `func_8005D898` and `func_8005D7F8` called from a hook
changed the counts as read (ids 3, 43, 104 up, 105 down), slot 1 with id 43 (IRON
PLATE) wrote `0x801B25D5`, and the names decode as the game's items (EXCELLECTOR
three times for ids 0..2, the sword's tiers). Which of `D6`/`D7` is the shield is
the static reading. A mod adds through `func_8005D898` (or writes the
count directly to exceed the cap) and equips through the two setters.

### Magic

Spells are the 0x18-byte records at `0x801B77EC` (`+0` the known count, `+1` the
effect id, `+0x16` the MP cost); a new game zeroes records 0..0x5E. The selected
spells are `0x801B25AB` (charging), `0x801B25AC` and `0x801B25AD` (committed), and
the record of a cast in flight is pointed to by `0x801B2590`. `func_8002D130(id,
release, multiplier)` is the cast: it refuses when MP `0x801B2500` is below the
record's `+0x16` cost, points `0x801B2590` at the record and, on the release
pass, subtracts the cost; `func_8002DEEC(id)` is the id-driven alternate and
`func_8002FE1C` is the pad dispatcher. The charge gauge is `0x801B2502`
(0..5000) with the cooldown `0x801B2506`.

**The bow** is `func_8002D2A0`'s branch for weapons 27 and 28 (LARGE BOW, ELCHRIS
BOW); weapon ids below 27 take the other weapons' path. The clip byte `u8 0x801B25AE`
picks the phase: 0 drawn, 1 loosed. A press (`func_8002C040`, called from
`func_8002FE1C` with the clock at -1) sets the clip byte to 0 and the clock to 0.
The next tick with the clock at 0 nocks: the arrow id is `0x93` (ARROWS) for 27 and
`0x94` (LIGHT ARROWS) for 28, its count at `0x800C85E8 + id` goes down, and
`func_80053C84` spawns the projectile, kept at `0x801B25A0` (cleared if the count
is 0). Each tick of the draw adds the weapon record's `+0x1C` to the clock, clamped
at `0x0FFF` (full draw). While the attack mask (`u16 0x80081870`) is set in the pad
word `0x801B265C` the clock holds. Released, the clip byte becomes 1 and the clock 0
(the loose), then the clock steps `+0x190` a tick until 4095 or more, when it is set
to -1 and the charge `0x801B2502` cleared. So the bow does not take the sword
swing's `0x180` a tick to `0x0F80`.

The magic menu's book is records 0..30, named by item ids 150..180 in the name
table (FIRE BALL, FIRE WALL, ...); six of them (153, 160, 164, 168, 172, 174)
are the unused `00 FF` placeholder. **Measured 2026-10-03**: a level-1 save
knows only LIGHT (record 29, 5 MP), and writing `+0` = 1 into the others marks
them known; whether the menu then lists and casts them is to be judged in play.

Regeneration runs in stage 4: `func_80030CBC(rec)` and `func_80030D68(rec)` call
`func_80030BE0(±1)` and `func_80030C6C(±1)` when the play-tick counter
`0x801B2580` divides the equipment record's interval fields; the flat "quarter of
max MP" arm is in `func_8002AB18`. A mod restores MP at the end of stage 4, or
forces it before the cost is subtracted; it should not freeze `0x801B2500`, since
the item heals and the full restores share it.

## Saves and the start menu

- **The loader is `func_8002860C(slot)`**: it opens `bu00:BASLUS-00255<slot>`
  (the name at `0x80081818`, the digit `'0' + slot`), reads `0x6000` bytes into
  the buffer at `*(gp+0x1B4)`, checks the sum of `0x5C00` bytes from `+0x400`
  (`func_80028C74`) against the word at `+0x200`, unpacks the state
  (`func_8005FFD0`) and writes the slot to `0x8009C2C0` (`gp+0xAC`). It returns
  0, 1 (no file) or 2 (bad sum), and retries three times.
- **The saver** (`0x800288C0..`) fills the title from the template at
  `0x80081828` (`func_80028B70`: the slot digit at `+0x23`, EXP and level in
  full-width digits), packs the state (`func_8005F7BC`) and writes the same sum.
- **The start menu `func_8001FA60`** checks the card (`func_80028034`,
  `func_800280D4`, nine tries), reads its directory (`func_800281C4`), and only
  if the byte at **`0x800102FA`** is 1 — the title's "load" choice, written by
  OPEN.EXE into the boot stub's data — calls the slot chooser
  **`func_8001FC5C`**, which calls the loader and returns the slot. Otherwise it
  returns -1, a New Game.
- In game, the menu (Cross) has Save and Load (`func_8001E710`, which calls the
  loader at `0x8001E968`). Driving it from a script with Cross saves over the
  slot: see "Driving the game without a person" in `docs/DEVELOPMENT.md`.
- **Load is a card session around the loader, then a short arm.** `func_8001E710`
  opens the session `func_80028034` (which allocates the `0x6000`-byte buffer at
  `gp+0x1B4`), calls the loader, closes it with `func_80028090`, and returns the
  slot or 0. The menu dispatcher `func_8001A774` turns a load into -3, and its
  caller's -3 arm (`0x80030740`) is: `func_80028F90()` (the post-load fixup: the
  equipment and spells re-applied; `func_80028E9C` zeroes the view tilt
  `0x801B2618` and the death camera's sink `0x801B2650`), then
  `func_80029188(a, a, a, u8[0x8018FADB], u8[0x8018FADC], 0xFF)` with `a =
  u8[0x8018FAD8]`, then `func_8002B6F0()`. `func_80029188` is the respawn: it
  runs the area load itself (stages 8 and the CD loader in a loop), and its
  placement `func_8002B760` clears the action byte.

### A full card: the title's Continue and the in-game Save

**Both executables read a full card as unusable** (OPEN.EXE read 2026-10-05,
GAME.EXE 2026-10-05). The card check (OPEN.EXE `func_80014174`, GAME.EXE
`func_800280D4`, copies of one routine) waits for the card's event
(`func_80028C98`: 0 done, 1 error, 2 timeout, 3 new card), then proves it
writable by opening `bu00:BASLUS-00255TEMP` with create (the name at
`0x8001108C` in OPEN.EXE, mode `0x200`) and deleting it; it returns 2 when the
create fails. A save is 3 blocks of the card's 15, so **five saves fill it**,
and from then on every check returns 2. The runtime's card
(`MemoryCard.Create`) refuses a create with no free block, the check and its
callers are the game's own code. Not checked: whether a real BIOS refuses this
create too (it asks for 0 blocks, mode `0x200`); the original disc with a real
BIOS and five saves on the card would settle it. The callers:

- **The title** (`0x80011FDC`) takes any non-zero answer as no card, skips the
  directory read `func_80014264`, leaves the save count at 0, and the menu
  `func_800131AC(&choice, count)` has no Continue.
- **GAME.EXE's start menu** `func_8001FA60` treats 2 as 0 and goes on to the
  directory.
- **The in-game Load list** `func_8002008C` retries the check ten times, then
  reads the directory anyway, which is why Load works on a full card.
- **The in-game Save** `func_800203C8(slot)` runs the check once: 0 saves
  (`func_80028750`), 1 shows the no-card message (`func_8002098C`), 2 asks
  `func_80020560`, the format prompt, and on yes formats the card
  (`func_800285DC`, `format("bu00:")`) before saving. So on a full card **no
  save can be written**, and the only way forward the game offers erases all
  five saves. Measured: card A with five saves, menu opened with Cross, the save
  chosen: one TEMP open/close/erase and the game waiting at the prompt, the card
  unchanged.

The saver `func_80028750` reads the directory and only creates a file for a slot
that has none (`open(name, 0x30200)`, 3 blocks); a slot that exists is opened
for write (mode 2), written (`0x6000` bytes), read back and summed. So saving
over a slot needs no free block; a new slot on a full card fails its own create
and the game reports the save as failed.

`patches/FullCard.cs` posts on both checks and turns 2 into 0, so the directory
decides. **Measured**: card A with five saves, Start pressed through OPEN.EXE:
the title preselected Continue and `0x800102FA` read 1 in GAME.EXE; with
`KF3_FULLCARD=0`, 0 (a New Game). In game, slot 1 loaded, Cross four times:
slot 1's first block rewritten, the other four saves and the directory
untouched, and slot 1 then loaded back through the game's sum check
(`KF3_AUTOSTART=1`: fdat17, HP 108/134, LV 12).

## Menus and full-screen messages

Every menu runs on one framework, Verdite2's shape: the enter `func_80027198`
(shrinks the primitive buffers to `0x7400` each and stores the displayed frame),
the frame head `func_80026FE4`, the presenter `func_800270F8` (which pastes the
stored frame every frame) and the leave `func_80027310`. A sign or a line of
dialogue is `func_800441D4(file, entry)`, a 4-bit TIM over a `MoveImage` copy of
the frame, faded by `func_80043BB8(brightness, step)`; dismissing one writes the
examine bit into the pad word. The addresses, the layout and the fade's return
values are written up in "Menus and messages draw the world live" in
[WIDESCREEN.md](WIDESCREEN.md), where `patches/MenuWorld.cs` replaces the paste
and the fade.

### The in-game menu, its lists and its font

Read 2026-10-06 off the recompiled code and `GAME.EXE`'s data, for a port
settings page drawn with the game's own menu routines; the font and the masks
were measured in play.

**The menu `func_8001A774`** (called from examine's `func_800305D8`; the menu
button is preset 3's Circle) enters the framework, then loops: the chooser
`func_800221E8(cursor, 5, &sel, &confirmed)` with `&cancel` at `sp+0x10`,
`PadRead_game(1)`, and two frames of head, `func_800227EC` (status page 1),
`func_800252F4(0, 6, cursor, 0)` (the list) and the presenter. A choice
(`sel` 0..5) goes through the jump table at `0x80011448` to the six pages:
`func_8001AA84`, `func_8001C48C`, `func_8001C7A8`, `func_8001D784`,
`func_8001DD94`, and **`func_8001E484`, SYSTEM**. `cancel` starts at -99
(`0xFFFFFF9D`); the chooser writes -1 there on the cancel button, and the menu
leaves. Its return goes to the caller as in "Saves and the start menu".

**The chooser `func_800221E8`** writes `*sel = -1` and `*confirmed = 0`, then
reads the pad through the repeat gate `func_800279D8` (when the flag
`gp+0x34` = `0x8009C248` is set it clears it and waits up to 8 `VSync(0)`s
while any button is held) and `func_800279A4` (`PadRead_game(1)`, setting the
flag on any button). Up (`0x1000`) moves the cursor back and wraps to the
count, Down (`0x4000`) forward and wraps to 0, both with sound `0xC`
(`func_8002792C`); the confirm mask `gp+0x38` = `0x8009C24C` sets
`*confirmed = 1` and `*sel = cursor` with sound `0xD`; the cancel mask
`gp+0x3C` = `0x8009C250` writes -1 to `*cancel` with sound `0xE`. It returns
the cursor. **Measured** (slot 1, fdat17, menu open and closed): the masks
read `0x0040` (Cross) and `0x0020` (Circle). No other button is tested, and
`func_80027A40` (called before a page opens) waits until the pad is released.

**L1 + R1 held in the menu** calls `func_8001A9DC`, a location readout left in
the retail game: `area × 10000 + (0x801B25F0 >> 11) × 100 + (0x801B25F8 >> 11)`
(the area byte `0x8018FADD`), six digits in the number font at (247, 210). So
L1 alone is not free in the menu. **L2 is**: measured, L2 pressed with the menu
open left it open with its state unchanged, and Circle then closed it.

**SYSTEM `func_8001E484`** is `func_800252F4(3, 5, cursor, 0)` over LOAD,
OPTION 1, OPTION 2, BUTTON CONFIG and QUIT GAME, through a jump table to Load
`func_8001E710`, `func_8001EC84`, `func_8001F004`, the control config
`func_8001F31C` (which calls `func_8001F4C0`, see `docs/INPUT.md`) and
`func_8001F8B4`. **OPTION 2 `func_8001F004`** is the template for a page of
switches: a loop over the same repeat gate, Up/Down for the row (sound `0xC`),
Left `0x8000`/Right `0x2000` for the value (`0xD`), cancel to leave (`0xE`),
drawn as `func_800252F4(5, 5, cursor, 1)` plus the values by `func_800246BC`,
and kept in bytes at `0x801B25DF..25E4` on leaving (`func_8001F274`).

**The lists are a table, not code.** `func_800252F4(group, count, cursor,
mode)` draws group `g` from `0x8007E660 + 0xFC × g`: nine `0x1C`-byte records,
a header (drawn only when its X is non-zero) and up to eight items. A record is
`s16 X`, `s16 Y` and a 24-byte string ending in `0xFF`. Items sit 26 apart,
from Y 32 (group 0) or under a header at Y 32 from Y 58. The selected item is boxed with `func_80026020` (blink counter
`gp+0x4C`) and its label drawn bright by `func_800261DC(0x8007E570, record)`; the
others are boxed with `func_80025F38` (box template `0x8007E5A0`) and drawn by
`func_80026158`, which sets the label colour words to the unselected shade and
calls `func_800261DC`. A header is boxed plain and drawn bright. The same shape as
Verdite2's, at different addresses and with eight items, not ten.

| group | header | items |
|---|---|---|
| 0 | (none) | USE ITEM, USE MAGIC, EQUIPMENT, STATUS/RECORDS, STORAGE, SYSTEM |
| 1 | STATUS/RECORDS | OFFENSE/DEFENSE, CONVERSATION |
| 2 | STORAGE | STORE, TAKE OUT |
| 3 | SYSTEM | LOAD, OPTION 1, OPTION 2, BUTTON CONFIG, QUIT GAME (records 6-8: SAVE, LOAD, QUIT) |
| 4 | OPTION 1 | SOUND EFFECT, MUSIC, BRIGHTNESS |
| 5 | OPTION 2 | DISPLAY HP/MP, DISPLAY COMPASS, DISPLAY ITEMS, WALKING EFFECT, PANEL |
| 6 | SHOP | BUY, SELL |
| 7 | (none) | STAY, DO NOT STAY |

**The scrolling lists** (items, magic, equipment, storage, the shop, the save
slots) are a descriptor on the caller's stack, read 2026-10-07 for
`patches/MenuMouse.cs`: `u8` X `+0x1C`, Y `+0x1D`, count `+0x1E`, visible rows
`+0x1F`, the page (the entry on row 0) `+0x20`, the cursor `+0x21`, its row
`+0x22`; `+0x24`, `+0x28`, `+0x2C` and `+0x30` are the columns' sources. The
stepper **`func_800222FC(desc, items, &confirmed, &cancel)`**, a mode at
`sp+0x10` (1 and 2 a quantity, Left/Right on `gp+0x48`), reads the pad once
through `func_800279A4` and steps the three bytes: Up and Down wrap and scroll
the page at its edges, each with sound `0xC` and then
**`func_80027A9C(items[cursor])`**, the item shown and the quantity reset to 1.
The confirm mask writes `*confirmed = 1` with no sound (the caller blips `0xD`),
the cancel mask `*cancel = -1` with `0xE`; L1/R1/L2/R2 and others turn the item
model (`gp+0x180..0x190`). Fifteen callers. The drawer **`func_80025468(desc,
mode)`** puts row `r`'s text at `(X + 13, Y + 8 + 16 r)` and the cursor's
highlight at `(X + 6, Y + 5 + 16 r)`, sized by the template `0x8007E5E8` (254 x
17), inside a frame `func_80026ACC(X, Y, 266, visible × 16 + 12)`.

**The two-box prompt** is drawn by **`func_80025B24(rec0, rec1, flag)`** (an
address OPEN.EXE also uses, so it is called through the dispatcher): two list
records boxed with the 54 x 24 template `0x8007E594`, `rec0` selected when the
flag is 0. Its loops keep the flag in a register: the item prompt
`func_80024C70` toggles it on Up or Down and returns
`-flag` on confirm, -1 on cancel; QUIT GAME `func_8001F8B4` toggles on any
direction (YES at (171, 162), NO beside it, NO first) and leaves on NO or
cancel. The format prompt `func_80020560` draws it over the chooser.

**The label font** (template `0x8007E570`: tpage `0x1D`, 4-bit at VRAM
(832, 256); CLUT `0x7D05` at (80, 500); cells 7 × 15) is a 16-column grid:
glyph `c` is at u = `(c & 0xF) × 8`, v = `(c >> 4) × 15`, advanced 7 pixels
and drawn twice for its shadow. Read off a VRAM dump (the shell's `vram`),
resident in play as well as in the menu:

| codes | glyphs |
|---|---|
| `0x00..0x19` | A..Z |
| `0x20..0x29` | 0..9 |
| `0x30..0x38` | `.` `,` `'` `-` `=` `/` `*` `#` `!` |
| `0x39` | a filled dot (a bullet) |
| `0x3A`, `0x3B` | `?`, a middle dot |
| `0x7F` | space (an empty cell) |
| `0xFF` | end |

`0x1A..0x1F`, `0x2A..0x2F`, `0x3C..0x3F` and row `0x40` are rings from other
art; rows `0x50..0x7F` are empty. **There is no lower case, no colon, no `%`,
no `+` and no brackets.** The smaller **number font** (template `0x8007E564`, the
same page, u 238 for codes 0..10 and u 245 from 11, v = `index × 15`) is 0..9,
`0x0A` blank, then C E G L M P V X, `0x13` a small ×, and `0x14` `/`;
`func_800277C0(n, digits, pad, mode)` formats a number into it, and modes 1..5
add `×`, `G`, `MP`, `EXP` or `LV`.

### The equipment page and the shop

Read 2026-10-08 off the recompiled code. The equipment page's addresses were
measured through `KF3_GEARCOMPARE=probe` (under "Comparing gear"); the shop's
were not run.

**The equipment page `func_8001C7A8`** (from the top menu, RA `0x8001A880`)
draws ten rows from the labels at `0x8007F230`: WEAPON, MAGIC, SHIELD, HEAD,
BODY, ARMS, FEET, ITEM 1, ITEM 2, * BUTTON. Its descriptor is at `sp+0x18`, its
list at (27, 35), ten rows. Row 1 opens the spell page `func_8001D0C4`, row 9
`func_8001D3A0`, and the others `func_8001CBB8(row)`.

**The candidate picker `func_8001CBB8(row)`** takes its slot byte and id range
from a jump table at `0x80011460`: row 0 the weapon, slot `0x25AF`, ids
0x00..0x21; row 2 the shield `0x25D6`, 0x33..0x3E; row 3 the head `0x25D4`,
0x22..0x29; row 4 the body `0x25D5`, 0x2A..0x32; row 5 the arms `0x25D7`,
0x3F..0x47; row 6 the feet `0x25D8`, 0x48..0x50; rows 7 and 8 the rings `0x25D9`
and `0x25DA`, 0x51..0x5E (while the list is built the other ring's count is
decremented, and restored after). `func_8001AEA4` lists the held ids of the
range: names at `sp+0x50`, counts at `sp+0x410`, ids at `sp+0x438`, and a last
TAKE OFF row with id 0xFF. The descriptor at `sp+0x18` puts the list at X 27,
Y 147, three rows, under a header EQUIPMENT at (31, 32) drawn by
`func_80027688`. The highlighted id is `u8[sp+0x438 + u8[sp+0x39]]` and its
count `u8[sp+0x36]`.

The picker's loop is the stepper `func_800222FC` (RA `0x8001CF34`), then two
frames of head, the item's model `func_80025BE8(id)` (RA `0x8001CF78`), the list
`func_80025468(desc, 5)` (RA `0x8001CF84`) and the presenter (RA `0x8001CF8C`).
Confirm opens the prompt `func_80024C70(desc, 5, 5, id)` (RA `0x8001CEB0`), with
USE at (45, 58) and CANCEL at (45, 84); its own frames draw the list at RA
`0x80024F34`. USE equips through `func_8002BDC0(id)` for the weapon, and through
`func_8002BB84(id, slot)` for rows 2 to 8 with slot 4, 0, 1, 2, 3, 5, 6.

**The shop.** The event script interpreter `func_8005C308` runs a byte
0x00..0x0F as `func_80021114(byte & 0xF)` (RA `0x8005C6BC`), the shop's group 6:
BUY is `func_80021298(shop)` and SELL `func_80021724(shop)`. BUY builds its stock
from the 150-byte table `0x80080718 + 150 × shop` through `func_8001AEA4` and
`func_800216A4`: ids at `sp+0x1190`, prices as u32 at `[sp+0x50] + 4 × index`,
and the descriptor at `sp+0x20` from `func_80027688(desc, 6, 0)`. The
highlighted id is `u8[sp+0x1190 + u8[sp+0x41]]` and its count `u8[sp+0x3E]`.
The loop is the stepper (RA `0x8002149C`), two frames of head, the model (RA
`0x800215A4`), the list `func_80025468(desc, mode)` (RA `0x800215B0`) and the
presenter (RA `0x800215B8`). The mode is 0x0D for shop 0 and 0x0A otherwise;
shop 0 has no quantity, and the others multiply by the quantity `gp+0x48`.
Confirm opens `func_80024C70(desc, 3, mode, id)` (RA `0x80021420`). A purchase
checks GOLD `0x801B2534` against price × quantity and the count below 100,
subtracts the gold, adds to the count and, for shop 0 and an id other than 0x6B
(DRAGON CRYSTAL), takes one from the stock table.

### The port settings page

Read 2026-10-06 off the recompiled code and `GAME.EXE`'s data, for
`patches/SettingsPage.cs` (`docs/SETTINGS.md`, "The page in the game's menu").

**The item.** Group 0 is drawn only by the top menu, from two calls
(`func_800252F4(0, 6, cursor, 0)` returning to `0x8001A7D8`, the opening frames,
and `0x8001A938`, the loop); its record 7 (`0x8007E724`) is zeros in `GAME.EXE`.
A pre on the list writes PORT SETTINGS there at (31, 188), under SYSTEM at the
group's spacing, and raises the count to 7; a pre on the top menu's chooser call
raises its last index from 5 to 6. The menu's dispatch (`sel < 6` through the
table at `0x80011448`) ignores 6, waits for the buttons up and goes on.

**The hook.** A post on the chooser `func_800221E8` whose `c.RA` is `0x8001A8E4`
is the top menu's call (the callee restores `RA`, so the post sees it), between
the menu's frames; `sel` 6 with `confirmed` set opens the page. The menu's `sp` is `c.SP` there: `sel` at `sp+0x18` (-1),
`confirmed` `sp+0x1C`, `cancel` `sp+0x20` (`0xFFFFFF9D` until cancelled),
checked against the generated code. The cursor is in `s3`, not memory. The pad
word `PadRead_game(1)` returns has L2 `0x0001`, R2 `0x0002`, L1 `0x0004`, R1
`0x0008` (the menu's L1+R1 test masks `0x0004` and `0x0008`). Hooks are MonoMod
detours, so the page's own calls into the frame head and presenter take the
vblank hold and the world behind menus as the game's do.

**OPTION 2's loop**, which the page copies: the repeat gate `func_800279D8`, the pad
through `func_800279A4`, then two frames of `func_80026FE4`, the list, the values
and `func_800270F8` per poll. Up/Down write 0 to `gp+0x54`, which makes the next
frame head restart the selected box's pulse (`gp+0x4C` set to `0x30`); the head
counts `gp+0x4C` and `gp+0x50` (the value boxes' pulse) round 0..`0x5F`. Cross,
Left and Right all change a value (Cross as Right); OPTION 2 wraps, the page does
not.

**Boxes and text.** The box template `0x8007E5A0` is tpage `0x1E`, CLUT `0x7D45`,
u/v 0, 118 × 24, drawn at the record's X - 6, Y - 6 by `func_80027494`, a
POLY_FT4 whose drawn size is separate from its texture's (`a2`/`a3` against
`sp+0x18`/`sp+0x1C`). So a label fits 16 cells from X 45. `0x8007E594` is a 54 ×
24 box from u `0xA0`, and `0x8007E5AC` the 16 × 16 icons (tpage `0x1C`, CLUT
`0x7A85`) the hint row draws. A plain box takes its grey from the byte `gp+0x58`;
the selected one pulses from the counter it is passed. The label colour is three
words, `gp+0x5C`, `+0x60`, `+0x64`; `func_80026158` sets them to `0x47/0x47/0x57`
(PANEL `0x801B25E4` = 0) or `0x57` each and puts them back; `gp+0x68` is the
shadow's.

**The hint row** (`func_800252F4`'s tail, Y `0xD6`): mode 0 draws the confirm
icon `[gp+0x40]` at X `0x5F`, mode 1 icon 9 at `0x5B` (OPTION 2's, for
Left/Right), then both draw "select" (`0x8009C290`) at `0x69`, the cancel icon
`[gp+0x44]` at `0xA0` and "return" (`0x8009C288`) at `0xAA`. Icons go through
`func_800269C0(x, y, icon)`, the words through `func_80026570(x, y, text, 0,
[sp+0x10] 0)`, whose font is ASCII lower case `a`..`z` (`c - 0x61`) only. The
control config's `func_8001F4C0` ends by swapping the confirm and cancel masks and
icons between the two configurations (icons 1 and 3).

**The start menu's question** `func_8002200C` is group 7 (STAY at (101, 94), DO
NOT STAY at (101, 122)) drawn by `func_800252F4(7, 2, cursor, 0)` over the same
chooser with a last index of 1. It enters the framework itself
(`func_80027198`), so the page draws the two items itself rather than calling it.

**The frame's room.** Under a header at Y 32 items are 26 apart from Y 58: a
sixth ends at Y 206, a seventh would cross the hint row. The game's own pages
use five at most.

## Death and auto reload

The death clock `0x801B261E` (zeroed by the latch, +1 a tick in the state-17
handler at `0x80031C54`) runs the sequence: 1..31 the animation, 32..64 the fade
(`func_80017158`, amount `(n - 32) << 7`), and at 65 the respawn. At 65 the
handler asks `func_8005D7BC(0x6B)`, **DRAGON CRYSTAL**: held, it is used up
(`func_8005D7F8`), the player is placed at `(0x1C800, -0x3A80, 0xC000)` and
respawned in area 0 with everything else kept. Not held, the game starts over:
`func_8005EA64` (the New Game inventory) and `func_80029188(0, 0, 0, 0, 0, 0xFF)`.
At 15 Hz, 65 ticks is 4.3 s.

`patches/AutoReload.cs` is Verdite2's auto reload on these addresses. A post on
stage 4 `func_80030FCC` (which runs while dead, on a world tick) watches the
action byte; from a death it saw the player enter, it holds the clock at 31 for
2.5 s, then runs the menu's Load without the menu: `func_80028034`, the loader
on the last used slot `0x8009C2C0` (or a pinned one), `func_80028090`, and the -3
arm above, with `func_8003078C` (the game's reset of the action byte and the
view springs) kept for a death the respawn did not clear. A death holding a
DRAGON CRYSTAL is left to the game. A session that has neither saved nor loaded
(slot 0) is left to the game too.

**Measured 2026-10-05**, slot 1 (`fdat17`, area 5, LV 12, HP 108/134), with and
without `KF3_FPS=60`: the shell's `kill`, the clock held at 31, then
`reloaded slot 1 into area 5 (HP 108/134, LV 12, state 0x00, held at tick 31)`,
the save's position and heading, the loop running, the tilt and sink at 0. The
tilt spring's velocity `0x801B2674` is left at -4, as the game's own respawn
leaves it. The beacon's `area` reads 255 afterwards, since the respawn's
release passes `0xFF` to the pending descriptor `0x8018FAE4`; the menu's own
Load passes the same. **A script must wait for the loop before `kill`**: after
`autostart` the area's arrival holds the main loop for about 5 s (`fdat17`), and
a death the patch never saw the player alive for is not armed, so a `kill` sent
then is the game's own death (the first try ran to 65 and a New Game). A death
in play cannot come first, since the world runs in the same loop as stage 4.
A death by damage, measured 2026-10-06 at `KF3_FPS=144`: the shell's `hurt 500`
took HP 108 to 0 through `func_8002A6F4`, whose latch call armed the same reload
(`death (LV 12, max HP 134)`, then `reloaded slot 1 into area 5 (HP 108/134, …)`).
Not measured: the DRAGON CRYSTAL deferral.

## Comparing gear

`patches/GearCompare.cs` is Verdite2's gear comparison, originally by @Acranon
as a Verdite2 mod, ported to these addresses. Beside the equipment page's
candidate list and a shop's buy list (and over their USE/CANCEL prompts), it
shows every stat the highlighted item would change, now and after: PWR, MAG,
OFFENSE and DEFENSE, with a TOTAL under the last two as a rough guide. It is on
by default. Settings ▸ Gameplay ▸ Compare gear and the menu's PORT SETTINGS ▸
GAMEPLAY ▸ COMPARE GEAR set it, kept as `kf3.gearcompare.enabled`; the variable
`KF3_GEARCOMPARE` wins (`0` off, `probe` a line per item compared;
`docs/ENV_VARS.md`).

"After" is the game's own arithmetic, not a copy of it. The candidate is written
into its slot byte, `func_80029500` runs, the words are read, and then the slot
byte, `0x801B2524..0x801B255B` and `0x801C12F0` are put back. The real setters
are never called, since the weapon one loads a model. The slot is the picker's
row on the equipment page; on a shop it is the id's range, a ring taking an
empty ring slot first.

The hooks: a pre and a post on `func_8001CBB8` and on `func_80021298` mark which
page is open. A post on the list drawer `func_80025468` whose RA is `0x8001CF84`
or `0x800215B0` reads the highlighted id, recomputes when it changes, and draws.
RA `0x80024F34`, the prompt's list, redraws the last panel.

It is drawn with the game's own menu routines, called from C#, as status page 2
draws: labels by `func_800261DC` in the label font `0x8007E570`; numbers by
`func_800277C0` then `func_8002636C` in the number font `0x8007E564`; and the
window `func_80026ACC` last, so it lands underneath. It is not Verdite2's
MenuDraw, whose packets and addresses belong to the other game.

The layout is right-aligned at X 293, between Y 24 and 141: above the list frame
at 147, and right of the header and the prompt's boxes. It is one column of full
names, 13 px a row, when that fits; otherwise two columns of three-letter names,
12 px a row.

### Measured (2026-10-08)

Slot 1 (fdat17, LV 12), driven through the shell's `press`: `[KF3] gear compare:
on, probe, 3 routines hooked`. Circle, Down, Down, Cross, Cross opens WEAPON's
candidates, and each Down gives a line: id 0x00 (EXCELLECTOR, worn) `no change`,
0x03 (LONG SWORD) `SLASH 39>50, BLOW 32>21, STAB 9>11, TOTAL 80>82`, 0x04 (FLAME)
`... FIRE 0>22, TOTAL 80>111`. BODY's candidates: 0x2C (HIGH-METAL ARMOR, worn) `no
change`, 0xFF (TAKE OFF) `SLASH 39>22, BLOW 42>31, STAB 27>18, TOTAL 108>71`. Cross
on FLAME opened USE/CANCEL and Circle closed it. Afterwards the weapon byte, the
seven slot bytes and `0x801B2524..0x801B255B` read as before, and no exception was
logged.

Not measured: a shop (none was reached), a ring row, and anything by eye. The
panel's place against the item's model, the USE/CANCEL boxes and the shop's page
are for the user to judge.

## The ending

Read 2026-10-06 off the recompiled code; Verdite2's "The ending" in its
`docs/RUNTIME.md` is the same shape on its own addresses.

**The boot stub.** `SLUS_002.55`'s loader loop is `func_80010038`, run with the
stub's `gp` = `0x80010260` (set by its entry `func_80010120`). It loads the file
named by the index word at `[gp]` from the table at `0x8001024C` (`0` =
`OPEN.EXE`, `1` = `GAME.EXE`, `2` = `END.EXE`) into the header at `0x8001026C`,
`Exec`s it as a call, and when it returns takes the next index from the byte
`[gp+4]` points to, which is `0x800102F0` (read in play: `[gp]` = 1, `[gp+4]` =
`0x800102F0`). Verdite2's stub is the same loop with
absolute addresses instead of `gp`.

**GAME.EXE's hand-over.** The main loop's exit word `0x8009C3F8` (`gp+0x1E4`,
"The session and the main loop") picks the next executable after
`func_80015064`:

| exit word | `0x800102F0` (next) | `0x800102F8` | what follows |
|---|---|---|---|
| 2 | 0 | (unchanged) | the title |
| 3 | 2 | 2 | `END.EXE`, all three movies |
| 4 | 2 | 3 | `END.EXE`, the last movie only |

Which of 3 and 4 the game's own ending writes, and when 4 is used, is not read.

**`END.EXE`'s main, `func_800119B8`**, after its setup: when `0x800102F8` is 2,
`func_80011D14(0)` and `func_80011D14(1)` then 60 `VSync`s; then
`func_80011D14(2)`; then it clears both stub bytes, `PadStop`, `CdControl(8)`
(stop), and **`while(1);` at `0x80011AB8`, with no `VSync`**. `func_80011D14(n)`
is the movie player, one movie a call; Start does not skip them. Measured
lengths: about 110 s, 62 s and 120 s.

**In the port the spin is a dead window.** A frame reaches the window only from
`VSync`. Measured 2026-10-06 with the hold off (`KF3_BOOTEXE=end
KF3_ENDINGHOLD=0`): once the last movie returned, the main thread sat in
`func_800119B8` → `Interrupts.PollSlow` → `TickVBlank`, the process at 104% of a
core, and the beacon (which runs off the `VSync` event) silent.

**`patches/EndingHold.cs`** (Verdite2's `EndingHold`, on by default;
`KF3_ENDINGHOLD=0` compares): a post on `func_80011D14` that, after movie 2, makes
the tail's two writes and then `VSync(0)`s instead of spinning. A button seen going
*down* (not still held from the movies) returns to the title the stub's way: index
0 at `[0x80010260]` and `0x800102F0`, then `func_80010038` entered with the stub's
`gp`, on the ending's stack a few words down. `KF3_ENDINGEXIT=0` holds for good, as
the console did (its only way out was the reset button). The shell's `press`
counts as a button.

**`patches/BootExe.cs`** gets there without finishing the game:
`KF3_BOOTEXE=end` skips the first `OPEN.EXE` at its entry `0x800136C8` with the
bytes exit word 3 writes (`end3`: exit word 4's), once, so the title reached
later runs.

Measured 2026-10-06:

- **`KF3_BOOTEXE=end`**: `OPEN.EXE` skipped, `overlay open overwritten by end`,
  movies 0, 1, 2, the hold. The thread then in `EndingHold.AfterMovie` →
  `LibEtc.VSync` → `Present` → `FrameClock.Throttle`, the process at 15% of a
  core, the beacon a line a second. The shell's `press Cross`: `returning to the
  title`, `overlay end overwritten by open`, `loaded overlay: open`.
- **Through GAME.EXE**, slot 1 at `KF3_FPS=144`, the shell's `poke 8009C3F8
  03000000`: `overlay game overwritten by end`, movies 0, 1 and 2, the hold, Cross,
  then `OPEN.EXE` playing its intro (`func_80013EBC` under the stub's
  `func_80010038` under `func_800119B8`) with the beacon running, and Start
  presses took the title back into `GAME.EXE`.
- **`KF3_BOOTEXE=end3`**: only movie 2, then the hold.

**Not ported**: Verdite2's `Program.cs` unloads every `fdat*` overlay when `open`
or `end` loads, because area modules do not overlap any executable and so
survive the swap in the dispatcher. They survive here too: through `GAME.EXE`,
`fdat17` was never reported overwritten while `END.EXE` ran. Nothing in the run
called into it. **To judge by eye**: the held frame is the last movie's last
picture, and the title comes up after the button.

## The geometry path

Surveyed 2026-10-02 (Phase 3 of the sharing plan) against Verdite2's `GAME.EXE`,
whose stage 13 and the routines below it Verdite2 rewrote in C#. Read from the
disassembly, matched with `tools/verdite-core/scripts/match_code.py`, and
measured with `KF3_GEOPROBE` (below); the comparison and what it means for
sharing are in Verdite2's `docs/SHARING.md`. **Nothing here is judged by eye**:
the roles come from packet counts, GPU command codes and table slots.

### Stage 15's calls, measured

`KF3_GEOPROBE=1` hooks each of stage 15's 22 calls and walks the ordering table
(and the 8-entry front table, below) before and after it: the packets new to the
table are what the call added, by GPU command, size and slot. Slot 1, `fdat02`,
the autostart position, turning and walking (`KF3_AUTOPAD=12:Left:3000,20:Up:6000`),
15.0 frames a second, four 5-second windows:

| # | call | packets added a frame | slots | role (and Verdite2's stage 13 counterpart) |
|---|---|---|---|---|
| 1 | `func_800357E8` | 0 | | the camera block (`func_8002E22C`) |
| 2 | `func_800351FC` | 0 | | animated textures (`func_8002DC78`, by shape) |
| 3 | `func_80041F9C` | 0 | | the fade stepper (`func_80033FBC`) |
| 4 | `func_80034BF4` | 0 | | the cull grid: writes the 25x25 grid at `0x1F800120` (`func_8002D3A8`'s job) |
| 5 | `func_80035630` | clears both tables | | flip the buffers, `ClearOTagR` both tables, reset the primitive buffer (`func_8002E064`) |
| 6 | `func_80043858` | 0 | | sound slots (`func_800353AC`, instruction for instruction) |
| 7 | `func_8003DF50` | 0 here | | the first-person arm: `0x801B25A4` is the swing clock (-1 idle, 0..0xFFF stepped 0x180 a tick by `func_8002D2A0` during a Square swing; for a bow, see "The bow" above), its clip byte is `u8[0x801B25AE]`, and its blender call's slot is `0x801B259C`; it returns before drawing while the clock is -1, lit from the player's own tile's light record (`func_80032400`) |
| 8 | `func_80016A98` | 0 | | wrapped angle difference (`func_80015374`, identical) |
| | (inline) | | | the HUD block: the HUD model table at `0x800819B4..0x80081C44` from the player block |
| 9 | `func_8003C35C` | 16 (`POLY_GT4`/`GT3`, blended) | 7-11 | the HUD's 3D models, the records at `0x80081C20` (`func_80031D5C`) |
| 10 | `func_80041E68` | 30 (15 `DR_MODE` + 15 `SPRT`) | 24-40 | overlays, through `func_80041AD4` (`func_80033E78`) |
| 11 | `func_80041D9C` | 0 | | no counterpart in stage 13 |
| 12 | `func_8003BFD0` | 60-358 | 405-6818 | **the map tile walk** (`func_80031C94`) |
| 13 | `func_80040AE4` | 243-370 | 1462-6165, and the front table | **the creature, object, effect and billboard walk** (`func_800331B4`) |
| 14 | `func_8003D280` | 0 | | full-screen quad, no counterpart |
| 15-18 | `func_8003D38C`, `func_8003D41C`, `func_8003D568`, `func_8003D64C` | 0 here | | the four full-screen quads (`func_8003202C`..`func_80032234`, 0.84-0.88) |
| 19 | `func_8003D79C` | 0 | | full-screen quad, no counterpart |
| 20 | `func_80035700` | splices the front table | 8190 | the swap (`func_8002E0FC`) |
| 21 | `func_80019614` | 0 | | the frame gate (`func_80017880`) |
| 22 | `func_80043940` | 0 | | each sound slot serviced (`func_8003549C`, 0.91) |

**Stage 15 is stage 13 in the same order**, with one call inserted (#11) and two
more full-screen quads (#14, #19); `match_code.py tree` pairs the rest in order.
About 650-800 packets a frame here, of which the map and the model walk add about 90%.

The front table: `func_80035630` clears a second, 8-entry table (pointer
`0x801A91B8`, `0x801A9178 + 32 * buffer`), and the swap links it in at slot 8190
of the main one, so it is drawn first, behind everything: entry 8190's link goes to
the front table's entry 7, and the front table's entry 0 to what entry 8190 linked,
so walked from the head it is entry 8191, entry 8190, the front table's eight entries,
then entry 8190's own packets and the rest. The sky (object kind
`0xF0`, below) is drawn into it: 45 packets (39 `POLY_GT4`, 6 `POLY_GT3`) at
front slot 7.

### The frame's tables and buffers

| what | where |
|---|---|
| the buffer index | u8 `0x801AEAE8`, toggled by `func_80035630` |
| ordering table, 8192 entries, `ClearOTagR` | `0x80199174 + 0x8000 * buffer`; pointer `0x801A9174` |
| front table, 8 entries | `0x801A9178 + 0x20 * buffer`; pointer `0x801A91B8` |
| primitive buffer descriptor `{start, end, cur}` | `0x80199158 + 12 * buffer`; pointer `0x80199170`. `cur` reset to `start` each frame |
| packets counted by kind | `0x801AEB10`, `0x801AEB14`, `0x801AEB18` (zeroed each frame) |

**The geometry routines share a parameter block in the scratchpad** (`0x1F800000`),
not arguments: the walk fills it and every assembler reads it. The fields this
survey needed:

| scratchpad | what |
|---|---|
| `+0x08` | ordering-table base for the map: the table + `0x3C0` (a 0xF0-slot bias) |
| `+0x0C` | the front table |
| `+0x10` | the area's model table (from `*0x801A929C`) |
| `+0x14`, `+0x18` | the primitive cursor and end, copied from the descriptor and back |
| `+0x1C` | the face header being assembled (`+0x1D` its length in words, `+0x1F` its code) |
| `+0x20` | the face cursor |
| `+0x24` | the model's 28-byte entry |
| `+0x28` | the normals |
| `+0x30`..`+0x3C` | the current face's vertex-cache entries |
| `+0x44` | the vertex cache (`0x801AAC54`) |
| `+0x48`, `+0x50` | cache and source cursors during the transform |
| `+0x4C` | the model's vertices |
| `+0x54` | the colour the lighting op starts from |
| `+0x58`, `+0x5C` | the fog's near and far (`0x801AEC7C`, `0x801AEC80`) |
| `+0x64` | the face's otz |
| `+0x66` | the near limit: a face whose every corner's otz is below it (100) is dropped |
| `+0x68`, `+0x6C`, `+0x70` | packets counted |
| `+0x100`..`+0x118` | the tile's position, the camera position and tile, scratch |
| `+0x120` | the 25x25 visibility grid (`func_80034BF4`), one byte a cell |
| `+0x124`, `+0x270` | the model walk's page bitmaps (reusing the grid's space after the map walk) |

### The map

The same format as Verdite2's, at another address:

- **The map**: 80x80 tiles of 10 bytes at `0x801D4464`. `+0` the lower half's
  mesh (240 or more is not drawn), `+1` its height (`y = -(h << 7)`), `+2` its
  rotation (bits 0-1), `+4` its light record (bits 0-5); `+5..+9` the upper half.
  A tile is 2048 units. **Bits 2-3 of `+2` change at run time**: a RAM diff
  across a 12 s walk in `fdat17` (2026-10-06) found 22 bytes changed, every one of
  them `+2` of a tile on the player's path (`0x8 -> 0x4 -> 0x0`, `0x0 -> 0x4`), and
  none standing still. What they mean is not read; the rotation bits never moved.
  **On a load the map and its meshes change over several frames**: the new half
  table is in RAM before the area's module is loaded, then the mesh vertices and
  the water rects fill in, for 50-250 ms of drawn frames (2026-10-06,
  `KF3_MAPPROBE`; "One build an arrival" in `DEVELOPMENT.md`).
- **The light records**: 64 of `0x6C` bytes at `0x801AEEFC`: four 20-byte light
  matrices by rotation at `+0`, the colour matrix at `+0x50`, the back colour as
  three bytes at `+0x64` (shifted left 4), and the depth cue's pair at `+0x68`,
  `+0x6A` (handed to `func_80035358`). Verdite2's are `0x68` bytes, with the back
  colour at `+0x62`.
- **The depth cue's pair**: `func_80035358(near, far)` stores them at `0x801AEC7C`
  and `0x801AEC80`, where the assemblers read them (scratchpad `+0x58`/`+0x5C`), and
  calls `SetFogNear(near, 200)`, or `SetFogNear(0xFFFF, 200)` when near equals far.
  The half's record is therefore its only light and fog input: colour matrix, back
  colour and pair. A record fogs nothing when its near is 32000 or more (the
  assembler's gate) or equals its far.
- **Records across a tile edge** (the saved 28-area RAM corpus, 2026-10-05, from
  arrival positions): each area uses 1 to 5 records, with pairs from
  `(6000, 14000)` to `(18000, 24000)`; none fogs nothing. Of 107,137 drawn halves,
  1,725 (1.6%) have a half on the same level among the eight around them whose
  pair differs, and 1,504 one whose colour matrix or back colour differs; 784
  record edges in all. Areas 20-25 and 27 use one record each. Some areas differ in
  fog only (area 4: 136 fog, 43 light; area 19: 34 and 0). Measured live, the
  records are uploaded about once per area load and did not change while standing
  in an area; the map's half table changed one to two times per load. These are the
  inputs `NeighbourBlend` blends (`docs/GPU_RENDERER.md`, "Blending light and fog
  across tile edges").
- **The meshes**: the model table `*(0x1F800010)`, 28-byte entries from `+0xC`:
  `+0` the vertices' offset, `+4` the vertex count, `+8` the normals' offset,
  `+0x10` the faces' offset, `+0x14` the face count. A vertex is 8 bytes
  (`SVECTOR`). A face is a header word (byte 1 its length in words, byte 3 its
  kind, bit 1 blended) and a body. A triangle's body: the packet's UV, CLUT and
  page halfwords at `+0..+0xA`, the normal at `+0xC`, the corners' vertex-cache
  offsets at `+0xE`, `+0x10`, `+0x12`. A quad's: UVs to `+0xE`, the normal at
  `+0x10`, corners at `+0x12..+0x18`. Verdite2's `FillTriangle`/`FillQuad` take
  the UVs and the normal from the same offsets. The map assembler takes three kinds: `0x24` (triangle,
  one normal), `0x2C` (quad, one normal) and `0x34` (triangle, an `NCDS` per
  corner on three normals: Verdite2's map assembler has no such kind); a `0x3C`
  face is skipped. The lit model assembler takes all four, `NCDS` for the flat
  kinds and `NCDT` for the others.

**`func_8003BFD0`, the walk** (Verdite2's `func_80031C94`): a 25x25 window of
cells reaching ahead of the camera (Verdite2's is 24x24; see "The draw radius and
the window" below), its origin at `0x801AEC74`/`78` (copied to `+0x118`/`+0x11C`),
bounded to the 80x80 map. A cell's grid byte must
have bit `0x02` for either half to be drawn (Verdite2 has bit 0 for the lower and
bit 1 for the upper); its position relative to the camera goes to
`0x1F800100` and `func_8003BB04` draws it.

**`func_8003BB04`, a half** (Verdite2's `func_80031950`): the light record's
light matrix for the half's rotation into LLM, its colour matrix into LCM, the
depth cue, the back colour into BK; one `MVMVA` puts the half's position through
the view matrix (`0x801AEB4C`) into the translation, and the rotation is one of
the four view-times-rotation matrices the camera block precomputes. Then the far
gate (`0x8018FAD4 == 1` and `0x8018FAEA` set: a mesh past half the table is
skipped, Verdite2's `0x8017E05C`/`0x8017E072` gate) and the assembler: **grid bit
`0x04` with at least 10 KB of primitive buffer left is `func_8003AB04`, otherwise
`func_80039D50`.** Inlined GTE throughout, where Verdite2 calls libgte.

**The far gate** in full: while the s16 at `0x8018FAD4` is 1 (an area change
pending, "Changing area" above) and the byte at `0x8018FAEA` is set, a half whose
mesh index is at or past half the model table's count (`*(table + 4) >> 1`) is
skipped. It only acts during a load; what `0x8018FAEA` marks was not read. The
retained renderer's added halves (`patches/RenderDistance.cs`) take the same gate.

### The draw radius and the window

Read from `func_80034BF4` and checked against it (2026-10-05): the recompiled
classifier ran on the 28-area corpus's RAM at 64 yaws and five pitches, 8,960 runs,
in `tools/scene-probe/RenderDistanceFixtures.cs`.

- **The eye's tile is the window middle, not its centre.** The middle cell is
  row `(0xC7FF - 8·sin s7) >> 12`, column `(0xC7FF - 8·cos s7) >> 12` (`s7` = yaw +
  `0x400`, sin and cos at 4096), 4 to 20; the epilogue writes the origin
  `0x801AEC74`/`78` as the eye's tile (`0x801AEC64`/`68`) less the middle. The
  window so reaches up to 20 cells ahead of the eye and 4 behind it. Rows are Z,
  columns X. In every run the middle was the eye's tile.
- **A cell is drawn** (bit `0x02`) only when it is in the window, inside the cone
  (two half-planes from an apex about three cells behind the eye, at the
  half-angle S5) and its **whole-tile offset (i, j) from the eye's tile** has
  `i² + j² < T5`, in integers; then a flood may clear it. `T5` is the radius byte
  squared, times `cos(S5/2) >> 12` once `S5 ≥ 600` (looking up or down). Every lit
  cell of the 8,960 runs met all three tests; 521,464 cells that met them were
  cleared by a flood.
- **The radius byte `0x801AEAE9` is a zone value.** Three writers: the area set-up
  `func_80035394` writes 13 (and 1 to `0x801AEAEA`); object kind `0xE3` writes its
  own while `func_80046884` puts the player's tile inside its box, once from
  `func_80044D9C` (record bytes −8/−7 the box, −6 the radius, −5 for `0x801AEAEA`)
  and every tick from `func_80047010` (object `+0x30`/`+0x31` the box, `+0x32` the
  radius, `+0x33`). The corpus's arrivals read 9 in areas 3 and 12-27 and 13 in the
  rest; a run that arrived in 21, 25 and 27 from other tiles read 13 throughout.
  What `0x801AEAEA` does was not read.
- **The sky** (`func_800400AC`) ran in areas 0-12 of the scene census and in none of
  13-27.

**`func_80039D50`, the bulk assembler** (Verdite2's `func_8002FECC`, 147 calls
and about 285 packets a frame here):

1. The vertex pass: `RTPS` on each vertex into an 8-byte cache entry: the screen
   word, otz `SZ3 >> 2`, and a fog weight `((otz - near/4) << 14) / (far - near)`
   clamped to `0..0x1F0F`, or 0 when the near is 32000 or more. Verdite2's
   transform (`func_8002E650`) takes the weight from `IR0` on one of three curves
   with the same 32000 cut-off; the cache entry has the same layout.
2. Per face: `NCLIP` on the cached screen words (dropped unless positive); the
   near test; the otz the average of the corners (`/3`, `>>2`), clamped to
   `0x1F0F`; a packet of `0x28` (`POLY_GT3`) or `0x34` (`POLY_GT4`) bytes from the
   cursor, the routine returning if it would pass the end; then **the same packet
   Verdite2's `FillTriangle`/`FillQuad` write**: the same offsets from the same
   face fields, `NCCS` on the face normal (Verdite2's `NormalColorCol`; a `0x24`
   triangle runs it twice on the same normal) and a
   `DPCS` per corner with the corner's fog weight (`DpqColor`), the length byte 9
   or 12, the code `0x34`/`0x3C` with the face's blend bit; linked at slot
   `otz + 0xF0` by an inline `addPrim`.
3. No clipper. A face is sent whole whatever its size or nearness, unless all its
   corners are nearer than the near limit.

**`func_8003AB04`, the near assembler** (8 calls, about 72 packets a frame here):
the same vertex pass, then each face is handed to **libgte's polygon division**,
four routines with no static name (`func_80074D88` and `func_800756A8` for a
triangle, `func_80075188` and `func_80075B48` for a quad: an 8-byte entry and a
body that runs `RTPT` on the subdivided corners in a stack frame of 0x18-byte
vertex records and writes the packets itself), with one `NCDS` colour a face.
**The near map is drawn as subdivided `POLY_FT3`/`POLY_FT4`: flat colour, no
per-corner fog.** Verdite2 subdivides small meshes with its own routine and then
assembles them with the clipped assembler (`func_80030540`); KF3 has no
counterpart of that clipper (`Clip3FTP`/`Clip4FTP` are not linked).

### The models

**`func_80040AE4`, the walk** (Verdite2's `func_800331B4`; `match_code.py` ranks
it first of 1155). The same four tables in the same order, the same liveness
tests and the same record fields, with other strides:

| table | base | records | stride | live | Verdite2 |
|---|---|---|---|---|---|
| creatures | `0x80185DA8` | 200 | `0x88` | `u8[+0x9] == 1` | 200 x `0x7C` |
| objects | `0x80191A5C` | 396 | `0x44` | `u16[+0x6] != 0xFF` | 396 x `0x44` |
| effects | `0x801B80EC` | 128 | `0x4C` | `u8[+0x0] != 0xFF` | 128 x `0x48` |
| billboards | `0x80182968` | 128 | `0x18` | | 128 x `0x18` |

A creature: flags at `+0x28` (`0x2000`, `0x80000` the volume query, `0x20`
placed at its record with the matrix at `0x8007E4C4`), the query at `+0x2C`, the
model `u8[+1] + 0x80`, rotation `+0x40/+0x42+0x800/+0x44`, and the stack
arguments `+0x48`, `+0x5C`, `+0xC`, `+0x18`, `+0x14`, `+0x16`, `+0x13`, `+0x15`:
every offset as Verdite2's. The definitions are 120 bytes at `0x8018C7E8`. An
object: the kind `u8[+4]` (`0x1F` an ambient sound, through `func_80046884`,
identical to Verdite2's `func_80037810`; `0xF0` the sky; and `0xF2`, `0xE5`,
`0xE9`, which Verdite2's walk does not have), definitions of 24 bytes at
`0x8018FB3C`. The two page bitmaps are in the scratchpad (`+0x124`, `+0x270`)
rather than the stack. The queries and helpers match Verdite2's at 0.92-1.00
(`func_80040694` the point query, `func_80040708` the volume one, `func_800407CC`
identical to `func_80032EAC`, `func_8004EEE0` the placement, 0.95).

**The queries read the grid's copy**: the cull grid's epilogue copies the 25x25 grid
from the scratchpad to `0x801AEC84` (`func_80018F5C`, `0x9C` words) after the flood,
and `func_80040694` reads the byte at `0x801AEC84 + 25·((z >> 11) + s32[0x801AEC70]) +
(x >> 11) + s32[0x801AEC6C]` (the negated window origin), 0 outside the window;
`func_80040708` ORs a square of them. The walk draws a record when that byte ANDs its
mask (a creature's `u8[+3]`, with `0x10` added for flag `0x2000`; an object's `u8[+0]`,
or its flag `0x08` for far scenery).

**The page bitmaps are the game's on-demand loader** (2026-10-06). A model the walk
draws marks its model in the bitmap at `+0x124` and its texture pages at `+0x270`;
after the creatures and again after the objects, `func_800409C8` goes through the
model bitmap against the slot table at `0x801A92B0` (4 bytes a model): a marked model
with no slot is **requested from the CD** (`func_80040830`), a loaded one is pinned
(status byte at slot pointer `-0xC` set to 2), an unmarked one released (1).
`func_800408D0` does the same for texture pages against `0x801B0A2C` (8 bytes a page,
the status at record `+4`; `func_80015CE0` loads one): a creature's pages (definition
`+7`, `+8`) from entry `0x33`, an object's (`+2`) from `0x93`. The walk's residency
test `func_800405E8` passes a model below `0x68` always (the fixed ones effects and
billboards use) and any other only with status 1 or 2. **Marking models the game would
not draw makes it stream them**: the first version of the render distance's models did,
and the first warp crashed in the CD loader's checksum `func_80019B68`, reading
`0x80800000`.

**The billboard clock `0x80182964` is bumped by this walk**, once a walk, as
Verdite2's `0x80195170` is by its own; see "What still runs at the render rate".

**`func_8003E34C`, the submitter** (Verdite2's `func_80032588`, ranked first by `match_code.py` once a libgte call counts as the GTE commands it runs): the matrices by
inline `MVMVA` (three a product, where Verdite2 calls `MulMatrix0` and friends),
the light and depth cue, then **the MO pose blender `func_800431E8`** for a model
under `0x80`, and one of three paths on a submit flag: `0x40` is `func_800366A8`
(its own vertex pass and libgte's division, all four routines: the near path),
`0x04` the vertex pass then `func_80037BEC` (forced blending, its rate from the
flag's low bits), otherwise the vertex pass then **`func_80035CA4`, the lit
assembler** (Verdite2's `func_8002F214`, ranked first: the same face fields and
packet offsets, `NCLIP`/`NCDS`/`NCDT` where Verdite2 calls `NormalClip`,
`NormalColorDpq`, `NormalColorDpq3`). The vertex pass is inline, twice: `RTPS`
with the map's fog weight, or (`fp` clear) an orthographic `MVMVA` with a fixed
depth, Verdite2's `func_8002E910`.

`func_8003F304` is a second submitter into the front table, through
`func_80038844` (the lit assembler on `+0x0C`); not called in this scene. The model
walk calls it (`0x80041440`) for an object whose flag byte has `0x08` set: far
scenery, such as area 4's castle, kept behind everything. In the 28-area census only
fdat14 (area 4) submits through it.

**`func_800400AC`, the sky** (Verdite2's `func_80032AC4`): reached from object
kind `0xF0` with the same arguments (`u16[+0xA]`, `u8[+0x3C]`, `u8[+0x3B]`,
`0x1FFF - u8[+0x3A]`, `rec+0x24`, `rec+0x34`, `u8[+1]`), its model `u16[+6] -
0xDD`; its assembler `func_80039428` lights with `NCCT`/`NCCS` (no depth cue)
into the front table. The record is also copied to `0x8018FAF8`, and redrawn from
there after the table when `0x8018FAD4 == 1`.

**`func_800431E8`, the MO pose blender** (Verdite2's `func_80034DA8`, 0.77; the
same field offsets), `(slot, bank index, clip byte, clip time)`: its decoders are
Verdite2's, `func_80042D70` (0.98), `func_80042E34` (identical), `func_80042EB0`
(0.98), `func_80043894` (0.93); **`func_80042CAC` (0.95) is the clip clock**,
Verdite2's `func_8003486C`: `(bank, clip, time, &segment)`, the weight through the
pointer at the caller's `sp+0x10`, the segment record in v0. The clip record
layout and the blender's re-morph are in "3c" in `docs/SMOOTHING.md`. The same MO format; three of Verdite2's small callees
(the vertex-cache helpers) are not called.

**A record's rotation** goes through the game's own `RotMatrix`, `func_800166F4`
(read 2026-10-07): `Ry'(y) Rx(x) Rz(z)` from the s16s at `+0`, `+2`, `+4`, with
`Ry'(t) = [[c,0,-s],[0,1,0],[s,0,c]]` (`func_8001660C`, the usual Ry with the
angle negated), `func_80016598` the usual Rx and `func_80016680` the usual Rz. The
walk hands an object's `+0x24..+0x28` through the scratchpad lane `0x1F800114`
with `0x800` added to y. The item pickup `func_8005DB30` and the camera's axes are
in "Turning a picked-up item" in `docs/INPUT.md`.

### The camera block

`func_800357E8(VECTOR *pos, SVECTOR *rot)` (Verdite2's `func_8002E22C`, the same
shape): the position to `0x801AEC4C` and its tile (`>> 11`) to `0x801AEC64`/`68`,
the rotation to `0x801AEC5C`, `RotMatrix` into the view matrix `0x801AEB4C`,
`func_80016598` on the pitch into `0x801AEC0C`, and **four view-times-rotation
matrices at `0x801AEB8C + 0x20 k`** (`func_80016290`), which the map's halves load
by their rotation. Verdite2 negates the yaw and has no precomposed four.

**A null pointer keeps the previous camera**: the block tests `pos` and `rot`
separately and skips the copy for a 0. **Stage 15 passes its own two arguments
straight through**, and it has three callers: the main loop `func_80014BD4` at
`0x80014FA8`, with two blocks in its own frame (`sp + 0x18`, the position;
`sp + 0x28`, the rotation; stage 10 fills them, see the main loop's table), and
`func_80030568` and `func_800305D8`, both with `0, 0`: redraws from the last
camera, not yet identified. Read 2026-10-02 for `docs/SMOOTHING.md`, which
builds on it.

### How the assemblers are entered

- `func_80039D50(mesh)` and `func_8003AB04(mesh)`: `a0` the mesh id (`& 0xFFFF`),
  everything else from the scratchpad. `func_80039D50` is a leaf with no stack
  frame; it reads the fog's near and far itself.
- `func_80035CA4(model, bias)`: `a0` the model index (its 28-byte entry, as the
  map's), `a1` a slot bias added to the face's average otz. Unlike the map
  assembler, a face whose average is 0 or less, or whose slot is `0x2000` or
  more, is dropped, as Verdite2's `Place` drops. The vertex cache is already
  filled by the caller (the submitter's inline pass, or `func_8003C35C`'s).
- `func_80037BEC(model, bias, rate)`: the same, `a2` the blend rate (0-3).

### The HUD's models and their transform

`func_8003C35C` (stage 15's call #9) has **no transform routine of its own**: the
HUD's vertex transform is inline, two copies of one loop at `0x8003C6FC` and
`0x8003C784`. Read from `generated/game.cs` on 2026-10-05; C# in
`patches/PolyAssemblerHud.cs` (`docs/PICTURE.md`, "The HUD's transform").

- **The records**: from `0x80081C20`, 0x24 bytes each, until a first byte of
  `0xFF`; a record whose first byte is 1 is drawn. `+0x02` u8 a 0x6C-byte light
  block at `0x801AEEFC + 0x6C * n`; `+0x04` u16 the model (its pointer at
  `0x801A92B0 + 4 * id`); `+0x08..+0x0C` the scale (three s16, `ScaleMatrix`);
  `+0x10..+0x14` the translation (three s16, loaded as `TR`); `+0x18..+0x1C` the
  rotation (`RotMatrix`); `+0x01`, `+0x06` and `+0x20` go to `func_800431E8`.
- **Per record**: `RotMatrix` into the stack, `func_80035358` with the light
  block's `+0x68`/`+0x6A` (not identified), `BK` from its `+0x64..+0x66 << 4`, the
  light colour matrix from its `+0x50`, its light matrix (`+0x00`) times the
  rotation by three `MVMVA`s into the stack and loaded as `LLM`, `ScaleMatrix`, then
  the scaled rotation as `R` and the translation as `TR`. The scratchpad gets the
  table, cursor, end, cache (`+0x44` = `0x801AAC54`), colour and a CLUT offset of 0
  (`+0x84`). `func_800431E8` decides whether the vertex pointer at `+0x4C` is
  recomputed from the model header (the first loop) or the last one reused (the
  second); the loops are otherwise the same.
- **The transform**, per vertex: `MVMVA` with `sf=1`, `R·V0 + TR`, **no divide**:
  the HUD is orthographic. `MAC1..3` are stored to scratchpad `+0x54`, `+0x58`,
  `+0x5C`, and the cache entry is filled from them as halfwords: X at `+0`, Y at
  `+2`, `MAC3` at `+6`, then `MAC3 >> 2` at `+4`. Then `func_80035CA4(0, 0)`
  draws the model from the cache. The shift by 12 drops the fraction, and no
  `RTPS`/`RTPT` runs, so the vertex map learned nothing of these corners (0%).
- **The compass is the only piece.** In slot 1's save (`fdat17`), standing and
  turning, only the record at `0x80081C44` is drawn: model 0, 26 vertices, light
  block 48, scale 85 on every axis, `rot.x` 4017 (a fixed tilt about X) and
  `rot.y` the heading. Its `R` always has elements off the diagonal.
  The gauges are not drawn by this routine.

### The HUD's sprites

`func_80041E68` (stage 15's call #10) walks records of `0x14` bytes from
`0x800819B4` until a first byte of `0xFF`, and hands each shown one (first byte
non-zero) to `func_80041AD4(record, record[1], colour)`, which writes a
`DR_MODE` and a `SPRT`. `func_80041D9C` (call #11) walks `0x80081928` the same
way, drawing a record once per bit of its first byte (1, 2, 4), the last with a
second colour: the bottom message box. Read from `generated/game.cs`, 2026-10-06.

| offset | what |
|---|---|
| `+0x00` | shown (call #11: the bits) |
| `+0x01` | the mode argument; 2 with `u8[0x8018FAD8] == 10` draws one pixel down and right |
| `+0x02`, `+0x03` | u, v |
| `+0x04`, `+0x05` | width, height (the gauges write the fill's width here) |
| `+0x06`, `+0x08` | X, Y, s16: the `SPRT`'s position |
| `+0x0E` | CLUT |
| `+0x10` | the tpage for `SetDrawMode` |
| `+0x12` | the ordering-table slot (dropped at 0 or less, or `0x2000` and up) |

Stage 15's HUD block shows one of two sets by `u8[0x801B25DD]`: records 0-12
(`0x800819B4..0x80081AA4`) or 15-29 (`0x80081AE0..0x80081BF8`), and hides 13 and
14 (`0x80081AB8`, `0x80081ACC`). In slot 1's save the second set is drawn: the HP/MP panel and its
digits at X 5..80, Y 12..51. The compass model (above) is at X 290.

### What a C# assembler would have to keep

- **The scratchpad is not in `PSMemory.Ram`**: it is a separate 1 KB array
  (`PSMemory._scratchpad`, private), reached only through `ReadU32`/`WriteU32` at
  `0x1F800000`. Verdite2's verify modes snapshot `Ram` alone, which here would
  miss every parameter and counter the routines share; a verify needs the
  scratchpad snapshotted too (1 KB through the accessors, or a fork accessor).
- The per-frame packet counters at `+0x68..+0x70` and `0x801AEB10..18`.
- libgte's division, which writes packets from library code: a C# near path
  means rewriting those four routines too, since they are where the near map's
  packets come from.

## What still runs at the render rate

With frame pacing on, `KF3_RATECENSUS=20` (in `fdat02`, standing, 144 fps) lists
the words that change between two frames on which no stage ran. Besides the
buffers a frame swaps and the vblank handler's counters:

- **`0x801AEB20`**: the first scrolling texture's phase, `func_800351FC` (stage 15
  call #2). Held to the tick by `patches/TextureScroll.cs` since 2026-10-02; the
  records are in "3e" in `docs/SMOOTHING.md`.
- **`0x80182964`** (held to the tick by `patches/SpriteAnim.cs` since 2026-10-02, and by the C# walk when it is on,
  `docs/SMOOTHING.md`), a counter `func_80040AE4` (stage 15's call at `0x800428A8`,
  the model walk: see "The geometry path") bumps once a drawn frame, and the nine `0x18`-byte records after it, each with
  a cel index cycling 0-3: billboard or sprite animation, the shape of
  Verdite2's `SpriteAnim` defect. Not fixed.
- Single words at `0x801920FC`, `0x80192D78`, `0x80192ECC`, `0x80192FDC`,
  `0x80194274` (an opencode reading: object records' `+0x40`, slots 24, 71, 76,
  80 and 150; no writer found), `0x800C87F0` and near `gp` (`0x8009C07C`,
  `0x8009C1A4..`, `0x8009C20C`, `0x8009EEBC`, written by `func_8006C744`).
  None is reached by a `lui` literal; not identified.
- **The bottom message box** (only while one is shown): stage 15's call #3
  `func_80041F9C` steps its state machine (`0x801AEAF7..F9`). Held to the tick by
  `patches/MessageBoxHold.cs` since 2026-10-02 ("The message box at the bottom" in
  `docs/SMOOTHING.md`).
- **Loops that present frames of their own**, which no census taken standing in
  the main loop sees: a dozen routines entered from a gated stage call stage 15
  themselves (the item pickup `func_8005DB30`, the fades, the script
  interpreter), held to the tick by `patches/LoopPacing.cs`; and every `VSync`
  outside stage 15 (the menu's presenter `func_800270F8` and its cursor repeat
  `func_800279D8`, the loading screens, the movies, `func_80019538`'s two-vblank
  wait) returned at once, held to a real vblank by `patches/VBlankPacing.cs`. Both
  2026-10-02; see `docs/SMOOTHING.md` and `docs/DEVELOPMENT.md`.
- **The compass needle** (only while turning, so a standing census misses it):
  stage 15's HUD block steps its spring, the speed at `gp + 0xD8` (`0x8009C2EC`)
  and the yaw at `0x80081C3A`/`0x80081C5E`. Held to the tick by `Stage15` since
  2026-10-02.

## Scene inventory refresh (2026-10-04)

Current generated source contains 38 stage-15 call sites and 19 distinct callers
including modal and area-module drawing. `8005C0D4` calls stage 15. The native
scene census attributes family/domain/area/caller and preserves unknown packets.
The present normal slot-1 save is area 5/fdat17; older fdat02 measurements describe
a different save. See [GPU_RENDERER.md](GPU_RENDERER.md) for loaded-area corpus
conditions, numerical checks and unresolved direct/indirect callers.
