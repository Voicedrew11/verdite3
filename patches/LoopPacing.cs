using System.Diagnostics;
using System.Reflection;
using RecompOne.Runtime.Context;
using RecompOne.Runtime.Memory;
using RecompOne.Runtime.Modding;

namespace Kf3;

/// <summary>
/// Hold the modal loops -- the routines entered from a gated stage that call stage
/// 15 themselves, so they present their own frames -- to the world's rate, and fill
/// the gap with redraws.
///
///     KF3_LOOPPACING=0        leave them on the render rate -- comparison only
///     KF3_LOOPPACING_PROBE=1  a line a second: modal calls, redraws each, the
///                             arguments and caller of the last
///
/// FramePacing's stage gate cannot reach inside a modal loop: stages 1-14 decide
/// only whether the loop is entered, and once inside, its body steps once per
/// drawn frame. A redraw of stage 15 passes the frame boundary, so the loop below
/// iterates once per world tick while the picture is drawn at the render rate and
/// the smoothers carry between ticks. See "Loops that draw their own frames" in
/// docs/SMOOTHING.md.
/// </summary>
public static class LoopPacing
{
    /// <summary>Stage 15, the frame builder <c>func_800422B8(VECTOR*, SVECTOR*)</c>.</summary>
    const uint Stage15Routine = 0x800422B8;

    /// <summary>The return address of the main loop's own <c>jal</c> to stage 15;
    /// a call from anywhere else is a modal loop drawing its own frame.</summary>
    const uint MainLoopReturn = 0x80014FB0;

    static readonly ModInfo _self = new()
    {
        Id = "kf3.looppacing",
        Name = "Loop pacing",
        Version = "1.0",
        Description = "Runs a loop that draws its own frames at the world's rate.",
    };

    /// <summary>On by default; <c>KF3_LOOPPACING=0</c> is the comparison.</summary>
    public static bool Enabled { get; set; } = true;

    static bool _probe;
    static bool _queued;
    static bool _bound;

    /// <summary>True while the post is driving redraws of its own. Every hook fires
    /// on those too, this one's pre and post included, so without it the nested calls
    /// would overwrite the recorded arguments and recurse.</summary>
    static bool _inRedraw;
    static bool _modal;
    static uint _arg0, _arg1;
    static uint _callerRa;

    /// <summary>Stage 15 as a callable, bound on first use through the detours every
    /// patch has committed by then.</summary>
    static Action<CpuContext, IMemory>? _stage15;

    static bool _capWarned, _lostWarned;

    // The probe: modal calls and redraws since the window opened.
    static readonly Stopwatch _clock = Stopwatch.StartNew();
    static double _probeAt;
    static long _calls, _redraws;

    public static void Configure(string? mode, string? probe)
    {
        Enabled = mode?.Trim().ToLowerInvariant() is not ("0" or "off");
        _probe = probe?.Trim() == "1";
    }

    public static void Install()
        => HookAttach.OnOverlayLoad("loop pacing", Attach,
            "See \"Loops that draw their own frames\" in docs/SMOOTHING.md.");

    static bool Attach()
    {
        SymbolRegistry.Build();
        var target = SymbolRegistry.Resolve("game", null, Stage15Routine);
        if (target == null)
        {
            Console.Error.WriteLine($"[KF3] loop pacing: no game function at 0x{Stage15Routine:X8}");
            return false;
        }

        if (!_queued)
        {
            var pre = typeof(LoopPacing).GetMethod(nameof(BeforeStage15), BindingFlags.Public | BindingFlags.Static)!;
            var post = typeof(LoopPacing).GetMethod(nameof(AfterStage15), BindingFlags.Public | BindingFlags.Static)!;
            // The post runs after every other post on stage 15, so it redraws only
            // once their own restores have been made.
            _queued = HookManager.AddPre(_self, target, pre) && HookManager.AddPost(_self, target, post, int.MaxValue - 1);
            if (!_queued) return false;
        }

        HookManager.Commit();
        bool ok = HookAttach.Installed(target);
        Console.WriteLine(ok ? $"[KF3] loop pacing: {(Enabled ? "on" : "off")}"
                             : "[KF3] loop pacing: not installed");
        return ok;
    }

