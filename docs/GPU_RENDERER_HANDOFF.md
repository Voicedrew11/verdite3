# Retained GPU renderer: next-session handoff

## Status (2026-10-05, second session)

The user reports the floor gaps below are gone. They then reported floor drawn over
creatures and objects ("floor tiles behind a plant enemy show above it",
"ultimately a z fighting issue"). Measured cause: near map packets drawn after the
retained models at slot 1 won their pixels through the depth tolerance. Fixed by
runtime `0086` (model stencil mask). See "Models under later packets" in
`GPU_RENDERER.md` for the mechanism, the 28-area tour and the cost.

**Uncommitted**: the bulk-map correction below, `0086` (shared subtree:
`tools/RecompOne`, its own commit, then push to the fork), the `gpu` shell command,
the switches, `scripts/model_mask_tour.py` and these documents.

## Next slice

1. The user checks by eye that creatures and objects on near floor no longer have
   floor over them. If some still do, the remaining candidate measured so far is
   the retained map in front of a model by more than the tolerance (up to about
   0.1% of model samples within 960 units). Under the packets, the table's 0xF0-slot
   tile bias drew the model over that floor. Run `scripts/model_mask_tour.py` first.
2. Performance: no valid per-stage timing exists yet (see "Baseline"). Measure
   Retained GPU against packets at fixed views with the GPU frame timers before
   optimising. Under the mask, at the worst tour view: 617 fps uncapped.
3. Then persistent near subdivision (below). It would also remove the packets
   `0086` guards against.

## Earlier slice: map-half ownership at one missing-floor view (superseded)

On 2026-10-05 the user supplied an image showing large, sharply bounded black
gaps between textured floor sections. The user reports that this specifically
happens with **Scene renderer = Retained GPU** and **Native scene reference =
C#**. Treat this as a reported visual correctness failure and prioritize it
before implementing persistent near subdivision or front-table barriers.

The image also has a black background, but missing sky has not been established
by a reference comparison. Area, position, heading, full feature settings and
exact build used for the image were not recorded. Do not infer them from the
HUD or assume the area-5 numerical fixture reproduces the problem. The image
was user-supplied; agents did not capture the game window. The original report
was followed by a documentation-only handoff. The continuation then corrected
an independently demonstrated bulk-map GT3 extraction defect, described below;
the floor-gap cause remains unknown.

Keep the next session bounded: **one reproducible view, one map-half ownership
trace, and a correction only if the trace establishes a cause**. If the original
view is still unavailable, finish with a reusable opt-in diagnostic and a clear
account of the missing evidence. Do not begin another all-area survey or expand
near/front rendering in this slice. Prefer working without subagents.

## Start here

Continuation (2026-10-05): a contained extraction correction separates bulk-map
GT3 vertex references from the model/near layout. The new bulk fixture fails
against the old build at retained corner 1 versus the recompiled packet; details
are in GPU_RENDERER.md's "Bounded bulk-map decoding correction". This is not a
proven fix for the reported gaps: none of the saved 28-area map fixtures contains
GT3. Keep the floor-gap reproduction and ownership trace as the next priority.

Read `NOTES.md`, `docs/GPU_RENDERER.md`, `docs/DEVELOPMENT.md` and repository
guidance. Verdite3 targets SLUS-00255; sibling Verdite2 addresses do not apply.
Current branch is `main`, HEAD `54238a7`. **The bulk-map correction is still
uncommitted**, so HEAD alone does not contain the current implementation.
The existing tick-rate settings were separately committed as `8e47b33`.

Preserve these current changes:

- `patches/RetainedAssets.cs`: separate `Family.MapBulk` cache entries; bulk
  GT3 vertex stride differs from its normal stride, and bulk GT4 remains skipped.
- `patches/RetainedMap.cs`: map extraction requests `MapBulk`; the opt-in near
  descriptor probe explicitly requests `Lit` to preserve near/model decoding.
- `tools/scene-probe/BulkMapFixtures.cs` (untracked) and `Program.cs`: new
  comparisons against packets emitted by recompiled `80039D50`.
