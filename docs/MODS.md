# Mods

The runtime-loaded mods under `mods/`: what each does, how it is built, and what
has been measured or judged. The addresses they rest on are in
`docs/GAME_INTERNALS.md`; this document is about the mods themselves.

A mod is a folder with a `mod.json` and C# that the runtime compiles with Roslyn
at boot (no implicit usings; every namespace named). Mods default to off: enable
one in the game's Mods panel, which writes `mods.<id>.enabled=True` under
`[RecompOne]` in `interface.ini`. A disabled mod loads silently, and so does an
enabled one that does not compile, apart from a `[Mods]` line on the console.

## Debug Tools (`mods/kf3debug`, `kf3.debug`)

Built 2026-10-03 as a port of Verdite2's `mods/kf2debug`: the same structure,
class names and conventions, with every address re-found for this game (the
reverse engineering is in `docs/GAME_INTERNALS.md`, "The player" and the sections
under it, and "Changing area"). Everything is off until it is turned on; every
hook returns at once on a bool test when off, and nothing acts without a
character in an area (`GameState.IsInGame`, max HP non-zero), so the attract demo
is untouched.

| file | what |
|---|---|
| `GameState.cs` | the player block's addresses and typed accessors; stage 4 `func_80030FCC` is the player's tick, and every feature posts on it |
| `Cheats.cs` | invincibility, infinite MP, enemies that ignore you, a walk/turn speed multiplier |
| `Noclip.cs` | flight through walls and floors, camera-relative, with the cinematic camera, return-to-start and snap-to-floor |
| `Attributes.cs` | the character editor: EXP, level (through the game's level-up), HP/MP and their maxima, gold, the six base attributes, the five conditions, and the derived ratings with a lock that holds them across the recompute |
| `Items.cs` | the inventory (give, remove, set a count, give all) and the equipment slots, through the game's own add, remove and equip routines |
| `Magic.cs` | the spell book: learn, forget, learn all, and the selected spell (new; Verdite2's mod has no magic editor) |
| `Warp.cs` | four position bookmarks, persisted, and a warp to any of the 28 areas |
| `Hotkeys.cs` | F2 panel, F3 noclip, F4 invincibility, F5/F6 bookmark 1, F7 snap to floor, F8 return to the noclip start; V film mode (the cinematic camera, enemies ignoring you and noclip, all on, or all off when they already are; no toast); Page Up/Page Down and Left Shift fly (Space is attack in the port's layout); the pad's spare button toggles noclip |
| `DebugPanel.cs` | the dockable panel (Debug ▸ KF3 Debug): Cheats, Attributes, Items, Magic, Warp, State, Keys |

### The hooks

| address | kind | by | why |
|---|---|---|---|
| `0x80030FCC` stage 4 | post | all | the last player code before stage 10 copies the player into the camera: flight, restores, queued edits, hotkeys |
| `0x8002A6F4` take damage | pre | Cheats | zero the amount; the routine's own `amount == 0` return skips HP, knockback, flash and death |
| `0x8002A6A0`, `0x80030BE0` HP adders | pre | Cheats | zero a negative amount (poison, equipment drain); heals pass |
| `0x80030A6C` death latch | pre, skip | Cheats, Noclip | the falls and drowning call it at full HP; Noclip refuses it while flying, as Verdite2's does |
| `0x8002F9BC` walk, `0x8002F5C0` look | pre | Cheats | scale the walk and turn rates stage 4 has just written |
| `0x80029500` recompute | post | Attributes | re-apply locked ratings after the game re-sums them |
| `0x80047010` stage 3 | post | Warp | start an area change where the game's own exits run |
| `0x8004BF1C` behaviour picker | pre | Cheats | Enemies ignore you: the distance to the player in `a0` becomes `0x20000` |

The shared collision queries (`func_80033F38` and the floor queries) are never
hooked: creatures and objects call them from some thirty sites.

### Measured, 2026-10-03

From a run with a throwaway test entry that took commands from a file on the
stage-4 tick (not committed):

- **Load**: 15 hooks on 9 functions, all committed.
- **Invincibility**: a direct `func_8002A6F4(0, 10, 0)` took HP 50 to 40 with it
  off and left 50 with it on; `func_80030A6C(0)` set the dead state 0x11 with it
  off and was refused with it on.
- **Infinite MP**: MP written to 5 was back at 30 on the next tick.
- **Speed**: x3 moved the player about 3.8 times as far in the same hold
  (one comparison; the unscaled walk may have met a wall).
- **Items and equipment**: give, remove and equip changed the counts and slots
  as read back; the probe's names are the game's.
- **Magic**: learn-all marked 24 records known; the six unused placeholder
  records are skipped.
- **Level-up**: two levels through `func_8002A310`: level 1 to 3, max HP 50 to
  64, EXP set to the threshold, base attributes raised. HP is not refilled; that
  is the game's routine.
- **Bookmarks**: save, walk away, recall: back to the saved position exactly.
- **Area warp**: `fdat05`, `fdat17`, `fdat32`, `fdat62`, `fdat83` and back to
  `fdat02` load and play on. See "Changing area" in `docs/GAME_INTERNALS.md` for
  why the landing needed two fixes: the warp now moves the player to the nearest
  tile with a floor and calls the game's own placement `func_8002B760`, and by
  default switches noclip on at arrival ("Fly after an area warp"), because
  without it the first step cost 16 HP in `fdat32` and killed in `fdat62`. A
  warp to `fdat08` was sent straight back to `fdat02`.
- **Noclip**: flies forward and up under a held pad, and hands the player back to
  the game cleanly; the speed under real input was not measured (the test could
  not hold a button for every tick).

### Enemies ignore you, 2026-10-08

Verdite2's Peaceful switch, left out of the first port because the picker it hooks
had not been found here. It is now (`docs/GAME_INTERNALS.md`, "The creature AI is a
rule table scored on one number"): a pre-hook on `func_8004BF1C` tells it the player
is `0x20000` away, so only a creature's "player is far" rules can pass, and the
game's own scorer picks among them. The switch is in the Cheats tab, off by default
and unsaved, like invincibility; `KF3_DEBUG_PEACEFUL=1` starts with it on.

Measured in `fdat17` with the player held 5000 units from creature 14 (type 5) for
30 s: off, it alternated between its far rule `0x00` and its approach rule `0x05`;
on, it picked `0x00` and kept it. The load reports 17 hooks (15 before, plus the
picker and noclip's refusal of the death latch).

The same port brought over two smaller things Verdite2's mod had and this one did
not: noclip refuses the death latch while flying, so the below-the-floor check and
drowning cannot end a flight with invincibility off (by construction; no flight
was measured), and the cinematic
camera's two smoothing sliders have their tooltips. Verdite2's cinematic camera
also hides its mouse-capture glyph; this port has no such glyph, so there is
nothing to hide.

### To judge by eye

Nothing here has been looked at: the panel and its layout, noclip's feel and
speed (the KF2 default of 7000 units a second against this game's walk of 3000),
the cinematic camera, whether a creature switched to ignoring you mid-swing still lands the blow and whether its far behaviour still walks it towards you (both open in Verdite2 too), whether learned spells appear in the magic menu and cast,
whether the equipment editor's slot names match the game's equipment screen
(shield and gauntlets are the static reading), and where area warps land.
