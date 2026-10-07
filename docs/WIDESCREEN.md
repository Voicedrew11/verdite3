# Widescreen: the margin, the tints, the cull cone

Verdite2's widescreen, ported. Its write-up is Verdite2's `docs/WIDESCREEN.md`
("Widescreen: the margin, the HUD and the three culls"); this file says what
carried over, what is different here and what was measured. Started 2026-10-02.

## Status

**Mechanism measured, picture not judged. Off (4:3) by default**, like every
picture switch here: `KF3_WIDESCREEN=16:9` (also `16:10`, `21:9`, a number,
`off`), Settings ▸ Testing ▸ Aspect, or the `aspect` shell verb. A set variable
wins over the kept `kf3.widescreen.aspect`.

| piece | Verdite2 | here |
|---|---|---|
| the margin | the runtime's (`Display.WideAspect`) | the same, shared fork |
| the present latch cleared on an executable load | `Widescreen.cs` | ported (`open`, `game`, `end`) |
| full-screen tints stretched | `Widescreen.Stretch` | ported, by shape |
| the HUD anchored | moved by its records, off | ported, by its records, off; DISPLAY ▸ HUD AT EDGES; see below |
| the tile cone widened | `CullCone.cs` | a different build; see below |
| the view-space clipper | `ViewClip.cs`, a no-op below 8:3 | **no clipper in this game**; the near path's libgte division has a screen test, widened with the aspect (`NearScreen.cs`) |
| the primitive buffer | ran out; moved to 4 MB (`PrimBuffer.cs`) | measured, see below |
| the world behind menus and messages | `MenuWorld.cs`, on | ported, on; the pass lives in the frozen frame's store, not a moved buffer; see the last section |

## The margin, the latches and the tints

`patches/Widescreen.cs`. The runtime renders the margin; the patch sets the
aspect, clears the backend's margin-content latches when an **executable** loads
(never for the `fdat` area modules, which are `GAME.EXE` still running; Verdite2's
"The present gate" says why), and widens any primitive that is semi-transparent,
flat and spans the clip rectangle's full width, setting `GpuHle.PortWidenedPrim`
so the latch does not count it. `KF3_WIDESCREEN_EFFECTS=0` is the comparison. At
4:3 no `RenderPrimEvent` listener is attached.

The HUD at the screen edges is in its own section below. Verdite2's `DrawOTag`
replacement is not ported: it fed only the anchoring Verdite2 guessed at from the
ordering table, which its records replaced.

Measured, 16:9, slot 1, `fdat02`, turning and walking, 144 fps
(`KF3_WIDESCREEN_PROBE=1 KF3_PRESENT_PROBE=1`):

- **13-35% of primitives reach the margin** in the area, per 2 s window (Verdite2:
  about 25%). The game culls per tile and by depth and leaves the screen edge to
  the GPU here too, so the margin has a picture to show.
- Every present after the area's first frames picks the wide target
  (`wide 288, plain 0, vram fallback 0`); the title and boot are `vram fallback`,
  as in Verdite2.
- **Tints**: 74 and 241 stretched in the two windows of the area's fade-in, then
  **0 in every window of play**, the negative control that no world geometry is
  caught.
- 144.0 fps drawn at 15.0 ticks/s; no exceptions.

**To judge by eye**: the margins in play; the fade-in and a death fade or damage
flash reaching the window's edges; the title and menus (pillar-boxed, by design);
the HUD at its authored place, and at the edges (below).

## The HUD at the screen edges

Ported 2026-10-06 from Verdite2's "The HUD is moved by its records": the records
a HUD drawer reads have their screen X moved out by the margin for the call and
put back after it, left of the screen's middle (160) to the left and right of it
to the right. Nothing downstream is told, and a menu's primitives are never
touched. Verdite2 has one HUD drawer; this game has two, both stage 15's (and
`MenuWorld`'s pass, which draws through the same calls):

