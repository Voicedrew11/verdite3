# Port settings: one list for the Settings window and the game's menu

The port's settings are to be changed from inside the game too: PORT SETTINGS,
an item under SYSTEM in the in-game menu, opens pages drawn with the game's own menu routines ("The in-game menu, its
lists and its font" in [GAME_INTERNALS.md](GAME_INTERNALS.md)). This file is the
model both screens share, and the rules it keeps. **Built 2026-10-06: the list,
the store, the session, the shell's `settings` verb and the page in the game's
menu, all measured; the page judged by the user ("looks great", 2026-10-06).**

## The rules

1. **Nothing is written unless a person changed that setting.** Booting, opening
   the page, moving the cursor, or changing a value and changing it back write
   nothing. Nothing calls `ViewConfig.Default`, and nothing writes a value that came
   from a default or a `KF3_*` variable.
2. **No key means the default.** Writing the default would pin it, and a player who
   never chose should follow the default if a release changes it (widescreen, say,
   once it is judged). Back to the default removes the key.
3. **A write touches only the keys that changed.** A key that already holds the
   text is not written again.
4. **The game's page stages its changes.** They apply live, so they can be seen,
   and reach the file only on Save. Discard, or the game quit with the page open,
   writes nothing and puts the live values back.
5. **A setting a `KF3_*` variable set is locked in game**: a saved value would be
   overridden again at the next boot. The Settings window still lets it be changed,
   as before; whether it should lock too is open.
6. **A value off the page's steps is shown as it is** (a slider's 137 fps, a
   variable's 1.85:1) and is not rounded onto a step until the player moves it.
7. **Keys and their text are what they were**, so no player's file is migrated.
8. **Port settings never go into game memory or the card.** The game's own
   OPTION values are saved on the card (`0x801B25DF..25E4`); these are not.

## Why a store, and why the host thread

`interface.ini` is the runtime's `ViewConfig`, a plain `Dictionary` loaded once at
boot. `Rt.SaveView()` writes all of it back and calls
`ImGui.SaveIniSettingsToMemory()` for the layout on the way. ImGui runs on the host
thread, the game on its own (`Runtime.cs`, `RunGame`). The game's menu runs on the
game thread, so a write from it would call ImGui off its thread and could change
the dictionary while the host reads it. Every write therefore goes through one
store that touches the dictionary only on the host thread.

## The pieces

| file | what |
|---|---|
| `tools/verdite-core/src/Settings/PortSetting.cs` | one setting: key, variables, both labels, page, the steps Left/Right moves through and their text, default, live getter and setter, how the key is stored (`Int`/`Float`, the text `SetInt`/`SetFloat` write), how the Settings window draws it, usable-when |
| `patches/PortSettings.cs` | the list (41 settings), the game page's fifteen rows on four pages (DISPLAY, GRAPHICS, WORLD, GAMEPLAY), seven of them combined rows, checked at start-up, the boot check, and the Settings window's row drawer |
| `tools/verdite-core/src/Settings/WindowMode.cs` | windowed, fullscreen or borderless for the page: the runtime's two keys, applied on the host thread |
| `tools/verdite-core/src/Settings/SettingsStore.cs` | the one writer: `Write` (the Settings window), `Submit` (any thread; queued off the host), a pump that writes the queue on the host's next frame |
| `tools/verdite-core/src/Settings/SettingsSession.cs` | the page without its drawing: open, step, reset, save, discard |
| `tools/verdite-core/src/Settings/MenuFont.cs` | text to the menu font's codes, and why a text cannot be drawn |
| `patches/SettingsPage.cs` | the page in the game's menu: the PORT SETTINGS item, the loop, the drawing, SAVE CHANGES / DISCARD CHANGES |

**Every value is a double**: a switch is 0/1, a choice the chosen value itself (an
aspect ratio, a frame rate, a slot), so the key keeps what it held.

**The list does not read saved values at boot.** Each feature's own start-up still
does, exactly as before, so moving a setting onto the list changes nothing a
player has. `PortSettings.Install` runs last in `Program.cs` and adds two checks:

