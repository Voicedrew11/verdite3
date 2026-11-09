# Japanese text and videos (KF3_LANGUAGE=ja)

The Japanese King's Field III (SLPS-00377; Rev 1 for the menus, either revision for the rest) supplies the text
pictures, the talk tables, the conversation text and thirteen videos. It is laid
over the US disc (SLUS-00255) in memory at boot. The code is `patches/JapaneseDisc.cs`
(finds the discs, installs the hook), `patches/JapaneseMix.cs` (pure bytes: builds
the replacements) and `patches/MixedDiscImage.cs` (serves them). Nothing is written
to disk, and the player's Japanese disc is never copied into the repo.

## What it does and does not do

- Language: `KF3_LANGUAGE=ja` or `en`; with no variable, the stored `kf3.language`
  (Settings ▸ Gameplay ▸ Language, or the game's PORT SETTINGS ▸ GAMEPLAY); with
  neither, English.
- The stored value is read at `RuntimeReadyEvent`. The settings store is loaded by
  `Runtime.Initialize`, inside `Entry.Run`, so it is empty when `Program.cs` installs
  the hook. Verdite Core's card icon opens the US disc from `Program.cs` before that;
  that open passes through unmixed and decides nothing. `KF3_LANGUAGE` decides at once.
- A change of language takes effect at the next start only. The page and the
  Settings window keep the new value and change nothing in the run.
- No Japanese disc found: one `[KF3] japanese:` line, and the game plays in English.
- The Japanese disc is found in the US disc's folder, or in its `Japanese discs`
  subfolder, as `.cue` or `.chd`; `KF3_JP_DISC` names one directly. The first file
  in ordinal order wins, so `King's Field III (Japan) (Rev 1).cue` is taken before
  `King's Field III (Japan).cue` when both are there.

## What stays US

- The program: OPEN, GAME, END and the 28 area code modules.
- All gameplay data: creature statistics (1600 HP where Japan has 800), items and
  their effects, and the area's creature definitions.
- In FDAT, every entry except the area data (the code entries load at exact sectors).
- In ITEM, entries 0..149 (the 3D models).
- The disc's tracks, leadout and data sector count.

## What comes from the Japanese disc

- `CD/COM/TALK.T` and `STALK.T`, whole. The US slot is rewritten from its start with
  the Japanese file; the rest of the slot is zero.
- `CD/COM/ITEM.T`, entries 150 and up, where the Japanese entry is a TIM image (its
  first word is 0x10). Dry run on the Rev 1 disc: 239 identical, 581 replaced.
- `CD/COM/FDAT.T`, the area data entries `3n+1` for n = 0..27. Their conversation
  rules are mixed by `MixAreaData`. Dry run: 18 of 28 changed.
- Thirteen videos, served whole and sector for sector from the Japanese disc:
  `OP/M0`, `M1`, `M3`, `M5`, `M6`, `STR/S03`, `S06`, `S07`, `S08`, `S12`, `S13`, `S14`,
  `S15`. Eleven have the same length on both discs. S14 and S15 are shorter in Japan
  (685 frames against 686, 965 against 968).

The short two are safe because of how a video ends. GAME stops one when the frame
number passes `lhu [record+2]` (`0x80061750..0x80061778`), and the record is entry `n`
of a 0x34-byte table at `0x801E7EDC` for video `Snn`. The table is loaded from one FDAT
entry, with the same limits on both discs: 683 for S14 and 963 for S15, read from the
running game's RAM and from both discs' FDAT. So the Japanese frames 684 and 964 exist
and the video ends where it should. The sectors past a shorter file read as zeros.

Videos that stay US: L0, L1, S11.

- L0, L1 and S11: US-only; there is no Japanese counterpart to mix.

## The title screen (`patches/JapaneseTitle.cs`)

`OP/OP.D` holds the title's images and sound bank. The Japanese file is longer than
the US file's sectors (217,712 bytes against 172,384) and than the US buffer, its bank
is at another offset, and its pieces sit elsewhere, so it is not served through the
disc layer. In `open`, five routines are replaced while the Japanese file is in use:

| US routine | Japanese routine | what changes |
|---|---|---|
| `func_80012494` | `0x80012E84` | the file, read from the Japanese disc into a 0x3C000-byte buffer |
| `func_800126AC` | `0x800130BC` | the images, then the bank at +0x2A350 |
| `func_8001279C` | `0x800131AC` | the background, and the logo in four pieces at y 60..180 |
| `func_80012B0C` | `0x80013704` | one copyright line at y 170 |
| `func_80012EA0` | `0x800138A4` | NEW and CONTINUE at y 192 and 204 |

Everything else in OPEN.EXE stays US: the movies, the pad, the card check. The text
is English on the Japanese disc too. `OP.D` is checked by SHA-256 and is the same on
both revisions; any other file, or no Japanese disc, leaves the US title.

Not used: the FROM SOFTWARE logo (the file's last three images), which the Japanese
program shows only when the title times out (`0x80012A68`).

## The two revisions

Both work with the same disc layer, but only Rev 1 gets Japanese menus (see "The menu's text"). The dry run on each (2026-11-08) gave the same plan
for all 28 areas, the same per-area changes, and the same sector counts: TALK 2731,
STALK 2451, ITEM 2429, FDAT 48, 7659 replaced in all, 11 videos served. The two discs
carry the same text layout. The mix does not depend on it: it reads each archive's
table at run time and checks the slot layout before it edits anything
(`RequireSameSlots`).

## Archive format

An archive (FDAT, ITEM, TALK and the rest) is a u16 table:

- u16 `count`, then `count+1` u16 start sectors, counted from the file's first
  sector, which holds the table.
- Entry `i` is the sectors from `start[i]` up to `start[i+1]`. A zero-length entry
  is empty.
- Each non-empty entry ends with a checksum word. The seed is 0x12345678; the sum of
  every word before it, wrapping, is the last word. The game checks it with
  `func_80019B68`. `WriteChecksum` recomputes it after an area entry is edited.

## Area data layout

Each area entry is 0x7000 bytes (FDAT entries `3n+1`, n = 0..27).

- 40 creature definitions of 0x78 bytes, starting at offset 4.
- 16 rule pointers at +0x38. Each is relative to 0x12C4, the first rule's file offset.
- A rule of type 0x70 is a conversation. Its header is 20 bytes. Its stream ends with
  0xFF. The u16 at +0xC is the TALK base page.

## Conversation opcodes (measured)

A rule's stream is read by `func_8005C308`. A talk page is `u16[rule+12]` plus the
page byte.

| byte | meaning |
|---|---|
| `0xF0 n` | jump back n bytes; also sets +0x13 |
| `0xF2` | skip, with one operand |
| `0xF3` | no-op |
| `0xF4 x` | call module slot x |
| `0xF5 n` | repeat count n |
| `0xF6` | seen; not decoded |
| `0xF7 f v` | set flag f to v |
| `0xF8 n` | jump back n bytes |
| `0xF9 f v l` | if flag f equals v, jump to label l |
| `0xFE` | label |
| `0xFF` | end of the stream |
| any other byte | a page number, added to the base |

## Where the code is

| routine | what |
|---|---|
| `func_8005C308` | the conversation interpreter |
| `func_800441D4` | loads a picture |
| `func_8001A34C` | opens an archive by name; reads its sector table at run time |
| `func_8001A154` | reads one entry |
| `func_80019B68` | checks an entry's sum |

## Measured comparison (conversation rules)

- 43 conversation rules in 28 areas: 23 identical; 16 differ in page numbers only;
  4 differ only in where pages and pauses sit; none differ in flags.
- 2 other rules differ and stay US.
- US creature numbers stay US, so the Japanese text sometimes describes numbers the
  port does not show.

## Switches and checks

- `KF3_LANGUAGE`, `KF3_JP_DISC`, `KF3_JP_PROBE`, `KF3_JP_DRYRUN`: `docs/ENV_VARS.md`.
- Dry run: `KF3_LANGUAGE=ja KF3_JP_DRYRUN=1 dotnet bin/Release/net10.0/KingsField3.dll disc/KingsField3.cue`.
  Exit 0 and one `[KF3] japanese: area` line per area when the mix succeeds.
- Probe: `KF3_JP_PROBE=1` logs each file's plan, and then, every 5 s when it changed,
  `[KF3] japanese: served N override sector(s), M video sector(s)`.

## The menu's text (`patches/JapaneseMenu.cs`)

The labels are drawn in Japanese over the US label font, with the primitives the US
routine builds (`func_800261DC`, the label routine). The aim is the Japanese game's
screen: its words, in its places, with its window sizes.

The two programs have the same menu routines in the same order, so a call in one has
a counterpart in the other (`patches/JapaneseMenuText.cs` holds the pairs). Compared
instruction by instruction on 2026-11-08:

- List tables, windows, cursors and every number's position are identical.
- Japan draws different words, moves a few (the three columns of the item action row,
  the card question), and adds labels of its own (six on the status page where the US
  has one line, two on the format question).
- Card messages differ most: other line breaks, so other line counts and window sizes.
- Japan fixes the confirm and cancel buttons where the US reads them from gp+0x38 and
  gp+0x3C. That is control, not text, and stays US unless the player turns on the
  separate Japanese buttons (`docs/INPUT.md`).

**Where the words come from.** Nothing from the discs is in the code.
`patches/JapaneseProgram.cs` is a small MIPS reader that tracks constants through a
GAME.EXE routine without running the game, so the Japanese records, positions and
window sizes are read from the player's own disc at run time. The first label drawn
builds everything; `KF3_JP_PROBE=1` prints all of it.

**How a label finds its Japanese.** One US word can mean two things (FLAME is a sword
and a spell; OPTION 2 is a list row and a title), so a label is looked up by where it
is drawn from, in this order:

| source | key | gives |
|---|---|---|
| a list group's record (`0x8007E660` and up) | the record's address | Japanese record |
| a card message record | the buffer the Japanese builder wrote | the record in memory |
| one of a choice's two records (`func_80025B24`) | call and argument, plus the US word when the code branches | Japanese record |
| a call in the site table (45 of them) | the call; `func_80026158`'s caller for the grey form | the Japanese call's record, plus any extra labels |
| a list page's title (a copy of a list record) | its US text, a group's first record preferred | Japanese words |
| a list row (`func_80025468`) | its US text, in spells first if the list kind (s4 = 2, 4, 0x10) or equipment row says so | item or spell name |
| anything else | its US text | equipment slots, status words, DATA n, NEW |

The address, call and argument keys are what removes the wrong translations: a spell
row now finds the spell's name, not the sword's. The right-aligning code 0xFF draws
nothing and only moves the pen.

**Card messages.** The ten US routines that fill a message's records are replaced: the
Japanese routine runs in `JapaneseProgram` and its records are written where the US
ones would go. The window call that follows takes the Japanese line count and size,
read from the code after the Japanese builder's call. The label routine recognises
those records by address and contents, since a stack address gets reused.

**Atlas.** `patches/JapaneseFontAtlas.cs`: 14 x 15 px cells, 18 across, in page rows
75 to 254 of tpage 0x1D. 185 of the 216 cells are free; the US block at (832, 448) and
448 nonzero pixels of the US font block's unused rows hold 28, which are left alone.
Three more hold the US blanks, whose pixels are zero but which are still drawn: the
label font's space (code 0x7F, page (120, 105), across two cells) and the number font's
padding (code 0x0A, page (238, 150)). With a Japanese glyph there, the spaces in PORT
SETTINGS and the padding before every number drew slices of that glyph, once enough
glyphs had been cached to reach those cells. They are the only codes in those rows the
US text tables use, and no other label template is on this page.
The menus use more glyphs than 185 in all, but no one screen comes near,
so the cells are a cache: a glyph is written the first time it is drawn, a cell is
reused when its glyph has not been drawn for 3 vblanks (queued primitives read VRAM
when they are drawn), and each use checks its pixels and rewrites them if the game
reloaded the font page.

**Revision.** The addresses are good on Rev 1 only. Both `GAME.EXE` files are checked
by SHA-256 and Rev 0 keeps the English menu, with one `[KF3] japanese:` line.

Measured 2026-11-08 (Rev 1, autostart into fdat02, `KF3_JP_PROBE=1`):

- The build: 292 labels resolved, none left out: 38 list records, 129 items, 25 spells,
  34 words, 45 sites, 15 message calls. Every site's position and codes equal an
  independent script's reading of the same code.
- A walk of the main menu, items, magic, equipment and a slot, status and its pages,
  storage and the system page: about 20,000 Japanese draws, at most 103 cell writes,
  0 forced evictions. Only the port's own PORT SETTINGS and settings pages stayed English.
- Card messages: CHECKING MEMORY CARD built and drawn at boot and in the in-game Load.
- Area change (fdat02 to fdat05): the atlas's rows of VRAM identical before and after.
- New game: the opening movie streamed from the Japanese disc (60,000 sectors served).
- English run (`KF3_LANGUAGE` unset): no `japanese` line.

Not observed in a run, only read from the code: the save-slot rows DATA n and NEW (the
Load page's rows did not appear in the walk), FLAME and ANTIDOTE on a spell row (the
save used knows neither), the save and format questions, and videos S14 and S15 (late
in the game).

## Open

- A scratch run needs `CdPath` in its own `settings.json`. Without it the game waits
  at the disc picker and spins at about 100% CPU before the first overlay, with or
  without the layer. That is what the 2026-11-08 runs first hit, and it is not the
  DualSense stall in `docs/DEVELOPMENT.md`: a DualSense was connected in the runs that
  reached Fdat17, and `SDL_GAMECONTROLLER_IGNORE_DEVICES` was not needed.
- Nothing Japanese has been judged by eye: conversations, item descriptions, the
  menus, the videos and a save loading in both languages still need a person.
- The port's settings page was not reached through the shell (the top menu's cursor could not be
  placed on PORT SETTINGS reliably); its labels are unchecked.
