using System.Runtime.InteropServices;
using RecompOne.Runtime;
using RecompOne.Runtime.Context;
using RecompOne.Runtime.Memory;
using RecompOne.Runtime.Modding;

namespace Kf3;

/// <summary>
/// The two bulk polygon assemblers in C#: `func_80039D50`, the map's, and
/// `func_80035CA4`, the lit models' and the HUD's (PolyAssemblerLit.cs). The same
/// reads and stores of RAM in the same order and the same GTE operations, with the
/// scratchpad's working state in locals.
///
///     KF3_POLYASM=1         both in C# (the default)
///     KF3_POLYASM=0         both recompiled, to compare
///     KF3_POLYASM=verify    run both on every call and compare RAM, the scratchpad,
///                           registers and the GTE
///     KF3_POLYASM_MAP=0     func_80039D50 recompiled
///     KF3_POLYASM_LIT=0     func_80035CA4 recompiled
///
/// See "The geometry path in C#" in docs/GEOMETRY.md.
/// </summary>
public static partial class PolyAssembler
{
    const uint Map = 0x80039D50;

    const uint FogNear = 0x801AEC7C;
    const uint FogFar = 0x801AEC80;

    enum Mode { Off, On, Verify }
    static Mode _mode = Mode.On;
    static bool _queuedMap;

    public static bool MapEnabled { get; set; } = true;

    /// <summary>Running totals; never reset.</summary>
    public static long MapCalls;

    static readonly ModInfo _self = new()
    {
        Id = "kf3.polyasm",
        Name = "Polygon assembler",
        Version = "1.0",
        Description = "func_80039D50 and func_80035CA4 in C#.",
    };

    public static void Configure(string? mode, string? map, string? lit)
    {
        _mode = mode?.Trim().ToLowerInvariant() switch
        {
            "verify" => Mode.Verify,
            "0" or "off" => Mode.Off,
            _ => Mode.On,
        };
        MapEnabled = map?.Trim() != "0";
        LitEnabled = lit?.Trim() != "0";
    }

    public static void Install()
    {
        if (_mode == Mode.Off) return;
        HookAttach.OnOverlayLoad("polyasm", Attach);
    }

    static bool Attach()
    {
        SymbolRegistry.Build();
        var map = SymbolRegistry.Resolve("game", null, Map);
        var lit = SymbolRegistry.Resolve("game", null, Lit);
        if (map == null || lit == null) return false;

        if (!Queue(ref _queuedMap, map, nameof(ReplaceMap))) return false;
        if (!Queue(ref _queuedLit, lit, nameof(ReplaceLit))) return false;

        HookManager.Commit();
        bool ok = HookAttach.Installed(map) && HookAttach.Installed(lit);
        string State(bool on) => !on ? "off" : _mode.ToString().ToLowerInvariant();
        Console.WriteLine(!ok
            ? "[KF3] polyasm: not installed"
            : $"[KF3] polyasm: map {State(MapEnabled)}, lit {State(LitEnabled)}");
        return ok;
    }

