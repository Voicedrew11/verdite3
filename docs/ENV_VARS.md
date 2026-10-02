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

The runtime still reads seven switches under Verdite2's prefix (`KF2_CDTRACE`,
`KF2_GLDEBUG`, `KF2_GTE_FAST`, `KF2_GTE_LIGHTCACHE`, `KF2_RAM_PROBE`, `KF2_SWAP`,
`KF2_VRAMCHECK`); they work here under those names until the fork takes the
prefix from the game.
