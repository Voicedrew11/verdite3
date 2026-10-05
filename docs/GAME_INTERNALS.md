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
| `0x801B25A4` | s16 | arm swing/cast clock, -1 idle, stepped during a swing |
| `0x801B25A6` | s16 | arm swing window |
| `0x801B25A8` | s16 | arm swing window |
| `0x801B25AA` | u8 | arm/spell runtime state; read by `func_8002FE1C` |
| `0x801B25AB` | u8 | charging spell id |
| `0x801B25AC` | u8 | committed spell id |
| `0x801B25AD` | u8 | committed spell id (second slot) |
| `0x801B25AE` | u8 | arm clip byte / cast phase |
| `0x801B25AF` | u8 | equipped weapon id and current arm-effect id (0xFF none) |
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

`func_80029500` is the recompute every equip, level-up, load and condition
change calls. It copies the six base stats `0x801B2516..2520` into the adjusted
`0x801B2524..252E`, halves them while the curse flag `0x801B255E` is set, zeroes
the 17 ratings `0x801B2538..255A`, and sums in the equipped weapon
(`0x801B25AF`, record `0x801D37A4`, stride 0x44) and the accessory records
(`func_800293E4`). Anything written straight to `0x801B2524..255A` lasts only
until the next call. A mod hooks the recompute and overwrites the derived words.

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
the table at `0x801D37A4` (stride 0x44); armor and accessories 34..94 use
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
| 7 | `func_8003DF50` | 0 here | | the first-person arm: `0x801B25A4` is the swing clock (-1 idle, 0..0xFFF stepped 0x180 a tick by `func_8002D2A0` during a Square swing), its clip byte is `u8[0x801B25AE]`, and its blender call's slot is `0x801B259C`; it returns before drawing while the clock is -1, lit from the player's own tile's light record (`func_80032400`) |
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
of the main one, so it is drawn first, behind everything. The sky (object kind
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
  A tile is 2048 units.
- **The light records**: 64 of `0x6C` bytes at `0x801AEEFC`: four 20-byte light
  matrices by rotation at `+0`, the colour matrix at `+0x50`, the back colour as
  three bytes at `+0x64` (shifted left 4), and the depth cue's pair at `+0x68`,
  `+0x6A` (handed to `func_80035358`). Verdite2's are `0x68` bytes, with the back
  colour at `+0x62`.
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
cells round the camera (Verdite2's is 24x24), its origin at `0x801AEC74`/`78`
(copied to `+0x118`/`+0x11C`), bounded to the 80x80 map. A cell's grid byte must
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
`func_80038844` (the lit assembler on `+0x0C`); not called in this scene.

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
