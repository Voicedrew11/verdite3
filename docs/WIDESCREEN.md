# Widescreen: the margin, the tints, the cull cone

Verdite2's widescreen, ported. Its write-up is Verdite2's `docs/WIDESCREEN.md`
("Widescreen: the margin, the HUD and the three culls"); this file says what
carried over, what is different here and what was measured. Started 2026-10-02.

## Status

**Mechanism measured, picture not judged. Off (4:3) by default**, like every
picture switch here: `KF3_WIDESCREEN=16:9` (also `16:10`, `21:9`, a number,
`off`), Settings ▸ Testing ▸ Aspect, or the `aspect` shell verb. A set variable
wins over the kept `kf3.widescreen.aspect`.

| piece | Verdite2 | here |
|---|---|---|
| the margin | the runtime's (`Display.WideAspect`) | the same, shared fork |
| the present latch cleared on an executable load | `Widescreen.cs` | ported (`open`, `game`, `end`) |
| full-screen tints stretched | `Widescreen.Stretch` | ported, by shape |
| the HUD anchored | off, `KF2_WIDESCREEN_HUD=1` | not ported |
| the tile cone widened | `CullCone.cs` | a different build; see below |
| the view-space clipper | `ViewClip.cs`, a no-op below 8:3 | **no clipper in this game**; the near path's libgte division has a screen test, widened with the aspect (`NearScreen.cs`) |
| the primitive buffer | ran out; moved to 4 MB (`PrimBuffer.cs`) | measured, see below |

## The margin, the latches and the tints

`patches/Widescreen.cs`. The runtime renders the margin; the patch sets the
aspect, clears the backend's margin-content latches when an **executable** loads
(never for the `fdat` area modules, which are `GAME.EXE` still running; Verdite2's
"The present gate" says why), and widens any primitive that is semi-transparent,
flat and spans the clip rectangle's full width, setting `GpuHle.PortWidenedPrim`
so the latch does not count it. `KF3_WIDESCREEN_EFFECTS=0` is the comparison. At
4:3 no `RenderPrimEvent` listener is attached.