    /// <summary>Bind stage 15 as a callable, once, on the first modal call that wants
    /// a redraw. Late on purpose: a delegate made here goes through the detours every
    /// patch has committed by now, so a redraw runs the smoothers too.</summary>
    static void Bind()
    {
        _bound = true;
        var target = SymbolRegistry.Resolve("game", null, Stage15Routine);
        if (target == null) return;
        _stage15 = target.CreateDelegate<Action<CpuContext, IMemory>>();
    }

    /// <summary>The arguments of the frame the game is about to draw, and whether the
    /// call came from a modal loop rather than the main loop. Skipped during a redraw,
    /// which is this class's own recorded arguments coming back round.</summary>
    public static bool BeforeStage15(CpuContext c, IMemory m)
    {
        if (!_inRedraw)
        {
            _arg0 = c.A0;
            _arg1 = c.A1;
            _callerRa = c.RA;
            _modal = c.RA != MainLoopReturn;
        }
        return true;
    }

    /// <summary>A modal loop's frame has been drawn. If the world did not tick on it,
    /// redraw stage 15 with the loop's own arguments until it does, so the loop's body
    /// -- and everything it steps -- runs once per world tick.</summary>
    public static void AfterStage15(CpuContext c, IMemory m)
    {
        if (_inRedraw || !_modal) return;

        if (_probe) _calls++;

        if (Enabled && FramePacing.Enabled
            && (FramePacing.Uncapped || FramePacing.TargetFps > FramePacing.LogicHz))
        {
            int n = Redraw(c, m);
            if (_probe) _redraws += n;
        }

        if (_probe) Probe();
    }

    static int Redraw(CpuContext c, IMemory m)
    {
        if (!_bound) Bind();
        if (_stage15 == null) return 0;

        double capMs = 3000.0 / Math.Max(FramePacing.LogicHz, 1.0);
        int n = 0;
        bool lost = false;
        var saved = c.Snapshot();
        _inRedraw = true;
        try
        {
            double until = _clock.Elapsed.TotalMilliseconds + capMs;
            while (!FramePacing.TickedThisFrame && _clock.Elapsed.TotalMilliseconds < until)
            {
                long frames = FramePacing.Frames;
                c.A0 = _arg0;
                c.A1 = _arg1;
                _stage15(c, m);
                n++;
                if (FramePacing.Frames == frames) { lost = true; break; }
            }
        }
        finally
        {
            _inRedraw = false;
            // The loop resumes with exactly the registers stage 15 left it.
            c.Restore(saved);
        }

        if (lost && !_lostWarned)
        {
            _lostWarned = true;
            Console.Error.WriteLine("[KF3] loop pacing: a redraw did not reach the frame boundary -- " +
                                    "the modal loop's body is no longer held to the world. Reported once.");
        }
        else if (!lost && !FramePacing.TickedThisFrame && !_capWarned)
        {
            _capWarned = true;
            Console.Error.WriteLine($"[KF3] loop pacing: {n} redraws over {capMs:0} ms did not reach a tick at " +
                                    $"{FramePacing.LogicHz:0.#} Hz -- the modal loop's body is running faster " +
                                    "than the world. Reported once.");
        }
        return n;
    }

    static void Probe()
    {
        double now = _clock.Elapsed.TotalMilliseconds;
        if (_probeAt <= 0.0) { _probeAt = now; return; }
        double dt = now - _probeAt;
        if (dt < 1000.0) return;

        if (_calls > 0)
            Console.WriteLine($"[KF3] loop pacing: {_calls * 1000.0 / dt:0.#} modal stage-15 call(s)/s, " +
                              $"{_redraws / (double)_calls:0.0} redraw(s) each, " +
                              $"last a0=0x{_arg0:X8} a1=0x{_arg1:X8} ra=0x{_callerRa:X8}");

        _probeAt = now;
        _calls = _redraws = 0;
    }
}
