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
| 7 | `func_8003DF50` | 0 here | | the first-person arm: returns at once while the s16 at `0x801B25A4` is -1 (this save), lit from the player's own tile's light record (`func_80032400`) |
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
same field offsets): its decoders are Verdite2's, `func_80042D70` (0.98),
`func_80042E34` (identical), `func_80042EB0` (0.98), `func_80042CAC` (0.95),
`func_80043894` (0.93). The same MO format; three of Verdite2's small callees
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

- **`0x80182964`** (held to the tick by `patches/SpriteAnim.cs` since 2026-10-02,
  `docs/SMOOTHING.md`), a counter `func_80040AE4` (stage 15's call at `0x800428A8`,
  the model walk: see "The geometry path") bumps once a drawn frame, and the nine `0x18`-byte records after it, each with
  a cel index cycling 0-3: billboard or sprite animation, the shape of
  Verdite2's `SpriteAnim` defect. Not fixed.
- Single words at `0x801920FC`, `0x80192D78`, `0x80192ECC`, `0x80192FDC`,
  `0x80194274`, `0x801AEB20`, `0x800C87F0` and near `gp` (`0x8009C07C`,
  `0x8009C1A4..`, `0x8009C20C`, `0x8009EEBC`, written by `func_8006C744`).
  None is reached by a `lui` literal; not identified.
- **The compass needle** (only while turning, so a standing census misses it):
  stage 15's HUD block steps its spring, the speed at `gp + 0xD8` (`0x8009C2EC`)
  and the yaw at `0x80081C3A`/`0x80081C5E`. Held to the tick by `Stage15` since
  2026-10-02.
