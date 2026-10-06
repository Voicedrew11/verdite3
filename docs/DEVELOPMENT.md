# Development: build, run and diagnose

How to build the recompiler, recompile the game into `generated/`, build and run
`KingsField3`, and read a run's logs. Verdite2's `docs/DEVELOPMENT.md` is the
model, with its `KF2_*` switches becoming `KF3_*` here.

## Status

Boots, plays, changes areas, saves and loads (2026-10-02). The port's first
patches are the agent harness and frame pacing (below). Without pacing the game's
own gate holds the world to 15 (it ran at 30 until the fork's vblank fix
`2013e51`): see "The session and the main loop" in
`docs/GAME_INTERNALS.md`.

## Build and run

```bash
bash scripts/setup_tools.sh        # build the recompiler
dotnet run --project tools/RecompOne/RecompOne.Recompiler -c Release --no-build -- config/kf3.json
dotnet build KingsField3Recomp.csproj -c Release
dotnet bin/Release/net10.0/KingsField3.dll disc/KingsField3.cue
```

The recompile reports `applied 63 patches, 0 reimplementations` and 2501
functions. Anything else from the first line is a name that started binding (see
"The SDK entry points" in `docs/RECOMPILATION.md`). `setup_tools.sh --signatures`
fetches the PSY-Q bank `--autoconfigure` reads; `--pull-fork`/`--push-fork` and
`--pull-core`/`--push-core` move the two subtrees.

Run it from the repository root: the runtime writes `carda.sav`, `cardb.sav`,
`settings.json` and `interface.ini` into the working directory (all gitignored).
**The disc comes from `settings.json`'s `CdPath`, not the command line**: in a
directory with no `settings.json` (a fresh git worktree) the runtime opens its
disc picker and waits for a person, cue argument or not. Copy `settings.json` and
the two cards into a worktree before running there.

## Diagnostics

`KF3_LOG=bios,cd,gpu,dma,sdk,spu,mdec,irq` (or `all`) turns on the runtime's log
channels. Every run prints each overlay as it loads, which is how a run is read
without looking at it:

```
[Dispatcher] loaded overlay: open
[KF3] irq callback table: open 0x8003E948
[Dispatcher] overlay open overwritten by game
[Dispatcher] loaded overlay: game
[KF3] irq callback table: game 0x8009AF9C
[Dispatcher] loaded overlay: fdat02
[Dispatcher] overlay fdat02 overwritten by fdat14
```

**For a hang, take the managed stack** of the live process, as in Verdite2:
recompiled functions carry their MIPS address in their name.

```bash
~/.dotnet/tools/dotnet-stack report -p $(pgrep -a dotnet | grep KingsField3.dll | awk '{print $1}')
```

The intro movie looks like a hang in one: the main thread sits in
`LibCdStream.StGetNext` under `func_80013EBC` while the movie plays.

The runtime's own diagnostics are still read under Verdite2's names (`KF2_CDTRACE`,
`KF2_GLDEBUG`, `KF2_GTE_FAST`, `KF2_GTE_LIGHTCACHE`, `KF2_RAM_PROBE`, `KF2_SWAP`,
`KF2_VRAMCHECK`): the fork reads them by name. See `docs/TODO.md`.

## The acceptance test

What a change to the fork, the config or the maps must keep passing. Measured
2026-10-02 against fork `a617cf8` and Verdite Core `a6c2434`; the steps marked
**by eye** were the user's.

1. The recompile reports `applied 63 patches, 0 reimplementations`.
2. Boot from the cue. The log shows `open`, its table `0x8003E948`, then the
   intro movie; skip it to the title (**by eye**).
3. Past the memory card screen a short video plays and the game starts: the log
   shows `game`, its table `0x8009AF9C`, and `fdat02` (**by eye**: an area,
   playable).
4. Walk into the next area: `fdat02 overwritten by fdat14`, and the music
   changes (**by eye**).
5. Save. `carda.sav` gains directory entry `BASLUS-002551`, 3 blocks (24576
   bytes), its title block reading `KING'S FIELD 2-1 EXP 0 LV 1` for a new game.
   A check:

   ```bash
   python3 -c "d=open('carda.sav','rb').read();print([d[i*128+10:i*128+30].split(b'\0')[0] for i in range(1,16) if d[i*128]==0x51])"
   ```
6. Load that save in game and from the title screen (**by eye**). The title-screen
   load shows `game overwritten by open`, then `open overwritten by game`.
7. No `unmapped call` anywhere in the log.