    static bool Queue(ref bool queued, System.Reflection.MethodInfo target, string method)
    {
        if (queued) return true;
        var impl = typeof(PolyAssembler).GetMethod(method,
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        queued = HookManager.AddReplace(_self, target, impl);
        return queued;
    }

    // PGXP follows values through the registers, which locals do not have.
    static bool Recompiled(bool on) => !on || RecompOne.Runtime.Pgxp.Pgxp.CpuTracking;

    static void ReplaceMap(Action<CpuContext, IMemory> orig, CpuContext c, IMemory m)
    {
        if (Recompiled(MapEnabled) || m is not PSMemory mem) { orig(c, m); return; }
        if (_mode == Mode.Verify) Verify(_mapCheck, orig, c, mem, RunMap);
        else RunMap(c, mem);
    }

    // ---- func_80039D50 -------------------------------------------------------

    static long _mapExhausted, _zeroFogRange;

    /// <summary>Faces by kind (0x24, 0x2C, 0x34) that reached the fill, for verify's report.</summary>
    static readonly long[] _mapKinds = new long[3];

    /// <summary>Faces by kind (0x24, 0x2C, 0x34, 0x3C) the loop met, filled or not.</summary>
    static readonly long[] _mapSeen = new long[4];

    /// <summary>a0 the mesh; everything else from the scratchpad. A leaf with no
    /// stack frame.</summary>
    static void RunMap(CpuContext c, PSMemory mem)
    {
        MapCalls++;
        var fr = new Frame(mem, c);
        EnterMap(ref fr);
        MapBody(c, ref fr);
        LeaveMap(ref fr);
        c.LO = fr.Lo;
        c.HI = fr.Hi;
    }

    static void EnterMap(ref Frame fr)
    {
        var mem = fr.Mem;
        fr.Ot = mem.ReadU32(Pad + 0x08u);
        fr.Cursor = mem.ReadU32(Pad + 0x14u);
        fr.End = mem.ReadU32(Pad + 0x18u);
        fr.Word = mem.ReadU32(Pad + 0x1Cu);
        fr.Pkt = mem.ReadU32(Pad + 0x2Cu);
        fr.P0 = mem.ReadU32(Pad + 0x30u);
        fr.P1 = mem.ReadU32(Pad + 0x34u);
        fr.P2 = mem.ReadU32(Pad + 0x38u);
        fr.P3 = mem.ReadU32(Pad + 0x3Cu);
        fr.Lit = mem.ReadU32(Pad + 0x40u);
        fr.Cache = mem.ReadU32(Pad + 0x44u);
        fr.Src = mem.ReadU32(Pad + 0x4Cu);
        fr.Colour = mem.ReadU32(Pad + 0x54u);
        fr.Nclip = mem.ReadU32(Pad + 0x60u);
        fr.Otz = mem.ReadU16(Pad + 0x64u);
        fr.Limit = (short)mem.ReadU16(Pad + 0x66u);
        fr.Faces = mem.ReadU32(Pad + 0x68u);
        fr.Links = mem.ReadU32(Pad + 0x6Cu);
        fr.Packets = mem.ReadU32(Pad + 0x70u);
        fr.Dst = fr.Cache;
        fr.Near = (int)mem.ReadU32(FogNear);
        fr.Far = (int)mem.ReadU32(FogFar);
    }

    /// <summary>What the routine leaves in the scratchpad, at its end or when the
    /// primitive buffer runs out.</summary>
    static void LeaveMap(ref Frame fr)
    {
        var mem = fr.Mem;
        mem.WriteU32(Pad + 0x14u, fr.Cursor);
        mem.WriteU32(Pad + 0x1Cu, fr.Word);
        mem.WriteU32(Pad + 0x20u, fr.Face);
        mem.WriteU32(Pad + 0x24u, fr.Header);
        mem.WriteU32(Pad + 0x28u, fr.Normals);
        mem.WriteU32(Pad + 0x2Cu, fr.Pkt);
        mem.WriteU32(Pad + 0x30u, fr.P0);
        mem.WriteU32(Pad + 0x34u, fr.P1);
        mem.WriteU32(Pad + 0x38u, fr.P2);
        mem.WriteU32(Pad + 0x3Cu, fr.P3);
        mem.WriteU32(Pad + 0x40u, fr.Lit);
        mem.WriteU32(Pad + 0x48u, fr.Dst);
        mem.WriteU32(Pad + 0x50u, fr.Src);
        mem.WriteU32(Pad + 0x58u, (uint)fr.Near);
        mem.WriteU32(Pad + 0x5Cu, (uint)fr.Far);
        mem.WriteU32(Pad + 0x60u, fr.Nclip);
        mem.WriteU16(Pad + 0x64u, fr.Otz);
        mem.WriteU32(Pad + 0x68u, fr.Faces);
        mem.WriteU32(Pad + 0x6Cu, fr.Links);
        mem.WriteU32(Pad + 0x70u, fr.Packets);
    }

    static void MapBody(CpuContext c, ref Frame fr)
    {
        var mem = fr.Mem;
        uint table = mem.ReadU32(Pad + 0x10u);
        fr.Header = (c.A0 & 0xFFFFu) * 28u + 0xCu + table;
        fr.Normals = mem.ReadU32(fr.Header + 8u) + 0xCu + table;

        for (uint n = mem.ReadU32(fr.Header + 4u); n != 0; n--)
        {
            Interrupts.Poll(c, mem);
            fr.Check();
            Transform(ref fr, fr.Src, fr.Dst);
            fr.Dst += 8u;
            fr.Src += 8u;
        }

        fr.Face = mem.ReadU32(fr.Header + 0x10u) + 0xCu + table;
        uint count = mem.ReadU32(fr.Header + 0x14u);
        fr.Faces += count;
        for (; count != 0; count--)
        {
            Interrupts.Poll(c, mem);
            fr.Check();
            uint word = fr.Word = mem.ReadU32(fr.Face);
            uint f = fr.Face += 4u;
            uint cmd = word >> 24;
            if (_mode == Mode.Verify && (cmd & 0xC5u) == 0x04u && (cmd & 0x20u) != 0) _mapSeen[((cmd & 0x18u) >> 3)]++;

            bool ok = (cmd & 0xFDu) switch
            {
                0x24u => MapTriangle(ref fr, f, cmd, gouraud: false),
                0x2Cu => MapQuad(ref fr, f, cmd),
                0x34u => MapTriangle(ref fr, f, cmd, gouraud: true),
                _ => true,
            };
            if (!ok) { _mapExhausted++; return; }

            fr.Face = f + ((word >> 6) & 0x3FCu);
        }
    }

    /// <summary>RTPS into the cache entry: the screen word, otz SZ3 >> 2 (stored as a
    /// word, so the fog half is zeroed first), and the fog weight
    /// ((otz - near/4) << 14) / (far - near), clamped to 0..0x1F0F, or 0 when the
    /// near is 32000 or more.</summary>
    static void Transform(ref Frame fr, uint src, uint dst)
    {
        var mem = fr.Mem;
        Gte.Write(0, mem.ReadU32(src));
        Gte.Write(1, mem.ReadU32(src + 4u));
        Gte.Rtps(12, false);
        mem.WriteU32(dst, Gte.Read(14));
        mem.WriteU32(dst + 4u, (uint)((int)Gte.Read(19) >> 2));
        if (fr.Near < 32000)
        {
            uint z = (uint)(short)R16(ref fr, dst + 4u);
            if (fr.Far == fr.Near) _zeroFogRange++;
            Divide(ref fr, (z - (uint)(fr.Near >> 2)) << 14, (uint)fr.Far - (uint)fr.Near);
            W16(ref fr, dst + 6u, (ushort)fr.Lo);
        }
        else W16(ref fr, dst + 6u, 0);
        if ((short)R16(ref fr, dst + 6u) >= 0x1F10) W16(ref fr, dst + 6u, 0x1F0F);
        if ((short)R16(ref fr, dst + 6u) < 0) W16(ref fr, dst + 6u, 0);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    static bool Near(ref Frame fr, uint p) => (short)R16(ref fr, p + 4u) < fr.Limit;

    /// <summary>The otz as the game stores it, clamped to 0x1F0F.</summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    static void Clamp(ref Frame fr, short z) => fr.Otz = z >= 0x1F10 ? (ushort)0x1F0F : (ushort)z;

    /// <summary>The 0x24 and 0x34 kinds. False when the primitive buffer ran out,
    /// which ends the call.</summary>
    static bool MapTriangle(ref Frame fr, uint f, uint cmd, bool gouraud)
    {
        uint p0 = fr.P0 = fr.Cache + R16(ref fr, f + 0x0Eu);
        uint p1 = fr.P1 = fr.Cache + R16(ref fr, f + 0x10u);
        uint p2 = fr.P2 = fr.Cache + R16(ref fr, f + 0x12u);
        if (!Visible(ref fr, p0, p1, p2)) return true;
        if (Near(ref fr, p0) && Near(ref fr, p1) && Near(ref fr, p2)) return true;

        int sum = (short)R16(ref fr, p0 + 4u) + (short)R16(ref fr, p1 + 4u) + (short)R16(ref fr, p2 + 4u);
        Clamp(ref fr, (short)Third(ref fr, sum));
        if (!Allocate(ref fr, 0x28u, out uint pkt)) return false;
        fr.Packets++;

        _mapKinds[gouraud ? 2 : 0]++;
        if (gouraud) FillGouraudTriangle(ref fr, pkt, f, cmd, fr.Normals, p0, p1, p2);
        else FillTriangle(ref fr, pkt, f, cmd, fr.Normals, p0, p1, p2);
        Link(ref fr, pkt);
        return true;
    }

    static bool MapQuad(ref Frame fr, uint f, uint cmd)
    {
        uint p0 = fr.P0 = fr.Cache + R16(ref fr, f + 0x12u);
        uint p1 = fr.P1 = fr.Cache + R16(ref fr, f + 0x14u);
        uint p2 = fr.P2 = fr.Cache + R16(ref fr, f + 0x16u);
        if (!Visible(ref fr, p0, p1, p2)) return true;
        uint p3 = fr.P3 = fr.Cache + R16(ref fr, f + 0x18u);
        if (Near(ref fr, p0) && Near(ref fr, p1) && Near(ref fr, p2) && Near(ref fr, p3)) return true;

        int sum = (short)R16(ref fr, p0 + 4u) + (short)R16(ref fr, p1 + 4u)
                + (short)R16(ref fr, p2 + 4u) + (short)R16(ref fr, p3 + 4u);
        Clamp(ref fr, (short)(sum >> 2));
        if (!Allocate(ref fr, 0x34u, out uint pkt)) return false;
        fr.Packets++;

        _mapKinds[1]++;
        FillQuad(ref fr, pkt, f, cmd, fr.Normals, p0, p1, p2, p3);
        Link(ref fr, pkt);
        return true;
    }

    // ---- KF3_POLYASM=verify -------------------------------------------------

    /// <summary>Below the entry SP: this call's frame and its callees'. The two
    /// versions leave different garbage there, and nothing reads it after.</summary>
    const uint StackWindow = 0x2000;

    const int PadWords = 256;

    sealed class Check(string name, Func<string> extra)
    {
        public readonly string Name = name;
        public readonly Func<string> Extra = extra;
        public byte[] Before = [], Theirs = [];
        public readonly uint[] PadBefore = new uint[PadWords], PadTheirs = new uint[PadWords], PadOurs = new uint[PadWords];
        public readonly Gte.State GteEntry = new(), GteTheirs = new(), GteOurs = new();
        public long Calls, Bad, BadPad, BadReg, BadGte;
        public readonly List<string> Samples = new();
        public double ReportAt;
    }

    static readonly Check _mapCheck = new("func_80039D50", () =>
    {
        string s = $"; {_mapExhausted} buffer exhaustion(s), {_zeroFogRange} zero fog range(s); " +
                   $"filled 0x24 {_mapKinds[0]}, 0x2C {_mapKinds[1]}, 0x34 {_mapKinds[2]}; " +
                   $"met 0x24 {_mapSeen[0]}, 0x2C {_mapSeen[1]}, 0x34 {_mapSeen[2]}, 0x3C {_mapSeen[3]}";
        _mapExhausted = _zeroFogRange = 0;
        Array.Clear(_mapKinds);
        Array.Clear(_mapSeen);
        return s;
    });

    /// <summary>The scratchpad is a separate array in PSMemory, outside Ram, so it is
    /// taken word by word through the accessors.</summary>
    static void SavePad(PSMemory mem, uint[] to)
    {
        for (int i = 0; i < PadWords; i++) to[i] = mem.ReadU32(Pad + (uint)i * 4u);
    }

    static void LoadPad(PSMemory mem, uint[] from)
    {
        for (int i = 0; i < PadWords; i++) mem.WriteU32(Pad + (uint)i * 4u, from[i]);
    }

    static void Verify(Check k, Action<CpuContext, IMemory> orig, CpuContext c, PSMemory mem,
                       Action<CpuContext, PSMemory> run)
    {
        var ro = mem.Ram;
        var ram = MemoryMarshal.CreateSpan(ref MemoryMarshal.GetReference(ro), ro.Length);
        if (k.Before.Length != ram.Length)
        {
            k.Before = new byte[ram.Length];
            k.Theirs = new byte[ram.Length];
        }

        uint a0 = c.A0, a1 = c.A1;
        ram.CopyTo(k.Before);
        SavePad(mem, k.PadBefore);
        var entry = c.Snapshot();
        Gte.Save(k.GteEntry);

        orig(c, mem);
        ram.CopyTo(k.Theirs);
        SavePad(mem, k.PadTheirs);
        var theirs = c.Snapshot();
        Gte.Save(k.GteTheirs);

        k.Before.CopyTo(ram);
        LoadPad(mem, k.PadBefore);
        c.Restore(entry);
        Gte.Load(k.GteEntry);
        run(c, mem);
        SavePad(mem, k.PadOurs);
        var ours = c.Snapshot();
        Gte.Save(k.GteOurs);

        k.Calls++;
        int hi = (int)(entry.SP & (uint)(ram.Length - 1));
        int lo = Math.Max(0, hi - (int)StackWindow);
        bool same = ram[..lo].SequenceEqual(k.Theirs.AsSpan(0, lo))
                 && ram[hi..].SequenceEqual(k.Theirs.AsSpan(hi));
        if (!same)
        {
            k.Bad++;
            if (k.Samples.Count < 8)
            {
                int i = FirstDiff(ram, k.Theirs, 0, lo);
                if (i < 0) i = FirstDiff(ram, k.Theirs, hi, ram.Length);
                int diffs = CountDiffs(ram, k.Theirs, 0, lo) + CountDiffs(ram, k.Theirs, hi, ram.Length);
                k.Samples.Add($"a0={a0:X} a1={a1:X}: {diffs} byte(s), first 0x{0x80000000u + (uint)i:X8} " +
                              $"recompiled {k.Theirs[i]:X2} ours {ram[i]:X2}");
            }
        }

        int pad = k.PadOurs.AsSpan().CommonPrefixLength(k.PadTheirs);
        if (pad < PadWords)
        {
            k.BadPad++;
            if (k.Samples.Count < 8)
                k.Samples.Add($"a0={a0:X} a1={a1:X}: scratchpad +0x{pad * 4:X2} recompiled {k.PadTheirs[pad]:X8} " +
                              $"ours {k.PadOurs[pad]:X8}");
        }

        if (ours.S0 != theirs.S0 || ours.S1 != theirs.S1 || ours.S2 != theirs.S2 || ours.S3 != theirs.S3 ||
            ours.S4 != theirs.S4 || ours.S5 != theirs.S5 || ours.S6 != theirs.S6 || ours.S7 != theirs.S7 ||
            ours.FP != theirs.FP || ours.SP != theirs.SP || ours.RA != theirs.RA ||
            ours.LO != theirs.LO || ours.HI != theirs.HI)
        {
            k.BadReg++;
            if (k.Samples.Count < 8)
                k.Samples.Add($"a0={a0:X} a1={a1:X}: registers, LO {theirs.LO:X8}/{ours.LO:X8} HI {theirs.HI:X8}/{ours.HI:X8} " +
                              $"SP {theirs.SP:X8}/{ours.SP:X8}");
        }

        if (Gte.Diff(k.GteTheirs, k.GteOurs) is { } gte)
        {
            k.BadGte++;
            if (k.Samples.Count < 8) k.Samples.Add($"a0={a0:X} a1={a1:X}: GTE {gte}");
        }

        // The recompiled result stands, so a mismatch cannot reach the picture.
        k.Theirs.CopyTo(ram);
        LoadPad(mem, k.PadTheirs);
        c.Restore(theirs);
        Gte.Load(k.GteTheirs);

        double now = Environment.TickCount64 / 1000.0;
        if (now < k.ReportAt) return;
        k.ReportAt = now + 2.0;
        Console.WriteLine($"[polyasm] verify {k.Name}: {k.Calls} call(s), {k.Bad} RAM mismatch(es), " +
                          $"{k.BadPad} scratchpad mismatch(es), {k.BadReg} register mismatch(es), " +
                          $"{k.BadGte} GTE mismatch(es){k.Extra()}");
        foreach (var s in k.Samples) Console.WriteLine($"[polyasm]   {s}");
        k.Samples.Clear();
        k.Calls = k.Bad = k.BadPad = k.BadReg = k.BadGte = 0;
    }

    static int FirstDiff(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b, int from, int to)
    {
        int i = a[from..to].CommonPrefixLength(b[from..to]);
        return i == to - from ? -1 : from + i;
    }

    static int CountDiffs(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b, int from, int to)
    {
        int n = 0;
        for (int i = from; i < to; i++) if (a[i] != b[i]) n++;
        return n;
    }
}
