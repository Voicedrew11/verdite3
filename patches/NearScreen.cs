using System.Reflection;
using RecompOne.Runtime;
using RecompOne.Runtime.Context;
using RecompOne.Runtime.Memory;
using RecompOne.Runtime.Modding;

namespace Kf3;

/// <summary>
/// Widens the near assemblers' screen block to the widescreen aspect.
///
/// libgte's four polygon-division entries, func_80074D88/80075188/800756A8/
/// 80075B48, drop a whole face when every corner's screen X is past
/// OFX +/- pih/2. pih is the u32 at block+4, written with an immediate 0x140
/// (320) per face by the near assemblers in patches/NearPath.cs, so no memory
/// write can fix it. A pre-hook on each entry rewrites that word before the
/// body runs; the C# near path calls the divisions directly, so it widens at
/// the top of DivTri/DivQuad/DivTri2/DivQuad2 in patches/NearPathDivide.cs.
///
///     KF3_NEARSCREEN=0        leave the block at 320
///     KF3_NEARSCREEN_PROBE=1  a line every 2 s: faces, the entry test's rejects
///                             and how many the widened width rescues
///
/// The block is a stack struct the near assemblers fill per face, so at 4:3
/// nothing is written and there is nothing to restore.
/// </summary>
public static class NearScreen
{
    /// <summary>Which division entry is asking, so the probe can read the right corners.</summary>
    public enum Kind { Tri, Quad, Tri2, Quad2 }

    // The four game-overlay entries (bodies func_80074D90, func_80075190,
    // func_800756B0, func_80075B50; generated/game.cs:167809, 168113, 168505, 168851).
    const uint TriEntry = 0x80074D88;
    const uint QuadEntry = 0x80075188;
    const uint Tri2Entry = 0x800756A8;
    const uint Quad2Entry = 0x80075B48;

    /// <summary>The game's own screen width, the immediate the assemblers store.</summary>
    const uint StockWidth = 320u;

    /// <summary>Widen the block. On unless KF3_NEARSCREEN=0.</summary>
    public static bool Enabled { get; private set; } = true;

    static bool _probe;

    // The window.
    static long _faces, _sz, _right, _left, _bottom, _top, _rescued;
    static uint _width = StockWidth;
    static double _windowStart = Now;

    static double Now => Environment.TickCount64 / 1000.0;

    static readonly uint[] _corner = new uint[4];

    static readonly ModInfo _self = new()
    {
        Id = "kf3.nearscreen",
        Name = "Near screen",
        Version = "1.0",
        Description = "Widens the near assemblers' polygon-division screen block.",
    };

    public static void Configure(string? on, string? probe)
    {
        if (!string.IsNullOrWhiteSpace(on))
            Enabled = on.Trim() is not ("0" or "off");
        if (!string.IsNullOrWhiteSpace(probe) && !probe.Equals("0", StringComparison.Ordinal))
            _probe = true;
        _windowStart = Now;
    }

    public static void Install()
    {
        HookAttach.OnOverlayLoad("near screen", Attach);
    }

    static bool Attach()
    {
        SymbolRegistry.Build();
        var tri = SymbolRegistry.Resolve("game", null, TriEntry);
        var quad = SymbolRegistry.Resolve("game", null, QuadEntry);
        var tri2 = SymbolRegistry.Resolve("game", null, Tri2Entry);
        var quad2 = SymbolRegistry.Resolve("game", null, Quad2Entry);
        if (tri == null || quad == null || tri2 == null || quad2 == null)
        {
            Console.Error.WriteLine("[KF3] near screen: a division entry is not mapped; the block stays 320");
            return false;
        }

        var self = typeof(NearScreen);
        const BindingFlags flags = BindingFlags.Public | BindingFlags.Static;
        int n = 0;
        if (HookManager.AddPre(_self, tri, self.GetMethod(nameof(BeforeTri), flags)!)) n++;
        if (HookManager.AddPre(_self, quad, self.GetMethod(nameof(BeforeQuad), flags)!)) n++;
        if (HookManager.AddPre(_self, tri2, self.GetMethod(nameof(BeforeTri2), flags)!)) n++;
        if (HookManager.AddPre(_self, quad2, self.GetMethod(nameof(BeforeQuad2), flags)!)) n++;
        if (n < 4)
        {
            Console.Error.WriteLine("[KF3] near screen: an entry did not hook; the block stays 320");
            return false;
        }

        HookManager.Commit();
        if (!HookAttach.Installed(tri) || !HookAttach.Installed(quad) ||
            !HookAttach.Installed(tri2) || !HookAttach.Installed(quad2))
        {
            Console.Error.WriteLine("[KF3] near screen: a division entry did not install; the block stays 320");
            return false;
        }

        Console.WriteLine($"[KF3] near screen: {(Enabled ? "follows the aspect" : "off")}");
        return true;
    }

    /// <summary>Pre-hook on func_80074D88: A1 is the block.</summary>
    public static void BeforeTri(CpuContext c, IMemory m) => Widen((PSMemory)m, c.A1, Kind.Tri);
    /// <summary>Pre-hook on func_80075188.</summary>
    public static void BeforeQuad(CpuContext c, IMemory m) => Widen((PSMemory)m, c.A1, Kind.Quad);
    /// <summary>Pre-hook on func_800756A8.</summary>
    public static void BeforeTri2(CpuContext c, IMemory m) => Widen((PSMemory)m, c.A1, Kind.Tri2);
    /// <summary>Pre-hook on func_80075B48.</summary>
    public static void BeforeQuad2(CpuContext c, IMemory m) => Widen((PSMemory)m, c.A1, Kind.Quad2);

