using System.Reflection;
using RecompOne.Runtime.Context;
using RecompOne.Runtime.Memory;
using RecompOne.Runtime.Modding;

namespace Kf3;

/// <summary>
/// KF3_GEOPROBE=1: what each of stage 15's calls adds to the ordering table. The table
/// is walked before and after each call and the packets new to it counted, by GPU
/// command and by slot; KF3_GEOPROBE_FUNCS=hex,... adds functions reached from
/// anywhere. One block every five seconds. KF3_GEOPROBE=time reports each call's
/// inclusive time instead, without walking the tables. See "The geometry path" in
/// docs/GAME_INTERNALS.md.
/// </summary>
public static class GeometryProbe
{
    // Stage 15 (func_800422B8): each call site and its callee.
    static readonly (uint Site, uint Callee)[] Calls =
    [
        (0x800422D8, 0x800357E8), (0x800422E0, 0x800351FC), (0x800422E8, 0x80041F9C),
        (0x800422F0, 0x80034BF4), (0x800422F8, 0x80035630), (0x80042300, 0x80043858),
        (0x80042308, 0x8003DF50), (0x80042470, 0x80016A98), (0x80042888, 0x8003C35C),
        (0x80042890, 0x80041E68), (0x80042898, 0x80041D9C), (0x800428A0, 0x8003BFD0),
        (0x800428A8, 0x80040AE4), (0x800428B0, 0x8003D280), (0x800428B8, 0x8003D38C),
        (0x800428C0, 0x8003D41C), (0x800428C8, 0x8003D568), (0x800428D0, 0x8003D64C),
        (0x800428D8, 0x8003D79C), (0x800428E0, 0x80035700), (0x800428E8, 0x80019614),
        (0x800428F0, 0x80043940),
    ];

    const uint OtPointer = 0x801A9174;   // u32: the ordering table being built
    const int OtWords = 0x2000;
    const uint FrontPointer = 0x801A91B8;   // u32: an 8-entry table the swap splices in
    const int FrontWords = 8;
    const int MaxPackets = 1 << 16;

    sealed class Row
    {
        public string Label = "";
        public long Calls, Added, Removed, Words, Ticks;
        public int MinSlot = int.MaxValue, MaxSlot = -1;
        public uint MinAddr = uint.MaxValue, MaxAddr;
        public readonly Dictionary<byte, long> Codes = new();
        public readonly Dictionary<int, long> Sizes = new();
    }

    static readonly ModInfo _self = new() { Id = "kf3.geoprobe", Name = "Geometry probe", Version = "1.0" };
    static readonly Dictionary<uint, int> _bySite = new();
    static readonly Dictionary<uint, int> _byCallee = new();
    static readonly List<Row> _rows = new();
    static readonly List<uint> _extra = new();
    static readonly Dictionary<int, Stack<HashSet<uint>?>> _stacks = new();
    static readonly Dictionary<int, Stack<long>> _clocks = new();

    // KF3_GEOPROBE=time: each call's inclusive wall time only, no table walks.
    static bool _timing;
    static bool _queued;
    static long _frames, _windowStart = -1;

    public static void Install()
    {
        string? mode = Environment.GetEnvironmentVariable("KF3_GEOPROBE");
        if (mode != "1" && mode != "time") return;
        _timing = mode == "time";
        for (int i = 0; i < Calls.Length; i++)
        {
            _bySite[Calls[i].Site] = i;
            _rows.Add(new Row { Label = $"{i + 1,2} {Calls[i].Callee:X8}" });
        }
        foreach (var s in (Environment.GetEnvironmentVariable("KF3_GEOPROBE_FUNCS") ?? "")
                     .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (_extra.Count == 16) break;
            uint a = Convert.ToUInt32(s.Replace("0x", "", StringComparison.OrdinalIgnoreCase), 16);
            if ((a & 0xFF000000) == 0) a |= 0x80000000;
            if (_byCallee.ContainsKey(a)) continue;
            _byCallee[a] = _rows.Count;
            _extra.Add(a);
            _rows.Add(new Row { Label = $"   {a:X8}*" });
        }
        HookAttach.OnOverlayLoad("geoprobe", Attach);
    }

