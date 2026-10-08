# Environment variables

Every `KF3_*` switch the port reads, in one list, with what each does and its
default. This fills in as diagnostics are added, the way Verdite2's
`docs/ENV_VARS.md` did. Add an entry here when a switch is added to
`Program.cs`.

Most of the switches below also have a live control in Settings ▸ Testing ("The
Testing tab" in `docs/DEVELOPMENT.md`); a set variable wins over a value the tab
kept.

## Status

| switch | what | default |
|---|---|---|
| `KF3_LOG` | the runtime's log channels: `bios`, `cd`, `gpu`, `dma`, `sdk`, `spu`, `mdec`, `irq`, or `all` | none |
| `KF3_AGENT` | `1`: the `[KF3-AGENT]` state beacon on stdout | off |
| `KF3_SHELL` | `1` or a port: the command channel on `127.0.0.1:27903` | off |
| `KF3_AUTOSTART` | `1`..`15`: load that card A slot at boot; `new`: a New Game | off |
| `KF3_BOOTEXE` | `end`: skip the first `OPEN.EXE` straight into `END.EXE`, as `GAME.EXE`'s exit word 3 hands over (all three movies); `end3`: as exit word 4 does (the last movie only). A diagnostic: the ending without finishing the game | off |
| `KF3_ENDINGHOLD` | `0`: leave `END.EXE` to spin after its last movie, which here is a window that is neither redrawn nor closable; a comparison only | on |
| `KF3_ENDINGEXIT` | `0`: hold the ending's last frame for good, as the console did, instead of returning to the title on a button | on |
| `KF3_AUTOPAD` | `seconds:button:holdMs,…` from the first area load | none |
| `KF3_FULLCARD` | `0`: the games' own card checks, which on a full card (five saves) leave Continue off at the title and make the in-game Save offer a format instead; a comparison only | on |
| `KF3_AUTORELOAD` | `0`: leave a death to the game. Settings ▸ Gameplay, kept as `kf3.autoreload.enabled`; the variable wins | on |
| `KF3_AUTORELOAD_SLOT` | `0` the last used slot, `1`..`5` pins one. Gameplay ▸ Save slot, kept as `kf3.autoreload.slot` | 0 |
| `KF3_AUTORELOAD_DELAY` | seconds of the death sequence before the reload (0-10); not a setting | 2.5 |
| `KF3_FPS` | frame pacing: the picture's rate, or `off` for uncapped; unset is the kept rate (`kf3.fps`), else 144. Pacing is turned off only in Settings ▸ Testing (kept as `kf3.pacing=0`) | 144 (2026-10-06) |
| `KF3_TICKRATE` | the world's rate under pacing (5-60 Hz); changes gameplay speed. Testing ▸ Frame pacing ▸ Tick rate, kept as `kf3.tickrate`; the variable wins at boot | 15 |
| `KF3_FPS_PROBE` | `1`: a pacing line a second | off |
| `KF3_PACING_NOBOUNDARY` | `1`: leave the frame boundary unhooked, to test the watchdog | off |
| `KF3_VBLANKPACING` | `0`: leave every VSync call outside stage 15 on the runtime's clock, to compare (with `KF3_FPS`, they wait a real vblank by default) | on |
| `KF3_VBLANKPACING_PROBE` | `1`: a line a second while any wait happened: VSync calls held by mode, the mean wait, and calls exempted inside stage 15 | off |
| `KF3_LOOPPACING` | `0`: let a loop that calls stage 15 itself (item pickup, message box, fades, area-module fades) step once per drawn frame instead of once per world tick; a comparison only | on |
| `KF3_LOOPPACING_PROBE` | `1`: a line a second while a modal loop runs: modal stage-15 calls a second (the loop body's rate), redraws each, and the last call's `a0`/`a1` and caller | off |
| `KF3_STAGEPROBE` | `1`: which main-loop stages write the ordering table, every 5 s | off |
| `KF3_RATECENSUS` | seconds: which words change on frames no stage ran on (needs `KF3_FPS`) | off |
| `KF3_PROFILE` | `1`: the frame profiler records from boot, a console summary every 5 s; `panel`: and opens its panel (Shift+P toggles it at any time) | off |
| `KF3_PROFILE_OUT` | a path: every recorded frame's sections and GPU passes as CSV, for `scripts/profile_report.py` | none |
| `KF3_PROFILE_SPIKE` | ms: a console line for each frame whose work passes it | off |
| `KF3_PROFILE_FUNCS` | `stages`, or `[overlay:]hex` items joined by `+`: functions to time with an empty pre-hook | none |
| `KF3_PREJIT` | `0`: leave every method to its first call instead of compiling the recompiled code, the patches and the runtime ahead on background threads from the first overlay load; a comparison only (`DEVELOPMENT.md`, "The stutters") | on |
| `KF3_PREJIT_PROBE` | `1`: a line as the pass reaches the end of each batch (each area module, the patches, the runtime, the generics, `game`, `main`, `end`, `open`) | off |
| `KF3_PREJIT_THREADS` | threads for that pass, at lowest priority | a quarter of the cores, 1-4 |
| `KF3_MAP_SETTLE` | ms that what the retained map is built from must hold before a rebuild of more than 8 chunks, or the area's first; the game's packets draw the map meanwhile. `0`: rebuild at every change, as before | 150 |
| `KF3_CROSSPROBE` | `1`: every present read back across an `fdat` load (400 ms before, 3 s after): luminance, fingerprint, frame time, buffer, map ready, retained draws and triangles, packets, tick, camera. Stalls the GPU; use `scale 2` | off |
| `KF3_MAPPROBE` | `1`: a line per retained map rebuild: chunks built, updates spent waiting, vertices, build and sort ms | off |
| `KF3_TINTPROBE` | `1`: every 2 s that drew a full-screen tint, the frames and tinted frames, each split by whether a tick built it, and a strip a character a frame | off |
| `KF3_GEOPROBE` | `1`: what each of stage 15's calls adds to the ordering tables, by GPU command and slot, every 5 s; `time`: each call's inclusive time instead (its hooks cost about 0.4 ms a frame) | off |
| `KF3_GEOPROBE_FUNCS` | `hex,hex,...` (up to 16): more functions for `KF3_GEOPROBE`, reported per call site | none |
| `KF3_PRIMBUF_PROBE` | `1`: every 2 s, the primitive buffer's capacity, peak and mean frame use, frames, frames that ran out (cursor past or within one `0x34`-byte packet of end), and half-tiles that set the near bit `0x04` but had under 10 KB left and got the bulk assembler; measures only, moves nothing | off |
| `KF3_POLYASM` | the bulk polygon assemblers `func_80039D50` and `func_80035CA4`, and the HUD's `func_8003C35C`, in C#: `1` (or unset), `0` recompiled, `verify` both on every call, compared (RAM, scratchpad, registers, GTE), a report every 2 s | on |
| `KF3_POLYASM_MAP` | `0`: `func_80039D50` recompiled, the rest as `KF3_POLYASM` says | on |
| `KF3_POLYASM_LIT` | `0`: `func_80035CA4` recompiled | on |
| `KF3_POLYASM_HUD` | `0`: `func_8003C35C`, the HUD's models and their orthographic transform, recompiled; in C# it hands the compass's sub-pixel fraction to the vertex map while `KF3_SUBPIXEL` is on (`docs/PICTURE.md`, "The HUD's transform") | on |
| `KF3_STAGE15` | stage 15 `func_800422B8` in C#: `1` (or unset), `0` recompiled, `verify` records the recompiled routine at every call and replays this one against it, a report every 2 s | on |
| `KF3_STAGE15_NEEDLE` | `0`: step the compass needle's spring every drawn frame, as the routine does (held to the world tick by default) | held |
| `KF3_CAMERABLOCK` | the camera block `func_800357E8` in C#: `1` (or unset), `0` recompiled, `verify` both on every call, compared | on |
| `KF3_SMOOTH` | `0`: draw each frame from the last tick's camera instead of the one interpolated between the last two ticks (smoothing runs only under `KF3_FPS`, and needs stage 15 in C#); judged 2026-10-02 | on |
| `KF3_SMOOTH_MODELS` | `0`: draw each creature, object, effect and billboard from its last tick instead of at its position, facing and clip time interpolated between the last two ticks (runs only under `KF3_FPS`, needs `KF3_MODELWALK` on; the clip time also needs `KF3_MOPOSE`); judged 2026-10-02 | on |
| `KF3_SMOOTH_PROBE` | `1`: a line a second: frames drawn and cameras carried, tick samples, snaps and placements carried across; models carried, snaps, clip frames carried, wraps, turns, re-seeks and backward steps; mouse-led frames a second and the mean \|applied − asked\| a tick | off |
| `KF3_SPRITEANIM` | `0`: let the billboard cels step on every drawn frame under pacing (held to the tick by default; `KF3_FPS_PROBE=1` prints walks stepped and held) | held |
| `KF3_MSGBOX` | `0`: step the bottom message box (`func_80041F9C`) on every drawn frame under pacing (held to the tick by default; `KF3_FPS_PROBE=1` prints calls stepped and held while it is shown) | held |
| `KF3_TEXSCROLL` | `0`: run the scrolling textures (`func_800351FC`) on every drawn frame under pacing; `hold`: run them on the first stage 15 of a tick only; `carry`: also redraw them each frame at the phase interpolated between ticks. `KF3_FPS_PROBE=1` prints calls, runs and carried uploads | carry (2026-10-06) |
| `KF3_TRUECOLOR` | `0`: the console's 15-bit, not 24-bit shading on the GL backend (`GteDepth.TrueColor`, fork `0021`); Testing ▸ Picture ▸ Shading ▸ Smooth | on (2026-10-06) |
| `KF3_NODITHER` | `0`: keep the dither, not clear the GPU's dither bit in PutDrawEnv's `dtd` and in the table's E1 words; Shading ▸ Dither | on (2026-10-06) |
| `KF3_NODITHER_PROBE` | `1`: every 2 s, the draw envs and table E1 words that asked for dither, and GPUSTAT bit 9 after each frame | off |
| `KF3_PERSPECTIVE` | `0`: the console's affine textures, not perspective-correct through the address map (`GteDepth.Enabled`, fork `0009`/`0012`) | on (2026-10-06) |
| `KF3_SUBPIXEL` | `0`: whole-pixel vertices, not sub-pixel through the address map (`GteDepth.Subpixel`, fork `0010`) | on (2026-10-06) |
| `KF3_SUBPIXEL_CULL` | `0`: decide facing on whole pixels under sub-pixel, as the game does (the fractional test is `0052`) | fractional |
| `KF3_PERSPECTIVE_PROBE`, `KF3_SUBPIXEL_PROBE` | `1`: every 2 s, the address map's roots, propagations, hits and misses; sub-pixel adds the fractions carried and the facing test's changes | off |
| `KF3_MAPCOVERAGE` | `1`: every 2 s, packets and corners the map answered for, by the routine that wrote them | off |
| `KF3_WIDESCREEN` | the presented aspect: `4:3`/`off` (the untouched path), `16:9`, `16:10`, `21:9`, or any `W:H` or decimal ratio; Testing ▸ Picture ▸ Aspect (kept as `kf3.widescreen.aspect`) | 16:9 (2026-10-06) |
| `KF3_WIDESCREEN_PROBE` | `1`: every 2 s, the share of primitives reaching the margin and full-screen tints stretched; `2`: also lists every wide primitive once per shape. Both also print each distinct set of drawn HUD records' positions, per drawer | off |
| `KF3_MENUWORLD` | `0`: a menu pastes the frozen 320-wide frame and a sign or line of dialogue its 1x copy, as the game does; a comparison. Testing ▸ Picture, kept as `kf3.menuworld`; the variable wins | on |
| `KF3_MESSAGE_FADE` | `1`-`4`: vblanks each step of a sign's or message's fade is shown for, live behind it; 1 is the game's own (10 steps in, 7 out, at 60 a second). Gameplay ▸ Message fade length, kept as `kf3.messagefade`; the variable wins | 1 |
| `KF3_MENUWORLD_PROBE` | `1`: a line a second while passes run (passes, peak primitive bytes, overflows, sessions refused, message fades live and left to the game) and a line per message fade with its steps and time, and each menu's caller as it opens; `2` also compares the first three passes with the last main-loop frame's packets, padding masked | off |
| `KF3_SETTINGSPAGE_PROBE` | `1`: a line for each open of the port settings page (PORT SETTINGS, under SYSTEM in the in-game menu), page turned, value stepped, save and discard; a refused step is logged without it. `docs/SETTINGS.md` | off |
| `KF3_MENUWORLD_TEST` | `file:entry,…`: open those full-screen messages (`func_800441D4`) from the player's tick, 10 s after the first area load and 5 s after each closes; dismiss with `KF3_AUTOPAD` (e.g. `6:305` and `17:Triangle:300`) | none |
| `KF3_WIDESCREEN_EFFECTS` | `0`: leave the death fade and the damage flash 320 wide, to compare against the default (they are stretched across the margin whenever an aspect is chosen) | on |
| `KF3_WIDESCREEN_HUD` | `1`: move the compass and the HP/MP panel out to the new edges by the margin, `0`: leave them in their 4:3 box; over DISPLAY ▸ HUD AT EDGES, kept as `kf3.widescreen.hud`. Nothing at 4:3 | off (not judged) |
| `KF3_PRESENT_PROBE` | `1` or `2`: every 2 s, what each present picked -- wide, plain, VRAM fallback; the wide setting is `GpuHle.PresentProbe` | off |
| `KF3_ZBUFFER` | `0`: the ordering table alone, not per-pixel occlusion from the C# assemblers' depth records (`GteDepth.ZBuffer`, `GtePacketDepth`); a packet with no record keeps painter's order | on (2026-10-06) |
| `KF3_ZBUFFER_PROBE` | `1`: every 2 s, packet depths recorded, polygons that found theirs, triangles tested, unmatched | off |
| `KF3_BLENDORDER` | `0`: draw blended surfaces in table order under the Z-buffer, not after the opaque ones behind them (fork `0079`) | on |
| `KF3_NEARPATH` | the near path (`func_8003AB04`, `func_800366A8`, libgte's division) in C#: `1` (or unset), `0` recompiled, `verify` compares it | on |
| `KF3_MODELWALK` | the model walk `func_80040AE4` (creatures, objects, effects, billboards) in C#: `0` recompiled, `verify` both on every call, compared (RAM, scratchpad, registers, GTE); verified 2026-10-02 | on |
| `KF3_WIDESCREEN_CULL` | the tile-visibility cone widened to the aspect: `0` leaves the stock 4:3 cone, a number pins the widening factor instead of the aspect's (1 at 4:3) | follows aspect |
| `KF3_WIDESCREEN_CULL_PROBE` | `1`: every 2 s the factor, the last stock and widened half-angles, tiles lit, tiles added, and the oracle's mismatches (stock classifier against the game's own grid; must be 0); `2` also prints the last grid as ASCII | off |
| `KF3_DEBUG_NOCLIP`, `KF3_DEBUG_GODMODE`, `KF3_DEBUG_INFINITEMP`, `KF3_DEBUG_PEACEFUL` | `1`: the debug mod (`mods/kf3debug`, enabled in the Mods panel) starts with noclip, invincibility, infinite MP or enemies ignoring you on; see `docs/MODS.md` | off |
| `KF3_DEBUG_NOCLIP_SPEED` | noclip's flight speed, units a second | 7000 |
| `KF3_DEBUG_SPEED` | the speed multiplier's factor (the switch itself is in the panel) | 2 |
| `KF3_DEBUG_HOTKEYS` | `0`: the debug mod's F2-F8 and flight keys off | on |
| `KF3_DEBUG_ITEMS_PROBE` | `1`: print the item table, counts and equipment once an area is up; `2` also gives one of everything | off |
| `KF3_DEBUG_MAGIC_PROBE` | `1`: print the spell book once an area is up; `2` also learns every spell | off |
| `KF3_NEARSCREEN` | the near assemblers' polygon-division screen block (the u32 at block+4, the game's 320) widened to the aspect; `0` leaves it at 320 | follows aspect |
| `KF3_NEARSCREEN_PROBE` | `1`: every 2 s, near faces seen, rejects by the entry test's SZ, X-right, X-left, Y-bottom and Y-top groups at the stock 320, and how many X rejects the wide width rescues | off |
| `KF3_MOPOSE` | the MO pose blender `func_800431E8` in C#: `0` recompiled, `verify` both on every call, compared (RAM, scratchpad, registers, GTE); verified 2026-10-02 | on |
| `KF3_KEYS` | `fps`: the port's WASD keyboard layout (the default for a fresh install); `stock`: RecompOne's own bindings | fps |
| `KF3_MOUSE` | `0`: hand the mouse back; look and the mouse buttons are off | on |
| `KF3_MOUSE_TURN`, `KF3_MOUSE_LOOK` | mouse-look sensitivities, turn and pitch | 1.0 |
| `KF3_MOUSE_INVERTY` | `1`: flip look-Y | off |
| `KF3_MOUSE_LEAD` | `0`: show mouse motion when the next tick spends it, not the frame it happens | on |
| `KF3_MOUSE_BUTTONS` | the pad button the left, right and middle mouse buttons press, e.g. `Square,Triangle,Cross` (attack, magic, examine) | Square,Triangle,Cross |
| `KF3_MENUMOUSE` | `0`: the menus are pad and keyboard only; on, the pointer hovers and clicks them (`docs/INPUT.md`, "The menu pointer") | on |
| `KF3_MENUMOUSE_PROBE` | `1`: each widget's rows as drawn, and a line a second: the pointer in game pixels, the live widget and row, hovers, moves, scrolls, confirms, back-outs, injections | off |
| `KF3_MOUSE_KEY` | a Silk.NET key name, or `None`: a key that captures and releases the pointer besides the click | None |
| `KF3_ANALOG` | `0`: hand the sticks back (the default layout wires the left stick to the D-pad, which in this game turns); on by default | on |
| `KF3_ANALOG_LOOK` | `0`: the right stick stops turning and looking | on |
| `KF3_ANALOG_MOVE_ENABLE` | `0`: the left stick stops walking and strafing | on |
| `KF3_ANALOG_TURN`, `KF3_ANALOG_PITCH`, `KF3_ANALOG_MOVE` | stick sensitivities, turn, pitch and move | 1.25, 1.25, 1.0 (2026-10-06) |
| `KF3_ANALOG_DEADZONE`, `KF3_ANALOG_MOVEDEADZONE` | radial deadzones, look (and move unless the second is set) and move | 0.15 |
| `KF3_ANALOG_CURVE`, `KF3_ANALOG_MOVECURVE` | response curves | 1.35 / 1.0 |
| `KF3_ANALOG_ACCEL`, `KF3_ANALOG_ACCELMAX`, `KF3_ANALOG_ACCELTIME` | the look ramp: on, the peak multiplier and the seconds to reach it | 1 / 2.2 / 0.5 |
| `KF3_ANALOG_INSTANTSTOP` | `0`: let a released look axis coast on the game's decay (movement is never stopped) | on |
| `KF3_ANALOG_INVERTY`, `KF3_ANALOG_INVERTTURN`, `KF3_ANALOG_INVERTSTRAFE`, `KF3_ANALOG_INVERTFWD` | `1`: flip that axis | off |
| `KF3_ANALOG_PROBE` | `1`: a report of what the sticks drove, written by `AnalogProbe` | off |
| `KF3_ITEMTURN` | `0`: a picked-up item held up spins as the game spins it; on, the mouse and the right stick turn it (`docs/INPUT.md`, "Turning a picked-up item") | on |
| `KF3_ITEMTURN_GYRO` | `1`: the pad's gyroscope turns it too, and the runtime switches the sensor on | off |
| `KF3_ITEMTURN_PROBE` | `1`: a line a second while an item is held up: the phase, the record, the turn and tilt, frames drawn turned, spin passes held, frames each input drove | off |
| `KF3_ITEMTURN_TEST` | an item id (`0x6B` in `fdat02`): hold it up from the player's tick 10 s after the first area load, the right stick taken as full right and half down for the hold's second second; turns the probe on; dismiss with `KF3_AUTOPAD` (e.g. `17:Cross:300`) | none |
| `KF3_GYROAIM` | `1`: the pad's gyroscope aims a drawn bow (weapon 27 or 28, the attack drawn), one to one, through the look (`docs/INPUT.md`, "Gyro aim with a drawn bow"); switches the sensor on | off |
| `KF3_GYROAIM_PROBE` | `1`: a line a second: on/off, the pad's gyro, the weapon, the swing clock, attack held or up, vblanks gated in, ticks spent, units turned and pitched | off |
| `KF3_RUMBLE` | `0`: no rumble; the pad rumbles as a bow is nocked, drawn and loosed (`docs/INPUT.md`, "Rumble") | on |
| `KF3_RUMBLE_HD` | `0`: a Switch Pro Controller, a single Joy-Con and a DualSense over USB take two-motor rumble, not HD rumble (runtime 0104 for the Switch pads, 0105 for the DualSense) | on |
| `KF3_RUMBLE_PROBE` | `1`: a line a second: on/off, HD state, the phase, the tension, waves asked | off |
| `KF3_RUMBLE_TEST` | `1`: turns the probe on, and from 3 s after start-up plays the bow's waves three times over without a bow (nock, 1.5 s draw, 1 s full draw, loose, rest; 3.5 s cycle) | off |

The runtime still reads seven switches under Verdite2's prefix (`KF2_CDTRACE`,
`KF2_GLDEBUG`, `KF2_GTE_FAST`, `KF2_GTE_LIGHTCACHE`, `KF2_RAM_PROBE`, `KF2_SWAP`,
`KF2_VRAMCHECK`); they work here under those names until the fork takes the
prefix from the game.