    /// <summary>The probe runs before the write so it measures the game's own 320,
    /// whatever path reached this. Then the widened width replaces the 320.</summary>
    public static void Widen(PSMemory mem, uint block, Kind kind)
    {
        uint wide = Enabled && Widescreen.On
            ? StockWidth + 2u * (uint)Widescreen.Margin
            : StockWidth;

        if (_probe) Probe(mem, block, kind, wide);

        if (!Enabled || !Widescreen.On) return;
        if (mem.ReadU32(block + 4u) != StockWidth) return;
        mem.WriteU32(block + 4u, wide);
    }

    /// <summary>Re-run the entry test of the matching body from its own pointers:
    /// SZ (u32 at +0x14 against control 26 >> 1), then X right and X left (s16 at
    /// +0x10 against control 24 >> 16 +/- pih/2), then Y bottom and Y top (s16 at
    /// +0x12 against control 25 >> 16 +/- piv/2). Attribute the face to the first
    /// group all corners fall outside, and count the X rejects the wide pih keeps.</summary>
    static void Probe(PSMemory mem, uint block, Kind kind, uint wide)
    {
        int n = Corners(mem, block, kind);

        uint pih = mem.ReadU32(block + 4u);
        uint piv = mem.ReadU32(block + 8u);
        int ofx = (int)Gte.ReadControl(24) >> 16;
        int ofy = (int)Gte.ReadControl(25) >> 16;
        uint szHalf = Gte.ReadControl(26) >> 1;

        _faces++;
        switch (Reject(mem, n, pih, piv, ofx, ofy, szHalf))
        {
            case Reject0.Sz: _sz++; break;
            case Reject0.Right: _right++; if (Reject(mem, n, wide, piv, ofx, ofy, szHalf) == Reject0.None) _rescued++; break;
            case Reject0.Left: _left++; if (Reject(mem, n, wide, piv, ofx, ofy, szHalf) == Reject0.None) _rescued++; break;
            case Reject0.Bottom: _bottom++; break;
            case Reject0.Top: _top++; break;
        }

        _width = wide;
        Report();
    }

    enum Reject0 { None, Sz, Right, Left, Bottom, Top }

    /// <summary>The five-group entry test, first match wins, exactly as the body
    /// scopes it. <paramref name="pih"/> is the width being tested.</summary>
    static Reject0 Reject(PSMemory mem, int n, uint pih, uint piv, int ofx, int ofy, uint szHalf)
    {
        bool sz = true, right = true, left = true, bottom = true, top = true;
        int r = ofx + (int)(pih >> 1);
        int l = ofx - (int)(pih >> 1);
        int b = ofy + (int)(piv >> 1);
        int t = ofy - (int)(piv >> 1);
        for (int i = 0; i < n; i++)
        {
            uint p = _corner[i];
            if (mem.ReadU32(p + 0x14u) >= szHalf) sz = false;
            int x = (short)mem.ReadU16(p + 0x10u);
            int y = (short)mem.ReadU16(p + 0x12u);
            if (r >= x) right = false;
            if (x >= l) left = false;
            if (b >= y) bottom = false;
            if (y >= t) top = false;
        }

        if (sz) return Reject0.Sz;
        if (right) return Reject0.Right;
        if (left) return Reject0.Left;
        if (bottom) return Reject0.Bottom;
        if (top) return Reject0.Top;
        return Reject0.None;
    }

    /// <summary>Corner pointers as the matching body's entry test reads them.
    /// Tri: A3=block+0x60, u32 at A3+0x48/0x4C/0x50 (NearPathDivide.cs:23,31-33,
    /// 739,747-749). Quad: A3=block+0x78, u32 at A3+0x78/0x7C/0x80/0x84
    /// (NearPathDivide.cs:334,342-345, 1092,1100-1103).</summary>
    static int Corners(PSMemory mem, uint block, Kind kind)
    {
        if (kind is Kind.Tri or Kind.Tri2)
        {
            uint a3 = block + 0x60u;
            _corner[0] = mem.ReadU32(a3 + 0x48u);
            _corner[1] = mem.ReadU32(a3 + 0x4Cu);
            _corner[2] = mem.ReadU32(a3 + 0x50u);
            return 3;
        }

        uint a3q = block + 0x78u;
        _corner[0] = mem.ReadU32(a3q + 0x78u);
        _corner[1] = mem.ReadU32(a3q + 0x7Cu);
        _corner[2] = mem.ReadU32(a3q + 0x80u);
        _corner[3] = mem.ReadU32(a3q + 0x84u);
        return 4;
    }

    static void Report()
    {
        if (!_probe) return;
        double now = Now;
        if (now - _windowStart < 2.0) return;

        Console.WriteLine($"[KF3] nearscreen: faces {_faces} sz {_sz} right {_right} " +
                          $"left {_left} bottom {_bottom} top {_top} rescued {_rescued} width {_width}");

        _windowStart = now;
        _faces = _sz = _right = _left = _bottom = _top = _rescued = 0;
    }
}
