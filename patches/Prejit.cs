using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using RecompOne.Runtime.Context;
using RecompOne.Runtime.Dispatch;
using RecompOne.Runtime.Events;
using RecompOne.Runtime.Memory;
using RecompOne.Runtime.Modding;

namespace Kf3;

/// <summary>
/// Compile the recompiled code before the game runs it, on background threads,
/// so the first frames of an area, a creature's first action and a first pose do
/// not stop to JIT it. Verdite2's <c>Prejit</c>, ported.
///
///     KF3_PREJIT=0          leave every method to be compiled on first call
///     KF3_PREJIT_PROBE=1    a line per batch as the pass reaches its end
///     KF3_PREJIT_THREADS=n  threads for the pass (a quarter of the cores, 1-4)
///
/// ## The defect
///
/// `TieredCompilationQuickJit` is off and must stay off -- tier-up recompiles a
/// hooked method and MonoMod's detour does not follow -- so **every** recompiled
/// function, patch and runtime method is compiled by the full optimizing JIT the
/// first time it is called, on the thread that called it. Measured with
/// `KF3_PROFILE_SPIKE=8` before this pass: 260 ms at the first frames of play
/// (stages 3-5, <see cref="Analog"/>), 559 ms at the first retained-world frame
/// (485 ms of it JIT, 323 methods: `GpuWorld.Begin`, stage 15, the model walk),
/// 50-170 ms the first time a creature acts, 10-20 ms for a first pose.
///
/// ## Preparing rather than tiering
///
/// <c>RuntimeHelpers.PrepareMethod</c> compiles a body without calling it, and
/// the set to compile is already enumerable: every overlay is registered before
/// the first `Dispatcher.Load`, so `Functions` holds a delegate per recompiled
/// function and <c>Delegate.Method</c> is the method the JIT would compile
/// later. The port's own assembly -- the patches and Verdite Core, which compiles
/// into it as source -- and the runtime go in the same pass: a replace hook is an
/// ordinary managed method, and so is the runtime code it spends its time in.
///
/// Two things Verdite2's pass leaves to the game thread are taken here too:
/// **constructors**, which `GetMethods` does not return (a type's first use
/// compiled its `.cctor` at the first area), and **the library's generics closed
/// over this port's value types**, as far as the port's fields name them.
///
/// **The pass starts at the first overlay load, `main`, not at the area's.**
/// The load returns straight into the first frame of the new area, so preparing
/// the incoming module from its own <c>OverlayLoadedEvent</c> would race the
/// frames it is meant to protect.
///
/// Several threads take from the head of one ordered list -- one thread took
/// 8.9 s, longer than an autostart takes to reach its area. Lowest priority and
/// a yield every 32 methods, so a machine with few cores
/// spends its cores on the picture; background, so it cannot hold up an exit.
/// A method <see cref="HookManager"/> has already committed is left alone --
/// MonoMod prepares its target when it installs the detour, so there is nothing
/// to gain and one less thing to do concurrently with a detour being written.
///
/// See "The stutters" in docs/DEVELOPMENT.md.
/// </summary>
public static class Prejit
{
    /// <summary>Methods between two yields. Lowest priority is what actually
    /// keeps this off the game thread's back; the yield is for a host whose
    /// scheduler will not preempt on priority alone.</summary>
    const int YieldEvery = 32;

    public static bool Enabled { get; private set; } = true;

    static bool _probe;
    static Thread[]? _threads;
    static (string Label, MethodBase Method)[]? _work;
    static Stopwatch? _clock;
    static int _next, _finished, _prepared, _left, _refused;

    /// <summary>Threads the pass runs on. One thread took 8.9 s over 7751
    /// methods, longer than an autostart takes to reach its area, and `game`
    /// alone was 4.6 s of it; a quarter of the cores, at most four, leaves the
    /// rest to the game, the driver and the audio.</summary>
    public static int Threads { get; private set; } = Math.Clamp(Environment.ProcessorCount / 4, 1, 4);

    /// <summary>Methods compiled by the pass, and whether it has finished. Read
    /// by the probe line; nothing gates on them.</summary>
    public static int Prepared { get; private set; }

    public static bool Done { get; private set; }

    public static void Configure(string? enabled, string? probe, string? threads)
    {
        if (!string.IsNullOrWhiteSpace(enabled)) Enabled = enabled != "0";
        if (!string.IsNullOrWhiteSpace(probe)) _probe = probe != "0";
        if (int.TryParse(threads, out int n) && n > 0) Threads = Math.Min(n, Environment.ProcessorCount);
    }

