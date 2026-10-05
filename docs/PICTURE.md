# The picture: 24-bit colour, perspective, sub-pixel, the Z-buffer

The plan for Verdite3's first picture features, and its work as it is done.
Written 2026-10-02, after smoothing (`docs/SMOOTHING.md`) was finished and judged.
The geometry routines named here are "The geometry path" in
`docs/GAME_INTERNALS.md`; the C# assemblers are `docs/GEOMETRY.md`. Verdite2's
write-ups are in its `docs/RENDERING.md` ("Perspective correction", "Sub-pixel
vertex positioning", "Z-buffer", "The assemblers write the depth", "A thin face
was culled on whole pixels") and are the background for everything below.

## Status

**All four units built and measured, none judged** (2026-10-02). Every switch is off, in Settings ▸ Testing ▸ Picture; the near path is recompiled until a model close to the eye has run under verify. The runtime half of all four features is already in
the fork this repository shares with Verdite2 (`tools/RecompOne`), switched off.
What is missing is Verdite3's half: the switches, the probes, the controls, and
for the Z-buffer, the depth.

## What the fork already has

| feature | runtime switch | fork patch | what feeds it |
|---|---|---|---|
| 24-bit shading | `GteDepth.TrueColor` | `0021` | nothing: the GL backend keeps 8 bits a channel in `quant5` |
| no dither | the E1 draw-mode word's bit 9 | none | the game's `PutDrawEnv` `dtd` byte (Verdite2's `NoDither.cs` borrows and restores it) |
| perspective-correct textures | `GteDepth.Enabled` | `0009`, `0012` | **the address map**, `GteVertexMap` |
| sub-pixel vertices | `GteDepth.Subpixel` | `0010` | the address map; a fractional facing test in the C# assemblers (`0052`) |
| Z-buffer | `GteDepth.ZBuffer`, `GtePacketDepth.Enabled` | `0014`, `0036`, `0050`, `0051`, `0079` | **packet records** written by the C# assemblers (`0050`) |

`GteDepth.Enabled`, `Subpixel` and `ZBuffer` each switch `GteVertexMap` on through
`GteVertexMap.SetActive(GteDepth.Active)`.

## The decision: the address map for the vertices, records for the depth

**Perspective and sub-pixel do not need a native vertex map.** `GteVertexMap`
follows each value the GTE writes (`RTPS`/`RTPT`'s screen words, with their
depth and the fraction the GTE truncated) through RAM by value, `NoteRead` and
`NoteWrite` in `PSMemory`, into the packet that finally holds it. It works through
recompiled code and C# alike, as long as the C# reads and writes the vertex words
through `PSMemory`. In Verdite2 it answered for 92-97% of vertices even with every
assembler in C# ("Measured: the coverage was already there" in its
`RENDERING.md`), because King's Field copies whole words out of a transform cache.
Verdite3 builds its packets the same way, and its C# assemblers were written to
keep the recompiled order of every vertex read and write for this reason
("Exactness rules" in `docs/GEOMETRY.md`). Verdite2's C# publishes fractions itself
in one place only, the HUD's orthographic transform (`PolyAssemblerHud.cs`,
`GteVertexMap.Publish`), which the map cannot follow; leaving the HUD on whole
pixels costs nothing anyone sees.

**The Z-buffer needs depth from the assemblers.** Verdite2's Z-buffer never looked
right on the address map's depth, and it stopped needing to when its assemblers
became C#: they know every corner's depth and record it beside the packet
(`GtePacketDepth`, a side table keyed by packet address, checked against the
command word and the first and last vertex words before it is believed). **While
that source is active the depth buffer takes a packet's record or nothing**, and a
packet with nothing keeps painter's order, as on the console. Verdite3's C# bulk
assemblers build about three quarters of the frame; the near path (about 72
packets a frame here, and the geometry closest to the eye) is still recompiled
library code and would have no records.

## The units