    static bool Attach()
    {
        SymbolRegistry.Build();
        var pre = typeof(GeometryProbe).GetMethod(nameof(Pre), BindingFlags.Public | BindingFlags.Static)!;
        var post = typeof(GeometryProbe).GetMethod(nameof(Post), BindingFlags.Public | BindingFlags.Static)!;
        var targets = new List<MethodInfo>();
        foreach (uint a in Calls.Select(c => c.Callee))
        {
            var t = SymbolRegistry.Resolve("game", null, a);
            if (t == null) { Console.Error.WriteLine($"[KF3] geoprobe: no game/0x{a:X8}"); return false; }
            targets.Add(t);
        }
        // A function reached from anywhere is told apart by its own pair of methods.
        for (int k = 0; k < _extra.Count; k++)
        {
            var t = SymbolRegistry.Resolve("game", null, _extra[k]);
            if (t == null) { Console.Error.WriteLine($"[KF3] geoprobe: no game/0x{_extra[k]:X8}"); return false; }
            targets.Add(t);
        }
        if (!_queued)
        {
            for (int i = 0; i < Calls.Length; i++)
            {
                HookManager.AddPre(_self, targets[i], pre);
                HookManager.AddPost(_self, targets[i], post);
            }
            for (int k = 0; k < _extra.Count; k++)
            {
                var slot = typeof(Slot<>).MakeGenericType(SlotMarkers[k]);
                slot.GetField("Row")!.SetValue(null, _byCallee[_extra[k]]);
                HookManager.AddPre(_self, targets[Calls.Length + k], slot.GetMethod("Pre")!);
                HookManager.AddPost(_self, targets[Calls.Length + k], slot.GetMethod("Post")!);
            }
            _queued = true;
        }
        HookManager.Commit();
        int n = targets.Count(HookAttach.Installed);
        Console.WriteLine($"[KF3] geoprobe: {n}/{targets.Count} functions hooked");
        return n == targets.Count;
    }

    public static void Pre(CpuContext c, IMemory m) => Enter(_bySite.TryGetValue(c.RA - 8, out int i) ? i : -1, m);
    public static void Post(CpuContext c, IMemory m) => Leave(_bySite.TryGetValue(c.RA - 8, out int i) ? i : -1, m);

    static void Enter(int row, IMemory m)
    {
        if (row < 0) return;
        if (_timing)
        {
            if (!_clocks.TryGetValue(row, out var ck)) _clocks[row] = ck = new();
            ck.Push(System.Diagnostics.Stopwatch.GetTimestamp());
            return;
        }
        if (!_stacks.TryGetValue(row, out var st)) _stacks[row] = st = new();
        st.Push(Walk(m, null));
    }

    static void Leave(int row, IMemory m)
    {
        if (_timing)
        {
            if (row < 0 || !_clocks.TryGetValue(row, out var ck) || ck.Count == 0) return;
            _rows[row].Calls++;
            _rows[row].Ticks += System.Diagnostics.Stopwatch.GetTimestamp() - ck.Pop();
            if (row < Calls.Length && Calls[row].Callee == 0x80043940) { _frames++; Report(); }
            return;
        }
        if (row < 0 || !_stacks.TryGetValue(row, out var st) || st.Count == 0) return;
        var before = st.Pop();
        if (before == null) return;
        var r = _rows[row];
        r.Calls++;
        var seen = new HashSet<uint>();
        Walk(m, (addr, slot, len) =>
        {
            if (!seen.Add(addr) || before.Remove(addr)) return;
            r.Added++;
            r.Words += len;
            r.MinSlot = Math.Min(r.MinSlot, slot);
            r.MaxSlot = Math.Max(r.MaxSlot, slot);
            r.MinAddr = Math.Min(r.MinAddr, addr);
            r.MaxAddr = Math.Max(r.MaxAddr, addr);
            byte code = (byte)(m.ReadU32(addr + 4) >> 24);
            r.Codes[code] = r.Codes.GetValueOrDefault(code) + 1;
            r.Sizes[len] = r.Sizes.GetValueOrDefault(len) + 1;
        });
        before.ExceptWith(seen);
        r.Removed += before.Count;
        if (row < Calls.Length && Calls[row].Callee == 0x80043940) { _frames++; Report(); }
    }

    struct M0; struct M1; struct M2; struct M3; struct M4; struct M5; struct M6; struct M7;
    struct M8; struct M9; struct M10; struct M11; struct M12; struct M13; struct M14; struct M15;
    static readonly Type[] SlotMarkers =
    [
        typeof(M0), typeof(M1), typeof(M2), typeof(M3), typeof(M4), typeof(M5), typeof(M6), typeof(M7),
        typeof(M8), typeof(M9), typeof(M10), typeof(M11), typeof(M12), typeof(M13), typeof(M14), typeof(M15),
    ];

