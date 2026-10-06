using RecompOne.Runtime;
using RecompOne.Runtime.Context;
using RecompOne.Runtime.Memory;
using RecompOne.Runtime.Modding;

namespace Kf3;

/// <summary>
/// The model walk <c>func_80040AE4</c> in C#: the creature, object, effect and
/// billboard tables in that order, then the billboard clock.
///
///     KF3_MODELWALK=0        the recompiled routine
///     KF3_MODELWALK=1        this transcription (the default)
///     KF3_MODELWALK=verify   run both on every call and compare, the recompiled
///                            result standing
///
/// A transcription, not a rewrite: the same stack frame, the same scratchpad and
/// RAM writes, the same values left in callee-saved registers. Every call goes
/// through a delegate to its recompiled callee so every hook on it still fires
/// (<see cref="SpriteAnim"/> brackets the walk itself and keeps working). The two
/// things a later unit substitutes -- the position a submitter is given and the
/// rotation the walk writes to the scratchpad at 0x1F800114/116/118 -- are marked
/// <c>// carry seam</c>.
///
/// With the render distance past the game's edge, each visibility query is also asked of
/// <see cref="RenderDistance.ModelBits"/>, so a model past the game's reach is drawn
/// (C# only: the verify mode and the recompiled routine keep the game's reach). The
/// page bitmaps are the game's on-demand loader (func_800409C8 loads a marked model from
/// the CD and pins it, func_800408D0 a marked texture page, and both release what is
/// unmarked), so such a model marks nothing: it is drawn only if its model is already
/// resident (the walk's own test) and its texture pages already loaded.
///
/// See "The models" in docs/GAME_INTERNALS.md and
/// <c>scratch/u3a-notes.md</c> for what each submit is handed.
/// </summary>
public static class ModelWalk
{
    const uint Routine = 0x80040AE4;

    // ---- the four tables -----------------------------------------------------
    const uint Creatures = 0x80185DA8; const uint CreatureStride = 0x88; const int CreatureCount = 200;
    const uint Objects = 0x80191A5C; const uint ObjectStride = 0x44; const int ObjectCount = 396;
    const uint Effects = 0x801B80EC; const uint EffectStride = 0x4C; const int EffectCount = 128;
    const uint Billboards = 0x80182968; const uint BillboardStride = 0x18; const int BillboardCount = 128;

    /// <summary>Per-creature definitions, 120 bytes each; +7/+8 are two texture pages.</summary>
    const uint CreatureDefs = 0x8018C7E8;

    /// <summary>Per-object definitions, 24 bytes each; +2 is a texture page, +0xC the volume mask.</summary>
    const uint ObjectDefs = 0x8018FB3C;

    /// <summary>The matrices a submit can be handed.</summary>
    const uint ViewMatrix = 0x801AEB4C, PitchMatrix = 0x801AEC0C, RomMatrix = 0x8007E4C4;

    /// <summary>The scratchpad, the two page bitmaps and the rotation lane.</summary>
    const uint Pad = 0x1F800000;
    const uint Pages = Pad + 0x124, Cluts = Pad + 0x270;

    /// <summary>func_800408D0's texture pages, 8 bytes each: 0 while a page is not
    /// loaded. A creature's pages are from 0x33, an object's from 0x93.</summary>
    const uint PageTable = 0x801B0A2C;

    /// <summary>The billboard clock, bumped once per walk.</summary>
    const uint Clock = 0x80182964;

    /// <summary>The sky: gate, copy pointer, the copied record and its flag/counter.</summary>
    const uint SkyGate = 0x801AEAEA, SkyCopyPtr = 0x8018FAF4;
    const uint SkyCopy = 0x8018FAF8, SkyFlag = 0x8018FAD4, SkyCount = 0x8018FAD6;

    /// <summary>The two table pointers the walk publishes into the scratchpad.</summary>
    const uint OrderingTablePtr = 0x801A9174, FrontTablePtr = 0x801A91B8;

    /// <summary>The ambient sound table (10-byte records) and the player block.</summary>
    const uint Ambients = 0x801B234C;
    const uint PlayerX = 0x801B25F0, PlayerY = 0x801B25F4, PlayerZ = 0x801B25F8;

    enum Fn
    {
        Memset, PointQuery, Resident, Place, SubmitWorld, VolumeQuery,
        PrepareA, PrepareB, SkyQuery, ModelDef, Sky, Ambient, Sound, SubmitFront,
    }

    /// <summary>Each site's callee.</summary>
    static readonly uint[] Callees =
    [
        0x80018FBC, 0x80040694, 0x800405E8, 0x8004EEE0, 0x8003E34C, 0x80040708,
        0x800409C8, 0x800408D0, 0x800407CC, 0x80040568, 0x800400AC, 0x80046884,
        0x8001576C, 0x8003F304,
    ];

    enum Mode { Off, On, Verify }
    static Mode _mode = Mode.On;

    /// <summary>The mode as the Testing tab sets it: 0 recompiled, 1 C#, 2 verify.</summary>
    public static int Setting { get => (int)_mode; set => _mode = (Mode)Math.Clamp(value, 0, 2); }

    /// <summary>Whether the C# walk draws (KF3_MODELWALK unset or 1), so the carry and the
    /// billboard hold are this file's.</summary>
    public static bool InCSharp => _mode == Mode.On;

    // The carried position and rotation, in the walk's frame above its own 0x90 bytes.
    const uint Frame = 0x90u, CarryFrame = 0xA8u, CarriedPos = 0x90u, CarriedRot = 0xA0u;
    static bool _carry;
    static long _billboardTick = -1;

    // The record whose submit is next: its table (-1 none), address and identity.
    static int _table = -1;
    static uint _rec, _ident;
    static bool _queued;
    static Action<CpuContext, IMemory>[]? _callees;

    static readonly Differential _check = new("modelwalk", "func_80040AE4", 0x1000);

    static readonly ModInfo _self = new()
    {
        Id = "kf3.modelwalk",
        Name = "Model walk",
        Version = "1.0",
        Description = "func_80040AE4, the creature, object, effect and billboard walk, in C#.",
    };

    /// <summary>How many On-mode walks have run.</summary>
    public static long Frames;