Steps 1-3 and 7 now run as a program, with the beacon reading the result:

```bash
KF3_AUTOSTART=1 KF3_AGENT=1 KF3_SHELL=1 KF3_FPS=144 KF3_FPS_PROBE=1 \
    dotnet bin/Release/net10.0/KingsField3.dll disc/KingsField3.cue
# [KF3] pacing: 144 fps, boundary 3/3 DrawOTag + 3/3 VSync, 14/14 stage(s), frame gate skipped, world at 15 Hz
# [KF3] autostart: loaded slot 1
# [KF3] autostart: in fdat02, area 0, HP 50/50, LV 1, slot 1
# [KF3] pacing: 144.0 fps drawn of 144, 144.0 VSync call(s)/s, 15.0 tick(s)/s of 15 Hz, ...
```

Changing areas, saving, and the title-screen load still need a person.

## Driving the game without a person

Four switches, all off unless set (see `docs/ENV_VARS.md`). They are Verdite2's
harness, rebuilt on this game's addresses.

**A connected DualSense stalls `KF3_AUTOSTART`** (2026-10-02): the boot sits in
OPEN.EXE's pad loop (`func_800136D8`, the managed stack in `BiosB.PadRead`) and
never reaches GAME.EXE, with or without pacing; the cause is not read. Hide the
pad from SDL for a scripted run:
`SDL_GAMECONTROLLER_IGNORE_DEVICES=0x054C/0x0CE6`.

- **`KF3_AGENT=1`**, the beacon (`patches/AgentBeacon.cs`): `[KF3-AGENT] overlay
  <name>` on each load, and once a second
  `{"overlay":…,"inGame":…,"loop":…,"hp":…,"maxHp":…,"mp":…,"maxMp":…,"level":…,"exp":…,"area":…,"slot":…,"pos":[x,y,z],"yaw":…}`.
  `inGame` is an area module up and a non-zero max HP; **`loop` is whether the
  main loop ran in the last second**, which is false during the New Game's
  opening movie, a menu's own loop and a load. The fields are "The player" in
  `docs/GAME_INTERNALS.md`.
- **`KF3_SHELL=1`** (or a port), the command channel (`patches/AgentServer.cs`):
  TCP `127.0.0.1:27903` (Verdite2 uses 27900, so both can run), one request a
  line, one JSON line back: `state`, `press <button> [ms]`, `peek <hex addr>
  [bytes]`, `poke <hex addr> <hex bytes>` (up to 64), `dump <file>` (the 2 MB
  of RAM, for diffing), `vram <file>` (the 1024 × 512 16-bit VRAM shadow, row by
  row: textures and fonts without a screenshot), `settings [open|step <key>
  <-1|1>|reset <key>|save|discard]` (the game menu's settings session without its
  page, `docs/SETTINGS.md`), `gpu` (the retained renderer's cumulative draw and
  model-mask counters), `kill` (the player through the game's death latch, for
  auto reload: wait until the beacon's `loop` has been true for a few seconds
  first), `hurt <amount>` (the player through the take-damage routine
  `func_8002A6F4`, as a blow: the damage flash, the knockback, and the death
  latch at HP 0), `scale [1..8]` (the render scale, taken at the next present,
  unsaved: fork `0097`), `help`. Everything runs
  from the vblank on the game thread. There is no `load`; `warp` needs
  `KF3_SCENE_DRIVER=1`.
- **`KF3_AUTOSTART=<1..15>|new`** (`patches/AutoStart.cs`): Start is pulsed
  through OPEN.EXE; GAME.EXE's start menu is told the title chose Load (the byte
  `0x800102FA`) and the slot chooser is replaced by the game's own card loader
  on that slot. No input is needed in GAME.EXE. `new` sets the byte to 0 instead;
  the opening movie then plays for about 30 s before the loop runs. Measured:
  slot 1 lands in `fdat02` at HP 50/50, LV 1, the save's position and heading.
- **`KF3_AUTOPAD=seconds:button:holdMs,…`** (`Program.cs`): scripted pad input
  through `PAD_dr`, its clock started by the first area module load.

