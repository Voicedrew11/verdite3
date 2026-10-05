# Input: pad, keyboard and mouse

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
`0x80081868`**. Its defaults:

| mask address | default | button | action |
|---|---|---|---|
| `0x80081868` | `0x1000` | Up | walk forward |
| `0x8008186A` | `0x4000` | Down | walk back |
| `0x8008186C` | `0x8000` | Left | turn left |
| `0x8008186E` | `0x2000` | Right | turn right |
| `0x80081870` | `0x0010` | Triangle | attack **inferred**; Square in play |
| `0x80081872` | `0x0040` | Cross | the in-game menu; Circle in play |
| `0x80081874` | `0x0080` | Square | magic **inferred**; Triangle in play |
| `0x80081876` | `0x0020` | Circle | examine / interact **inferred**; Cross in play |
| `0x80081878` | `0x0004` | L1 | strafe left |
| `0x8008187A` | `0x0001` | L2 | look one way |
| `0x8008187C` | `0x0008` | R1 | strafe right |
| `0x8008187E` | `0x0002` | R2 | look the other way |
| `0x80081880` | `0x0100` | Select | the map; **inferred** |
| `0x80081882` | `0x0800` | Start | the card / options menu |

**`func_8001F4C0`, the control-config screen, rewrites the table** from preset
indices at `0x801B25E2` (face layout) and `0x801B25E3` (direction layout). The
direction presets rewrite the eight direction entries; the face presets only ever
swap **Triangle to Square** and **Cross to Circle**, i.e. the pairs {attack,
magic} and {menu, examine}. The attack/magic/examine names above are therefore
**inferred** from those config strings; the buttons and code paths are read.
**The game does not play on these defaults.** Read live 2026-10-05 from a New
Game in `fdat02`: the face preset byte `0x801B25E2` is `3`, and the table holds
Square at `0x80081870`, Circle at `0x80081872`, Triangle at `0x80081874` and
Cross at `0x80081876`. A Circle press through the command channel opened the
menu (`loop` went false). That is Verdite2's arrangement, and the keyboard and
mouse defaults follow it; layout version 1 followed the column above and put
the menu on F.

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
**A/D** strafe, the **arrows** walk and turn, **Space** attack (Square), **F** examine
and confirm (Cross), **Q** magic (Triangle), **Tab** the in-game menu (Circle), **Enter** Start, **Right Shift** Select.
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