| drawer | records | X | drawn in `fdat17`, slot 1 |
|---|---|---|---|
| `func_8003C35C`, the HUD models (call #9) | `0x80081C20`, `0x24` each | `+0x10`, the translation; orthographic, so the screen X of the model's middle | record 1, the compass, at (290, 32) |
| `func_80041E68`, the sprites (call #10) | `0x800819B4`, `0x14` each | `+6`; the side is decided by the middle, `X + w/2` (`w` at `+4`) | records 15-29, the HP/MP panel and its digits, at X 5..80 |

The bottom message box's drawer, `func_80041D9C` (call #11, records from
`0x80081928`), is left alone: the box is centred, and moving its halves apart
would tear it. The record layouts are "The HUD's sprites" in
[GAME_INTERNALS.md](GAME_INTERNALS.md).

**A setting, off by default**: DISPLAY ▸ HUD AT EDGES on the game's page, under
the aspect, dimmed at 4:3; Testing ▸ Picture in the Settings window. Kept as
`kf3.widescreen.hud` (Verdite2's key under this prefix); `KF3_WIDESCREEN_HUD`
wins for a run. It takes effect on the next frame the HUD is drawn. To make room
under the aspect, PER-PIXEL LIGHT moved from PICTURE to WORLD (since 2026-10-07 the pages are DISPLAY, GRAPHICS, WORLD and GAMEPLAY, `docs/SETTINGS.md`).

Measured 2026-10-06, isolated run directory, slot 1, `fdat17`, 16:9 (margin 54),
`KF3_WIDESCREEN_PROBE=1`, driven with the shell:

- **The records are put back**: peeked between frames, the panel's frame (record
  29) reads X 5 and the compass 290 with the anchoring off and on, and after the
  menu.
- Turned on through the shell's `settings` session (as the game's page does):
  **3.1% of packet primitives reach the margin** before, **93.8%** after (the
  retained renderer draws the world, so the packets left are mostly the HUD: 2
  model records and 30 sprite records moved on each of about 340 calls a second).
- **With the menu open the game hides every HUD record** (both tables drawn
  "none"), as in Verdite2, so the menu has nothing moved and nothing to flash.
- Save wrote one line, `kf3.widescreen.hud=1`; the next boot printed `HUD at the
  edges`, and `KF3_WIDESCREEN_HUD=0` over it `HUD in its 4:3 box`.

**To judge by eye**: where the panel and the compass land at 16:9 and 21:9 (the
panel 5 px from the new left edge, the compass 30 px from the right, their 4:3
insets), and a menu's first and last frames.

## The cull cone: a classifier, not a trapezoid

Surveyed 2026-10-02 (an opencode agent, read-only, checked by hand). Verdite2
draws its cone from a seven-pair table with four Bresenham edges and a scanline
fill, which is why widening it there meant recording corners and ORing a second
trapezoid. **Verdite3's `func_80034BF4` has none of that.** It computes the cone
per cell:

- the camera's pitch (`0x801AEC5C`) and yaw (`0x801AEC5E`); a half-angle
  `S5 = shaped(pitch) + 0x1B8`. **440/4096 of a turn is 38.7°, which is
  atan(160/200)**: the 4:3 screen's half-angle at the GTE's `H = 200`, exact.
  Looking up or down opens it;
- two edge rays at `yaw + 0x400 ± S5`, as incremental half-plane tests;
- the draw radius squared `T5`: the u8 at `0x801AEAE9` squared, scaled by
  `cos(S5/2)` once `S5 ≥ 600`;
- every cell of the 25×25 grid at scratchpad `0x1F800120` written by distance
  from the window's centre: under 5 → `0x1E` inside both half-planes, under `T5`
  → `0x1A` inside, under 256 → `0x08` regardless, else 0. Bit `0x02` is what the
  map walk draws, `0x04` the near assembler;
- a five-cell cross written when the u8 at `0x801B25E5` is `0x11`, the offset
  words (`0x801AEC6C`..`78`), then one of two occlusion floods (`func_800345F4`,
  `func_800348F4`, by yaw), which only clear cells.

Because every cell is classified, **Verdite2's dropped-row failure cannot happen
here**: a cell off the grid is never a boundary another row depends on.

### Widening it: the half-angle opened, the radius kept

`patches/CullCone.cs`. A pre hook on both floods, where the stock grid is complete
and nothing has been cleared yet, recomputes the classifier in C# from guest
memory (the game's own `rsin` and `func_80076DA0` run on a context of their own,
below the live stack) and runs it again with the half-angle opened to
`atan(Factor · tan S5)`, `Factor = (320 + 2·margin) / 320`, only while
`S5 < 1024`. The draw radius stays the stock one. **A cell is only added**: one
the stock grid left at 0 that the wider wedge marks gets `0x1E` or `0x1A`, and the
epilogue's crosses (the eye's, always lit; the one behind it, cleared unless
`0x801B25E5` is `0x11`) are never touched. The flood then shadows added cells
like any other. At 4:3 the factor is 1 and the hook returns at once.
`KF3_WIDESCREEN_CULL=0` is the comparison, `=<factor>` pins one.

