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
  cels at `0x80182964` first (Verdite2's `SpriteAnim`), then the unidentified
  words in "What still runs at the render rate" in `docs/GAME_INTERNALS.md`.
- **Carrying the view between ticks** (Verdite2's `FrameSmoothing` and the rest),
  without which a higher rate draws the same picture several times.
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
