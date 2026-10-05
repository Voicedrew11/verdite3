# Handoff: blending light and fog across tile edges

Written 2026-10-05, before any research for this unit. Everything under
"Known" comes from the documents named; everything else is to be found.

## The goal

Each map tile carries its own light record, so the colour and fog change in a
step at every tile edge. Port Verdite2's neighbour blending to the retained GPU
renderer: at each pixel, blend the record of the tile it lies on with its
neighbours', so the light runs smoothly across the edge. It is an enhancement:
a switch, **off by default until the user has judged it**, and off must give
exactly today's picture.

This is item 1 of the graphics not yet ported from Verdite2, and the open gate
"neighbour blending" in `docs/GPU_RENDERER.md`.

## Known

**In Verdite2** (`~/Desktop/KFII-PC`; read it for the method, never its addresses):

- `patches/EvenFog.cs` is the switch, with three parts. **Port only the
  neighbour blending.** Its fix for fog on clipped halves (`func_800302E8`) has
  nothing to correct here: this game has no view-space clipper
  (`docs/WIDESCREEN.md`).
- `patches/PolyAssemblerFog.cs` holds the neighbour selection and the bilinear
  weights. Its `TileBase 0x801C8484` (80 x 80 tiles, two 5-byte halves) is
  **Verdite2's map**, not this one.
- `docs/VERDITE3_GPU_PLAN.md`, "Lighting: reuse the mechanism, adapt the inputs"
  and Phase 5 steps 2-3, is the brief this unit follows. In short:
  - The neighbour selection and the weight logic are mostly reusable. The two
    maps share tile dimensions and quarter turns.
  - Adapt the record decoding, and blend the inputs this game's near/far fog
    needs.
  - **Blending two records and blending their evaluated results are not the
    same thing.** Choose one, and record which and why.
  - The final shader works at the fragment's own depth. `CurveWord`'s
    screen-affine weights are only good for a first prototype.
  - Do **not** bring over Verdite2's fog knee, visible-cell bits, arm bias,
    water texture rectangles or model-water classifications.
  - Handle missing neighbours, both half levels, quarter turns and shared
    boundaries. Off restores the native inputs.
- `docs/RENDERING.md` there has Verdite2's measurements and reasoning. Search
  it for EvenFog.

**Here:**

- `docs/GPU_RENDERER.md`, "Feature wiring checkpoint": "Neighbour controls are
  not yet wired." Static map corners carry source normals for record lighting,
  and must clear the model-only `FlagDots`, otherwise the corner is lit twice.
  The `recordLit` shader matches the GTE exactly over 48 NCCS fixture cases.
- The map assembler's source RGB is at scratchpad `+54` (the model families use
  `+64`). Non-neutral map RGB falls back to packets as `map-source-colour`.
- `patches/SceneFeatures.cs` holds the Video controls this switch joins: per-pixel
  lighting, fog from pixel depth (`RetainedScene.MainFogFromZ`, `KF3_FOG_DEPTH`),
  AO, normals and mipmaps. Each has English, pt-BR and es-419 labels.
- `patches/RetainedMap.cs` builds the map's chunks and uploads the light
  records. The neighbour data goes in beside them.
- The near map is retained too (`RetainedNear.cs`). Blending must cover the bulk
  faces and the near faces alike, or a seam will appear where one meets the
  other.

## To find first

1. **This game's tile and light-record layout**: the map base, the stride, the
   halves, the rotation bits, and what a record holds (colour, fog, near/far).
   Start at `docs/GAME_INTERNALS.md` and `RetainedMap.cs`, which already decodes
   records.
2. **How this game's fog is computed** for bulk and near faces, including the
   32000 cutoff and the clamps (Phase 5 step 2). Neighbour blending sits on top
   of that formula, so the formula has to be exact first.
3. **Where a pixel's tile can be found in the shader**: world position from
   depth, or a tile coordinate passed from the vertices. Check what
   `GlMainView`'s world shader already has.
4. **Whether records change at run time** (tile mutations, light changes). If
   they do, the neighbour data needs the same dirty updates as the records.

## Suggested order

1. Document the findings above in `docs/GAME_INTERNALS.md` (the layout) and in a
   new section of `docs/GPU_RENDERER.md`.
2. Choose the blend semantics (records or results) and record the choice.
3. Shared runtime (`tools/RecompOne`): the shader term and its uniforms or
   texture, neutral when off. **These go in their own commits**, pushed to the
   fork. Verdite2 shares the shader, so its acceptance test must still pass.
4. Game side: upload the neighbour data with the records, and add the switch
   (`KF3_NEIGHBOUR_BLEND`, or a name that matches its neighbours, plus a key
   `kf3.*`). Add a Video control in `SceneFeatures.cs` with all three languages,
   and a line in `docs/ENV_VARS.md`.
5. Measure, without screenshots:
   - Off: identical to the current output on the existing fixtures.
   - On: a fixture comparing values just inside two sides of a tile edge.
   - Cases with a missing neighbour, each quarter turn, and both half levels.
   - Retained draws still 0 missed (the `[KF3] retained scene` line).
6. Hand it to the user to judge by eye: lighting and fog transitions, seams
   between near and bulk faces, and any doorways or tile edges where it looks
   wrong.

## Rules that apply

- No screenshots or window capture. Measure with counters and fixtures, then ask.
- Nothing from Verdite2 is a fact here until it has been measured on this disc.
- Commit messages state the finding. Findings go in `docs/`.
