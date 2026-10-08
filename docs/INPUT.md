# Input: pad, sticks, keyboard and mouse

The pad is read by the game as it always was; the port layers a keyboard layout
and a mouse look on top of it. What each button *does* is the game's — the port
only presses a button, and the game's own control-config screen decides the verb.
Built 2026-10-02; the switches are in `docs/ENV_VARS.md`, and what still needs a
person is at the end of this file.

## The pad and the action-mask table

**Stage 4 `func_80030FCC` reads the pad once a frame** through `PadRead_game`
(`0x800785AC`, the BIOS `PAD_dr`), into the u16 at **`0x801B265C`**. The previous
frame's word is **`0x801B265E`**, written from `0x265C` at the end of the stage.
There is no stored edge word: newly-pressed is computed inline as
`0x265C & ~0x265E` wherever it is needed, and `func_8002B6F0` masks the face and
Select bits out of `0x265C` during scene and area transitions.

The game tests `pad & u16[table[i]]` against a **14-entry u16 table at
`0x80081868`**. The face entries depend on the save: `func_8001F4C0`, the
control-config screen, rewrites the table from preset indices at `0x801B25E2`
(face) and `0x801B25E3` (direction), and **a New Game sets the face preset to 3**
(`func_8002B64C`, line 30723 of `generated/game.cs`). The table in `.data`, which
is what RAM holds before a save loads, is preset 0, and the first reading of it
was taken for the game's defaults. The face presets only ever swap the pairs
{attack, magic} and {menu, examine}:

| mask address | entry | action | preset 3 (a New Game) | preset 0 (`.data`) |
|---|---|---|---|---|
| `0x80081868` | 0 | walk forward | Up `0x1000` | Up |
| `0x8008186A` | 1 | walk back | Down `0x4000` | Down |
| `0x8008186C` | 2 | turn left | Left `0x8000` | Left |
| `0x8008186E` | 3 | turn right | Right `0x2000` | Right |
| `0x80081870` | 4 | attack | Square `0x0080` | Triangle `0x0010` |
| `0x80081872` | 5 | the in-game menu | Circle `0x0020` | Cross `0x0040` |
| `0x80081874` | 6 | magic | Triangle `0x0010` | Square `0x0080` |
| `0x80081876` | 7 | examine, talk, open | Cross `0x0040` | Circle `0x0020` |
| `0x80081878` | 8 | strafe left | L1 `0x0004` | L1 |
| `0x8008187A` | 9 | look one way | L2 `0x0001` | L2 |
| `0x8008187C` | 10 | strafe right | R1 `0x0008` | R1 |
| `0x8008187E` | 11 | look the other way | R2 `0x0002` | R2 |
| `0x80081880` | 12 | the map, **inferred** | Select `0x0100` | Select |
| `0x80081882` | 13 | the card / options menu | Start `0x0800` | Start |

**Settled in play, not by reading** (2026-10-03, slot 1, preset 3, the `press`
and `peek` verbs): Square (entry 4) sets `0x801B25B3` and runs the swing clock
`0x801B25A4` (`0x0300`..`0x0F00`, then -1); Triangle (entry 6) does nothing
visible with no spell readied; Circle (entry 5) opens the in-game menu (the pad
word freezes while the modal menu runs). A read of the code had entries 4 and 6
the other way round: a new entry-4 press in `func_8002FE1C` calls
`func_8002C040(0)`, which sets `0x801B25B3` (taken for a cast latch), and
`func_8002D2A0` tests entry 6 near its swing-clock writes (line 33537). The swing
starts from entry 4. Entry 7, examine, is `func_800305D8` calling
`func_8005E2D0`, which measures the distance to the objects in front. A save on
this machine dumps as preset 3 (`KF3_ANALOG_PROBE=1` prints the live table), and
in play F, then bound to Circle, opened the menu.

**The keyboard layout and the mouse defaults are preset 3's** (layout version 3).
It is Verdite2's: Space attack, Q magic, F examine, Tab the menu. Versions 1 and
2 had the menu and examine crossed (from reading preset 0), version 2 attack and
magic too; both are migrated once. A player who picks
another preset in the game's own screen moves the pad's verbs and not the keys'.

