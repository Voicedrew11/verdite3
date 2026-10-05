# Packaging assets

`verdite3.png` and `verdite3.ico` are the shipping mark: for now Verdite2's green
verdite orb, copied. The PNG is 256×256 with an alpha channel; the ICO holds 16,
32, 48 and 256 for Windows (taskbar, shortcuts, the stub and the launcher's
`ApplicationIcon`). To replace the mark, overwrite the two files at those sizes.

The orb is the *shipped* mark and the fallback. In play the window wears the
fourth save slot's memory-card icon instead, read off the player's disc at boot
(`patches/CardIcon.cs`); `KF3_ICON=orb` keeps this one. A release cannot carry
that icon — see "The window icon" in `docs/PACKAGING.md`.

`verdite3.desktop` is the entry the AppImage carries; `package.env` one level up
names the port to Verdite Core's packaging scripts.
