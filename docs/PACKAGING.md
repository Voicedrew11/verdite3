# Packaging

How the port becomes something a person can download. `docs/DEVELOPMENT.md` is
the document for working on the port; this one is about shipping it.

**The launcher and the packaging are Verdite Core's**, shared with Verdite2, and
were first written for Verdite2: the design and why it is shaped this way are in
Verdite2's `docs/PACKAGING.md` (the problem a release has to solve, first run, the
two things that were nearly wrong, versioning (Verdite3 differs: "The tag is the version" below), telling the player about a new
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

The port's one icon is **the fourth save slot's memory-card icon**, read off the
player's disc at boot by `patches/CardIcon.cs`, in its third frame (the hair blown
furthest out). The release cannot carry the game's art, which is the same line
`generated/` is on, so it ships **no mark at all** (2026-10-08; Verdite2's orb,
copied, stood in until then): the executables have no icon resource, the AppImage
a transparent one, and until a run has read the disc once there is no icon.
After that it is the card icon everywhere the game can put it (below).

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
using them: another revision of the disc wears no icon rather than whatever bytes
sit there (`[KF3] icon: the icon is not where SLUS-00255 has it; no card icon this
run`).

**When it is read.** With a disc on the command line (the launcher always passes
one, and so does a run from the checkout) the icon is read at once, before
`Entry.Run`. With none — `KingsField3.exe` opened on its own — the disc is not
known yet: `settings.json` is loaded by the window, which `Entry.Run` makes, and
a first run's disc only once the picker answers. `CardIcon` read an empty
`CdPath` there and returned without a word, so Windows showed the shipped mark. It now
waits for the first overlay to load and sets the icon on the window's thread
(`GpuJobs.Run`); measured under Wine with no argument, the icon line now comes
just before `loaded overlay: open`.

**Where else it goes.** Each read writes the icon out of the window too, all of
it Verdite Core's (`WindowIcon`, `DesktopEntry`, `ShortcutIcon`):

- **`icon.png`** (256 px) in the data directory, the working directory. The next
  run sets it before anything else: the launcher before its build popup, and
  `CardIcon` before the disc is read, which is what a run with no disc argument
  shows until the first overlay.
- **Linux:** every size into `~/.local/share/icons/hicolor/<n>x<n>/apps/verdite3.png`,
  which outranks the AppImage's and is the only icon a Wayland compositor reads,
  and a `verdite3.desktop` when no packager wrote one.
- **Windows:** `icon.ico` (16, 32 and 48 as 32-bit DIBs, 256 as PNG) in the data
  directory, and every shortcut on the Desktop, in the Start menu or pinned to
  the taskbar whose target is this process, or the stub one level above `bin\`,
  pointed at it, then Explorer told (`[KF3] icon: 2 shortcut(s) now wear
  icon.ico`). An executable's own icon is read out of the file, which is the
  release's, so the shortcuts are what carry the card; `Verdite3.exe` itself
  shows Windows's plain program icon in a folder view. The installer is per-user
  (`INNO_PRIVILEGES=lowest` in `packaging/package.env`, into
  `%LOCALAPPDATA%\Programs\Verdite3`), so its shortcuts are the player's and
  writable; an all-users shortcut is left as it is, with a line saying so.

Measured 2026-10-08 under Wine, in a fresh prefix, with the developer build
published for `win-x64` into a `bin\` beside a stand-in stub: a desktop shortcut
to `bin\KingsField3.exe` and a Start menu one to the stub both came to name
`icon.ico`, a Notepad shortcut beside them did not, and a second run rewrote
nothing. The `.ico` decodes at all four sizes to the card icon. **Not seen:** how
Explorer and the taskbar draw it on Windows, and the installer itself (no `pwsh`
or `iscc` here).

`KF3_ICON=off` clears the icon, `0`/`1`/`2` picks a frame; `KF3_ICON_INSTALL=0`
writes nothing into `~/.local/share/icons` or `applications`, or the shortcuts.
The app id is `verdite3`, set in `Program.cs` before the window.

## Building a release

```bash
bash scripts/setup_tools.sh                 # builds the RecompOne subtree, tracked here

