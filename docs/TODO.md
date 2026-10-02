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

- **The world runs at the drawn rate, 60.** Verdite2's `FramePacing` (a 20 Hz
  world clock, the frame gate skipped) is the model, once this game's frame gate
  and stages are found.
- **Make the acceptance test a program**: scripted pad input, an auto start into
  a save slot, and a state beacon (Verdite2's `KF2_AUTOPAD`, `KF2_AUTOSTART`,
  `KF2_AGENT`), so a fork change can be checked without a person.
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
