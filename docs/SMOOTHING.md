# Smoothing: the picture carried between ticks

The plan for drawing frames between the world's ticks that actually move, and
its work as it is done. Frame pacing itself is "Frame pacing" in
`docs/DEVELOPMENT.md`; the routines named here are "The geometry path" in
`docs/GAME_INTERNALS.md`. Verdite2's smoothers (`FrameSmoothing`,
`ObjectSmoothing`, `AnimSmoothing`, `FluidSmoothing`, `LoopPacing`, `SpriteAnim`)
and their write-ups in its `docs/PATCHES_AND_MODS.md` are the background.

## Status

**Plan only; nothing built** (2026-10-02). Chosen by the user as the next work,
ahead of the near path in `docs/GEOMETRY.md`, because it is what a player sees
first. The first unit is stage 15 and the camera block in C#, below.

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

## Open checks (answer them in unit 1)

- Which of stages 1-14 fills the main loop's `sp + 0x18`/`sp + 0x28`, from which
  player fields, and whether head bob or a landing offset is added there (as
  Verdite2's stage 8 adds both).
- Whether anything inside stage 15 reads the player's own position or rotation
  rather than the camera block's copies: Verdite2's arm and object walk did. The
  arm `func_8003DF50` is lit from the player's own tile; the walks' queries
  (`func_80040694`, `func_80040708`) are to be read.
- What `func_80030568` and `func_800305D8` are, and whether they run inside
  pacing's frames.
- Whether the sound listener reads the camera (Verdite2's stage 9 does), which
  would then hear the carried position or the ticked one.

## Handoff: the next session

**Where it stands.** Verdite3 `main`, local commits only (none pushed): the bulk
assemblers are C# and on (`KF3_POLYASM`, `docs/GEOMETRY.md`); frame pacing is
built and off until judged. Only one save exists (card A slot 1,
`KF3_AUTOSTART=1`); keep a copy of `carda.sav` before driving menus, since Cross
saves over the slot.

**The work: unit 1 above**, stage 15 and the camera block in C# with verify and a
view override. Read first: this file; "Stage 15's calls, measured", "The camera
block" and "The frame's tables and buffers" in `docs/GAME_INTERNALS.md`;
"Frame pacing" in `docs/DEVELOPMENT.md`; `patches/FramePacing.cs`,
`patches/PolyAssembler.cs` (the verify harness to generalise). In Verdite2
(`~/Desktop/KFII-PC`): `patches/Stage13.cs`, `patches/CameraBlock.cs`, and
"Stage 13 in C#", "Drawing the frame from another camera" and "The view has to be
carried between ticks" in its `docs/PATCHES_AND_MODS.md`. Copy conventions and
techniques from Verdite2, never its addresses.

**Then unit 2**, the first one the user looks at. Ask before starting it: it
needs the user's eyes, and pacing itself is still unjudged.

**Deferred by this plan**: the near path and the Z-buffer (`docs/GEOMETRY.md`,
"After this unit"), and the shared fill's extraction (Verdite2's
`docs/SHARING.md`). Nothing waits on them.

**Don't**: push anything without asking; let an opencode agent share a checkout
being edited (inputs for a read-only agent go in a scratch directory of their
own); run more than about five subagents at once.