    // A function reached from anywhere is reported per call site too.
    static readonly Dictionary<(int, uint), int> _siteRows = new();
    static int BySite(int row, uint site)
    {
        if (row < 0) return -1;
        if (_siteRows.TryGetValue((row, site), out int r)) return r;
        r = _rows.Count;
        _rows.Add(new Row { Label = $"{_rows[row].Label.Trim()}@{site:X8}" });
        _siteRows[(row, site)] = r;
        return r;
    }

    static class Slot<T>
    {
        public static int Row = -1;
        public static void Pre(CpuContext c, IMemory m) => Enter(BySite(Row, c.RA - 8), m);
        public static void Post(CpuContext c, IMemory m) => Leave(BySite(Row, c.RA - 8), m);
    }

    // Every packet hanging from the table, from its last entry (the one DrawOTag is
    // handed) to the terminator; a slot is the table entry a packet follows. The
    // 8-entry table at *0x801A91B8 is walked too (slots reported as 9000+), since
    // the swap splices it in only at the end of the frame.
    static HashSet<uint>? Walk(IMemory m, Action<uint, int, int>? each)
    {
        var set = each == null ? new HashSet<uint>() : null;
        WalkOne(m, m.ReadU32(OtPointer), OtWords, 0, set, each);
        WalkOne(m, m.ReadU32(FrontPointer), FrontWords, 9000, set, each);
        return set;
    }

    static bool InTable(uint p, uint ot, int words) => p >= ot && p < ot + (uint)words * 4;

    static void WalkOne(IMemory m, uint ot, int words, int slotBase, HashSet<uint>? set, Action<uint, int, int>? each)
    {
        if ((ot & 0xFF000000) != 0x80000000) return;
        uint lo = ot, hi = ot + (uint)words * 4;
        uint p = ot + (uint)(words - 1) * 4;
        int slot = words - 1;
        for (int guard = 0; guard < MaxPackets; guard++)
        {
            uint tag = m.ReadU32(p);
            if (p >= lo && p < hi) slot = (int)((p - lo) / 4);
            else if (slotBase == 0 && p >= m.ReadU32(FrontPointer) && p < m.ReadU32(FrontPointer) + FrontWords * 4) break;
            else if (slotBase != 0 && InTable(p, m.ReadU32(OtPointer), OtWords)) break;
            else
            {
                int len = (int)(tag >> 24);
                if (set != null) set.Add(p); else each!(p, slotBase + slot, len);
            }
            uint next = tag & 0xFFFFFF;
            if (next == 0xFFFFFF) break;
            p = 0x80000000 | next;
        }
    }

    static void Report()
    {
        long now = Environment.TickCount64;
        if (_windowStart < 0) { _windowStart = now; _frames = 0; return; }
        double s = (now - _windowStart) / 1000.0;
        if (s < 5.0) return;
        Console.WriteLine($"[KF3] geoprobe: {_frames / s:0.0} frames/s; per call: packets added (removed), words, slots, codes, packet sizes");
        foreach (var r in _rows)
        {
            if (r.Calls == 0) continue;
            double f = Math.Max(1, _frames);
            string codes = string.Join(" ", r.Codes.OrderByDescending(kv => kv.Value).Take(6)
                .Select(kv => $"{kv.Key:X2}:{kv.Value / (double)r.Calls:0.#}"));
            string sizes = string.Join(" ", r.Sizes.OrderByDescending(kv => kv.Value).Take(4)
                .Select(kv => $"{kv.Key}w:{kv.Value / (double)r.Calls:0.#}"));
            if (_timing)
            {
                Console.WriteLine($"[KF3] geoprobe: {r.Label} {r.Calls / f:0.##}/frame " +
                                  $"{r.Ticks * 1000.0 / System.Diagnostics.Stopwatch.Frequency / f:0.000} ms/frame inclusive");
                r.Calls = r.Ticks = 0;
                continue;
            }
            Console.WriteLine($"[KF3] geoprobe: {r.Label} {r.Calls / f:0.##}/frame +{r.Added / (double)r.Calls:0.#} (-{r.Removed / (double)r.Calls:0.#})" +
                              $" {r.Words / (double)r.Calls:0.#}w" +
                              (r.MaxSlot >= 0 ? $" slots {r.MinSlot}..{r.MaxSlot} at {r.MinAddr:X8}..{r.MaxAddr:X8}" : "") +
                              (codes.Length > 0 ? $" [{codes}] [{sizes}]" : ""));
            r.Calls = r.Added = r.Removed = r.Words = 0;
            r.MinSlot = int.MaxValue; r.MaxSlot = -1; r.MinAddr = uint.MaxValue; r.MaxAddr = 0;
            r.Codes.Clear(); r.Sizes.Clear();
        }
        _windowStart = now;
        _frames = 0;
    }
}
