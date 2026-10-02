# Verdite3 — King's Field III (`SLUS-00255`) RecompOne port

Static recompilation of **King's Field III** (the Japanese numbering) — the game
released in North America as **King's Field II** — using
[RecompOne](https://github.com/BlackLabelHQ/RecompOne) (MIT).

**This file is the index.** It says what the project is and where it stands;
findings live in `docs/`, split by what you would be doing when you need them.

## Which game this is

The series was renumbered for the West, so the name is ambiguous:

| Chronological | Japan | North America |
|---|---|---|
| 1st (1994) | King's Field, SLPS-00017 | *not released* |
| 2nd (1995) | King's Field II, SLPS-00069 | King's Field, SLUS-00158 |
| 3rd (1996) | King's Field III, SLPS-00377 | King's Field II, SLUS-00255 |

This project targets the **third game**, `SLUS-00255`, the disc sold in North
America as "King's Field II". The sibling project Verdite2 targets `SLUS-00158`, a
*different game*; no address or finding carries between them. The user supplies
their own dump of `SLUS-00255`.

## Status

**Bootstrapping: nothing has been recompiled yet.** The repository holds the
license, the conventions, and `tools/RecompOne`, a `git subtree` of the shared
fork `Voicedrew11/verdite-recompone` pinned at `a617cf8`, and `tools/verdite-core` (Verdite Core) at `536167a`.
There is no `config/kf3.json`, no `generated/`, and no function map yet. The
disc is in place at `disc/KingsField3.cue` and has been read (see "What is on
the disc" in `docs/RECOMPILATION.md`). The bring-up order is in `docs/TODO.md`.

## The documents

- [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md) — build, run and diagnose.
- [docs/RECOMPILATION.md](docs/RECOMPILATION.md) — config, overlays, function maps, SDK addresses.
- [docs/GAME_INTERNALS.md](docs/GAME_INTERNALS.md) — the game's own addresses and routines.
- [docs/ENV_VARS.md](docs/ENV_VARS.md) — every `KF3_*` switch.
- [docs/TODO.md](docs/TODO.md) — next steps: the Phase 2 bring-up.

## Where to write a new finding

The finding goes in the document, not in the commit message. Pick by what a
reader would be doing when they need it. If it fits nowhere yet, put it in the
general-purpose part of the nearest document.
