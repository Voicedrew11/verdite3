# Smoothing: the picture carried between ticks

The plan for drawing frames between the world's ticks that actually move, and
its work as it is done. Frame pacing itself is "Frame pacing" in
`docs/DEVELOPMENT.md`; the routines named here are "The geometry path" in
`docs/GAME_INTERNALS.md`. Verdite2's smoothers (`FrameSmoothing`,
`ObjectSmoothing`, `AnimSmoothing`, `FluidSmoothing`, `LoopPacing`, `SpriteAnim`)
and their write-ups in its `docs/PATCHES_AND_MODS.md` are the background.

## Status

**Units 1 and 2 built** (2026-10-02). Stage 15 and its camera block are C#,
verified (0 mismatches standing, turning, walking, and with the menu opened and
closed) and **on**; the camera is carried between ticks, **judged by the user
2026-10-02 and on whenever pacing is** (`KF3_SMOOTH=0` to compare); the billboard
cels and the compass needle are held to the tick under pacing. **Unit 3 is next;
its handoff is at the end of this file.** Chosen by the user as the next work, ahead of the near path in
`docs/GEOMETRY.md`, because it is what a player sees first.

## Why

Frame pacing (`KF3_FPS`) runs the world at 15 Hz and draws as often as asked, so
every counter in the game keeps its rate. But stage 15 draws each frame from the
state the last tick left: at 144 fps that is the same picture about ten times,
which reads as stutter, not as 144 fps. Smoothing draws each frame from the
world **interpolated** between the last two ticks.

In Verdite2 the point of the smoothing work turned out to be animation quality,
above all the judder when a creature's clip loops; the camera and the objects are
the machinery under it. The order below still starts with the camera, because it
is the largest visible change and the base the rest stands on.

## The decision: own the stage, then interpolate

Verdite2 built its smoothers first, as hooks that write a carried value into RAM
before a routine and put the real one back after (`FrameSmoothing` brackets
stage 8, the routine that builds the render camera). It worked, with one class of
defect built in: anything that read the player's position by another route than
the bracketed routine was drawn at the tick, not the carried eye, so the arm and
the creatures sheared against the walls between ticks. Verdite2 later rewrote
stage 13 and its camera block in C# for other reasons, and the seam that came out
of that, `Stage13.ViewOverride` (draw the frame from a camera of the port's, the
cull grid following), is what smoothing should have stood on.

**Verdite3 starts there**: stage 15 and its camera block in C#, verified, with a
view override; then each smoother is a value handed to C# that already places
the geometry, not a write into RAM and a put-back. Verdite2's code is the
template for the rewrite and its findings are the rules; its smoother files are
not ported (most of their length is Verdite2's own: mouse lead, the compass and
gauges round its HUD builder, the analog hand-off, a settings page).

## What makes stage 15 the seam (read 2026-10-02)

- **Stage 15 (`func_800422B8`) takes the camera as its arguments**: `a0` a
  position (`VECTOR`), `a1` a rotation (`SVECTOR`), handed unchanged to call #1,
  the camera block `func_800357E8(pos, rot)`. It does not fetch the camera
  itself.
- **Its three callers**:
  - the main loop `func_80014BD4`, at `0x80014FA8`, with `a0 = sp + 0x18` and
    `a1 = sp + 0x28`: two blocks in the main loop's own frame, filled by one of
    stages 1-14 (not yet identified; Verdite2's counterpart is stage 8,
    `func_80025A1C`). Under pacing stages 1-14 run only on a tick, so on the
    frames between, these blocks hold the last tick's camera: exactly the pair
    an interpolator needs.
  - `func_80030568` and `func_800305D8`, with `a0 = a1 = 0`. **The camera block
    keeps the previous camera for a null pointer** (it tests `a0` and `a1`
    separately), so these are redraws from the last camera: menus or loops that
    draw their own frames (not yet identified which).
