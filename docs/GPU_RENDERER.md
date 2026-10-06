# Native scene submission and the retained GPU renderer

Implementation follows Verdite2's `docs/VERDITE3_GPU_PLAN.md` (2026-10-04).
The target is the retained mesh/pose/instance backend already in RecompOne.
World packets remain the numerical reference and explicit fallback. This document
distinguishes source inspection, runtime exercise, numerical equivalence and
the user's visual judgement. None implies the next.

## Baseline and protected work

Started on `main` at `130f870`, seven commits ahead of `origin/main`. Existing
changes in `FramePacing.cs`, `TestingSection.cs`, `DEVELOPMENT.md` and
`ENV_VARS.md` are retained. A copy of each original dirty file is in
`/tmp/verdite3-gpu-reference/`. Tests use copied cards, settings and interface
configuration there; the normal save cards are not used for writes.

The current slot-1 card loads **area 5, fdat17**, position
`(83569, -12800, 132288)`, yaw 1718, HP 108/134, level 12.
The older fdat02 baseline is a different save. The copied interface configuration
has enhancements enabled and a 16.8 Hz tick override. Measurements explicitly
set `KF3_TICKRATE=15` so this does not change the user's settings.

Release build before edits passed. The bounded reference run with
`KF3_FPS=1000`, 15 Hz world ticks and geometry **timing** hooks measured roughly
590–605 drawn frames/s, 15 ticks/s, 828 packets/frame, stationary in fdat17.
The timing probe initially counted frames twice after it registered some hooks
and then failed resolving an extra address; its inclusive ms/frame values from
that run are invalid. Pacing's independent fps/packet totals remain usable.
The attachment is now transactional and accepts short addresses without
duplicating hooks on retries. Fresh corrected measurements are required.

## Drawing inventory

`python3 scripts/drawing_inventory.py` inventories all generated overlays into
ignored `scratch/drawing-inventory.json`. It records call sites and unresolved
dispatch expressions rather than treating static reachability as execution.
Current source has **28 area modules** and **38 static stage-15 call sites**.
The 19 distinct callers include the main loop, modal loops and nine area modules.
Stage 15 owns those world redraws regardless of the simulation stage that called
it. `func_8005C0D4` really does call stage 15 in current generated source; the
contrary statement in the older smoothing log is stale.

World submission families, source-inspected:

- Map walk `8003BFD0` (one caller); half `8003BB04` (four call sites).
  `8003BE34` is the independent cell helper; no static callers were found.
- Model walk `80040AE4`; main submit `8003E34C` (four call sites), front
  `8003F304` (two), sky `800400AC` (two), arm `8003DF50` (one).
- Assemblers: map bulk `80039D50`, map near `8003AB04`, lit `80035CA4`,
  model near `800366A8`, forced blend `80037BEC`, front `80038844`, sky
  `80039428`. The four subdivision entries and their bodies remain part of the
  near reference. Map bulk supports 24/2C/34 and skips 3C; models support all four.

Presentation ownership, source-inspected and retained on packets:

- HUD models `8003C35C` call the shared lit assembler. Its caller domain,
  rather than the assembler's address, determines ownership.
- Standalone preview `8004290C`, called by `80025BE8` at `80025D9C`, owns
  its projection H=200 and separate mesh table. It remains packet-owned.
- HUD icons `80041E68`, overlays `80041D9C`, and six screen-effect routines
  `8003D280/38C/41C/568/64C/79C` remain packet-owned, with their table order.
- Menus, messages, loading screens and movies retain packets. The source audit
  found three SDK DrawOTag entry points (GAME, OPEN, END); every one is probed.
  Direct GP0/DMA and indirect guest emitters still need runtime attribution.

The scene census (`KF3_SCENE_CENSUS=1`) brackets all 26 main drawing and domain
entry points. It reports area/overlay, immediate caller, assembler domain,
submit flags, packet command counts, bytes, table positions and source vertex
projection counts where the assembler owns the transform. Unattributed packets
are preserved and reported. It is diagnostic, not a performance configuration.
`KF3_SCENE_CENSUS_FILE` exports cumulative JSON to a chosen ignored/temp path.

## Native reference unit

`NativeScene` owns ten replace hooks: map walk, independent cell helper, half,
main/front/sky/arm submitters, and forced-blend/front/sky assemblers. The literal
reference bodies preserve register arithmetic, scratchpad and RAM order, GTE
operations, guest stack frames and callees. They are built separately from the
existing bulk and near implementations.

`KF3_NATIVE_SCENE=0|1|verify` currently defaults to **0**. In verify, the original
result stands after comparing RAM, scratchpad, callee-saved registers, SP/RA,
LO/HI and GTE. Nested submitters remain original during one comparison to avoid
replaying a different child path. `KF3_NATIVE_SCENE_VERIFY_FUNCS=hex,...` chooses
the functions to compare, so separate runs can exercise every family instead of
only the outer map walk. Installation, execution and equivalence are distinct.
The supplemental verifier compares all CPU snapshot fields, an 8 KB guest stack
window and relevant callee order/RA/SP/arguments. The all-area outer-function run
completed 93,501 comparisons without a difference. Nested functions remain
original during each comparison: inner assemblers still need selected runs.

## Corpus driver and open gates

