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
/// Interpolates, never extrapolates; an angle step larger than <see cref="SnapAngle"/>
/// in one tick snaps, and a placement (a position step past <see cref="SnapUnits"/>, an
/// area crossing's among them) keeps the pair's lag and speed across it. See
/// "2. The camera carried" in docs/SMOOTHING.md.
/// </summary>
public static class ViewSmoothing
{
    /// <summary>A position step in one tick past which the view snaps (a warp, a load).</summary>
    public const int SnapUnits = 1536;

    /// <summary>An angle step in one tick past which the view snaps (a cut), 0x1000 a turn.</summary>
    public const int SnapAngle = 0x300;

    public static bool Enabled { get; set; }

    /// <summary>Carrying now: on, under pacing, with stage 15 in C#.</summary>
    public static bool Active => Enabled && FramePacing.Enabled && Stage15.InCSharp;

    public static bool ProbeOn { get => _probe; set => _probe = value; }
    static bool _wasActive;

    /// <summary>Held off by the shell's fixed <c>view</c>.</summary>
    public static bool Suspended { get; set; }

    static Camera _prev, _cur, _drawn;
    static bool _primed;
    static long _tick = -1;

    static bool _probe;
    static double _probeAt = -1.0;
    static long _frames, _moved, _samples, _snaps, _placements;

    public static void Configure(string? mode, string? probe)
    {
        Enabled = mode?.Trim() is not ("0" or "off");
        _probe = probe?.Trim() == "1";
    }

    public static void Install()
    {
        // Hooked in every state; Active decides per frame, so the Testing tab can switch it.
        Stage15.OnHanded = OnHanded;
        // Not an area module: a crossing loads one after the placement that moved
        // the player, which OnHanded has already carried the pair across; clearing
        // it there held the view for a tick mid-stride. Any other executable re-primes.
        Event.AddListener<OverlayLoadedEvent>(e => { if (!e.Name.StartsWith("fdat", StringComparison.Ordinal)) _primed = false; });
        Console.WriteLine($"[KF3] view smoothing: {(Enabled ? "on" : "off")}");
    }

    static void OnHanded(Camera handed)
    {
        if (Suspended) return;
        if (!Active)
        {
            // Carrying off under pacing, the mouse still leads: the tick's view plus
            // what the hand has moved since.
            if (Leading && Lead(handed, 1.0, false) is { } led)
            {
                Stage15.ViewOverride = led;
                _wasActive = true;
            }
            else if (_wasActive) { Stage15.ViewOverride = null; _primed = false; _wasActive = false; }
            return;
        }
        _wasActive = true;
        long tick = FramePacing.Ticks;
        bool ticked = false, snapped = false;
        if (!_primed)
        {
            _prev = _cur = handed;
            _primed = true;
            _tick = tick;
        }
        else if (tick != _tick && FramePacing.IterationTicked)
        {
            _tick = tick;
            var last = _cur;
            int stepX = _cur.X - _prev.X, stepY = _cur.Y - _prev.Y, stepZ = _cur.Z - _prev.Z;
            _prev = _cur;
            _cur = handed;
            _samples++;
            ticked = true;
            if (Placed(last, handed))
            {
                // A placement landed in this tick (a crossing, a warp): the pair
                // straddles two places. Put prev the last tick's step behind cur, so
                // the view keeps its tick of lag and its speed instead of holding
                // still for a tick (Verdite2's FrameSmoothing).
                _prev = _prev with { X = handed.X - stepX, Y = handed.Y - stepY, Z = handed.Z - stepZ };
                _placements++;
            }
            if (Turned(_prev, _cur)) { _prev = _prev with { Pitch = _cur.Pitch, Yaw = _cur.Yaw, Roll = _cur.Roll }; _snaps++; snapped = true; }
        }
        else if (handed != _cur)
        {
            // Moved without a tick of the world (a placement, a stage outside pacing):
            // shift the whole pair by the move, so the view carries straight through it.
            _prev = new Camera(_prev.X + (handed.X - _cur.X), _prev.Y + (handed.Y - _cur.Y), _prev.Z + (handed.Z - _cur.Z),
                Add12(_prev.Pitch, Wrap(handed.Pitch - _cur.Pitch)), Add12(_prev.Yaw, Wrap(handed.Yaw - _cur.Yaw)),
                Add12(_prev.Roll, Wrap(handed.Roll - _cur.Roll)));
            _cur = handed;
            _placements++;
        }

        double frac = FramePacing.TickFraction;
        var view = Lerp(_prev, _cur, frac);
        if (Leading)
        {
            if (snapped) _tickYaw = _tickPitch = 0;
            else if (ticked) TakeSpent();
            view = Lead(view, frac, true) ?? view;
        }
        Stage15.ViewOverride = view;
        if (_probe) Probe(view);
    }

