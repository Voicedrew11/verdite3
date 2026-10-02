using System.Reflection;
using RecompOne.Runtime;
using RecompOne.Runtime.Context;
using RecompOne.Runtime.Memory;
using RecompOne.Runtime.Modding;

namespace Kf3;

/// <summary>
/// KF3_MAPCOVERAGE=1: how the GTE vertex address map covers the frame, by the
/// routine that wrote each packet. Pre/post hooks record the primitive-buffer range
/// each packet writer bumps (the scratchpad cursor at +0x14, +0x18 its end); a pre
/// on DrawOTag walks the ordering table and asks <see cref="GteVertexMap.Peek"/>
/// about every polygon packet's vertex words, without moving the map's own counters.
/// One line a row every 2 s: packets/s, vertices/s, hit%. The map is assumed to be
/// on already (KF3_PERSPECTIVE or KF3_SUBPIXEL); off, every vertex misses. See
/// "Unit 2" in docs/PICTURE.md.
/// </summary>
public static class MapCoverage
{
    const uint Pad = 0x1F800000;
    const uint Cursor = Pad + 0x14;
    // Stage 15 call #5: clears both tables and resets the primitive buffer, so the
    // previous frame's recorded ranges are dropped here.
    const uint ResetRoutine = 0x80035630;
    const uint DrawOTagAddr = 0x8007A104;

    enum Owner { Map, NearMap, Lit, Hud, NearModel, Arm, Blend, Front, Sky, Overlay, Other, Count }

    enum Kind { Range, Hud, Arm }

    sealed class Target
    {
        public uint Addr;
        public int Row = -1;
        public Kind Kind = Kind.Range;
        public bool Split;   // the lit assembler: hud/arm pick the row
    }

    // The packet-writing routines, all GAME.EXE.
    static readonly Target[] _targets =
    [
        new() { Addr = 0x80039D50, Row = (int)Owner.Map },        // map bulk
        new() { Addr = 0x8003AB04, Row = (int)Owner.NearMap },    // near map
        new() { Addr = 0x80035CA4, Row = (int)Owner.Lit, Split = true },
        new() { Addr = 0x800366A8, Row = (int)Owner.NearModel },  // models' near submit
        new() { Addr = 0x8003DF50, Row = (int)Owner.Arm, Kind = Kind.Arm },
        new() { Addr = 0x80037BEC, Row = (int)Owner.Blend },
        new() { Addr = 0x80038844, Row = (int)Owner.Front },
        new() { Addr = 0x80039428, Row = (int)Owner.Sky },
        new() { Addr = 0x80041E68, Row = (int)Owner.Overlay },
        new() { Addr = 0x8003C35C, Kind = Kind.Hud },             // flag only: HUD's models
    ];

    static readonly string[] _names =
        ["map", "nearmap", "lit", "hud", "nearmodel", "arm", "blend", "front", "sky", "overlay", "other"];

    // One routine's packet range: [Lo, Hi) of the primitive buffer, physical.
    struct Span { public uint Lo, Hi; public int Row; }

    static readonly List<Span> _spans = [];
    static readonly uint[] _start = new uint[_targets.Length];
    static readonly bool[] _have = new bool[_targets.Length];
    static readonly int[] _pending = new int[_targets.Length];
    static int _inHud, _inArm;

    static readonly long[] _packets = new long[(int)Owner.Count];
    static readonly long[] _vertices = new long[(int)Owner.Count];
    static readonly long[] _hits = new long[(int)Owner.Count];

    static long _windowStart = -1;
    static bool _warnedMap;
    static bool _installed;
    static bool _probe;

    static readonly ModInfo _self = new()
    {
        Id = "kf3.mapcov",
        Name = "Address map coverage",
        Version = "1.0",
        Description = "The GTE vertex map's coverage, by packet-writing routine.",
    };