`KF3_SCENE_DRIVER=1` enables `warp <0..27>` on the shell channel. Requests are
queued at VSync, then the game-thread stage-3 post hook calls the game's existing
area loader with its cross-area descriptor. Stage 4 confirms both the actual
area byte and overlay before nearest-floor placement using `8002B760`.
A request reply is **not** coverage; confirm `state` after arrival. This driver
is intended for copied runtime state. It neither saves nor changes normal cards.
An empty map, bounced warp, death or scripted transition remains unresolved.

## Retained implementation checkpoint (2026-10-04)

Implemented, opt-in: `KF3_GPU_WORLD=shadow|1` submits persistent source meshes,
rigid pose buffers and MO key/delta buffers to the existing shared retained
backend. The submission seam precedes the legacy vertex loop; ordinary retained
instances do not capture per-frame transformed vertices. `shadow` keeps packet
rendering while exercising extraction. `1` suppresses successfully owned bulk
map/model/sky/arm geometry when native submission, perspective, depth and the GL
backend are available. Native submission is enabled automatically only when its
environment variable is absent. Reference/verify modes remain available.

Mesh topology, normal/face/vertex mutations and mesh-generation invalidation are
checked by the source probe. Map chunks hash tile/source dependencies; light
records upload separately. A changed chunk currently republishes the combined
static map; partial GPU chunk updates and resource budgets remain open. MO
publication defers the posed RAM buffer only for retained consumers, with explicit
materialisation before a packet consumer. Deferred fallback's complete CPU/GTE
side effects and all clip readers still require verification.

Main and forced-blend instances publish per-face table keys with separately
counted CPU order calculations. Sky and opaque arm instances use the backend's
ordered passes. Near subdivision, front-table submits, orthographic branches and
blended arms still produce **explicit attributed fallbacks**. Static map decoding
keeps the bulk assembler's GT4 skip; the near map path reference-skips GT4 as
well; the near model assembler supports GT4.
HUD and standalone previews remain packet-owned by caller domain.

The shared depth-linear cue (curve 5) accepts this game's near/far records. It
uses quarter-depth quantisation and truncating division, the 32000 cutoff and
7951 maximum. Integer records use integer division on both CPU and GPU; the
Radeon float reciprocal previously made three exact boundaries one unit short.
The actual composed world/normal programs link on the Radeon RX 9070 XT. All
270 isolated cue cases are exact. The actual shader pose function matches 27
vertices from nine literal guest-decoder fixtures, including negative/wrapped
deltas and endpoint weights. These probes do not establish visual quality or
complete lighting/projection/subdivision equivalence.

## Exercised coverage and its limits

The copied-card corpus confirmed **all 28 area/overlay pairs** with one heading
in native verify, and four headings in retained shadow extraction. The driver
holds player physics after its first warp to avoid the collision routine's
unbounded probe when a synthetic position is invalid in a new area. Other game
stages still run. Coverage JSON labels this condition explicitly. These are
confirmed loaded-area fixtures, not normal gameplay or exceptional-context
acceptance. Boss damage/death after capture is possible.

The outer native run exercised the map walk/half in 28 areas, main submit in 26,
front in two, sky in 13 and forced blend in nine. Front assembler and independent
cell helper were not exercised. Inner replacements require isolated comparisons.
Source discovery found map records 24/2C/26/2E throughout the corpus, plus model
bank candidates including 34/3C/36/3E and untextured sky. Candidate-bank records
are discovery only; resident stale banks and skipped command bytes do not prove
actual reachability. Clip/segment enumeration remains open.

The first retained shadow run counted 594,759 submissions / 533,517 retained,
with near subdivision the reported fallback. It preceded deferred shadow poses;
its pose count therefore does not verify MO deferral. The first actual GPU run
in area 5 counted 317,367 submissions / 268,197 retained and 49,170 near fallbacks.
The following actual-GPU run confirmed all 28 areas at four headings and counted
13,379 successful backend main draws, zero misses, 59,452 drawn instances,
172,548,916 static triangles and 3,151,966 blended-map triangles cumulatively.
There were 112 source MO pose builds / 6,558 hits and 7,496 deferred poses / 826
materialisations. Its only reported substitution fallback was near subdivision
(149,954 submissions). AO was off, so normal/surface zeros in this run prove
nothing about those passes. Extraction, backend exercise and numeric agreement
remain separate; no visual judgement is recorded.

Verdite2's bounded compatibility run with the changed shared runtime kept its
retained map, sky, animated poses, water, mirror and normal passes active. Its
reported census had zero legacy world projections/3D packets and zero missing
or behind-depth surface samples. This is its new-game reference area, not a
whole-game compatibility claim.

## Open completion gates

Near/subdivision topology and UV/colour/order arithmetic; front table and generic
barriers; orthographic/view-space/blended-arm branches; all exceptional/modal,
area-module, boot/movie/menu/preview callers; complete indirect/DMA attribution;
full native inner-function and deferred-fallback equivalence; every clip/segment;
GPU projection and lighting numeric fixtures; neighbour blending (built and
measured, off until judged: see "Blending light and fog across tile edges"); solid blended
surface classification; SSAO/filter/mipmap/distance settings; texture packs;
mutation/resource/epoch/second-view checks; fixed-world performance and user
visual acceptance. Opaque or starting-area success is not full coverage.

## Feature wiring checkpoint