- **At start-up** (a programmer's mistake, so it stops the boot): a key declared
  twice, a page empty or over six rows (what the frame fits, below), a label or
  value text the menu font cannot draw (`MenuFont.Check`: capitals, digits,
  `. , ' - = / * # ! ?`, space) or that does not fit: a label 16 characters (the
  box), a value 18, a page name with its `9/9` 16.
- **At `RuntimeReadyEvent`**: every setting with no key kept and no variable set
  must be live at its declared default, or a reset would not put back what the
  player had. Printed as `[KF3] settings: <key> boots at <v>, declared default <d>`
  and a summary line.

**The pump** is an `IFloatingPanel` that is always open and draws nothing: the host
draws every open panel each frame, the one per-frame call a patch gets on the host
thread without a runtime change. Its open state is written with the others, so
`interface.ini` gains `Panels.kf3settingsstore=True` at the first save.

**The session** (game thread) reads every setting on open. A step applies the value
(unless the setting waits for a boot) and stages it; a step back to the opening
value unstages it, and a step onto the default stages the key's removal (rule 2),
as a reset does. Save submits only what is still staged. Discard puts back each
staged setting whose live value is still the staged one, so a change made in the
Settings window meanwhile is not undone. A setting changed in both is decided by
the later save.

**The Settings window** draws Gameplay and Video's world enhancements from the list
(`PortSettings.Draw`). A slider applies while dragged and is written once, when let
go, where before the file was rewritten every frame of a drag. The two distances
are two controls each and keep their own drawing, writing through the store. The
Testing tab is a developer's and keeps its own code; its player-facing switches
(aspect, shading, the picture, pacing, smoothing, mouse look) are declared on the
list for the game's page and draw `Ui.None` in the window. It writes the same keys
in the same text on the host thread.

## Measured (2026-10-06)

The shell's `settings` verb drives a session on the game thread, as the page will.
A copy of the player's `interface.ini` (39 `kf3.*` keys) in an isolated run
directory, slot 1, `fdat17`. The `[RecompOne]` key lines were compared before and
after, `Panels.*` aside:

| check | result |
|---|---|
| boot, then quit | identical; `30 declared on 5 pages, 24 kept in interface.ini, 0 default(s) wrong` (the first run found `kf3.smooth` declared off, booting on; fixed) |
| open, Z-buffer off, back on, save | identical, 0 writes |
| open, perspective off, message fade x2, discard | identical, 0 writes, both live values back |
| open, message fade x2, save | one line: `kf3.messagefade: 1 -> 2` |
| open, reset message fade, save | the key removed, live x1 |
| frame rate from UNCAPPED (the kept 0): Right, Left, Left, discard | Right refused at the end, then 360, 240; discard put 0 back |
| `KF3_WAVES=0` against a kept `kf3.waves=1`: step, save | refused (`set by KF3_WAVES`), identical during the run and after quit |

**Not measured**: the Settings window's rows (ImGui is not driven by the shell;
they are the user's to look at), and a change in the window during a session.

## The page in the game's menu

`patches/SettingsPage.cs`, built 2026-10-06 on the reading in "The port settings
page" in [GAME_INTERNALS.md](GAME_INTERNALS.md). **PORT SETTINGS**, a seventh
item under SYSTEM in the top in-game menu, opens it, with the menu's own Cross,
so a keyboard reaches it as a pad does. (It first opened on L2, which no keyboard
layout binds; the item replaced that on the user's word, 2026-10-06.) The game's
dispatch takes only items 0..5, so choosing the seventh leaves the menu as it
was, and coming back from the page leaves the cursor on it. Inside:

| button | does |
|---|---|
| Up / Down | the row, wrapping (sound `0xC`) |
| Left / Right, Cross | the value one step (sound `0xD`); a switch flips either way; no wrap |
| L1 / R1 | the page, wrapping, back to the first row (sound `0xC`) |
| Circle | leave (sound `0xE`); with changes, SAVE CHANGES / DISCARD CHANGES first |

At the question, Cross chooses and Circle goes back to the page with the changes
still staged. The page last shown is kept for the next open. A step the session
refuses (`set by KF3_X`, `not usable`) makes no sound and is logged. **The values go
round** (2026-10-08): Right from the last value is the first and Left from the first
is the last (`PortSetting.Next`); a value off the steps with none on that side goes
round to the far end. `at the end` is left only for a row of one value, which none
is, so the refusals measured below as `at the end` now step round instead.

**What is on it** (cut to what a player chooses, on the user's word, 2026-10-06;
grouped by subject, a page short of six rows being fine, on the user's word,
2026-10-07, GAMEPLAY to gain more rows later):

| page | row | values | stands for |
|---|---|---|---|
| DISPLAY | DISPLAY | WINDOWED, FULLSCREEN, BORDERLESS | the runtime's `Fullscreen` and `Borderless` |
| | RESOLUTION | 240P … 1920P | `RenderScale` 1..8: the game's 240 lines times the scale, taken at the next present (fork `0097`), at most the context's MaxScale |
| | ASPECT | 4/3, 16/9, 16/10, 21/9 | |
| | HUD AT EDGES | ON, OFF | `kf3.widescreen.hud`, dimmed at 4/3 (`docs/WIDESCREEN.md`) |
| | FRAME RATE | ORIGINAL, 30 … 360, UNCAPPED | `kf3.pacing` and `kf3.fps`: ORIGINAL is no pacing |
| GRAPHICS | TEXTURE FILTER | OFF, MIPMAPS, 2X … 16X | `kf3.mipmaps` and `kf3.aniso`: the taps walk the mip chain, so any turns the mipmaps on |
| | PER-PIXEL LIGHT | ON, OFF | |
| | AMB. OCCLUSION | OFF, LOW, MEDIUM, HIGH | `kf3.ao` and `kf3.ao.quality` |
| WORLD | RENDER DISTANCE | ORIGINAL, ENHANCED | `kf3.renderdistance` and its fade: ENHANCED is 16 tiles faded over 3, the user's |
| | WATER | ORIGINAL, ENHANCED, FULL | `kf3.murk`, `kf3.waves`, `kf3.planar`: ENHANCED the swell, FULL the reflections too; murk off in all three until it is good enough to ship (the user, 2026-10-08) |
| GAMEPLAY | CONTROLS | 2D, 3D | `kf3.controls`: 2D the twin sticks, 3D one stick as tank controls, the left stick walking and turning and the right stick unused (`docs/INPUT.md`, "2D and 3D controls") |
| | INVERTED CAMERA | OFF, ON | `kf3.analog.invertpitch`: the right stick's up and down reversed, as a flight stick, dimmed in 3D (`docs/INPUT.md`, "Inverted camera"); Settings ▸ Gameplay, moved off Input ▸ Analog's "Invert look Y" |
| | RELOAD ON DEATH | ON, OFF | |
| | GYRO | ON, OFF | `kf3.itemturn.gyro` and `kf3.gyroaim.on`: the pad's gyroscope turns a picked-up item and aims a drawn bow (`docs/INPUT.md`) |
| | RUMBLE | OFF, ON, HD | `kf3.rumble.on` and `kf3.rumble.hd`: the pad rumbles as a bow is drawn and loosed, ON on two motors, HD on a Switch Pro Controller, a single Joy-Con or a DualSense over USB (`docs/INPUT.md`, "Rumble"); OFF keeps the HD choice |

Until 2026-10-07 the same rows filled two pages, PICTURE (DISPLAY to TEXTURE
FILTER) and WORLD (the rest). The page shown is not kept between runs, so the
regrouping moved no player's file. Measured in an isolated run directory with the
player's `interface.ini`: `33 declared on 4 pages`; PORT SETTINGS opened on
DISPLAY, R1 four times went GRAPHICS, WORLD, GAMEPLAY and back to DISPLAY, then
Circle gave `left, nothing changed`, with `interface.ini` identical and 0 writes.

Off the page, and kept in the Settings window with their variables: the smoothers
(on whenever pacing is), mouse look and instant look, the menu pointer, fog from depth, blend tile
edges (now **on by default**, the user having kept it on), the reload slot (last
used), the message fade, and the renderer's own switches (shading, perspective,
sub-pixel, the Z-buffer, the world behind menus, scrolling textures, the AO normals,
the enhancement distance). A saved value of any of them still holds.

**A combined row** (`PortSetting.Parts`) keeps nothing of its own. Its value is
`Join` of its parts' shown values; a step stages each part with what `Split` gives
it, NaN leaving a part as the page found it (ORIGINAL frame rate keeps the kept
rate, not the 30 walked through). Parts set to no step (the Settings window, a
variable) show as CUSTOM, or for taps without mipmaps `4X NO MIPMAPS`, valued
between two steps so Left and Right each reach one. A part's variable locks the row.

**The display mode** is two runtime keys that F11, the menu bar and the Display tab
read on the host thread. A step is applied there (`SettingsStore.OnHost`) with the
keys put back as they were, so it reaches the file only on Save; `WindowMode.Watch`
follows a change made anywhere else. Windowed removes `Fullscreen` and leaves
`Borderless`, so F11 still covers the screen the way it did. Off Windows, BORDERLESS
is the window manager's fullscreen (`HostWindow.SetFullscreen`), so it looks the
same as FULLSCREEN there.

**Six rows a page**, not the list groups' eight: under a header at Y 32 the game's
items are 26 apart from Y 58, so a sixth ends at Y 206 and a seventh would cross
the hint row at 214. The pages were regrouped to fit (four, where the model had
five).

**How it draws.** The game's own routines on records built on the guest stack, as
OPTION 2 does: the header box and bright label; each row's label in the 118-wide
box, the selected one pulsing and bright, the others the game's unselected shade;
the value in the label font at OPTION 2's value column (X 171), without a box; a
row that cannot change (locked by a variable, or needing another setting that is
off) has its label and value darker than the unselected shade (`0x28/0x28/0x30`);
the hint row is OPTION 2's (the Left/Right icon, "select", the cancel icon,
"return"). The question is the start menu's STAY / DO NOT STAY places (X 101, Y 94
and 122) with the hint row of a chooser. Nothing in the game's list table is
written.