    /// <summary>
    /// Arm the pass. Deferred to the first <see cref="OverlayLoadedEvent"/>
    /// because that is the first moment the dispatcher's registry is populated,
    /// and installed last in Program.cs so the patches' own attach listeners have
    /// run -- and their hooks committed -- before the first method is prepared.
    /// </summary>
    public static void Install()
    {
        if (!Enabled)
        {
            Console.WriteLine("[KF3] prejit: off, every method compiles on its first call");
            return;
        }

        Event.AddListener<OverlayLoadedEvent>(_ => Start());
    }

    static void Start()
    {
        if (_threads != null) return;
        _work = Order();
        _clock = Stopwatch.StartNew();
        _threads = new Thread[Threads];
        for (int i = 0; i < _threads.Length; i++)
        {
            _threads[i] = new Thread(Warm)
            {
                IsBackground = true,
                Priority = ThreadPriority.Lowest,
                Name = "kf3-prejit",
            };
            _threads[i].Start();
        }
        Console.WriteLine($"[KF3] prejit: warming the recompiled code on {_threads.Length} background thread(s)");
    }

    /// <summary>The whole pass, in order, flattened up front so that several
    /// threads can take from the head of it: reflection over the assemblies is
    /// milliseconds, the compile is seconds.</summary>
    static (string Label, MethodBase Method)[] Order()
    {
        var seen = new HashSet<RuntimeMethodHandle>();
        var work = new List<(string, MethodBase)>();
        foreach (var (label, methods) in Batches())
            foreach (var mi in methods)
                if (mi != null && seen.Add(mi.MethodHandle)) work.Add((label, mi));
        return work.ToArray();
    }

    static void Warm()
    {
        var work = _work!;
        int step = 0;

        for (int i; (i = Interlocked.Increment(ref _next) - 1) < work.Length;)
        {
            var (label, mi) = work[i];
            switch (Prepare(mi))
            {
                case Verdict.Prepared: Interlocked.Increment(ref _prepared); break;
                case Verdict.LeftAlone: Interlocked.Increment(ref _left); break;
                default: Interlocked.Increment(ref _refused); break;
            }

            // A batch is reported when the thread that takes its last method has
            // compiled it; with several threads the batch before it may still be
            // finishing, so the times are when each was reached, not its cost.
            if (_probe && (i + 1 == work.Length || work[i + 1].Label != label))
                Console.WriteLine($"[KF3] prejit: {label} done by {_clock!.Elapsed.TotalMilliseconds:0} ms");

            if (++step % YieldEvery == 0) Thread.Yield();
        }

        if (Interlocked.Increment(ref _finished) != _threads!.Length) return;
        Prepared = _prepared;
        Done = true;
        Console.WriteLine($"[KF3] prejit: {_prepared} method(s) compiled in " +
                          $"{_clock!.Elapsed.TotalMilliseconds:0} ms on {_threads.Length} thread(s), " +
                          $"{_left} left to their hook" + (_refused > 0 ? $", {_refused} refused" : ""));
    }

    enum Verdict { Prepared, LeftAlone, Refused }

    static Verdict Prepare(MethodBase mi)
    {
        // A generic definition has no code until it is instantiated, and an
        // abstract or extern method has no body at all; PrepareMethod throws on
        // both rather than reporting it.
        if (mi.IsAbstract || mi.IsGenericMethodDefinition || mi.ContainsGenericParameters)
            return Verdict.LeftAlone;

        if (mi is MethodInfo m && HookManager.IsCommitted(m)) return Verdict.LeftAlone;

        try
        {
            var owner = mi.DeclaringType;
            if (owner is { IsGenericType: true })
                RuntimeHelpers.PrepareMethod(mi.MethodHandle,
                    [.. owner.GetGenericArguments().Select(static a => a.TypeHandle)]);
            else
                RuntimeHelpers.PrepareMethod(mi.MethodHandle);
            return Verdict.Prepared;
        }
        catch
        {
            // Nothing here is load-bearing: a method that will not prepare is a
            // method that compiles on its first call, which is where it was.
            return Verdict.Refused;
        }
    }

    /// <summary>
    /// Ordered by what entering an area needs, because the pass is racing the
    /// player: the 28 area modules (5-20 functions each), then the port's own
    /// replace hooks and the runtime they spend their time in -- which is what
    /// the first frame of an area is -- then `game`, which holds the stages and
    /// the renderer and is the bulk at 2122. `open` and `end` are last: the title
    /// has already run by the time the pass reaches it, and the ending is hours
    /// away.
    /// </summary>
    static IEnumerable<(string Label, IEnumerable<MethodBase?> Methods)> Batches()
    {
        var overlays = Dispatcher.Overlays;
        var names = overlays.Keys.ToList();
        names.Sort(static (a, b) => Rank(a) != Rank(b)
            ? Rank(a) - Rank(b)
            : string.CompareOrdinal(a, b));

        foreach (var name in names)
        {
            if (Rank(name) > 0) break;
            yield return (name, Functions(overlays, name));
        }

        yield return ("patches", PortMethods());
        yield return ("runtime", AssemblyMethods(typeof(HookManager).Assembly, null));
        yield return ("generics", Generics());

        foreach (var name in names)
        {
            if (Rank(name) == 0) continue;
            yield return (name, Functions(overlays, name));
        }
    }