`SceneFeatures` registers English/pt-BR/es-419 controls under Video for per-pixel
lighting, fog by pixel depth, SSAO and quality, geometry normals, anisotropy,
mipmaps and enhancement distance. Testing gets live packet/shadow/GPU and
native-reference selections. The original pacing/settings changes are untouched.
Unjudged lighting/AO/filter features default off; depth fog and geometry normals
are ready for opted-in consumers. Environment choices override saved settings at
boot. These controls use the existing shared passes; their Verdite3 runtime and
normal/surface coverage measurements are recorded separately below.

Static map corners carry source normals for record lighting and must clear the
model-only `FlagDots`: leaving it set treated recordLit's colour as raw model
dots and lit it twice. A source fixture checks this distinction. GPU record-light
fixtures use the actual shader function against the native GTE. This correction
postdates the first all-area GPU run above. Neighbour controls are not yet wired.

The source probe now passes 299 assertions. The isolated Radeon probe also
checks the actual recordLit shader against 48 GTE NCCS cases across all four
record rotations, signed normals/matrices and varied source colours: all exact.
This bounded fixture range does not cover extreme matrix-product overflow.

With lighting, AO, geometry normals, anisotropy 8 and mipmaps enabled in area 5,
the first feature run counted 3,564 main GPU draws, zero misses, 111,776,079
normal-pass static triangles, 3,564 sky objects / 342,144 sky faces, and one
record upload. Its surface-check count was zero: AO allocated only the normal
attachment when reflection consumers were disabled. The shared probe now
requests its own surface attachment, independently of reflection features.
A fresh final run checks that mechanism separately; no surface coverage is
inferred from the earlier zero. No arm or blended-solid surface coverage was
exercised by this area-5 run.

The map assembler's source RGB is scratchpad +54 (the model families use +64).
Until per-frame static-map RGB is expressed in the scene contract, non-neutral
map RGB is an explicit `map-source-colour` fallback, rather than silently drawing
it as 808080. This branch is source-reviewed, not exercised by the corpus.

Continuation priority: selected inner native comparisons (half, forced/front/sky
assemblers and synthetic independent-cell fixtures), followed by retained near
subdivision and generic front/table barriers. Keep per-frame transformed-vertex
capture out of the implementation. Complete exceptional-context ownership and
per-pixel neighbour/solid-surface policies before default adoption.

Final feature/probe checkpoint: 2,967 main draws / 0
misses, 93,041,025 static normal-pass triangles,
2,965 AO passes, 1,093 mip entries /
2,578 decodes with no atlas-full events. The numerical surface
probe ran 10 times but counted **zero depth-bearing pixels**.
Its zero missing/behind counts are therefore **inconclusive**, not successful
coverage. Resolve whether projection/culling, final presentation masks, or probe
target/depth selection caused this before claiming pixel coverage. Draw counters
prove submission to the backend, not that the user sees those surfaces.

This is an opt-in development checkpoint, **not the completed authorised
renderer**. No visual judgement has been requested/recorded yet. The next user
checks, after numerical depth coverage is understood, are map/model visibility,
near joins, sky and arm layering, lighting/fog transitions, transparency, and
HUD/menu/text masking. No screenshots or game-window scraping were used.

## Local checkpoint ownership

Shared runtime commit: `c2c11f8`, exclusively `tools/RecompOne`; the matching
shared subtree commit is `e09dc04770633a2e16d430192ebab3d4d083fbf4` on local branch
`checkpoint/retained-linear-cue`. No remote was contacted or pushed. Game source
and its probes/docs are a separate checkpoint. The pre-existing pacing/settings
changes remain uncommitted; the renderer documentation append is staged on its
own where those documents already had user changes. Temporary fixtures, cards,
settings, generated code and test logs are not committed.

## Continuation checkpoint (2026-10-05)

Selected inner native comparisons were reviewed clean from the root checkout:
`8003BB04` (half), `80037BEC` (forced blend), `80038844` (front) and `80039428`
(sky), with `POLYASM`, `NEARPATH`, `MODELWALK` and `MOPOSE` off. The run covered
all 28 areas at four **actual guest** headings through the driver's `--guest-yaw`,
and the latest result is **478,515 full CPU/stack/order comparisons with zero
mismatches**. The front assembler had only **one live call** in that run, so
reachability is shown, not full coverage.

New synthetic fixtures add 13 comparisons (8 front, 5 independent cell) and 152
assertions: the full 2 MB image, scratchpad, GTE, CPU state, guest stack and
callee order are exact and the accepted packet sizes/counts are asserted. The
independent near-cell test rejects.

The near descriptor probe (`KF3_GPU_NEAR_PROBE=1`, default off) is depth
eligibility only: source corners, depth, mean, level, the direct OTZ key and the
GT4 map skip. The combined source probe passes **539 assertions**, including
88 near-descriptor assertions and checks that describing a face preserves RAM,
scratchpad and GTE state. Bounded fixtures match recompiled packet counts and
OT buckets; facing, screen-box and partial-subdivision culls are not modelled.

The earlier near-GT4 sentence in this document is corrected: the near **map**
path reference-skips GT4 just as bulk does, and only the near **model** path
supports it.

Depth cause identified: the recompiled near packets lack `GtePacketDepth`, and
with AO on the unrecorded opaque mask stamps the far plane. An actual GPU run
defaults to native near when the environment variable is unset or whitespace;
explicit `KF3_NEARPATH=0`/`verify`, or disabled map/model near subfamilies,
block retained drawing with `near-depth-disabled` and leave packets in charge.
Packet and shadow boot defaults are unchanged.

