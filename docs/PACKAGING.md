# Packaging

How the port becomes something a person can download. `docs/DEVELOPMENT.md` is
the document for working on the port; this one is about shipping it.

**The launcher and the packaging are Verdite Core's**, shared with Verdite2, and
were first written for Verdite2: the design and why it is shaped this way are in
Verdite2's `docs/PACKAGING.md` (the problem a release has to solve, first run, the
two things that were nearly wrong, versioning, telling the player about a new
release) and in Verdite Core's README ("The launcher", "Packaging"). This file is
what is Verdite3's.

## No prebuilt binary

`generated/` is a translation of FromSoftware's code, so the assembly that plays
the game has to be built on the machine of somebody who owns the disc, and the
generated dispatch tables bake absolute LBAs from one mastering. The release ships
the inputs (`config/`, `Program.cs`, `patches/**`, Verdite Core's `src/`, `mods/`)
and the launcher builds the game from the player's image at first run, into the
data directory, and keeps it until something that went into it changes.

## The two projects

| project | builds without the disc | what it is |
|---|---|---|
| `Verdite3.Launcher/Verdite3.Launcher.csproj` | **yes** | the shipped executable, `Verdite3` |
| `KingsField3Recomp.csproj` | no | the developer path, unchanged |

`Verdite3.Launcher/` is a csproj that names the executable and imports
`tools/verdite-core/launcher/Launcher.targets`, and a `Program.cs` that hands
`Launcher.Run` this port's record:

| field | value |
|---|---|
| `Name`, `AppId` | `Verdite3`, `verdite3` |
| `GameTitle`, `Serial` | `King's Field II`, `SLUS-00255` (boot file `SLUS_002.55`) |
| `WrongDiscs` | `SLUS-00158`, the US *King's Field*: Verdite2's game |
| `RecompilerConfig`, `GameAssembly` | `kf3.json`, `KingsField3` |
| `UpdateRepository` | `Voicedrew11/verdite3` |
| `PlayAfter` | `open` and `game` are the title (GAME.EXE starts at the memory card screen, its start menu); `end` and `fdat*` are play |

The disc check and the build key take the disc's files from `kf3.json`:
`SYSTEM.CNF`, `SLUS_002.55`, `OPEN.EXE`, `GAME.EXE` and `END.EXE` (each at least
`0x800`, its header) and `CD/COM/FDAT.T` (at least to the end of `fdat53`'s slice).

`KingsField3Recomp.csproj` removes `Verdite3.Launcher/**` and `packaging/**` from
its globs. The first is load-bearing: the launcher's build stages `patches/**`
into `Verdite3.Launcher/bin/.../content/src/`, and without the remove the game
project compiles every patch twice (measured: 930 CS0229 errors).
`GameCompile`'s options are the ones this csproj sets (unsafe, nullable, implicit
usings plus `Verdite.Core`, QuickJit off), and must stay so.

The launcher's own switches are `VERDITE3_DATA` (the data directory, otherwise
`~/.local/share/verdite3` or `%LOCALAPPDATA%\Verdite3`), `VERDITE3_UPDATE_CHECK`
(`0` off, `force` past the daily limit) and `VERDITE3_BUILD` (the commit stamped
into a build made outside a checkout); see `docs/ENV_VARS.md`.

## Measured

2026-10-05, on the change that added the launcher:

- **The developer build, before and after**, `KF3_AUTOSTART=1 KF3_AGENT=1
  KF3_FPS=144 KF3_FPS_PROBE=1`: slot 1 loaded into `fdat17`, area 5, HP 108/134,
  at 144.0 fps and 14.9 ticks/s; the same `[KF3]` lines but the icon's;
  `carda.sav` unchanged.
- **The AppImage on an empty data folder** (`VERDITE3_DATA`, a `settings.json`
  whose `CdPath` is this disc): `[Verdite3] 0.1.0+c2f617ac9`, the game recompiled
  and compiled into `builds/85c87dc0af9383e8/`, then the same run: `fdat17`, 144.0
  fps, 14.9 ticks/s, the same `[KF3]` lines. 44 MB.
- **The update check** with `force`: `update check failed: … 404 (Not Found)`,
  because `Voicedrew11/verdite3` has no release yet; nothing written, nothing
  announced. It reads a real answer once a `v*` release is published.