## Packaging and the window icon

See `docs/PACKAGING.md`. The `VERDITE3_*` switches are the shipped launcher's
(`Verdite3`), not the game's; the developer build never reads them.

| switch | what | default |
|---|---|---|
| `KF3_ICON` | the window icon: `orb` (or `png`) the shipped mark, `off` (or `none`) no icon, `0`/`1`/`2` a frame of the fourth save slot's card icon | frame 2 |
| `KF3_ICON_INSTALL` | `0`: write nothing into `~/.local/share/icons` or `applications` (the copy a Wayland compositor reads) | on, Linux only |
| `VERDITE3_DATA` | the launcher's data directory: saves, settings, the built game | `~/.local/share/verdite3`, `%LOCALAPPDATA%\Verdite3` |
| `VERDITE3_UPDATE_CHECK` | `0`: never ask GitHub for a newer release; `force`: ask past the once-a-day limit | on, daily |
| `VERDITE3_BUILD` | the commit a launcher build says it is, for a build made outside a git checkout | the checkout's `HEAD` |

## Native scene and retained renderer development

- `KF3_NATIVE_SCENE=0|1|verify`: literal native map/model/sky/arm submission and
  forced/front/sky assemblers in C#; on by default (Testing ▸ Native scene
  reference), `0` recompiled.
