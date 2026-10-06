# Handoff: the port settings page in the game's own menu

**Done 2026-10-06**: built and measured as `patches/SettingsPage.cs`; see "The page
in the game's menu" in [SETTINGS.md](SETTINGS.md) and "The port settings page" in
[GAME_INTERNALS.md](GAME_INTERNALS.md). Two things below turned out otherwise: a
page holds six rows, not eight (the frame, not the list group, is the limit), and
the unselected label is `func_80026158`, the selected one `func_800261DC`. Kept for
its reading.

Written 2026-10-06, after the model under it was built and measured. Everything
under "Known" was read off the recompiled code or measured in play; everything
under "To find" was not.

## The goal

L2 in the in-game menu opens the port's settings, drawn with the game's own menu
routines so it looks like part of the game: pages of rows, Up/Down for the row,
Left/Right for the value, the game's sounds, and on leaving with changes, SAVE
CHANGES or DISCARD CHANGES. A launch feature.

**The model is done; build only the page.** `docs/SETTINGS.md` is the model and its
rules: read it first. The page reads and changes settings only through
`SettingsSession` (`patches/SettingsSession.cs`), and never touches
`interface.ini`, `Rt.View` or `Rt.SaveView` itself (they belong to the host
thread, and the menu runs on the game thread). The rules the user asked for
(nothing written unless the player changed it and chose SAVE) hold as long as the
page goes through the session.

## Decide with the user first

- **The keyboard key for L2.** Every layout in `patches/KeyLayout.cs` has
  `L2 = ""`. Changing the layout bumps `Version` and migrates players' bindings
  (`KeyLayoutApply`), which would rewrite a player's own binding: the rule in
  `docs/SETTINGS.md` forbids that. Either bind L2 only where it is empty, without a
  migration, or leave the layout alone and give the page a host-side key of its
  own. Ask.
- **Switching pages**: L1/R1, or Left/Right on a header row. L1 alone is free
  inside the page's own loop (the L1+R1 readout below is the top menu's, not the
  page's).
- **Which settings show in game at launch.** Several are off until judged. A
  setting with `Page = null` stays in the Settings window only.

## Known

### The model (`docs/SETTINGS.md`)

- `PortSettings.Pages` (PICTURE, MOTION, WORLD, WATER, GAMEPLAY) and
  `PortSettings.OnPage(page)`, eight rows at most, already checked at start-up
  against the font. Each `PortSetting` has `MenuLabel`, `MenuValue(v)` (text the
  font can draw, checked for every step), `IsUsable`, `LockedBy` (a `KF3_*`
  variable).
- `SettingsSession.Open()`; `Shown(s)` (the value to draw); `Step(s, ±1)` returns
  null when it moved, else why not (`set by KF3_X`, `not usable`, `at the end`);
  `Reset(s)`; `Dirty`; `Save()`; `Discard()`. Open discards a session left open.
- `MenuFont.Encode(text)` gives a record's string bytes, `0xFF` included.
- The shell's `settings` verb drives the same session from the game thread and
  prints every setting's live and shown value and whether it is staged: use it to
  check what the page did.

### The in-game menu ("The in-game menu, its lists and its font" in `GAME_INTERNALS.md`)

- **The menu** `func_8001A774` loops: the chooser `func_800221E8(cursor, 5, &sel,
  &confirmed)` with `&cancel` at `sp+0x10`, then `PadRead_game(1)` (L1+R1 test),
  then two frames of `func_80026FE4` (frame head), `func_800227EC` (status page),
  `func_800252F4(0, 6, cursor, 0)` (the list), `func_800270F8` (present). Its
  locals: `sel` at `sp+0x18` (-1 none), `confirmed` `sp+0x1C`, `cancel` `sp+0x20`
  (-99 = `0xFFFFFF9D` until cancelled, then -1).
- **The hook point**: a post on `func_800221E8` whose `c.RA` is `0x8001A8E4`
  (the chooser has other callers; the callee restores `RA` before returning, so a
  post sees its caller's return address). It is between frames: no frame is open.
  Open the page there when L2 is newly down, `sel` is -1 and `cancel` is still -99
  (the menu's stack is `c.SP` at that point; check the offsets against the
  generated code before trusting them).
- **The chooser's pad**: `func_800279D8` is a repeat gate (when the flag `gp+0x34`
  = `0x8009C248` is set it clears it and waits up to 8 `VSync(0)`s while any button
  is held), `func_800279A4` reads `PadRead_game(1)` and sets the flag on any
  button. Confirm mask `gp+0x38` = `0x8009C24C` reads `0x0040` (Cross), cancel
  `gp+0x3C` = `0x8009C250` reads `0x0020` (Circle). **L2 (`0x0001`) is tested by
  nothing in the top menu**: measured, L2 with the menu open left it open and its
  state unchanged. `func_80027A40` waits for every button up.
