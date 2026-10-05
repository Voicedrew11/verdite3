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

**Boots, plays, changes areas, saves and loads** (2026-10-02). `OPEN.EXE`,
`GAME.EXE`, `END.EXE` and 28 area code modules from `CD/COM/FDAT.T` are
recompiled, with 63 PSY-Q entry points bound by address. The port's own patches
are the agent harness (`KF3_AGENT`, `KF3_SHELL`, `KF3_AUTOSTART`, `KF3_AUTOPAD`)
frame pacing (`KF3_FPS`, a 15 Hz world, off until judged) and the two bulk
polygon assemblers in C# (`KF3_POLYASM`, verified, on), stage 15 and its camera block
in C# (`KF3_STAGE15`, verified, on) and the camera carried between ticks
(`KF3_SMOOTH`, judged, on under pacing), with the compass needle and the gauges;
the model walk and the MO pose blender in C# (`KF3_MODELWALK`, `KF3_MOPOSE`,
verified, on), the creatures, objects and their clip times carried between ticks
(`KF3_SMOOTH_MODELS`, judged, on under pacing) and the scrolling textures held
to the tick (`KF3_TEXSCROLL`); keyboard and mouse controls (`KF3_KEYS`,
`KF3_MOUSE`; `docs/INPUT.md`, not yet judged by eye); the
picture's 24-bit shading, no dither,
perspective, sub-pixel and a Z-buffer from the assemblers' depth records, with the
near path in C# (`docs/PICTURE.md`; all measured, none judged, all off); every
switch is live in Settings ▸ Testing. `tools/RecompOne` is a `git subtree`
of the shared fork `Voicedrew11/verdite-recompone`, based on `2013e51` (the vblank
event delivered once), with unpublished local retained-runtime checkpoint
`e09dc047` on `checkpoint/retained-linear-cue`, and
`tools/verdite-core` of Verdite Core at `a6c2434`. The acceptance test is in
`docs/DEVELOPMENT.md`; what is next is in `docs/TODO.md`.

**Retained GPU renderer in development** (2026-10-04): opt-in native scene and
persistent mesh/pose submission; all-area reference fixtures and shader probes.
Near/front and exceptional contexts remain open. No visual acceptance or full
GPU coverage is claimed; see `docs/GPU_RENDERER.md`.

## The documents

- [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md) — build, run and diagnose.
- [docs/RECOMPILATION.md](docs/RECOMPILATION.md) — config, overlays, function maps, SDK addresses.
- [docs/GAME_INTERNALS.md](docs/GAME_INTERNALS.md) — the game's own addresses and routines,
  including the geometry path (stage 15's calls, the map, the models).
- [docs/INPUT.md](docs/INPUT.md) — pad, keyboard and mouse: the mask table, turn/look, the layouts.
- [docs/GPU_RENDERER.md](docs/GPU_RENDERER.md) — retained scene implementation, measured coverage and open gates.
- [docs/GEOMETRY.md](docs/GEOMETRY.md) — the polygon assemblers in C#: the plan and its work.
- [docs/SMOOTHING.md](docs/SMOOTHING.md) — drawing between ticks: stage 15 in C#, the
  smoothers, all built and judged.
- [docs/PICTURE.md](docs/PICTURE.md) — 24-bit colour, perspective, sub-pixel and the
  Z-buffer: the plan and its work, built and awaiting judgement.
- [docs/WIDESCREEN.md](docs/WIDESCREEN.md) — the margin, the screen tints, the cull cone
  and the primitive buffer, ported from Verdite2.
- [docs/MODS.md](docs/MODS.md) — the mods under `mods/`: the debug tools (noclip,
  invincibility, the character, item and spell editors, area warp).
- [docs/ENV_VARS.md](docs/ENV_VARS.md) — every `KF3_*` switch.
- [docs/TODO.md](docs/TODO.md) — next steps: the Phase 2 bring-up.

## Sharing with Verdite2

The plan for sharing code between the Verdite games, its per-file inventory and
the progress log stay in Verdite2 (`~/Desktop/KFII-PC`): `docs/SHARING.md`,
`SHARING_PLAN.md`, `SHARING_INVENTORY.md`. The inventory is Verdite2's file list,
and the extraction steps change Verdite2 and are proved by its acceptance test.
They move to Verdite Core when it gets its first C#. A unit done here is written
up in this repo's own documents and logged in Verdite2's `docs/SHARING.md`.

## Where to write a new finding

The finding goes in the document, not in the commit message. Pick by what a
reader would be doing when they need it. If it fits nowhere yet, put it in the
general-purpose part of the nearest document.
