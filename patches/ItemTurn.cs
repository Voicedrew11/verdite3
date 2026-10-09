using System.Diagnostics;
using System.Reflection;
using RecompOne.Runtime.Context;
using RecompOne.Runtime.Events;
using RecompOne.Runtime.Hardware;
using RecompOne.Runtime.Memory;
using RecompOne.Runtime.Modding;

namespace Kf3;

/// <summary>
/// Turn a picked-up item by hand while the game holds it up: the mouse, the right
/// stick, and the pad's gyroscope when asked.
///
///     KF3_ITEMTURN=0          the game's own spin only
///     KF3_ITEMTURN_GYRO=0     the gyroscope does not turn it too (it does by default since 2026-10-08)
///     KF3_ITEMTURN_PROBE=1    a line a second while an item is held up
///     KF3_ITEMTURN_TEST=id    hold up item <c>id</c> from the player's tick 10 s after
///                             the first area load, with the right stick taken as
///                             full right and half down for its second second;
///                             dismiss with KF3_AUTOPAD
///
/// The pickup is <c>func_8005DB30</c>, a loop that draws its own frames. It flies
/// the item to the middle of the screen (stage 15 called from <c>0x8005DF84</c>),
/// spins it <c>0x40</c> a pass on its yaw (record <c>+0x26</c>) until a new button
/// (<c>0x8005DFE8</c>), then turns it to face the camera (<c>0x8005E194</c>) or not,
/// and flies it out (<c>0x8005E278</c>). The record is at the routine's
/// <c>sp+0x80</c>; its rotation is three s16 at <c>+0x24</c> (x), <c>+0x26</c> (y),
/// <c>+0x28</c> (z), which the walk hands the submitter with <c>0x800</c> added to y.
///
/// **The turn is drawn, not written.** The record keeps the angles the game wrote,
/// so its spin, its turn back and its comparisons are untouched; the walk asks
/// <see cref="Present"/> for the rotation to draw (ModelWalk.Carry), every drawn
/// frame, after the smoother. The one write is undoing the spin's step while the
/// player is turning the item, so it holds still under the hand and spins again
/// <see cref="ResumeMs"/> after they let go.
///
/// The game's <c>RotMatrix</c> is <c>func_800166F4</c>: <c>Ry'(y) Rx(x) Rz(z)</c>,
/// where <c>Ry'(t)</c> is <c>[[c,0,-s],[0,1,0],[s,0,c]]</c> (<c>func_8001660C</c>)
/// and Rx and Rz are the usual. The camera's yaw is the s16 at <c>0x801AEC5E</c>,
/// and its right is <c>Ry'(yaw)</c> applied to x: the item is placed along
/// <c>yaw + 0x400</c>, and the pose it turns back to faces the camera with exactly
/// <c>Ry'(yaw)</c>. The yaw the player adds goes on the record's own yaw, about the
/// vertical; the tilt goes about the camera's right, outside it, so the item tips
/// toward and away from the eye however far it has spun (a turntable). The product
/// is read back into the game's three angles.
///
/// Sign: moving right brings the side nearest the eye to the right, and moving
/// down brings it down, for the mouse, the stick and the gyro alike. See "Turning
/// a picked-up item" in docs/INPUT.md.
/// </summary>
public static class ItemTurn
{
    const uint Routine = 0x8005DB30;
    const uint Stage15Routine = 0x800422B8;
    const uint PlayerTick = 0x80030FCC;     // stage 4, where KF3_ITEMTURN_TEST holds an item up

    // Stage 15's return addresses inside the routine: which part of the pickup is drawing.
    const uint Hold = 0x8005DFE8, FaceBack = 0x8005E194, FlyOut = 0x8005E278;

    const uint ViewYaw = 0x801AEC5E;    // s16, the camera's yaw
    const uint RecordAt = 0x80;         // the routine's sp+0x80 holds the item's record
    const uint YawField = 0x26;         // s16, the yaw the spin steps
    const int Spin = 0x40;              // the spin's step a pass

    /// <summary>The tilt's limit either way: a quarter turn shows the top or the underside.</summary>
    const float PitchLimit = 0x400;