    // ---- the mouse leads the tick ------------------------------------------------
    //
    // The look routine spends the mouse once a tick, and the lerp reaches that turn
    // only at the next tick: up to two ticks from hand to picture. A mouse asks for
    // a displacement the game adds unchanged (patches/MouseLook.cs), so the view
    // can show it the frame it happens: the lerp's share of the last tick's mouse
    // turn is replaced by all of it, and the motion not yet spent is added on top.
    // Verdite2's FrameSmoothing.MouseLead; see "The mouse leads the tick" in
    // docs/INPUT.md.

    // Set by a mod whose own camera filter fights the lead -- the debug mod's
    // cinematic camera smooths the angles the lead would jump past, and the two
    // together judder. The setting itself is left alone.
    public static bool LeadSuppressed;

    // Not while a picked-up item is held up: the mouse is turning the item then
    // (ItemTurn), and the look routine is not running to spend it.
    static bool Leading => Mouse.Lead && FramePacing.Enabled && Stage15.InCSharp && !ItemTurn.HoldsMouse && !LeadSuppressed;
    static int _tickYaw, _tickPitch;
    static long _ledFrames, _ledTicks;
    static double _ledMiss;

    /// <summary>What the game turned by for the mouse on this tick, measured off
    /// the base angles.</summary>
    static void TakeSpent()
    {
        var m = MouseLook.Memory;
        var spent = m == null ? null : Mouse.SpentThisFrame(m);
        (_tickYaw, _tickPitch) = spent is { } t ? (t.Yaw, t.Pitch) : (0, 0);
        if (_probe && spent is { } q)
        {
            _ledTicks++;
            _ledMiss += Math.Abs(q.Yaw - q.AskedYaw) + Math.Abs(q.Pitch - q.AskedPitch);
        }
    }

    /// <summary>The view with the mouse's lead added, or null when there is none.
    /// <paramref name="frac"/> is the lerp's phase, 1 when nothing is carried.</summary>
    static Camera? Lead(Camera view, double frac, bool carried)
    {
        var m = MouseLook.Memory;
        if (m == null) return null;
        Mouse.Poll();
        var (turn, look) = Mouse.Pending;

        double keep = 1.0 - frac;
        double yaw = (carried ? _tickYaw * keep : 0) + turn;

        // Held inside the game's pitch limit, off the base angle the next tick adds
        // to, so looking into the limit stops at it instead of overshooting.
        int basePitch = S12(m.ReadU16(Mouse.PitchAddress));
        int ahead = Math.Clamp(basePitch + (int)Math.Round(look), -Mouse.PitchLimit, Mouse.PitchLimit) - basePitch;
        double pitch = (carried ? _tickPitch * keep : 0) + ahead;

        int dy = (int)Math.Round(yaw), dp = (int)Math.Round(pitch);
        if (dy == 0 && dp == 0) return carried ? view : null;
        if (_probe) _ledFrames++;
        return view with { Yaw = Add12(view.Yaw, dy), Pitch = Add12(view.Pitch, dp) };
    }

    static int S12(ushort a) => ((a + 0x800) & 0xFFF) - 0x800;

    static short Add12(short a, int d) => (uint)a < 0x1000u ? (short)((a + d) & 0xFFF) : (short)(a + d);

    static bool Placed(in Camera a, in Camera b) =>
        Math.Abs((long)b.X - a.X) > SnapUnits || Math.Abs((long)b.Y - a.Y) > SnapUnits ||
        Math.Abs((long)b.Z - a.Z) > SnapUnits;

    static bool Turned(in Camera a, in Camera b) =>
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
                          $"{_samples / dt:0.0} tick sample(s)/s, {_snaps} snap(s), {_placements} placement(s), {Stage15.NeedleCarried / dt:0.0} needle(s) and {Stage15.GaugeCarried / dt:0.0} gauge(s) carried/s; " +
                          $"drawn [{view.X},{view.Y},{view.Z}] yaw {view.Yaw}, handed [{_cur.X},{_cur.Y},{_cur.Z}] yaw {_cur.Yaw}; " +
                          $"mouse led {_ledFrames / dt:0.0} frame(s)/s, |applied - asked| {(_ledTicks > 0 ? _ledMiss / _ledTicks : 0):0.00} a tick");
        _probeAt = now;
        _frames = _moved = _samples = _snaps = _placements = 0;
        _ledFrames = _ledTicks = 0; _ledMiss = 0;
        Stage15.NeedleCarried = Stage15.GaugeCarried = 0;
    }
}
