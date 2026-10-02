using System.Reflection;
using RecompOne.Runtime.Context;
using RecompOne.Runtime.Memory;
using RecompOne.Runtime.Modding;

namespace Kf3;

/// <summary>
/// KF3_STAGEPROBE=1: which main-loop stages submit primitives. Each stage's ordering
/// table words are diffed across its call, and its calls and presents counted; one
/// line every five seconds. See "The session and the main loop" in docs/GAME_INTERNALS.md.
/// </summary>
public static class StageProbe
{
    // The main loop at 0x80014F24, in call order, then the frame gate.
    static readonly uint[] Stages =
    [
        0x800341E8, 0x80034180, 0x80047010, 0x80030FCC, 0x80052E5C, 0x8005BC50, 0x8005EB20,
        0x80018358, 0x80061940, 0x8002B330, 0x800156BC, 0x80034300, 0x80018CD0, 0x80015A48,
        0x800422B8, 0x80019614,
    ];

    const uint OtPointer = 0x801A9174;   // u32: the ordering table being built
    const int OtWords = 0x2000;

    static readonly ModInfo _self = new() { Id = "kf3.stageprobe", Name = "Stage probe", Version = "1.0" };

    static readonly long[] _calls = new long[Stages.Length];
    static readonly long[] _otWrites = new long[Stages.Length];
    static readonly long[] _presents = new long[Stages.Length];
    static readonly uint[][] _before = new uint[Stages.Length][];
    static readonly long[] _vsAt = new long[Stages.Length];
    static long _windowStart = -1;

    public static void Install()
    {
        if (Environment.GetEnvironmentVariable("KF3_STAGEPROBE") != "1") return;
        for (int i = 0; i < Stages.Length; i++) _before[i] = new uint[OtWords];
        HookAttach.OnOverlayLoad("stageprobe", Attach);
    }

    static bool Attach()
    {
        SymbolRegistry.Build();
        var pre = typeof(StageProbe).GetMethod(nameof(Pre), BindingFlags.Public | BindingFlags.Static)!;
        var post = typeof(StageProbe).GetMethod(nameof(Post), BindingFlags.Public | BindingFlags.Static)!;
        var targets = new List<MethodInfo>();
        for (int i = 0; i < Stages.Length; i++)
        {
            var t = SymbolRegistry.Resolve("game", null, Stages[i]);
            if (t == null) { Console.Error.WriteLine($"[KF3] stageprobe: no game/0x{Stages[i]:X8}"); return false; }
            HookManager.AddPre(_self, t, pre);
            HookManager.AddPost(_self, t, post);
            targets.Add(t);
        }
        HookManager.Commit();
        int n = targets.Count(HookAttach.Installed);
        Console.WriteLine($"[KF3] stageprobe: {n}/{Stages.Length} stages hooked");
        return n == Stages.Length;
    }

    static readonly Stack<int> _stack = new();

    public static void Pre(CpuContext c, IMemory m)
    {
        int i = Index(c);
        _stack.Push(i);
        if (i < 0) return;
        _calls[i]++;
        _vsAt[i] = RecompOne.Runtime.Sdk.LibEtc.VSyncCalls;
        Snapshot(m, _before[i]);
    }

    public static void Post(CpuContext c, IMemory m)
    {
        int i = _stack.Count > 0 ? _stack.Pop() : -1;
        if (i < 0) return;
        _presents[i] += RecompOne.Runtime.Sdk.LibEtc.VSyncCalls - _vsAt[i];
        var now = new uint[OtWords];
        Snapshot(m, now);
        for (int k = 0; k < OtWords; k++) if (now[k] != _before[i][k]) _otWrites[i]++;
        if (i == Stages.Length - 1) Report();
    }

    static void Snapshot(IMemory m, uint[] into)
    {
        uint ot = m.ReadU32(OtPointer);
        if ((ot & 0xFF000000) != 0x80000000) { Array.Clear(into); return; }
        for (int k = 0; k < OtWords; k++) into[k] = m.ReadU32(ot + (uint)k * 4);
    }

    // The return address names the call site in the main loop, which names the stage.
    static int Index(CpuContext c)
    {
        uint pc = c.RA - 8;
        return pc switch
        {
            0x80014F24 => 0, 0x80014F2C => 1, 0x80014F34 => 2, 0x80014F3C => 3, 0x80014F44 => 4,
            0x80014F4C => 5, 0x80014F5C => 6, 0x80014F64 => 7, 0x80014F6C => 8, 0x80014F78 => 9,
            0x80014F84 => 10, 0x80014F8C => 11, 0x80014F94 => 12, 0x80014F9C => 13, 0x80014FA8 => 14,
            0x800428E8 => 15,
            _ => -1,
        };
    }

    static void Report()
    {
        long now = Environment.TickCount64;
        if (_windowStart < 0) { _windowStart = now; return; }
        double s = (now - _windowStart) / 1000.0;
        if (s < 5.0) return;
        var parts = new List<string>();
        for (int i = 0; i < Stages.Length; i++)
        {
            parts.Add($"{i + 1}:{Stages[i]:X8} {_calls[i] / s:0.0}/s ot {_otWrites[i] / Math.Max(1, _calls[i])} vs {_presents[i]}");
            _calls[i] = _otWrites[i] = _presents[i] = 0;
        }
        Console.WriteLine("[KF3] stageprobe: " + string.Join(" | ", parts));
        _windowStart = now;
    }
}
