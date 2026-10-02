# Recompilation: config, function maps and SDK addresses

How the recompiler turns the disc's MIPS into C#: the overlays in
`config/kf3.json`, the swept function maps under `config/funcmaps/`, and mapping
PSY-Q entry points by address to the runtime's HLE. Verdite2's
`docs/RECOMPILATION.md` is the model, and it documents the traps to expect here
too — overlays that share an address range, entry points a linear sweep misses,
and `ScanCrossImage` splitting one function into several. None of those is yet
known to apply to this disc; they are what to look for, not findings.

## Status

Nothing recompiled yet. The disc has been read; the overlays below are what
`config/kf3.json` will declare.

## What is on the disc

Read with `tools/verdite-core/scripts/inspect_disc.py disc/KingsField3.cue`
and `extract_file.py … --header-only` (2026-10-02). The user's dump came as
`King's Field II (USA).cue/.bin`. It was renamed to `disc/KingsField3.cue` and
`.bin`, with the cue's `FILE` line changed to match. The `.bin` is 571,766,496
bytes, SHA-1 `131d2574f6ea101823193845f001bb58cdd3ed5e`. The volume id is
`SLUS-00255`.

`SYSTEM.CNF` boots `cdrom:\SLUS_002.55;1` (`TCB = 4`, `EVENT = 32`,
`STACK = 80200000`).

| file | LBA | file size | entry pc | text addr | text size |
|---|---|---|---|---|---|
| `SLUS_002.55` | 38 | 0x1000 | `0x80010120` | `0x80010000` | 0x800 |
| `OPEN.EXE` | 40 | 0x30000 | `0x800136C8` | `0x80011000` | 0x2F800 |
| `GAME.EXE` | 136 | 0x8C000 | `0x800144F8` | `0x80011000` | 0x8B800 |
| `END.EXE` | 416 | 0x27000 | `0x80011D04` | `0x80011000` | 0x26800 |

**The same shape as Verdite2's disc.** There is a 4 KiB boot stub and three real
executables. All three load at `0x80011000`, so they are mutually exclusive and
are declared as overlays, each with `"skip": 2048` to step past the 0x800-byte
header, and each address-based config entry has to name its overlay. `GAME.EXE`
is much larger than Verdite2's: 0x8B800 bytes of text against 0x5E000. The
other two are close in size (Verdite2: `OPEN.EXE` 0x2E000, `END.EXE` 0x29000).

The rest of the disc:
- `CD/COM/*.T` archives: `MO`, `MOF`, `VAB`, `RTIM`, `FDAT`, `RTMD`, `ITEM`,
  `TALK`, `STALK`. Going by Verdite2, `FDAT.T` probably holds the per-area code
  modules. That is unchecked.
- `DRM/D00.S`-`D17.S` (18 files, 0x17F800 each).
- `STR/S03.S`-`S15.S` and `OP/` (`L0`, `L1`, `M0`-`M6` `.S`, `OP.D`). These
  look like streamed audio and video. That is also unchecked.
- `LICENSEA.DAT`, `LOAD.MSG`, `COPY.TXT`.