`KF3_GPU_SURFACE_PROBE` stages eligible retained frames only, independently of AO
and SSR, reports absent/stale resources and FBO/serial-pair mismatches, and
preserves the GL read state. In the same area 5, native near off gave zero depth-bearing samples across five surface checks;
native near on gave 34,531 across eight checks. After the fix, AO off gave
295,050 across five checks, with zero missing/behind samples; AO on gave 21,583
across five checks, with 66 missing and four behind. These residual AO cases
remain open. The two runs use different resolutions, so those numbers are not
directly comparable. Main and final stages carry nonzero depth and distinct
physical FBOs, with some serial mismatches reported explicitly, so they are not
always the same frame.

Still next: persistent source-space near subdivision mesh/pose, exact midpoint
UV/colour/topology, and generic front/table barriers. The Release build passes;
the actual composed Radeon shaders link, with all 270 cue cases, 27 pose vertices and 48 native GTE
light cases exact. User visual acceptance remains unrecorded; no screenshots
were used. The earlier ownership note describes the previous checkpoint.

The final combined AO-off run in area 5 counted 2,756 main draws, zero misses,
and 236,040 depth-bearing samples across four surface checks with zero
missing/behind samples. After-main/final probes counted 128,292/177,030 depth
samples; one early final target was absent. The source descriptor probe observed
108,965 faces, of which 81,725 passed its depth gates; this is not a drawn-face
count. Flat triangles/quads were exercised; gouraud/GT4 reachability was not.
Explicit `KF3_NEARPATH=0` produced `near-depth-disabled`, zero retained draws
and zero stage probes while packets continued.

The changed shared runtime also passed a bounded Verdite2 new-game run: retained
map/poses/sky/water/mirror and normal passes stayed active, with zero reported
legacy world projections/3D packets, zero missed map walks/mirror captures, and
165,484 depth-bearing surface samples with zero missing/behind samples in the
last window. This establishes starting-area compatibility only.

Shared runtime checkpoint: game-repo commit `e57a33c` contains only
`tools/RecompOne`; its exact matching subtree commit is
`4c3375f6b82b4e0798f479adad4c68ae347ecf98`, the head of the Verdite fork branch
`checkpoint/retained-depth-probes` (first pushed at `a339e6f`, game commit `04a9c9b`). No upstream issue or PR was created.
Game adapter/probe/docs changes are committed separately from the shared runtime.
The user's pre-existing pacing/settings edits are preserved in their own commit.

## Reported visual regression (2026-10-05)

The user supplied an image showing large black gaps between textured floor
sections and reports that it specifically occurs with **Retained GPU** and
**C# native scene reference** enabled. This is now a reported visual correctness
failure; successful submission/depth counters do not establish complete visible
geometry. The cause and exact area/view/settings have not been established.
The black background in the image has not independently been confirmed as a
sky defect. No renderer changes were attempted after the report: the user asked
to leave investigation for the next session.

Prioritize reproducing and fixing this regression before extending retained
near/front ownership. See [the next-session handoff](GPU_RENDERER_HANDOFF.md)
for the current checkpoint, source entry points, verification and next slice.

## Bounded bulk-map decoding correction (2026-10-05)

Source inspection found a retained extraction defect in bulk map GT3 (`34/36`).
`80039D50` reads its vertex references at body `+0xE/+0x10/+0x12`, while lit
models and the near map route read `+0xE/+0x12/+0x16`. Its normals remain at
`+0xC/+0x10/+0x14`: the second bulk vertex really uses the second normal's
reference. `RetainedMap` previously requested the lit-model layout, changing
the retained triangle before suppressing the original bulk packet.

Extraction now has a separate `MapBulk` cache family with independent vertex
and normal strides and the reference GT4 skip. The opt-in near descriptor probe
still requests the lit layout; near packets remain the fallback. The runtime
subtree is unchanged.

`tools/scene-probe/BulkMapFixtures.cs` compares retained corners with packets
emitted by the actual recompiled bulk assembler, covering eight opaque/semi
commands and four retained tile rotations each, UV/normal associations and
separate model/near cache entries. Against the pre-change assembly it fails on
`bulk 34/0: retained corner 1 differs from recompiled packet`.

After the correction, the combined source probe passes **879 assertions**
(340 new bulk assertions, plus the existing 539). The Release game build passes
with existing generated/runtime warnings. The composed world/normal shaders
link under **llvmpipe** in this sandbox; all 270 fog cases, 27 pose vertices and
48 GTE light cases match. These are source/packet and isolated shader checks,
not a live hardware draw or a floor-gap visual comparison. Evidence is under
`/tmp/verdite3-bulk-map-before`, `/tmp/verdite3-bulk-map-after` and
`/tmp/verdite3-bulk-map-build.log`; those temporary files are not required inputs.

This defect is **not established as the reported floor-gap cause**. A fresh
offline scan of the saved 28-area RAM corpus found only map `24/26/2C/2E`,
with no bulk GT3 records. That scan establishes the contents of those snapshots,
not exhaustive gameplay reachability. The original area/view/settings and
per-half ownership investigation remain needed; no visual acceptance is claimed.

## Models under later packets (2026-10-05)

The user reports that the floor gaps above are gone (no fixed view was recorded,
so this is their report rather than a measurement), and that floor behind a
creature, such as a plant enemy, shows over it, with other kinds of models too.

