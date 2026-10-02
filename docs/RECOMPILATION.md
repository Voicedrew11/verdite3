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

## The function maps

Swept 2026-10-02, then cleaned in this order, which matters:

```bash
RC="dotnet run --project tools/RecompOne/RecompOne.Recompiler -c Release --no-build --"
$RC --generate-function-file -linear-sweep -disc disc/KingsField3.cue \
    -file GAME.EXE -base 80011000 -skip 800 -out config/funcmaps/game.json
# (OPEN.EXE, END.EXE the same; SLUS_002.55 at -base 80010000)
# 1. cut each map at the end of code (below)
# 2. python3 tools/verdite-core/scripts/add_call_targets.py disc/KingsField3.cue GAME.EXE config/funcmaps/game.json
# 3. python3 tools/verdite-core/scripts/merge_branch_spans.py
```

| map | swept | after the cut | after harvest | after merge |
|---|---|---|---|---|
| `main` | 9 | | | 9 |
| `open` | 496 | 484 | 486 | 485 |
| `game` | 1203 | 1194 | 1214 | 1156, then 1155 by hand |
| `end` | 370 | 365 | 366 | 366 |

**Each executable's text runs on past its code into data, and the sweep makes
functions of it.** The last real function ends at `0x8002C1B8` in `OPEN.EXE`,
`0x8007E4C4` in `GAME.EXE` and `0x800232A0` in `END.EXE` (the last `jr ra`, and
no `jal` from code lands past it); the text runs to `0x80040800`, `0x8009C800`
and `0x80037800`. What follows is tables of halfwords that decode as `jr at`,
`jr ra` and branches. In `GAME.EXE` that data held 11 "functions", one of them
11796 bytes long, whose "branches" reach back into real code, so
`merge_branch_spans` merged half the executable into one function. The cut has
to come **before** the harvest, or `add_call_targets` harvests `jal`s out of the
data functions. `GAME.EXE`'s text also *starts* with data: the first function is
at `0x800144F0`, and `0x80011000`-`0x800144F0` holds read-only data, among it
the jump tables (one at `0x800136A0`).

**The sweep's own call discovery reads data too.** A `jal`-shaped data word made
false starts in the middle of straight-line code, e.g. `0x80040004`, inside a
loop of `func_8003F304` whose `bne` at `0x80040034` branches back across them.
`merge_branch_spans` rejoins those; one it cannot see, `0x80040E3C`, inside
`func_80040AE4` (no prologue, nothing calls it, reached only by fallthrough), was
rejoined by hand: `0x80040AE4` size 4080. A check that finds this class: a start
that no code `jal`s or `j`s to and that the instruction before it runs on into.
Run after any re-sweep; it finds none now in any of the three.

Not every fallthrough start is false: libgte's routines have two entry points
where one sets up registers and runs on into the other (`0x80075188` into
`0x80075190`), and both are called. They stay split; the recompiler emits the
fallthrough as a call.