bash packaging/linux/build-appimage.sh      # dist/Verdite3-<v>-x86_64.AppImage
pwsh packaging/windows/build-windows.ps1    # dist/…-win-x64.zip and the installer
bash scripts/release.sh 0.2.0               # tag HEAD v0.2.0; commits nothing, never pushes
```

All three are wrappers of Verdite Core's scripts, which read `packaging/package.env`
(`NAME=Verdite3`, `APP_ID=verdite3`, `INNO_APP_ID`, this port's installer
GUID, which must never change or be reused, and `INNO_PRIVILEGES=lowest`, the
per-user install "The window icon" needs). `.github/workflows/ci.yml` builds the
launcher and the stub with no disc on every push; `release.yml` packages both
platforms on a `v*` tag and opens a draft release.

### The tag is the version

Verdite2 writes its number in a `VERSION` file, and a release there is a commit
that bumps it and a tag on that commit, which CI checks against each other.
Verdite3 has no `VERSION` file: **the version is the newest `vMAJOR.MINOR.PATCH`
tag that `HEAD` contains**, and a release is that tag and nothing else. Verdite
Core resolves it by one rule (its README, "Packaging"): a `VERSION` file first,
which is why CI refuses one here; then `VERDITE_VERSION`, which `release.yml` sets
from the tag it was pushed for; then the tags; then `0.0.0`, before the first.

- A build between releases carries the last release's number, and the commit it
  was made from after it (`0.2.0+<9-char sha>`, `Ver.Full`), which is what a bug
  report should quote. It is never announced as an update to itself.
- The number is still `MAJOR.MINOR.PATCH`, since the update check compares it so
  and Windows wants numbers in the assembly version; how far to move it is the
  person tagging's call. `release.sh` refuses one that is not above the newest
  tag, because the update check never announces a lower one.
- The clone needs its tags: CI checks out with `fetch-depth: 0`, and a shallow or
  tagless checkout builds as `0.0.0` (`bash tools/verdite-core/scripts/version.sh`
  prints what a build here would call itself).

```bash
bash scripts/release.sh 0.2.0
git push origin HEAD && git push origin v0.2.0      # pushing the tag publishes the draft
```

### Previews

A preview is a build handed to testers: a commit, not a version, and no promise
of which release it becomes. When one proves a release, the tag goes on that
commit, so the release is exactly what was tested. There is one preview at a
time, the **rolling `preview` prerelease**, replaced whole each time:

```bash
git push origin main
bash scripts/prerelease.sh      # builds HEAD as GitHub has it; replaces `preview`
```

The script starts `release.yml` by hand with `preview=true`; its `preview` job
zips each package with the `PREVIEW_PASSWORD` repository secret (AES-256, so 7-Zip
or WinRAR opens them and Windows Explorer does not) and deletes and recreates the
`preview` prerelease at that commit, with fixed notes: the password is had on the
Verdite Project Discord, and back up the saves (where they are) first. The assets are always
`Verdite3-preview-linux-x86_64.zip`, `Verdite3-preview-win-x64.zip` and
`Verdite3-preview-win-x64-setup.zip`, so
`…/releases/download/preview/<name>` never changes; the files inside, and the
title, carry the commit. The build calls itself the last release's number and the
commit (`0.1.0+<sha>`).

- **It is never GitHub's "Latest".** A prerelease cannot be: the badge, and
  `releases/latest`, which the update check reads, name only a published full
  release, so no player is told about a preview. With no full release yet, the
  repository's sidebar shows the newest prerelease, marked *Pre-release*.
- **`preview` is no version.** `version.sh` reads `vMAJOR.MINOR.PATCH` tags only,
  and `release.yml` starts on `v*` alone, so the tag moving starts nothing. A clone
  that fetched it keeps a stale copy when it moves; `git tag -d preview` drops it.
- **It goes when the release does.** `preview-cleanup.yml` deletes the prerelease
  and its tag when a full release is *published* (not when its draft is opened),
  so a preview stays up until the release is live.
- The password is the secret, never the repository, which is public:
  `gh secret set PREVIEW_PASSWORD` changes it.
- Called `rc` until 2026-10-08; renamed because it is handed out for any fix worth
  testing, not only a commit meant to ship.