    /// <summary>KF3_MAPCOVERAGE: live. While false each hook returns at once.</summary>
    public static bool ProbeOn
    {
        get => _probe;
        set
        {
            if (_probe == value) return;
            _probe = value;
            _spans.Clear();
            _inHud = _inArm = 0;
            Array.Clear(_have);
            Array.Clear(_packets);
            Array.Clear(_vertices);
            Array.Clear(_hits);
            _warnedMap = false;
            _windowStart = Environment.TickCount64;
        }
    }

    public static void Configure(string? on)
    {
        if (!string.IsNullOrWhiteSpace(on)) ProbeOn = on.Trim() is not ("0" or "off");
    }

    public static void Install() => HookAttach.OnOverlayLoad("mapcov", Attach);

    static bool Attach()
    {
        if (_installed) return true;
        SymbolRegistry.Build();
        var self = typeof(MapCoverage);
        var targets = new List<MethodInfo>();

        for (int i = 0; i < _targets.Length; i++)
        {
            var t = SymbolRegistry.Resolve("game", null, _targets[i].Addr);
            if (t == null) { Console.Error.WriteLine($"[KF3] mapcov: no game/0x{_targets[i].Addr:X8}"); continue; }
            var slot = typeof(Slot<>).MakeGenericType(Markers[i]);
            slot.GetField(nameof(Slot<int>.Id))!.SetValue(null, i);
            HookManager.AddPre(_self, t, slot.GetMethod(nameof(Slot<int>.Pre))!);
            HookManager.AddPost(_self, t, slot.GetMethod(nameof(Slot<int>.Post))!);
            targets.Add(t);
        }

        var reset = SymbolRegistry.Resolve("game", null, ResetRoutine);
        if (reset != null)
        {
            HookManager.AddPre(_self, reset, self.GetMethod(nameof(BeforeReset), BindingFlags.Public | BindingFlags.Static)!);
            targets.Add(reset);
        }
        else Console.Error.WriteLine($"[KF3] mapcov: no game/0x{ResetRoutine:X8}");

        var ot = SymbolRegistry.Resolve("game", null, DrawOTagAddr);
        if (ot != null)
        {
            HookManager.AddPre(_self, ot, self.GetMethod(nameof(BeforeDrawOTag), BindingFlags.Public | BindingFlags.Static)!);
            targets.Add(ot);
        }
        else Console.Error.WriteLine($"[KF3] mapcov: no game/0x{DrawOTagAddr:X8}");

        HookManager.Commit();
        int n = targets.Count(HookAttach.Installed);
        _installed = n == targets.Count;
        Console.WriteLine($"[KF3] mapcov: {(ProbeOn ? "on" : "off")}, {n}/{targets.Count} function(s) hooked");
        return _installed;
    }

    // ---- the packet writers --------------------------------------------------

    public static void Pre(int target, CpuContext c, IMemory m)
    {
        if (!ProbeOn) return;
        var t = _targets[target];
        if (t.Kind == Kind.Hud) { _inHud++; return; }
        if (t.Kind == Kind.Arm) _inArm++;
        _start[target] = m.ReadU32(Cursor) & RecompOne.Runtime.Runtime.RamWordMask;
        _pending[target] = RowFor(target);
        _have[target] = true;
    }

    public static void Post(int target, CpuContext c, IMemory m)
    {
        if (!ProbeOn) return;
        var t = _targets[target];
        if (t.Kind == Kind.Hud) { if (_inHud > 0) _inHud--; return; }
        if (_have[target])
        {
            uint end = m.ReadU32(Cursor) & RecompOne.Runtime.Runtime.RamWordMask;
            if (end > _start[target]) _spans.Add(new Span { Lo = _start[target], Hi = end, Row = _pending[target] });
            _have[target] = false;
        }
        if (t.Kind == Kind.Arm && _inArm > 0) _inArm--;
    }

    static int RowFor(int target)
    {
        var t = _targets[target];
        if (!t.Split) return t.Row;
        return _inHud > 0 ? (int)Owner.Hud : _inArm > 0 ? (int)Owner.Arm : (int)Owner.Lit;
    }

    // ---- the frame boundary and the walk -------------------------------------