Mechanism, measured. The table walk draws the retained map and models at slot 1
(`LibGpu.WalkOTag`), and then every packet left in the table. The only scene
fallback in every area is near-subdivided map halves
(`8003BB04:8003C294:near-subdivision-pending`), so those packets are the near
floor and wall faces. A tested packet draws its colour against `0051`'s tolerance
(1 unit plus half the depth's change across a pixel), which gives a coplanar
overlap to the later table entry. Over a model's pixels it gave the pixel to a near
map face up to the tolerance **behind** the model. A floor seen at a grazing angle
has a large slope term. Under the packets, those faces were linked 0xF0 slots
deeper than the model and drawn before it.

Fix: shared runtime `0086` (`tools/RecompOne/docs/RECOMPONE_PATCHES.md`). The
models mark the display target's stencil, and a tested packet drawn after them in
that frame tests against the true depth over the mark. An opaque packet that wins
there clears the mark. `KF3_GPU_MODEL_MASK=0` turns it off for comparison.

The probe (`KF3_GPU_MASK_PROBE=1`, shell `gpu`) counts with occlusion queries
before the decision, so its counts do not depend on the switch. A tour of all 28
areas through the scene driver looked at the three live creatures/objects nearest
each arrival, from 900 and 2000 units at four headings (648 views, render-only
view override, physics held). Results:

- Packet samples behind a model that the tolerance passed: **24,871,575** in 31
  views, in areas 0, 1, 7, 9, 10 and 17. In the worst view (area 0, creature 25 at
  `58792,-14336,124304`, camera 2000 units south, yaw 0), they covered about 13% of
  the creature's samples (14.3M of 107M over 75 frames). The mask holds all of them
  back, by construction: the same queries with the same depth state. On the final
  build, at that view: 94.5M of 244M packet samples over model pixels were behind.
- Map in front of a model by more than the tolerance, which under the table's
  0xF0-slot tile bias the model would have covered: 3.5/20/87/160/1197 parts per
  million of model samples within 8/32/128/512/960 units. Not changed. It is the
  same in the packet Z-buffer path, which does not use the table slot for depth.
- Blended packets in front of a model by less than the tolerance: zero.

`scripts/model_mask_tour.py` repeats the tour. It reads the counters once the view
has settled, whereas the run above also counted the frames of each move.

Cost at the worst view, uncapped, without the probe: 617 fps with the mask, 628
without (about 0.03 ms a frame, 23 masked batches a frame). The combined source
probe still passes 879 assertions, and the composed shaders link on the Radeon
with all cue/pose/light cases exact.

Not judged by eye. **For the user to check:** creatures and objects standing on
near floor no longer have floor drawn over them, while turning and walking up to
them. Verdite2 keeps its behaviour, since `RetainedScene.ModelMask` is off unless
the game sets it, but its depth attachment is now depth-stencil; no Verdite2 run
was made.

## Seeing through doors, and floor over models again (2026-10-05)

The user reports, with **Retained GPU** and the C# native reference (the only
configuration to be supported): standing next to a door, part of an NPC shows
through it; and floor tiles in front of statues, beds and creatures, which `0086`
was meant to have fixed, still happen.

### The near geometry drew in painter's order

Measured with the scene census, which now counts each emitter's polygons that have
no depth record (`Unrecorded`), in fdat17 at the autostart position, walking:
**385,261 of 789,180 near map packets (49%) had no record**. With AO on, an
unrecorded packet is `zMode 3`: no depth test, and the far plane written where it
draws. So the near floor and walls (the faces nearest the eye, a door beside you)
painted over the retained models drawn at slot 1 whatever their depth, and erased
the depth behind them, so anything tested after them showed through. `0086`'s mask
only reaches packets that have a record.

Cause: the near path recorded a corner's `SZ` only from the division's own `RTPT`s.
A face's original corners come from the vertex cache, with their screen word at
record `+0x10` and `SZ` at `+0x14`; they were never noted, so every sub-polygon
touching one had no record. `NearPath.NoteCorners`, at each of the four division
entries, notes them. Unrecorded near packets fell to **22,382 of 484,692 (4.6%)**.
Every one left had a corner whose `SZ` was 0: at or behind the eye, where the GTE's
screen position is saturated and no depth can be interpolated across the packet.