- `KF3_NATIVE_SCENE_VERIFY_FUNCS=hex,...`: compare only selected function
  addresses, allowing inner assemblers to be checked separately from outer calls.
- `KF3_GPU_WORLD=0|shadow|1`: retained drawing with explicit attributed
  fallbacks, the default since 2026-10-05 (Testing ▸ Scene renderer); `shadow`
  extracts beside packets, `0` draws packets only. Requires native submission,
  perspective/depth and a supported backend for substitution; without them the
  packets draw, and the scene line reports the blocker.
- `KF3_GPU_CENSUS_FILE=path`: cumulative submissions/fallbacks, asset and actual
  backend counters as JSON, rewritten periodically.
- `KF3_SCENE_CENSUS=1`, `KF3_SCENE_CENSUS_FILE=path`: packet/domain/caller/area
  and source projection census, including unknown packets.
- `KF3_SCENE_DRIVER=1`: diagnostic game-thread shell `warp 0..27`, with actual
  area/overlay confirmation. Holds player physics after warp. Use copied state.
- `KF3_GPU_MODEL_MASK=0`: under `KF3_GPU_WORLD=1`, let packets drawn after the
  retained models keep the depth tolerance over the models' pixels (runtime `0086`),
  to compare. On by default.
- `KF3_GPU_MASK_PROBE=1`: count, with occlusion queries (a stall per batch), packet
  samples over model pixels that are behind the model, and model samples the map
  hides by 8-960 units; read with the shell's `gpu` command or the census file.
