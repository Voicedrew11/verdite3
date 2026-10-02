# Development: build, run and diagnose

How to build the recompiler, recompile the game into `generated/`, build and run
`KingsField3`, and read a run's logs. Verdite2's `docs/DEVELOPMENT.md` is the
model, with its `KF2_*` switches becoming `KF3_*` here.

## Status

Boots, plays, changes areas, saves and loads (2026-10-02). There are no patches
of the port's own yet: the world runs once per drawn frame, at 60.

## Build and run

```bash
bash scripts/setup_tools.sh        # build the recompiler
dotnet run --project tools/RecompOne/RecompOne.Recompiler -c Release --no-build -- config/kf3.json
dotnet build KingsField3Recomp.csproj -c Release
dotnet bin/Release/net10.0/KingsField3.dll disc/KingsField3.cue
```

The recompile reports `applied 63 patches, 0 reimplementations` and 2501
functions. Anything else from the first line is a name that started binding (see
"The SDK entry points" in `docs/RECOMPILATION.md`). `setup_tools.sh --signatures`
fetches the PSY-Q bank `--autoconfigure` reads; `--pull-fork`/`--push-fork` and
`--pull-core`/`--push-core` move the two subtrees.

Run it from the repository root: the runtime writes `carda.sav`, `cardb.sav`,
`settings.json` and `interface.ini` into the working directory (all gitignored).

## Diagnostics

`KF3_LOG=bios,cd,gpu,dma,sdk,spu,mdec,irq` (or `all`) turns on the runtime's log
channels. Every run prints each overlay as it loads, which is how a run is read
without looking at it:

```
[Dispatcher] loaded overlay: open
[KF3] irq callback table: open 0x8003E948
[Dispatcher] overlay open overwritten by game
[Dispatcher] loaded overlay: game
[KF3] irq callback table: game 0x8009AF9C
[Dispatcher] loaded overlay: fdat02
[Dispatcher] overlay fdat02 overwritten by fdat14
```

**For a hang, take the managed stack** of the live process, as in Verdite2:
recompiled functions carry their MIPS address in their name.

```bash
~/.dotnet/tools/dotnet-stack report -p $(pgrep -a dotnet | grep KingsField3.dll | awk '{print $1}')
```

The intro movie looks like a hang in one: the main thread sits in
`LibCdStream.StGetNext` under `func_80013EBC` while the movie plays.

The runtime's own diagnostics are still read under Verdite2's names (`KF2_CDTRACE`,
`KF2_GLDEBUG`, `KF2_GTE_FAST`, `KF2_GTE_LIGHTCACHE`, `KF2_RAM_PROBE`, `KF2_SWAP`,
`KF2_VRAMCHECK`): the fork reads them by name. See `docs/TODO.md`.

## The acceptance test

What a change to the fork, the config or the maps must keep passing. Measured
2026-10-02 against fork `a617cf8` and Verdite Core `acf4873`; the steps marked
**by eye** were the user's.

1. The recompile reports `applied 63 patches, 0 reimplementations`.
2. Boot from the cue. The log shows `open`, its table `0x8003E948`, then the
   intro movie; skip it to the title (**by eye**).
3. Past the memory card screen a short video plays and the game starts: the log
   shows `game`, its table `0x8009AF9C`, and `fdat02` (**by eye**: an area,
   playable).
4. Walk into the next area: `fdat02 overwritten by fdat14`, and the music
   changes (**by eye**).
5. Save. `carda.sav` gains directory entry `BASLUS-002551`, 3 blocks (24576
   bytes), its title block reading `KING'S FIELD 2-1 EXP 0 LV 1` for a new game.
   A check:

   ```bash
   python3 -c "d=open('carda.sav','rb').read();print([d[i*128+10:i*128+30].split(b'\0')[0] for i in range(1,16) if d[i*128]==0x51])"
   ```
6. Load that save in game and from the title screen (**by eye**). The title-screen
   load shows `game overwritten by open`, then `open overwritten by game`.
7. No `unmapped call` anywhere in the log.

No step yet runs without a person: there is no scripted pad input, auto start or
state beacon here. Those are the next things that would make this test a
program, as Verdite2's is.