Select is **inferred** to be the map screen — its handler `func_80019F58(3)` is
an empty stub in this build and the body begins at `func_80019F60`.

The modal menus do their own `PadRead_game` rather than reading `0x801B265C`
(`func_8001A774` keeps the word locally; the card helpers poll `PadRead_game`
directly), which is why the port injects input through `PadReadEvent` and not
through the globals: the menus have to see it too.

## The turn and look routine

`func_8002F5C0` (stage 4) is the game's own turn and look, and everything the
keyboard and the mouse drive goes through it.

**Yaw.** The velocity is the s16 at **`0x801B264C`**. A held turn button adds
`rate >> 2` and clamps the result to plus or minus `rate`, where `rate` is the
s16 at **`0x801B2668`** — `32` while walking, `40` standing. With neither button
the velocity decays toward 0 by the same `rate >> 2`, and the remainder is applied
unclamped (this is the branch a step larger than the clamp uses). The base yaw is
**`0x801B2612 = (yaw + vel) & 0xFFF`**; **Left increases it** (`0x8008186C`),
Right decreases it (`0x8008186E`).

**Pitch.** The velocity is the s16 at **`0x801B264E`**. **R2 alone** adds `3` and
clamps at `32` (`0x8008187E`); **L2 alone** subtracts `3` and clamps at `-32`
(`0x8008187A`); with neither, it decays by `3`. **L2 and R2 together recentre the
pitch**: the velocity is stepped `±8` toward level, limited to a quarter of the
distance to it, and snapped to 0 when it arrives. Base pitch is
**`0x801B2610 = (pitch + vel) & 0xFFF`**, limited by `func_80016A78` (the angle
past the limit is pinned to it) to `0x2BC` and `0xD44` — a 12-bit angle, not a
sign-extended s16.

**The camera's angles** `0x801B2608` (pitch), `0x801B260A` (yaw) and `0x801B260C`
(roll) are **composed** from the base angles by `func_80030FCC` later the same
frame, the same sign, and stage 10 `func_8002B330` reads them. Driving the base
yaw in one tick lands the heading in the same tick.

## The keyboard layout

The port's layout (`KF3_KEYS=fps`, the default for a fresh install): **W/S** walk,
**A/D** strafe, the **arrows** walk and turn, **Space** attack, **F** examine,
**Q** magic, **Tab** or **Escape** the in-game menu, **Enter** Start, **Right Shift** Select.
Escape is a second key on Circle, pressed inside `PAD_dr` by `KeyLayout` as the
arrows are: the runtime's table holds one key a button. Only with this layout in
place, and not for a press that closed a popup or while Escape is the mouse's
capture key.
**L2 and R2 are left unbound**, because pitch is the mouse's and only the mouse's.

It is the port's *default*, not an override: `Configure` runs before
`ConfigManager.Load`, so a fresh install gets it and `settings.json` wins on every
run after. `Install` migrates an existing stock config once; `KF3_KEYS=stock`
asks for RecompOne's own bindings instead. The arrows are added back to Up/Down
as a *second* binding through the `PAD_dr` event, since the binding table holds
one key per button.

## Mouse look

