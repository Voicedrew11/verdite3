using System.Reflection;
using RecompOne.Runtime;
using RecompOne.Runtime.Context;
using RecompOne.Runtime.Memory;
using RecompOne.Runtime.Modding;

namespace Kf3;

/// <summary>
/// <c>func_800431E8(slot, bank, clip, time)</c>, the MO pose blender, in C#. It keeps a
/// 0x50-byte frame; a rigid bank (whose record's word at <c>+0x4</c> is 0) just publishes
/// the model's mesh into the scratchpad and returns 1, while an MO bank walks the clip
/// through the clock <c>func_80042CAC</c>, rebuilds the slot's keyframe when the clip or
/// segment moved, copies it to the posed buffer at <see cref="Posed"/> and decodes the
/// segment's deltas there at the published 12.12 weight (<c>func_80042EB0</c>). When clip
/// and segment are unchanged the keyframe is not rebuilt but the copy and decode still
/// run, so a different weight moves the mesh (Verdite2's <c>L80034FCC</c>).
///
///     KF3_MOPOSE=1        this transcription (the default)
///     KF3_MOPOSE=0        the recompiled routine
///     KF3_MOPOSE=verify   run both on every call from one state and compare (RAM, the
///                         scratchpad, the callee-saved registers and the GTE)
///
/// <see cref="ClipCarry"/> lets a caller hand the clock a fractional time: it returns a
/// floored time and a fraction, and the fraction is spent on the weight the clock
/// published, so a pose can be drawn between two world ticks. Null by default and never
/// used in verify mode.
/// </summary>
public static class MoPose
{
    const uint Routine = 0x800431E8;

    /// <summary>The bank pointer table: <c>mem[base + bank*4]</c> is the bank record.</summary>
    public const uint BankTable = 0x801A92B0;

    /// <summary>The posed 8-byte vertices <c>func_80042EB0</c> decodes into.</summary>
    public const uint Posed = 0x801B3468;

    /// <summary>Hands the clock a floored time and a fraction; true to carry.</summary>
    public delegate bool ClipCarryFn(IMemory m, uint bank, uint clip, int time,
                                     out int floorTime, out double frac);

    /// <summary>Set to carry a pose between ticks; null is the game's own time.</summary>
    public static ClipCarryFn? ClipCarry;

    /// <summary>Whether the C# blender runs (mode on), so a carry can be asked of it.</summary>
    public static bool InCSharp => _mode == Mode.On;

    enum Mode { Off, On, Verify }
    static Mode _mode = Mode.On;

    /// <summary>The mode as the Testing tab sets it: 0 recompiled, 1 C#, 2 verify.</summary>
    public static int Setting { get => (int)_mode; set => _mode = (Mode)Math.Clamp(value, 0, 2); }
    static bool _queued;
    static Action<CpuContext, IMemory>[]? _callees;

    static readonly Differential _check = new("mopose", "func_800431E8", 0x800);

