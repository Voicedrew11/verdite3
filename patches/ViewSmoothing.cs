using RecompOne.Runtime.Events;

namespace Kf3;

/// <summary>
/// The camera carried between world ticks: stage 15 is handed the camera the last
/// tick left, and each drawn frame is given, through <see cref="Stage15.ViewOverride"/>,
/// the camera interpolated between the last two ticks at the clock's fraction.
///
///     KF3_SMOOTH=1         on whenever pacing is (judged 2026-10-02); 0 to compare
///     KF3_SMOOTH_PROBE=1   a line a second: frames drawn, distinct cameras drawn, ticks, snaps
///
/// Interpolates, never extrapolates; a jump larger than <see cref="SnapUnits"/> or
/// <see cref="SnapAngle"/> in one tick snaps, and an area change re-primes. See
/// "2. The camera carried" in docs/SMOOTHING.md.
/// </summary>
public static class ViewSmoothing
{
    /// <summary>A position step in one tick past which the view snaps (a warp, a load).</summary>
    public const int SnapUnits = 1536;

    /// <summary>An angle step in one tick past which the view snaps (a cut), 0x1000 a turn.</summary>
    public const int SnapAngle = 0x300;

    public static bool Enabled { get; private set; }

    /// <summary>Held off by the shell's fixed <c>view</c>.</summary>
    public static bool Suspended { get; set; }

    static Camera _prev, _cur, _drawn;
    static bool _primed;
    static long _tick = -1;

    static bool _probe;
    static double _probeAt = -1.0;
    static long _frames, _moved, _samples, _snaps;

    public static void Configure(string? mode, string? probe)
    {
        Enabled = mode?.Trim() is not ("0" or "off");
        _probe = probe?.Trim() == "1";
    }

    public static void Install()
    {
        if (!Enabled) return;
        // Without pacing every frame is a tick, and there is nothing to carry.
        if (!FramePacing.Enabled) { Enabled = false; return; }
        if (!Stage15.InCSharp)
        {
            Enabled = false;
            Console.Error.WriteLine("[KF3] view smoothing: needs stage 15 in C# (KF3_STAGE15 unset or 1); off.");
            return;
        }
        Stage15.OnHanded = OnHanded;
        Event.AddListener<OverlayLoadedEvent>(_ => _primed = false);
        Console.WriteLine("[KF3] view smoothing: on");
    }

    static void OnHanded(Camera handed)
    {
        if (Suspended) return;
        long tick = FramePacing.Ticks;
        if (!_primed)
        {
            _prev = _cur = handed;
            _primed = true;
            _tick = tick;
        }
        else if (tick != _tick && FramePacing.IterationTicked)
        {
            _tick = tick;
            _prev = _cur;
            _cur = handed;
            _samples++;
            if (Jump(_prev, _cur)) { _prev = _cur; _snaps++; }
        }
        else if (handed != _cur)
        {
            // Moved without a tick of the world (a stage outside pacing): no pair to carry.
            _prev = _cur = handed;
            _snaps++;
        }

        var view = Lerp(_prev, _cur, FramePacing.TickFraction);
        Stage15.ViewOverride = view;
        if (_probe) Probe(view);
    }

    static bool Jump(in Camera a, in Camera b) =>
        Math.Abs((long)b.X - a.X) > SnapUnits || Math.Abs((long)b.Y - a.Y) > SnapUnits ||
        Math.Abs((long)b.Z - a.Z) > SnapUnits ||
        Math.Abs(Wrap(b.Pitch - a.Pitch)) > SnapAngle || Math.Abs(Wrap(b.Yaw - a.Yaw)) > SnapAngle ||
        Math.Abs(Wrap(b.Roll - a.Roll)) > SnapAngle;

    /// <summary>An angle step in one tick too large to sweep.</summary>
    internal static bool Jumped(short a, short b) => Math.Abs(Wrap(b - a)) > SnapAngle;

    static int Wrap(int d) => ((d + 0x800) & 0xFFF) - 0x800;

    static Camera Lerp(in Camera a, in Camera b, double t) => new(
        Mix(a.X, b.X, t), Mix(a.Y, b.Y, t), Mix(a.Z, b.Z, t),
        Turn(a.Pitch, b.Pitch, t), Turn(a.Yaw, b.Yaw, t), Turn(a.Roll, b.Roll, t));

    static int Mix(int a, int b, double t) => (int)(a + Math.Round(((long)b - a) * t));

    /// <summary>The short way round at 12 bits, kept in 0..0xFFF when both ends are.</summary>
    internal static short Turn(short a, short b, double t)
    {
        int v = a + (int)Math.Round(Wrap(b - a) * t);
        if ((uint)a < 0x1000u && (uint)b < 0x1000u) v &= 0xFFF;
        return (short)v;
    }

    static void Probe(in Camera view)
    {
        _frames++;
        if (view != _drawn) _moved++;
        _drawn = view;
        double now = Environment.TickCount64 / 1000.0;
        if (_probeAt < 0.0) _probeAt = now;
        double dt = now - _probeAt;
        if (dt < 1.0) return;
        Console.WriteLine($"[KF3] view smoothing: {_frames / dt:0.0} frame(s)/s, {_moved / dt:0.0} with a new camera, " +
                          $"{_samples / dt:0.0} tick sample(s)/s, {_snaps} snap(s), {Stage15.NeedleCarried / dt:0.0} needle(s) and {Stage15.GaugeCarried / dt:0.0} gauge(s) carried/s; " +
                          $"drawn [{view.X},{view.Y},{view.Z}] yaw {view.Yaw}, handed [{_cur.X},{_cur.Y},{_cur.Z}] yaw {_cur.Yaw}");
        _probeAt = now;
        _frames = _moved = _samples = _snaps = 0;
        Stage15.NeedleCarried = Stage15.GaugeCarried = 0;
    }
}
