# Handoff: what is left of the Japanese language option

Written 2026-11-08, with the work uncommitted in the working tree. Read
`docs/JAPANESE.md` first: it is the reference for what is built and measured. Tasks 1 to
6 of the first handoff are done there.

## Still to do

1. **The user's eye.** No person has seen any of it. Look at a conversation, an item
   description, the menus (status page, item action row, a card message), a video, and a
   save loading in both languages. Try it with
   `KF3_LANGUAGE=ja dotnet bin/Release/net10.0/KingsField3.dll disc/KingsField3.cue`.
2. **Not yet seen in a run** (read from the code only): the save slots DATA n and NEW, a
   spell row showing FLAME or ANTIDOTE, the save and format questions, videos S14 and S15.
3. **Rev 0's menus, optional.** Rev 0 plays with the disc layer but English menus,
   because its `GAME.EXE` keeps the tables and routines elsewhere. The search takes Rev 1
   first, so this matters only to a player with Rev 0 alone. Left out by the user's choice.

## Commit

Only when the user asks. `tools/` is untouched. A message states the finding; never a
session URL. Delete this file when the list above is empty.