    /// <summary>The stick at full deflection, in angle units a second (about 180 degrees).</summary>
    const float StickRate = 0x800;

    /// <summary>A gyro rate below this, in radians a second, is a hand at rest, not a turn:
    /// it keeps a pad's drift from creeping the item or holding its spin.</summary>
    const float GyroRest = 0.05f;

    /// <summary>How long after the last turn the game's spin comes back.</summary>
    const double ResumeMs = 1500;

    /// <summary>How quickly the turn eases back once the item is let go: the time constant, in seconds.</summary>
    const double EaseSeconds = 0.08;

    const float Units = 4096f / (2f * MathF.PI);     // angle units a radian

    public const string OnKey = "kf3.itemturn.on";
    public const string GyroKey = "kf3.itemturn.gyro";

    public static bool Enabled = true;
    public static bool UseGyro = true;

    static readonly HashSet<string> _fromEnv = [];
    static readonly ModInfo _self = new()
    {
        Id = "kf3.itemturn",
        Name = "Item turn",
        Version = "1.0",
        Description = "Turns a picked-up item with the mouse, the right stick or the gyroscope.",
    };

    enum Phase { Idle, FlyIn, Hold, Return }

    static Phase _phase;
    static uint _record;
    static float _yaw, _pitch;          // what the player added: yaw on the record's, tilt about the camera's right
    static long _stepped = -1;          // the drawn frame the input was last taken on
    static double _lastMs, _turnedMs = double.NegativeInfinity;
    static readonly Stopwatch _clock = Stopwatch.StartNew();

    static bool _probe, _queued;
    static double _probeAt;
    static long _drawn, _undone, _mouseFrames, _stickFrames, _gyroFrames;

    static int _test = -1;
    static bool _testing;
    static readonly Stopwatch _testClock = new();
    static double _holdMs;

    /// <summary>Inside the pickup, so the mouse is the item's: the view's lead
    /// must not show it (ViewSmoothing).</summary>
    public static bool HoldsMouse => Enabled && _phase != Phase.Idle;

    /// <summary>Whether the walk may be asked to draw an item turned: it then makes
    /// room for the substitute rotation (ModelWalk.Run).</summary>
    public static bool Showing => Enabled && _phase is Phase.Hold or Phase.Return;

    public static void Configure()
    {
        Kept.Env("KF3_ITEMTURN", OnKey, ref Enabled, _fromEnv);
        Kept.Env("KF3_ITEMTURN_GYRO", GyroKey, ref UseGyro, _fromEnv);
        _probe = Environment.GetEnvironmentVariable("KF3_ITEMTURN_PROBE")?.Trim() is "1" or "on" or "true";
        var test = Environment.GetEnvironmentVariable("KF3_ITEMTURN_TEST")?.Trim();
        if (!string.IsNullOrEmpty(test))
        {
            bool hex = test.StartsWith("0x", StringComparison.OrdinalIgnoreCase);
            if (!int.TryParse(hex ? test[2..] : test, hex ? System.Globalization.NumberStyles.HexNumber
                                                          : System.Globalization.NumberStyles.Integer, null, out _test))
                throw new ArgumentException($"KF3_ITEMTURN_TEST: bad item id '{test}'");
            _probe = true;
        }
    }

    public static void Install()
    {
        Event.AddListener<OverlayLoadedEvent>(e =>
        {
            if (_test >= 0 && !_testClock.IsRunning && e.Name.StartsWith("fdat", StringComparison.Ordinal))
                _testClock.Start();
        });
        Event.AddListener<RuntimeReadyEvent>(_ =>
        {
            Kept.Saved(OnKey, ref Enabled, _fromEnv);
            Kept.Saved(GyroKey, ref UseGyro, _fromEnv);
            SetGyro(UseGyro);
        });
        HookAttach.OnOverlayLoad("item turn", Attach, "See \"Turning a picked-up item\" in docs/INPUT.md.");
    }

    public static void SetEnabled(bool on)
    {
        Enabled = on;
        SetGyro(UseGyro);
    }