`Mouse` (Verdite Core's since 2026-10-02, `tools/verdite-core/src/Mouse.cs`, with
this game's values in `MouseLook.Game`) collects the motion, and
`patches/MouseLook.cs` is a **replace hook on
`func_8002F5C0`**. The hook pre-loads the axis's velocity with `step ± accel` and
masks that axis's buttons out of the pad word for the call — **both L2 and R2 for
pitch**, because held together they recentre — then puts the word back. The tick
after a mouse-driven tick gets a **zero velocity**, so the view does not coast on
the game's decay when the hand stops. Fractions are carried tick to tick, so a
slow hand still turns.

The mouse's buttons press pad buttons through `PadReadEvent`, attached only while
the pointer is captured: **left Square (attack), right Triangle (magic), middle
Cross (examine)** by default. The game's own config decides the verb, so
remapping in-game moves the mouse with it.

### Capture behaves as a desktop game's

Since 2026-10-07 there is no capture key by default (`KF3_MOUSE_KEY`, `None`):

- **A click on the picture captures**: `OutputView.Hovered`, or the click's own
  position inside `OutputView`'s rectangle, since just after a release ImGui can
  still hold the locked pointer's virtual position. Not while a popup is open,
  `PopupManager`'s or ImGui's own (a menu bar's dropdown), and not while a menu
  holds the pointer. **Focus is not asked**: a click is focus. The buttons held
  at that moment stay out of the pad word until they are let go, so the click is
  not an attack.
- **Losing focus releases** (fork `0101`, `HostWindow.FocusChanged`), and the
  player comes back with a click.
- **A popup opening releases**, as before.
- **A menu releases and gives it back.** `MenuWorld`'s hooks on the framework's
  enter `func_80027198` and leave `func_80027310` call `Mouse.Suspend`: the first
  enter releases a captured pointer and holds it free (no click captures, no key
  either), and the last leave, or the main loop's own stage-15 call after a
  session left some other way, or an overlay load, captures it again if the
  enter took it. Every full-screen menu runs on that framework, so the top menu,
  its pages, the settings page, save and load all release; a sign or a line of
  dialogue (`func_800441D4`) does not, since examine (the middle button)
  dismisses it. The free pointer is for the menu when it takes the mouse.
  **The look routine ends a suspension too** (`Mouse.TakeLook`): it runs only
  while the player walks about, so a leave the game never made cannot keep the
  pointer from the world.
- **Escape opens the menu** (see the keyboard layout), so Escape gives the
  pointer back the way a desktop game's does, and closing the menu captures it
  again. Circle cancels in every chooser, so Escape also backs out.

Measured 2026-10-07 with `KF3_AUTOSTART=new` and four Circles through the shell
once the loop ran: the start menu (entered from `0x8001FA8C`) and the in-game
menu twice (from `0x8001A7B0`) each suspended on enter and resumed on leave. A
headless run has no click, so the release and recapture of a captured pointer
are to be judged in play.

**First play, the same day: the menu released the pointer and nothing captured
it again**, not closing the menu and not a click. Unread: the first build's click
and resume both required `HostWindow.Focused` and no popup, and the click also
`OutputView.Hovered`. The second build drops focus from the click, adds the
rectangle and the look routine's end of a suspension, and logs every refusal:
`mouse: click not captured: <why>` for a click on the world, and `mouse: not
captured again after the menu: <why>` when the leave could not capture. A
suspension the look routine ended says so too. The pointer-capture glyph
(`MouseIndicator`) is gone: a hidden cursor is the state.

## The mouse leads the tick

`ViewSmoothing.cs` already carries the camera between ticks: stage 15 is handed
the last tick's camera and each drawn frame is given the camera interpolated
between the last two ticks. The look routine spends the mouse once a tick, so the
lerp reaches that turn only at the next tick — up to two ticks from hand to
picture. The **mouse lead** shows the motion the frame it happens: the lerp's
share of the last tick's mouse turn is replaced by all of it, and the motion not
yet spent is added on top. It is Verdite2's `FrameSmoothing.MouseLead` adapted to
`Stage15.ViewOverride`, and **it also leads with carrying off under pacing**: the
override is the tick's view plus what the hand has moved since. `Mouse.Lead`
(Instant mouse look, `KF3_MOUSE_LEAD`) gates it.

### Measured

2026-10-02, with a synthetic mouse (a local test hack, not committed: the hand
at 400 pixels a second in 400 ms bursts, a function of wall time, fed through
`Mouse.Poll`), slot 1 in `fdat02`, 15 ticks a second, each run 60 s:

| | 144 fps, lead off | 144 fps, lead on | 60 fps, lead on |
|---|---|---|---|
| view starts moving after the hand | 52.4 ms (max 56) | 6.9 ms (max 7) | 16.9 ms (max 18) |
| view stops after the hand | 95.2 ms (max 111) | 4.0 ms | 0 |
| steady turn, view against the hand | about a tick behind | within 1-2 units | within 1-2 units |

The start is one drawn frame. With the lead on, the drawn yaw matches the hand to
1-2 units (under 0.2°) on every frame, across tick boundaries; the tick then lands
on it, `|applied - asked|` reading 0.00 a tick in `KF3_SMOOTH_PROBE`. The one
wart is Verdite2's: when the hand stops, the view can settle back 1 unit at the
next tick. Pitch, the hand moving down: the view stops at the limit (700) on the
frame the base reaches it and never passes it. Pacing held at 144.0 and 60.0 fps
drawn at 15 ticks/s, the look hook attached in every run.

### Not yet judged by eye

- the pitch direction: whether mouse down looks down;
- sensitivity;
- the feel at 60 fps;
- turning into a menu or a load.

## The menu pointer

`patches/MenuMouse.cs`, Verdite2's `MenuMouse` on this game's widgets
(2026-10-07): point at an item in a menu and the game's own cursor goes to it,
**left click** chooses, **right click** backs out, a left click clear of the
menu's boxes backs out too, and the **wheel** pages a list longer than its
window. On by default (`KF3_MENUMOUSE`, Input ▸ Mouse ▸ "Point at the menus"),
and not under mouse look: the menus give a captured pointer back
(`Mouse.Suspend`, above), so the desktop pointer is what the player points
with. While the pointer is captured nothing is sampled, its position then
being a virtual one.

The game draws its menus out of three widgets, each with its own cursor, so
there are three mechanisms, as in Verdite2 (the routines are in "The in-game
menu, its lists and its font" in `docs/GAME_INTERNALS.md`):

| widget | drawn by | stepped by | driven by |
|---|---|---|---|
| fixed list | `func_800252F4(group, count, cursor, mode)` | the chooser `func_800221E8` | a post writing the chooser's return and, for a click, its out-parameters as its confirm arm does |
| scrolling list | `func_80025468(desc, mode)` | `func_800222FC(desc, items, &confirmed, &cancel)` | a post writing `desc+0x21`/`+0x22` and replaying the move arm (sound `0xC`, `func_80027A9C(items[cursor])`); the wheel writes the page `+0x20` |
| prompt | `func_80025B24(rec0, rec1, flag)` | the chooser, or the prompt's own loop | as the fixed list |

**A page with a loop of its own** keeps its cursor in a register: OPTION 1,
OPTION 2, QUIT GAME's prompt, the item prompt `func_80024C70` and the port's
settings page. A post on the menu's pad read `func_800279A4` ORs in one Up or
Down a read while the hovered row and the drawn cursor disagree, then, for a
click once they agree, the confirm mask, or Right on a page of values (a list
drawn in mode 1, OPTION 1 and 2, and the settings page, whose loop steps on
Right). Three pushes that do not move the drawn cursor stop it until the pointer
moves. The chooser's and the stepper's own reads (return addresses `0x80022234`
and `0x80022384`) are left to their posts.

**The rows are the boxes the game drew**, read off what it draws them from:
a list item is its table record's X, Y less 6 by the template `0x8007E5A0`'s
118 x 24, a prompt's box the same off `0x8007E594` (54 x 24), a scrolling row
`(X + 6, Y + 5 + 16 r)` off the descriptor, 254 wide (the highlight template
`0x8007E5E8`) and 16 tall. The port's settings page draws its boxes itself
and hands them over (`MenuMouse.Rows`). A widget counts only if it was drawn
since the last menu pad read, so a page's leftovers never steer the next loop;
the chooser takes it only with `last + 1` rows. The gutters between boxes are
no row, and a back-out needs a click 8 pixels clear of all of them.

**Backing out is one flag the pad read spends**: right click over the picture
or a left click clear of the menu ORs the cancel mask (`gp+0x3C`) into the next
`func_800279A4`, so every screen with a cancel arm takes it as its own cancel
button. **Whichever device moved last owns the cursor**: hover takes it only
once the pointer has moved, for 2 s, and hands it back when the pad moves it.

The conversion to game pixels is Verdite2's: OutputView's rectangle is
`GameW + 2 * margin` game pixels wide with column 0 at the margin
(`Display.WideMargin`).

### Measured

2026-10-07, slot 1 in `fdat02`, the shell's `point` (a pointer in game pixels,
`docs/DEVELOPMENT.md`) and `KF3_MENUMOUSE_PROBE=1`; all five hooks attached.

- **The top menu**: 7 rows at x 25, y 26 + 26 n, 118 x 24, PORT SETTINGS
  included. The pointer at y 115, 64 and 38 put the cursor on rows 3, 1 and 0;
  at y 51, the gutter, it stayed. A click on USE ITEM opened it.
- **The item list**: 2 entries, 3 visible, rows at x 33, y 152, 254 x 16 (the
  highlight is 17 tall). Hovering row 1 and row 0 wrote `+0x21`/`+0x22` to 1/1
  and 0/0; right click backed out to the top menu.
- **SYSTEM ▸ OPTION 2** (group 5, mode 1, rows at x 39): its own loop followed
  the pointer through injected Downs and Ups (0 to 2 on entry, 2 to 4, 4 to 0);
  two clicks on DISPLAY HP/MP injected two Rights, and the option bytes
  `0x801B25DF..E4` read the same before and after.
- **PORT SETTINGS**: the page's 5 rows, hover to row 1, right click `left,
  nothing changed`.
- **QUIT GAME's prompt**: two boxes at y 156, YES from x 165; hovering YES and
  NO toggled the drawn flag, a click on NO left the prompt. Two right clicks
  closed SYSTEM and the menu, and the beacon's `loop` was true again.
- **A left click clear of the menu** (game 280, 120) backed out of it.

The cards, `settings.json` and `interface.ini` were byte-identical afterwards.

### Not yet measured or judged

- **By eye**: whether the cursor lands under the real pointer at every aspect
  and window size (the shell's pointer skips the conversion), the 2 px gutters
  when sweeping down a list, and whether a click on a value row reads well.
- The wheel (the shell cannot scroll; no list measured was longer than its
  window) and the chooser over a prompt (the format prompt `func_80020560`).
- **BUTTON CONFIG** (`func_8001F31C`) draws no list through `func_800252F4`, so
  only backing out reaches it.

## Analog twin-stick control

`patches/Analog.cs`, Verdite2's `Analog` ported (2026-10-03): the **left stick
walks and strafes, the right stick turns and looks**, by pre-loading the game's
own velocity words and asserting the matching button, so collision, the pitch
limit and the walk all run through the game's code on an amount the stick chose.
The settings, env vars and defaults are Verdite2's under `kf3.analog.*` and
`KF3_ANALOG*` (`docs/ENV_VARS.md`); the page is Input ▸ Gamepad.

**One hook spends both the mouse and the sticks.** `MouseLook`'s replace on
`func_8002F5C0` hands the mouse's whole steps to `Analog.BeforeLook`, which adds
the right stick's share, owns the turn bits while the left stick is deflected
(the runtime binds the left stick to the D-pad, and Left/Right turn), and writes
the velocity through `Drive`. The pad word is put back after the call, as before.
With the sticks centred the mouse path lands on the same step it always did.

**The walk is a replace of its own on `func_8002F9BC`**, read for the port (line
numbers in `generated/game.cs`, 2026-10-03):

| axis | velocity | button + | button − | accel, clamp | no button |
|---|---|---|---|---|---|
| forward | s16 `0x801B2648` | Up | Down | `max>>2`, ±`max` | decays by `max>>3`, applied unclamped |
| strafe | s16 `0x801B2646` | R1 | L1 | `max>>2`, ±`max` | decays by `max>>2`, applied unclamped |

`max` is the s32 at `0x801B2664` (200). The two decays differ, unlike Verdite2's;
it does not matter here, because the stick's walk step is clamped to `max` and
so always takes a button branch: the stick walks no faster than the D-pad. The
walk routine reads only those four masks, never the turn bits. Forward moves
along base yaw `+ 0x400` and strafe right along the base yaw (`func_8002E3F8`),
so `0x801B2612` is a quarter turn behind the heading; nothing here depends on it.

**Putting the pad word back matters.** Stage 4 reads `0x801B265C` again after
the look and walk calls and copies it to `0x801B265E`, the previous frame's word
the edge tests use; a word left changed would be next frame's "previous".

`KF3_ANALOG_PROBE=1` (`patches/AnalogProbe.cs`) reports, every 10 s, how many
ticks the sticks drove, the velocities, the yaw steps and the pitch, and dumps the
action-mask table once. It counts a tick by `FramePacing.Ticks`: stage 4's post
runs every drawn frame, its body only on a tick.

### Measured

2026-10-03, slot 1 in `fdat02`, 144 fps, the sticks synthesised (a local test
hack overriding the bytes `Analog` reads, not committed; no pad was attached):

- Sticks centred: the hooks attach, the probe reads `look 0 move 0`, and pacing
  holds at 144.0 fps drawn and 15.0 ticks/s. The D-pad (`KF3_AUTOPAD` Left for 4 s)
  still turns: 51 of 149 ticks stepped, mean 38.
- Right stick full right: every tick steps the yaw by exactly 88 (rate 40 x the
  ramp's 2.2), `turnVel` -88, so the overspeed path holds and right is yaw
  decreasing.
- Left stick at (+0.56, −0.68), right stick at +0.38 down: `fwdVel` 136 and
  `strafeVel` 111 (0.68 and 0.56 of 200), the player walks until a wall stops
  them, and the pitch runs to the limit 700 and stays there.

### Not yet judged by eye

- **the pitch direction**: stick down and mouse down both raise the base pitch,
  which in Verdite2 looks down; Verdite3's sign is the same plumbing, not checked
  on screen;
- the left stick's leak into turning on a real pad (the synthetic stick did not
  go through the runtime's D-pad binding);
- the feel: sensitivities, the ramp, the deadzones, at 60 and 144 fps.

## Turning a picked-up item

`patches/ItemTurn.cs` (2026-10-07): while the game holds an item up in the middle
of the screen, **the mouse, the right stick and, when asked, the pad's
gyroscope turn it**. Left and right turn it on its own vertical, as the game's
spin does; up and down tip it toward and away from the eye, a quarter turn
either way, so the top and the underside can be seen. Turning stops the game's
spin, and the spin comes back 1.5 s after the hand lets go. When the item is put
away or taken, the turn eases back to nothing over about a tenth of a second while
the game turns it to face the camera or flies it out. Settings ▸ Gameplay:
**Turn a picked-up item** (`kf3.itemturn.on`, on) and under it **Gyro turns it
too** (`kf3.itemturn.gyro`, off).

**The pickup is `func_8005DB30`**, a loop that draws its own frames (the
return addresses of its stage-15 calls name its parts):

| stage 15 returns to | what is drawing |
|---|---|
| `0x8005DECC` | waiting for the item's model to be resident (`func_800405E8`) |
| `0x8005DF84` | the fly-in, a scale from 0 to `0x1000` by `0x200` |
| `0x8005DFE8` | **the hold**: yaw `+0x40` a pass until a new button |
| `0x8005E194` | the item put back: yaw `+0x100` a pass until it faces the camera (`0x801B260A + 0x800`) |
| `0x8005E278` | the fly-out, the scale back to 0 |

Examine (mask entry 7) takes the item; another button puts back an item already
in the world (`a0` a record, so `fp` stays `-1`), and takes one the routine made
itself (`a0 = 0`, `fp` the item id in `a1`). The record is at the routine's
`sp+0x80`, an Objects-table record (`0x80191A5C`, `0x44` each): `+0x14` the
position, **`+0x24`/`+0x26`/`+0x28` the rotation x, y, z**, `+0x06` the item id.

**The turn is drawn, not written.** `ModelWalk.Carry` asks `ItemTurn.Present`
for the rotation of the record being drawn, after the smoother's, every drawn
frame; the record keeps what the game wrote, so the spin, the turn back and its
comparison see their own angles. The one write is the spin's step taken back
(`+0x26 - 0x40`) on the hold's own call while the player is turning (a loop
pacing redraw comes back with the same return address, so `LoopPacing.InRedraw`
tells them apart). Without smoothing the walk widens its frame to the carry
frame while an item is up, to hold the substitute rotation.

**The geometry.** The game's `RotMatrix` is `func_800166F4`, `Ry'(y) Rx(x) Rz(z)`
with `Ry'(t) = [[c,0,-s],[0,1,0],[s,0,c]]` (`func_8001660C`; `func_80016598` and
`func_80016680` are the usual Rx and Rz). The camera's yaw is the s16 at
`0x801AEC5E`; the item is placed along `yaw + 0x400` and the pose it turns back
to is `Ry'(yaw)` (the walk adds `0x800` to the record's y), so the camera's right
is `Ry'(yaw)` applied to x. The drawn rotation is
`Ry'(yaw) Rx(tilt) Ry'(-yaw) · Ry'(y + turn) Rx(x) Rz(z)`, read back into the
game's three angles. Checked numerically: 20000 random turns come back from the
12-bit angles within 0.0015 of the matrix (one angle unit), and a positive turn
brings the side nearest the eye right, a positive tilt brings it down.

**The inputs.** The mouse is `Mouse.TakeLook`, taken every drawn frame while
the item is up (so `Mouse.Live` holds); the view's lead (`ViewSmoothing`) is off
for the whole pickup, which would otherwise show that motion as a turn of the
camera. The stick is `Analog.RightStick`, shaped as the look shapes it, full
deflection `0x800` a second times the look sensitivities and inversions. The
gyroscope is the runtime's (`Controller.GyroX`/`GyroY`, radians a second, fork
patch `0102`), read only while the setting is on, which is also when the runtime
switches the sensor on; SDL's axes for a pad held in front are x across, y up, z
toward the player, so y is the turn and x the tilt, one to one (the item turns as
far as the pad does). A rate under 0.05 rad/s is taken as a hand at rest, so a
pad's drift neither creeps the item nor holds its spin.

### Measured

2026-10-07, slot 1 in `fdat02`, 165 fps, `KF3_ITEMTURN_TEST=0x6B` (an item lying
in that area, so its model is loaded) and `KF3_AUTOPAD=17:Cross:300`:

- The pickup reached by a run for the first time: loop pacing held it at 15
  passes a second, 10 redraws each, and the picture at 165.0 fps throughout.
- The walk asks for the record every drawn frame (about 165 a second), and with
  the stick (synthesised: full right and half down for one second) it drew the
  turned rotation on every frame: yaw `+0x800` a second, the tilt to its limit
  `0x400`.
- The spin was held on 15 passes a second while turning and for 1.5 s after,
  then the game's spin came back (1, then 0 held).
- Cross took the item: the fly-out drew it easing from yaw -1536, tilt 1024, to
  -1 and 1 by the time the routine returned, and the walk was back in the
  world. No exceptions.
- **Not reached**: an item already in the world. Called with its record from the
  player's tick, the routine waited in the model loop (`0x8005DECC`) for good
  (two records tried), where the game calls it from the examine handler
  (`func_8005E2D0`); the put-back path differs only in drawing `0x8005E194` first. The
  mouse (no pointer in a scripted run) and the gyroscope (no pad with one) were
  not driven; they feed the same two sums the stick does.

### Not yet judged by eye

- **the directions**: right should bring the near side right and down bring it
  down, for the mouse, the stick and the gyro, as derived from the camera's axes
  above; not checked on screen;
- the gyroscope on a real pad: which way is which, the drift, whether one to one
  feels right;
- the feel: the stick's rate, the 1.5 s before the spin comes back, the ease on
  letting go.

## Gyro aim with a drawn bow

`patches/GyroAim.cs` (switch `KF3_GYROAIM`, off by default): while a bow is in
hand and being drawn, **the pad's gyroscope turns and tips the view, one to one**.
Settings ▸ Gameplay: **Gyro aims a drawn bow** (`kf3.gyroaim.on`, off), drawn under
the item-turn rows. The game's own PORT SETTINGS page GAMEPLAY has one row for
both gyroscope readers, **GYRO** (`row.gyro`, `docs/SETTINGS.md`): ON sets this and
**Gyro turns it too** together, OFF clears both, and one without the other shows
CUSTOM.

**A bow** is weapon id 27 (LARGE BOW) or 28 (ELCHRIS BOW), read off the name table
at `0x8007F620` (stride `0x18`), in the equipped-weapon byte `0x801B25AF`.

**The gate** is "drawn": the arm's swing clock `s16 0x801B25A4` is not -1 (an
attack press sets it going), or the attack action's mask (`u16 0x80081870`, entry
4 of the action mask table) is set in the pad word `0x801B265C`. The mask is read
rather than a button, so whatever the layout binds to attack, a mouse button
included, draws. Whether the game holds a bow's draw while attack is held, or
fires on the press and runs the clock about 0.7 s regardless, is **not known**:
nobody has watched it.

