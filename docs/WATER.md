# Water: murk, waves and planar reflections

Verdite2's water effects, ported to this game's retained renderer (2026-10-06):
murky water, water waves (a swell of the water's vertices and ripples over its
texture) and planar reflections. **Built and measured** (Video ▸ World
enhancements ▸ *Planar reflections*, *Murky water*, *Water waves*; `KF3_PLANAR`,
`KF3_MURK`, `KF3_WAVES`; the game's page WORLD ▸ WATER). The waves and the
reflections are on by default (the user's, 2026-10-06). **The murk is off, and in
neither ENHANCED nor FULL**: judged not good enough to ship yet (the user,
2026-10-08); its switch and `KF3_MURK=1` still turn it on.

The shared runtime already carries every pass these need, built for Verdite2:
`WaterMurk` and the reflection pass (`0067`), `PlanarReflections` (`0068`),
`WaterWaves` (`0078`), and the retained water, swell, plane finder and mirror draw
(`0085`). **Nothing in `tools/RecompOne` changed.** The port's half, in `patches/`,
is the game's facts and the per-frame wiring:

| file | Verdite2's | what |
|---|---|---|
| `WaterRects.cs` | `Reflections` (its rects), `Waves.ReadRects` | which faces are water |
| `WaterSwell.cs` | `WaterSwell` | which corners the swell may move |
| `Waves.cs` | `Waves` | the clock, the camera for the ripples, the swell's waves |
| `Murk.cs` | `Murk` | the murk's switch, distance and vertical |
| `PlanarMirror.cs` | `PlanarWalk`, `PlanarCull` | the mirror, from the retained frame |

`RetainedMap` flags each water face (`RetainedScene.FlagWater`) and each free corner
(`FlagSwell`) as it builds a chunk. `RetainedModels` marks every instance placed in
the world as mirrored. `GpuWorld.Begin` reads the rects before the map is checked,
and hands each frame's view to the three. The switches are in `SceneFeatures`.
Everything runs only under the retained renderer, which is the configuration this
port supports: the packet renderer (`KF3_GPU_WORLD=0`) draws water as the game did.

## Which faces are water

The game's two scrolling-texture records at `0x801AEB1C` (`TextureScroll`, "3e" in
`SMOOTHING.md`) copy a source image into a fixed dest rect every tick: this game's
counterpart of Verdite2's fluid slots. The layout is this game's own: active byte
`+0`, phase `+4`, dest `+8`/`+0xA`, source `+0x10`/`+0x12`, size `+0x14`/`+0x16`.

**The saved 28-area RAM corpus, read offline** (`/tmp/verdite3-next-phase/inner-corpus`,
the arrival snapshots `GPU_RENDERER.md` describes):

- One record is live in every area: dest **(1016, 96), 8x32 halfwords**, source
  (984, 96); the second record is unused (active byte 0xFF).
- Blended map faces on that rect, by blend mode, counted per drawn half:
  averaging (mode 0) in areas 0, 2, 5, 6, 8, 9, 10, 13 and 16 (from 134 in area 5 to
  2,183 in area 2), all level except 280 in area 2; **additive (mode 1) in areas
  1 (858), 2 (210), 6 (60) and 17 (568)**, all level.
- Other blended map faces (pages at 512 and 576, areas 14, 16, 17, 20, 22 and 24)
  are off the rect and mostly not level: not water.
- No mesh in the area's model table that the map does not use samples the rect.

So a face is water when it is **blended in an averaging mode (0 or 3) and samples a
scrolling rect** (`WaterRects.IsWater`), which is Verdite2's rule and the one the
runtime's shaders apply (`mode == 0 || mode == 3`). **The additive liquid of areas
1, 2, 6 and 17 is left out**, as Verdite2 leaves out its additive fire. What it is
(lava, glowing water) is for the user to say. Murking it would darken a glow.

Verdite2 needed `ModelWater` because slimes and spinning crystals use its water
texture. Here no model mesh in the area tables was found on the rect, and models
are never flagged as water, so a creature can never be murked or mirrored in. The
creature banks outside the area table were not scanned.

## Waves

**The swell** moves the water's vertices by three long waves (Verdite2's tuning:
338 units at the crest, a longest wave of 6,114, periods as the root of the length).
Verdite2 also moved the vertices in guest RAM for its packets. Here the map is the
retained mesh, so `WaterSwell.Build` only decides which corners are free, and 0085's
vertex shader moves them (`swellDy`), in the colour pass and the surface pass alike.
A water corner is free unless it lies on the rim (an edge only one water face
has) or where any other face of the map has a corner, so shores never move and no
crack opens. It is worked out over the whole map, from what places each half
(kind, height, turn), the meshes and the rect. Measured: area 5, 61 of 178 water
positions free (117 rim, 0 shared), built in 9-10 ms once per load; area 10, 217 of
384 free in 1.6-1.9 ms. A load changes the half table a few times, and each change
rebuilds the set. Each chunk takes from it only its own water faces and free corners
(`WaterSwell.ChunkHash`, 2026-10-06), so a change rebuilds the chunks that hold
water, not the whole map ("The stutters" in `DEVELOPMENT.md`).

