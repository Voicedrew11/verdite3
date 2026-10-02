# Verdite3

A static recompilation of **King's Field III** (the Japanese numbering) using
[RecompOne](https://github.com/BlackLabelHQ/RecompOne). The series was renumbered
for the West: the game sold in North America as **King's Field II** is disc
`SLUS-00255`, and that is the disc this project targets. (The sibling project
Verdite2 targets `SLUS-00158`, the first game's US release, which is a different
game.)

You must supply your own dump of `SLUS-00255`. No disc data is included, and
none ever will be.

**Status: bootstrapping. Nothing has been recompiled yet.** The repository holds
the license, the conventions, and `tools/RecompOne`, a subtree of the shared fork
`Voicedrew11/verdite-recompone`. The disc image is not here; the bring-up order
is in `docs/TODO.md`.

## No prebuilt binary

A playable binary cannot be shipped. The generated code is a translation of
FromSoftware's own code, so the assembly that plays the game has to be built on
the machine of somebody who owns the disc. A prebuilt binary would also bake
absolute disc addresses from one mastering, so it could silently fail to load
data on a differently mastered dump. The project ships its inputs and builds the
game at first run instead.

## Upstream

This project is built on the RecompOne fork, not directly on upstream RecompOne.
No pull requests and no issues go to upstream RecompOne; see `AGENTS.md`.