**The rate and its spending.** The rate is integrated on every `VSyncEvent` with
the stopwatch's step, clamped to 0.1 s. A rate under 0.05 rad/s is a hand at rest
(ItemTurn's `GyroRest`). The integral is in angle units at 4096/2π a radian, one to
one. `GyroAim.Take()` is called in `patches/MouseLook.cs` just before
`Analog.BeforeLook`, which adds the sum to the stick's share (`stickTurn`,
`stickPitch`); so the gyro gets the mouse's per-tick ceiling (`Mouse.StepCap`) and
stops on the same tick as the mouse. The sum is dropped when the gate fails, while
`ItemTurn` holds the mouse, or when the look routine has not taken it for 250 ms
(a menu, a cutscene).

**The signs.** SDL's axes for a pad held in front: +y (the pad turned left) turns
the view left, which is yaw increasing (Left `0x8008186C` increases yaw). +x (the
far edge tipped up) lowers pitch, the way a mouse pushed away goes, so
`Mouse.InvertY` flips it as it flips the mouse.

**The shared request.** Both gyro readers use `Controller.WantGyro`, and each
writes it: `GyroAim.WantGyro()` sets it to `GyroAim.Enabled || (ItemTurn.Enabled
&& ItemTurn.UseGyro)`, and `ItemTurn.SetGyro` calls it. So turning either setting
off no longer switches the sensor off under the other one.

