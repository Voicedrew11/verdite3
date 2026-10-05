# The geometry path in C#

The plan for owning Verdite3's polygon assemblers in C#, and its work as it is
done. The routines, tables and records it relies on are in "The geometry path" in
`docs/GAME_INTERNALS.md`; the comparison with Verdite2 and the overall build
order are in Verdite2's `docs/SHARING.md` (2026-10-02, the geometry survey).

## Status

**The first unit is built and on** (2026-10-02): `func_80039D50` and
`func_80035CA4` run in C# (`KF3_POLYASM`, on by default; `0` puts both back),
verified with 0 mismatches over three sessions in `fdat02`. See "The first unit:
the bulk assemblers" below for the numbers. The three variants stay recompiled
(see "The variants are not the same loop"). The near path is next on this
path, **deferred behind smoothing** (`docs/SMOOTHING.md`), which the user put
first on 2026-10-02.

## Why the assemblers, and why these two first

Verdite2's picture features rest on its assemblers being C#: the routine that
builds a packet knows every corner's depth, what lit it and which mesh asked, and
records them beside the packet (Verdite2's fork patch `0050`, `GtePacketDepth`;
`0048`, `GteLightMap`). The alternative, matching finished packets back to GTE
output by address, is the path the Z-buffer never worked on in Verdite2.

`func_80039D50` (the map's bulk) and `func_80035CA4` (the lit models and the
HUD's models) come first because they build most of the frame, and because they
build Verdite2's packets: the same `POLY_GT3`/`GT4` from the same face fields,
the same GTE operations, the same `otz + 0xF0` slot. So Verdite2's packet fill
can be copied nearly as it is, and the two copies compared later for a shared one.

## What they buy

Measured where it says so; slot 1, `fdat02`, the autostart position, turning and
walking.

1. **A Z-buffer from records, over most of the frame.** With `0050` active the
   depth buffer takes a packet's record or nothing, and a packet with nothing
   keeps painter's order, as on the console. These two routines add about three
   quarters of the frame's packets (probe: `func_80039D50` 147 calls and about
   285 packets a frame, `func_80035CA4` about 200 for the models and 16 for the
   HUD, of 650-800). **Not covered after this unit**: the near map
   (`func_8003AB04` through libgte's division, about 72 packets a frame here),
   the near models (`func_800366A8`), the blended and front-table variants, the
   sky, the billboards and overlays. The near map is where interpenetration is
   closest to the eye, so the Z-buffer can be tried after this unit but is not
   finished until the near path is C# too.
2. **The same records for per-pixel lighting and even fog** (`0048`, `0049`):
   the fill knows the light matrix, the back colour and each corner's fog
   weight. Verdite3's fog weight is the CPU's own formula, which the C# computes
   and can hand on directly.
3. **Identity**: which mesh, half and model a packet came from, which Verdite2's
   later features (materials, the GPU world, reflections) all start from, and a
   facing test at fractional corners (`0052`) once sub-pixel is on.
4. **Some speed, not much and not the reason.** Uncapped (`KF3_FPS=off`), 793
   fps, 1.26 ms a frame. `KF3_GEOPROBE=time` puts `func_80039D50` at 0.73 ms a
   frame inclusive, but the probe's own hooks cost about 0.4 ms a frame (it reads
   600 fps), so the routine is roughly 0.5 ms, about 40% of the frame. Verdite2's
   C# copy of its counterpart took 30-36% off that routine. Expect 0.15-0.25 ms,
   10-20% more uncapped frames, and nothing at 60 or 144 on this machine. The
   recompiled routine stores and reloads its working state in the scratchpad
   between almost every step; locals are where the time would come from.
5. **The verify harness with the scratchpad**, which every later C# routine here
   needs (all of them pass parameters through it).

**What it does not buy.** No picture changes: every picture switch in the fork
(`GteDepth`: perspective, sub-pixel, Z-buffer, AO) is off in Verdite3, and this
unit turns none on. **Perspective correction and sub-pixel do not need it**: in
Verdite2 both still come from the address map (`GteVertexMap`), which follows
recompiled code as well as C#, so they could be switched on in Verdite3 as their
own small unit, before or after this one.

## The unit: scope

- `func_80039D50` and `func_80035CA4` as replace hooks, in C#, under
  `KF3_POLYASM=0|1|verify` (**0, the recompiled routines, by default** until a
  session of verify reads clean), with `KF3_POLYASM_MAP=0` and `KF3_POLYASM_LIT=0`
  to compare each alone.
- No records (`0050`, `0048`): they are side tables and do not change RAM, so
  they go in with the Z-buffer unit, where they can be measured by what they
  serve.
