# Packaging assets

`verdite3.desktop` is the entry the AppImage carries; `package.env` one level up
names the port to Verdite Core's packaging scripts.

There is no mark here, on purpose. The port's icon is the fourth save slot's
memory-card icon, read off the player's disc by `patches/CardIcon.cs`, and a
release cannot carry it, so nothing shipped has an icon of its own: the
executables have no icon resource and the AppImage gets a transparent square
(appimagetool refuses an AppDir with none). Once the game has read the disc, the
window, the icon theme and the shortcuts wear the card icon — see "The window
icon" in `docs/PACKAGING.md`.
