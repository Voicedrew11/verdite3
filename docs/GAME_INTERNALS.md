# Game internals

The reverse-engineered game: the main loop and its stages, player state,
movement, areas, saves and the boot stub — every address and routine this project
learns, written here rather than left in the commit that found it. Verdite2's
`docs/GAME_INTERNALS.md` is the model, but none of its addresses apply.

## Status

Little is known yet: what the bring-up needed.

## Area code modules

`GAME.EXE` keeps the resident area module's pointer at `0x8018FAE0` and calls
through its slots: `func_80044D9C` calls slot 8 (`+0x20`), the first call that
reached one. A module is loaded at `0x801E8308` from `CD/COM/FDAT.T` entry
`3n+2`, and the pointer is the base plus 4 (past a count word). Area n's data are
entries `3n` and `3n+1`. See "GAME.EXE loads code" in `docs/RECOMPILATION.md`.

## Saves

The save is one 3-block file, `BASLUS-002551`, on card A. Its title block reads
`KING'S FIELD 2-1 EXP 0 LV 1` in full-width Shift-JIS (a new game's first
save). The game formats an empty card on the memory card screen.
