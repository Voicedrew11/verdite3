using RecompOne.Runtime.Events;

namespace Kf3;

/// <summary>
/// KF3_TINTPROBE=1: whether the game's full-screen tints (the death fade, the
/// damage flash, an area's fade-in) are drawn on every frame or only on the frames
/// a world tick built -- the strobe Verdite2's <c>TintHold</c> fixed, asked of this
/// game. A tint is what <see cref="Widescreen"/> stretches: flat, semi-transparent,
/// spanning the clip rectangle's full width.
///
/// Every two seconds in which a tint was seen: the frames drawn, how many of them a
/// tick built, and the tinted frames split the same way. A tint held across the
/// frames between ticks is tinted on about as many idle frames as the idle share
/// of all frames; a strobe is tinted on tick frames only. Then the run of tinted
/// and untinted frames, one character a frame (<c>T</c> a tinted tick frame,
/// <c>t</c> a tinted idle one, <c>.</c>/<c>,</c> untinted tick/idle), so a fade's
/// shape is visible too. See "The tints between ticks" in docs/SMOOTHING.md.
/// </summary>
public static class TintProbe
{
    const int EdgeSlack = 2;

    static bool _on;

    // The frame being drawn: its identity, whether a tick built it, and whether it
    // held a tint.
    static long _frame = -1;
    static bool _frameTicked, _frameTinted;

    // The report window.
    static long _frames, _tickFrames, _tinted, _tintedTick;
    static readonly System.Text.StringBuilder _strip = new();
    static long _windowStart;

    public static void Install()
    {
        var v = Environment.GetEnvironmentVariable("KF3_TINTPROBE");
        if (string.IsNullOrWhiteSpace(v) || v == "0") return;
        _on = true;
        _windowStart = Environment.TickCount64;
        Event.AddListener<RenderPrimEvent>(OnPrim);
        Event.AddListener<VSyncEvent>(_ => Close());
        Console.WriteLine("[KF3] tintprobe: on");
    }

    static void OnPrim(RenderPrimEvent e)
    {
        Close();
        // Read while the frame's own ordering table is drawn: the iteration that
        // built it decided its tick at stage 1, and the next has not begun.
        _frameTicked = FramePacing.IterationTicked;
        if (_frameTinted || !e.SemiTransparent || e.Gouraud) return;

        int left = e.DrawLeft, right = e.DrawRight + 1;
        int lo = int.MaxValue, hi = int.MinValue, top = int.MaxValue, bottom = int.MinValue;
        for (int i = 0; i < e.Count; i++)
        {
            lo = Math.Min(lo, e.X[i]); hi = Math.Max(hi, e.X[i]);
            top = Math.Min(top, e.Y[i]); bottom = Math.Max(bottom, e.Y[i]);
        }
        // Full width and most of the height: a stretched tint has already left the
        // clip rectangle, so "at least as wide" rather than "on the edges".
        if (lo > left + EdgeSlack || hi < right - EdgeSlack) return;
        if (bottom - top < (e.DrawBottom - e.DrawTop) / 2) return;
        _frameTinted = true;
    }

    // A new frame begins once FramePacing has counted the last one's boundary;
    // book the finished frame then.
    static void Close()
    {
        if (!_on) return;
        long f = FramePacing.Frames;
        if (f == _frame) return;
        if (_frame >= 0 && AgentBeacon.Overlay.StartsWith("fdat", StringComparison.Ordinal))
        {
            _frames++;
            if (_frameTicked) _tickFrames++;
            if (_frameTinted) { _tinted++; if (_frameTicked) _tintedTick++; }
            if (_strip.Length < 600)
                _strip.Append(_frameTinted ? (_frameTicked ? 'T' : 't') : (_frameTicked ? '.' : ','));
        }
        _frame = f;
        _frameTicked = false;
        _frameTinted = false;

        if (Environment.TickCount64 - _windowStart < 2000) return;
        if (_tinted > 0)
        {
            Console.WriteLine($"[KF3] tintprobe: {_frames} frames ({_tickFrames} ticked), " +
                              $"tinted {_tinted} ({_tintedTick} ticked, {_tinted - _tintedTick} idle)");
            Console.WriteLine($"[KF3] tintprobe: {_strip}");
            Console.Out.Flush();
        }
        _frames = _tickFrames = _tinted = _tintedTick = 0;
        _strip.Clear();
        _windowStart = Environment.TickCount64;
    }
}