Under Retained GPU those packets are no longer drawn. A near half is the same
static mesh the backend already holds, so `RetainedMap.Submit` notes it like a
bulk half; the GPU clips it at its near plane (`MainNear`, 16) with true depth. A
model under the submit's `0x40` flag (the models' near path) is a retained
`Tile` instance (`modelTileKept`: facing on the screen while every corner
projects, otherwise its plane against the eye, and the GPU's near clip). Census
afterwards: **no** `near-subdivision-pending` fallback, no near packet; the packets
left are the HUD's models, a screen tint and a few unattributed menu packets.
`NoteCorners` stays for the packet renderer's Z-buffer.

Differences from the near path, by construction: the game dropped a divided piece
whose corners were all nearer than H/2; the GPU draws down to 16 units. Midpoint
colour and fog come from the retained shader (per pixel with fog from depth), not
from the division's corners. Neither is judged by eye.

### The tolerance on faces seen edge-on

`0051`'s tolerance is 1 unit plus half the fragment's own depth change across a
pixel. On a face seen nearly edge-on (a creature's silhouette, a wall at the edge
of a doorway), half a pixel spans hundreds of units, and the face drew over
surfaces that far in front of it. The tolerance probe (`KF3_GPU_TOLERANCE_PROBE=1`,
runtime `0087`) counts, before each colour pass of the map and the models, the
samples that pass only because the tolerance exceeded 0.25/1/4/16/64/512 units.
Tour of areas 0, 5 and 7 (three things each, 900 and 2000 units, four headings):
map samples more than 512 units behind 1,874-4,142 per area, instance samples more
than 64 units behind 326-1,282.

`0087` bounds the slope term in the retained main view at the world width of
`RetainedScene.DepthCapPixels` game pixels at the fragment's depth (z / H each);
the game sets 1 (`KF3_GPU_DEPTH_CAP`, 0 unbounded). A coplanar partner's offset is a
unit or two of world, so this keeps the ownership of seams and models flush with the
floor. Same tour: **nothing more than 512 units behind anywhere, instances more than
64 behind: 0**. Model samples hidden by the map within 8 units (the floor contact
the tolerance exists for) are unchanged, 9.1e-5 of model samples before and after.

All 28 areas on the final build (`model_mask_tour.py`'s views with the tolerance
probe and the scene census; three things an area, 648 views; area 21 has none):
375,539 main draws, **0 missed, no fallback reason at all**. The only unrecorded
polygons are the HUD's models, the screen tints (836) and 551 unattributed menu
packets, none of them world geometry. Map samples passing only by more than
64 units: 4,292 of 2.3e11, none by more than 512; instance samples by more than
16 units: 61,361 of 3.5e10, none by more than 64. Source probe 879 assertions;
the composed shaders link on the Radeon with every cue, pose and light case exact.
Uncapped at the autostart view in fdat17, probes off: HEAD 654-683 fps, this build
780-794 fps; the near packets cost more than the retained halves that replace them.
No Verdite2 run: `0087` changes nothing until a game sets `DepthCapPixels` or the
probe. **Not judged by eye.**

### Models just behind the floor: measured, left alone

The table linked a map tile 0xF0 slots deeper than its mean, so a model's base
sunk into the floor drew whole. The question was whether the Z-buffer cutting such
bases is what the user sees. A trial shader rule drew a hidden model fragment over
an upward-facing map surface whose plane it lay less than a set distance below.
Six areas (0, 1, 7, 9, 10, 17; six things; 48 views each), with every floor-facing
surface made transparent to models against none: the share of model samples hidden
by the map within 128 units was unchanged in areas 0, 7 and 9 (it is walls and
door frames), fell from 2.8e-3 to 1.3e-3 in area 1, and was under 4e-6 in 10 and
17. Bases in the floor are not the effect reported, and the rule would also show
models through the top of a low step, so it was taken out.

## Sign lettering under its plate (2026-10-05)

The user reports, under Retained GPU, that the text on signs and other decals does
not show: the object it is on covers it.

Mechanism, from the meshes. A wall plaque is a map mesh (area 0 kinds 118 and 221,
area 5 kind 105 in the RAM corpus) whose **face 0** is the lettering (CLUT `7A0D`,
page `0F`) and a later face (51, or 36) the plate under it, on **the same four
corners**. The assembler links each face at the head of its table slot, so the walk
draws a slot's faces last built first: the lettering, built first, is drawn last
and on top. The retained store held each mesh's faces in source order, and under
`0051`'s tolerance a coplanar pair goes to the later draw, so the plate covered the
lettering. Before the near map was retained, a plaque close enough to read was a
near packet, drawn in table order; from further off the plate already won.

A scan of every mesh in the corpus's model table for faces sharing a plane and
overlapping: 10 opaque pairs, each the full plaque quad, lettering first; 186
pairs with a blended face, which the backend draws after the opaque ones (a
model's sorted by slot and build order, the map's in the store's order).

Fix: `RetainedAssets.Build` stores a mesh's faces last first (opaque and blended
partitions both), and `RetainedMap.BuildChunk` copies a half's faces last first,
so of two faces on one plane the first built is drawn last, as the walk draws it.
Faces not on one plane are still decided by depth. A model's blended faces, the
sky and the arm are sorted by their own slot and build index, which this does not
change.

`BulkMapFixtures.CoplanarOrder`: two quads on the same corners through the
recompiled bulk assembler (`80039D50`), its table slot walked from the head; the
retained map's and a model mesh's last-drawn face must be the walk's last. Against
the previous build it fails (`retained map draws another face last than the table
walk`); now the source probe passes **884 assertions**, and the composed shaders
link on the Radeon with every cue, pose and light case exact.

The last fixes still hold, measured on this build with the tour of areas 0, 5 and 7
(`model_mask_tour.py`, three things an area, 72 views; mask and tolerance probes
on): 2,554,741 submissions all retained, no fallback; 17,173 main draws, **0
missed**; no packet batch over the models (no near packets are left); map samples
passing only by more than 512 units **0**, instance samples by more than 64 units
**0**, as after `0087`. **Not judged by eye.**

## The view too narrow until the Z-buffer was toggled (2026-10-05)

Reported: the field of view was wrong, and turning the Z-buffer off and on again
put it right. `GpuWorld.ReadView` gave the retained view `GteDepth.ProjH/ProjCx/
ProjCy`, which only `Gte.Rtp` publishes (`NoteProjection`, with AO or SSR on).
Retained drawing replaces every world RTP, so they stayed at their defaults:
measured at `func_80035630` with retained drawing on, `ProjH` 320 with no vertex
ever seen, the GTE's own H **200**, OFX/OFY 160/120. Turning the Z-buffer off
blocks retained drawing, the packets project through RTP and publish 200, and the
value outlived turning it back on. The AO, normal, reflection and clip passes read
the same stale 320 while the world was retained.

Fix: `GpuWorld.Begin` publishes H and OFX/OFY from the GTE's control registers
(26, 24, 25) before the frame's view is read. Measured after: retained drawing on
every frame, 0 missed. **Not judged by eye.**

## Blending light and fog across tile edges (2026-10-05)

Each map half is lit and fogged from its own light record, so where two records
meet, the colour and the fog step at the tile edge. `KF3_NEIGHBOUR_BLEND=1` (Video ▸
*Blend light across tile edges*, key `kf3.neighbourblend`, **off by default**) blends
them, ported from the neighbour part of Verdite2's `EvenFog`. Its clipped-half fog
fix is not ported: this game has no view-space clipper. The inputs, measured on
this disc, are in `GAME_INTERNALS.md` ("Records across a tile edge"): 1.6% of drawn
halves sit beside a half whose pair differs, and no record is without fog.

### What is blended, and where

Runtime `0088` (`NeighbourBlend`, `tools/RecompOne/docs/RECOMPONE_PATCHES.md`) blends
**per pixel**, in `PrimFs`. The four records weighing in are those of the half the
pixel lies on and of the three halves around its quarter of the tile, on the same
level, with Verdite2's bilinear weights between tile centres. Here the weights come
from the pixel's world X and Z, not from a mesh corner: the own record alone at the
tile's centre, half and half on an edge, a quarter each at a corner. A missing half
(mesh byte 240 or more, or past the map's edge) weighs nothing, so both sides of a
shared edge or corner blend the same set. The quarter turn does not enter the
weights, since they come from world position. It enters only the own record's light
matrix, through the corner's dots, which `recordLit` already computes per turn.

Verdite2 blends at mesh corners, from each corner's sign of offset. A face that spans
a tile would then carry corner blends across its whole interior and never show its
own record. Computing per pixel avoids that, and avoids T-junction seams.

**The fog blends results, not records.** Each record's cue weight is evaluated at
the pixel's own depth (`LinearDepthCue`: quarter depth, truncating division, the
32000 cutoff, 7951 maximum), clamped at 4096, where the colour it leaves is black,
and those weights are averaged. Why: a record with no fog has no near/far pair to
average (its near is a sentinel); each record's weight is clamped, so a mean pair
would move both clamp points and draw a curve no tile has; and averaging the weights
is averaging the pictures the tiles draw, which is the intended result. The shared
0085 path averages DQA/DQB instead. For Verdite2's curves that is a mean taken
before the knee, and for this game's pairs it does not work. A pixel whose records
use another curve is left as it was.

**The light blends records**, as 0085 and Verdite2 do: the colour matrix and back
colour, each mean rounded half away from zero to an integer, under the own record's
light matrix, then `NormalColorCol`'s integers. NCCS is linear in both before its
clamps, so this equals blending results except where a clamp bites. The rounding
also makes a pixel just off a tile's centre give exactly the own record's colour, so
the blended region meets the unblended without a step.

A pixel whose weighing records all fog and light alike is drawn exactly as before.
A blended pixel is lit and fogged per pixel. With per-pixel lighting off, the rest of
the map keeps its corner-interpolated fog. Where the blend starts, at a tile's centre
lines, the two differ only by that interpolation's error.

Bulk and near faces are both the retained static mesh (`RetainedNear` only probes),
so they blend alike and no seam can form between them. Models, the sky and the arm
are not map halves and are untouched. Reflections and shadow passes clear the mode
(`EndWorldUniforms`).

### Wiring

`RetainedMap.UpdateHalves` hands the runtime every half's record each frame.
`NeighbourBlend.SetHalves` uploads only when one changed: a map mutation updates
the table as the chunks update. Records still upload through 0085 when their hash
changes. `RetainedMap.FogMixed/LightMixed` count the halves the blend can change.
The `[KF3] retained scene` line prints the mode, those counts and the record/half
uploads.

### Measured

- **Fixtures** (`tools/scene-probe/NeighbourFixtures.cs`). The half table, mutation and
  removal go through `RetainedMap.Update` with records in RAM. 3,096 points on shared
  edges and corners, on both levels, give the same fog (within 0.01 of 4096) and
  exactly the same light from either side, at six depths and three dot sets; 2,952 of
  them blend the fog and 2,340 the light. Also covered: a centre (own record only);
  0.01 units off a centre (the own colour exactly); a missing neighbour; a record
  without fog beside one with fog (half the weight on the edge); the upper level
  seeing only the upper level; the map's corner; a curve this does not blend. The
  source probe passes **7,092 assertions** (884 before).
- **GPU** (`scripts/shader_probe.py`, Radeon RX 9070 XT, offscreen). PrimFs's actual
  functions ran on 3,294 points: across edges, corners and centres, just inside each
  side, past the tile (a mesh reaches 10 units beyond it), the map's corner and the
  upper level. **0 mismatches**: the light is exact, and the fog is within 0.00035 of
  the CPU reference (1,638 fog-blended and 1,602 light-blended cases). The composed
  world program links, and every existing cue, pose and light case is still exact.
  Off by construction: `uNeighbour` 0 leaves `vNb` 0, and every changed expression
  reduces to the one before.
- **Live, all 28 areas** (scene driver, four headings from each arrival, blend on):
  28,455 main draws, **0 missed**, no blocker. The mixed-half counts match the offline
  corpus area by area (area 3 289/289, area 4 136/43, area 13 398/398, area 19 34/0).
  There were 30 record uploads and 43 half-table uploads in 29 loads. Blend off,
  areas 3/13/4: 3,983 draws, 0 missed.
- **Cost**: uncapped at the autostart view in fdat17, 1,029-1,058 fps with the blend
  on against 1,022-1,049 off, within the run-to-run spread. A view filled with
  blended tiles was not timed.

No Verdite2 run. Its vendored runtime predates `0086`/`0087` and the linear cue, so
it cannot take this change until it pulls them. Mode 0 changes nothing, as with
`0087`. **Not judged by eye.** No screenshots were taken.

**For the user to check**, with the switch on and off: light and fog running smoothly
across tile edges, in place of a step; no new seam between near and far floor, or at
doorways and between levels; tiles keeping their own look at their centres; and any
edge where the blend looks wrong. Area 13 (398 mixed halves), area 3 (289) and
area 11 (127) have the most blended edges; area 4's are mostly fog alone.

## Far scenery over the world (2026-10-06)

Reported with a screenshot: the "LODs" (a distant castle and a pale tree) drawn over
the stone walls of a room in front of them.

**What they are.** The model walk (`func_80040AE4`) sends an object whose flag byte
has `0x08` set to `func_8003F304`, the front-table submitter (call at `0x80041440`),
instead of the main submitter. The front table is linked to be drawn first, behind
everything ("The frame's tables" in `GAME_INTERNALS.md`), so these are far scenery the
game never depth-sorts: painter's order hides them behind any nearer surface. The
front submit is still a packet fallback (`front-table-policy-pending`), and in the
28-area census it happens only in **fdat14 (area 4)**: 5,408 submits from that one
call site.

**Why they drew on top.** The swap (`func_80035700`) points the main table's entry
8190 at the front table's entry 7 and the front table's entry 0 at what entry 8190
pointed to. Walked from the head: entry 8191 (slot 0), entry 8190 (slot 1), the front
table's eight entries (slots 2-9), then the rest. The retained main view is drawn at
slot 1, so it went in *before* the front table, and the front table's packets, with no
depth record, were then drawn with no depth test (zMode 0, or 3 with AO), over the
finished world.

**Fix** (runtime `0096`). `RetainedScene.UnderSlots`, which this port sets to 8: for
that many slots after a main view that drew, the walk sets `RetainedScene.UnderWorld`,
and a packet there with no recovered depth takes zMode 5: at the far plane, tested
`LEQUAL`, writing nothing, so it shows only where the main view left the far plane.
That is where painter's order would have left it showing (the retained sky writes no
depth, or the far plane with AO, so the castle still draws over the sky). The map's
water is not drawn ahead of such a packet, and a sprite there is not an overlay for
the reflection pass. A packet with a depth record keeps its own test. `KF3_GPU_UNDER=0`
is the walk as before. Verdite2 leaves `UnderSlots` at 0.

