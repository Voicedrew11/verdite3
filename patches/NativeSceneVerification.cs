using System.Reflection;
using System.Runtime.InteropServices;
using RecompOne.Runtime.Context;
using RecompOne.Runtime.Memory;
using RecompOne.Runtime.Modding;
using Game = Recompiled.KingsField3_game;

namespace Kf3;

/// <summary>Full integer-register, stack and relevant-callee checks supplement Differential.</summary>
public static class NativeSceneVerification
{
    static readonly string[] Names = ["RotMatrix", "ScaleMatrix", "SquareRoot0", "func_800166F4",
        "func_80017158", "func_800171B0", "func_80018F5C", "func_80035358", "func_80035CA4",
        "func_800366A8", "func_80037BEC", "func_80038844", "func_80039428", "func_80039D50",
        "func_8003AB04", "func_8003BB04", "func_800431E8", "ratan2"];
    readonly record struct Call(int Id, uint Ra, uint Sp, uint A0, uint A1, uint A2, uint A3);
    static readonly List<Call> Reference = new(), Native = new();
    static int _phase;
    static bool _queued;
    static long _calls, _registers, _stack, _order, _report;
    static readonly ModInfo Self = new() { Id = "kf3.scene-verification", Name = "Native scene state verifier", Version = "1.0" };
    public static void Attach()
    {
        if (_queued) return;
        var targets = Names.Select(n => typeof(Game).GetMethod(n)).ToArray();
        if (targets.Any(t => t == null)) throw new InvalidOperationException("native reference callee is missing");
        for (int i = 0; i < Names.Length; i++)
        {
            Type type = typeof(Site<>).MakeGenericType(Markers[i]);
            type.GetField(nameof(Site<N0>.Id))!.SetValue(null, i);
            HookManager.AddPre(Self, targets[i]!, type.GetMethod(nameof(Site<N0>.Pre))!, order: int.MinValue);
        }
        _queued = true;
    }
    internal static void Run(Differential check, Action<CpuContext, IMemory> original, CpuContext c, PSMemory m,
        Action<CpuContext, PSMemory> implementation, uint routine)
    {
        int hi = (int)(c.SP & 0x1FFFFF), lo = Math.Max(0, hi - 0x2000);
        byte[] stack = new byte[hi - lo]; CpuSnapshot expected = default;
        Reference.Clear(); Native.Clear();
        try
        {
            check.Run((cc, mm) =>
            {
                _phase = 1; original(cc, mm); _phase = 0;
                expected = cc.Snapshot(); m.Ram.Slice(lo, stack.Length).CopyTo(stack);
            }, c, m, (cc, mm) =>
            {
                _phase = 2; implementation(cc, mm); _phase = 0;
                var actual = cc.Snapshot();
                bool regs = MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref actual, 1))
                    .SequenceEqual(MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref expected, 1)));
                bool ram = mm.Ram.Slice(lo, stack.Length).SequenceEqual(stack);
                bool order = Reference.SequenceEqual(Native);
                _calls++; if (!regs) _registers++; if (!ram) _stack++; if (!order) _order++;
                if ((!regs || !ram || !order) && (_registers + _stack + _order) < 9)
                {
                    Console.Error.WriteLine($"[KF3] full native state {routine:X8}: registers={regs} stack={ram} calls={order}");
                    if (!regs)
                        foreach (var field in typeof(CpuSnapshot).GetFields())
                            if (!Equals(field.GetValue(actual), field.GetValue(expected)))
                                Console.Error.WriteLine($"[KF3]   {field.Name}: original={field.GetValue(expected):X} native={field.GetValue(actual):X}");
                    if (!order)
                    {
                        int at = 0; while (at < Reference.Count && at < Native.Count && Reference[at] == Native[at]) at++;
                        Console.Error.WriteLine($"[KF3]   call mismatch at {at}: original={(at < Reference.Count ? Reference[at].ToString() : "end")} native={(at < Native.Count ? Native[at].ToString() : "end")}");
                    }
                }
            });
        }
        finally { _phase = 0; }
        if (Environment.TickCount64 < _report) return;
        _report = Environment.TickCount64 + 5000;
        Console.WriteLine($"[KF3] full native state: {_calls} comparisons, {_registers} full-register, {_stack} stack, {_order} call-order mismatches (cumulative)");
    }
    static void Note(int id, CpuContext c)
    {
        if (_phase == 0) return;
        (_phase == 1 ? Reference : Native).Add(new(id, c.RA, c.SP, c.A0, c.A1, c.A2, c.A3));
    }
    struct N0; struct N1; struct N2; struct N3; struct N4; struct N5; struct N6; struct N7; struct N8;
    struct N9; struct N10; struct N11; struct N12; struct N13; struct N14; struct N15; struct N16; struct N17;
    static readonly Type[] Markers = [typeof(N0), typeof(N1), typeof(N2), typeof(N3), typeof(N4), typeof(N5),
        typeof(N6), typeof(N7), typeof(N8), typeof(N9), typeof(N10), typeof(N11), typeof(N12), typeof(N13),
        typeof(N14), typeof(N15), typeof(N16), typeof(N17)];
    static class Site<T>
    {
        public static int Id = -1;
        public static void Pre(CpuContext c, IMemory m) => Note(Id, c);
    }
}