**The ripples** are runtime 0078's, fed the frame's camera and the clock each frame.
The clock is the world's own: ticks plus the fraction between them, scaled by the
speed. It stands still in a menu (`FramePacing.Frozen`) and moves smoothly at any
frame rate. Measured: 469-1,165 rippled batches a second at 144 fps. The `waves`
shell verb tunes everything live.

## Murky water

Runtime 0067's reflection pass lays a dark colour over a water pixel by the run of
the view ray through the water, surface to floor (Verdite2's distance, 1,886 units
to 63%, and colour, `0.03, 0.05, 0.06`). Only a surface within `MaxTilt` (0.75) of
level is murked. `Murk.Frame` publishes the world's vertical in view space from the
frame's view.

The water reaches the surface buffer as id 2 (water) in the normal pass's water
slices. Checked with the surface probe and a temporary readback over area 5's
autostart view: the water there is 21,000-24,000 units off, behind near ground at
about 2,700. The normal pass drops it by the frame's depth, correctly, so that view
has no visible water at all. The plane finder still counts it, since it does not
test occlusion.

## Planar reflections

**Not a second walk.** Verdite2's `PlanarWalk` re-ran the game's tile walk from a
mirrored camera and replayed the object walk's submits, because its packets were
its picture. Here the world is already retained: each frame holds every map half
the walk drew and every model instance placed in the world, so the mirror is that
frame from another camera. Nothing of the game runs twice, and its RAM, GTE and
stack are never touched.

**The plane** is the runtime's: the frame's visible water triangles, at rest, binned
by height and screen area (`NoteWaterPlane`). The next frame mirrors in the
heaviest bin. No water on screen means no mirror, and from under the plane there
is none either.

**The mirrored camera**: with S flipping world Y about the plane and F flipping the
view's Y, the view matrix `R` becomes `F R S` (negate every element with exactly one
index 1). That is the same yaw with pitch and roll negated, an ordinary camera, so
back faces still cull. The eye goes to `2h - Y` and the view translation's Y flips.
The image is upside down and the pass reads row `2*OFY - y`, as for Verdite2.

**What the mirror draws**, before the frame's `DrawOTag` (game `0x8007A104`), into the
target's planar texture (`Runtime.Gpu.DrawRetainedMain` under `Capturing`):

- The halves the main view draws (the walk's and the render distance's), plus a
  cull of its own, as Verdite2's `PlanarCull` gave its mirror. That cull takes every
  half within the draw distance (the radius byte, or the render distance) whose box
  reaches above the plane and meets the mirrored frustum (`RenderDistance.Frustum`).
  There is no occlusion flood: a cavern the eye cannot see can be plain in the water.
- Every model instance placed in the world, and their blended faces, keyed by the
  mirrored view's depth.
- The map's blended faces after its opaque ones (`KF3_PLANAR_WATER=0` to leave them out).
- Not drawn: the sky, the arm, and models standing only where the mirror's own cull
  reaches. The object walk admits models by the eye's grid; Verdite2 admitted those
  as mirror-only submits.
- **No mip atlas by default.** The atlas's per-key lookups cost the mirror as much CPU
  as the main view: 1.6 ms a frame over area 5's pool with mipmaps on, against 0.1
  ms without. `KF3_PLANAR_MIPS=1` filters the mirror through the atlas as Verdite2
  does. A per-frame memo of the lookups in the runtime would give both views the
  atlas at one view's cost. It is not done: it is a shared-subtree change.

### Measured

A tour of the nine areas with averaging water (`warp`, then the camera 900 units above
the water tile with the most water around it, eight headings, pitched ±350;
`KF3_PLANAR_PROBE=1` and the `planar` shell verb, which asks the next reflection pass
for its readback):

| area | plane Y | views with water | best view: water of the picture | of it, planar |
|---|---|---|---|---|
| 0 | -12161 | 8 of 16 | 77.7% | 100% |
| 2 | -12161 | 15 of 16 | 99.5% | 89.8% |
| 5 | -12161 | 7 of 16 | 78.0% | 100% |
| 6 | -12161 | 8 of 16 | 58.2% | 100% |
| 8 | -12161 | 8 of 16 | 95.9% | 100% |
| 9 | -12161 | 8 of 16 | 88.4% | 100% |
| 10 | -9729 | 8 of 16 | 92.7% | 100% |
| 13 | -513 | 8 of 16 | 90.0% | 100% |
| 16 | -513 | 8 of 16 | 79.5% | 100% |