    /// <summary>The pad's gyroscope is switched on only while it would be used: a pad
    /// streams a larger report once it is (runtime 0102). GyroAim reads it too.</summary>
    public static void SetGyro(bool on)
    {
        UseGyro = on;
        GyroAim.WantGyro();
    }

    static bool Attach()
    {
        SymbolRegistry.Build();
        var routine = SymbolRegistry.Resolve("game", null, Routine);
        var stage15 = SymbolRegistry.Resolve("game", null, Stage15Routine);
        if (routine == null || stage15 == null)
        {
            Console.Error.WriteLine("[KF3] item turn: not installed (no game function at " +
                                    $"0x{(routine == null ? Routine : Stage15Routine):X8})");
            return false;
        }

        if (!_queued)
        {
            _queued = HookManager.AddPre(_self, routine, Own(nameof(Enter)))
                      && HookManager.AddPost(_self, routine, Own(nameof(Leave)))
                      && HookManager.AddPre(_self, stage15, Own(nameof(BeforeStage15)));
            if (_queued && _test >= 0 && SymbolRegistry.Resolve("game", null, PlayerTick) is { } tick)
                HookManager.AddPre(_self, tick, Own(nameof(Test)));
            if (!_queued) return false;
        }

        HookManager.Commit();
        bool ok = HookAttach.Installed(routine) && HookAttach.Installed(stage15);
        Console.WriteLine(ok ? $"[KF3] item turn: {(Enabled ? "on" : "off")}, gyro {(UseGyro ? "on" : "off")}"
                             : "[KF3] item turn: not installed");
        return ok;
    }

    static MethodInfo Own(string name) =>
        typeof(ItemTurn).GetMethod(name, BindingFlags.Public | BindingFlags.Static)!;

    public static bool Enter(CpuContext c, IMemory m)
    {
        _phase = Phase.FlyIn;
        _record = 0;
        _yaw = _pitch = 0f;
        _turnedMs = double.NegativeInfinity;
        _stepped = -1;
        return true;
    }

    public static void Leave(CpuContext c, IMemory m)
    {
        if (_probe && _phase != Phase.Idle)
            Console.WriteLine($"[KF3] item turn: put away at yaw {_yaw:0} tilt {_pitch:0}");
        if (_phase == Phase.Hold || _phase == Phase.Return)
            // Motion gathered while the item was held is nobody's turn of the view.
            Mouse.TakeLook();
        _phase = Phase.Idle;
        _record = 0;
        _yaw = _pitch = 0f;
    }

    /// <summary>Which part of the pickup is drawing, by where stage 15 was called from.
    /// A redraw of loop pacing's comes back with the same return address, so the spin
    /// is undone only on the loop's own call.</summary>
    public static bool BeforeStage15(CpuContext c, IMemory m)
    {
        if (_phase == Phase.Idle || !Enabled) return true;

        switch (c.RA)
        {
            case Hold:
                if (_phase != Phase.Hold)
                {
                    _phase = Phase.Hold;
                    _lastMs = _holdMs = _clock.Elapsed.TotalMilliseconds;
                }
                _record = m.ReadU32(c.SP + RecordAt);
                if (!LoopPacing.InRedraw && Turning && _record != 0)
                {
                    uint at = _record + YawField;
                    m.WriteU16(at, (ushort)((m.ReadU16(at) - Spin) & 0xFFF));
                    if (_probe) _undone++;
                }
                break;
            case FaceBack:
            case FlyOut:
                if (_probe && _phase != Phase.Return)
                    Console.WriteLine($"[KF3] item turn: let go at yaw {_yaw:0} tilt {_pitch:0}, " +
                                      (c.RA == FaceBack ? "turning back to face the camera" : "flying out"));
                _phase = Phase.Return;
                break;
        }

        if (_probe) Probe();
        return true;
    }

    static bool Turning => _clock.Elapsed.TotalMilliseconds - _turnedMs < ResumeMs;

    /// <summary>Whether this record is the item held up, for the walk.</summary>
    public static bool Shows(uint record) => Showing && record == _record && _record != 0;

