# Recompilation: config, function maps and SDK addresses

How the recompiler turns the disc's MIPS into C#: the overlays in
`config/kf3.json`, the swept function maps under `config/funcmaps/`, and mapping
PSY-Q entry points by address to the runtime's HLE. Verdite2's
`docs/RECOMPILATION.md` is the model, and it documents the traps to expect here
too — overlays that share an address range, entry points a linear sweep misses,
and `ScanCrossImage` splitting one function into several. None of those is yet
known to apply to this disc; they are what to look for, not findings.

## Status