**The oracle is the game's own grid.** `KF3_WIDESCREEN_CULL_PROBE=1` runs the C#
classifier at the stock angle every frame and counts the cells where it differs
from what `func_80034BF4` wrote. The first transcription (an opencode agent's)
read 1,500-52,000 a window; two bugs, found by reading it against the MIPS: the
row step's `S1` was missing its `- A3`, and the epilogue's second cross was not
excluded. After: **0 mismatches in every window**, at 16:9 level and at 21:9
looking up and down (stock `S5` 440 to 839, so both arms of the radius). `=2`
prints the grid as a map once a window.

Measured, slot 1, `fdat02`, 144 fps:

| aspect | `S5` → widened | tiles lit before | added a frame |
|---|---|---|---|
| 16:9 level | 440 → 534 | 156-170 | 27-38 |
| 21:9 level | 440 → 620 | 162 | 64 |
| 21:9 looking up or down | 620-839 → 772-916 | 196-250 | 12-48 |

Pacing held at 144.0 fps and 15.0 ticks/s. **Not judged by eye**: whether the
margins still fill in and empty at the edges as you turn.

## The near path's screen test

Reported by eye: at a wide aspect, geometry popped in at the bottom corners and
on walls close to the camera, with smoothing and the mouse lead off, and
`KF3_WIDESCREEN_CULL=1.6` did not fix it.

The earlier claim that this game has nothing to port from Verdite2's clipper was
wrong. This game links no `Clip3FTP`/`Clip4FTP`, but near faces go through
libgte's four polygon-division entries. Cells with grid bit `0x04`, within
distance² < 5 of the window centre, are drawn by the near assembler
`func_8003AB04`, and the models' near path is `func_800366A8`; both reach
`func_80074D88` and `func_800756A8` (triangles), `func_80075188` and
`func_80075B48` (quads), whose C# twins are `DivTri`/`DivTri2`/`DivQuad`/
`DivQuad2` in `patches/NearPathDivide.cs`. Their entry test drops the whole face
when every corner's SZ is under H/2, or every corner's X is past `OFX ± pih/2`,
or every corner's Y is past `OFY ± piv/2`. `pih` and `piv` are words at `block+4`
and `block+8` of a stack block the near assemblers fill per face with the
immediates `0x140` (320) and `0xF0` (240), at seven sites. So a face wholly in
the margin was dropped. The far assembler `func_80039D50` has no screen test,
which is why the margins otherwise fill.

The fix: `patches/NearScreen.cs` pre-hooks the four entries, and the C# near path
calls the same `NearScreen.Widen` at the top of its four twins (they are called
directly, so the hooks do not fire there). It writes 320 + 2 × margin into
`block+4` when it reads 320 there and the aspect is wide: 428 at 16:9, 560 at
21:9. The vertical 240 is unchanged. Because the game writes 320 again for every
face, 4:3 writes nothing and an aspect change needs no restore. No setting: it is
part of the aspect the player picked (see Verdite2's "Three checkboxes that were
not choices"). `KF3_NEARSCREEN=0` is the comparison; `KF3_NEARSCREEN_PROBE=1`
re-evaluates each face's corners at the entry and prints, every 2 s, faces, the
SZ/right/left/bottom/top rejects at 320 and how many X rejects the wide width
keeps.

Measured, `fdat02` from slot 1, a scripted 75 s walk turning left and right (same
script every run), 144 fps:

| run | width | X rejects (right + left) | kept by the wide width | packets/frame at rest |
|---|---|---|---|---|
| 4:3 | 320 | 22,342 + 23,900 | 0 | 113 |
| 4:3, `KF3_NEARSCREEN=0` | 320 | 22,352 + 23,887 | 0 | 113 |
| 16:9, `KF3_NEARSCREEN=0` | 320 | 22,409 + 23,962 | 0 | 113 |
| 16:9 | 428 | 22,230 + 23,669 | 8,916 (19%) | 125 |
| 21:9 | 560 | 22,415 + 23,963 | 15,618 (34%) | 125 |

The SZ rejects were 0 in every run; the vertical rejects did not change with the
width. `KF3_NEARPATH=verify` at 16:9 with the fix: 0 RAM, scratchpad, register
and GTE mismatches over ~2,016 calls a window. `KF3_PRIMBUF_PROBE=1`: peak 8,228
bytes at 4:3, 8,708 at 16:9, 8,868 at 21:9 of 106,496; 0 frames ran out and 0 near
halves starved in every run. Pacing 144.0 fps drawn at 15.0 ticks/s.

The cone was checked as the second suspect and ruled out. Unlike Verdite2, the
window is built round the camera's tile (it reaches 20 cells ahead and 4 behind;
see "The draw radius and the window" in `GAME_INTERNALS.md`) and the occlusion flood's rays start at the
camera tile (`func_800345F4` / `func_800348F4`, called from `func_80034BF4`), and
the `KF3_WIDESCREEN_CULL_PROBE=2` map shows the wedge's apex about four cells
behind the camera, so every cell round the camera is already lit. A Verdite2-style
rescue (force-lighting a radius-3 disc round the camera after the flood) was built
and measured: it lit 33-35 cells a frame, all of them cells the flood had
cleared, and changed neither the near faces passed (43,270 vs 43,288) nor
packets/frame (125 vs 125). Not kept.

**Mechanism measured, picture not judged**: whether the corners and the near
walls still pop in is the user's to look at.

## Render distance, and a fade where things pop in

`patches/RenderDistance.cs` with runtime `0089` (`DistanceFade`). Built
2026-10-05 from `RENDER_DISTANCE_HANDOFF.md`; the models since 2026-10-06, with
runtime `0098` (a model's own fade). **Measured, not judged; both off by default.**
The retained renderer only: the packet path and the reflections keep the game's
reach, and the guest's grid is never changed.

- `KF3_RENDERDIST=<tiles>` (Video ▸ "Draw past the game's distance",
  `kf3.renderdistance`): draw the map and the models out to that many tiles, up to
  30. Below the game's own edge it does nothing. `KF3_RENDERDIST_MODELS=0` keeps
  the models at the game's reach.
- `KF3_RENDERDIST_FADE=<tiles>` (Video ▸ "Fade in at the edge of the view",
  `kf3.renderdistance.fade`): fade the map and the models out over that band
  before the edge, which is the render distance when it is on and the game's edge
  when it is off.

### The game's edge

The game draws a tile when its whole-tile offset from the eye's tile is inside the
radius, it is in the window and it is in the cone ("The draw radius and the window"
in `GAME_INTERNALS.md`). That edge is stepped, and moves with the eye's tile and
the yaw (the window reaches ahead). A fade that ends at a fixed distance from the
eye must end where no left-out tile can be, from any yaw and any place in the
eye's tile, or a tile would vanish while still partly opaque. `GameEdge` finds that
distance by brute force over the classifier (64 yaws, 4x4 places, a grid of
-8..32), cached by `T5` and the cone's half-angle rounded up to 32:

| `T5` | when | edge |
|---|---|---|
| 169 | radius 13, level | 11.66 tiles |
| 143 | radius 13, looking up or down a little | 10.63 |
| 132 | radius 13, looking down further | 7.81 |
| 81 | radius 9, level | 7.81 |
| 68, 63 | radius 9, looking up or down | 7.07, 6.71 |

The fixture checked each against the exact nearest left-out tile for that run's
yaw from 9x9 places: never nearer than the edge, and equal to it at the worst yaw.
Looking up or down far enough to scale `T5` pulls the edge in, since the game
draws less there; level and slightly pitched views keep it constant, and turning
never moves it.

### Selection

Each frame (`GpuWorld.Begin`, after the grid and before the walk), with the
render distance past the game's edge, every half whose tile is outside the game's
window-and-radius, whose tile comes within the distance, whose mesh exists, which
the far gate would not skip, and whose box (the mesh's own height range and reach,
`RetainedMap.MeshYMin`/`MeshYMax`/`MeshReach`) meets the widened frustum is written
255 into the frame's `MainHalves`. Inside the radius the game's grid decides, its
flood included. There is no occlusion rule past the radius (see below).

### The fade

Per pixel in `PrimFs`, `clamp((edge - d) / band, 0, 1)` with `d` the horizontal
distance from the camera, through the ordered dither the half gate already used,
and the same in `NormalFs` for AO and the surface buffer. Past 62,976 of view depth
it also falls to 0 by 65,024, so nothing drawn reaches the 16-bit depth limit.
The sky and the arm never fade.

### The models

The model walk (`func_80040AE4`, in C# as `ModelWalk`) draws a creature, object,
effect or billboard when the grid byte under it, through `func_80040694` or
`func_80040708` ("The models" in `GAME_INTERNALS.md`), ANDs its mask. With the render
distance past the game's edge, `RenderDistance.ModelBits` answers too: a tile the
query covers that is outside the game's window and radius, within the distance, with
the model's box (its tile half a tile wider each way, four tiles above its position
and one below) meeting the frustum, reads `0x1A`, the classifier's "in the cone,
inside the radius, not near". Inside the game's reach its grid decides, flood
included, as for the map. The submitter's far limit (an instance's `Far`, the
ordering table's end: a face at 32,768 or more of view depth was dropped) is lifted
for the walk's models while this is on; it only ever cut models past the game's
reach. Only the C# walk does this (`KF3_MODELWALK=1`, the default).

- **Nothing is loaded for them.** The walk's page bitmaps are the game's on-demand
  loader: a marked model or texture page is requested from the CD if missing and
  pinned if present, and the rest are released. A model admitted only by the render
  distance marks nothing, and is drawn only when its model is resident (the walk's
  own test) and its texture pages are loaded (`0x801B0A2C`); otherwise it is
  refused. Marking them made the game stream far models: the first warp crashed in
  the CD loader. So a far object whose model or texture is not in memory still
  appears at the game's edge, once the game itself asks for it.
- **Creatures fade at their spawn distance.** A creature exists only while awake,
  within its type's sleep distance of the player (17 tiles for nearly all), and
  wakes 16 to 17 tiles away ("Creatures wake and sleep by distance" in
  `GAME_INTERNALS.md`). With the distance past that, one would appear out of nothing.
  So when its wake distance (definition `+0xA`) is nearer than the edge, a creature
  is faded out at it, over the fade's band (or cut there with no band), by its
  horizontal distance from the camera, through runtime `0098`'s per-model weight
  (`ModelInstance.FadeOut`, dithered as the distance fade is, in the colour and
  normal passes). Creatures past 16 tiles are rare: they wake there and either come
  nearer or go back to sleep at 17.
- **The drawn bit**: the walk sets `0x80` in an object's `u8[+3]` when it draws it,
  and stage 3 reads it to turn some objects towards the player; a far object drawn
  now gets that turn as a near one does.

### Measured

- **Fixtures** (`tools/scene-probe/RenderDistanceFixtures.cs`): the classifier as
  above; the selection on a synthetic map (the game's tiles never added, the ring
  past the radius and the far tiles added, nothing behind the eye, past the
  distance or empty, the far gate honoured); the fade exported for the GPU. The
  source probe passes 5,632,542 assertions (the classifier sweep is most of them).
- **GPU** (`scripts/shader_probe.py`, RX 9070 XT): the packet programs still link
  with the new inputs; 360 fade cases match `DistanceFade.Weight` within 5e-7, and
  112 dither cases match `DistanceFade.Dropped` exactly.
- **Live, 28 areas at 30 tiles with a 3-tile fade** (`scripts/render_distance_tour.py`,
  four level headings and two pitched at each arrival): **0 missed** of 24,508
  retained draws; 128-501 halves added a frame by area (walked: 36-211); **0** of the
  walk's halves outside the predicted reach; **0** added halves the walk also drew.
  The deepest added box corner reached 65,826, which the depth fade covers.
- **Off** (areas 3, 12, 13, 21, 25): no frame faded, nothing added, 4,384 draws, 0
  missed; every walked half inside the predicted reach.
- **Cost**, uncapped (`KF3_FPS=off`), fixed views at arrival, 16:9 (all at radius
  13 in this run; see "The draw radius and the window"):

  | view | off | game edge, fade 3 | 16 | 24 | 30 |
  |---|---|---|---|---|---|
  | area 21, yaw 0 | 1,345 | 1,350 | 1,267 | 1,164 | 1,082 |
  | area 25, yaw 0 | 1,327 | 1,318 | 1,242 | 1,142 | 1,069 |
  | area 27, yaw 2048 | 1,322 | 1,356 | 1,268 | 1,159 | 1,080 |
  | area 10, yaw 2048 | 423 | 421 | 364 | 360 | 350 |
  | area 0, yaw 2048 | 1,001 | 1,001 | 843 | 578 | 531 |

  The fade alone costs nothing measurable. Area 0 looking back over the map is the
  heaviest: 30 tiles halves its frame rate there, still over 500 fps. The runtime's
  GPU timers (`0084`) have no reader in this port, so no per-pass split.
- **The models** (2026-10-06): the source probe still passes 5,632,542 assertions,
  and `scripts/shader_probe.py` links all four programs (RX 9070 XT) with every
  cue, pose, light, neighbour, fade and dither case unchanged. The tour at 30
  tiles, fade 3, in all 28 areas: **0 missed** of 28,039 retained draws, no crash
  through 28 warps; **0-37 models a frame drawn past the game's reach** by area (0
  in 21 and 25, 37 in area 8); 0-1,677
  refusals for a texture page not loaded over an area's six views (areas 15 and 18
  the most); creatures faded in area 17 (154 times). Off (areas 3, 12, 13, 17, 21, 25): no model
  admitted, no draw missed.
