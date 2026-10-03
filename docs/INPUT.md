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
**Q** magic, **Tab** the in-game menu, **Enter** Start, **Right Shift** Select.
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
remapping in-game moves the mouse with it. **Escape** captures and releases by
default (`KF3_MOUSE_KEY`); a popup opening takes the pointer back.

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