### Measured

- **Live, all 28 areas** (scene driver, eight guest and render headings at each
  arrival): 0 missed of about 26,400 main draws; triangles drawn under the world
  **only in area 4** (35,787), 0 elsewhere.
- **Occlusion** (`KF3_GPU_UNDER_PROBE=1`, area 4, guest and view yaw 0 and 512): from
  the arrival point the castle stands against open sky and all of its samples show,
  as before. With the eye moved 8,192 units in −X and −Z and 1,500 lower, **9.4% and
  10.0%** show: the world in front hides the rest, which before the fix was all drawn.
- The source probe passes 5,632,542 assertions; the shader probe links the four
  programs on the Radeon with every case exact.

**Judged fixed by the user** (2026-10-06): the castle and the tree no longer draw
over the walls in front of them.

## AO's box round billboards, again (2026-10-06)

With AO on, billboards had a faint dark, translucent box round them again. The fix
of 2026-10-05 (runtime `0058`'s amendment) made `NormalFs` drop a textured face's
transparent texels, but only for the table's packets: the retained map and models,
default since `253d90d`, go into the normal and surface buffers through
`WorldNormalVs`, which handed `NormalFs` no texel (`vTex = 0`). A transparent texel
of a retained face then wrote its normal at its own depth over the wall the depth
buffer holds behind it. `WorldNormalVs` now passes the texel, and the runtime
refuses to start (`GlShaders.RequireTexel`) if a normal program stops reading it.

### Measured

`KF3_AO=1 KF3_GPU_SURFACE_PROBE=1`, `KF3_AUTOSTART` slots 1-5, holding Left for 40 s
(about 1.5 turns), 12 surface checks each. `ahead` is the probe's count of opaque
surfaces in front of the depth, on its 4-pixel grid:

| save | area | `vTex = 0` put back | fixed |
|---|---|---|---|
| 1 | fdat17 | 0 | 0 |
| 2 | fdat14 | 0 | 0 |
| 3 | fdat05 | 784 | 0 |
| 4 | fdat41 | 0 | 0 |
| 5 | fdat05 | 1,239 | 0 |

`behind` was 15 and 16 in fdat17 either way, and `missing` 0 throughout. Not yet
judged by eye.

