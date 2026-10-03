# TODO

Next steps and open questions. The first task is Phase 2 bring-up: get the game
recompiled and running, following Verdite2's method but applied to this disc.

## Phase 2: bring-up

- ~~Obtain the disc; find the executables and their load bases.~~ Done
  2026-10-02: see "What is on the disc" in `docs/RECOMPILATION.md`.
- ~~Write `config/kf3.json`, declaring the overlays and their addresses.~~
- ~~Sweep a function map per executable into `config/funcmaps/`.~~
- ~~Identify the PSY-Q functions with the signature bank, and list the
  executables that link PSY-Q in `config/verdite.json`'s `sdkOverlays`.~~
- ~~Recompile. Boot. Reach gameplay. Save and load through the memory card.
  Record the acceptance test.~~ Done 2026-10-02: see "The acceptance test" in
  `docs/DEVELOPMENT.md`. The picture is the user's to confirm.

## Next

- ~~The world runs at the drawn rate.~~ It ran at 30, twice the game's 15 (the
  vblank delivered twice); frame pacing holds it to 15 at any rate, off until
  judged. See "Frame pacing" in `docs/DEVELOPMENT.md`.
- ~~Make the acceptance test a program.~~ Beacon, command channel, auto start
  and scripted pad, 2026-10-02. Still by hand: changing areas, saving, the
  title-screen load.
- ~~The vblank event is delivered twice a vblank.~~ Fixed in the fork,
  `2013e51` (amends `0021`; `0825391` here), 2026-10-02: 60.0 a second.
- **What stage 15 advances runs at the render rate under pacing**: the billboard
  cels and the compass needle are held to the tick (2026-10-02, `docs/SMOOTHING.md`);
  the scrolling textures too since; the unidentified words in "What still runs at the render rate" in
  `docs/GAME_INTERNALS.md` remain. The loops that draw their own frames, the
  menu, the loading screens and the bottom message box are held since
  2026-10-02 (`LoopPacing`, `VBlankPacing`, `MessageBoxHold`); **to judge by eye:
  an item pickup's spin, a coin pickup's message box, the menu's cursor repeat,
  and the fades.**
- **The geometry path in C#** (the sharing plan's picture features rest on it):
  surveyed, see "The geometry path" in `docs/GAME_INTERNALS.md`; the build order
  is in Verdite2's `docs/SHARING.md` (2026-10-02, the geometry survey, and its
  handoff). `func_80039D50` and `func_80035CA4` are C# and verified
  (`KF3_POLYASM`, 2026-10-02; "The first unit" in `docs/GEOMETRY.md`). The near
  path (`func_8003AB04`, `func_800366A8` and libgte's division) and the Z-buffer
  are next on this path, now that smoothing is done, as units 3 and 4 of
  **`docs/PICTURE.md`, the next work** (24-bit colour, perspective, sub-pixel and
  the Z-buffer, planned 2026-10-02).
- ~~**Carrying the view between ticks**~~ (chosen 2026-10-02, ahead of the near
  path). Done 2026-10-02: units 1-3, the camera, the HUD and the creatures with
  their clip times, verified and judged, on under pacing; every switch is live in
  Settings ▸ Testing. See `docs/SMOOTHING.md`; its leftovers are put off.
- **Widescreen**: built 2026-10-02 (`docs/WIDESCREEN.md`): the margin, the tints
  and the cull cone (verified cell for cell), off until judged. **To judge by eye**:
  the margins in play, fades and flashes at the edges, tiles appearing at the sides
  as you turn. The primitive buffer is re-asked in a busier area than `fdat02`.
- **Keyboard and mouse**: built 2026-10-02 (`docs/INPUT.md`); the pitch direction,
  sensitivity and feel are to be judged; the menu pointer (Verdite2's `MenuMouse`)
  and twin-stick analog are not ported.
- **`load` and `warp` for the command channel**: the loader is known; how the
  in-game Load re-enters the area is not.
- `KF3_PRESENT_PROBE`: the fork reads `KF2_PRESENT_PROBE` from Verdite2's
  `Program.cs` wiring, which this `Program.cs` does not have.
- **The fork reads seven `KF2_*` switches by name** (see `docs/ENV_VARS.md`).
  Taking the prefix from the game is a fork change, and Verdite2's acceptance
  test must pass after it.
- The intro and ending movies are played but were never looked at closely; the
  ending (`END.EXE`) has not been reached.
- 47 duplicate entry points from the recompiler's escape scan in `game` (see
  "The function maps" in `docs/RECOMPILATION.md`).

## Open questions

- Where `GAME.EXE` names `0x801E8308` as the module destination: no
  `lui`/`addiu` literal of it exists. The base is scored, and the run confirms
  it, but the loader is not read yet.
- The data entries past 95 in `FDAT.T` (large, no pointer table) are unread.