- **Cost of the models**, uncapped, 30 tiles, level at four headings at arrival:
  area 12 971-1,054 fps with `KF3_RENDERDIST_MODELS=0`, 825-951 with the models;
  area 8 605-981 against 489-864. 5-27% of the frame rate, over 480 fps.

### Not covered

- **Occlusion past the radius**: none. Far land may show through cave walls or
  doorways where the game's flood would have hidden it; Verdite2's flood
  continuation is the fix if it does. Areas 13-27 draw no sky.
- **Pops a distance fade cannot cover**: cells the flood or the cone change as you
  turn or round a corner still pop. A time-based fade (Verdite2's `ReflectionReach`)
  would be the next step.
- **Models past the reach that are not in memory**: refused, not loaded (above), so
  they still appear at the game's edge. Loading them is the game's loader's job and
  it was not built for more than its radius.
- **The packet path and `KF3_MODELWALK=0`/`verify`** keep the models at the game's
  reach.
- **Reflections** keep the game's halves.

### For the user to judge

Distant land and skylines instead of sky; the edge fading rather than popping,
walking towards it and away; creatures fading in, at the edge and (past 16 tiles)
at their spawn distance; far objects, and whether any pop in at the game's edge
for a texture not loaded; gaps or backs of geometry the
designers never meant to be seen; caves and doorways (far land through walls);
performance outdoors; the fade pulling in when looking down.

## Radial fog

`KF3_FOG=distance` (Video ▸ World enhancements ▸ *Fog* ▸ *By distance from the
eye*, `kf3.fog` = 2), runtime `0100`, **not the default (by pixel depth); measured;
seen working by the user, the look not yet judged** (2026-10-07). It was a checkbox of
its own beside *Fog from pixel depth* at first; the two were one choice with a
combination that half worked (distance over corner fog is the level rescale's
approximation), so they are one dropdown.

The GTE cues by SZ, the view depth, so a pixel is fogged by how far in front of the
camera's plane it is, not how far from the eye: at the side of the picture, or below
a camera looking down, the same wall is fogged as if nearer. With this game's H of
200, a 4:3 side edge is fogged as at 78% of its distance and a corner at 71%; at
16:9 the side edge is at 68% and a corner at 63%. So the fog moves as the view
turns (a wall darkens as it comes to the centre), and it goes black further out at
the sides than the render distance's horizontal circle (above) does.

With the switch on, the retained world, the packets that carry a depth, the
neighbour blend (`0088`) and the reflection pass's own fog (the murk's colour, a
reflection's longer path) take the cue at the eye's distance instead; the planar
mirror takes it at the mirrored eye's, which is the path through the water. At the
picture's centre nothing changes; off the centre there is more fog than the game's.

### Measured

Seven areas warped to with the scene driver, a level camera at the player, a frame
before and after a turn of 128 (11.25°), the second frame mapped back onto the
first through H; once with the switch off and once on. "Turn" is how a world
point's log brightness moves per unit change of the cosine of its angle off the
view axis (0 is a fog that does not move as you turn); the change is radial minus
depth, in the sum of RGB, at the picture's centre and its outer columns:

| area | pixels changed | change centre / edges | turn, off → on | mean change of a point across the turn, off → on |
|---|---|---|---|---|
| 0 | 8.2% | 0.0 / -28.2 | -0.73 → 0.01 | 28.3 → 18.4 |
| 4 | 9.4% | -2.3 / -10.7 | -0.75 → 0.01 | 19.2 → 14.6 |
| 11 | 1.6% | -2.8 / 0.0 | -0.01 → 0.00 | 14.0 → 13.5 |
| 14 | 17.6% | 0.0 / -9.9 | 0.71 → 0.25 | 24.7 → 7.6 |
| 20 | 6.0% | 0.0 / -1.3 | -1.72 → 0.04 | 14.6 → 9.5 |

Areas 8 and 24 are too dark at their entry for the turn measure (under 9% of the
picture lit); radial took area 8's mean from 20.4 to 17.4. The start area, area 5,
is bright and barely fogged near its start: 0.04 mean change. Cost: 392 → 380 fps
uncapped over area 5's pool (scale 6), about 0.08 ms a frame.

### For the user to judge

In a foggy area, at 16:9: turning in place, whether walls still brighten towards the
edges; the corners of the picture darker than the game's; the edge of the render
distance at the sides; looking down at the floor; water's reflections and murk.

## The primitive buffer

Verdite2's ran out at a wide aspect (`0x19000` bytes a buffer) and was moved above
2 MB. Here the buffer is `0x1A000` bytes (106,496), and the near assembler is
taken only with `0x2800` bytes left, so running low would first show as near
halves drawn by the bulk assembler, unsubdivided. `KF3_PRIMBUF_PROBE=1`, `fdat02`,
turning and walking: **peak 57% at 4:3, 66% at 16:9 with the cone widened, 31%
at 21:9 looking up and down; 0 frames ran out and 0 near halves starved in every
window.** Not moved. One area only: worth re-asking in a busier one.

## Menus and messages draw the world live

Verdite2's `MenuWorld` ("Menus draw the world live" and "Messages draw the world
live" in its `docs/PATCHES_AND_MODS.md`), ported 2026-10-05 as
`patches/MenuWorld.cs`. **On by default**, as in Verdite2; `KF3_MENUWORLD=0` or
Settings ▸ Testing ▸ Picture ▸ "The world live behind menus and messages" is the
comparison. Mechanism measured, **picture not judged**.

### What the game does

The same two shapes as Verdite2, on different addresses.

**Every menu** (the in-game menu, the shops, the save screens; eight callers)
runs on one framework:

| routine | what |
|---|---|
| `func_80027198` | enter: saves both primitive descriptors (`0x80199158`, `0x80199164`) to `0x8009C404`/`0x8009C410`, shrinks them to `0x7400` bytes each from `start`, turns off `dfe` and `isbg` on both draw environments (`0x801A91BC + 0x5C k`, `+0x17`/`+0x18`), and `StoreImage`s the displayed frame (the RECT at `gp+0x178` = `0x8009C38C`, 320×240) to `start + 0xE800`, kept at `gp+0x174` = `0x8009C388` |
| `func_80026FE4` | frame head: flips `0x801AEAE8`, points `0x80199170` and the OT pointer `0x801A9174` at that buffer's, `ClearOTagR(ot, 0x2000)`; it does not clear the 8-entry front table `*0x801A91B8` |
| `func_800270F8` | presenter: `DrawSync`, `VSync`, `PutDispEnv`, `PutDrawEnv`, **`LoadImage` of the stored frame**, `DrawOTag(ot + 0x7FFC)` |
| `func_80027310` | leave: puts the descriptors back and `dfe`/`isbg` on |

The world's buffers are `0x1A000` bytes each, so the shrunk menu buffers and the
`0x25800`-byte store fill exactly the `0x34000` the world's two did.

**A full-screen message** (a sign, a line of dialogue), `func_800441D4(file,
entry)`, called from the script interpreter `func_8005C308`, the item-use
dispatcher `func_8005CBE0` and the action interpreter `func_8005E2D0` (for
example `(6, 0x131)`):

1. `ClearImage` of the RECT at `0x8009C2D8`, the message's 4-bit TIM loaded
   through `func_8001A154` into `*0x80199154` and uploaded by `func_80043B38`
   (texture page `(0x3C0, 0x100)`, CLUT `(0x60, 0x1E0)`);
2. the buffers shrunk as a menu's, and the RECT at `0x8009C2D0` = `gp+0xBC`,
   **(320, 0) 320×240, world texture space**, saved to `start + 0xE800`
   (`0x80199168`, the shrunk desc1's end);
3. `MoveImage` of the drawn frame to (320, 0);
4. the fade `func_80043BB8(0, 12)`, then, if it ran out, a wait spinning on
   `PadRead(1)` with no draw and no `VSync`, then `func_80043BB8(0x50 or the
   press's brightness, -12)`;
5. `LoadImage` of the saved texture space back, and the buffers put back.

`func_80043BB8(brightness, step)` steps once a frame: the frame head
`func_80035630`, two `POLY_FT4`s of the message picture into OT entry 0 at the
brightness (additive at (0x20, 0x20), subtractive one pixel down and right, so the
subtractive one draws first), the moved frame in two halves at `0x80 - b/2`,
`DrawSync`, stage 15's swap `func_80035700`. It stops when the brightness leaves
1..0x77 (returning -1, or -2 once every button was up) or on a press after all
were up (returning the brightness). **A press writes the examine bit** (the mask
at `0x80081876`) into the pad word `0x801B265C`: set if the press was examine,
cleared otherwise; the ran-out path does the same with its last read and leaves
the word alone if nothing was held. The caller's wait does the same.

The menu's paste is 320 wide with no depth, so nothing reaches the margin. The
message's wait draws nothing at all, so the wide target goes idle and the present
falls back to the 1x VRAM frame a few seconds in.

### The patch

- **The menu's presenter is replaced.** Stage 15's drawing half
  (`Stage15.DrawScene`: the camera block from the stored view, the cull grid, the
  arm, the HUD models, both overlay calls, the map, the model walk and the six
  quads) runs into an ordering table, front table and descriptor of its own, the
  front table spliced in after entry `0x1FFE` as the swap does, the table's end
  linked to the head of the menu's, and one `DrawOTag` walks both, with the
  world's `isbg` put back around `PutDrawEnv`. Not called: the texture scroll, the
  bottom message box's stepper, the frame head, the sound slots, the HUD state,
  the swap and the frame gate.
- **Where the pass goes.** The menu's pass lives in the frozen frame's store,
  which nothing reads once the paste is gone: a descriptor, the `0x8000`-byte
  table, the front table, then 120,784 bytes of primitives, more than the world's
  own buffer. Verdite2 used `PrimBuffer`'s relocated second buffer; this game has
  2 MB and no relocation. A session is decided at the enter, which checks the
  shrunk layout and the store's size, and ended at the leave, on the main loop's
  next stage 15 call or on an overlay load.
- **The message's fade is replaced**, loop and return value to the letter, the
  examine bit included, with the world pass in place of the moved frame. The
  store holds the saved texture space, so the pass borrows it: the saved texels go
  back into VRAM first (the walls sample (320, 0); nothing drawn samples the
  copy), the bytes are kept aside for the fade and copied back before it returns,
  for the game's own restore at close. A fade-in that runs out waits for the
  button itself, still drawing, and returns the `0x50` the caller's wait sets.
  What is left of the dim is Verdite2's: no quads for the copy, a black
  `POLY_F4` at 50% across the margin once the brightness is `0x36`, and the
  game's two text quads.
- **The world as it was last drawn.** A post on stage 15 keeps the GTE, the
  scratchpad, the fog words `0x801AEC7C`/`80`, the draw environments' clear and
  the camera the frame was drawn from (`Stage15.ViewOverride`, the carried one);
  a pre keeps the tick fraction. A pass puts those in and its own back after.
  **`FramePacing.Frozen`** holds the smoothers: a menu's frames are frame
  boundaries too and move `Ticks`, so without it the billboard cels stepped and
  the creatures' pairs rolled. While it is set no walk is a tick's first, no
  iteration ticked, and the tick fraction is the last frame's. A pre on
  `func_8001576C` keeps the model walk's ambient sources silent during a pass.
- **The pass is a scene to the retained renderer.** `GpuWorld` begins a retained
  frame from a post on the frame head, but only inside stage 15, and a pass calls
  neither. So under Testing ▸ Scene renderer ▸ Retained GPU the world behind a
  menu or a sign fell back to the packet path, a different renderer from play's.
  With the Z-buffer on, that path draws the near faces in table order among
  depth-tested ones, so the user saw slivers of the wall in front of a sign
  (2026-10-05). The pass now enters the scene (`GpuWorld.SceneIn`/`SceneOut`) and
  begins the retained frame where the frame head would have, after the cull grid
  (`Stage15.DrawScene`'s `atFrameHead`).

### Measured

16:9, `KF3_FPS=144`, slot 1 (`fdat17`, area 5):

- **A message**, `KF3_MENUWORLD_TEST=6:305` with Triangle at 17 s: 418 frames held
  live until the press, 60 passes/s, `[present] wide 120-180, vram fallback 0` in
  every window, then the 7-frame fade-out and close; peak 32,976 of 120,784 bytes,
  no overflow. With `KF3_MENUWORLD=0` the game's wait reads `wide 6, vram fallback
  117` in its last window: the drop to 4:3.
- **The fade's speed** (2026-10-05, reported as "too quick, maybe at the frame
  rate"): it is not. Each step ends in the swap's `VSync(0)`, one vblank as on a
  console. Timed by the probe at 800+ fps: in 10 steps in 180 ms and out 7 in 113
  ms with pacing on (`VBlankPacing` holds the `VSync`), 140 and 113 ms with pacing
  off. `KF3_MESSAGE_FADE`/Gameplay ▸ Message fade length draws each step for more
  vblanks: x2 measured 347 and 230 ms. A longer fade-out lets the button come up
  during it, so it returns -2 where x1 returned -1, the game's own rule. **x1, the
  default, is the game's speed; whether longer looks better is the user's.**
- **The in-game menu** (`func_8001A774`), opened and closed with Circle: 60
  passes/s, every present wide, peak 31,760 bytes, then 144 fps in the main loop
  again. **Primitives reaching the margin while it is open: 34.6% live, 0.0% with
  the paste.** Cross (examine) in that save opens a script-driven menu instead,
  `func_8002008C` from the script interpreter: 40.4% against 0.0%. The start
  menu at boot (`func_8001FA60`) keeps the paste, since no world has been drawn.
- **Nothing moved during four seconds of that script-driven menu**: the billboard clock
  `0x80182964` and its cels, the camera block, the view matrix and the scratchpad
  read the same (shell `peek`).
- **A pass draws the last frame's packets exactly.** `KF3_MENUWORLD_PROBE=2` keeps
  the last main-loop frame's packets at the swap and compares the first three
  passes against them: 827 of 827 identical once the padding is masked (the high
  halves of a textured polygon's third and fourth texture words, which hold
  whatever the buffer held), and the vertex map's and the depth records' hit rates
  read the same in play and behind a sign (97.5% against 97.2%, 86% both).
- **Under the retained renderer** (`KF3_GPU_WORLD=1`): before the fix, 0 packet
  depths a second in play and 44,000 behind a sign, the packet path. After it, 0
  behind a sign and behind the in-game menu, the retained map drawn on every pass
  (2,421 retained frames drawn, 0 missed), the pass's own packets down to 0-1,216
  bytes. The packet renderer is unchanged (32,976 bytes a pass, 41,000 depths a
  second).
- Also run without pacing: the same, no exceptions.

**To judge by eye**: that the world behind a menu and a message is the frame
before it opened (nothing jumps, nothing moves, the margin filled), the 50% dim
and the message's lettering over it, and the fade. **Not checked**: a shop, the
save screens and a real sign or NPC reached in play rather than through the
test switch. `KF3_MENUWORLD_PROBE=1` names each menu's caller as it opens.