- **The camera block** writes the position to `0x801AEC4C` and its tile
  (`>> 11`) to `0x801AEC64`/`68`, the rotation to `0x801AEC5C`, `RotMatrix` into
  the view matrix `0x801AEB4C`, the pitch helper into `0x801AEC0C`, and four
  view-times-rotation matrices at `0x801AEB8C + 0x20 k`. Everything after it in
  the stage (the cull grid at call #4, the map and model walks) should read the
  camera from those, which is what makes the override consistent; **that is to be
  confirmed while writing stage 15** (the open checks below).
- Under pacing (`patches/FramePacing.cs`), stages 1-14 are gated to the tick and
  stage 15 is not: it runs on every drawn frame.

## The units

### 1. Stage 15 and the camera block in C#, with verify

Nothing visible changes; verify is the proof.

- `func_800422B8` as a replace hook in a new `patches/Stage15.cs`: its 22 calls
  in order, each through the dispatcher so every hook on a callee still fires,
  and the inline HUD block (the HUD model table at `0x800819B4..0x80081C44` from
  the player block). Template: Verdite2's `patches/Stage13.cs` (stage 13 is stage
  15 with one call fewer and two fewer full-screen quads; "Stage 15's calls,
  measured" in `docs/GAME_INTERNALS.md` pairs them). Write down the gp bytes the
  prologue stores (`gp + 0xCC..0xCE` from `0x801B25E1`).
- `func_800357E8` in C# (`patches/CameraBlock.cs`), Verdite2's `CameraBlock` as
  the template, with Verdite3's differences: no yaw negation, and the four
  precomposed matrices (`func_80016290`).
- `KF3_STAGE15=0|1|verify` and `KF3_CAMERABLOCK=0|1|verify`, **0 by default
  until a session of verify reads clean**, as `KF3_POLYASM` went. Verify:
  Verdite2's `KF2_STAGE13=verify` records the recompiled stage's calls and
  replays ours against them; the camera block compares RAM, the scratchpad,
  registers and the GTE with `PolyAssembler`'s harness (generalise it into one
  file rather than copying it).