- **Given `SLUS-00158`**, the launcher refuses before building: "This is King's
  Field (SLUS-00158), which is a different game; Verdite2 plays it. …"

**Not run:** the Windows package (no `pwsh` here; the stub builds as
`Verdite3.exe` from core's project) and the release workflow. **Never looked at by
eye:** the build popup, the update popup and badge, and the icon at the sizes a
desktop draws it.

**A saved disc path is checked before the argument.** The runtime's
`WaitForValidDisc` reads `CdPath` from `settings.json` and opens its picker if
that file is gone, even when a cue is on the command line; the picker then waits
for a click. The checkout's `settings.json` still names
`~/Desktop/verdite3/disc/KingsField3.cue` from before the repository moved, so a
scripted boot from the checkout sits at the picker (a managed stack shows it in
`HostWindow.WaitForValidDisc`, presenting). Pick the disc once by hand, or point
`CdPath` at `disc/KingsField3.cue`.

## The window icon

The window wears **the fourth save slot's memory-card icon**, read off the
player's disc at boot by `patches/CardIcon.cs`, in its third frame (the hair blown
furthest out). The orb in `packaging/shared/` is the shipped mark and the
fallback: `Program.cs` sets it first, and the card icon replaces it only when the
disc answers. The release cannot carry the game's art, which is the same line
`generated/` is on.

**Where it is.** Each of the five save slots has its own icon: the card shows
"2-1" to "2-5" in the save's title, and each slot's header carries a different
16-colour palette and three different frames. All five are in `CD/COM/FDAT.T`
**entry 96** (sectors `0x54E`–`0x5FC` of the archive, by its own table: `u16`
count, then `u16` start sectors):

- the five palettes back to back at `+0x422F0`, `0x20` apart (BGR555, entry 0 the
  background, `0x0000` here as in the card header);
- the pixels at `+0x438E8`, `0x700` an icon: sixteen rows `0x70` apart, each row
  holding frame 0, 1 and 2 eight bytes apart (4bpp, 16 pixels).

Reconstructed that way, all fifteen frames are byte-identical to the five saves in
this port's `carda.sav`, which is what proves the layout. The frames differ only
in the hair: 0 tucked in, 1 part out, 2 furthest out. Nothing in `GAME.EXE` holds
a second copy of a palette (Verdite2's icon is found that way); the only copy on
the disc is the archive's. So `CardIcon` finds entry 96 through the archive's
table and checks the icon's palette and its sixteen rows against a SHA-256 before
using them: another revision of the disc keeps the orb rather than wearing
whatever bytes sit there (`[KF3] icon: the icon is not where SLUS-00255 has it`).

Decoding, the whole-multiple scale to 16-256 px and the icon theme copy for
Wayland are Verdite Core's `WindowIcon` and `DesktopEntry`, as in Verdite2.
`KF3_ICON=orb` keeps the shipped mark, `off` clears it, `0`/`1`/`2` picks a frame;
`KF3_ICON_INSTALL=0` writes nothing into `~/.local/share/icons` or
`applications`. The app id is `verdite3`, set in `Program.cs` before the window.

## Building a release

```bash
bash scripts/setup_tools.sh                 # builds the RecompOne subtree, tracked here

bash packaging/linux/build-appimage.sh      # dist/Verdite3-<v>-x86_64.AppImage
pwsh packaging/windows/build-windows.ps1    # dist/…-win-x64.zip and the installer
bash scripts/release.sh 0.2.0               # bump VERSION, commit, tag; never pushes
```

All three are wrappers of Verdite Core's scripts, which read `packaging/package.env`
(`NAME=Verdite3`, `APP_ID=verdite3`, and `INNO_APP_ID`, this port's installer
GUID, which must never change or be reused). `VERSION` is the one place the
number is written (`0.1.0` to start). `.github/workflows/ci.yml` builds the
launcher and the stub with no disc on every push; `release.yml` packages both
platforms on a `v*` tag and opens a draft release.

`packaging/shared/verdite3.png` and `.ico` are, for now, Verdite2's orb copied;
replace them at the same sizes (`packaging/shared/README.md`).
