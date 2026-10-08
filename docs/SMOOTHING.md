# Smoothing: the picture carried between ticks

The plan for drawing frames between the world's ticks that actually move, and
its work as it is done. Frame pacing itself is "Frame pacing" in
`docs/DEVELOPMENT.md`; the routines named here are "The geometry path" in
`docs/GAME_INTERNALS.md`. Verdite2's smoothers (`FrameSmoothing`,
`ObjectSmoothing`, `AnimSmoothing`, `FluidSmoothing`, `LoopPacing`, `SpriteAnim`)
and their write-ups in its `docs/PATCHES_AND_MODS.md` are the background.

## Status

**Units 1, 2 and 3 built** (2026-10-02). Stage 15 and its camera block are C#,
verified and **on**; the camera is carried between ticks, **judged by the user
2026-10-02 and on whenever pacing is** (`KF3_SMOOTH=0` to compare); the billboard
cels, the compass needle's spring and the scrolling textures are held to the tick.
Unit 3: the model walk and the MO pose blender are C#, verified (0 mismatches with
creatures in view) and **on**; the compass needle and the HUD gauges are carried
with the view; the creatures, objects, effects and billboards and their clip
times are carried, **judged by the user 2026-10-02 ("looks good") and on whenever
pacing is** (`KF3_SMOOTH_MODELS=0` to compare). What is left is at the end of this
file, and the user has put all of it off.

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
  areas stuttered while its smoothers re-primed). **Changed 2026-10-06**: a
  position jump (a placement: a crossing, a warp) no longer snaps. On a tick,
  `prev` is put the last tick's step behind `cur`; off a tick the whole pair
  shifts by the move. Only an angle step past `0x300` snaps, and an area
  module's load no longer re-primes: the snap and the re-prime held the view
  still for a tick mid-stride at every crossing ("Crossing between areas" in
  `DEVELOPMENT.md`).
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
  **A rotation handed alone** (`a0 = 0`, `a1 ≠ 0`) is handed too, as the block's
  eye with that rotation: only the item pickup does that, steering the view level
  as the item flies in. Before, `OnHanded` never ran for it and the override, the
  last world frame's, held the old pitch over the whole pickup (2026-10-08; "The
  item held up at the eye" in `docs/INPUT.md`).
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

## Unit 3, done: the things in the world carried between ticks

Written 2026-10-02 by four opencode agents in worktrees (the walk, the blender,
the carry's logic, a reading of what still steps) and merged, wired and measured
by the orchestrator. The plan this followed (3a-3e) is in this file's history.

### 3a. The model walk in C#

`patches/ModelWalk.cs`, `func_80040AE4` as a replace hook, one method per table,
one per submit (`SubmitWorld` `func_8003E34C`, `SubmitFront` `func_8003F304`,
`SubmitSky` `func_800400AC`). `KF3_MODELWALK`, **on** (`0` the recompiled routine,
`verify` the comparison).

- **Verify**: 0 mismatches (RAM, scratchpad, registers, GTE) standing, turning
  and walking from slot 1, and with five live creatures in view through the shell's
  `view 124000 -14400 85044 0 1024 0` (the creatures of `fdat02` wander 20-34k
  units west of the start, past its cull).
- `KF3_GEOPROBE=1`: call #13's line is identical on and off (311 packets, 2470
  words, the same slots, codes and sizes). Uncapped: about 1290-1330 fps
  recompiled, 1390-1450 with the walk and the blender in C#.