- The variants (`func_80037BEC` blended, `func_80038844` into the front table,
  `func_80039428` the sky's) are the lit loop with other parameters; they follow
  once the lit loop verifies, in this unit if it is cheap.

## Design

**Files.** `patches/PolyAssembler.cs` (install, modes, the hooks, verify, and the
map's body), `patches/PolyAssemblerFill.cs` (the `Frame`, allocate,
`FillTriangle`, `FillQuad`, the GTE lighting, link) and
`patches/PolyAssemblerLit.cs` (the lit loop, as Verdite2 splits it), namespace `Kf3`. The fill is copied from Verdite2's `PolyAssembler.cs` and kept
**textually close** (same names, same order of reads and writes, same comments),
because the unit after the near path diffs the two games' fills to extract one.
Leave out what Verdite3 has no use for yet: the depth and lighting records,
`RenderDistance`, `Remaster`, `TileColours`, and the `GteVertexMap` hoisting in
`Frame.Refresh`.

**Hooking.** `HookManager.AddReplace` on the method `SymbolRegistry.Resolve("game",
null, addr)` gives, through `HookAttach.OnOverlayLoad`, retried until
`HookAttach.Installed` reads true for both. With PGXP's CPU tracking on, call the
recompiled routine (as Verdite2 does), since PGXP follows values through
registers the C# does not have.

**Exactness rules** (Verdite2's, "The polygon assembler in C#" in its
`docs/PATCHES_AND_MODS.md`):
- Every read and write of a vertex word, a packet word and a table entry goes
  through `PSMemory` in the recompiled routine's order, so that the address map
  follows vertices into packets for perspective and sub-pixel later.
- GTE operations as `Gte` calls in the same order, with the same registers
  loaded, so the GTE is left as the routine leaves it. That includes the quirks:
  a `0x24` triangle runs `NCCS` twice on the same normal.
- The same number of `Interrupts.Poll` calls: the recompiled `func_80039D50` polls
  at its two loop heads (the vertex pass and the face loop); read
  `generated/game.cs` for `func_80035CA4`'s.
- **The scratchpad holds the routine's working state**, and the recompiled code
  stores to it constantly (the cursors at `+0x20`, `+0x48`, `+0x50`, the corners
  at `+0x30..+0x3C`, the `NCLIP` result at `+0x60`, the otz at `+0x64`, the
  packet at `+0x2C`, the counters at `+0x68..+0x70`, the cursor at `+0x14`).
  Nothing reads it while the routine runs, so the C# keeps these in locals and
  writes **the values the routine would have left**, at its end and at each early
  return (the primitive buffer running out). Verify proves the end state.
- Integer edge cases: the recompiler's `div` leaves `LO`/`HI` unchanged when the
  divisor is 0 (the fog weight divides by `far - near`, which `func_80039D50` does
  not test), and the `/3` is `mult` by `0x55555556`, which equals C#'s `/ 3` for
  every 32-bit sum.

**`func_80039D50(mesh)`**, from "The map" in `docs/GAME_INTERNALS.md`:
1. Set up: the mesh's 28-byte entry from the model table (`*(0x1F800010)`), the
   vertex, normal and face pointers, the fog's near and far from `0x801AEC7C`
   and `0x801AEC80`.
2. The vertex pass: `RTPS` per vertex, the cache entry (screen word, `SZ3 >> 2`,
   the fog weight clamped to `0..0x1F0F`, 0 when the near is 32000 or more).
3. The face loop over the three kinds (`0x24`, `0x2C`, `0x34`; anything else
   skipped, `0x3C` included): `NCLIP` (dropped unless positive), the near reject
   (every corner's otz under `+0x66`), the otz (`/3` or `>> 2`) clamped to
   `0x1F0F`, allocate (`0x28` or `0x34` bytes; return if past the end), fill,
   light (`NCCS` then `DPCS` per corner with the corner's fog weight, or `NCDS` per
   corner for `0x34`), the length byte and code, link at `*(0x1F800008) + otz * 4`,
   count. The `0x34` kind's three normals are not read yet: read its branch
   (`0x8003A704..0x8003AA5C`) first.

**`func_80035CA4(model, bias)`**: the same entry, faces and fill, on a vertex
cache its caller filled; `NCDS` for `0x24`/`0x2C`, `NCDT` for `0x34`/`0x3C`; the
slot is the average plus `bias`, and a face whose average is 0 or less, or whose
slot is `0x2000` or more, is dropped (Verdite2's `Place`). Port it from Verdite2's
`PolyAssemblerLit.cs`.

**Verify** (`KF3_POLYASM=verify`), Verdite2's harness: snapshot RAM, the CPU
context and the GTE; run the recompiled routine; snapshot; restore; run the C#;
compare; put the recompiled result back, so a mismatch never reaches the picture.
Compare all of RAM except the `0x2000` bytes below the entry `SP`, `S0-S7`, `FP`,
`SP`, `RA`, the GTE's 64 registers, **and the scratchpad's 1 KB**. `PSMemory`'s
scratchpad array is private and outside `Ram`: read it with 256 `ReadU32`s, or add
a span accessor to the fork in a commit of its own. Report mismatches with the
first differing address and both values, a line every few seconds.

## Done when

- Verify reads **0 mismatches** (RAM, scratchpad, registers, GTE) over a session
  in `fdat02`: standing, turning both ways, walking, the menu opened and closed;
  and a second area if one can be reached (only one save exists on the card).
- `KF3_GEOPROBE=1` reads the same packets per call with `KF3_POLYASM=1` as with 0.
- `KF3_FPS=144 KF3_FPS_PROBE=1` still reads 144.0 fps at 15.0 ticks/s, and the
  uncapped rate is measured both ways without the probe.
- Written up here, with the numbers; `KF3_POLYASM*` in `docs/ENV_VARS.md`.

Nothing in it needs the user's eyes: the picture is identical by construction,
and verify is the proof.

## The first unit: the bulk assemblers

Built 2026-10-02, as designed above, with these findings and measurements. Slot 1,
`fdat02`; "the start" is the autostart position, "the walked spot" is where
`KF3_AUTOPAD=12:Left:3000,20:Up:6000` leaves the player.

**What the routines actually do, where the plan was short.**
- **The working state goes back to the scratchpad at the end, not as it goes.**
  `Frame` holds the parameters (`+0x08`, `+0x14`, `+0x18`, `+0x44`, the colour at
  `+0x54` or `+0x64`) and the working state; `LeaveMap`/`LeaveLit` write what the
  routine would have left, read first from the scratchpad so that a field the
  call never reached keeps its old value. The map leaves `+0x14`, `+0x1C..+0x40`,
  `+0x48`, `+0x50`, `+0x58`, `+0x5C`, `+0x60`, `+0x64` and its counters
  `+0x68..+0x70`; the lit assembler leaves `+0x14`, `+0x1C..+0x28`, its NCLIP
  result at `+0x70` and **its own counters at `+0x78` (faces), `+0x7C` (links) and
  `+0x80` (packets)**, not the map's.
- **The lit assembler adds a CLUT offset**: every packet's `+0xE` is the face's
  CLUT plus the u16 at scratchpad `+0x84`. Verdite2's has no such offset.
- **The map's `0x34` kind reads its corners where a `0x24` face keeps them**
  (`+0xE`, `+0x10`, `+0x12`), while its normals are at `+0xC`, `+0x10`, `+0x14`:
  the second corner's index is the second normal's. The lit assembler reads a
  `0x34` face's corners at `+0xE`, `+0x12`, `+0x16`. The C# keeps the map's
  reading. No `0x34` face reaches the map assembler in `fdat02` (counted over one
  session: 0 of 390,310 faces met; no `0x3C` either), so it may never matter.
- **The map links a negative otz nowhere**: the clamp is to `0x1F0F`, and the
  link's `sltiu` against `0x1F10` then drops a negative one after its packet was
  allocated and counted. The models drop a mean of 0 or less, and a slot of
  `0x2000` or more, as Verdite2's `Place` does.
- **`LO` and `HI`** are left as the last `mult` (the `/3`) or `div` (the fog
  weight) left them, including the recompiler's untouched `LO` on a zero
  divisor, and verify compares them. A zero fog range (`far == near`) never
  occurred.
- **Read order is kept for RAM, not for the scratchpad**: every read and write of
  a vertex, cache entry, face, normal, packet and table entry goes through
  `PSMemory` in the recompiled order (the cache's fog half is written as the game
  writes it: zeroed by the otz's word store, the quotient, then each clamp), and
  the GTE sees the same writes and commands in the same order. Reads of the
  scratchpad are not repeated.

**Verify** (`KF3_POLYASM=verify`), over three sessions: standing, turning both
ways, walking, and the in-game menu opened and closed (through `KF3_SHELL`,
`press Cross` then `press Circle`; `carda.sav` byte-identical afterwards):

| routine | calls | RAM | scratchpad | registers (S0-S7, FP, SP, RA, LO, HI) | GTE |
|---|---|---|---|---|---|
| `func_80039D50` | 153,278 | 0 | 0 | 0 | 0 |
| `func_80035CA4` | 13,337 | 0 | 0 | 0 | 0 |

Faces filled: the map 87,576 `0x24` and 196,433 `0x2C`; the lit assembler
354,979 `0x24`, 92,079 `0x2C`, 85,768 `0x34` and 14,972 `0x3C`. Two branches
never run in `fdat02` were forced, under verify only, by edits made for the test
and then removed: **every `0x24` map face relabelled `0x34`** for the call (32,261
calls, 0 mismatches), and **the primitive buffer's end pulled to 0x60 bytes past
the cursor** on every fourth call (32,241 map and 2,364 lit calls, 1,456 and 567
of them ending early, 0 mismatches).

**`KF3_GEOPROBE=1`** with `KF3_GEOPROBE_FUNCS=8003AB04,80039D50,80035CA4`,
`KF3_FPS=15` and the survey's autopad: all 206 per-call lines (packets, words,
slots, GPU codes, sizes, every window) are **identical** with `KF3_POLYASM=1` and
`0`, once the packet addresses are left out.

**Pacing**: `KF3_FPS=144 KF3_FPS_PROBE=1` reads 144.0 fps drawn at 15.0 ticks/s
with the C# on.

**Speed** (`KF3_FPS=off`, no probe; the mean of each run's fps lines, two runs
each way, alternated):

| where | recompiled | C# | per frame |
|---|---|---|---|
| the start (the lit assembler; the map's halves all near) | 1,243 and 1,227 | 1,310 and 1,338 | 0.81 → 0.76 ms |
| the walked spot (147 map calls a frame) | 796 and 791 | 1,172 and 1,171 | 1.26 → 0.85 ms (+48%) |

**The plan's estimate was low**: it expected 10-20% from the map, and the walked
spot gains 48%, 0.41 ms a frame, most of the routine. The recompiled routine
reaches the scratchpad through `PSMemory` on almost every instruction, which is
dearer than Verdite2's stack traffic. Still nothing at 60 or 144 on this machine.

### The variants are not the same loop

The plan said `func_80037BEC`, `func_80038844` and `func_80039428` were the lit
loop with other parameters. Read against `func_80035CA4` (2026-10-02):
- `func_80037BEC` (forced blending) is: `a2 << 5` replaces the page's bits 5-6,
  the code byte is forced (`0x26`, `0x2E`, `0x36`, `0x3E`), and it spills the
  packet and corners to `+0x2C..+0x3C`, which the C# would have to leave.
- `func_80038844` (the front table) is not: it always shifts the fog weight right
  by `3 - a2`, and with `a2` non-zero links into the 8-entry table at `+0x0C` at
  `((otz >> 11) + bias) & 7` (`>> 13` of the sum for quads), with no range test.
- `func_80039428` (the sky) is a different loop: kinds `0x30`/`0x38` of its own
  (corners at `+0xA..`, the colour from the face, untextured `POLY_G3`/`G4`), `0x34`/`0x3C` lit with `NCCT`/`NCCS` and no `IR0`, the slot
  `(short)a1 & 7` in the `+0x0C` table, no depth from the vertices.

**Only `func_80039428` runs in `fdat02`** (once a frame, 45 packets, `0x34` and
`0x3C` only; `KF3_GEOPROBE_FUNCS=80037BEC,80038844,80039428`, turning and
walking): the other two cannot be verified here, and the sky's own kinds are not
met either. All three stay recompiled until a scene calls them; the sky is drawn
behind everything into the front table, so the Z-buffer does not need its records.

## After this unit

1. **The near path**: `func_8003AB04`, `func_800366A8` and libgte's four division
   routines (`func_80074D88`, `func_80075188`, `func_800756A8`, `func_80075B48`),
   verified the same way. New code: Verdite2 has no counterpart.
2. **The Z-buffer**: the transforms note each cache slot's `SZ3`, the fills
   record each packet (`0050`), `GteDepth.ZBuffer` and `GtePacketDepth.Enabled`
   switched on behind a setting that ships off until judged by eye; the HUD's
   models (`func_80035CA4` from `func_8003C35C`) left unrecorded, as Verdite2
   leaves its HUD.
3. **Extract the shared fill** into Verdite Core (the proposal in Verdite2's
   `docs/SHARING.md`), with `KF2_POLYASM=verify` and Verdite2's acceptance test
   as the proof that Verdite2 did not move.

## Retained submission work (2026-10-04)

The final renderer now submits persistent source meshes/poses before legacy
projection loops. Literal native scene references supplement the existing bulk
and near assemblers. Their all-area numerical results and remaining drawing
families are recorded in [GPU_RENDERER.md](GPU_RENDERER.md). Near subdivision
is still a counted packet fallback; an unsplit triangle is not treated as its
verified replacement.
