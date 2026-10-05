# Handoff: render distance, and a fade where things now pop in

Written 2026-10-05, before any implementation. Facts under "Known" are measured or
read from the source named; everything else is to be found.

## The goal

Most of this game is outdoors, and the map stops drawing at a radius each area
sets. Past that radius you see sky where land should be, and land appears at the
edge as you walk. Two enhancements, each behind a switch, **off by default until
the user has judged them**. Off must give exactly today's picture.

1. **Render distance**: draw the retained map out to a distance the user picks,
   further than the game's own radius.
2. **Fade-in** (the user's idea): instead of popping in at the edge of what is
   drawn, map and models fade in over a band of distance. It applies at whatever
   the edge is: the game's own radius with (1) off, or the extended one with it
   on.

Both are for the **retained GPU renderer** only. The packet path
(`KF3_GPU_WORLD=0`) keeps the game's reach, and the guest's grid is never
changed.

## Known

### What the game draws, and how far

From `docs/WIDESCREEN.md` ("The cull cone") and `docs/GAME_INTERNALS.md`:

- `func_80034BF4` classifies every cell of a **25x25 grid at scratchpad
  `0x1F800120`, centred on the camera**, so at most 12 cells each way. Bit `0x02`
  is what the map walk (`func_8003BFD0`) draws; `0x04` marks the near assembler.
  The window's origin is at `0x801AEC74`/`78`.
- A cell is drawn if it is inside the cone and its squared distance from the
  window's centre is under `T5`: the **u8 at `0x801AEAE9`, squared**, scaled by
  `cos(S5/2)` once the half-angle `S5 >= 600` (looking up or down). Cells under 5
  from the centre are always drawn (`0x1E`); `0x08` (beyond `T5`, under 256) is
  not drawn. Then one of two occlusion floods (`func_800345F4` / `func_800348F4`,
  by yaw) only clears cells.
- `patches/CullCone.cs` already re-runs the classifier in C# (pre hook on both
  floods) to widen the half-angle for widescreen. That is the code to reuse for
  "inside the cone" past the radius.
- **The radius byte, read at arrival in the saved 28-area RAM corpus
  (`/tmp/verdite3-gpu-reference/shadow-corpus/areaNN.ram`)**: **9 cells (18,432
  units) in areas 3 and 12-27; 13 in areas 0-2 and 4-11**, cut to 12 by the
  window. Whether the byte changes inside an area is not known.
- **Fog** (`GAME_INTERNALS.md`, "The depth cue's pair" and "Records across a tile
  edge"): per tile record; pairs range from `(6000, 14000)` to `(18000, 24000)`,
  and the weight reaches 4096 at view depth = far. The far colour is set once,
  in `func_80035394`, to `(5, 5, 5)` (GTE control 21-23 = `0x50`). The retained
  shader fogs to black. Areas 21 and 25 have only `(18000, 22000)`, so at the
  9-tile edge the land is hardly fogged at all: **the cut is a lit edge, not a
  black silhouette**. Elsewhere, fully fogged land is a near-black silhouette
  against an unfogged sky (the sky submitter `800400AC` ran in 13 of 28 areas),
  and the silhouette also ends at the edge.
- The map walk's half routine `func_8003BB04` has a far gate (`0x8018FAD4 == 1`
  and `0x8018FAEA` set: a mesh past half the table is skipped). Added halves never
  go through it. Note what it does, in case far meshes were meant to be skipped.

### What the retained renderer already does

From `tools/RecompOne/RecompOne.Runtime/Gpu` and `patches/`:

- **The whole 80x80 map is on the GPU** (`RetainedMap`, `RetainedScene.SetStatic`,
  chunks of 8x8 tiles).
- **The main view draws only the halves the game's walk submitted**:
  `RetainedMap.Submit` calls `RetainedScene.NoteHalf` (`patches/RetainedMap.cs`),
  which writes 255 into `Frame.MainHalves` (160x80, index `(z * 80 + x) * 2 +
  upper`). `BeginWorldMain` uploads it to `uHalves` with `uHalfGate = 1`.