- **What each submit is handed** (the full table, with line numbers, was the
  agent's `scratch/u3a-notes.md`): a0 the record's mask byte, a1 the model, **a2 a
  pointer to the position**, **a3 a pointer to the rotation**, nine stack words.
  A creature's position is `func_8004EEE0(rec, sp+0x38)`'s result (which is
  `rec+0x2C` itself when `flags & 3 == 0`), or `rec+0x2C` with the fixed matrix
  `0x8007E4C4` and no rotation under flag `0x20`; its rotation is
  `rec+0x40/+0x42+0x800/+0x44` written to `0x1F800114`. An object's is `rec+0x14`
  and `rec+0x34..`; kind `0xF2` builds its position in `sp+0x38`. An effect's
  rotation is either the scratch lane or a pointer into its record (`rec+0x28`).
  **No table draws from another's copy**, except the sky record copied to
  `0x8018FAF8` and redrawn from there.
- **The billboard hold** is in the C# walk now: a walk that is not the tick's
  first neither steps the cels nor bumps the clock; `SpriteAnim`'s pre/post stands
  down while the walk is C#. The clock `0x80182964` reads 14.96 a second at 144 fps.
  (A rate census lists it, and `0x801AEB20`, at exactly 25% of idle pairs with the
  C# walk: the census samples at the vblank, which the walk polls inside its
  loops; the direct measurement is the one to trust.)

### 3b. Positions and rotations carried

`patches/ModelSmoothing.cs` holds the logic; the walk calls it at the submit
seams. **The record and the scratchpad lane keep the tick's values**: the carried
position and rotation are copies in the walk's own frame (`0xA8` bytes instead of
`0x90` while carrying; `+0x90` the `VECTOR`, `+0xA0` the `SVECTOR`), and a2/a3
point at them. Keyed by table and slot, with an identity per table (a creature's
model and definition, an object's id and kind, an effect's id and model, a
billboard's id), so a reused slot primes instead of sweeping. Sampled when
`FramePacing.Ticks` moved on a ticked iteration; a slot that missed a tick (culled)
primes; a step over 1536 units or `0x300` snaps; a value moved outside a tick
snaps; an overlay load forgets everything.

- **All four tables** go through the seam. The sky does not (it is drawn round the
  camera).
- **The queries still cull against the ticked position** (`func_80040694`,
  `func_80040708` are handed the record): a model crossing the cull's edge can
  appear or go one tick late. Not seen; not changed.
- `KF3_SMOOTH_PROBE=1` adds a line: records drawn a second by table, carried,
  snaps, clip calls, clip frames carried, wraps, turns, re-seeks, backward steps.

### 3c. The MO pose blender in C#

`patches/MoPose.cs`, `func_800431E8(slot, bank index, clip byte, clip time)`.
`KF3_MOPOSE`, **on**; verify 0 mismatches over the same runs, 165-195 calls a
second with creatures in view (75 standing at the start, every one a rigid bank,
which has no clip).

- **The clip clock is `func_80042CAC(bank, clip, time, &segment)`**, the weight
  written through the pointer at the blender's `sp+0x10` (no prologue, as
  Verdite2's `func_8003486C`), the segment record returned in v0. The clip record
  layout is Verdite2's: clip table at `bank + u32[bank+0x10]`, record at `bank +
  u32[table + clip*4]`, `u16` segment count, `u32` bank-relative segment offsets
  from `+4`, `u16[seg+2]` the duration, `u16[seg+0]` the direction flag
  (`0x1000 - weight` when set).
- **It re-morphs on every call** even when the clip and segment are unchanged (it
  skips only the keyframe rebuild), so a fractional time moves the mesh.
- `MoPose.ClipCarry`: where the blender hands the clock its time, a carried floor
  time goes in instead and the fraction is spent on the published weight
  (`frac * 4096 / duration`, negated for a reversed segment, clamped).

### 3d. The clip time carried

`ModelSmoothing.CarryClip`, Verdite2's `Mode.Timeline`: the clip time on a circle
whose length is the clip's own (summed from the segment table), the tick's step
unwrapped against the settled rate with the two reflection candidates, a changed
clip byte a cut, a step nothing explains a re-seek (hold and re-seed). Keyed by
the record whose submit is in progress (`Enter`/`Leave` round each submit).

- **Measured** (`KF3_FPS=144`, the `view` above, 24 s): a creature drawn on 144
  frames a second, its position carried on 144 and its clip time on 144; a wrap
  recognised about every 2 seconds; **0 re-seeks, 0 backward steps**. Its clip
  time advances about 85 a tick.
- **The first-person arm** (`func_8003DF50`, stage 15's `Site.Arm`) is carried
  too. It calls the blender `func_800431E8` with `a0 = 0x801B259C` (fixed),
  `a1 = 0x20`, `a2 = u8[0x801B25AE]` (the clip byte) and `a3 = s16[0x801B25A4]`
  (the clip time), and returns before drawing while that s16 is -1. The s16 is
  **the swing clock**, not a missing weapon: it is -1 between swings and steps
  0x180 a tick, 0x0180 to 0x0F80, over about 0.7 s when a Square press starts a
  swing on slot 1 (`fdat02`); `func_8002D2A0` (stage 4) steps it, `func_8002BDC0`
  writes -1 and `func_8002C040` writes 0. Triangle does not swing, and a recovery
  gate means 20 presses 1.2 s apart gave 6 swings. The arm's placement record
  (`*0x801B2594`, `+0x34..+0x3F`) did not change during a swing: the whole swing
  is the MO clip, as in Verdite2.
- **The change**: `ModelSmoothing` gained one clip slot for the arm (`ArmTable` 4,
  index `Total`; clip state only, never the position arrays), and stage 15 calls
  `ModelSmoothing.EnterArm()` before `Site.Arm` and `Leave()` after, so the carry
  sees the arm's clip like a creature's. Verdite2 needed an idle latch to reset the
  slot between swings; Verdite3 does not, because `CarryClip` primes a slot that
  missed a tick and the arm never reaches the blender while idle.
- **Measured** (`KF3_FPS=144`; `KF3_SMOOTH_PROBE=1` prints
  `[KF3] model smoothing: arm N clip call(s)/s, M carried` in a second the arm
  drew): one swing drew the arm on 93 frames and carried the clip time on 87 (the
  first tick primes and draws the game's own time); over six swings, 0 re-seeks and
  0 backward steps. The mechanism is measured; the picture has not been judged by
  eye.

### 3e. The HUD, and what else steps

- **The compass needle** is drawn between its two tick samples while the view is
  carried, written for the HUD's call and put back after it (`Stage15.CarryNeedle`).
  Turning at 144 fps: a new needle on about 141 of 144 frames, 0 snaps. Its target
  is the camera block's yaw `0x801AEC5E`, which is the drawn camera's under the
  override.
- **The gauges**: `Gauge1`/`Gauge2` read HP, MP and two eased followers,
  `0x801B2502` and `0x801B2506`, which stage 4 steps (`func_8002D2A0`,
  `func_8002FE1C`), so the bars stepped 15 times a second. The followers are now
  interpolated for the gauges' build and put back (`Stage15.CarryFollowers`): the
  bars filling on arrival draw a new length on all 144 frames a second.
- **The scrolling textures**, stage 15's call #2 `func_800351FC`: two records of
  `0x18` at `0x801AEB1C`, each a countdown (`+2`, reloaded from `+1`), a phase
  (`+4`, stepped by `+3`, wrapped at the image's height `+0x16`) and a source
  rectangle (`+0x10`) copied into VRAM at `(+8, +0xA + phase)` with two
  `MoveImage`s (`func_80079E90`). It ran every drawn frame, so the textures
  scrolled at the drawn rate. **`patches/TextureScroll.cs` runs it on the first
  stage 15 of a tick only** (`KF3_TEXSCROLL=0` to compare): 15 runs a second at
  144 fps. `KF3_TEXSCROLL=carry` redraws at the interpolated phase too, about two
  uploads a second here: the step is a pixel or so a tick, so there is little to
  carry.
- **Loops that draw their own frames**: the earlier reading here (that none
  needed Verdite2's `LoopPacing`) was wrong. A dozen routines call stage 15
  themselves, and the bottom message box is stepped by stage 15's own call #3;
  see "Loops that draw their own frames" and "The message box at the bottom"
  below.

**Judged by eye** (the user, 2026-10-02, at 144 fps): the creatures carried,
"looks good"; on whenever pacing is since.

## Loops that draw their own frames

`patches/LoopPacing.cs`, **on by default** (`KF3_LOOPPACING=0` to compare). A
routine entered from a gated stage that calls stage 15 itself presents its own
frames, and the stage gate cannot reach inside it: the world does not tick there,
so its body steps once per drawn frame and every counter it advances runs at the
render rate. The patch hooks stage 15's pre and post. The main loop's own `jal`
leaves `0x80014FB0` in RA; any other caller is such a loop. A modal call whose
frame the world did not tick on is followed by redraws of stage 15 with the loop's
own `a0`/`a1`, and each redraw passes FramePacing's frame boundary, advances the
logic clock and is paced to `KF3_FPS`; the loop's body therefore waits for the
next world tick, about 15 times a second, while the picture is drawn at the render
rate and `ViewSmoothing` and `ModelSmoothing` carry between ticks. The register
file is snapshotted and restored around the redraws, and a flag makes the nested
calls ignore the pre and post, so the loop resumes with exactly the registers
stage 15 left it. A backstop of three ticks' wall time (200 ms) and a check that
the frame boundary advanced each redraw warn once to stderr if either fails. The
first version capped the count instead (three ticks' worth at the rate in play,
never fewer than 64); uncapped, with no rate measured yet at the first fade, 64
redraws at ~1000 fps fell short of a tick and the warning fired.

The routines, read off the recompiled `generated/*.cs` (only the fade confirmed
by a run): the **item pickup**
`func_8005DB30` (spin `s16[slot+0x26] += 0x40` a body, stage 15 called
`(0, 0x801B2608)`), a **slide-in popup** `func_8005D948` (to `S2 = 4096` by
`0x200`, `(0, 0)`; called from the action interpreter `func_8005E2D0`), the **fades** `func_80046C00` and `func_8005DA94` (their
own stack camera blocks), the **message/script interpreter** `func_8005C308`,
the **item-use dispatcher** `func_8005CBE0`, the **area-module fades** (`fdat08`
`func_801E8C3C` and the sibling fdat loops), and the one-shot redraw
`func_80030568` (a single frame, so it has nothing to hold). `func_8005C0D4` also calls stage 15 in current generated source (inventory
refreshed 2026-10-04); the previous exclusion was stale. Its loop/context needs
separate exercise in the GPU coverage programme.

**Left out of Verdite2's `LoopPacing`**, deliberately, it is much more than this
port needs: its carry of a camera a loop pans itself (three angles at `a1` and a
position at `a0`, interpolated between the loop's last two iterations), its
pace-only mode (`LoopPacing=pace`: hold the body, do not redraw, so the speed is
right and the picture steps at the tick), and its interface-frame pacing
(`InterfaceHz`, 60) for a modal loop that draws no world -- that last belongs to a
separate `VBlankPacing` patch here ("Menus and loading screens wait for a vblank"
in `docs/DEVELOPMENT.md`).

**Measured 2026-10-02**, slot 1 in `fdat02`, `KF3_LOOPPACING_PROBE=1`: the fade-in
on arriving (`func_80046C00`, RA `0x80046C84`, its own stack blocks
`0x801FFF50`/`0x801FFF60`) ran **15-16 iterations a second with 7.9-8.6 redraws
each at `KF3_FPS=144`** (144.0 fps drawn, 15.0-15.9 ticks/s), and 18.8 a second
with 49 redraws each uncapped (~935 fps); before, its body ran once per drawn
frame. Opening the menu passes `func_80030568` (RA `0x800305A8`). The popup was
not reached by a scripted run. The item pickup was on 2026-10-07
(`KF3_ITEMTURN_TEST`, "Turning a picked-up item" in `docs/INPUT.md`): its hold
(RA `0x8005DFE8`) ran 15 passes a second with 10 redraws each at 165 fps. **Not
judged by eye**: the pickup's spin, the popup and the fades.

## The message box at the bottom

`patches/MessageBoxHold.cs`, **on by default** (`KF3_MSGBOX=0` to compare). The
box a coin pickup shows at the bottom of the screen is a state machine in
`func_80041F9C`, **stage 15's call #3**, so it ran on every drawn frame. Read off
the recompiled code: a request is queued by `func_80041EEC(kind, amount)` (kind
`0x19` carries an amount) into the kind table `0x801AEAED` behind the cursor
`0x801AEAF6`; the state at `0x801AEAF7` goes 0 (idle) -> 1 (slide in: the level
`0x801AEAF9` `+0x14` a call to `0x64`) -> 2 (hold: `0x801AEAF8` from `0x0F` down
by 1) -> 3 (slide out, `-0x14` to 0). It writes only the HUD records stage 15's
overlay walks (calls #10 and #11, `func_80041E68` and `func_80041D9C`, through
`func_80041AD4`) draw; its only callees are `func_800277C0` and `func_80041F8C`,
neither of which writes the table. So the patch skips it on every stage 15 but the
first of a tick (`FramePacing.FirstWalkOfTick`), and the records it wrote last are
drawn again: in 5 ticks, held 15, out in 5, about 1.7 s, as the console's 15 Hz
gate gave it. The slide steps at 15 Hz; nothing is carried between ticks.
**Not measured by a run (no scripted pickup) and not judged.**


## The tints between ticks

Verdite2's tints strobed above the tick rate: its stage 1 reset the screen-tint
request every drawn frame while the stages that ask for a tint ran only on a
tick, so a death fade or a damage flash showed one frame in seven (its
`TintHold`). Asked of this game on 2026-10-06 with `patches/TintProbe.cs`
(`KF3_TINTPROBE=1`): per drawn frame, whether a tint was drawn (flat,
semi-transparent, the clip rectangle's full width and at least half its height,
the shape `Widescreen.Stretch` widens), and whether a tick built the frame.

Measured, slot 1 (`fdat17`), `KF3_FPS=144`, the shell's `hurt 5`, `hurt 5`,
`hurt 20` four seconds apart, then `kill`:

- **Each damage flash is drawn on every frame for its whole length**, ticked or
  not: 88 tinted frames, 10 of them ticked and 78 idle, for each `hurt 5`; the
  strip reads `TtttttttttTttttttttt…` (`T` a tinted tick frame, `t` a tinted
  idle one). `hurt 20` ran three ticks longer.
- The death fade and the reload's fade-in are tinted on every frame too.

**No strobe, and nothing to port.** This port gates stages 1-14 together, so the
reset and the requests always agree, and the tint is drawn by stage 15 from what
they left. The flash steps at the tick rate, as on the console. **To judge by
eye**: nothing new; the flash and the fades are as they were.

## Handoff: what is left

**The next work is `docs/PICTURE.md`** (24-bit colour, perspective, sub-pixel,
the Z-buffer), planned 2026-10-02.

**Where it stands.** Verdite3 `main`. Units 1-3 are built and judged. **The user
said the rest is not important yet (2026-10-02)**; it is kept here for when it is:

1. The needle, the gauges and the texture hold are on and were not looked at
   separately.
2. If a model pops at the cull's edge, hand the queries the carried position.
3. The arm is carried and measured (3d); waiting to be judged by eye (swing with
   Square).
4. Check the loops' reading in 3e against the code before trusting it.

**Running the game**: as before (the DualSense, `carda.sav`); the shell port is
`27903`. `view x y z pitch yaw roll` with `KF3_SMOOTH_PROBE=1` is how a creature is
put on screen without walking to it.

**Deferred**: the near path and the Z-buffer (`docs/GEOMETRY.md`), and the shared
fill's extraction (Verdite2's `docs/SHARING.md`).
