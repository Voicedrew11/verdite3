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
with the view; **the creatures, objects, effects and billboards and their clip
times are carried by `KF3_SMOOTH_MODELS=1`, measured and not yet judged by eye,
so off.** The handoff for what is left is at the end of this file.

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
- The first-person arm (`func_8003DF50`) is not carried: it returns at once on
  slot 1, so there was nothing to measure.

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
- **Loops that draw their own frames**: an opencode reading says
  `func_800305D8` (from stage 4) loops over stages 14 and 13 with two VSyncs a
  pass and `func_80030568` is a one-shot redraw, neither through the frame gate,
  so neither needs Verdite2's `LoopPacing`. **Not checked against the code or run.**
  While one runs the world does not tick, so the tick-held clocks above wait for
  the stage gate's watchdog.

**Judged by eye**: nothing in unit 3 yet.

## Handoff: what is left

**Where it stands.** Verdite3 `main`, local commits only (none pushed). Units 1-3
are built; the camera carry is judged; the model carry is measured and off.

1. **The user judges `KF3_SMOOTH_MODELS=1`** at 144 fps: creatures walking and
   looping, doors turning, effects. If it looks right, make it on whenever pacing
   is (as `KF3_SMOOTH` went) and say so here. The needle, the gauges and the
   texture hold are on now and also want a look.
2. If a model pops at the cull's edge, hand the queries the carried position.
3. The arm, when a save with one is available.
4. Check the loops' reading in 3e against the code before trusting it.

**Running the game**: as before (the DualSense, `carda.sav`); the shell port is
`27903`. `view x y z pitch yaw roll` with `KF3_SMOOTH_PROBE=1` is how a creature is
put on screen without walking to it.

**Deferred**: the near path and the Z-buffer (`docs/GEOMETRY.md`), and the shared
fill's extraction (Verdite2's `docs/SHARING.md`).