- **A partial weight is already a dither** in the main view: in `WorldVs`, weight
  0 is not drawn, and anything else sets `vFade = weight / 255`. `PrimFs`
  discards by a 4x4 ordered dither when `vFade < 1`. No blending, depth kept.
  But `NoteHalf` only writes 255, and `MainHalves`' comment says "nothing grows or
  fades". **A small runtime API is needed** to note a half at a weight, or a
  per-pixel fade (below).
- **The normal pass does not dither**: `WorldNormalVs` drops weight 0 only, so AO
  and normals would see a fading half in full. Its fade must match the colour
  pass's.
- **Vertex cost is already paid** for every map chunk in the view frustum:
  `CullChunks` (`GlRetained.cs`) tests chunk boxes against the frustum only.
  There is no distance test, and its fog bound is 0 for curve 5. The vertex
  shader then drops ungated halves. Extra distance costs mostly fragments and
  overdraw. Not measured.
- **Depth limit: 65,536 units of view depth.** `PrimFs` writes `gl_FragDepth =
  vDepth = z / 65536`, so anything deeper sits on the far plane: no order among
  itself, and AO counts it as no surface. The fog's exact integer path stops at
  65,535 as well. This is 32 tiles of view depth. With heights up to `255 << 7`,
  cap the horizontal radius at about 30 tiles, and make the fade reach 0 before
  the depth limit. Raising the limit is a shared-runtime change to every pass
  that reads that depth: not part of this unit.
- **Models** (`func_80040AE4`, the model walk) are submitted only for cells the
  grid lit (its page bitmaps at scratchpad `+0x124` / `+0x270`). Creatures past
  the radius are never submitted, and may not be simulated either. **Models stay
  at the game's reach in this unit**; the fade still applies to them at that
  reach.

### Verdite2 (`~/Desktop/KFII-PC`; method only, never its addresses)

- `patches/RenderDistance.cs` and "Render distance" in its `docs/WIDESCREEN.md`.
  Its grid is a 24x24 trapezoid and it draws through guest code, so its cell
  arithmetic and s16 reach do not apply. What applies is its rule for adding
  cells: "lit only where a neighbour nearer the camera is lit and the eye's level
  has a half there, so the game's flood is continued outward rather than
  replaced". That keeps far land from being drawn behind cave walls.
- `patches/ReflectionReach.cs`: halves "grown, held, fading" over time. That is a
  time-based fade, the option for pops a distance fade cannot cover (below).

## Design to start from

**Selection** (render distance). Inside the game's radius, the game's grid
decides, including what its occlusion flood cleared. Past it, out to `R` tiles
(horizontal distance from the camera), the port adds halves whose tile box meets
the view frustum. That includes the ring inside the window but past the radius
(cells 9-12 in radius-9 areas). Start without an occlusion rule and measure.
Bring over Verdite2's flood continuation only if far land shows through walls or
costs too much. Add halves on the CPU after the walk (a post hook on
`func_8003BFD0`, or before the frame's draw), written only into
`Frame.MainHalves`, never the guest grid.

**Fade**. Per pixel, by **horizontal** distance from the camera, so turning on
the spot never fades anything: `fade = clamp((edge - d) / band, 0, 1)`, through
the existing `vFade` dither. Map fragments need their world XZ (`inWorld` for
static corners). Models need theirs from `modelVertex`. Use one rule for both, so
a creature and the ground under it fade together. The edge is `R` with render
distance on, otherwise the game's radius from the byte above (`T5`'s root, in
cells, as the game measures it), pulled in slightly so nothing is still visible
where the game stops submitting it. Apply the same discard in the normal pass.
Fading per half (weights in `MainHalves`) is the cheaper fallback, but it steps a
whole tile at a time.

