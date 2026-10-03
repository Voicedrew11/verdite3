using System.Reflection;
using RecompOne.Runtime.Context;
using RecompOne.Runtime.Memory;
using RecompOne.Runtime.Modding;

namespace Kf3;

/// <summary>
/// KF3_PRIMBUF_PROBE=1: how much of the frame's primitive buffer the map walk
/// spends, and how often it runs out. A measurement only; the buffers are not
/// moved. See "The primitive buffer" in docs/GAME_INTERNALS.md.
///
/// Stage 15 call #5, `func_80035630`, flips the buffer index at `0x801AEAE8` and
/// resets the new buffer's cursor to its start. The descriptor pointer
/// `0x80199170` still names the buffer the *previous* frame drew into, so a pre
/// hook there reads that frame's final cursor before it is rewound: the live
/// capacity is `end - start` and the frame's use is `cur - start` of the
/// descriptor `0x80199158 + 12 * buffer`.
///
/// `func_8003BB04` draws one half-tile. After the far gate it picks the near
/// assembler `func_8003AB04` when the grid byte's bit `0x04` is set **and** at
/// least `0x2800` bytes (`end - 0x2800 >= cursor`) are left, otherwise the bulk
/// `func_80039D50`. A pre hook counts the halves whose bit `0x04` was set but
/// whose remaining bytes were under `0x2800`, so the bulk assembler took them.
///
/// A frame is counted as running out when its cursor is past the end or within
/// one `0x34`-byte packet of it (`cur + 0x34 > end`): an assembler bumps the
/// cursor past the end and returns, ending the rest of the call.
///
///     KF3_PRIMBUF_PROBE=1   a line every 2 s
/// </summary>
public static class PrimBufferProbe
{
    /// <summary>Stage 15 call #5: flips the buffer index and rewinds the cursor.</summary>
    const uint ResetRoutine = 0x80035630;

    /// <summary>Draws one half-tile, choosing the near or the bulk assembler.</summary>
    const uint HalfTile = 0x8003BB04;

    /// <summary>u32: this frame's `{start, end, cur}` descriptor (set by the reset).</summary>
    const uint DescriptorPointer = 0x80199170;

    const uint Pad = 0x1F800000;
    const uint PadCursor = Pad + 0x14;   // the walk's primitive cursor
    const uint PadEnd = Pad + 0x18;      // its end
    const uint ModelTable = Pad + 0x10;  // the area's model table

    // The far gate `func_8003BB04` runs before the assembler: with the s16 at
    // 0x8018FAD4 equal to 1 and the byte at 0x8018FAEA set, a half whose mesh id
    // (the byte at the record `a0`) is at or past half the table is skipped.
    const uint FarGateSetting = 0x8018FAD4;
    const uint FarGateFlag = 0x8018FAEA;

    /// <summary>A POLY_GT4, the largest packet the map writes.</summary>
    const int PacketBytes = 0x34;

    /// <summary>The near assembler's reservation: under this much left, the bulk is used.</summary>
    const uint NearReserve = 0x2800;

    static bool _on;

    static readonly ModInfo _self = new()
    {
        Id = "kf3.primbufprobe",
        Name = "Primitive buffer probe",
        Version = "1.0",
        Description = "Measures the frame's primitive buffer use; moves nothing.",
    };

    // The window.
    static long _frames, _usedSum, _peak, _ranOut, _nearStarved;
    static long _capacity;
    static double _windowStart = Now;

    static double Now => Environment.TickCount64 / 1000.0;

    public static void Configure(string? on) =>
        _on = !string.IsNullOrWhiteSpace(on) && on.Trim() is not ("0" or "off");

    public static void Install()
    {
        if (!_on) return;
        HookAttach.OnOverlayLoad("primbuf probe", Attach);
    }

    static bool Attach()
    {
        SymbolRegistry.Build();
        var self = typeof(PrimBufferProbe);
        var reset = SymbolRegistry.Resolve("game", null, ResetRoutine);
        var half = SymbolRegistry.Resolve("game", null, HalfTile);
        if (reset == null || half == null)
        {
            Console.Error.WriteLine($"[KF3] primbuf: not installed (no game function at " +
                                    $"0x{ResetRoutine:X8} or 0x{HalfTile:X8})");
            return false;
        }

        HookManager.AddPre(_self, reset, self.GetMethod(nameof(BeforeReset), BindingFlags.Public | BindingFlags.Static)!);
        HookManager.AddPre(_self, half, self.GetMethod(nameof(BeforeHalfTile), BindingFlags.Public | BindingFlags.Static)!);
        HookManager.Commit();
        if (!HookAttach.Installed(reset) || !HookAttach.Installed(half))
        {
            Console.Error.WriteLine("[KF3] primbuf: not installed (a hook did not attach)");
            return false;
        }

        Console.WriteLine("[KF3] primbuf: probing");
        return true;
    }

    /// <summary>The previous frame's final `{start, end, cur}`, read before the reset.</summary>
    public static void BeforeReset(CpuContext c, IMemory m)
    {
        uint desc = m.ReadU32(DescriptorPointer);
        if (desc < 0x80000000u) return;
        uint start = m.ReadU32(desc), end = m.ReadU32(desc + 4u), cur = m.ReadU32(desc + 8u);
        // A descriptor before it is first set, or a stale one, can hold anything;
        // a real one is in main RAM with a capacity under its 2 MB.
        if (start < 0x80000000u || end <= start || end - start > 0x200000u) return;
        if (cur < start) return;

        long used = cur - start;
        _frames++;
        _usedSum += used;
        _capacity = end - start;
        if (used > _peak) _peak = used;
        if (cur + PacketBytes > end) _ranOut++;

        Report();
    }

    /// <summary>Half-tiles that set the grid's near bit but had under 10 KB left.</summary>
    public static void BeforeHalfTile(CpuContext c, IMemory m)
    {
        if ((c.A2 & 0x04u) == 0) return;

        // The far gate runs first: a half past half the table never picks an assembler.
        if ((short)m.ReadU16(FarGateSetting) == 1)
        {
            uint flag = m.ReadU8(FarGateFlag);
            if (flag != 0)
            {
                uint model = m.ReadU32(ModelTable);
                uint half = m.ReadU32(model + 4u) >> 1;
                if (m.ReadU8(c.A0) >= half) return;
            }
        }

        uint cur = m.ReadU32(PadCursor), end = m.ReadU32(PadEnd);
        if (end <= NearReserve) return;
        if (end - NearReserve >= cur) return;   // at least 10 KB left: the near path
        _nearStarved++;
    }

    static void Report()
    {
        double now = Now;
        if (now - _windowStart < 2.0) return;
        long mean = _frames > 0 ? _usedSum / _frames : 0;
        double pct = _capacity > 0 ? 100.0 * _peak / _capacity : 0.0;
        Console.WriteLine($"[KF3] primbuf: capacity {_capacity} bytes, peak {_peak} ({pct:0.0}%), " +
                          $"mean {mean}, {_frames} frames, {_ranOut} ran out, " +
                          $"{_nearStarved} near-starved");
        _windowStart = now;
        _frames = _usedSum = _peak = _ranOut = _nearStarved = 0;
    }
}