The HUD anchoring and Verdite2's `DrawOTag` replacement are not ported: Verdite2
ships the anchoring off, and the replacement exists only for it. This game's HUD
is 3D models (stage 15's call 9, the front of the table) and sprites (call 10),
so Verdite2's two clusters would not find it anyway.

Measured, 16:9, slot 1, `fdat02`, turning and walking, 144 fps
(`KF3_WIDESCREEN_PROBE=1 KF3_PRESENT_PROBE=1`):

- **13-35% of primitives reach the margin** in the area, per 2 s window (Verdite2:
  about 25%). The game culls per tile and by depth and leaves the screen edge to
  the GPU here too, so the margin has a picture to show.
- Every present after the area's first frames picks the wide target
  (`wide 288, plain 0, vram fallback 0`); the title and boot are `vram fallback`,
  as in Verdite2.
- **Tints**: 74 and 241 stretched in the two windows of the area's fade-in, then
  **0 in every window of play**, the negative control that no world geometry is
  caught.
- 144.0 fps drawn at 15.0 ticks/s; no exceptions.

**To judge by eye**: the margins in play; the fade-in and a death fade or damage
flash reaching the window's edges; the title and menus (pillar-boxed, by design);
the HUD at its authored place.

## The cull cone: a classifier, not a trapezoid

Surveyed 2026-10-02 (an opencode agent, read-only, checked by hand). Verdite2
draws its cone from a seven-pair table with four Bresenham edges and a scanline
fill, which is why widening it there meant recording corners and ORing a second
trapezoid. **Verdite3's `func_80034BF4` has none of that.** It computes the cone
per cell:

- the camera's pitch (`0x801AEC5C`) and yaw (`0x801AEC5E`); a half-angle
  `S5 = shaped(pitch) + 0x1B8`. **440/4096 of a turn is 38.7°, which is
  atan(160/200)**: the 4:3 screen's half-angle at the GTE's `H = 200`, exact.
  Looking up or down opens it;
- two edge rays at `yaw + 0x400 ± S5`, as incremental half-plane tests;
- the draw radius squared `T5`: the u8 at `0x801AEAE9` squared, scaled by
  `cos(S5/2)` once `S5 ≥ 600`;
- every cell of the 25×25 grid at scratchpad `0x1F800120` written by distance
  from the window's centre: under 5 → `0x1E` inside both half-planes, under `T5`
  → `0x1A` inside, under 256 → `0x08` regardless, else 0. Bit `0x02` is what the
  map walk draws, `0x04` the near assembler;
- a five-cell cross written when the u8 at `0x801B25E5` is `0x11`, the offset
  words (`0x801AEC6C`..`78`), then one of two occlusion floods (`func_800345F4`,
  `func_800348F4`, by yaw), which only clear cells.

Because every cell is classified, **Verdite2's dropped-row failure cannot happen
here**: a cell off the grid is never a boundary another row depends on.

### Widening it: the half-angle opened, the radius kept

`patches/CullCone.cs`. A pre hook on both floods, where the stock grid is complete
and nothing has been cleared yet, recomputes the classifier in C# from guest
memory (the game's own `rsin` and `func_80076DA0` run on a context of their own,
below the live stack) and runs it again with the half-angle opened to
`atan(Factor · tan S5)`, `Factor = (320 + 2·margin) / 320`, only while
`S5 < 1024`. The draw radius stays the stock one. **A cell is only added**: one
the stock grid left at 0 that the wider wedge marks gets `0x1E` or `0x1A`, and the
epilogue's crosses (the eye's, always lit; the one behind it, cleared unless
`0x801B25E5` is `0x11`) are never touched. The flood then shadows added cells
like any other. At 4:3 the factor is 1 and the hook returns at once.
`KF3_WIDESCREEN_CULL=0` is the comparison, `=<factor>` pins one.