- `KF3_GPU_TOLERANCE_PROBE=1`: count, with occlusion queries before each colour pass
  of the retained map and models, the samples that pass only because the depth
  tolerance exceeded 0.25/1/4/16/64/512 units; the shell's `gpu` command
  (`toleranceSamples`, `toleranceBehind`). Runtime `0087`.
- `KF3_GPU_DEPTH_CAP=N`: the retained main view's ceiling on the tolerance's slope
  term, in game pixels' width at the fragment's depth; `0` leaves it unbounded, to
  compare. Default 1.
- `KF3_GPU_UNDER=0`: under `KF3_GPU_WORLD=1`, draw the front table's packets (the
  objects `func_8003F304` submits) over the retained world again, as before runtime
  `0096`, to compare. On by default.
- `KF3_GPU_UNDER_PROBE=1`: count, with two occlusion queries per batch (a stall
  each), the samples of packets drawn under the world and those that show; the
  shell's `gpu` command (`underTriangles`, `underSamples`, `underShown`).
- `KF3_GPU_MODEL_CLIP=0`: place a retained model's corners the GTE cannot project
  at the divide's saturated ends again, as before runtime `0103`, to compare: a
  billboard seen from below draws as a sheared slab. On by default (such a face is
  clipped at the near plane).
- `KF3_GPU_CLIP_PROBE=1`: count, on the CPU from each submit's GTE, the models'
  faces kept by depth and those with a corner the GTE cannot project (clipped),
  billboards apart; the shell's `gpu` command (`clipFaces`, `clipped`,
  `clipBillboardFaces`, `clipBillboardClipped`).