    static readonly ModInfo _self = new()
    {
        Id = "kf3.mopose",
        Name = "MO pose blender",
        Version = "1.0",
        Description = "func_800431E8, the MO pose blender, in C#.",
    };

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
        HookAttach.OnOverlayLoad("mo pose", Attach);
    }

    static bool Attach()
    {
        SymbolRegistry.Build();
        var target = SymbolRegistry.Resolve("game", null, Routine);
        if (target == null) return false;
        if (!Bind()) return false;
        if (!_queued)
        {
            var impl = typeof(MoPose).GetMethod(nameof(Replace),
                BindingFlags.NonPublic | BindingFlags.Static)!;
            _queued = HookManager.AddReplace(_self, target, impl);
            if (!_queued) return false;
        }
        HookManager.Commit();
        bool ok = HookAttach.Installed(target);
        Console.WriteLine(ok ? $"[KF3] mo pose: {_mode.ToString().ToLowerInvariant()}"
                             : "[KF3] mo pose: not installed");
        return ok;
    }

    /// <summary>A delegate per callee, so every hook on it still fires.</summary>
    static bool Bind()
    {
        if (_callees != null) return true;
        uint[] callees =
        [
            0x80043894,   // free a slot record (0)
            0x800439B0,   // take a free slot record (1)
            0x800194B0,   // allocate the slot's vertex buffer (2)
            0x800438E0,   // scan the record pool after a failed allocation (3)
            0x80042CAC,   // the clip clock (4)
            0x80042D70,   // keyframe copy, first (5)
            0x80042E34,   // keyframe copy, the rest (6)
            0x80042EB0,   // the delta decoder (7)
        ];
        var fns = new Action<CpuContext, IMemory>[callees.Length];
        for (int i = 0; i < callees.Length; i++)
        {
            var mi = SymbolRegistry.Resolve("game", null, callees[i]);
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
        if (_mode == Mode.Verify) _check.Run(orig, c, mem, (cc, mm) => Run(cc, mm, false));
        else Run(c, mem, true);
    }

    static void Call(CpuContext c, PSMemory mem, int which, uint ra)
    {
        c.RA = ra;
        _callees![which](c, mem);
    }

    /// <summary>The routine, transcribed. <paramref name="carry"/>, false in verify mode,
    /// lets <see cref="ClipCarry"/> shift the clock's weight by a tick fraction.</summary>
    static void Run(CpuContext c, PSMemory mem, bool carry)
    {
        uint sp = c.SP - 0x50u;
        c.SP = sp;
        mem.WriteU32(sp + 0x30u, c.S2);
        c.S2 = c.A0;                                   // slot record pointer
        mem.WriteU32(sp + 0x28u, c.S0);
        c.S0 = c.A1;                                   // bank index
        mem.WriteU32(sp + 0x40u, c.S6);
        c.S6 = c.A2;                                   // clip byte
        c.V0 = 0x801B0000u;
        c.V0 = c.V0 - 0x6D50u;                         // 0x801A92B0
        c.V1 = c.S0 << 2;
        mem.WriteU32(sp + 0x2Cu, c.S1);
        c.S1 = c.V1 + c.V0;                            // &BankTable[bank]
        mem.WriteU32(sp + 0x4Cu, c.RA);
        mem.WriteU32(sp + 0x48u, c.FP);
        mem.WriteU32(sp + 0x44u, c.S7);
        mem.WriteU32(sp + 0x3Cu, c.S5);
        mem.WriteU32(sp + 0x38u, c.S4);
        mem.WriteU32(sp + 0x34u, c.S3);
        c.A0 = mem.ReadU32(c.S2);                       // the slot's record, or 0
        c.FP = mem.ReadU32(sp + 0x60u);                 // the caller's +0x10 word (the vertex count)
        c.S3 = mem.ReadU32(c.S1);                       // the bank record
        c.S4 = c.A3;                                    // the clip time
        mem.WriteU32(sp + 0x18u, c.A0);
        c.V0 = mem.ReadU32(c.S3 + 0x4u);
        c.S7 = 0x1F800000u;                             // the scratchpad base, both paths
        if (c.V0 != 0u) goto L432AC;

        // A rigid bank: the record (if any) is let go and the mesh is published as is.
        if (c.A0 == 0u) goto L43264;
        Call(c, mem, 0, 0x80043264u);
        L43264: ;
        c.A0 = mem.ReadU32(c.S1);
        c.V0 = mem.ReadU32(c.A0 + 0x8u);
        c.A0 = c.A0 + c.V0;
        c.V0 = c.A0 + 0xCu;
        c.At = 0x1F800000u;
        mem.WriteU32(c.At + 0x10u, c.A0);
        c.At = 0x1F800000u;
        mem.WriteU32(c.At + 0x24u, c.V0);
        c.V1 = mem.ReadU32(c.A0 + 0xCu);
        c.V1 = c.V1 + 0xCu;
        c.V1 = c.V1 + c.A0;
        c.At = 0x1F800000u;
        mem.WriteU32(c.At + 0x4Cu, c.V1);
        c.V0 = 1u;
        goto L434F8;

        L432AC: ;
        if (c.A0 != 0u) goto L43310;

        // No record for this slot yet: take one and allocate its vertex buffer.
        c.A0 = sp + 0x18u;
        Call(c, mem, 1, 0x800432BCu);
        c.V0 = mem.ReadU32(sp + 0x18u);
        if (c.V0 != 0u) goto L432D8;
        c.V0 = 0u;
        goto L434F8;

        L432D4: ;
        Interrupts.Poll(c, mem);
        c.V0 = mem.ReadU32(sp + 0x18u);
        L432D8: ;
        mem.WriteU16(c.V0 + 0x2u, (ushort)c.S0);
        mem.WriteU32(c.V0 + 0x10u, c.S2);
        L432E4: ;
        Interrupts.Poll(c, mem);
        c.A0 = c.FP << 3;
        Call(c, mem, 2, 0x800432ECu);
        c.V1 = mem.ReadU32(sp + 0x18u);
        if (c.V0 != 0u) { mem.WriteU32(c.V1 + 0xCu, c.V0); goto L43308; }
        mem.WriteU32(c.V1 + 0xCu, c.V0);
        Call(c, mem, 3, 0x80043300u);
        goto L432E4;

        L43308: ;
        mem.WriteU32(c.S2, c.V1);
        goto L43338;

        L43310: ;
        c.V0 = mem.ReadU16(c.A0 + 0x2u);                // the record's bank
        if (c.V0 == c.S0) goto L43338;
        Call(c, mem, 0, 0x80043328u);
        c.V1 = mem.ReadU32(sp + 0x18u);
        c.V0 = 0x00FFu;
        mem.WriteU16(c.V1 + 0x4u, (ushort)c.V0);        // stale the keyframe
        goto L432D4;

        // The clip clock: it publishes the segment index at sp+0x1C and its 12.12
        // weight at sp+0x20, and returns the segment record in v0.
        L43338: ;
        c.A0 = c.S3;
        c.A1 = c.S6;
        c.A2 = c.S4;
        c.V0 = sp + 0x20u;
        c.A3 = sp + 0x1Cu;
        mem.WriteU32(sp + 0x10u, c.V0);
        double frac = 0.0;
        bool carried = false;
        if (carry && ClipCarry is { } fn
            && fn(mem, c.S3, c.S6, (int)c.S4, out int floorTime, out frac))
        {
            c.A2 = (uint)floorTime;
            carried = true;
        }
        Call(c, mem, 4, 0x80043354u);
        if (carried) SpendCarry(mem, c, sp, frac);
        c.A3 = mem.ReadU32(sp + 0x18u);
        c.V1 = mem.ReadU16(c.A3 + 0x4u);
        c.S4 = c.V0;                                    // the segment record
        if (c.V1 != c.S6) goto L43380;
        c.V1 = mem.ReadU16(c.A3 + 0x6u);
        c.V0 = mem.ReadU32(sp + 0x1Cu);
        if (c.V1 == c.V0) { c.A0 = c.FP; goto L4347C; }

        // The keyframe for this segment, from the model's own vertices.
        L43380: ;
        c.V1 = mem.ReadU32(c.S7 + 0x10u);
        c.V0 = c.V1 + 0xCu;
        mem.WriteU32(c.S7 + 0x24u, c.V0);
        c.V0 = mem.ReadU32(c.V1 + 0xCu);
        c.V1 = mem.ReadU32(c.S7 + 0x10u);
        c.V0 = c.V0 + 0xCu;
        c.A1 = c.V0 + c.V1;
        mem.WriteU32(c.S7 + 0x4Cu, c.A1);
        c.V0 = mem.ReadU32(c.S3 + 0xCu);
        c.S0 = mem.ReadU16(c.S4 + 0x6u);                // the segment's key list count
        if (c.S0 == 0u) { c.S2 = c.S3 + c.V0; goto L43424; }
        c.S2 = c.S3 + c.V0;
        c.S1 = c.S4 + 0xAu;
        c.V0 = mem.ReadU16(c.S4 + 0x8u);
        c.S0 = c.S0 - 0x1u;
        c.V0 = c.V0 << 2;
        c.V0 = c.V0 + c.S2;
        c.A2 = mem.ReadU32(c.V0);
        c.A0 = mem.ReadU32(c.A3 + 0xCu);
        c.A2 = c.S3 + c.A2;
        Call(c, mem, 5, 0x800433DCu);
        c.V0 = c.S0 & 0xFFFFu;
        if (c.V0 == 0u) goto L4345C;
        c.S5 = 0xFFFFu;
        L433E8: ;
        Interrupts.Poll(c, mem);
        c.V0 = mem.ReadU16(c.S1);
        c.S1 = c.S1 + 0x2u;
        c.S0 = c.S0 + c.S5;
        c.V1 = mem.ReadU32(sp + 0x18u);
        c.V0 = c.V0 << 2;
        c.V0 = c.V0 + c.S2;
        c.A1 = mem.ReadU32(c.V0);
        c.A0 = mem.ReadU32(c.V1 + 0xCu);
        c.A1 = c.S3 + c.A1;
        Call(c, mem, 6, 0x80043410u);
        c.V0 = c.S0 & 0xFFFFu;
        if (c.V0 != 0u) goto L433E8;

        L4345C: ;
        c.V0 = mem.ReadU16(c.S4 + 0x4u);
        c.V1 = mem.ReadU32(sp + 0x18u);
        c.V0 = c.V0 << 2;
        c.V0 = c.V0 + c.S2;
        c.V0 = mem.ReadU32(c.V0);
        mem.WriteU32(c.V1 + 0x8u, c.V0);
        c.A0 = c.FP;
        goto L4347C;

        // The segment has no key list: copy the model's vertices straight in.
        L43424: ;
        c.A0 = c.FP;
        c.V1 = mem.ReadU32(c.A3 + 0xCu);
        c.A2 = 0xFFFFu;
        L43430: ;
        Interrupts.Poll(c, mem);
        c.V0 = mem.ReadU32(c.A1);
        c.A1 = c.A1 + 0x4u;
        c.A0 = c.A0 + c.A2;
        mem.WriteU32(c.V1, c.V0);
        c.V1 = c.V1 + 0x4u;
        c.V0 = mem.ReadU32(c.A1);
        c.A1 = c.A1 + 0x4u;
        mem.WriteU32(c.V1, c.V0);
        c.V0 = c.A0 & 0xFFFFu;
        if (c.V0 != 0u) { c.V1 = c.V1 + 0x4u; goto L43430; }
        c.V1 = c.V1 + 0x4u;
        goto L4345C;                                     // the bulk copy falls into L4345C

        // Always reached: copy the keyframe to the posed buffer and decode at the weight.
        L4347C: ;
        c.A2 = 0x801B0000u;
        c.A2 = c.A2 - 0x3468u;                          // 0x801B3468
        c.V0 = mem.ReadU32(sp + 0x18u);
        c.V1 = mem.ReadU16(sp + 0x1Cu);
        c.A1 = mem.ReadU32(c.V0 + 0xCu);
        c.A3 = 0xFFFFu;
        mem.WriteU16(c.V0 + 0x4u, (ushort)c.S6);
        mem.WriteU16(c.V0 + 0x6u, (ushort)c.V1);
        L4349C: ;
        Interrupts.Poll(c, mem);
        c.V0 = mem.ReadU32(c.A1);
        c.A1 = c.A1 + 0x4u;
        c.A0 = c.A0 + c.A3;
        mem.WriteU32(c.A2, c.V0);
        c.A2 = c.A2 + 0x4u;
        c.V0 = mem.ReadU32(c.A1);
        c.A1 = c.A1 + 0x4u;
        mem.WriteU32(c.A2, c.V0);
        c.V0 = c.A0 & 0xFFFFu;
        if (c.V0 != 0u) { c.A2 = c.A2 + 0x4u; goto L4349C; }
        c.A2 = c.A2 + 0x4u;
        c.S0 = 0x801B0000u;
        c.S0 = c.S0 - 0x3468u;
        c.V0 = mem.ReadU32(sp + 0x18u);
        c.A0 = c.S0;
        c.A1 = mem.ReadU32(c.V0 + 0x8u);
        c.A2 = mem.ReadU32(sp + 0x20u);
        c.A1 = c.S3 + c.A1;
        Call(c, mem, 7, 0x800434E8u);
        c.V0 = mem.ReadU32(sp + 0x18u);
        c.V1 = 0x0002u;
        mem.WriteU32(c.S7 + 0x4Cu, c.S0);
        mem.WriteU16(c.V0, (ushort)c.V1);

        L434F8: ;
        c.RA = mem.ReadU32(sp + 0x4Cu);
        c.FP = mem.ReadU32(sp + 0x48u);
        c.S7 = mem.ReadU32(sp + 0x44u);
        c.S6 = mem.ReadU32(sp + 0x40u);
        c.S5 = mem.ReadU32(sp + 0x3Cu);
        c.S4 = mem.ReadU32(sp + 0x38u);
        c.S3 = mem.ReadU32(sp + 0x34u);
        c.S2 = mem.ReadU32(sp + 0x30u);
        c.S1 = mem.ReadU32(sp + 0x2Cu);
        c.S0 = mem.ReadU32(sp + 0x28u);
        c.SP = sp + 0x50u;
    }

    /// <summary>Spend the carry's fraction on the weight the clock published.</summary>
    static void SpendCarry(PSMemory mem, CpuContext c, uint sp, double frac)
    {
        // v0 is the segment record: +0x0 the direction flag, +0x2 the duration.
        uint segment = c.V0;
        if (segment == 0u) return;
        int duration = mem.ReadU16(segment + 0x2u);
        if (duration == 0) return;
        int add = (int)Math.Round(frac * 4096.0 / duration);
        if (mem.ReadU16(segment) != 0u) add = -add;      // a reversed segment runs down
        int weight = (int)mem.ReadU32(sp + 0x20u);
        int next = Math.Clamp(weight + add, 0, 0x1000);
        mem.WriteU32(sp + 0x20u, (uint)next);
    }
}