**The oracle is the game's own grid.** `KF3_WIDESCREEN_CULL_PROBE=1` runs the C#
classifier at the stock angle every frame and counts the cells where it differs
from what `func_80034BF4` wrote. The first transcription (an opencode agent's)
read 1,500-52,000 a window; two bugs, found by reading it against the MIPS: the
row step's `S1` was missing its `- A3`, and the epilogue's second cross was not
excluded. After: **0 mismatches in every window**, at 16:9 level and at 21:9
looking up and down (stock `S5` 440 to 839, so both arms of the radius). `=2`
prints the grid as a map once a window.

Measured, slot 1, `fdat02`, 144 fps:

| aspect | `S5` → widened | tiles lit before | added a frame |
|---|---|---|---|
| 16:9 level | 440 → 534 | 156-170 | 27-38 |
| 21:9 level | 440 → 620 | 162 | 64 |
| 21:9 looking up or down | 620-839 → 772-916 | 196-250 | 12-48 |

Pacing held at 144.0 fps and 15.0 ticks/s. **Not judged by eye**: whether the
margins still fill in and empty at the edges as you turn.

## The near path's screen test

Reported by eye: at a wide aspect, geometry popped in at the bottom corners and
on walls close to the camera, with smoothing and the mouse lead off, and
`KF3_WIDESCREEN_CULL=1.6` did not fix it.

The earlier claim that this game has nothing to port from Verdite2's clipper was
wrong. This game links no `Clip3FTP`/`Clip4FTP`, but near faces go through
libgte's four polygon-division entries. Cells with grid bit `0x04`, within
distance² < 5 of the window centre, are drawn by the near assembler
`func_8003AB04`, and the models' near path is `func_800366A8`; both reach
`func_80074D88` and `func_800756A8` (triangles), `func_80075188` and
`func_80075B48` (quads), whose C# twins are `DivTri`/`DivTri2`/`DivQuad`/
`DivQuad2` in `patches/NearPathDivide.cs`. Their entry test drops the whole face
when every corner's SZ is under H/2, or every corner's X is past `OFX ± pih/2`,
or every corner's Y is past `OFY ± piv/2`. `pih` and `piv` are words at `block+4`
and `block+8` of a stack block the near assemblers fill per face with the
immediates `0x140` (320) and `0xF0` (240), at seven sites. So a face wholly in
the margin was dropped. The far assembler `func_80039D50` has no screen test,
which is why the margins otherwise fill.

The fix: `patches/NearScreen.cs` pre-hooks the four entries, and the C# near path
calls the same `NearScreen.Widen` at the top of its four twins (they are called
directly, so the hooks do not fire there). It writes 320 + 2 × margin into
`block+4` when it reads 320 there and the aspect is wide: 428 at 16:9, 560 at
21:9. The vertical 240 is unchanged. Because the game writes 320 again for every
face, 4:3 writes nothing and an aspect change needs no restore. No setting: it is
part of the aspect the player picked (see Verdite2's "Three checkboxes that were
not choices"). `KF3_NEARSCREEN=0` is the comparison; `KF3_NEARSCREEN_PROBE=1`
re-evaluates each face's corners at the entry and prints, every 2 s, faces, the
SZ/right/left/bottom/top rejects at 320 and how many X rejects the wide width
keeps.

Measured, `fdat02` from slot 1, a scripted 75 s walk turning left and right (same
script every run), 144 fps:

| run | width | X rejects (right + left) | kept by the wide width | packets/frame at rest |
|---|---|---|---|---|
| 4:3 | 320 | 22,342 + 23,900 | 0 | 113 |
| 4:3, `KF3_NEARSCREEN=0` | 320 | 22,352 + 23,887 | 0 | 113 |
| 16:9, `KF3_NEARSCREEN=0` | 320 | 22,409 + 23,962 | 0 | 113 |
| 16:9 | 428 | 22,230 + 23,669 | 8,916 (19%) | 125 |
| 21:9 | 560 | 22,415 + 23,963 | 15,618 (34%) | 125 |

The SZ rejects were 0 in every run; the vertical rejects did not change with the
width. `KF3_NEARPATH=verify` at 16:9 with the fix: 0 RAM, scratchpad, register
and GTE mismatches over ~2,016 calls a window. `KF3_PRIMBUF_PROBE=1`: peak 8,228
bytes at 4:3, 8,708 at 16:9, 8,868 at 21:9 of 106,496; 0 frames ran out and 0 near
halves starved in every run. Pacing 144.0 fps drawn at 15.0 ticks/s.

The cone was checked as the second suspect and ruled out. Unlike Verdite2, the
window is centred on the camera and the occlusion flood's rays start at the
camera tile (`func_800345F4` / `func_800348F4`, called from `func_80034BF4`), and
the `KF3_WIDESCREEN_CULL_PROBE=2` map shows the wedge's apex about four cells
behind the camera, so every cell round the camera is already lit. A Verdite2-style
rescue (force-lighting a radius-3 disc round the camera after the flood) was built
and measured: it lit 33-35 cells a frame, all of them cells the flood had
cleared, and changed neither the near faces passed (43,270 vs 43,288) nor
packets/frame (125 vs 125). Not kept.

**Mechanism measured, picture not judged**: whether the corners and the near
walls still pop in is the user's to look at.

## The primitive buffer

Verdite2's ran out at a wide aspect (`0x19000` bytes a buffer) and was moved above
2 MB. Here the buffer is `0x1A000` bytes (106,496), and the near assembler is
taken only with `0x2800` bytes left, so running low would first show as near
halves drawn by the bulk assembler, unsubdivided. `KF3_PRIMBUF_PROBE=1`, `fdat02`,
turning and walking: **peak 57% at 4:3, 66% at 16:9 with the cone widened, 31%
at 21:9 looking up and down; 0 frames ran out and 0 near halves starved in every
window.** Not moved. One area only: worth re-asking in a busier one.