    public static void BeforeReset(CpuContext c, IMemory m)
    {
        if (!ProbeOn) return;
        _spans.Clear();
        _inHud = _inArm = 0;
        Array.Clear(_have);
    }

    public static void BeforeDrawOTag(CpuContext c, IMemory m)
    {
        if (!ProbeOn) return;
        Walk(m, c.A0);
        Report();
    }

    static void Walk(IMemory m, uint ot)
    {
        uint mask = RecompOne.Runtime.Runtime.RamWordMask;
        uint addr = ot & mask;
        for (int guard = 0; guard < 0x100000; guard++)
        {
            uint header = m.ReadU32(addr);
            uint body = addr + 4u;
            uint cmd = m.ReadU32(body) >> 24;
            if (cmd >= 0x20u && cmd <= 0x3Fu) Count(m, addr, body, cmd);
            uint next = header & 0xFFFFFFu;
            if (next == 0xFFFFFFu || (next & 0x800000u) != 0) break;
            addr = next & mask;
            if (addr == 0u) break;
        }
    }

    static void Count(IMemory m, uint pkt, uint body, uint cmd)
    {
        int row = OwnerOf(pkt);
        _packets[row]++;

        int n = (cmd & 0x08u) != 0 ? 4 : 3;          // bit 27: quad
        bool gouraud = (cmd & 0x10u) != 0;           // bit 28: shaded
        bool tex = (cmd & 0x04u) != 0;               // bit 26: textured
        uint off = 4u;                               // body starts at the command word; first xy follows it
        for (int k = 0; k < n; k++)
        {
            if (k > 0) off += 4u + (tex ? 4u : 0u) + (gouraud ? 4u : 0u);
            uint at = body + off;
            uint word = m.ReadU32(at);
            _vertices[row]++;
            if (GteVertexMap.Peek(at, word, out _)) _hits[row]++;
        }
    }

    static int OwnerOf(uint pkt)
    {
        int best = -1;
        uint bestLo = 0, bestHi = uint.MaxValue;
        for (int i = 0; i < _spans.Count; i++)
        {
            var s = _spans[i];
            if (pkt < s.Lo || pkt >= s.Hi) continue;
            if (s.Lo > bestLo || (s.Lo == bestLo && s.Hi < bestHi))
            {
                best = s.Row; bestLo = s.Lo; bestHi = s.Hi;
            }
        }
        return best < 0 ? (int)Owner.Other : best;
    }

    static void Report()
    {
        long now = Environment.TickCount64;
        if (_windowStart < 0) { _windowStart = now; return; }
        double s = (now - _windowStart) / 1000.0;
        if (s < 2.0) return;

        if (!_warnedMap && !GteVertexMap.Active)
        {
            _warnedMap = true;
            Console.Error.WriteLine("[KF3] mapcov: the address map is off -- enable KF3_PERSPECTIVE or KF3_SUBPIXEL");
        }

        for (int i = 0; i < (int)Owner.Count; i++)
        {
            double v = _vertices[i];
            double hit = v == 0.0 ? 0.0 : 100.0 * _hits[i] / v;
            Console.WriteLine($"[KF3] mapcov {_names[i]}: {_packets[i] / s:F0}/s, {v / s:F0}/s, {hit:F1}%.");
            _packets[i] = _vertices[i] = _hits[i] = 0;
        }
        Console.Out.Flush();
        _windowStart = now;
    }

    // One distinct pair of methods a target, so the same delegate is not shared.
    struct M0; struct M1; struct M2; struct M3; struct M4;
    struct M5; struct M6; struct M7; struct M8; struct M9;

    static readonly Type[] Markers =
    [
        typeof(M0), typeof(M1), typeof(M2), typeof(M3), typeof(M4),
        typeof(M5), typeof(M6), typeof(M7), typeof(M8), typeof(M9),
    ];

    static class Slot<T>
    {
        public static int Id;
        public static void Pre(CpuContext c, IMemory m) => MapCoverage.Pre(Id, c, m);
        public static void Post(CpuContext c, IMemory m) => MapCoverage.Post(Id, c, m);
    }
}
