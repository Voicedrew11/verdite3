using System.Reflection;
using System.Text.Json;
using RecompOne.Runtime;
using RecompOne.Runtime.Context;
using RecompOne.Runtime.Events;
using RecompOne.Runtime.Memory;
using RecompOne.Runtime.Modding;

namespace Kf3;

/// <summary>Caller/domain census, including packets without an attributed writer.</summary>
public static class SceneCensus
{
    const uint Pad = 0x1F800000;
    static readonly uint[] Addresses =
    [
        0x800422B8, 0x8003C35C, 0x8004290C, 0x8003DF50,
        0x8003BFD0, 0x8003BE34, 0x8003BB04, 0x80040AE4,
        0x8003E34C, 0x8003F304, 0x800400AC,
        0x80039D50, 0x8003AB04, 0x80035CA4, 0x800366A8,
        0x80037BEC, 0x80038844, 0x80039428,
        0x80041E68, 0x80041D9C,
        0x8003D280, 0x8003D38C, 0x8003D41C, 0x8003D568, 0x8003D64C, 0x8003D79C,
    ];
    static readonly string[] Names =
    [
        "scene", "hud-models", "preview", "arm", "map-walk", "cell", "half", "model-walk",
        "model-submit", "front-submit", "sky-submit", "map-bulk", "map-near", "model-lit",
        "model-near", "model-blend", "model-front", "sky", "hud-icons", "overlay",
        "screen0", "screen1", "screen2", "screen3", "screen4", "screen5",
    ];
    sealed class Row
    {
        public string Overlay { get; init; } = "";
        public int Area { get; init; }
        public string Domain { get; init; } = "";
        public uint Routine { get; init; }
        public uint Caller { get; init; }
        public long Calls { get; set; }
        public long Packets { get; set; }
        public long Bytes { get; set; }
        public long ProjectedVertices { get; set; }
        public Dictionary<byte, long> Codes { get; } = new();
        public Dictionary<uint, long> Flags { get; } = new();
        public Dictionary<int, long> Slots { get; } = new();
    }
    readonly record struct Entry(int Id, uint Caller, uint Cursor, uint PrimaryCursor, Row Row);
    readonly record struct PacketRange(uint Lo, uint Hi, Row Row);
    static readonly Stack<Entry> Stack = new();
    static readonly List<PacketRange> Ranges = new();
    static readonly Dictionary<(string, int, string, uint, uint), Row> Rows = new();
    static readonly HashSet<MethodInfo> Queued = new();
    static readonly ModInfo Self = new() { Id = "kf3.scene-census", Name = "Scene census", Version = "1.0" };
    static bool _on;
    static long _draws, _nextReport;
    static int _hud, _preview, _arm;
    public static void Install()
    {
        _on = Environment.GetEnvironmentVariable("KF3_SCENE_CENSUS") == "1";
        if (!_on) return;
        // An area can load inside a live scene. Its existing packets retain their
        // emitter provenance until FrameHead resets that primitive buffer.
        HookAttach.OnOverlayLoad("scene census", Attach);
    }
    static bool Attach()
    {
        SymbolRegistry.Build();
        var methods = Addresses.Select(a => SymbolRegistry.Resolve("game", null, a)).ToArray();
        var reset = SymbolRegistry.Resolve("game", null, 0x80035630);
        var draws = new[] { ("game", 0x8007A104u), ("open", 0x800166CCu), ("end", 0x80014428u) }
            .Select(t => SymbolRegistry.Resolve(t.Item1, null, t.Item2)).ToArray();
        if (methods.Any(t => t == null) || reset == null || draws.Any(t => t == null)) return false;
        for (int i = 0; i < methods.Length; i++)
        {
            var target = methods[i]!;
            if (!Queued.Add(target)) continue;
            var slot = typeof(Slot<>).MakeGenericType(Markers[i]);
            slot.GetField(nameof(Slot<M0>.Id))!.SetValue(null, i);
            HookManager.AddPre(Self, target, slot.GetMethod("Pre")!, order: int.MinValue + 2);
            HookManager.AddPost(Self, target, slot.GetMethod("Post")!, order: int.MaxValue - 2);
        }
        if (Queued.Add(reset)) HookManager.AddPost(Self, reset, typeof(SceneCensus).GetMethod(nameof(Reset))!);
        foreach (var t in draws)
            if (Queued.Add(t!)) HookManager.AddPre(Self, t!, typeof(SceneCensus).GetMethod(nameof(Draw))!, order: int.MinValue + 2);
        HookManager.Commit();
        bool ok = Queued.All(HookAttach.Installed);
        Console.WriteLine($"[KF3] scene census: {Queued.Count(HookAttach.Installed)}/{Queued.Count} committed");
        return ok;
    }
    static Row Get(uint routine, uint caller, string domain, IMemory m)
    {
        string overlay = AgentBeacon.Overlay;
        int area = overlay.StartsWith("fdat") ? m.ReadU8(AgentBeacon.Area) : -1;
        var key = (overlay, area, domain, routine, caller);
        if (!Rows.TryGetValue(key, out var row))
            Rows[key] = row = new() { Overlay = overlay, Area = area, Domain = domain, Routine = routine, Caller = caller };
        return row;
    }
    static void Enter(int id, CpuContext c, IMemory m)
    {
        if (id == 1) _hud++;
        if (id == 2) _preview++;
        if (id == 3) _arm++;
        string domain = _preview > 0 ? "preview" : _hud > 0 ? "hud" : _arm > 0 ? "arm" : Names[id];
        var row = Get(Addresses[id], c.RA - 8, domain, m);
        row.Calls++;
        if (id is 8 or 9)
        {
            uint flags = m.ReadU32(c.SP + 0x2C);
            row.Flags[flags] = row.Flags.GetValueOrDefault(flags) + 1;
        }
        // These assemblers own RTPS on every source vertex. Submitters are counted separately.
        if (id is 11 or 12 or 14)
        {
            uint header = id == 11 ? m.ReadU32(Pad + 0x10) + 12 + (c.A0 & 0xFFFF) * 28 : m.ReadU32(Pad + 0x24);
            if (InRam(header, 28)) row.ProjectedVertices += Math.Min(m.ReadU32(header + 4), 8192);
        }
        Stack.Push(new(id, c.RA - 8, m.ReadU32(Pad + 0x14) & 0x1FFFFF, PrimaryCursor(m), row));
    }
    static void Leave(int id, CpuContext c, IMemory m)
    {
        if (Stack.Count == 0 || Stack.Peek().Id != id)
        {
            Console.Error.WriteLine($"[KF3] scene census: unbalanced scope {Names[id]}");
            Stack.Clear(); _hud = _preview = _arm = 0;
            return;
        }
        var entry = Stack.Pop();
        uint end = m.ReadU32(Pad + 0x14) & 0x1FFFFF;
        if (id >= 11 && end > entry.Cursor) Ranges.Add(new(entry.Cursor, end, entry.Row));
        // Presentation emitters allocate through the frame header rather than the
        // assemblers' scratchpad lane. Bracket that cursor too.
        uint primary = PrimaryCursor(m);
        if (id >= 18 && entry.PrimaryCursor > 0 && primary > entry.PrimaryCursor)
            Ranges.Add(new(entry.PrimaryCursor, primary, entry.Row));
        if (id == 1) _hud--;
        if (id == 2) _preview--;
        if (id == 3) _arm--;
    }
    public static void Reset(CpuContext c, IMemory m) => Ranges.Clear();
    static uint PrimaryCursor(IMemory m)
    {
        uint header = m.ReadU32(0x80199170);
        return InRam(header, 12) ? m.ReadU32(header + 8) & 0x1FFFFF : 0;
    }
    static bool InRam(uint p, uint bytes) => (p & 0x1FFFFF) + bytes <= 0x200000 && (p & 0xFF000000) is 0x80000000 or 0;
    public static void Draw(CpuContext c, IMemory m)
    {
        _draws++;
        uint p = c.A0 & 0x1FFFFF;
        int slot = -1;
        var seen = new HashSet<uint>();
        for (int guard = 0; guard < 65536 && InRam(p, 4) && seen.Add(p); guard++)
        {
            uint tag = m.ReadU32(p);
            int words = (int)(tag >> 24);
            if (words == 0) slot++;
            else if (InRam(p, (uint)(words + 1) * 4))
            {
                Row? row = null;
                foreach (var range in Ranges)
                    if (p >= range.Lo && p < range.Hi && (row == null || range.Row.Routine != row.Routine)) row = range.Row;
                row ??= Get(0, c.RA - 8, "unattributed-packet", m);
                row.Packets++;
                row.Bytes += (words + 1) * 4;
                byte code = (byte)(m.ReadU32(p + 4) >> 24);
                row.Codes[code] = row.Codes.GetValueOrDefault(code) + 1;
                row.Slots[slot] = row.Slots.GetValueOrDefault(slot) + 1;
            }
            uint next = tag & 0xFFFFFF;
            if (next == 0xFFFFFF || (next & 0x800000) != 0) break;
            p = next;
        }
        long now = Environment.TickCount64;
        if (now < _nextReport) return;
        _nextReport = now + 5000;
        Console.WriteLine($"[KF3] scene census: {_draws} DrawOTag calls; {Rows.Count} caller/domain rows; " +
            $"{Rows.Values.Where(r => r.Domain == "unattributed-packet").Sum(r => r.Packets)} unattributed packets (cumulative)");
        if (Environment.GetEnvironmentVariable("KF3_SCENE_CENSUS_FILE") is { Length: > 0 } path)
            File.WriteAllText(path, JsonSerializer.Serialize(new { draws = _draws, rows = Rows.Values }, new JsonSerializerOptions { WriteIndented = true }));
    }
    struct M0; struct M1; struct M2; struct M3; struct M4; struct M5; struct M6; struct M7;
    struct M8; struct M9; struct M10; struct M11; struct M12; struct M13; struct M14; struct M15;
    struct M16; struct M17; struct M18; struct M19; struct M20; struct M21; struct M22; struct M23; struct M24; struct M25;
    static readonly Type[] Markers = [typeof(M0),typeof(M1),typeof(M2),typeof(M3),typeof(M4),typeof(M5),typeof(M6),typeof(M7),
        typeof(M8),typeof(M9),typeof(M10),typeof(M11),typeof(M12),typeof(M13),typeof(M14),typeof(M15),typeof(M16),typeof(M17),
        typeof(M18),typeof(M19),typeof(M20),typeof(M21),typeof(M22),typeof(M23),typeof(M24),typeof(M25)];
    static class Slot<T>
    {
        public static int Id = -1;
        public static void Pre(CpuContext c, IMemory m) => Enter(Id, c, m);
        public static void Post(CpuContext c, IMemory m) => Leave(Id, c, m);
    }
}
