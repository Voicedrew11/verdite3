# TODO

Next steps and open questions. The first task is Phase 2 bring-up: get the game
recompiled and running, following Verdite2's method but applied to this disc.

## Phase 2: bring-up

- ~~Obtain the disc; find the executables and their load bases.~~ Done
  2026-10-02: see "What is on the disc" in `docs/RECOMPILATION.md`.
- Write `config/kf3.json`, declaring the overlays and their addresses.
- Sweep a function map per executable into `config/funcmaps/`.
- Identify the PSY-Q functions with the signature bank, and list the
  executables that link PSY-Q in `config/verdite.json`'s `sdkOverlays`.
- Recompile.
- Boot.
- Reach gameplay.
- Save and load through the memory card.
- Record the acceptance test.