- Dirty `NOTES.md`, `docs/GPU_RENDERER.md`, `docs/TODO.md`, and this untracked
  handoff. Some documentation edits predate the completed code slice.

No shared subtree was edited in the completed slice. Release builds and all
879 assertions pass. The old assembly fails the new fixture at
`bulk 34/0: retained corner 1 differs from recompiled packet`. The new 340
assertions cover eight opaque/semi commands, four retained tile rotations,
UV/normal associations, GT4 skips and separate model/near cache entries.

The shared runtime checkpoint is game-repo commit `04a9c9b`, exclusively
`tools/RecompOne`. Its exact subtree commit is
`a339e6f715d6d83d7506fb55fdc040a61786b5c3`, pushed to the Verdite fork branch
`checkpoint/retained-depth-probes`. Shared edits still require subtree-only
commits and matching fork commits; no upstream issues or PRs.

## Investigation and bounded implementation

1. Obtain the user's area/position/heading and feature settings if needed.
   While waiting, prepare a small opt-in ownership diagnostic. Reproduce from
   copied cards/settings and an isolated copy of the built output, one bounded
   game process at a time. Set `KF3_TICKRATE=15` for measurements.
   Use beacon/shell state to confirm the loaded area. Compare reference packets
   with the same C# native reference against retained GPU at the same view;
   then use the recompiled reference to distinguish native submission from
   retained-only behavior. Disabling native scene can block retained rendering,
   so it is not an independent retained-GPU comparison.
2. Trace a small set of affected map halves at that one view. Emit a bounded
   report after loading has settled. For each sampled half, relate its guest
   address/tile/upper-half ID, source commands, mesh availability, published
   static corner count, near/bulk route, fallback reason, packet suppression,
   retained half gate and frame/MainSerial. Match frame serials rather than
   treating cumulative draw totals as proof of ownership. Document any new
   diagnostic switch in ENV_VARS.md.
   Establish whether a missing face was never published, gated/cull-rejected,
   suppressed without a retained draw, or drawn and subsequently overwritten.
3. Follow only the first broken ownership condition established by the trace.
   Fix a proven cause while preserving packet fallback and guest side
   effects. Keep the patch focused on this regression. Diagnostic changes to
   culling, depth or drawing all halves are comparisons, not acceptance fixes.
4. Add a meaningful source/state fixture or targeted numerical probe for a
   demonstrated failure. Run the existing checks below, then ask the user to
   confirm the floor gaps are gone at the original view and while turning.
   Record the mechanism, measurements and remaining limits in GPU_RENDERER.md.
   If no cause is established, finish with the bounded diagnostic and update
   this handoff with the specific missing evidence. Stop the slice there.

Source entry points for the investigation:

- `patches/NativeSceneHalf.cs`: `RunHalf` calls `RetainedMap.Submit` on the near
  and bulk branches, then skips the legacy vertex/packet loop when it returns
  true. It must retain the original epilogue and guest side effects.
- `patches/RetainedMap.cs`: `Update`, `BuildChunk`, `Submit`; source mesh family,
  vertex references, tile rotation/height, flags, `NoteHalf` and fallback gates.
- `patches/RetainedAssets.cs`: command-specific source decoding and topology.
- `patches/GpuWorld.cs`: frame lifetime, capture/drawing decisions and blockers.
- `tools/RecompOne/RecompOne.Runtime/Gpu/RetainedScene.cs`: `HalfFlag`,
  `NoteHalf`, `MainHalves`, frame ring and serials.
- Shared `Gpu/Backends/Common/GlMainView.cs`, `GlRetained.cs` and composed world
  shader sources: target selection, half gates, winding/culling, near clipping,
  projection and ordering against later packet passes.

These are investigation candidates, not established causes. The earlier depth fix
addressed unrecorded legacy near packets stamping far-plane depth with AO on;
that cause has not been connected to these visible floor gaps.

## Existing evidence and checks

Selected native inner verification across all 28 areas/four actual guest
headings recorded 478,515 full CPU/stack/call-order comparisons with zero
mismatches. Front had only one live call. **Native verification disables
retained capture**, so those results do not validate the retained suppression
branch. Thirteen synthetic front/cell comparisons check packet output and full
guest state; their near-cell case rejects rather than drawing.

