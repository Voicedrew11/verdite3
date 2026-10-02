# Game internals

The reverse-engineered game: the main loop and its stages, player state,
movement, areas, saves and the boot stub — every address and routine this project
learns, written here rather than left in the commit that found it. Verdite2's
`docs/GAME_INTERNALS.md` is the model, but none of its addresses apply.

## Status

The main loop, its frame gate and vblank handler, the player block, the card
loader and the start menu (2026-10-02, for the agent harness and frame pacing).

## Area code modules

`GAME.EXE` keeps the resident area module's pointer at `0x8018FAE0` and calls
through its slots: `func_80044D9C` calls slot 8 (`+0x20`), the first call that
reached one. A module is loaded at `0x801E8308` from `CD/COM/FDAT.T` entry
`3n+2`, and the pointer is the base plus 4 (past a count word). Area n's data are
entries `3n` and `3n+1`. See "GAME.EXE loads code" in `docs/RECOMPILATION.md`.

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
| 4 | `func_80030FCC` | opens the in-game menu (Cross), whose modal loop `func_8002008C` runs inside it and presents its own frames |
| 5 | `func_80052E5C` | |
| 6 | `func_8005BC50` | |
| | (inline) | `sb 0 → 0x801B24F2` |
| 7 | `func_8005EB20` | |
| 8 | `func_80018358` | also called by the card loader's wait loop |
| 9 | `func_80061940` | |
| 10 | `func_8002B330(sp+0x18, sp+0x28)` | |
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

**In the port the handler runs 120 times a second**, not 60: measured,
`0x801C12E8` advances 119.9997/s. `LibEtc.TickVBlank` delivers `0xF2000003` and
then raises IRQ 0, whose `ServiceIrq` delivers `0xF2000003` again (Verdite2's
`docs/TODO.md` records the same double delivery, found there by reading). So the
gate passes every two vblanks, and **without frame pacing the main loop runs at
30.0 a second, twice the game's 15** (measured: 30.0 calls/s of every stage;
holding Left turns 1200 units of yaw a second, against 600 at 15 Hz). The
play-time minute runs twice as fast too. See `docs/TODO.md`.

## The player

The block at `0x801B24E4` (cleared, `0x67` words, when a session starts). Found
by matching a save's payload against RAM, walking and turning while diffing
RAM, and from the code that reads it: the save-title filler `func_80028B70`
reads EXP and level, and `func_8001B2BC..` copies max HP into HP (a full heal).

| address | type | what |
|---|---|---|
| `0x801B24E4` | s32 | EXP |
| `0x801B24F0` | u8 | level |
| `0x801B24FA` | u16 | max HP |
| `0x801B24FC` | u16 | HP |
| `0x801B24FE` | u16 | max MP |
| `0x801B2500` | u16 | MP |
| `0x801B25F0`/`F4`/`F8` | s32 | position x, y, z (y is height; -12800 standing in `fdat02`) |
| `0x801B260A` | s16 | heading, `0x1000` a turn; Left increases it, 40 a world tick |
| `0x801B2588` | u32 | play time, minutes (from the vblank handler) |

A New Game starts at `[126976, -15360, 16384]` heading 0 in `fdat02`, with HP
50/50, MP 30/30, level 1. The camera's copy of the position is at `0x801AEC4C`.

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

## What still runs at the render rate

With frame pacing on, `KF3_RATECENSUS=20` (in `fdat02`, standing, 144 fps) lists
the words that change between two frames on which no stage ran. Besides the
buffers a frame swaps and the vblank handler's counters:

- **`0x80182964`**, a counter `func_80040AE4` (stage 15's call at `0x800428A8`)
  bumps once a drawn frame, and the nine `0x18`-byte records after it, each with
  a cel index cycling 0-3: billboard or sprite animation, the shape of
  Verdite2's `SpriteAnim` defect. Not fixed.
- Single words at `0x801920FC`, `0x80192D78`, `0x80192ECC`, `0x80192FDC`,
  `0x80194274`, `0x801AEB20`, `0x800C87F0` and near `gp` (`0x8009C07C`,
  `0x8009C1A4..`, `0x8009C20C`, `0x8009EEBC`, written by `func_8006C744`).
  None is reached by a `lui` literal; not identified.
