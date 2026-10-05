using System.Reflection;
using RecompOne.Runtime;
using RecompOne.Runtime.Dispatch;
using RecompOne.Runtime.Context;
using RecompOne.Runtime.Memory;
using RecompOne.Runtime.Modding;

namespace Kf3;

/// <summary>Game-owned submission and its literal numerical reference.</summary>
public static partial class NativeScene
{
    static readonly uint[] Addresses = [0x8003BFD0, 0x8003BE34, 0x8003BB04, 0x8003E34C, 0x8003F304,
        0x800400AC, 0x8003DF50, 0x80037BEC, 0x80038844, 0x80039428];
    static readonly Action<CpuContext, PSMemory>[] Bodies = [RunWalk, RunCell, RunHalf, RunSubmit, RunFront,
        RunSky, RunArm, RunBlendAssembler, RunFrontAssembler, RunSkyAssembler];
    static readonly Differential[] Checks = Addresses.Select(a => new Differential("native-scene", $"func_{a:X8}", 0x2000)).ToArray();
    static readonly bool[] Queued = new bool[Addresses.Length];
    static readonly ModInfo Self = new() { Id = "kf3.native-scene", Name = "Native scene submission", Version = "1.0" };
    static int _mode, _verifyDepth;
    public static int Setting
    {
        get => _mode;
        // The trace hooks are queued even while reference rendering is selected,
        // allowing an inner-function comparison without restarting the game.
        set => _mode = Math.Clamp(value, 0, 2);
    }
    static HashSet<uint>? _verifyFunctions;
    public static bool Verifying => _verifyDepth > 0 || _mode == 2;
    public static bool Enabled => _mode == 1;
    public static readonly long[] Calls = new long[Addresses.Length];
    public static void Install()
    {
        _mode = Environment.GetEnvironmentVariable("KF3_NATIVE_SCENE")?.Trim().ToLowerInvariant() switch
        { "1" or "on" => 1, "verify" => 2, _ => 0 };
        if (Environment.GetEnvironmentVariable("KF3_NATIVE_SCENE") == null && GpuWorld.Mode != 0) _mode = 1;
        if (Environment.GetEnvironmentVariable("KF3_NATIVE_SCENE_VERIFY_FUNCS") is { Length: > 0 } list)
            _verifyFunctions = list.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Select(s => Convert.ToUInt32(s.Replace("0x", "", StringComparison.OrdinalIgnoreCase), 16) | 0x80000000u).ToHashSet();
        HookAttach.OnOverlayLoad("native scene", Attach);
    }
    static bool Attach()
    {
        SymbolRegistry.Build();
        var targets = Addresses.Select(a => SymbolRegistry.Resolve("game", null, a)).ToArray();
        if (targets.Any(t => t == null)) return false;
        NativeSceneVerification.Attach();
        for (int i = 0; i < targets.Length; i++)
        {
            if (Queued[i]) continue;
            var slot = typeof(Slot<>).MakeGenericType(Markers[i]);
            slot.GetField("Id")!.SetValue(null, i);
            Queued[i] = HookManager.AddReplace(Self, targets[i]!, slot.GetMethod("Replace")!);
        }
        HookManager.Commit();
        bool ok = Queued.All(q => q) && targets.All(HookAttach.Installed);
        Console.WriteLine($"[KF3] native scene: {targets.Count(HookAttach.Installed)}/{targets.Length} committed; mode {_mode}");
        return ok;
    }
    static void Replace(int id, Action<CpuContext, IMemory> orig, CpuContext c, IMemory m)
    {
        if (_mode == 0 || _verifyDepth > 0 || _mode == 2 && _verifyFunctions != null && !_verifyFunctions.Contains(Addresses[id])
            || RecompOne.Runtime.Pgxp.Pgxp.CpuTracking || m is not PSMemory mem)
        { orig(c, m); return; }
        Calls[id]++;
        if (_mode == 2)
        {
            _verifyDepth++;
            try { NativeSceneVerification.Run(Checks[id], orig, c, mem, Bodies[id], Addresses[id]); }
            finally { _verifyDepth--; }
        }
        else Bodies[id](c, mem);
    }
    struct M0; struct M1; struct M2; struct M3; struct M4; struct M5; struct M6; struct M7; struct M8; struct M9;
    static readonly Type[] Markers = [typeof(M0),typeof(M1),typeof(M2),typeof(M3),typeof(M4),typeof(M5),typeof(M6),typeof(M7),typeof(M8),typeof(M9)];
    static class Slot<T>
    {
        public static int Id = -1;
        public static void Replace(Action<CpuContext, IMemory> orig, CpuContext c, IMemory m) => NativeScene.Replace(Id, orig, c, m);
    }
}