See `GPU_RENDERER.md` for what is implemented, exercised, verified and unresolved.

- `KF3_PERPIXEL=0`: no per-pixel retained lighting; unset uses saved choice, default on (2026-10-06).
- `KF3_FOG=corners|depth|distance` (or `0|1|2`): the fog worked out at a face's corners
  as the game does, by each pixel's view depth, or by each pixel's distance from the eye,
  which holds still as the view turns (runtime `0100`); unset uses the saved choice
  (`kf3.fog`, Video ▸ World enhancements ▸ *Fog*), default `depth`. See "Radial fog" in
  `WIDESCREEN.md`. `KF3_FOG_DEPTH=0` (the old switch) still means `corners`.
- `KF3_AO=1`, `KF3_AO_QUALITY=low|medium|high`: shared SSAO and its resolution/
  sample quality (on, medium by default, 2026-10-06).
- `KF3_AO_NORMALS=0`: compare depth-reconstructed normals with geometry normals.
- `KF3_ANISO=1..16`, `KF3_MIPMAPS=0|1`: decoded texture filtering and mip atlas (16 and on by default, 2026-10-06).
- `KF3_ENHANCEDIST=tiles`: enhanced shading/filtering range; 0 everywhere.
- `KF3_NEIGHBOUR_BLEND=1`: blend each map pixel's light and fog with the light records
  of the tiles around it, so they no longer step at a tile edge (runtime `0088`); unset
  uses the saved choice (`kf3.neighbourblend`), default on (2026-10-06).