What a distance fade does not cover: things that appear because the occlusion
flood or the cone changed as you turn or round a corner. Note those and leave
them, or add a short time-based fade later (Verdite2's `ReflectionReach`).

## To find first

1. Whether `0x801AEAE9` changes inside an area (scripted, by position, by
   time), and where the game writes it.
2. How the game measures the radius (cell distance from the window's centre, as
   integer cells, before or after the cos scale), so the fade's edge matches it
   exactly in both radius classes.
3. Which areas are outdoors in practice. The sky submitter's per-area counts in
   the scene census (`KF3_SCENE_CENSUS=1`) are a start.
4. What the far gate in `func_8003BB04` is for, before halves past it are drawn.

## Suggested order

1. Document the findings above in `docs/GAME_INTERNALS.md` (the radius byte, its
   writers) and a new section of `docs/WIDESCREEN.md` or `docs/GPU_RENDERER.md`.
2. Shared runtime (`tools/RecompOne`), **in its own commit, pushed to the fork's
   `checkpoint/retained-depth-probes`** with `VERDITE_FORK_BRANCH=... bash
   scripts/setup_tools.sh --push-fork --no-build`: a way to note a half at a weight
   (or a per-pixel fade: uniforms for the camera XZ, the edge and the band), and
   the same fade in the normal pass. Neutral when off. Next patch number `0089`.
3. Game side: the selection, the switches and the controls. Suggested:
   `KF3_RENDERDIST=<tiles>` (unset or the game's radius = off; cap about 30),
   `KF3_RENDERDIST_FADE=<tiles>` (band; 0 = no fade, which works with render
   distance off), `KF3_RENDERDIST_PROBE=1`. Keys `kf3.renderdistance` and
   `kf3.renderdistance.fade`. Video controls in `patches/SceneFeatures.cs` with
   English, pt-BR and es-419 labels, and lines in `docs/ENV_VARS.md`.
4. Measure, without screenshots (see below).
5. Hand it to the user to judge.

## Measure

- **Off**: no half added, every weight 255, no fade, identical counters on the
  same tour. The source probe (`tools/scene-probe`, 7,092 assertions) and
  `scripts/shader_probe.py` still pass. Rebuild the probe after a runtime change:
  it keeps its own copy of the runtime DLL.
- **Fixtures**: the selection (a synthetic map and camera; the game's cells kept
  as they are, the ring and the far halves added, nothing behind the camera or
  past `R`), and the fade on the GPU (distance to weight, horizontal only, and 0
  before the depth limit).
- **Live**: a probe line with halves added and faded per frame and the furthest
  view depth drawn. A 28-area tour at the largest setting must keep **0 missed**
  retained draws on the `[KF3] retained scene` line. Drive it with
  `KF3_SHELL=1 KF3_SCENE_DRIVER=1` through the shell's `warp <n>`, `view` and
  `gpu` commands; `scripts/model_mask_tour.py`'s `cmd` and `wait_area` helpers
  do the waiting. Run from a copied build output with copied cards and
  `interface.ini` (`docs/DEVELOPMENT.md`, "Retained scene verification").
- **Cost**: GPU frame timers (runtime `0084`) and uncapped fps (`KF3_FPS=off
  KF3_FPS_PROBE=1`) at fixed outdoor views (the shell's `view` override) in a
  radius-9 area (21, 25 or 27) and a radius-13 one, at the game's radius, 16, 24
  and 30 tiles.

## For the user to judge

Distant land and skylines instead of sky; the edge fading rather than popping,
walking towards it and away; creatures fading in rather than popping; whether
far geometry shows gaps or backs the designers never meant to be seen; caves and
doorways (far land through walls); performance outdoors.

## Rules that apply

- No screenshots or window capture. Measure with counters and fixtures, then ask.
- Nothing from Verdite2 is a fact here until it has been measured on this disc.
- Shared-subtree edits in their own commits, pushed to the fork.
- Commit messages state the finding; findings go in `docs/`.