All input goes through `PAD_dr` (a `PadReadEvent` listener: the buffer is
active-low with its two bytes swapped against `Controller`'s layout), which
reaches the menus.

**Cross opens the in-game menu, and a few more Crosses save over the slot.** A
scripted run that pressed Cross to "get past" something rewrote card A's slot 1
on 2026-10-02 (it had held the same new-game save, so nothing was lost). Circle
closes the menu. Keep a copy of `carda.sav` before driving the menus.

Diagnostics written for this: `KF3_STAGEPROBE=1` (which main-loop stages write
the ordering table) and `KF3_RATECENSUS=<seconds>` (which words change on frames
no stage ran on); their readings are in `docs/GAME_INTERNALS.md`.

`KF3_GEOPROBE=1` (`patches/GeometryProbe.cs`) goes one level down: it walks the
ordering table and the front table before and after each of stage 15's calls and
prints, every 5 seconds, the packets each call added (by GPU command, size, slot
range and address range). `KF3_GEOPROBE_FUNCS=8003E34C,80035CA4,...` adds any
function, reported per call site, which is how a packet is traced to the
assembler that wrote it. It walks the whole table at every hooked entry and exit,
so it is slow with many functions; the readings are in "The geometry path" in
`docs/GAME_INTERNALS.md`.

**A run stopped by `timeout` loses its last few kilobytes of console output**:
stdout is block-buffered when redirected, and SIGTERM ends the process before the
buffer flushes. A probe that prints rarely can lose every window but the first
(the first `KF3_MAPCOVERAGE` reading, taken during the load, read as if most rows
were 0). A probe should `Console.Out.Flush()` after each report.

The survey's run, from the repository root:

```bash
KF3_AUTOSTART=1 KF3_GEOPROBE=1 KF3_AUTOPAD=12:Left:3000,20:Up:6000 \
KF3_GEOPROBE_FUNCS=8003F304,800400AC,800366A8,80037BEC,80074D88,80075188,800756A8,80075B48,8003AB04,80039D50,80035CA4 \
    timeout 50 dotnet bin/Release/net10.0/KingsField3.dll disc/KingsField3.cue | grep geoprobe
```

`KF3_GEOPROBE=time` times each hooked call instead of walking the tables. Its
hooks are not free: 793 fps uncapped without it read 600 with it, so subtract
about 0.4 ms a frame spread over the hooked calls.

Two readings are the probe's, not the game's: call #5 shows one 4-word packet at
`0x8009C1B0` "added" at slot 0 (a fixed node the cleared table ends in), and call
#20 (the swap) shows about 600 "removed", because once the front table is linked
in at slot 8190 the walk of the main table stops there.

## The Testing tab

Settings ▸ **Testing** (`patches/TestingSection.cs`, added 2026-10-02) holds every
switch the port has, live, so a change can be compared without a restart:

- **Frame pacing** on or off, the frame rate (30-360, or uncapped), the live tick
  rate (5-60 Hz, with a reset to the original 15 Hz), and a readout of the frames
  drawn and ticks taken a second. The tick rate is dimmed while pacing is off;
  changing it changes gameplay speed.
- **Smoothing**: the camera (with the compass needle and the gauges), creatures
  and objects (not judged), the needle's and the billboards' tick holds, and the
  scrolling textures (every frame, held, carried). Dimmed while pacing is off.
- **Routines in C#**: stage 15, the camera block, the polygon assemblers, the
  model walk and the MO pose blender, each recompiled, C# or verify.
- The pacing and smoothing **console probes**.

To make that possible, **every one of those patches now attaches in every state**
and checks its switch on each call: pacing passes the stages, the frame gate and
the boundary straight through while it is off, and a routine set to recompiled
calls the original. Measured: with nothing set, the model walk runs 14.94 times a
second (the game's own rate, the frame gate in place); `KF3_FPS=144` still reads
144.0 fps at 15.0 ticks/s.

**Kept**: the on/off choices, the frame rate, the tick rate (`kf3.tickrate`) and
the textures' mode go in
`interface.ini` as `kf3.*` and come back at the next boot, applied on
`RuntimeReadyEvent` (the config loads after `Program.cs`). **A `KF3_*` variable
that is set wins** over a kept value. Checked: `kf3.pacing=1`, `kf3.fps=120` and
`kf3.smooth_models=1` with no variables boot to 120.0 fps at 14.9 ticks/s with a
creature carried on 120 of 120 frames. The routines' modes are not kept: verify
is a comparison for one session. Pacing turned on from the tab with no rate
chosen aims for 144.

## Frame pacing

`patches/FramePacing.cs`, **off unless `KF3_FPS` is set**, because the picture
has not been judged. Verdite2's mechanism ("Any frame rate" in its
`docs/PATCHES_AND_MODS.md`) on this game's loop:

- The frame gate `func_80019614` is skipped (its count zeroed), in GAME.EXE only.
- **The frame boundary** is the `DrawOTag` after a `VSync` call (the three
  `DrawOTag` and `VSync` entry points `config/kf3.json` binds); the frame is
  paced there to `KF3_FPS`, and the world clock advanced by wall time.
- **Stages 1-14 run only on a tick of the 15 Hz world clock**, decided once per
  loop iteration at stage 1, so the fourteen agree even when the menu inside
  stage 4 presents frames between them. Only calls from the main loop's own call
  sites are gated (the card loader's wait loop calls stages 8, 13 and 14). Stage
  15 runs every frame.
- **A watchdog**: no boundary for 500 ms and stage 1 ticks the world off the wall
  clock and paces the loop itself. `KF3_PACING_NOBOUNDARY=1` removes the boundary
  to test it.
- OPEN.EXE and END.EXE keep the runtime's 60 Hz throttle and their own waits.

**15, not 30.** The gate's literal is 4 vblanks; the 30 the port ran at was the
fork delivering the vblank event twice, fixed in `2013e51`. The table was
measured before that fix; after it, pacing off reads 15 and 600 a second
(the game's own gate), and 144 reads 144.0 fps, 14.9-15.0 ticks/s, 600 a second.

Measured 2026-10-02, slot 1 in `fdat02`, standing, `KF3_FPS_PROBE=1`, and yaw
turned by holding Left for 1 s (three times each):

| `KF3_FPS` | drawn | world ticks/s | yaw per s | packets a frame, ticked / idle |
|---|---|---|---|---|
| unset (pacing off) | 30 | 30 | 1200 | — |
| 15 | 15.0 | 15.0 | 600 | 418 / — |
| 60 | 60.0 | 15.0 | 600 | 733 / 744 (turning) |
| 144 | 144.0 | 15.0 | 600 | 418 / 418 |
| off (uncapped) | 1225.5 | 15.0 | 600 | 418 / 418 |
| 144, boundary removed | — | 14.7-14.8 (watchdog) | 600 | — |

Equal packet counts on ticked and idle frames say no skipped stage feeds the
picture. **Not judged by eye: the picture at any rate.** Expected, and not
fixed: the picture only changes 15 times a second (nothing is carried between
ticks), and whatever stage 15 itself advances runs at the render rate (see "What
still runs at the render rate" in `docs/GAME_INTERNALS.md`: billboard cels at
least). Menus, the opening movie and loads present their own frames and are
paced to `KF3_FPS` like any frame.

## Menus and loading screens wait for a vblank

`patches/VBlankPacing.cs`, **on by default** (`KF3_VBLANKPACING=0` compares).
**The rule: in GAME.EXE, with frame pacing on, a VSync call that is not inside
stage 15 waits a real vblank.** Mode 0 waits one, mode `n >= 2` waits `n`; mode
`< 0` and mode 1 are queries and return at once.

**Why.** The runtime's `LibEtc.VSync` presents and returns immediately for any
mode but 1 (`tools/RecompOne/RecompOne.Runtime/sdk/LibEtc.cs`); it is throttled
only by the runtime's per-call ceiling, which `FramePacing` sets permissively to
`max(60, 2*TargetFps)` = 288/s at 144 fps. On the console each `VSync(0)` waited
one vblank and `VSync(n)` waited `n` vblanks. So every loop that counts VSync
calls as a delay ran several times too fast once the host rate rose above the
runtime's ceiling -- the same trap the fork's own comment names, that a caller
which needs a rate must keep its own deadline.

The class keeps a 60 Hz wall-clock grid of its own (a `Stopwatch`; the runtime's
`_vcount` cannot be the clock, since it only advances from a VSync call). Its
deadline is `next += n * 1000/60`, and a deadline more than four periods behind
resyncs instead of fast-forwarding. It sleeps to ~1.5 ms before the deadline and
spins the rest, as `FramePacing.Floor` does. Calls made inside stage 15
(`func_800422B8`) are exempt: a pre at the lowest order and a post at the highest
keep a depth counter around it, so the frame swap's VSync stays `FramePacing`'s
frame boundary; the counter resets on every overlay load in case an exception
skipped a post.

**What it covers**, read from the recompiled code: the in-game menu presenter `func_800270F8` (two
presents per loop iteration), the cursor auto-repeat `func_800279D8` (up to 8x
`VSync(0)` while a direction is held), the highlight and window-slide counters
`func_80026FE4`, the area-transition loading screen `func_8003DAEC`, the
full-screen message's fade `func_80043BB8` (both presenting through the frame swap
`func_80035700`; the fade was misread here as an area-load bar, see "Menus and
messages draw the world live" in `docs/WIDESCREEN.md`), the
movie presenter and its loop, the two-vblank wait `func_80019538`, and the
`VSync(2)`/`VSync(4)` waits.

`KF3_VBLANKPACING_PROBE=1` prints once a second, only when any wait happened in
that second: held VSync calls per second by mode, the mean wait in ms, and calls
exempted inside stage 15 per second.

**Measured 2026-10-02**, slot 1 in `fdat02`, the menu opened with Cross and
Down held for 3 s, `KF3_VBLANKPACING_PROBE=1`: **60.0 held `VSync(0)` calls a
second** in the menu at both `KF3_FPS=144` and uncapped (the pacing probe reads
60.0 fps drawn there, 15.0 ticks/s), mean wait 15-16 ms; before, the menu
presented at the runtime's ceiling, `2 x KF3_FPS` (288 a second at 144). The
main loop is untouched (144.0 fps, 15.0 ticks/s, 0 held). The loads before the
area held 25-39 a second. **Not judged by eye**: the cursor repeat (8 vblanks,
133 ms), the highlight and window slide.

## Profiling a frame

Verdite2's frame profiler, ported 2026-10-06: `patches/FrameProfiler.cs`,
`patches/ProfilerPanel.cs`, `patches/GpuFrames.cs` and the runtime's
`Diagnostics/Profiler.cs` and `Diagnostics/GpuTimes.cs` (fork `0045`, `0084`). It says
where a frame's time went, by section, on the game thread, and the GPU's time by
pass. **Shift+P** opens the panel, and recording runs while it is open;
`KF3_PROFILE=1` records from boot and prints a summary every five seconds, and
`KF3_PROFILE_OUT=profile.csv` writes every frame for `scripts/profile_report.py`:

```bash
KF3_AUTOSTART=1 KF3_FPS=144 KF3_PROFILE=1 KF3_PROFILE_OUT=profile.csv \
    dotnet bin/Release/net10.0/KingsField3.dll disc/KingsField3.cue
python3 scripts/profile_report.py profile.csv --skip 30      # drop boot and first-hit JIT
```

**What is a section without asking.** Every function `HookManager` has detoured is
timed inside `Invoke`: the recompiled body as `func_XXXXXXXX@overlay` and every
pre, post and replace delegate on its own (`replace Stage15.Replace`, `post
GpuWorld.Begin`). With this port's patches that already covers all fifteen
main-loop stages and most of stage 15's calls. The runtime adds `LibEtc.VSync`,
`Runtime.PresentFrame`, the window's event pump, the picture compose, the
interface, `GlCore.Flush` and `LibGpu.DrawOTag`'s packet walk. Whatever nothing
claims is **game code (no section)**. Known addresses carry a label (`stage 4: the
player's tick (func_80030FCC@game)`, `map tile walk (func_8003BFD0@game)`), from
the stage table and stage 15's calls in `GAME_INTERNALS.md`, the menu's presenter,
and `DrawOTag` and `VSync` in each executable. ``replace Slot`1.Replace`` and ``pre
Site`1.Pre`` are Verdite Core's `HookAttach` slots, under their generic names.

**Self and inclusive.** Self is what a section did itself, excluding the sections
it called, so a frame's self times sum to its length. Inclusive adds the children:
stage 15's is nearly the whole frame, because the swap, the frame cap and the
present happen inside it.

**Work, swap and wait are three different things.** Each section is in a group:
game, hook, runtime, **swap** (the thread blocked on the driver in `SwapBuffers`)
or **wait** (a sleep to a deadline: `FramePacing.Floor`, `VBlankPacing`,
`FrameClock.Throttle`, `WaitVBlanks`). A frame capped at 144 fps is 6.94 ms whatever
it did, so the figure to chase is **work**, the frame less its waits and its swap.
The panel hides the waits unless *Show waits* is on.

**The frame boundary is the end of `Runtime.PresentFrame`**, not a hook. A section
still open there (a stage running a modal loop that presents its own frames) is
split: the time so far goes to the frame that ended, and the rest to the next.

**To see inside the game's own time, time more functions.** An empty pre-hook
makes any recompiled function a section: *Time the 15 stages* and *Time function*
in the panel, or `KF3_PROFILE_FUNCS=stages` / `game:80030FCC+8003BFD0`. Each is a
detour for the rest of the session. Stages 1-14 are already hooked, so `stages`
adds only stage 15's pre-hook.

**The GPU** (`0084`): while recording, the GL backend puts a `GL_TIME_ELAPSED`
query around every batch submit and each present pass and reads them back once
the GPU has finished, about three frames later; `GpuFrames` charges each to the
frame that issued it. The passes are `scene`, `capture`, `ao`, `reflections`,
`composite` and `world` (the retained renderer). Not counted: VRAM uploads,
writebacks, the interface and the swap. In the panel: a `GPU` line under the
header, a strip under the frame bars at their scale, and the passes as table rows
(the selector beside the filter: CPU + GPU, CPU, GPU). In the CSV: `gpu.*` rows.

**The spikes**: each frame also carries GC pause time, collections, the game
thread's allocations and JIT time. The panel lists frames over a work threshold
(twice the median plus 2 ms unless set) and a click reads one frame in the table;
`KF3_PROFILE_SPIKE=12` prints them.

**First measurement** (2026-10-06): slot 1 (area 5, `fdat17`, standing), the
Retained GPU renderer with the user's interface settings, `KF3_FPS=144`,
`KF3_PROFILE=panel KF3_PROFILE_FUNCS=stages`, RX 9070 XT; 5,406 frames after the
first 30 s. 144.0 fps, frame 6.95 ms (p99 7.50); **work 1.44 ms** (p99 1.96),
swap 0.06, the frame cap 5.44; **GPU 1.36 ms a present** (`world` 0.92, `ao` 0.31,
`scene` 0.11, `composite` 0.03). The largest CPU sections: `LibGpu.DrawOTag` 0.28
ms, `post GpuWorld.Begin` 0.27, the interface 0.13 (the panel itself among it), the
`HookAttach` slots 0.10 over 73 calls, the surface buffer 0.09, the profiler's own
reporting 0.07; stage 15's own body 0.02, the stages under 0.01 each. Every spike
past the boot was **JIT**: the first frames of play (stage 5 84 ms, `Analog.
ReplaceMove` 81, stage 4 71, all compiling), and one 19 ms frame a minute in (17 ms
of JIT in the vblank grid). Nothing here is judged by eye; whether the panel
reads well is the user's to say.

**Two costs taken off since** (2026-10-06), slot 5 (`fdat05`, standing), uncapped,
the user's settings: `NoDither`'s second walk of the ordering table before each
DrawOTag, **0.047 ms → gone** (runtime `0094`; "Unit 1" in `PICTURE.md`), and
stage 15's table clear, `func_80035630`'s two `ClearOTagR`s, whose DMA was 8192
calls down the full store path, **0.055 → 0.018 ms** (runtime `0095`). CPU work
there about 0.94-1.01 ms a frame after. The largest left are `post GpuWorld.Begin`,
0.25 ms, almost all `RetainedMap.Update` re-hashing the map to see whether it
changed, and the retained world's draw inside DrawOTag, 0.17 ms.

## Retained scene verification (2026-10-04)

Build the game, then snapshot its entire output into an isolated temporary
runtime before launching. Copy cards/settings/interface configuration too; do
not build over a running runtime's binaries. Tests below used
`/tmp/verdite3-gpu-reference/`, 15 Hz ticks and bounded launches.

`tools/scene-probe/SceneProbe.csproj` references the built game/runtime without
rebuilding them. Its executable takes the game output directory and a temporary
fixture directory. It checks source cache mutations and actual recompiled pose
math, and exports the runtime's composed shaders. `scripts/shader_probe.py
<fixture-directory>` links them in an offscreen EGL context and checks isolated
fog/pose/light, neighbour-blend and distance-fade numeric outputs, and that the
packet programs still link. It captures no game window. The render-distance
fixtures run the recompiled cull classifier on the RAM corpus (`KF3_CORPUS`,
default `/tmp/verdite3-gpu-reference/shadow-corpus`; skipped when absent), and
`scripts/render_distance_tour.py` is their live counterpart.
Rebuild the probe after the runtime changes: it keeps its own copy of the runtime
DLL, and a stale one exports the old shaders.

With one controlled shell-enabled game, `scripts/scene_corpus.py --output
<temporary-directory> --headings 4` confirms actual area/overlay and gathers RAM
fixtures for all 28 areas. `KF3_SCENE_DRIVER=1` is required. It labels held player
physics and stops on the first failure rather than piling up queued commands.
See `GPU_RENDERER.md` for observed results and their practical limits.
