using System.Diagnostics;
using System.Reflection;
using RecompOne.Runtime.Context;
using RecompOne.Runtime.Memory;
using RecompOne.Runtime.Modding;

namespace Kf3;

/// <summary>
/// Run the bottom message box's state machine once a tick, not once a drawn frame.
///
///     KF3_MSGBOX=0   step it on every drawn frame -- comparison only
///
/// `func_80041F9C` is stage 15's third call: it slides the box in (`+0x14` a call
/// to `0x64`), holds it for `0x0F` calls and slides it out, writing only the HUD
/// records stage 15's overlay walks draw. Under pacing every drawn frame stepped
/// it, so a coin pickup's box came and went ten times too fast. Skipped between
/// ticks, the records it wrote last are drawn again. See "The message box at the
/// bottom" in docs/SMOOTHING.md.
/// </summary>
public static class MessageBoxHold
{
    const uint Routine = 0x80041F9C;
    const uint State = 0x801AEAF7;   // 0 idle, 1 in, 2 hold, 3 out

    public static bool Enabled { get; set; } = true;

    internal static ModInfo Mod => _self;
    static readonly ModInfo _self = new()
    {
        Id = "kf3.msgboxhold",
        Name = "Message box pacing",
        Version = "1.0",
        Description = "Steps the bottom message box at the world's rate, not the frame rate.",
    };

    static bool _probe;
    static long _seen = -1;

    static readonly Stopwatch _clock = Stopwatch.StartNew();
    static double _windowMs;
    static long _calls, _stepped;

    public static void Configure(string? mode)
    {
        _probe = Environment.GetEnvironmentVariable("KF3_FPS_PROBE") == "1";
        if (string.IsNullOrWhiteSpace(mode)) return;
        Enabled = mode.Trim().ToLowerInvariant() is not ("0" or "off");
    }

    public static void Install()
    {
        // Attached whether or not pacing is on, so the Testing tab can switch both live.
        HookAttach.OnOverlayLoad("message box", Attach);
    }

    static bool Attach()
    {
        SymbolRegistry.Build();
        var target = SymbolRegistry.Resolve("game", null, Routine);
        if (target == null)
        {
            Console.Error.WriteLine($"[KF3] message box: not installed (no game function at 0x{Routine:X8})");
            return false;
        }

        HookManager.AddPre(_self, target,
            typeof(MessageBoxHold).GetMethod(nameof(Before), BindingFlags.Public | BindingFlags.Static)!);
        HookManager.Commit();
        if (!HookAttach.Installed(target))
        {
            Console.Error.WriteLine("[KF3] message box: not installed (the hook did not attach)");
            return false;
        }

        Console.WriteLine("[KF3] message box: held to the tick");
        return true;
    }

    /// <summary>False skips the routine: a drawn frame that is not the tick's first.</summary>
    public static bool Before(CpuContext c, IMemory m)
    {
        if (!Enabled || !FramePacing.Enabled) return true;
        bool step = FramePacing.FirstWalkOfTick(ref _seen);
        if (_probe) Probe(m, step);
        return step;
    }

    static void Probe(IMemory m, bool step)
    {
        if (m.ReadU8(State) != 0) { _calls++; if (step) _stepped++; }
        double now = _clock.Elapsed.TotalMilliseconds;
        if (_windowMs <= 0.0) { _windowMs = now; return; }
        if (now - _windowMs < 1000.0) return;
        // Silent while the box is idle.
        if (_calls > 0)
            Console.WriteLine($"[KF3] message box: {_stepped} call(s) stepped, {_calls - _stepped} held, while shown");
        _windowMs = now;
        _calls = _stepped = 0;
    }
}