    public static void Configure(string? mode)
    {
        _mode = mode?.Trim().ToLowerInvariant() switch
        {
            "0" or "off" => Mode.Off,
            "verify" => Mode.Verify,
            _ => Mode.On,
        };
    }

    public static void Install()
    {
        // Attached in every mode, so the Testing tab can switch it live; off runs the recompiled routine.
        HookAttach.OnOverlayLoad("model walk", Attach);
    }

    static bool Attach()
    {
        SymbolRegistry.Build();
        var target = SymbolRegistry.Resolve("game", null, Routine);
        if (target == null) return false;
        if (!Bind())
        {
            Console.Error.WriteLine("[KF3] model walk: a callee is not mapped; the recompiled routine stays.");
            return false;
        }
        if (!_queued)
        {
            var impl = typeof(ModelWalk).GetMethod(nameof(Replace),
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
            _queued = HookManager.AddReplace(_self, target, impl);
            if (!_queued) return false;
        }
        HookManager.Commit();
        bool ok = HookAttach.Installed(target);
        Console.WriteLine(ok ? $"[KF3] model walk: {_mode.ToString().ToLowerInvariant()}"
                             : "[KF3] model walk: not installed");
        return ok;
    }

    static bool Bind()
    {
        if (_callees != null) return true;
        if (Callees.Length != Enum.GetValues<Fn>().Length) return false;
        var fns = new Action<CpuContext, IMemory>[Callees.Length];
        for (int i = 0; i < Callees.Length; i++)
        {
            var mi = SymbolRegistry.Resolve("game", null, Callees[i]);
            if (mi == null) return false;
            fns[i] = mi.CreateDelegate<Action<CpuContext, IMemory>>();
        }
        _callees = fns;
        return true;
    }

    static void Replace(Action<CpuContext, IMemory> orig, CpuContext c, IMemory m)
    {
        // PGXP's RAM shadow is kept by the recompiled stores, which C# stores skip.
        if (_mode == Mode.Off || RecompOne.Runtime.Pgxp.Pgxp.CpuTracking || m is not PSMemory mem)
        {
            orig(c, m);
            return;
        }
        if (_mode == Mode.Verify) _check.Run(orig, c, mem, Run);
        else { Frames++; Run(c, mem); }
    }

    static void Call(CpuContext c, PSMemory mem, Fn fn, uint ra)
    {
        c.RA = ra;
        _callees![(int)fn](c, mem);
    }

    /// <summary>A visibility query, func_80040694 (<paramref name="cells"/> -1) or
    /// func_80040708, its answer widened past the game's reach by the render distance;
    /// the game's own answer is left in <see cref="_game"/>.</summary>
    static void Query(CpuContext c, PSMemory mem, uint pos, int cells, uint ra)
    {
        c.A0 = pos;
        if (cells >= 0) c.A1 = (uint)cells;
        Call(c, mem, cells < 0 ? Fn.PointQuery : Fn.VolumeQuery, ra);
        _game = c.V0;
        if (_mode == Mode.On) c.V0 |= RenderDistance.ModelBits(mem, pos, Math.Max(cells, 0));
    }
    static uint _game;

    /// <summary>Whether texture page <paramref name="index"/> of <see cref="PageTable"/> is
    /// loaded; a page past the loader's <paramref name="count"/> is none to wait for.</summary>
    static bool PageLoaded(PSMemory mem, uint first, uint page, uint count) =>
        page >= count || mem.ReadU32(PageTable + (first + page) * 8u) != 0u;

    // ---- the walk ------------------------------------------------------------

    static void Run(CpuContext c, PSMemory mem)
    {
        uint entry = c.SP;
        _carry = _mode == Mode.On && ModelSmoothing.Active;
        uint frame = _carry ? CarryFrame : Frame;
        uint sp = entry - frame;
        RenderDistance.InWalk = _mode == Mode.On;
        c.SP = sp;
        mem.WriteU32(sp + 0x7Cu, c.S5);
        mem.WriteU32(sp + 0x84u, c.S7);
        mem.WriteU32(sp + 0x78u, c.S4);
        mem.WriteU32(sp + 0x68u, c.S0);
        mem.WriteU32(sp + 0x74u, c.S3);
        mem.WriteU32(sp + 0x8Cu, c.RA);
        mem.WriteU32(sp + 0x88u, c.FP);
        mem.WriteU32(sp + 0x80u, c.S6);
        mem.WriteU32(sp + 0x6Cu, c.S1);
        mem.WriteU32(sp + 0x70u, c.S2);

        uint pageA = Pages, pageB = Cluts;
        uint ordering = mem.ReadU32(OrderingTablePtr), front = mem.ReadU32(FrontTablePtr);
        mem.WriteU32(Pad + 0x11Cu, pageA);
        mem.WriteU32(Pad + 0x120u, pageB);
        mem.WriteU32(Pad + 0x08u, ordering);
        mem.WriteU32(Pad + 0x0Cu, front);

        // The creature tables' bitmaps.
        c.A0 = pageA; c.A1 = 0; c.A2 = 0x20; Call(c, mem, Fn.Memset, 0x80040B80u);
        c.A0 = pageB; c.A1 = 0; c.A2 = 0x18; Call(c, mem, Fn.Memset, 0x80040B94u);
        mem.WriteU16(Pad + 0x84u, 0);

        RunCreatures(c, mem, sp);

        // Between the first and second table: upload the creature pages, clear them.
        c.A0 = 0; c.A1 = 0; c.A2 = 0x80; c.A3 = 0x80; Call(c, mem, Fn.PrepareA, 0x80040DC8u);
        c.A0 = 2; c.A1 = 0x91; c.A2 = 0x33; c.A3 = 0x60; Call(c, mem, Fn.PrepareB, 0x80040DDCu);
        uint soundClock = mem.ReadU32(0x801C12E8u);
        c.A0 = pageA; c.A1 = 0; c.A2 = 0x53; mem.WriteU32(sp + 0x60u, soundClock);
        Call(c, mem, Fn.Memset, 0x80040DF8u);
        c.A0 = pageB; c.A1 = 0; c.A2 = 8; Call(c, mem, Fn.Memset, 0x80040E08u);
        if (mem.ReadU8(SkyGate) == 0) mem.WriteU32(SkyCopyPtr, 0);

        RunObjects(c, mem, sp, soundClock);

        // Between the second table and the sky's redraw.
        c.A0 = 0; c.A1 = 0x80; c.A2 = 0x100; c.A3 = 0x118; Call(c, mem, Fn.PrepareA, 0x8004154Cu);
        mem.WriteU32(Pad + 0x11Cu, Pad + 0x250u);
        {
            uint a1 = ((short)mem.ReadU16(SkyFlag) == 1 && mem.ReadU8(0x8018FAEBu) == 1
                       && mem.ReadU8(0x8018FAE4u) != 0xFF)
                ? (uint)mem.ReadU8(0x8018FAE4u) << 5
                : (uint)mem.ReadU8(0x8018FAD8u) << 5;
            c.A0 = 1; c.A1 = a1; c.A2 = a1 + 0x22C; c.A3 = 0x20;
            Call(c, mem, Fn.PrepareA, 0x800415B8u);
        }
        c.A0 = 2; c.A1 = 0xF1; c.A2 = 0x93; c.A3 = 0x20; Call(c, mem, Fn.PrepareB, 0x800415CCu);
        if ((short)mem.ReadU16(SkyFlag) == 1 && (short)mem.ReadU16(SkyCount) > 0)
        {
            uint rec = mem.ReadU32(SkyCopyPtr);
            if (rec != 0)
                SubmitSky(c, mem, sp, 0x8004164Cu,
                    a0: (mem.ReadU16(rec + 6u) - 0xDDu) & 0xFFFFu,
                    a1: rec + 0x24u, pos: rec + 0x34u, a3: mem.ReadU8(rec + 1u),
                    s10: mem.ReadU16(rec + 0xAu), s14: mem.ReadU8(rec + 0x3Cu),
                    s18: mem.ReadU8(rec + 0x3Bu), s1c: 0x1FFFu - mem.ReadU8(rec + 0x3Au));
            mem.WriteU8(rec + 3u, (byte)(mem.ReadU8(rec + 3u) | 0x80u));
        }

        RunEffects(c, mem, sp);
        RunBillboards(c, mem, sp);

        c.RA = mem.ReadU32(sp + 0x8Cu);
        c.FP = mem.ReadU32(sp + 0x88u);
        c.S7 = mem.ReadU32(sp + 0x84u);
        c.S6 = mem.ReadU32(sp + 0x80u);
        c.S5 = mem.ReadU32(sp + 0x7Cu);
        c.S4 = mem.ReadU32(sp + 0x78u);
        c.S3 = mem.ReadU32(sp + 0x74u);
        c.S2 = mem.ReadU32(sp + 0x70u);
        c.S1 = mem.ReadU32(sp + 0x6Cu);
        c.S0 = mem.ReadU32(sp + 0x68u);
        c.SP = sp + frame;
        RenderDistance.InWalk = false;
        if (_carry) ModelSmoothing.ProbeFrame();
    }

    // ---- creatures: 200 x 0x88 at 0x80185DA8, live u8[+9]==1 -------------------

    static void RunCreatures(CpuContext c, PSMemory mem, uint sp)
    {
        uint s2 = Creatures, s0 = s2 + 3u, s3 = s2 + 0x2Cu;
        int s7 = 199;
        bool far;
    L80040B9C:
        Interrupts.Poll(c, mem);
        if (mem.ReadU8(s0 + 6u) != 1u) goto L80040D9C;
        {
            uint flags = mem.ReadU32(s0 + 0x25u);
            uint s1 = (flags & 0x2000u) != 0u ? mem.ReadU8(s0) | 0x10u : mem.ReadU8(s0);
            if ((mem.ReadU32(s0 + 0x25u) & 0x80000u) != 0u)
            {
                Query(c, mem, s3, 3, 0x80040D88u);
                s1 = mem.ReadU8(s0);
            }
            else Query(c, mem, s3, -1, 0x80040BECu);
            if ((c.V0 & s1) == 0u) goto L80040D9C;
            // Past the game's reach: drawn only with its pages loaded, and marking none.
            far = (_game & s1) == 0u;
            if (far)
            {
                uint def = CreatureDefs + (uint)mem.ReadU8(s0 - 1u) * 120u;
                if (!PageLoaded(mem, 0x33u, mem.ReadU8(def + 7u), 0x60u) || !PageLoaded(mem, 0x33u, mem.ReadU8(def + 8u), 0x60u))
                {
                    RenderDistance.ModelRefused();
                    goto L80040D9C;
                }
            }
        }
        Interrupts.Poll(c, mem);
        c.A0 = mem.ReadU8(s0 - 2u) + 0x80u;
        c.A1 = sp + 0x48u;
        Call(c, mem, Fn.Resident, 0x80040C08u);
        if (mem.ReadU32(sp + 0x48u) == 0u) goto L80040D2C;
        c.A0 = s2; c.A1 = sp + 0x38u;
        Call(c, mem, Fn.Place, 0x80040C20u);
        if (_mode == Mode.On)
            RenderDistance.ModelFadeOut = RenderDistance.CreatureFadeOut(mem, s2, CreatureDefs + mem.ReadU8(s2 + 2u) * 120u);
        if ((mem.ReadU32(s0 + 0x25u) & 0x20u) != 0u)
        {
            // Placed at its own record with a fixed matrix and no rotation.
            mem.WriteU16(Pad + 0x118u, 0);
            mem.WriteU16(Pad + 0x116u, 0);
            mem.WriteU16(Pad + 0x114u, 0);
            Record(0, s2, Creatures, CreatureStride, mem.ReadU8(s2 + 1u) | (uint)mem.ReadU8(s2 + 2u) << 8);
            SubmitWorld(c, mem, sp, 0x80040D2Cu,
                a0: mem.ReadU8(s0), a1: mem.ReadU8(s0 - 2u) + 0x80u, pos: s3, rot: Pad + 0x114u,
                s10: s2 + 0x48u, s14: s2 + 0x5Cu, matrix: RomMatrix,
                s1c: mem.ReadU8(s0 + 9u), s20: mem.ReadU16(s0 + 0x15u), s24: mem.ReadU8(s0 + 0x11u),
                s28: (short)mem.ReadU16(s0 + 0x13u), s2c: mem.ReadU8(s0 + 0x10u),
                s30: (sbyte)mem.ReadU8(s0 + 0x12u));
        }
        else
        {
            // The placement output, sp+0x38, is the position.
            uint pos = c.V0;
            mem.WriteU16(Pad + 0x114u, mem.ReadU16(s0 + 0x3Du));
            mem.WriteU16(Pad + 0x116u, (ushort)(mem.ReadU16(s0 + 0x3Fu) + 0x800u));
            mem.WriteU16(Pad + 0x118u, mem.ReadU16(s0 + 0x41u));
            Record(0, s2, Creatures, CreatureStride, mem.ReadU8(s2 + 1u) | (uint)mem.ReadU8(s2 + 2u) << 8);
            SubmitWorld(c, mem, sp, 0x80040D2Cu,
                a0: mem.ReadU8(s0), a1: mem.ReadU8(s0 - 2u) + 0x80u, pos: pos, rot: Pad + 0x114u,
                s10: s2 + 0x48u, s14: s2 + 0x5Cu, matrix: ViewMatrix,
                s1c: mem.ReadU8(s0 + 9u), s20: mem.ReadU16(s0 + 0x15u), s24: mem.ReadU8(s0 + 0x11u),
                s28: (short)mem.ReadU16(s0 + 0x13u), s2c: mem.ReadU8(s0 + 0x10u),
                s30: (sbyte)mem.ReadU8(s0 + 0x12u));
        }
    L80040D2C:
        RenderDistance.ModelFadeOut = 0;
        if (!far)
        {
            uint def = CreatureDefs + (uint)mem.ReadU8(s0 - 1u) * 120u;
            mem.WriteU8(Pad + 0x270u + mem.ReadU8(def + 7u), 1);
            mem.WriteU8(Pad + 0x270u + mem.ReadU8(def + 8u), 1);
            mem.WriteU8(Pad + 0x124u + mem.ReadU8(s0 - 2u), 1);
        }
    L80040D9C:
        s0 += CreatureStride; s3 += CreatureStride; s7--;
        if (s7 != -1) { s2 += CreatureStride; goto L80040B9C; }
    }

    // ---- objects: 396 x 0x44 at 0x80191A5C, live u16[+6] != 0xFF --------------

    static void RunObjects(CpuContext c, PSMemory mem, uint sp, uint soundClock)
    {
        uint s4 = Objects, s0 = s4 + 3u, s6 = s4 + 0x14u, fp = s4 + 0x34u, s1 = 0, s3 = 0, seen = 0, t0 = 0, kind;
        int s7 = ObjectCount - 1;
        bool far = false;
        mem.WriteU16(Pad + 0x84u, 0);
    L80040E3C:
        Interrupts.Poll(c, mem);
        if (mem.ReadU16(s0 + 3u) == 0xFFu) goto L80041514;
        kind = mem.ReadU8(s0 + 1u);
        mem.WriteU8(s0, (byte)(mem.ReadU8(s0) & 0x7Fu));
        if (kind == 0xE9u) goto L80041514;
        if (kind >= 0xEAu) goto L80040E88;
        if (kind == 0x1Fu) goto L800410A8;
        if (kind == 0xE5u) goto L80041514;
        goto L800412AC;

    L80040E88:
        if (kind == 0xF0u) goto L80040FAC;
        if (kind != 0xF2u) goto L800412AC;

        // Kind 0xF2: a placed model through the front table.
        c.A1 = mem.ReadU8(s0 + 0x35u); c.A2 = mem.ReadU8(s0 + 0x36u); c.A0 = s6;
        Call(c, mem, Fn.SkyQuery, 0x80040EACu);
        if (c.V0 == 0u) goto L80041514;
        mem.WriteU8(Pad + 0x124u + mem.ReadU16(s0 + 3u), 1);
        c.A0 = mem.ReadU16(s0 + 3u);
        s3 = s4 + 0x2Cu;
        Call(c, mem, Fn.ModelDef, 0x80040ED0u);
        s1 = c.V0 & 0xFFFFu;
        c.A0 = s1; c.A1 = sp + 0x48u;
        Call(c, mem, Fn.Resident, 0x80040EE0u);
        if (mem.ReadU32(sp + 0x48u) == 0u) goto L80041514;
        c.A1 = s1;
        mem.WriteU16(Pad + 0x114u, mem.ReadU16(s0 + 0x21u));
        mem.WriteU16(Pad + 0x116u, (ushort)(mem.ReadU16(s0 + 0x23u) + 0x800u));
        mem.WriteU16(Pad + 0x118u, mem.ReadU16(s0 + 0x25u));
        t0 = mem.ReadU8(s0 - 1u);
        {
            uint v = (uint)(short)mem.ReadU16(s0 + 0x37u);
            mem.WriteU32(sp + 0x38u, (uint)(((int)v << 11) + (int)mem.ReadU32(s0 + 0x11u)) >> 1);
            v = (uint)(short)mem.ReadU16(s0 + 0x39u);
            mem.WriteU32(sp + 0x40u, (uint)(((int)v << 11) + (int)mem.ReadU32(s0 + 0x19u)) >> 1);
            v = mem.ReadU8(s0 + 0x3Bu);
            mem.WriteU32(sp + 0x3Cu, (uint)((int)mem.ReadU32(s0 + 0x15u) - ((int)v << 11)) >> 1);
        }
        Record(1, s4, Objects, ObjectStride, mem.ReadU16(s4 + 6u) | (uint)mem.ReadU8(s4 + 4u) << 16);
        SubmitFront(c, mem, sp, 0x80041448u,
            a0: mem.ReadU8(s4), a1: s1, pos: sp + 0x38u, rot: Pad + 0x114u,
            s10: s3, s14: fp, matrix: ViewMatrix,
            s1c: mem.ReadU8(s0 - 2u), s20: mem.ReadU16(s0 + 7u), s24: mem.ReadU8(s0 + 2u),
            s28: (short)mem.ReadU16(s0 + 0xDu), s2c: t0, s30: (short)mem.ReadU16(s0 + 0xBu));
        goto L800414AC;

    L80040FAC:
        // Kind 0xF0: the sky.
        if (mem.ReadU8(SkyGate) == 0u) goto L80041514;
        c.A1 = mem.ReadU8(s0 + 0x35u); c.A2 = mem.ReadU8(s0 + 0x36u); c.A0 = s6;
        Call(c, mem, Fn.SkyQuery, 0x80040FD0u);
        if (c.V0 == 0u) goto L80041514;
        c.A1 = sp + 0x48u;
        s1 = (mem.ReadU16(s0 + 3u) - 0xDDu) & 0xFFFFu;
        c.A0 = s1;
        Call(c, mem, Fn.Resident, 0x80040FF0u);
        if (mem.ReadU32(sp + 0x48u) == 0u) goto L8004104C;
        SubmitSky(c, mem, sp, 0x8004103Cu,
            a0: s1, a1: s4 + 0x24u, pos: fp, a3: mem.ReadU8(s0 - 2u),
            s10: mem.ReadU16(s0 + 7u), s14: mem.ReadU8(s0 + 0x39u),
            s18: mem.ReadU8(s0 + 0x38u), s1c: 0x1FFFu - mem.ReadU8(s0 + 0x37u));
        mem.WriteU8(s0, (byte)(mem.ReadU8(s0) | 0x80u));
    L8004104C:
        {
            uint dst = SkyCopy, src = s4;
            uint end = s4 + 0x40u;
            do
            {
                Interrupts.Poll(c, mem);
                mem.WriteU32(dst, mem.ReadU32(src));
                mem.WriteU32(dst + 4u, mem.ReadU32(src + 4u));
                mem.WriteU32(dst + 8u, mem.ReadU32(src + 8u));
                mem.WriteU32(dst + 0xCu, mem.ReadU32(src + 0xCu));
                src += 0x10u;
                if (src != end) dst += 0x10u;
            } while (src != end);
            dst += 0x10u;
            mem.WriteU32(dst, mem.ReadU32(src));
            mem.WriteU32(SkyCopyPtr, SkyCopy);
        }
        goto L80041514;

    L800410A8:
        // Kind 0x1F: an ambient sound source; it draws nothing.
        c.A0 = (uint)((int)mem.ReadU32(s0 + 0x11u) >> 11);
        c.A1 = (uint)((int)mem.ReadU32(s0 + 0x19u) >> 11);
        c.A2 = mem.ReadU8(s0 + 0x35u); c.A3 = mem.ReadU8(s0 + 0x36u);
        mem.WriteU32(sp + 0x10u, 0x8000u);
        Call(c, mem, Fn.Ambient, 0x800410CCu);
        if (c.V0 == 0u) goto L8004128C;
        {
            uint v1 = (uint)(short)mem.ReadU16(Ambients + (uint)mem.ReadU8(s0 + 0x37u) * 10u);
            if (v1 - 0x93u < 0x20u)
                mem.WriteU8(Pad + (uint)(int)(short)(ushort)v1 + 0x1DDu, 1);
        }
        if ((int)(mem.ReadU32(s0 + 0x3Du) - soundClock) >= 0) goto L80041514;
        {
            uint interval = mem.ReadU16(s0 + 0x3Bu);
            mem.WriteU32(s0 + 0x3Du, soundClock + interval * 6u);
            int a1 = (int)mem.ReadU8(s0 + 0x35u) << 10;
            int a0 = (int)mem.ReadU32(PlayerX) - (a1 + (int)mem.ReadU32(s0 + 0x11u));
            if (a0 < 0) a0 = -a0;
            a0 = a1 - a0;
            int v1 = (int)mem.ReadU8(s0 + 0x36u) << 10;
            int v2 = (int)mem.ReadU32(PlayerZ) - (v1 + (int)mem.ReadU32(s0 + 0x19u));
            if (v2 < 0) v2 = -v2;
            v2 = v1 - v2;
            if (v2 < a0) a0 = v2;
            a1 = (int)mem.ReadU8(s0 + 0x39u) << 11;
            uint v0;
            if (a0 >= a1) { a1 = mem.ReadU8(s0 + 0x38u); goto L80041220; }
            if (a1 == 0) goto L80041514;
            MultS(c, mem.ReadU8(s0 + 0x38u), (uint)a0);
            DivS(c, c.LO, (uint)a1);
            a1 = (int)c.LO;
        L80041220:
            if ((mem.ReadU8(s0 + 0x3Au) & 1u) == 0u) { v0 = (uint)(a1 < 20 ? 1 : 0); goto L80041270; }
            {
                int dy = (int)mem.ReadU32(PlayerY) - (int)mem.ReadU32(s0 + 0x15u);
                if (dy < 0) dy = -dy;
                MultS(c, mem.ReadU8(s0 + 0x38u), (uint)dy);
                a1 -= (int)c.LO >> 13;
                v0 = (uint)(a1 < 20 ? 1 : 0);
            }
        L80041270:
            if (v0 != 0u) goto L80041514;
            c.A0 = mem.ReadU8(s0 + 0x37u) + 0x1F0u;
            Call(c, mem, Fn.Sound, 0x80041284u);
            s0 += ObjectStride;
            goto L80041518;
        }
    L8004128C:
        {
            uint interval = mem.ReadU16(s0 + 0x3Bu);
            mem.WriteU32(s0 + 0x3Du, soundClock + interval * 6u);
            goto L80041514;
        }

    L800412AC:
        if ((mem.ReadU32(s4) & 0x0A000000u) == 0x02000000u) goto L800414C0;
        Query(c, mem, s6, -1, 0x800412CCu);
        seen = c.V0;
        far = (_game & mem.ReadU8(s4)) == 0u && (mem.ReadU8(s0) & 8u) == 0u;
        if ((seen & mem.ReadU8(s4)) != 0u) goto L800412F4;
        if ((mem.ReadU8(s0) & 8u) == 0u) goto L80041514;
    L800412F4:
        s1 = ObjectDefs + (uint)mem.ReadU16(s0 + 3u) * 24u;
    L80041314:
        Interrupts.Poll(c, mem);
        if (far)
        {
            // Past the game's reach: drawn only with its page loaded, and marking none.
            if (!PageLoaded(mem, 0x93u, mem.ReadU8(s1 + 2u), 0x20u)) { RenderDistance.ModelRefused(); goto L80041514; }
        }
        else
        {
            mem.WriteU8(Pad + 0x124u + mem.ReadU16(s0 + 3u), 1);
            mem.WriteU8(Pad + 0x270u + mem.ReadU8(s1 + 2u), 1);
        }
        c.A0 = mem.ReadU16(s0 + 3u);
        s3 = s4 + 0x2Cu;
        Call(c, mem, Fn.ModelDef, 0x80041340u);
        s1 = c.V0;
        c.A0 = s1 & 0xFFFFu; c.A1 = sp + 0x48u;
        Call(c, mem, Fn.Resident, 0x80041350u);
        if (mem.ReadU32(sp + 0x48u) == 0u) goto L80041514;
        mem.WriteU16(Pad + 0x114u, mem.ReadU16(s0 + 0x21u));
        mem.WriteU16(Pad + 0x116u, (ushort)(mem.ReadU16(s0 + 0x23u) + 0x800u));
        mem.WriteU16(Pad + 0x118u, mem.ReadU16(s0 + 0x25u));
        t0 = mem.ReadU8(s0 - 1u);
        {
            uint v1c = mem.ReadU8(s0);
            if ((v1c & 1u) != 0u) t0 = (seen & 4u) != 0u ? t0 | 0x40u : t0 & 0xBFu;
            Record(1, s4, Objects, ObjectStride, mem.ReadU16(s4 + 6u) | (uint)mem.ReadU8(s4 + 4u) << 16);
            if ((v1c & 0x40u) != 0u)
            {
                mem.WriteU16(Pad + 0x118u, 0);
                mem.WriteU16(Pad + 0x116u, 0);
                mem.WriteU16(Pad + 0x114u, 0);
                SubmitWorld(c, mem, sp, 0x800414ACu,
                    a0: mem.ReadU8(s4), a1: s1 & 0xFFFFu, pos: s6, rot: Pad + 0x114u,
                    s10: s3, s14: fp, matrix: PitchMatrix,
                    s1c: mem.ReadU8(s0 - 2u), s20: mem.ReadU16(s0 + 7u), s24: mem.ReadU8(s0 + 2u),
                    s28: (short)mem.ReadU16(s0 + 0xDu), s2c: t0, s30: (short)mem.ReadU16(s0 + 0xBu));
            }
            else if ((v1c & 8u) == 0u)
            {
                SubmitWorld(c, mem, sp, 0x800414ACu,
                    a0: mem.ReadU8(s4), a1: s1 & 0xFFFFu, pos: s6, rot: Pad + 0x114u,
                    s10: s3, s14: fp, matrix: ViewMatrix,
                    s1c: mem.ReadU8(s0 - 2u), s20: mem.ReadU16(s0 + 7u), s24: mem.ReadU8(s0 + 2u),
                    s28: (short)mem.ReadU16(s0 + 0xDu), s2c: t0, s30: (short)mem.ReadU16(s0 + 0xBu));
            }
            else
            {
                SubmitFront(c, mem, sp, 0x80041448u,
                    a0: mem.ReadU8(s4), a1: s1 & 0xFFFFu, pos: s6, rot: Pad + 0x114u,
                    s10: s3, s14: fp, matrix: ViewMatrix,
                    s1c: mem.ReadU8(s0 - 2u), s20: mem.ReadU16(s0 + 7u), s24: mem.ReadU8(s0 + 2u),
                    s28: (short)mem.ReadU16(s0 + 0xDu), s2c: t0, s30: (short)mem.ReadU16(s0 + 0xBu));
            }
        }
    L800414AC:
        mem.WriteU8(s0, (byte)(mem.ReadU8(s0) | 0x80u));
        goto L80041514;

    L800414C0:
        s1 = ObjectDefs + (uint)mem.ReadU16(s0 + 3u) * 24u;
        Query(c, mem, s6, mem.ReadU8(s1 + 0xCu), 0x800414ECu);
        seen = c.V0;
        far = (_game & mem.ReadU8(s4)) == 0u && (mem.ReadU8(s0) & 8u) == 0u;
        if ((seen & mem.ReadU8(s4)) != 0u) goto L80041314;
        if ((mem.ReadU8(s0) & 8u) == 0u) goto L80041514;
        goto L80041314;

    L80041514:
        s0 += ObjectStride;
    L80041518:
        s6 += ObjectStride; fp += ObjectStride; s7--;
        if (s7 != -1) { s4 += ObjectStride; goto L80040E3C; }
        s4 += ObjectStride;
    }

    // ---- effects: 128 x 0x4C at 0x801B80EC, live u8[+0] != 0xFF ----------------

    static void RunEffects(CpuContext c, PSMemory mem, uint sp)
    {
        uint s4 = Effects, s0 = s4 + 9u, s3 = s4 + 0x40u, s2 = s4 + 0x30u, s6 = s4 + 0x28u, s1 = s4 + 0x18u;
        int s7 = 127;
    L8004167C:
        Interrupts.Poll(c, mem);
        if (mem.ReadU8(s4) == 0xFFu) goto L800418C0;
        {
            uint v1 = mem.ReadU8(s0 - 1u) & 3u;
            if (v1 == 0u) goto L800418C0;
            if (v1 != 2u)
            {
                Query(c, mem, s1, -1, 0x800416B0u);
                if ((c.V0 & mem.ReadU8(s0 + 1u)) == 0u) goto L800418C0;
            }
        }
        mem.WriteU16(Pad + 0x84u, mem.ReadU16(s0 + 5u));
        Record(2, s4, Effects, EffectStride, mem.ReadU8(s4) | (uint)mem.ReadU8(s4 + 3u) << 8);
        {
            uint place = mem.ReadU8(s0 - 1u) & 0xCu;
            if (place == 0u) goto L8004171C;
            if (place == 4u) goto L800417A4;
            if (place == 8u) goto L80041804;
            if (place == 0xCu) goto L80041864;
            goto L800418C0;
        }

    L8004171C:
        mem.WriteU16(Pad + 0x114u, mem.ReadU16(s0 + 0x1Fu));
        mem.WriteU16(Pad + 0x116u, (ushort)(mem.ReadU16(s0 + 0x21u) + 0x800u));
        mem.WriteU16(Pad + 0x118u, mem.ReadU16(s0 + 0x23u));
        SubmitWorld(c, mem, sp, 0x800418C0u,
            a0: mem.ReadU8(s0 + 1u), a1: mem.ReadU8(s0 - 6u) + 0x28u, pos: s1, rot: Pad + 0x114u,
            s10: s2, s14: s3, matrix: ViewMatrix,
            s1c: mem.ReadU8(s0 - 5u), s20: mem.ReadU16(s0 + 0xBu), s24: mem.ReadU8(s0 + 3u),
            s28: (short)mem.ReadU16(s0 + 9u), s2c: mem.ReadU8(s0), s30: -0x3C);
        goto L800418C0;
    L800417A4:
        SubmitWorld(c, mem, sp, 0x800418C0u,
            a0: mem.ReadU8(s0 + 1u), a1: mem.ReadU8(s0 - 6u) + 0x28u, pos: s1, rot: s6,
            s10: s2, s14: s3, matrix: RomMatrix,
            s1c: mem.ReadU8(s0 - 5u), s20: mem.ReadU16(s0 + 0xBu), s24: mem.ReadU8(s0 + 3u),
            s28: (short)mem.ReadU16(s0 + 9u), s2c: mem.ReadU8(s0), s30: -0x3C);
        goto L800418C0;
    L80041804:
        SubmitWorld(c, mem, sp, 0x800418C0u,
            a0: mem.ReadU8(s0 + 1u), a1: mem.ReadU8(s0 - 6u) + 0x28u, pos: s1, rot: s6,
            s10: s2, s14: s3, matrix: PitchMatrix,
            s1c: mem.ReadU8(s0 - 5u), s20: mem.ReadU16(s0 + 0xBu), s24: mem.ReadU8(s0 + 3u),
            s28: (short)mem.ReadU16(s0 + 9u), s2c: mem.ReadU8(s0), s30: -0x3C);
        goto L800418C0;
    L80041864:
        SubmitWorld(c, mem, sp, 0x800418C0u,
            a0: mem.ReadU8(s0 + 1u), a1: mem.ReadU8(s0 - 6u) + 0x28u, pos: s1, rot: s6,
            s10: s2, s14: s3, matrix: 0u,
            s1c: mem.ReadU8(s0 - 5u), s20: mem.ReadU16(s0 + 0xBu), s24: mem.ReadU8(s0 + 3u),
            s28: (short)mem.ReadU16(s0 + 9u), s2c: mem.ReadU8(s0), s30: 0x14);
        goto L800418C0;

    L800418C0:
        s0 += EffectStride; s3 += EffectStride; s2 += EffectStride; s6 += EffectStride; s1 += EffectStride;
        s7--;
        if (s7 != -1) { s4 += EffectStride; goto L8004167C; }
        s4 += EffectStride;
    }

    // ---- billboards: 128 x 0x18 at 0x80182968, live u16[+0] != 0xFFFF ----------

    static void RunBillboards(CpuContext c, PSMemory mem, uint sp)
    {
        uint s1 = Billboards, s0 = s1 + 5u, s2 = s1 + 8u, s3 = 0x1000u;
        int s7 = 127;
        // SpriteAnim's hold, made here when the walk is C#: a walk that is not the
        // tick's first neither steps the cels nor bumps the clock; nor does a frozen
        // pass (MenuWorld).
        bool step = !FramePacing.Frozen && (_mode != Mode.On || !SpriteAnim.Enabled || !FramePacing.Enabled
                    || FramePacing.FirstWalkOfTick(ref _billboardTick));
        mem.WriteU16(Pad + 0x118u, 0);
        mem.WriteU16(Pad + 0x116u, 0);
        mem.WriteU16(Pad + 0x114u, 0);
        mem.WriteU16(Pad + 0x84u, 0);
    L8004190C:
        Interrupts.Poll(c, mem);
        if (mem.ReadU16(s1) == 0xFFFFu) goto L80041A70;
        {
            uint a1 = mem.ReadU16(s1);
            Record(3, s1, Billboards, BillboardStride, a1);
            if ((a1 & 0x8000u) != 0u)
            {
                SubmitFront(c, mem, sp, 0x80041980u,
                    a0: mem.ReadU8(s0 - 3u), a1: ((a1 & 0x7FFFu) + 0x28u) & 0xFFFFu, pos: s2, rot: Pad + 0x114u,
                    s10: 0, s14: 0, matrix: RomMatrix, s1c: mem.ReadU8(s0) + 0x80u,
                    s20: 0, s24: 0x3C, s28: (int)s3, s2c: 7, s30: 0);
                goto L800419F4;
            }
            Query(c, mem, s2, -1, 0x80041990u);
            if ((c.V0 & mem.ReadU8(s0 - 3u)) == 0u) goto L800419F4;
            SubmitWorld(c, mem, sp, 0x800419F4u,
                a0: mem.ReadU8(s0 - 3u), a1: (mem.ReadU16(s1) + 0x28u) & 0xFFFFu, pos: s2, rot: Pad + 0x114u,
                s10: 0, s14: 0, matrix: ViewMatrix, s1c: mem.ReadU8(s0) + 0x80u,
                s20: 0, s24: 0x36, s28: (int)s3, s2c: 5, s30: 0);
        }
    L800419F4:
        if (!step) goto L80041A70;
        {
            uint interval = mem.ReadU8(s0 - 1u);
            if (interval == 0u) goto L80041A70;
            uint clock = mem.ReadU32(Clock);
            DivS(c, clock, interval);
            if (c.HI != 0u) goto L80041A70;
            uint cel = mem.ReadU8(s0) + 1u;
            mem.WriteU8(s0, (byte)cel);
            if (cel < mem.ReadU8(s0 - 2u)) goto L80041A70;
            mem.WriteU8(s0, 0);
        }
    L80041A70:
        s0 += BillboardStride; s2 += BillboardStride; s7--;
        if (s7 != -1) { s1 += BillboardStride; goto L8004190C; }
        s1 += BillboardStride;
        if (step) mem.WriteU32(Clock, mem.ReadU32(Clock) + 1u);
    }

    // ---- the submits: one method each, carrying pos/rotation at the seams ------

    /// <summary><c>func_8003E34C(mask, model, pos, rot, ...)</c>, the world submitter.
    /// Nine stack words, sp+0x10..0x30, as the walk writes them.</summary>
    static void SubmitWorld(CpuContext c, PSMemory mem, uint sp, uint ra,
        uint a0, uint a1, uint pos, uint rot,
        uint s10, uint s14, uint matrix, uint s1c, uint s20, uint s24, int s28, uint s2c, int s30)
    {
        c.A0 = a0; c.A1 = a1;
        bool carried = Carry(mem, sp, ref pos, ref rot);
        c.A2 = pos;
        c.A3 = rot;
        mem.WriteU32(sp + 0x10u, s10);
        mem.WriteU32(sp + 0x14u, s14);
        mem.WriteU32(sp + 0x18u, matrix);
        mem.WriteU32(sp + 0x1Cu, s1c);
        mem.WriteU32(sp + 0x20u, s20);
        mem.WriteU32(sp + 0x24u, s24);
        mem.WriteU32(sp + 0x28u, (uint)s28);
        mem.WriteU32(sp + 0x2Cu, s2c);
        mem.WriteU32(sp + 0x30u, (uint)s30);
        c.RA = ra;
        _callees![(int)Fn.SubmitWorld](c, mem);
        if (carried) ModelSmoothing.Leave();
    }

    /// <summary><c>func_8003F304(mask, model, pos, rot, ...)</c>, the front-table
    /// submitter, same stack shape.</summary>
    static void SubmitFront(CpuContext c, PSMemory mem, uint sp, uint ra,
        uint a0, uint a1, uint pos, uint rot,
        uint s10, uint s14, uint matrix, uint s1c, uint s20, uint s24, int s28, uint s2c, int s30)
    {
        c.A0 = a0; c.A1 = a1;
        bool carried = Carry(mem, sp, ref pos, ref rot);
        c.A2 = pos;
        c.A3 = rot;
        mem.WriteU32(sp + 0x10u, s10);
        mem.WriteU32(sp + 0x14u, s14);
        mem.WriteU32(sp + 0x18u, matrix);
        mem.WriteU32(sp + 0x1Cu, s1c);
        mem.WriteU32(sp + 0x20u, s20);
        mem.WriteU32(sp + 0x24u, s24);
        mem.WriteU32(sp + 0x28u, (uint)s28);
        mem.WriteU32(sp + 0x2Cu, s2c);
        mem.WriteU32(sp + 0x30u, (uint)s30);
        c.RA = ra;
        _callees![(int)Fn.SubmitFront](c, mem);
        if (carried) ModelSmoothing.Leave();
    }

    /// <summary>The record the next submit draws; the walk's own bookkeeping only.</summary>
    static void Record(int table, uint rec, uint baseAddr, uint stride, uint ident)
    {
        _table = table;
        _rec = rec;
        _ident = ident;
        _slot = (int)((rec - baseAddr) / stride);
    }
    static int _slot;

    /// <summary>Hand the submitter the record's position and rotation interpolated
    /// between its last two ticks, as copies in the walk's frame: the record and
    /// the scratchpad lane keep the tick's values. False when nothing is carried.</summary>
    static bool Carry(PSMemory mem, uint sp, ref uint pos, ref uint rot)
    {
        int table = _table;
        _table = -1;
        if (!_carry || table < 0) return false;

        int x = (int)mem.ReadU32(pos), y = (int)mem.ReadU32(pos + 4u), z = (int)mem.ReadU32(pos + 8u);
        short p = (short)mem.ReadU16(rot), w = (short)mem.ReadU16(rot + 2u), r = (short)mem.ReadU16(rot + 4u);
        ModelSmoothing.Carry(table, _slot, _ident, ref x, ref y, ref z, ref p, ref w, ref r);

        uint cp = sp + CarriedPos, cr = sp + CarriedRot;
        mem.WriteU32(cp, (uint)x);
        mem.WriteU32(cp + 4u, (uint)y);
        mem.WriteU32(cp + 8u, (uint)z);
        mem.WriteU32(cp + 12u, mem.ReadU32(pos + 12u));
        mem.WriteU16(cr, (ushort)p);
        mem.WriteU16(cr + 2u, (ushort)w);
        mem.WriteU16(cr + 4u, (ushort)r);
        mem.WriteU16(cr + 6u, mem.ReadU16(rot + 6u));
        pos = cp;
        rot = cr;
        ModelSmoothing.Enter(table, _slot);
        return true;
    }

    /// <summary><c>func_800400AC(model, light, pos, sel, uv, a, b, bias)</c>, the sky.</summary>
    static void SubmitSky(CpuContext c, PSMemory mem, uint sp, uint ra,
        uint a0, uint a1, uint pos, uint a3, uint s10, uint s14, uint s18, uint s1c)
    {
        c.A0 = a0; c.A1 = a1;
        // carry seam: `pos` (a2) is the position the sky is drawn at.
        c.A2 = pos;
        c.A3 = a3;
        mem.WriteU32(sp + 0x10u, s10);
        mem.WriteU32(sp + 0x14u, s14);
        mem.WriteU32(sp + 0x18u, s18);
        mem.WriteU32(sp + 0x1Cu, s1c);
        c.RA = ra;
        _callees![(int)Fn.Sky](c, mem);
    }

    /// <summary>A signed 32x32 multiply into LO and HI.</summary>
    static void MultS(CpuContext c, uint a, uint b)
    {
        long r = (long)(int)a * (int)b;
        c.LO = (uint)r;
        c.HI = (uint)(r >> 32);
    }

    /// <summary>A signed divide into LO and HI; MIPS leaves them for a zero
    /// divisor and clamps the one overflowing pair.</summary>
    static void DivS(CpuContext c, uint a, uint b)
    {
        if (b == 0u) return;
        int s = (int)a, t = (int)b;
        if (s == int.MinValue && t == -1) { c.LO = 0x80000000u; c.HI = 0u; }
        else { c.LO = (uint)(s / t); c.HI = (uint)(s % t); }
    }
}
