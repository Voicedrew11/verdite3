# Environment variables

Every `KF3_*` switch the port reads, in one list, with what each does and its
default. This fills in as diagnostics are added, the way Verdite2's
`docs/ENV_VARS.md` did. Add an entry here when a switch is added to
`Program.cs`.

## Status

| switch | what | default |
|---|---|---|
| `KF3_LOG` | the runtime's log channels: `bios`, `cd`, `gpu`, `dma`, `sdk`, `spu`, `mdec`, `irq`, or `all` | none |
| `KF3_AGENT` | `1`: the `[KF3-AGENT]` state beacon on stdout | off |
| `KF3_SHELL` | `1` or a port: the command channel on `127.0.0.1:27903` | off |
| `KF3_AUTOSTART` | `1`..`15`: load that card A slot at boot; `new`: a New Game | off |
| `KF3_AUTOPAD` | `seconds:button:holdMs,…` from the first area load | none |
| `KF3_FPS` | frame pacing: the picture's rate, or `off` for uncapped; unset is no pacing | unset |
| `KF3_TICKRATE` | the world's rate under pacing; a comparison only | 15 |
| `KF3_FPS_PROBE` | `1`: a pacing line a second | off |
| `KF3_PACING_NOBOUNDARY` | `1`: leave the frame boundary unhooked, to test the watchdog | off |
| `KF3_STAGEPROBE` | `1`: which main-loop stages write the ordering table, every 5 s | off |
| `KF3_RATECENSUS` | seconds: which words change on frames no stage ran on (needs `KF3_FPS`) | off |
| `KF3_GEOPROBE` | `1`: what each of stage 15's calls adds to the ordering tables, by GPU command and slot, every 5 s; `time`: each call's inclusive time instead (its hooks cost about 0.4 ms a frame) | off |
| `KF3_GEOPROBE_FUNCS` | `hex,hex,...` (up to 16): more functions for `KF3_GEOPROBE`, reported per call site | none |
| `KF3_POLYASM` | the bulk polygon assemblers `func_80039D50` and `func_80035CA4` in C#: `1` (or unset), `0` recompiled, `verify` both on every call, compared (RAM, scratchpad, registers, GTE), a report every 2 s | on |
| `KF3_POLYASM_MAP` | `0`: `func_80039D50` recompiled, the rest as `KF3_POLYASM` says | on |
| `KF3_POLYASM_LIT` | `0`: `func_80035CA4` recompiled | on |
| `KF3_STAGE15` | stage 15 `func_800422B8` in C#: `1` (or unset), `0` recompiled, `verify` records the recompiled routine at every call and replays this one against it, a report every 2 s | on |
| `KF3_STAGE15_NEEDLE` | `0`: step the compass needle's spring every drawn frame, as the routine does (held to the world tick by default) | held |
| `KF3_CAMERABLOCK` | the camera block `func_800357E8` in C#: `1` (or unset), `0` recompiled, `verify` both on every call, compared | on |
| `KF3_SMOOTH` | `0`: draw each frame from the last tick's camera instead of the one interpolated between the last two ticks (smoothing runs only under `KF3_FPS`, and needs stage 15 in C#); judged 2026-10-02 | on |
| `KF3_SMOOTH_MODELS` | `1`: draw each creature, object, effect and billboard at its position, facing and clip time interpolated between the last two ticks (runs only under `KF3_FPS`, and needs `KF3_MODELWALK` on; the clip time also needs `KF3_MOPOSE`); not judged by eye | off |
| `KF3_SMOOTH_PROBE` | `1`: a line a second: frames drawn and cameras carried, tick samples and snaps; models carried, snaps, clip frames carried, wraps, turns, re-seeks and backward steps | off |
| `KF3_SPRITEANIM` | `0`: let the billboard cels step on every drawn frame under pacing (held to the tick by default; `KF3_FPS_PROBE=1` prints walks stepped and held) | held |
| `KF3_TEXSCROLL` | `0`: run the scrolling textures (`func_800351FC`) on every drawn frame under pacing; `carry`: also redraw them each frame at the phase interpolated between ticks (not judged). `KF3_FPS_PROBE=1` prints calls, runs and carried uploads | held |
| `KF3_MODELWALK` | the model walk `func_80040AE4` (creatures, objects, effects, billboards) in C#: `0` recompiled, `verify` both on every call, compared (RAM, scratchpad, registers, GTE); verified 2026-10-02 | on |
| `KF3_MOPOSE` | the MO pose blender `func_800431E8` in C#: `0` recompiled, `verify` both on every call, compared (RAM, scratchpad, registers, GTE); verified 2026-10-02 | on |

The runtime still reads seven switches under Verdite2's prefix (`KF2_CDTRACE`,
`KF2_GLDEBUG`, `KF2_GTE_FAST`, `KF2_GTE_LIGHTCACHE`, `KF2_RAM_PROBE`, `KF2_SWAP`,
`KF2_VRAMCHECK`); they work here under those names until the fork takes the
prefix from the game.