The combined source probe now passes 879 assertions, including 340 new bulk-map
checks. The latest composed-program check links under llvmpipe, with 270 fog
cases, 27 pose vertices and 48 native GTE light cases exact; the preceding
checkpoint also linked on Radeon. Bounded retained area-5 runs have nonzero
main/final depth coverage and
zero missed main draws, but cannot establish that every intended face appears.
AO-on still had 66 missing/four behind-depth samples in one five-check run.

```bash
dotnet build KingsField3Recomp.csproj -c Release
dotnet build tools/scene-probe/SceneProbe.csproj -c Release
dotnet tools/scene-probe/bin/Release/net10.0/SceneProbe.dll \
  "$PWD/bin/Release/net10.0" /tmp/verdite3-gpu-next-fixtures
python3 scripts/shader_probe.py /tmp/verdite3-gpu-next-fixtures
git diff --check
```

Run the commands sequentially: finish the game build before running the probe.
Generate ignored C# first if absent, using the existing config/disc workflow.
If the shared runtime changes, repeat bounded Verdite2 compatibility as well.
Temporary evidence from the preceding session is under
`/tmp/verdite3-next-phase`; it may disappear and is not a required input.
Latest passing source fixtures/shaders are under `/tmp/verdite3-bulk-map-after`,
the failing old-build run used `/tmp/verdite3-bulk-map-before`, and the build log
is `/tmp/verdite3-bulk-map-build.log`. The older corpus scan used
`/tmp/verdite3-gpu-reference/corpus/area*.ram`; these snapshots contain only map
`24/26/2C/2E`, so they do not connect the GT3 defect to the reported gaps.

Useful switches: `KF3_GPU_WORLD=1`, `KF3_NATIVE_SCENE=1`,
`KF3_GPU_SURFACE_PROBE=1`, `KF3_GPU_CENSUS_FILE=/tmp/...json`,
`KF3_GPU_NEAR_PROBE=1`, and beacon/shell switches from DEVELOPMENT.md.
Actual GPU mode requires native near depth: unset/blank `KF3_NEARPATH` selects
native near; explicit `0`/`verify` or disabled map/model near subfamilies yield
`near-depth-disabled` and packet rendering. Disabling near therefore cannot
isolate a retained near failure while keeping retained drawing active.

Use counters/logs and offline numerical probes. Visual judgment belongs to the
user; do not capture or scrape the game window. The corpus driver holds player
physics after warp. `--guest-yaw --headings 4` changes the guest yaw as well as
the render view; render-only headings do not exercise the front submit's yaw
gate. These synthetic area fixtures are not ordinary gameplay acceptance.

## Deferred title-screen issue

The user reports being unable to get to Continue and explicitly asked to leave
it for now. **Do not investigate or alter it in this renderer slice.** Read-only
checks confirmed root `carda.sav` has five occupied SLUS-00255 save slots
(levels 12, 6, 14, 18, 18) and matches the two temporary card copies byte for
byte. Card B has no occupied directory entries. Root settings enable both cards
and use relative `carda.sav`/`cardb.sav` paths. This confirms the files exist;
it does not diagnose Continue. No cards or settings were changed. The completed
renderer verification used synthetic fixtures, without launching the game.

If Continue prevents reproducing the floor view, record that limitation and
continue independent diagnostic work rather than expanding into the deferred
menu/input issue.

## Work after the floor fix

After the visible regression is fixed, the planned implementation slice is
persistent **source-space near subdivision meshes/poses**, preserving exact
midpoint UV/colour, topology and ordering, followed by generic front/table
barriers. `RetainedNear.cs` is only a read-only descriptor probe; it does not
draw or suppress near packets. Its descriptors cover source corner depth,
mean, level and direct OTZ key, not facing/screen/subdivision culls. Near map
GT4 is reference-skipped; near model GT4 is supported. Near-map GT3 vertex
references are body+0xE/+0x12/+0x16, unlike bulk map GT3. Keep per-frame
transformed-vertex capture out of the retained implementation.