- **L1 + R1** in the top menu is a retail debug readout (`func_8001A9DC`); do not
  open on L1.
- **The template is OPTION 2**, `func_8001F004`: a loop over the same repeat gate;
  Up `0x1000`/Down `0x4000` for the row with sound `0xC`; Left `0x8000`/Right
  `0x2000` for the value with `0xD` (`func_8002792C(n)` plays sound `n`); cancel
  leaves with `0xE`; each frame `func_80026FE4`, `func_800252F4(5, 5, cursor, 1)`,
  the values by `func_800246BC(sp+0x10)`, `func_800270F8`.
- **Drawing a list** (`func_800252F4(group, count, cursor, mode)`): for each
  record, `func_80026020(0x8007E5A0, record)` (the selected box; blink counter
  `gp+0x4C`) or `func_80025F38(0x8007E5A0, record)`, then the label by
  `func_800261DC(0x8007E570, record)`; a header (X non-zero) the same way first;
  then a mode-dependent hint at the bottom (`func_800269C0`, `func_80026570`). Read
  the dim variant `func_80026158` for an unusable row.
- **A record** is `s16 X`, `s16 Y`, then the string, ending `0xFF`. The game's
  are in a static table at `0x8007E660 + 0xFC × group`, but **a record can be
  anywhere in RAM**: `func_800246BC` builds its value records on the guest stack
  (X `0xAB`, Y `0x3A`, …). So the page can build its records on the guest stack
  too, and call the box and text routines itself, a C# copy of `func_800252F4`'s
  loop, instead of writing into the static table. Prefer that: nothing to restore.
  Rows 26 apart; under a header at Y 32 the first item is at Y 58, X 45 (header
  X 31).
- **The font** fits 23 characters a record, 7 pixels each; a label column and a
  value column must both fit the 320-wide frame (or the margin, with widescreen).

### Calling game code from C#

`patches/AutoReload.cs` is the idiom: `using Game = Recompiled.KingsField3_game;`,
`var saved = c.Snapshot(); … Game.func_XXXXXXXX(c, m); … c.Restore(saved);`, with
arguments in `c.A0..A3` and stack arguments at `c.SP + 0x10` after `c.SP -= 0x20`.
The hook is `HookAttach.OnOverlayLoad(name, Attach, why)`, `Attach` resolving the
address with `SymbolRegistry.Resolve("game", null, addr)` and
`HookManager.AddPost`. Pacing: `func_800270F8` is already held to the vblank
(`VBlankPacing`), and `MenuWorld` draws the world behind it.

## The plan

1. The hook and an empty page: open on a new L2, draw one header and nothing else,
   leave on Circle; the repeat flag set on the way out so the held button does not
   reach the chooser. Measure through the log and the shell: the menu stays open
   after, the cursor unchanged.
2. Rows and values for one page through `SettingsSession`; Left/Right steps; a
   locked or unusable row drawn dim and refused (its reason logged).
3. All pages and the page switch.
4. Leaving with changes: SAVE CHANGES / DISCARD CHANGES with the game's own
   chooser and boxes (the start menu's STAY / DO NOT STAY, group 7, is that
   shape); Circle there goes back to the page.
5. A probe (`KF3_SETTINGSPAGE_PROBE=1`): open, page, step, save, discard lines.
   Add the switch to `docs/ENV_VARS.md`.
6. Write it up in `docs/SETTINGS.md` and `GAME_INTERNALS.md`; tick `docs/TODO.md`.

## Checking it

Through the shell, in an isolated copy (`docs/DEVELOPMENT.md`, "Retained scene
verification", for copying the build, cards, `settings.json` and
`interface.ini`): `press Circle` (preset 3 opens the menu), `press L2`, then the
page's buttons with `press`, then `settings` to read what the session holds.
Diff the `[RecompOne]` lines of `interface.ini` against the copy taken first, as in
`docs/SETTINGS.md` "Measured": open/browse/leave and change-back/discard must be
identical, a save exactly the lines changed. **How the page looks is the user's
to judge**: do not capture the window.

## Traps

- **Cross in the menu can save over a slot**: drive the menu only with copies of
  the cards.
- **`pkill -f "bin/KingsField3.dll"` kills the shell running it** (the pattern is
  in its own command line). Use `pkill -f "^dotnet bin/KingsField3.dll"`.
- A run directory without `settings.json` opens the disc picker and waits for a
  person.
- **The page must not leave the session open** on any path, an exception included:
  wrap the loop and discard in a `finally`.
- **The working tree when this was written**: the model, the `vram` and `settings`
  shell verbs and their docs were not yet committed. Check `git status` before
  starting.