    /// <summary>The rotation to draw the item with: the record's (as carried) with
    /// the player's turn added. Takes the input once a drawn frame.</summary>
    public static void Present(IMemory m, ref short x, ref short y, ref short z)
    {
        Step();
        if (_yaw == 0f && _pitch == 0f) return;
        if (_probe) _drawn++;

        int view = (short)m.ReadU16(ViewYaw);
        var turned = Mul(Mul(Mul(RyP(view), Rx(_pitch)), RyP(-view)), Euler(y + _yaw, x, z));
        (x, y, z) = Angles(turned);
    }

    static void Step()
    {
        long frame = FramePacing.Frames;
        if (frame == _stepped) return;
        _stepped = frame;

        double now = _clock.Elapsed.TotalMilliseconds;
        float dt = (float)Math.Clamp((now - _lastMs) / 1000.0, 0.0, 0.1);
        _lastMs = now;

        if (_phase == Phase.Return)
        {
            float keep = (float)Math.Exp(-dt / EaseSeconds);
            _yaw = Settle(Wrap(_yaw) * keep);
            _pitch = Settle(_pitch * keep);
            return;
        }

        float dYaw = 0f, dPitch = 0f;

        if (Mouse.Enabled && Mouse.Captured)
        {
            // TakeLook's turn is the look routine's, right negative; its pitch is
            // positive down with the player's inversion already in it.
            var (turn, look) = Mouse.TakeLook();
            if (turn != 0f || look != 0f)
            {
                dYaw -= turn;
                dPitch += look;
                if (_probe) _mouseFrames++;
            }
        }

        if (Analog.Enabled && Analog.AnalogLook)
        {
            var (sx, sy) = Analog.RightStick;
            // KF3_ITEMTURN_TEST: the stick full right and half down for the hold's second second.
            if (_testing && now - _holdMs is >= 1000.0 and < 2000.0) (sx, sy) = (1f, 0.5f);
            if (sx != 0f || sy != 0f)
            {
                dYaw += sx * StickRate * Analog.TurnSens * dt * (Analog.InvertTurn ? -1f : 1f);
                dPitch += sy * StickRate * Analog.PitchSens * dt * (Analog.InvertPitch ? -1f : 1f);
                if (_probe) _stickFrames++;
            }
        }

        if (UseGyro && Controller.Gyro)
        {
            // SDL's axes for a pad held in front of you: x across it, y up, z toward
            // you, anticlockwise positive. Turning the pad to the left (+y) brings the
            // near side right, and tipping its far edge up (+x) brings it down: the
            // item turns as the pad does.
            float gx = Rest(Controller.GyroX), gy = Rest(Controller.GyroY);
            if (gx != 0f || gy != 0f)
            {
                dYaw += gy * Units * dt;
                dPitch += gx * Units * dt;
                if (_probe) _gyroFrames++;
            }
        }

        if (dYaw == 0f && dPitch == 0f) return;
        _turnedMs = now;
        _yaw = Wrap(_yaw + dYaw);
        _pitch = Math.Clamp(_pitch + dPitch, -PitchLimit, PitchLimit);
    }

    static float Rest(float rate) => MathF.Abs(rate) < GyroRest ? 0f : rate;

    /// <summary>Into -0x800..0x800, so easing back takes the short way round.</summary>
    static float Wrap(float a)
    {
        a %= 4096f;
        if (a >= 2048f) a -= 4096f;
        if (a < -2048f) a += 4096f;
        return a;
    }

    static float Settle(float a) => MathF.Abs(a) < 0.5f ? 0f : a;

    // ---- the game's rotation, in doubles ---------------------------------------

    static double Rad(double units) => units * (2.0 * Math.PI / 4096.0);

    /// <summary><c>func_8001660C</c>: <c>[[c,0,-s],[0,1,0],[s,0,c]]</c>.</summary>
    static double[,] RyP(double units)
    {
        double c = Math.Cos(Rad(units)), s = Math.Sin(Rad(units));
        return new[,] { { c, 0, -s }, { 0, 1, 0 }, { s, 0, c } };
    }

    /// <summary><c>func_80016598</c>.</summary>
    static double[,] Rx(double units)
    {
        double c = Math.Cos(Rad(units)), s = Math.Sin(Rad(units));
        return new[,] { { 1, 0, 0 }, { 0, c, -s }, { 0, s, c } };
    }