Each unit ends with its controls in Settings ▸ Testing (`patches/TestingSection.cs`:
add a row, keep it in `interface.ini` as `kf3.*`, an env var still wins) and a
write-up in this file. **Every picture switch ships off until the user has judged
it by eye**, then on (as `KF3_SMOOTH` went); the agent measures and asks, it never
screenshots.

### Unit 1: 24-bit colour and no dither

- `patches/TrueColor.cs` from Verdite2's (72 lines): `GteDepth.TrueColor`,
  `KF3_TRUECOLOR`. Game-independent; the GL backend only.
- `patches/NoDither.cs` from Verdite2's (353 lines): the pre/post on
  **`PutDrawEnv`** that clears and restores the `DRAWENV + 0x16` `dtd` byte, and the
  ordering-table scan for a `DR_MODE`/`DR_TPAGE` E1 word with bit 9 set. This
  disc's `PutDrawEnv` is bound in `config/kf3.json` at `0x80016740` (open),
  `0x8007A178` (game) and `0x8001449C` (end), and its `DrawOTag` at `0x800166CC`,
  `0x8007A104` and `0x80014428` (`FramePacing.DrawOTag`); Verdite2's addresses are
  another game's. Read whether `func_80041E68`'s 15 `DR_MODE`
  packets a frame (stage 15 call #10, the overlays) carry bit 9: Verdite2's game
  emitted no mid-table E1 words, Verdite3's does.
- One Testing control, as Verdite2's *Shading* combo: Dither / None / Smooth
  (24-bit). Verdite2 found the two were one question.
- **Done when**: `KF3_NODITHER_PROBE=1` (port Verdite2's) reads GPUSTAT bit 9 as 0
  for a session with "None" or "Smooth", the draw-env and table counters say
  which route the bit took, 144.0 fps at 15.0 ticks/s; the user judges.

#### Unit 1, done: measured, not judged

- `patches/TrueColor.cs` is the switch alone (`KF3_TRUECOLOR`);
  `patches/NoDither.cs` hooks PutDrawEnv and DrawOTag in all three executables
  (6/6 committed) as pre/post pairs, so `FramePacing`'s post on DrawOTag and the
  config's HLE replace compose. One Testing control, **Picture ▸ Shading**: Dither
  (the console) / None / Smooth (24-bit), kept as `kf3.shading`; a set
  `KF3_TRUECOLOR` or `KF3_NODITHER` wins. Default Dither.
- **Both routes carry the bit here**, unlike Verdite2. `KF3_NODITHER_PROBE=1` in
  `fdat02`, 144 fps: 144 draw envs a second ask for dither (one a frame), and
  **2160 table E1 words a second, 15 a frame, every one with bit 9 set** (the
  overlays' `DR_MODE`s from `func_80041E68`); no other E1 word is in the table.
- GPUSTAT bit 9 after each frame: **1** with Dither, **0** with None for the
  whole session, so nothing reaches the GPU by a third route.
- 144.0 fps drawn at 15.0 ticks/s both ways.
- **Not judged by eye**: None's bands and Smooth's gradient.

### Unit 2: perspective and sub-pixel on the address map

- `patches/Perspective.cs` and `patches/Subpixel.cs` from Verdite2's (215 and 206
  lines), with their probes: `KF3_PERSPECTIVE_PROBE=1` (the map's hits, misses,
  roots and propagations a second), `KF3_SUBPIXEL_PROBE=1`. Drop what is
  Verdite2's own (its HUD publisher, its clipper).
- **Check the fill's fast path first.** `PolyAssemblerFill.cs` reads and writes
  guest RAM directly (`R16`, `W16`, `W8` through `Frame.Ram`) whenever
  `PSMemory.DirectRam`, and **`DirectRam` does not consider `GteVertexMap.Active`**.
  The direct helpers are 8- and 16-bit (face fields, UVs, colours), and the vertex
  and packet words appear to go through `ReadU32`/`WriteU32`, which the map sees;
  confirm that, and if any vertex word takes the direct path, turn it off while the
  map is active (Verdite2's `PolyAssembler.Hoisted` does exactly this, with
  `GteVertexMap.MaybeBound`).
- **Measure the coverage by routine**, not only in total: the map's hit rate with
  `KF3_GEOPROBE`'s per-call windows, or a probe of misses by the packet's owner.
  The expected answers: the bulk map and the lit models high (whole-word copies
  out of the cache); **the near map unknown** (libgte's division routines run
  `RTPT` on subdivided corners in a stack frame of 0x18-byte records and write the
  `POLY_FT3`/`FT4` packets themselves: nothing in Verdite2 is shaped like it); the
  HUD's models missed (an orthographic `MVMVA`, not a projection). A low near-map
  rate is unit 4's problem, not a reason to stop here.
- **The fractional facing test** (`0052`) in the C# assemblers: `NCLIP` on the
  corners plus their fractions (`GteVertexMap.Peek`, Verdite2's
  `PolyAssembler.cs` around its line 923), or thin faces drop on whole pixels under
  sub-pixel. `KF3_SUBPIXEL_CULL=0` to compare, as Verdite2's.
- **Done when**: verify for `KF3_POLYASM`, `KF3_MODELWALK`, `KF3_MOPOSE` and
  `KF3_STAGE15` still reads 0 with the map active (it reads and writes; the
  recompiled result stands, so a C# path the map does not follow shows as a
  coverage drop, not a mismatch); the coverage table by routine is written here;
  144.0 fps at 15.0 ticks/s and the uncapped rate both ways; the user judges both.

#### Unit 2, done: measured, not judged

- `patches/Perspective.cs` and `patches/Subpixel.cs` are switches and probes
  (`KF3_PERSPECTIVE`, `KF3_SUBPIXEL`, `KF3_PERSPECTIVE_PROBE`,
  `KF3_SUBPIXEL_PROBE`); each sets its `GteDepth` flag and
  `GteVertexMap.SetActive(GteDepth.Active)`. Testing ▸ Picture, kept as
  `kf3.perspective` and `kf3.subpixel`.
- **The fill's fast path needs no guard**: every vertex and screen word in
  `PolyAssembler*.cs` goes through `ReadU32`/`WriteU32`; the direct `R16`/`W16`/`W8`
  carry only face fields, UVs, colours and the otz, which the map does not track.
- **The fractional facing test** (`0052`) is in `PolyAssemblerFill.cs`'s one
  `Visible`: under sub-pixel, NCLIP on the corners plus their fractions
  (`GteVertexMap.Peek`); a corner the map did not answer for, or clamped, keeps the
  whole-pixel answer. It stands down under verify. `KF3_SUBPIXEL_CULL=0`, or the
  indented Testing box, to compare. Standing in `fdat02` it changed no face's
  facing (0 kept, 0 dropped a second); a thin face edge-on is where it would.
- **Verify reads 0** for `KF3_POLYASM`, `KF3_MODELWALK`, `KF3_MOPOSE` and
  `KF3_STAGE15` with both on, turning and walking.
- **The map answers for 97.4% of vertices** in total (`KF3_PERSPECTIVE_PROBE=1`).
  By routine, `KF3_MAPCOVERAGE=1` (`patches/MapCoverage.cs`: the packet cursor
  `0x1F800014` read round each writer, and each polygon in the table asked of
  `GteVertexMap.Peek`), `fdat02`, 144 fps:

  | routine | packets a frame | corners answered |
  |---|---|---|
  | map bulk `func_80039D50` | 254 | 99.9% |
  | near map `func_8003AB04` | 40 | **100%** |
  | lit models `func_80035CA4` | 93 | 100% |
  | HUD models (`func_80035CA4` under `func_8003C35C`) | 16 | 0% (orthographic) |
  | sky `func_80039428` | 45 | 90.8% |
  | anything else | 0 | |

  **The near map is covered**: libgte's division copies whole words from its
  `RTPT` records into the packets, so unit 4 publishes nothing. The arm, the
  models' near submit and the blended variants built nothing in this scene.
- 144.0 fps at 15.0 ticks/s both ways. **Uncapped, the map costs about a
  fifth**: 1420, 1427, 1404 fps off; 1108, 1135, 1127 on (standing, `fdat02`).
- **Not judged by eye**: perspective, sub-pixel.

### Unit 3: depth records from the C# assemblers (a partial Z-buffer)

- **The record**: `GtePacketDepth.Slot(pkt)` filled as each packet is finished,
  sealed with its command word and first and last vertex words; `NoDepth(pkt)` for
  a packet not recorded, so a stale record cannot match. Template: Verdite2's
  `PolyAssemblerDepth.cs` (`DepthOn`, `SealDepth`, `NoDepth`).
- **Where the depth comes from**: Verdite3's vertex cache entry already holds each
  corner's otz (`SZ3 >> 2`) beside its screen word ("The map" in
  `GAME_INTERNALS.md`), for the map (the assembler's own vertex pass) and for the
  models (the submitter's inline pass, still recompiled). Decide whether two bits
  of lost precision matter (Verdite2 notes the full `SZ3` beside the cache words);
  if they do, the map's vertex pass is C# and can keep it, and the models' pass
  can be read back from the GTE as each cache entry is written.
- **Recorded**: `func_80039D50` (the map's bulk) and `func_80035CA4` (the lit
  models). **Not recorded**: the HUD's models (`func_80035CA4` reached from
  `func_8003C35C`: an `InHud` flag round that call, as Verdite2's), the
  first-person arm (`func_8003DF50`, `InArm`), the sky and everything in the front
  table (drawn first, behind everything), the blended and front-table variants
  (`func_80037BEC`, `func_80038844`, still recompiled), the overlays and
  billboards. A miss is painter's order, which is what they had.
- `patches/ZBuffer.cs` from Verdite2's (530 lines), keeping: the switch
  (`GteDepth.ZBuffer`, `GtePacketDepth.Enabled` only while `KF3_POLYASM` is C#),
  the coplanar tolerance (`0051`, `DepthBias`/`DepthSlope`; Verdite2 found wall
  panels fighting without it), blended surfaces after the opaque ones behind them
  (`0079`, `BlendOrder`), the restart threshold, and the probe
  (`KF3_ZBUFFER_PROBE=1`: packet depths recorded a second, polygons that found
  theirs, triangles tested, unmatched). Drop its GPU-world and remaster parts.
- **Done when**: recorded equals found in the probe, the share of triangles tested
  is written here (expect about three quarters), 0 unmatched, the verify modes still
  0, 144.0 fps; the user judges it, **knowing the near map is not covered yet**.

#### Unit 3, done: measured, not judged

- `patches/PolyAssemblerDepth.cs` (drafted by an opencode agent, checked by its
  measurements): every packet the C# bulk map and lit-model fills finish gets a
  `GtePacketDepth` record of its corners' depths, sealed with the command word and
  the first and last vertex words; a packet not recorded gets `NoDepth`. Recording
  runs only while `GtePacketDepth.Enabled`, which `ZBuffer.SyncSource` sets each
  frame to "Z-buffer on and `KF3_POLYASM` in C#" (so never under verify).
- **Depth**: the map keeps the full `SZ3` per cache slot from its C# vertex pass,
  checked against the two cache words. The models' vertex pass is still the
  recompiled submitter's, so their records use `otz << 2` and lose two bits.
- **Not recorded**: the HUD's models (`func_80035CA4` under `func_8003C35C`) and
  the arm (`func_8003DF50`), by pre/post flags; the near path, the sky and
  everything else are not C# and have no records. They keep painter's order.
- `patches/ZBuffer.cs` (agent-drafted from Verdite2's): the switch, the coplanar
  tolerance (`DepthBias` 1, `DepthSlope` 0.5), blended surfaces after the opaque
  ones behind them (`KF3_BLENDORDER`), the restart threshold (off), the probe.
- **Measured**, `KF3_ZBUFFER_PROBE=1`, `fdat02` after turning and walking, 144
  fps: 104832 records a second and **104832 polygons found theirs**; 15264 a
  second had none (87.3% of polygons recorded); **84.1% of submitted triangles
  depth-tested** (the plan expected about three quarters). Verify reads 0 for
  `KF3_POLYASM`, `KF3_MODELWALK` and `KF3_STAGE15` with the Z-buffer on.
- 144.0 fps at 15.0 ticks/s. **Uncapped it costs about a quarter**: 1386, 1386,
  1373 fps off; 1022, 1065, 1072 on (standing).
- Packets a frame are 418 standing whatever the switches; a view after walking
  read 865, which is the view, not the records.
- **Not judged by eye**, and **the near map is not recorded yet** (unit 4): it is
  the geometry nearest the eye.

### Unit 4: the near path in C#, recorded

New code: Verdite2 has no counterpart (it subdivides with its own routine and
clips with `Clip3FTP`/`Clip4FTP`, which this game does not link).

- `func_8003AB04` (the near map: the vertex pass, then each face to libgte's
  division) and `func_800366A8` (the models' near submit, under the submit flag
  `0x40`), and **libgte's four division routines**: `func_80074D88` and
  `func_800756A8` (triangles), `func_80075188` and `func_80075B48` (quads), each an
  8-byte entry and a body that runs `RTPT` on the subdivided corners in a stack
  frame of 0x18-byte vertex records and writes the packets itself, one `NCDS`
  colour a face ("The map" in `GAME_INTERNALS.md`).
- `KF3_NEARPATH=0|1|verify`, through `Differential` (RAM, scratchpad, registers,
  GTE), as every C# routine here; on once verify reads 0 over a session in `fdat02`
  standing, turning and walking, and with a creature close (the shell's `view`).
- **Records** for every subdivided packet, each corner's depth from its `RTPT`
  `SZ`; and, **only if unit 2 measured the near map's address-map coverage as
  low**, publish each subdivided corner's fraction with `GteVertexMap.Publish` as
  the packet is written (Verdite2's HUD publisher is the shape).
- **Done when**: the Z-buffer probe's triangles tested rises by the near map's
  share; verify 0; the user judges the Z-buffer as a whole, and it goes on.

#### Unit 4, the transcription: verified

- `patches/NearPath.cs` and `patches/NearPathDivide.cs` (opencode, 4013 lines):
  `func_8003AB04`, `func_800366A8`, libgte's division bodies `func_80074D90`,
  `func_80075190`, `func_800756B0`, `func_80075B50` and their emitters
  `func_80075104`, `func_80075618`, `func_80075AB4`, `func_800760B4`, **copied
  literally from the recompiled C#**, registers kept in `CpuContext`, calls among
  the ten made direct. `KF3_NEARPATH=0|1|verify` (and `_MAP`, `_MODELS`);
  Testing ▸ Routines ▸ *Near path*. Recompiled by default.
- **A reconstruction did not converge**: the agent's first version, rebuilt from
  a reading with shared builders, mismatched on 30-43% of calls through two rounds
  of fixes (it emitted subdivided faces the routine culls: the packet cursor ended
  0xA0 or 0x140 bytes further on). The literal copy verified at the first run.
  For a routine whose recompiled form exists, transcribe it, then simplify under
  verify.
- **Verify reads 0** for `func_8003AB04` over a session turning and walking in
  `fdat02` (about 2000 calls a window), with `KF3_POLYASM` and `KF3_STAGE15` also
  verifying, 0. **`func_800366A8` was not called** in that session: the models'
  near submit needs a model close to the eye, and it has not run under verify.
- Uncapped, standing, interleaved: 962, 997 fps recompiled; 1061, 1071 in C#
  (about 7% more). These runs read about 990 against 1386 earlier in the day with
  the same switches; a build without the near path's hooks read the same 990, so
  that is the machine, not the code. Compare only runs taken together.

#### Unit 4, the records: measured, not judged

- The division bodies keep each corner's `SZ` from its `RTPT` in a host-side table
  keyed by the 0x18-byte stack record, and each of the four emitters seals its
  packet as `PolyAssemblerDepth` does (or `NoDepth`). Guest memory and the GTE are
  untouched: verify still reads 0. Records only with the near path in C#;
  `ZBuffer.SyncSource` turns recording on when either the bulk assemblers or the
  near path is C#.
- `KF3_ZBUFFER_PROBE=1`, standing in `fdat02`, 144 fps: near path recompiled,
  38304 records a second, all found, **60.4% of triangles tested**; in C#, 43920,
  all found, **74.4%**. The difference is 39 packets a frame, the near map's
  share (unit 2's table: 40).
- Everything on together (24-bit, no dither, perspective, sub-pixel, Z-buffer,
  near path in C#), after turning and walking: 132768 records a second, all
  found, **90.6% of triangles tested**; 144.0 fps at 14.9 ticks/s.
- What keeps painter's order: the HUD's models (16 packets a frame), the sky (45,
  drawn first), the arm, the overlays' sprites, and the blended and front-table
  variants and the models' near submit wherever they draw. Model depth is two
  bits coarser than the map's. **Not judged by eye.**
- **Corrected 2026-10-05**: "all found" was true of the records written, but the
  near map wrote one for only about half its packets. Its original corners come
  from the vertex cache (screen word at record `+0x10`, `SZ` at `+0x14`) and were
  never noted, so a sub-polygon touching one had no record and kept painter's
  order. `NearPath.NoteCorners` notes them at each division entry: unrecorded near
  map packets fell from 49% to 4.6%, every one left having a corner at or behind
  the eye (`SZ` 0). See "Seeing through doors" in `docs/GPU_RENDERER.md`.

### Unit 5: widescreen

Its own document: `docs/WIDESCREEN.md`.

### After this

The blended and front-table variants (`func_80037BEC`, `func_80038844`) and the
sky's assembler (`func_80039428`) in C# once a scene calls them; per-pixel
lighting and even fog from the same records (`0048`, `0049`); the shared fill
extracted into Verdite Core (Verdite2's `docs/SHARING.md`).

## Splitting it across opencode agents

As unit 3 of smoothing did (`docs/SMOOTHING.md`, "Using opencode agents", and the
orchestrator's memory): a worktree per editing agent with `generated/` and the cue
linked in and Verdite2's files copied into its `scratch/`, disjoint files, at most
five at once, run in the background with `~/.claude/scripts/opencode-run.sh`
(the wrapper's own shell is capped at ten minutes), and only the orchestrator runs
the game. A transcription with a verify is a good agent task; a reading of the code
is not, until it is checked.

- **One wave can hold units 1 and 2 and the transcription of unit 4**: `TrueColor`
  + `NoDither`; `Perspective` + `Subpixel` (the fractional cull touches
  `PolyAssembler*.cs`, so that agent owns those files); and the near path's six
  routines in new files of their own, with verify, records left out.
- Unit 3 touches `PolyAssembler*.cs` again, so it follows unit 2's merge; the
  near path's records follow both.

## Rules carried from Verdite2

- **A picture switch ships off until judged**, and the write-up says which of
  "mechanism measured" and "picture judged" it has.
- **A record is believed only when its seal matches** (command word, first and
  last vertex words); otherwise no depth, never an old one.
- **The recompiled result stands under verify**, so the address map is checked by
  its own probe, not by verify.
- **PGXP stays off**: it bought no coverage in Verdite2 and cost a fifth of the
  frame rate. Every C# routine here already falls back to the recompiled one while
  PGXP's CPU tracking is on.
- Check every change with `KF3_FPS=144 KF3_FPS_PROBE=1`: 144.0 fps at 15.0 ticks/s.

## Retained GPU path (2026-10-04)

[GPU_RENDERER.md](GPU_RENDERER.md) tracks the final source mesh/pose/instance
path, independent of packet depth capture. The shared depth-linear cue accepts
this game's quarter-depth near/far formula; 270 actual Radeon shader cases pass
exactly. Its 32000 cutoff, truncation and 7951 maximum belong to this cue; the
existing reciprocal curves remain available for other games. Complete native
light products, near subdivision and visual judgement remain open gates.