**`merge_branch_spans` needed three fixes for this disc** (Verdite Core, its own
commit; Verdite2's maps merge to the same bytes):

- A switch table was read until a word stopped looking like a label. The table
  at `0x800136A0` has 17 entries, and the word after it, `0x8009C354`, points
  into the data at the end of `GAME.EXE`'s text, so the read ran on. A table is
  now bounded by the `sltiu` that guards its index (`sltiu v0, a1, 0x11`).
- The `jal` check counted data words as calls. Only sites inside a known
  function count now, the rule `add_call_targets` already had.
- An absorbed start reached by fallthrough was reported as lost. It is the merged
  function's next instruction.

The recompiler's escape scan still adds 47 entry points in `game` and 1 in
`open`, all at labels already inside merged functions, which are emitted as a
label and also as a separate callable copy. Harmless, but each is a duplicate
of code in the merged function.

## The SDK entry points

`--autoconfigure` (upstream's PSY-Q signature bank, fetched into
`tools/RecompOne/RecompOne.Recompiler/AutoConfigure/signatures/`, gitignored)
named 188 of 496 functions in `OPEN.EXE`, 250 of 1203 in `GAME.EXE` and 157 of
370 in `END.EXE`. `merge_sdk_names.py --auto <dir>` wrote 516 of those into the
maps; 76 it refused because `SdkPatches` would bind them, and 3 because they
matched twice.

**This disc links a newer PSY-Q than Verdite2's.** Not one of Verdite2's
identified routines matches here by relocation-insensitive normal form, not even
the 8-instruction libcd thunks, so Verdite2's addresses and deltas are useless
and each identification was made again. Within this disc the overlay delta works
as it did there, per object:

| object | `game` − `open` | `end` − `open` |
|---|---|---|
| libcd `sys` (CdSync..CdRead) | `+0x46DE0` | `-0x2E88` |
| libgpu | `+0x63A38` | `-0x22A4` |

`patches[]` binds the same 21 entry points Verdite2 binds, in all three: 63.
The recompiler reports `applied 63 patches, 0 reimplementations`.

| function | `open` | `game` | `end` | how |
|---|---|---|---|---|
| `VSync` | `0x8001FE6C` | `0x8007910C` | `0x8001C208` | signature |
| `CdInit` | `0x8001AD4C` | `0x80064650` | `0x80017318` | signature |
| `CdSync` | `0x8001AF98` | `0x80061D78` | `0x80018110` | calls `CD_sync`; delta |
| `CdReady` | `0x8001AFB8` | `0x80061D98` | `0x80018130` | calls `CD_ready`; delta |
| `CdControl` | `0x8001B020` | `0x80061E00` | `0x80018198` | `CD_cw(com, param, result, 0)` |
| `CdControlF` | `0x8001B168` | `0x80061F48` | `0x800182E0` | `CD_cw(com, param, 0, 1)` |
| `CdControlB` | `0x8001B2A4` | `0x80062084` | `0x8001841C` | `CD_cw` then `CD_sync(0, result)` |
| `CdGetSector` | `0x8001B41C` | `0x800621FC` | `0x80018594` | signature; returns `(r < 1)` |
| `CdReadSync` | `0x8001B484` | `0x80062264` | `0x800185FC` | wraps the sync whose timeout names `CD_read` |
| `CdRead` | `0x8001B4A4` | `0x80062284` | `0x8001861C` | retries the internal that sizes by `mode & 0x30` |
| `DrawSync` | `0x80016168` | `0x80079BA0` | `0x80013EC4` | signature |
| `DrawOTag` | `0x800166CC` | `0x8007A104` | `0x80014428` | signature |
| `PutDrawEnv` | `0x80016740` | `0x8007A178` | `0x8001449C` | signature |
| `PutDispEnv` | `0x8001683C` | `0x8007A274` | `0x80014598` | `0x260`, `0x1F4`, `0xCDA` |
| `StUnSetRing` | `0x8001D880` | `0x80064758` | `0x80017430` | signature |
| `StSetStream` | `0x8001D96C` | `0x80064844` | `0x8001752C` | signature |
| `StSetRing` | `0x8001EFA4` | `0x80065E6C` | `0x8001B330` | signature |
| `StClearRing` | `0x8001EFD4` | `0x80065E9C` | `0x8001B360` | signature |
| `StGetNext` | `0x8001F034` | `0x80065EFC` | `0x8001B3C0` | signature |
| `StFreeRing` | `0x8001F160` | `0x80066028` | `0x8001B4EC` | signature |
| `DMACallback` | `0x8001F27C` | `0x80078684` | `0x8001B608` | signature |

What differs from Verdite2's library: `CdControl`, `CdControlF` and `CdControlB`
are full functions with their own retry loop, not thunks; the public `CdRead`
is a three-try wrapper round an internal `cd_read(buf, sectors, mode)`
(`0x8001D018` in `open`) that does the sizing; `StUnSetRing` and `StSetStream`
sit beside `CdRead2`, away from the rest of the ring. The public libcd routines
are what is bound, so the internals never run.

Not bound, as in Verdite2: the `libgpu` image routines (`LoadImage`, `MoveImage`,
`ClearImage`, `StoreImage`) and libpress (`DecDCTin` is named in all three, and
`DecDCToutCallback` in `END.EXE`).

### The interrupt-callback table

libapi here is the 4.x interrupt manager. `InterruptCallback`, `DMACallback`,
`ResetCallback` and `VSyncCallback` are 12-instruction thunks that call through a
struct of function pointers (`*0x8003F9CC` in `open`, `0x8003F9AC`; `+4`
DMACallback, `+8` InterruptCallback, `+0xC` ResetCallback, `+0x1C` the
interrupt environment). The real `InterruptCallback` (`setIntr`, `0x8001F678` in
`open`) indexes `env + 4` by `irq*4`, and `env + 0` is the "initialised" flag.
That is the layout upstream's fallback assumes, but `Program.cs` sets the table
per executable anyway:

| overlay | `setIntr` | env | table |
|---|---|---|---|
| `open` | `0x8001F678` | `0x8003E944` | `0x8003E948` |
| `game` | `0x80078A80` | `0x8009AF98` | `0x8009AF9C` |
| `end` | `0x8001BA04` | `0x80035A0C` | `0x80035A10` |

## GAME.EXE loads code

The first run that reached `GAME.EXE` died with `unmapped call: 0x801E8960`
from `func_80044D9C`, which loads a module pointer from `0x8018FAE0` and calls
its slot 8 (`+0x20`). The word `0x801E8960` occurs once on the disc, in
`CD/COM/FDAT.T`, `0x24` bytes into entry 2.

`FDAT.T` is the archive Verdite2's is: `u16` count (132), then `u16` start
sectors. Entries come in groups of three (about 68 KB, 28 KB, then a 2-4 KB code
module), and the code is at entries `3n+2`, n = 0..27: **28 modules**. Each is a
count word (5), 32 dispatch slots, then code, so the module pointer is the
module's base plus 4. Scoring every candidate base by how many slot targets land
on a prologue or just after a `jr ra` gives **`0x801E8308` for all 28**, each
with every target but one or two (13/14, 15/16; 16/16 for two). The modules make
no internal `jal`s to score by. Entries 84-95, 116, 117, 123 and 124 are empty,
and no entry outside `3n+2` has a pointer table. Entries 96 on are large and have
none either.

The destination is not a `lui`/`addiu` literal of `0x801E8308` or `0x801E830C`
in `GAME.EXE`, so the base was scored, not read. The run confirms it: the read
of entry 2's first sector armed `fdat02`, and the write that landed it activated
it (`[Dispatcher] loaded overlay: fdat02`).

They are declared like Verdite2's: `"skip": 0` and the base as `base`, so the
overlay covers the header. Sweeps start at the code (`min(slot) - base`, `0x88`
to `0x22C`) with the slot targets and internal `jal`s merged in; every slot
target was already a swept start, and `merge_branch_spans` finds nothing to
merge in any of them. Five resolve jump tables (`fdat02`, `08`, `14`, `20`,
`53`). Re-sweeping one (then `add_call_targets.merge()` with the slot targets,
then `merge_branch_spans`):

```bash
$RC --generate-function-file -linear-sweep -disc disc/KingsField3.cue \
    -file "CD/COM/FDAT.T" -base 801E83B0 -offset 18800 -skip A8 -size F58 \
    -out config/funcmaps/fdat02.json
```
