# Environment variables

Every `KF3_*` switch the port reads, in one list, with what each does and its
default. This fills in as diagnostics are added, the way Verdite2's
`docs/ENV_VARS.md` did. Add an entry here when a switch is added to
`Program.cs`.

## Status

| switch | what | default |
|---|---|---|
| `KF3_LOG` | the runtime's log channels: `bios`, `cd`, `gpu`, `dma`, `sdk`, `spu`, `mdec`, `irq`, or `all` | none |

The runtime still reads seven switches under Verdite2's prefix (`KF2_CDTRACE`,
`KF2_GLDEBUG`, `KF2_GTE_FAST`, `KF2_GTE_LIGHTCACHE`, `KF2_RAM_PROBE`, `KF2_SWAP`,
`KF2_VRAMCHECK`); they work here under those names until the fork takes the
prefix from the game.
