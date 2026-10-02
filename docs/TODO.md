# TODO

Next steps and open questions. The first task is Phase 2 bring-up: get the game
recompiled and running, following Verdite2's method but applied to this disc.

## Phase 2: bring-up

- Obtain `disc/KingsField3.cue`, the user's own dump of `SLUS-00255`.
- Use `tools/verdite-core/scripts/inspect_disc.py` and `extract_file.py` to find the
  executables and their load bases.
- Write `config/kf3.json`, declaring the overlays and their addresses.
- Sweep a function map per executable into `config/funcmaps/`.
- Identify the PSY-Q functions with the signature bank, and list the
  executables that link PSY-Q in `config/verdite.json`'s `sdkOverlays`.
- Recompile.
- Boot.
- Reach gameplay.
- Save and load through the memory card.
- Record the acceptance test.