- `Stage15.ViewOverride`: a camera of the port's (position, rotation) given to
  the camera block in place of the caller's, for one stage call, and nothing
  else changed. Shell verb `view` (as Verdite2's) to draw from a fixed camera,
  which is how the override is checked without smoothing.

**Done when**: verify 0 mismatches over a session in `fdat02` (standing,
turning, walking, the menu opened and closed); `KF3_GEOPROBE=1` identical with
both on and off; `KF3_FPS=144 KF3_FPS_PROBE=1` 144.0 fps at 15.0 ticks/s; the
uncapped rate measured both ways; the open checks below answered in this file.

### 2. The camera carried, and the billboard clock held

The first visible unit; the user judges it.

- Record the camera the main loop hands stage 15 at each tick (previous and
  current), and on every frame hand `ViewOverride` the interpolation at the
  tick fraction `FramePacing` already knows. **Interpolate, never
  extrapolate**: Verdite2's first carry predicted forward and bounced back
  whenever a turn eased off or the player stopped at a wall.
- Angles wrap at 12 bits (interpolate the short way round); a jump larger than
  a step (a warp, a load, an area change, a cutscene cut) snaps instead of
  sweeping, and the pair re-primes on an area change (Verdite2: walking between
  areas stuttered while its smoothers re-primed).
- The null-camera callers keep drawing from the last camera, so a menu or a
  loop's own frames need nothing here (`LoopPacing` is later).
- **The billboard clock** at `0x80182964` (and the nine 0x18-byte records after
  it) is bumped by the model walk on every walk, so under pacing billboards
  animate at the drawn rate ("What still runs at the render rate" in
  `docs/GAME_INTERNALS.md`). Hold it to the tick (Verdite2's `SpriteAnim`), and
  give the hold the stage gate's watchdog: a hold keyed on the frame fails
  closed.
- `KF3_SMOOTH=0` to compare; **off by default until the user has judged it**,
  as frame pacing is.

**Done when**: the rate census (`KF3_RATECENSUS`) no longer lists `0x80182964`;
144.0 fps at 15.0 ticks/s; a probe line (`KF3_SMOOTH_PROBE=1`) shows the carried
camera moving on every frame between ticks; and the user has looked at turning
and walking at 144 fps.

### 3. Later, on the same structure

- **Creatures and objects**: the model walk `func_80040AE4` and the submitter
  `func_8003E34C` in C# (Verdite2's `ModelWalk`), reading each record's position
  and rotation interpolated, without writing them into RAM. The four tables, their
  strides and their liveness tests are in "The models" in
  `docs/GAME_INTERNALS.md`.
- **Poses**: the MO pose blender `func_800431E8` in C# (Verdite2's `MoPose`,
  whose fields it shares), blending at a fractional weight between ticks. This is
  where the judder at a clip's loop point is fixed (Verdite2's `AnimSmoothing`:
  the loop point is the clip's cycle turnover, not anything about placement).
- **The rest**: the compass and the HUD gauges with the view; the animated
  textures (`func_800351FC`, Verdite2's `FluidSmoothing`); loops that draw their
  own frames (Verdite2's `LoopPacing`).

## Rules carried from Verdite2

- **A tick is a frame identity, not a flag**: something that must happen once a
  tick inside stage 15 keys on the tick's number, not on "ticked this frame".
- **The frame boundary fails open; a hold keyed on the frame fails closed**; both
  need the stage gate's 500 ms watchdog.
- **A gated stage must not draw**, and a value stepped inside a drawing
  function's own body cannot be gated; it needs a hold and a restore.
- **A liveness test is the renderer's**: a creature is drawn when `u8[+0x9] == 1`,
  an object when `u16[+0x6] != 0xFF`.
- **Hook order is declared, not implied** by where an `Install()` sits; on
  stage 15 the smoother restores run before anything that redraws.

## Unit 1, done: stage 15 and the camera block in C#

`patches/Stage15.cs` (drafted by an opencode agent from Verdite2's `Stage13.cs`
and reviewed), `patches/CameraBlock.cs`, and `patches/Differential.cs`, Verdite2's
verify harness with the scratchpad added (`PolyAssembler`'s verify now uses it
too). `KF3_STAGE15` and `KF3_CAMERABLOCK` are on; `=0` is the recompiled routine,
`=verify` the comparison.

- **Verify**: 0 mismatches over a session in `fdat02` with slot 1, standing,
  turning, walking (`KF3_AUTOPAD=12:Left:3000,17:Up:5000,23:Right:2000`), and with
  the menu opened (Cross) and closed (Circle); `KF3_POLYASM=verify` on at the same
  time, also 0. Stage 15's verify records the **sequence** of calls, since the
  compass call is made only while `u8[0x801B25DE]` is non-zero (21 or 22 calls).
  This save runs the HUD block's mode 2 (`u8[0x801B25DD] == 2`) and the compass
  branch; **mode 1 has not run under verify**.
- **The prologue** copies `u8[0x801B25E1]` to `gp + 0xCC`, `0xCD` and `0xCE`.
- `KF3_GEOPROBE=1`: the per-call table (packets, slots, codes, sizes) is identical
  with both on and both off.
- `KF3_FPS=144`: 144.0 fps drawn at 15.0 ticks/s. Uncapped (`KF3_FPS=off`,
  standing): 1331.6 and 1297.9 fps recompiled, 1308.8 with both in C#, 1300.4
  with smoothing on: the same, within run-to-run noise.
- **The menu does not call stage 15**: while it is open no stage 15 call is
  made (its loop presents frames of its own), so the menu's frames neither need
  nor see the override.
- **`Stage15.ViewOverride`** draws the frame from a camera of the port's: stored
  into the block and `a0 = a1 = 0` handed to it, so the cull grid, the map walk
  and the submitter follow (they read the block). After the frame the block is
  rebuilt from the camera the frame would have used, so the world's stages never
  read the drawn one. Shell verb `view [x y z pitch yaw roll | off]`, checked:
  the block reads the given camera while it is set and the handed one after `off`.
- **The compass needle** (`Stage15`, `KF3_STAGE15_NEEDLE=0` to compare): the HUD
  block steps the needle's spring (speed at `gp + 0xD8`, `0x8009C2EC`; yaw in the
  records at `0x80081C3A` and `0x80081C5E`) every call, so under pacing it swung at
  the drawn rate. It now steps on the first stage 15 of a tick
  (`FramePacing.FirstWalkOfTick`). A rate census while turning
  (`KF3_RATECENSUS=12`, Left and Right in turn) listed the three words held off;
  with the hold, none.

### The open checks, answered

- **Stage 10, `func_8002B330(sp+0x18, sp+0x28)`, fills the camera**: position =
  player x, **player y + `s16[0x801B2650]` + `s16[0x801B2654]` - `0x640`**, player
  z; rotation = the three halfwords at `0x801B2608` (pitch, the heading at
  `0x801B260A`, roll). Read from the code, and the handed camera reads
  `y = -14400` standing at `-12800`, both offsets 0. The two offsets are written
  by stage 4 `func_80030FCC` and `func_8002ED60` (a fall and a bob, by the code's
  shape; not watched moving). So the carry interpolates the eye with its bob.
  Stage 11 `func_800156BC` also takes the pointers; an opencode reading says it
  copies the player's position to `0x801B2A10` and does not write them, not
  checked further.
- **What inside stage 15 reads the player rather than the block**: the arm
  `func_8003DF50` reads the player's x and z (its light's tile); the model walk
  reads them for the ambient-sound gate `func_80046884` (a box test, not a
  listener). **The cull grid reads only the block** (angles `0x801AEC5C/5E`, tile
  `0x801AEC64/68`, and the flag `u8[0x801B25E5]`), so it follows the override.
  The map walk and the submitter read the block; the queries read the grid.
- **`func_80030568` and `func_800305D8`** (stage 15 with `0, 0`): an opencode
  reading puts `func_800305D8` under stage 4 and `func_80030568` under
  `func_8005C308`/`func_8005E2D0` (message, death or continue flows); not run
  here. They redraw from the block, which after an override holds the real camera.
- **Sound**: no listener reads the camera or the player's position; the sound
  slots read neither.

## Unit 2, done: the camera carried, and the billboard clock held

- **`patches/ViewSmoothing.cs`**, on whenever pacing is (`KF3_SMOOTH=0` to
  compare; needs stage 15 in C#). Judged by the user on 2026-10-02 at 144 fps:
  "it looks right". `Stage15.OnHanded` gives it each camera the main loop
  hands over; on a frame whose iteration ticked (`FramePacing.Ticks` moved) the
  pair shifts, and every frame is drawn from the interpolation at
  `FramePacing.TickFraction` (the clock's credit toward the next tick, 1 when
  the boundary is lost). A constant one-tick lag, never a prediction. Angles go
  the short way at 12 bits; a step over 1536 units or `0x300` in a tick snaps,
  and an overlay load re-primes. Walking moves about 180 units a tick.
- `KF3_SMOOTH_PROBE=1`, 144 fps: **144.0 frames a second with a new camera while
  turning or walking**, 0 standing, 15.0 tick samples a second, 0 snaps across
  the yaw's wrap (4095 to 0). Ticked and idle frames draw the same packets.
- **`patches/SpriteAnim.cs`** (drafted by an opencode agent, checked against the
  code): the model walk draws each billboard (`0x80182968`, 128 x `0x18`), then
  steps its cel `u8[+5]` when the clock `0x80182964` divides its interval
  `u8[+4]` (wrapping at `u8[+3]`), then bumps the clock. On every walk that is
  not a tick's first, a pre saves the clock and the cels and a post puts them
  back, so a held walk draws the tick's cel. On while pacing is on
  (`KF3_SPRITEANIM=0` to compare). Rate census, `KF3_RATECENSUS=15`, standing:
  `0x80182964..70` and the eight other records' words listed without it, none
  with it; 15 walks stepped and 129 held a second at 144 fps. Keyed on the tick
  count, which the watchdog's ticks advance too.

**Judged by eye** (the user, 2026-10-02): turning, walking and the billboards at
144 fps with the carry on.

## Handoff: unit 3, the things in the world carried between ticks

**Where it stands.** Verdite3 `main`, local commits only (none pushed). Units 1
and 2 are done and judged: the camera glides at any frame rate, and everything
else in the world still steps 15 times a second against it. That contrast is the
defect unit 3 removes; the goal, as in Verdite2, is **animation quality, above all
the judder when a creature's clip loops**. Movement of whole models is the
machinery under it.

**Read first**: this file; "The models", "Stage 15's calls, measured", "The
frame's tables and buffers" and "What still runs at the render rate" in
`docs/GAME_INTERNALS.md`; "Frame pacing" in `docs/DEVELOPMENT.md`;
`patches/Stage15.cs`, `patches/ViewSmoothing.cs`, `patches/SpriteAnim.cs`,
`patches/Differential.cs`. In Verdite2 (`~/Desktop/KFII-PC`), the templates:
`patches/ModelWalk.cs` (1608 lines), `patches/MoPose.cs` (465),
`patches/AnimSmoothing.cs` (1588), `patches/ObjectSmoothing.cs` (996); and in its
`docs/PATCHES_AND_MODS.md`: "The camera is not the only thing that moves" and
everything under it to "The arm rides the same clock" (the four tables, the
rotation lanes, the clip as a timeline), "The object and creature walk in C#",
"The compass is carried with the view", "The gauges are carried like the
needle", "Loops that render their own frames"; in its `docs/GAME_INTERNALS.md`,
"The model pipeline has no skeleton" (the clip record's layout and the clip
clock). **Copy conventions and lessons from Verdite2, never its addresses**; a
Verdite3 counterpart is found with `tools/verdite-core/scripts/match_code.py`.

**The design, decided in unit 1**: stage 15 is C#, so every carried value is
**handed to C# that already places the geometry**, never written into a record
and put back. Records stay the game's, so the AI, saves and triggers never see a
carried value. Sample each value on the first stage 15 of a tick
(`FramePacing.FirstWalkOfTick`, or `FramePacing.Ticks` moving on an
`IterationTicked` frame, as `ViewSmoothing.OnHanded` does), keep the previous and
current sample, and draw at `FramePacing.TickFraction`. Interpolate, never
extrapolate. Keep each piece off by its own switch until its verify reads clean,
then on whenever pacing is, as `KF3_SMOOTH` went.

### 3a. The model walk in C#, with verify (nothing visible changes)

- `func_80040AE4` as a replace hook, `patches/ModelWalk.cs`, every call through
  its detour (the submitter `func_8003E34C`, the sky `func_800400AC`, the front
  table submitter `func_8003F304`, the queries `func_80040694`/`func_80040708`,
  the ambient sound gate `func_80046884`, and the rest). The generated routine is
  about 1180 lines; it walks the four tables in "The models" with their liveness
  tests, then the billboards (whose cel stepping `SpriteAnim` now brackets: fold
  that hold into the C# walk and retire the pre/post pair).
- `KF3_MODELWALK=0|1|verify`, verify with `Differential` (RAM, scratchpad,
  registers, GTE; the walk keeps its page bitmaps in the scratchpad at `+0x124`
  and `+0x270`). Done when verify reads 0 over a session in `fdat02` with
  creatures and objects on screen, and `KF3_GEOPROBE=1` is identical on and off.
- **Write down, for each table, which record fields reach the submitter as its
  arguments**: position, rotation (a creature's yaw has `0x800` added, "The
  models"), model, clip byte and clip time. That list is 3b's input. A table's
  entry may be a copy of another (in Verdite2 stage 4 copied object positions into
  the creature record, which then drew from the copy); find which one is drawn.

### 3b. Positions and rotations carried

- In the C# walk, hand the submitter each record's position and rotation
  interpolated between its last two tick samples, keyed by table and slot, with
  the slot's identity (its model and liveness) re-checked so a reused slot snaps
  rather than sweeping from its last occupant. A jump larger than a step (a
  spawn, a warp, an area change) snaps; angles go the short way at 12 bits.
- **All four tables**: Verdite2 carried two at first, and both omissions reached a
  player as "the animation runs at a low frame rate". The object table has a
  rotation lane (doors turn).
- The point and volume queries cull against the eye; check they are asked about
  the drawn position, not the ticked one, or a model pops at the cull's edge.
- Probe: `KF3_SMOOTH_PROBE` extended with models carried a second and snaps.
  Done when a creature walking past reads a new position on every drawn frame,
  and the user has looked.

### 3c. The MO pose blender in C#, with verify

- `func_800431E8` (Verdite2's `func_80034DA8`, the same field offsets; its
  decoders are listed in "The models"). Find its clip clock (Verdite2's
  `func_8003486C(bank, clip, time, &segment, &weight)`: walks the clip's segments,
  publishes a 12.12 weight, and `0x1000 - weight` for a reversed segment) with
  `match_code.py`, and confirm the clip record's layout (clip table at
  `bank + u32[bank + 0x10]`, segment durations at `u16[seg + 2]`) holds here.
- `KF3_MOPOSE=0|1|verify`. In Verdite2 the blender re-morphs on every drawn frame
  even when the clip and segment are unchanged (`L80034FCC`), so only the time it
  morphs to is stuck on the tick; check that holds here, since it is what makes
  a fractional time move the mesh.

### 3d. The clip time carried: the loop-point judder

- The clip byte and the clip time are the submitter's stack arguments, not table
  fields (in Verdite2 the eighth and ninth stack words); 3a's list says where each
  table's come from.
- **Treat the clip time as a point on a circle whose length is the clip's own**
  (the sum of its segment durations; every clip Verdite2 measured was 4096). Each
  tick: the clip byte changed is a cut (draw the game's pose); otherwise unwrap the
  step against the settled rate, `k = round((rate - step) / length)`, with the two
  reflection candidates for a clip that turns at an end; nothing matched is a
  re-seek, so hold at the game's time and re-seed the rate. Draw at
  `prev + delta * fraction` folded back onto the clip, `floor` to the clock and
  the fraction into the weight. This is Verdite2's `Mode.Timeline`, which replaced
  five heuristics that each guessed at the wrap; read "The clip is a timeline, and
  none of that knew how long it was" before writing any of it.
- The first-person arm (`func_8003DF50`, returns at once while
  `s16[0x801B25A4] == -1`, as on slot 1) rides the same clock in Verdite2.
- Done when a looping creature's clip time reads a new value every drawn frame
  and **no backward step at the wrap** (a probe line: carried frames, wraps
  recognised, re-seeks), and the user has looked at a creature looping.

### 3e. The HUD with the view, and what else steps

- The compass needle steps on the tick since unit 1 and now lags the carried view;
  carry its yaw (`0x80081C3A`/`0x80081C5E`) between its tick samples, inside
  `Stage15.Run`'s HUD block, written for the HUD's call and restored after
  (Verdite2: "The compass is carried with the view"). The gauges' lengths the
  same way, if they are seen to step.
- Stage 15 call #2, `func_800351FC` (animated textures, Verdite2's
  `FluidSmoothing`), runs every drawn frame under pacing: find what it advances
  (the unidentified render-rate words in "What still runs at the render rate" are
  the leads; Verdite2's `find_writers` finds the code that writes a word).
- Loops that draw their own frames (Verdite2's `LoopPacing`): the in-game menu
  does not call stage 15 here; find which loops do (`func_80030568`,
  `func_800305D8`, with `0, 0`) and whether they need pacing at all.

**Running the game**: a connected **DualSense stalls `KF3_AUTOSTART` in OPEN.EXE**
(found 2026-10-02, cause not read); hide it from SDL with
`SDL_GAMECONTROLLER_IGNORE_DEVICES=0x054C/0x0CE6`. Only one save exists (card A
slot 1); keep a copy of `carda.sav` before driving menus, since Cross saves over
the slot. Check every pacing change with `KF3_FPS=144 KF3_FPS_PROBE=1`: 144.0 fps
drawn at 15.0 ticks/s, equal packets ticked and idle.

**Using opencode agents** (as units 1 and 2 did): each in a worktree of its own
(`git worktree add ../v3wt-<name> -b <branch> main`, with `generated` and the
cue symlinked in, and Verdite2's files it needs copied into its `scratch/`, since
opencode works inside its directory), disjoint files per agent, at most five at
once, and only the orchestrator runs the game. A transcription from generated
code is a good agent task; verify is what makes it trustworthy. A reading of the
code is not: unit 1's research agent named the wrong stage as the camera's source
and the wrong reads in the cull grid. Check an agent's claims against the code.

**Deferred by this plan**: the near path and the Z-buffer (`docs/GEOMETRY.md`,
"After this unit"), and the shared fill's extraction (Verdite2's
`docs/SHARING.md`). Nothing waits on them.

**Don't**: push anything without asking; let an opencode agent share a checkout
being edited; run more than about five subagents at once; screenshot the game
(what needs eyes is the user's to judge: ask).