- `KF3_RENDERDIST=<tiles>`: the retained map and models drawn out to that many tiles
  (up to 30; 0 or below the game's own edge is the game's reach); unset uses the saved
  choice (`kf3.renderdistance`), default 16 (2026-10-06). See "Render distance" in
  `WIDESCREEN.md`.
- `KF3_RENDERDIST_MODELS=0`: the models kept at the game's reach while the map is drawn
  past it (the comparison); creatures, objects, effects and billboards are drawn out to
  the render distance otherwise, with the C# model walk.
- `KF3_RENDERDIST_FADE=<tiles>`: the map and models faded out over that band before
  the edge of what is drawn (0 none); unset uses `kf3.renderdistance.fade`, default
  3 (2026-10-06).
- `KF3_RENDERDIST_PROBE=1`: a line every five seconds (halves added, the edge, the
  deepest box, the walk's halves outside the predicted reach, which must be 0; models
  past the reach, those refused for a texture page not loaded, creatures faded) and
  every change of the radius byte. Also the `renderdist <tiles> [fade]` shell verb
  and the `rd*` members of `gpu`.
- `KF3_PLANAR=1`, `KF3_MURK=1`, `KF3_WAVES=1`: the water (`docs/WATER.md`): planar
  reflections from a mirrored camera, murky water, the swell and ripples. Video ▸
  World enhancements, kept as `kf3.planar`, `kf3.murk`, `kf3.waves`; `0` turns one off,
  on by default (2026-10-06), except the murk, off and in neither of the WATER row's
  levels until it looks good enough to ship (2026-10-08).
  Retained renderer only.