    static IEnumerable<MethodBase?> Functions(IReadOnlyDictionary<string, IOverlay> overlays, string name)
    {
        IReadOnlyDictionary<uint, Action<CpuContext, IMemory>>? fns = null;
        try { fns = overlays[name].Functions; }
        catch { }
        return fns == null ? [] : fns.Values.Select(static f => f.Method);
    }

    static int Rank(string name) =>
        name.StartsWith("fdat", StringComparison.OrdinalIgnoreCase) ? 0
        : name.Equals("game", StringComparison.OrdinalIgnoreCase) ? 1
        : name.Equals("main", StringComparison.OrdinalIgnoreCase) ? 2
        : 3;

    /// <summary>`generated/`, `patches/` and Verdite Core's source compile into
    /// one assembly, so the recompiled functions above and these come out of the
    /// same place; the namespace is what separates them (`Recompiled` is the
    /// generated code, already warmed by overlay).</summary>
    static IEnumerable<MethodBase?> PortMethods()
        => AssemblyMethods(typeof(Prejit).Assembly, "Kf3")
            .Concat(AssemblyMethods(typeof(Prejit).Assembly, "Verdite.Core"));

    /// <summary>
    /// The library's generic types as the port's own fields close them over a
    /// value type -- `Dictionary&lt;(uint, uint, Family), Mesh&gt;`, `List&lt;Vertex&gt;` --
    /// which share no code with any other instantiation and are not precompiled,
    /// so they compiled on the game thread at the first area (79 methods, 22 ms
    /// of one frame). A field is the part of this that can be enumerated; a
    /// local's or a LINQ chain's instantiation still compiles on its first call.
    /// </summary>
    static IEnumerable<MethodBase?> Generics()
    {
        var closed = new HashSet<Type>();
        foreach (var asm in new[] { typeof(Prejit).Assembly, typeof(HookManager).Assembly })
        {
            Type?[] types;
            try { types = asm.GetTypes(); }
            catch (ReflectionTypeLoadException e) { types = e.Types; }
            foreach (var t in types)
            {
                if (t == null || t.ContainsGenericParameters) continue;
                FieldInfo[] fields;
                try { fields = t.GetFields(All); }
                catch { continue; }
                foreach (var f in fields) Collect(f.FieldType, closed);
            }
        }

        foreach (var g in closed)
        {
            MethodBase[] methods;
            try { methods = [.. g.GetMethods(All), .. g.GetConstructors(All)]; }
            catch { continue; }
            foreach (var mi in methods) yield return mi;
        }
    }

    static void Collect(Type t, HashSet<Type> closed)
    {
        while (t.HasElementType) t = t.GetElementType()!;
        if (!t.IsGenericType || t.ContainsGenericParameters) return;
        var args = t.GetGenericArguments();
        if (t.Assembly != typeof(Prejit).Assembly && t.Assembly != typeof(HookManager).Assembly
            && args.Any(static a => a.IsValueType) && !closed.Add(t))
            return;
        foreach (var a in args) Collect(a, closed);
    }

    const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static |
                             BindingFlags.Instance | BindingFlags.DeclaredOnly;

    /// <summary>Every method an assembly declares, optionally under one namespace
    /// root. The runtime goes in whole: a replace hook's own time is spent in the
    /// GTE fast path, the retained scene and the GL backend, and those are
    /// compiled on first call like everything else.</summary>
    static IEnumerable<MethodBase?> AssemblyMethods(Assembly asm, string? ns)
    {
        Type?[] types;
        try { types = asm.GetTypes(); }
        catch (ReflectionTypeLoadException e) { types = e.Types; }

        foreach (var t in types)
        {
            if (t == null || t.ContainsGenericParameters) continue;
            if (ns != null && (t.Namespace == null || !t.Namespace.StartsWith(ns, StringComparison.Ordinal)))
                continue;

            // Constructors too, static ones above all: GetMethods leaves them
            // out, and a type's first use then compiled its .cctor on the game
            // thread (RetainedAssets, RetainedNear, WaterSwell at the first area).
            MethodBase[] methods;
            try { methods = [.. t.GetMethods(All), .. t.GetConstructors(All)]; }
            catch { continue; }

            foreach (var mi in methods) yield return mi;
        }
    }
}
