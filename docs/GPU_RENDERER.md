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
GPU projection and lighting numeric fixtures; neighbour blending; solid blended
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

Shared runtime checkpoint: game-repo commit `04a9c9b` contains only
`tools/RecompOne`; its exact matching subtree commit is
`a339e6f715d6d83d7506fb55fdc040a61786b5c3`, pushed to the Verdite fork branch
`checkpoint/retained-depth-probes`. No upstream issue or PR was created.
Game adapter/probe/docs changes are committed separately from the shared runtime.
The user's pre-existing pacing/settings edits are preserved in their own commit.
