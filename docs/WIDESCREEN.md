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
| the view-space clipper | `ViewClip.cs`, a no-op below 8:3 | **no clipper in this game** |
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