    /// <summary><c>func_80016680</c>.</summary>
    static double[,] Rz(double units)
    {
        double c = Math.Cos(Rad(units)), s = Math.Sin(Rad(units));
        return new[,] { { c, -s, 0 }, { s, c, 0 }, { 0, 0, 1 } };
    }

    /// <summary><c>func_800166F4</c>: <c>Ry'(y) Rx(x) Rz(z)</c>.</summary>
    static double[,] Euler(double y, double x, double z) => Mul(Mul(RyP(y), Rx(x)), Rz(z));

    static double[,] Mul(double[,] a, double[,] b)
    {
        var r = new double[3, 3];
        for (int i = 0; i < 3; i++)
        for (int j = 0; j < 3; j++)
            r[i, j] = a[i, 0] * b[0, j] + a[i, 1] * b[1, j] + a[i, 2] * b[2, j];
        return r;
    }

    /// <summary>The game's three angles of a rotation, <see cref="Euler"/>'s inverse.
    /// Column 2 is <c>(-sin y cos x, -sin x, cos y cos x)</c> and row 1
    /// <c>(cos x sin z, cos x cos z, -sin x)</c>; straight up or down, z is taken as 0
    /// and y read off column 0, <c>(cos y, 0, sin y)</c>.</summary>
    static (short X, short Y, short Z) Angles(double[,] m)
    {
        double sx = Math.Clamp(-m[1, 2], -1.0, 1.0);
        double x = Math.Asin(sx), y, z;
        if (Math.Sqrt(m[1, 0] * m[1, 0] + m[1, 1] * m[1, 1]) > 1e-9)
        {
            y = Math.Atan2(-m[0, 2], m[2, 2]);
            z = Math.Atan2(m[1, 0], m[1, 1]);
        }
        else
        {
            y = Math.Atan2(m[2, 0], m[0, 0]);
            z = 0.0;
        }
        return (Angle(x), Angle(y), Angle(z));
    }

    static short Angle(double rad) => (short)((int)Math.Round(rad * (4096.0 / (2.0 * Math.PI))) & 0xFFF);

    // ---- KF3_ITEMTURN_TEST ----------------------------------------------------------

    /// <summary>Hold the test item up from the player's tick, once, as the examine
    /// handler would: <c>func_8005DB30(0, id)</c> makes the record itself.</summary>
    public static bool Test(CpuContext c, IMemory m)
    {
        if (_test < 0 || !_testClock.IsRunning || _testClock.Elapsed.TotalSeconds < 10.0) return true;
        // Only on an iteration the world ticks, as the examine handler inside stage 4
        // is: this pre-hook runs ahead of pacing's gate, and a pickup entered on a
        // skipped iteration would run its whole loop with no tick for the smoothers.
        if (!FramePacing.IterationTicked) return true;
        if (m is not PSMemory mem) return true;
        int id = _test;
        _test = -1;
        Console.WriteLine($"[KF3] item turn: test item 0x{id:X}");
        var saved = c.Snapshot();
        c.A0 = 0;
        c.A1 = (uint)id;
        _testing = true;
        Recompiled.KingsField3_game.func_8005DB30(c, mem);
        _testing = false;
        c.Restore(saved);
        Console.WriteLine($"[KF3] item turn: test item 0x{id:X} put away");
        Console.Out.Flush();
        return true;
    }

    // ---- the probe ---------------------------------------------------------------

    static void Probe()
    {
        double now = _clock.Elapsed.TotalMilliseconds;
        if (_probeAt <= 0.0) { _probeAt = now; return; }
        if (now - _probeAt < 1000.0) return;
        Console.WriteLine($"[KF3] item turn: {_phase} record 0x{_record:X8} yaw {_yaw:0} tilt {_pitch:0} " +
                          $"drawn {_drawn} spin held {_undone} | frames mouse {_mouseFrames} stick {_stickFrames} " +
                          $"gyro {_gyroFrames} (pad gyro {(Controller.Gyro ? "on" : "off")})");
        _probeAt = now;
        _drawn = _undone = _mouseFrames = _stickFrames = _gyroFrames = 0;
    }
}