### Measured (2026-10-06)

Isolated run directory (build, cards, `settings.json`, the player's
`interface.ini`), slot 1, `fdat17`, opened with L2 (the rows below; the PORT
SETTINGS row is the item that replaced it), driven with the shell's `press` and read with
`settings` and `KF3_SETTINGSPAGE_PROBE=1`. The `[RecompOne]` lines of
`interface.ini` against the copy taken first, `Panels.*` aside:

| check | result |
|---|---|
| open, all four pages with R1 round to the first, Circle | `left, nothing changed`, identical, 0 writes; the menu still open (`loop` false) |
| PORT SETTINGS (Up from USE ITEM wraps to it), Cross; then Circle, Down, Up, Cross | the page opened both times, on the page last shown; the record in group 0's slot 7 (`0x8007E724`); Circle twice then closed the menu, the loop running |
| ASPECT Right (16/9 to 16/10), Left, Circle | the second step `(as it was)`, no question, identical |
| L1 to GAMEPLAY, MESSAGE FADE Right, Circle, DISCARD CHANGES | `discarded`, identical, live x1 |
| RELOAD ON DEATH Right, Circle, SAVE CHANGES | `saved 1 change(s)`, one line: `kf3.autoreload.enabled=0` |
| RELOAD ON DEATH back to ON, SAVE CHANGES | `back to its default`, the key removed (before the session fix above it wrote `=1`) |
| FRAME PACING off, FRAME RATE Right | refused, `not usable`; FRAME RATE at UNCAPPED, Right: `at the end` |
| Circle at the question | back to the page, the session still open with pacing staged; then DISCARD: pacing back on, identical |
| Circle on the top menu after the page | the menu closed, the loop running |

