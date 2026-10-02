# Verdite Core

The game-agnostic code shared by the Verdite ports, the static recompilations of
the PlayStation King's Field games built on
[the RecompOne fork](https://github.com/Voicedrew11/verdite-recompone):
[Verdite2](https://github.com/Voicedrew11/verdite2) (King's Field, `SLUS-00158`)
and [Verdite3](https://github.com/Voicedrew11/verdite3) (King's Field II in the
US, `SLUS-00255`).

Each game takes this repository as a `git subtree --squash` at
`tools/verdite-core`, pinned per game, so a fresh clone of a game builds with
nothing fetched and each game upgrades on its own schedule. An edit made inside a
game's `tools/verdite-core` is its own commit, touching nothing else, and is
pushed back here soon after (`scripts/setup_tools.sh --push-core` in the game).

**Nothing here knows a game.** Disc paths, overlay names, addresses, record
layouts, env var prefixes and settings keys come from the game, through its
`config/verdite.json` or a command-line flag. Code moves in only once a second
game needs it.

## What is here

`scripts/` holds the reverse-engineering tools a new game is brought up with.
Each reads the game's `config/verdite.json` (found by walking up from the current
directory, or from `VERDITE_GAME_ROOT`) for anything it needs to know about the
game; a flag overrides it.

| script | what |
|---|---|
| `inspect_disc.py` | the disc's ISO 9660 tree, SYSTEM.CNF and every PS-X EXE header |
| `extract_file.py` | one file off the disc, or just its EXE header |
| `add_call_targets.py` | splice the `jal` targets a linear sweep missed into a function map |
| `merge_branch_spans.py` | rejoin functions the sweep split at an interior `jr`/`j` |
| `merge_sdk_names.py` | write the PSY-Q names a signature match found into the function maps |
| `match_code.py` | a function's nearest counterparts in *another game's* executable, by structure (opcodes, GTE commands, record offsets, constants, calls), and two routines' calls aligned in order |
| `verdite_game.py` | finds the game's root and reads its `config/verdite.json` |

A game's `config/verdite.json` today:

```json
{
  "disc": "disc/KingsField2.cue",
  "recompilerConfig": "config/kf2.json",
  "funcmaps": "config/funcmaps",
  "sdkOverlays": ["open", "game", "end"]
}
```

`inspect_disc.py` and `extract_file.py` need none of it, so they run in a game
that has no config yet.