Every view drew a mirror (74-75 a second at 144 fps with the probe's pauses), **0 missed,
0 dropped**. Looking up (pitch -350) shows no water, as it should. Area 2's 10%
non-planar is water whose height differs from the plane (its 280 tilted faces). The
planar-against-march check (`ComparedPct`, `MirrorDiff`) needs the screen march,
which this port does not switch on, so it was not run.

At area 5's autostart view, the mirror is about 526-619 halves (131-169 of them its
own cull) and 3-27 instances. CPU for the half cull 0.12 ms, the blended faces under
0.01, the backend's draw 0.10 (1.6 with the atlas).

**Cost**, uncapped (`KF3_FPS=1000`), area 5's pool from above (`view 74752 -13060 99328
350 1536 0`, 79,368 px of water), render scale 6, 16:9, the copied settings (per-pixel
lighting, AO, mipmaps and anisotropy 16 on), Radeon RX 9070 XT:

| | fps |
|---|---|
| all three off | 348-358 |
| murk only | 353-359 |
| waves only | 341-350 |
| planar only, the mirror through the atlas (before) | 214-216 |
| all three, mirror through the atlas (before) | 213-218 |
| all three off, again (the run beside the next) | 364-367 |
| all three, no atlas in the mirror (the default) | 307-326 |

So the three cost about 0.35 ms a frame here with the default mirror: the murk
nothing measurable, the waves about 0.1 ms, the mirror the rest. The profiler
(`KF3_PROFILE=1`) puts the mirror's GPU draw (the `capture` pass) at 0.57 ms a
present. With the atlas, its hook was 1.77 ms of CPU a frame, 1.62 of it the
backend's draw.

## The HUD over the water

Reported 2026-10-07: the text at the bottom of the screen (the message box an item
or coin pickup shows) was garbled over the water and took its colour, only the
outlines of the letters readable. Measured with a message queued by hand over
area 5's pool (`view 74752 -13060 99328 700 1536 0`; the kind table `0x801AEAED +
cursor`, the cursor `0x801AEAF6`, as `func_80041EEC` writes them) and the presented
picture read back:

- The box is `SPRT`s from tpage `0x1A` (4-bit at (640, 256)), CLUT `0x7804`, in
  the main table's entry 1, drawn by `func_80041AD4` once subtracted (tpage `0x5A`)
  and once added (`0x3A`): on the console that is a lighten, the text at full
  strength over anything darker than it.
- The runtime marked add/subtract texels as covers in the surface buffer, so murk
  and reflection skipped them while the water round them was darkened: the letters
  were the raw water's colour against the murk. With murk, waves and planar on,
  the murked water read 15 (mean RGB) against 60 plain; the subtracted letters read
  56, brighter than the water round them, and the added ones stood 30 over it
  against 270 over plain water.
- Fixed in the runtime, `0099` (`tools/RecompOne/docs/RECOMPONE_PATCHES.md`): the
  picture is copied before the frame's first see-through 2D primitive, the passes
  shade the copy where a HUD primitive lies, and what the primitive changed is
  added back. After: the letters stand 261 over the murked water against 270 over
  plain, and the copy costs about 0.05 ms a frame (342 → 336 fps uncapped at scale 6).

The item pickup's own loop (`func_8005DB30`) queues the same box; it was not driven
here (called by hand from the player stage, it waited without showing it). The
full-screen message's two text quads (`MenuWorld.MessageFade`, added and
subtracted) take the same path.

## Shell verbs and switches

`murk [on|off|tilt X|distance X]`, `waves [on|off|swell|swellsize|ripple|ripplesize|shade|speed <value>]`,
`planar [on|off]` (with the mirror's cumulative counters and the readback). All are
live and unsaved. The `KF3_*` switches are in `ENV_VARS.md`.

## For the user to judge

With each switch on and off, in areas 0, 2, 5, 8, 10 and 13 (the most water):

- **Planar reflections**: walls, cliffs and creatures standing in the water at the
  right place, the right way up, not drifting as you turn or walk; nothing popping in
  the water as you turn; the reflection's fog at its far edge; whether the mirror
  without mipmaps shimmers (`KF3_PLANAR_MIPS=1` to compare).
- **Murky water**: deep pools darker than the shallows, no dark lines at tile seams,
  nothing that is not water darkened.
- **Water waves**: the swell rising and falling with the shore still, no cracks
  opening, the ripples breaking up the tiled pattern.
- **The additive liquid** of areas 1 and 17 (and part of 2 and 6): what it is, and
  whether it should be water.
- **The bottom message over the water** (`0099`): picking an item up with the camera
  over a pool, the text readable and the water under and round it murked and
  reflected alike.

## Open

- Models standing only where the mirror's own cull reaches are not reflected.
- The sky is not reflected (nor in Verdite2).
- The mip-atlas lookups twice a frame, with `KF3_PLANAR_MIPS=1`.
- Warping with the scene driver while a `view` override is held crashed twice in
  `func_80019B68` (`unmapped address 0x80800000`, under `LibCd.PumpEvents`), with the
  water on and with it off; four warps without the override passed with the water
  off and with it on. It is the harness's, not the water's; not investigated.