No exception in either run. **Judged** by the user, 2026-10-06: "looks great"; the
PORT SETTINGS item was asked for then.

### The cut list, measured (2026-10-06)

Isolated run directory, the player's `interface.ini` with `Fullscreen=False`, slot
1, `fdat17`, driven with the shell's `settings`. Boot: `32 declared on 2 pages, 26
kept in interface.ini, 0 default(s) wrong`. The `[RecompOne]` lines against the copy,
`Panels.*` and ImGui's layout aside:

| check | result |
|---|---|
| open | read back as the file holds: WINDOWED, 1440P, 165, 16X, ENHANCED, FULL |
| TEXTURE FILTER Left, Right; WATER, RENDER DISTANCE Left; RESOLUTION Left; FRAME RATE Left; discard | 16X `(as it was)`; `render scale 6x -> 5x`, then back to 6x; identical, 0 writes |
| FRAME RATE Left ×10 to ORIGINAL, save | `at the end` past ORIGINAL; one line, `kf3.pacing` removed (before the NaN fix it also wrote `kf3.fps=30`) |
| DISPLAY Right | FULLSCREEN, the file untouched while staged |
| DISPLAY Right, save | `Fullscreen=True`, `Borderless=True` |
| DISPLAY Left ×2 to WINDOWED, save | `Fullscreen` removed, `Borderless=True` kept |

No exception. **Not judged**: the page drawn with the new rows, and the window
changing mode (the user's to look at).

## Onboarding

The first-run setup, Verdite Core's (`tools/verdite-core/src/Onboarding/`), brought
over from Verdite2 (its #63 and #67) on 2026-10-14. The launcher shows it **while
the game is being built** and holds the game back until both are done, and it runs
again from the in-game page: RUN SETUP, the DISPLAY page's last row, not a setting.
The welcome offers three presets (Original, Original++, Modern) and Custom; each is
a starting point, and the player walks every page (the display, a page of tiers per
category, the turning page) to a summary. How the popup behaves, and why, is in
Verdite2's `docs/SETTINGS.md`; it is the same code.

### Where setup lives

* `tools/verdite-core/src/Onboarding/` (`Onboarding`, `OnboardingPopup`): the
  game-agnostic half, compiled into the launcher and into the game.
* `config/onboarding.json`: this game's half: the display fields, the categories,
  groups and tiers, the turning page, `defaults` (what each key means when absent),
  and every string in en, pt-BR and es-419. It ships in `content/config`
  (`Launcher.targets` takes `config/**/*.json`), so it is part of the build key.
* `patches/Setup.cs`: RUN SETUP, the live apply, the boot check and the shell's
  `setup` verb (`setup`, `setup open`, `setup apply <tier> [two more]`,
  `setup preset original|original++|recommended`).
* `Launcher.cs` in Core: `Onboarding.Due` decides whether setup is shown;
  `VERDITE3_SETUP=1` shows it regardless.

### What this game's spec holds

| page | group | Original | Original++ | Modern |
|---|---|---|---|---|
| display | window, resolution, aspect, frame rate | the window left alone, 240p, 4:3, 15 fps (`kf3.pacing` 0) | suggested from the monitor | suggested from the monitor |
| controls | movement | D-pad only (`kf3.analog.on` 0) | sticks | sticks |
| | haptics | no rumble, no gyro | plain rumble | HD rumble, gyro aim and item turn |
| presentation | image | no perspective, sub-pixel or Z-buffer, dithered, unfiltered | the three on, no dither (`kf3.shading` 1), unfiltered | 24-bit, 16x, mipmaps |
| | lighting | per-pixel, neighbour blend, fog and AO off (corners) | AO off | all on, AO medium |
| | water | no waves, no reflections | the same | waves and planar reflections |
| | motion | no smoothing | camera and models smoothed | the same |
| | view | the game's distance (`kf3.renderdistance` 0) | the same | 16 tiles, faded over 3 |
| gameplay | gear, death | as released | comparison, reload | the same |
| turning | controls | tank (`kf3.controls` 1) | tank | twin stick, no acceleration |

Modern is every key absent, so it is exactly the declared defaults. Bools are
written `1` and `0`, as this game keeps them; the window keys keep the runtime's
`True` and `False`. Not in it: sound (this game has no audio settings), murky
water (off everywhere until it ships), the HUD at the edges (a choice of its own,
off by default), and the mouse.

**The turning page has no slider.** Verdite2's asks how fast creatures turn toward
the player, because its acceleration needs faster creatures to stay fair. This game
has no enemy turn speed, so the page only chooses the controls, and Modern picks
twin stick *without* acceleration: the stick at the D-pad's rate ("The stick turns
at the original's speed" in `docs/INPUT.md`). Acceleration is offered, and its
description says fights get easier.

**The frame rate's ORIGINAL** writes `kf3.pacing` 0 and removes `kf3.fps`; every
other step removes `kf3.pacing` (on) and writes `kf3.fps`. `kf3.fps` is first in each
option, so a rate matching no step (50, from the Settings window) is shown as "Keep
current (50)".

### What setup writes, and when

The rules are Core's, as in Verdite2: setup is **due** while `verdite.setup` is below
the spec's `version` (1) and the player has never seen it or has no `kf3.` key yet;
a value equal to its default **removes** the key; `null` in a tier is the default;
a group matching no tier is **Custom** and left alone unless a tier is chosen. In the
game every changed key is applied live: the window keys by Core, `kf3.analog.on` and
`kf3.analog.accel` by `Setup`'s own setters (the Settings window's), every other by
its `PortSetting.Apply`.

**The boot check** (`Setup.Check`) holds the spec to the code: each key it writes
has a live setter, and each key not saved reads what `defaults` says. Measured
2026-10-14 on the user's settings (28 kept): `[KF3] setup check: 28 key(s), 0
problem(s)`. A fresh data folder, where every key is compared, is not measured yet.

**Not yet seen**: the launcher's setup over a build, RUN SETUP on the in-game page,
and whether `RUN SETUP` (the page's sixth row of six) fits the frame as the other
rows do. All of it needs someone at the window.

## The defaults are the user's (2026-10-06)

The user's own settings became every player's default: 16:9, pacing on at 144 fps
with the scrolling textures carried, smooth shading (24-bit, no dither),
perspective, sub-pixel and the Z-buffer, per-pixel light, AO at medium, mipmaps
with 16 taps, render distance ENHANCED (16 tiles faded over 3), all three
water features (FULL; murk taken off again and out of FULL on 2026-10-08, not
good enough to ship yet), and the sticks' turn and look at 1.25 (back to 1.0 on
2026-10-14: "The stick turns at the original's speed" in `docs/INPUT.md`). Each is changed twice, where the feature starts up and in
its declared `Default`, which the boot check holds equal. A variable still wins,
and a `0` turns any one off.

Left as they were, being the machine's rather than the game's: the frame rate
(144, against the user's 165), the render scale (4, against 6), the display mode
(windowed), the interface scale, and the debug mod.

Measured from an empty data folder (no `interface.ini`): `33 declared on 2 pages,
0 kept in interface.ini, 0 default(s) wrong`; `widescreen: 1.778:1`, `pacing: 144
fps`, `texture scroll: carry`, and perspective, sub-pixel and the Z-buffer `on`.

## Next

- **A hint for L1/R1**: the hint row has no page icon. The 16 × 16 icons in tpage
  `0x1C` (template `0x8007E5AC`) are not mapped to buttons yet, and the hint font
  (`func_80026570`) is lower case only, no digits.
- **The Settings window and variables**: lock a variable-set setting there too, or not.
- ~~**An atomic save, in the fork**~~: done 2026-10-08, runtime `0106`.
  `interface.ini` and `settings.json` are written to `.tmp` and renamed over, a
  failed write is logged instead of thrown, and an unreadable one is kept as
  `.damaged-<time>` before the defaults replace it.
- **Verdite Core**: the list's machinery is game-agnostic; it moves there when
  Verdite2 takes it up.