**The probe.** `KF3_GYROAIM_PROBE=1` prints a line a second: on or off, the pad's
gyro on or off, the weapon id, the swing clock, attack held or up, the vblanks
gated in, the ticks spent, and the units turned and pitched.

### Measured

Builds. Nothing run on a pad.

### Not yet judged by eye

- the signs on a real pad: a left turn of the pad turns the view left, and the far
  edge tipped up looks up;
- whether one to one feels right, and the drift past the 0.05 rad/s rest;
- the gate's timing against the bow's actual draw: whether the swing clock or the
  attack mask is the right edge, since the game's hold is not known.

## The Input pane is the port's

`patches/InputSection.cs` **replaces** the runtime's Input section rather than
extending it. `SettingsRegistry.Register` removes by id and then adds, so
registering a section with the runtime's own id (`input`) on `RuntimeReadyEvent`
— after `HostWindow.Load` has registered the runtime's five — takes the pane
over. `Unregister("input")` is deliberately *not* called first: it would state
removal where the intent is substitution and would hide the one failure worth
naming. `Install` instead checks whether a section with id `input` already
exists and warns on the console if it does not, because then the register adds a
second Input tab rather than replacing the first. Only the `Register` line
stands between the port and the runtime's pane, so the wrapper is cheap to
abandon.

The pane is one tab bar with three tabs — Keyboard, Gamepad, Mouse — each body
in its own `BeginChild` taking the remaining height, so the bar never scrolls
away. `patches/BindingTable.cs` is the runtime's own table copied (that section
is `internal`, so it is unreachable): sixteen rows, one capture field per
device, an action column saying what each button does in King's Field by
default. The action verbs are measured from the action-mask table at
`0x80081868` and are English, not localised; a wrapped note under the table says
the game's own control-config screen can reassign them. The Pad 1 / Pad 2 tab
bar is dropped — the game reads pad 1 only, and `Keys2`/`Pad2` are never read or
written — and `MapButtonPage` does not exist here. Keyboard reset calls
`KeyLayout.ApplyStock` so it agrees with the RecompOne-layout button directly
above it; gamepad reset is a plain new `GamepadBindings` for pad 1.