- `KF3_PLANAR_TOLERANCE=48`, `KF3_PLANAR_RIPPLE=4`, `KF3_PLANAR_BIAS=8`: how far off the
  plane a surface takes the mirror, how far the water's texture bends it, and how far
  above the plane geometry must be to be mirrored (Verdite2's values).
- `KF3_PLANAR_FOG=0`: fog the mirror at its own view depth, not the level depth.
- `KF3_PLANAR_MIPS=1`: filter the mirror through the mip atlas, as the main view (off:
  the atlas's lookups cost the mirror 1.6 ms of CPU a frame over a pool).
- `KF3_PLANAR_WATER=0`: leave the map's blended faces out of the mirror (a comparison).
- `KF3_PLANAR_PROBE=1`: a line every 5 s: the plane, the mirror's halves, instances and
  CPU by part, its draws, and the reflection pass's readback; also turns on the
  readback the `planar` shell verb reports.
- `KF3_MURK_DISTANCE=1886`, `KF3_MURK_TILT=0.75`: the distance through water to 63% of
  the murk, and the cosine a murked surface may lean to (0 murks any).
- `KF3_WAVES_PROBE=1`: a line every 5 s: the water rect, the clock, rippled batches and
  the swell's free corners.
- `KF3_GPU_SURFACE_PROBE=1`: periodically request numerical surface/depth coverage,
  and print `[KF3] surface probe: ... behind= ahead= missing=` every 5 s. `ahead` must
  stay 0: an opaque surface in front of the frame's depth is AO's box round a
  billboard (see "AO's box round billboards, again" in `GPU_RENDERER.md`).

Video exposes the feature controls; Testing exposes session-only scene/reference
choices. These are implemented controls, not visual acceptance.
