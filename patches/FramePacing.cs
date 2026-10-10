using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using RecompOne.Runtime.Context;
using RecompOne.Runtime.Events;
using RecompOne.Runtime.Memory;
using RecompOne.Runtime.Modding;
using RecompOne.Runtime.Sdk;

namespace Kf3;

/// <summary>
/// Draw at any rate and tick the world at the game's own 15 Hz:
///
///     KF3_FPS=60|144|off   the picture's rate (off unless set: not judged yet)
///     KF3_TICKRATE=15      the world's (a comparison only)
///     KF3_FPS_PROBE=1      a line a second: fps drawn, ticks taken, packets drawn
///
/// The game's frame gate func_80019614 (four vblanks, 15 fps at most) is skipped;
/// the frame boundary is the DrawOTag after a VSync call, paced there; main-loop
/// stages 1-14 run only on a tick of the world clock, and stage 15, which builds
/// and draws the whole frame, runs every frame. Verdite2's FramePacing, with this
/// game's loop. See "Frame pacing" in docs/DEVELOPMENT.md and "The main loop" in
/// docs/GAME_INTERNALS.md.
/// </summary>
public static class FramePacing
{
    // The frame cap's sleep, as a section of the frame profiler.
    public static readonly int FloorWait = RecompOne.Runtime.Diagnostics.Profiler.Register(
        "FramePacing.Floor (frame cap)", RecompOne.Runtime.Diagnostics.ProfileGroup.Wait);

    public const double DefaultTickRate = 15.0;
    public static double LogicHz { get; private set; } = DefaultTickRate;
    public static double TargetFps { get; private set; }
    public static bool Enabled { get; private set; }

    /// <summary>The picture is drawn as fast as it can, with no target rate.</summary>
    public static bool Uncapped => TargetFps <= 0.0;

    const double SpinMs = 1.5;

    // The libgpu DrawOTag and libetc VSync each executable binds (config/kf3.json).
    static readonly (string Overlay, uint Addr)[] DrawOTag =
        [("open", 0x800166CC), ("game", 0x8007A104), ("end", 0x80014428)];
    static readonly (string Overlay, uint Addr)[] VSyncThunk =
        [("open", 0x8001FE6C), ("game", 0x8007910C), ("end", 0x8001C208)];

    const uint FrameGate = 0x80019614;       // spins VSync(0) while VBlankCount < 4
    const uint VBlankCount = 0x801C12EC;     // u32 the vblank event func_80019570 bumps
    const uint OtPointer = 0x801A9174;       // u32: the ordering table being built

    // Stages 1-14 of the main loop at 0x80014F24, and the jal that calls each from
    // there; a call from anywhere else (the card loader's wait loop calls stages 8,
    // 13 and 14) is not gated. None of them writes the ordering table in play.
    static readonly (uint Addr, uint Site)[] Stages =
    [
        (0x800341E8, 0x80014F24), (0x80034180, 0x80014F2C), (0x80047010, 0x80014F34),
        (0x80030FCC, 0x80014F3C), (0x80052E5C, 0x80014F44), (0x8005BC50, 0x80014F4C),
        (0x8005EB20, 0x80014F5C), (0x80018358, 0x80014F64), (0x80061940, 0x80014F6C),
        (0x8002B330, 0x80014F78), (0x800156BC, 0x80014F84), (0x80034300, 0x80014F8C),
        (0x80018CD0, 0x80014F94), (0x80015A48, 0x80014F9C),
    ];
    static readonly HashSet<uint> _sites = Stages.Select(s => s.Site + 8).ToHashSet();
    const uint FirstSite = 0x80014F24 + 8;

    internal static ModInfo Mod => _self;
    static readonly ModInfo _self = new() { Id = "kf3.framepacing", Name = "Frame pacing", Version = "1.0" };

    static readonly Stopwatch _clock = Stopwatch.StartNew();
    static double _due;
    static long _frames;
    static int _vsyncCalls;
    static double _lastBoundaryMs = -1.0;
    static bool _inGame;

    // The logic clock: credit in ticks, advanced by wall time at each boundary.
    static double _logicCredit;
    static double _logicClockMs = -1.0;
    static bool _tickThisFrame = true;

    // Decided once per main-loop iteration, at stage 1, so all fourteen agree even
    // when a menu inside stage 4 presents frames of its own between them.
    static bool _iterationTicks = true;

    const double BoundaryDeadMs = 500.0;
    static double _fallbackNextMs = -1.0;
    static bool _fallbackSaid;
    static long _fallbackTicks;

    static bool _probe;
    static double _windowStart;
    static long _windowFrames, _windowTicks, _windowPresentsMark, _windowStageRuns, _windowStageSkips;
    static long _packetsTick, _packetsTickFrames, _packetsIdle, _packetsIdleFrames;

    /// <summary>Frames a second over the last second; 0 until the first.</summary>
    public static double Measured { get; private set; }

    /// <summary>Whether the world advanced on the frame now being drawn.</summary>
    public static bool TickedThisFrame => !Enabled || _tickThisFrame;

    /// <summary>Whether the main-loop iteration now running ran its stages.</summary>
    public static bool IterationTicked => !Frozen && (!Enabled || _iterationTicks);

    /// <summary>
    /// The world is being drawn again as it stood at its last frame (MenuWorld's pass
    /// behind a menu or a message), not advanced: no walk is a tick's first, no
    /// iteration ticked, and <see cref="TickFraction"/> is <see cref="FrozenFraction"/>,
    /// so every smoother draws what it drew last. A menu's own frames are boundaries
    /// too and move <see cref="Ticks"/>, which is why the flag is needed.
    /// </summary>
    public static bool Frozen { get; set; }

    /// <summary>The tick fraction a frozen pass draws at: the last frame's.</summary>
    public static double FrozenFraction { get; set; } = 1.0;

    /// <summary>Frame boundaries reached: an identity, not a rate.</summary>
    public static long Frames => _frames;

    /// <summary>How far the world clock is toward its next tick, 0..1: where a frame
    /// drawn now falls between the last two ticks. 1 while the boundary is lost.</summary>
    public static double TickFraction
    {
        get
        {
            if (Frozen) return FrozenFraction;
            if (!Enabled || _lastBoundaryMs < 0.0) return 1.0;
            if (_clock.Elapsed.TotalMilliseconds - _lastBoundaryMs > BoundaryDeadMs) return 1.0;
            return Math.Clamp(_logicCredit, 0.0, 1.0);
        }
    }

    /// <summary>World ticks taken: an identity, not a rate.</summary>
    public static long Ticks { get; private set; }

    /// <summary>True for the first walk of the current tick, so a per-frame walk
    /// can be held to the world's rate; always true when pacing is off, and false
    /// while <see cref="Frozen"/>.</summary>
    public static bool FirstWalkOfTick(ref long seen)
    {
        if (Frozen) return false;
        if (!Enabled) return true;
        if (seen != Ticks) { seen = Ticks; return true; }
        return false;
    }

    public static void Configure(string? fps, string? tickRate, string? probe)
    {
        _probe = probe == "1";
        if (!string.IsNullOrWhiteSpace(tickRate))
        {
            if (!double.TryParse(tickRate, NumberStyles.Float, CultureInfo.InvariantCulture, out double hz)
                || !double.IsFinite(hz))
                throw new ArgumentException($"KF3_TICKRATE: cannot read '{tickRate}'");
            SetTickRate(hz);
        }
        Enabled = true;
        if (string.IsNullOrWhiteSpace(fps)) { TargetFps = DefaultFps; return; }
        if (fps.Equals("off", StringComparison.OrdinalIgnoreCase)) TargetFps = 0.0;
        else if (double.TryParse(fps, NumberStyles.Float, CultureInfo.InvariantCulture, out double rate))
            TargetFps = Math.Clamp(rate, 5.0, 1000.0);
        else throw new ArgumentException($"KF3_FPS: cannot read '{fps}'");
    }

    /// <summary>The frame rate pacing aims for when none is chosen: KF3_FPS unset and
    /// no rate kept.</summary>
    public const double DefaultFps = 144.0;

    static double _hostFps = -1.0;

    /// <summary>Turn pacing on or off while the game runs. The hooks are attached in
    /// either state and pass everything through while it is off.</summary>
    public static void SetEnabled(bool on)
    {
        if (on == Enabled) return;
        Enabled = on;
        _iterationTicks = _tickThisFrame = true;
        _logicClockMs = -1.0;
        _lastBoundaryMs = -1.0;
        if (!_inGame) return;
        if (on) ApplyHostCeiling();
        else if (_hostFps >= 0.0) RecompOne.Runtime.Runtime.TargetFps = _hostFps;
    }

    /// <summary>The drawn rate, 0 for uncapped.</summary>
    public static void SetTarget(double fps)
    {
        TargetFps = fps <= 0.0 ? 0.0 : Math.Clamp(fps, 5.0, 1000.0);
        _due = 0.0;
        if (Enabled && _inGame) ApplyHostCeiling();
    }

    /// <summary>The world's live rate, keeping its progress toward the next tick.</summary>
    public static void SetTickRate(double hz)
    {
        if (!double.IsFinite(hz)) throw new ArgumentOutOfRangeException(nameof(hz));
        hz = Math.Clamp(hz, 5.0, 60.0);
        if (hz == LogicHz) return;
        LogicHz = hz;
        _fallbackNextMs = -1.0;
    }

    public static bool ProbeOn { get => _probe; set => _probe = value; }

    public static void Install()
    {
        // Attached whether or not KF3_FPS is set, so the Testing tab can turn pacing on.
        // Only GAME.EXE has a world to pace. OPEN.EXE and END.EXE keep the runtime's
        // own 60 Hz throttle and their own waits, as with pacing off.
        Event.AddListener<OverlayLoadedEvent>(e =>
        {
            if (_hostFps < 0.0) _hostFps = RecompOne.Runtime.Runtime.TargetFps;
            if (e.Name is "open" or "end") { _inGame = false; if (Enabled) RecompOne.Runtime.Runtime.TargetFps = 60.0; }
            else if (e.Name == "game") { _inGame = true; _logicClockMs = -1.0; if (Enabled) ApplyHostCeiling(); }
        });
        Event.AddListener<VSyncEvent>(_ => WatchdogProbe());
        HookAttach.OnOverlayLoad("pacing", Attach, "See \"Frame pacing\" in docs/DEVELOPMENT.md.");
    }

    // RecompOne's own throttle paces per VSync call; it is only a backstop here.
    static void ApplyHostCeiling()
        => RecompOne.Runtime.Runtime.TargetFps = Uncapped ? 0.0 : Math.Max(60.0, TargetFps * 2.0);

    static readonly HashSet<string> _otHooked = [], _vsHooked = [];
    static readonly HashSet<uint> _stageHooked = [];
    static bool _gateHooked;

    static bool Attach()
    {
        SymbolRegistry.Build();
        var self = typeof(FramePacing);
        MethodInfo M(string n) => self.GetMethod(n, BindingFlags.Public | BindingFlags.Static)!;

        List<(string, MethodInfo)> ot = [], vs = [];
        List<(uint, MethodInfo)> stages = [];
        MethodInfo? gate = null;

        // KF3_PACING_NOBOUNDARY=1 leaves DrawOTag unhooked, to test the watchdog.
        bool noBoundary = Environment.GetEnvironmentVariable("KF3_PACING_NOBOUNDARY") == "1";
        foreach (var (overlay, addr) in DrawOTag)
            if (!noBoundary && !_otHooked.Contains(overlay) && SymbolRegistry.Resolve(overlay, null, addr) is { } t
                && HookManager.AddPost(_self, t, M(nameof(AfterDrawOTag)))) ot.Add((overlay, t));
        foreach (var (overlay, addr) in VSyncThunk)
            if (!_vsHooked.Contains(overlay) && SymbolRegistry.Resolve(overlay, null, addr) is { } t
                && HookManager.AddPre(_self, t, M(nameof(BeforeVSync)))) vs.Add((overlay, t));
        if (!_gateHooked && SymbolRegistry.Resolve("game", null, FrameGate) is { } g
            && HookManager.AddPre(_self, g, M(nameof(BeforeFrameGate)))) gate = g;
        foreach (var (addr, _) in Stages)
            if (!_stageHooked.Contains(addr) && SymbolRegistry.Resolve("game", null, addr) is { } t
                && HookManager.AddPre(_self, t, M(nameof(BeforeStage)))) stages.Add((addr, t));

        HookManager.Commit();

        foreach (var (o, t) in ot) if (HookAttach.Installed(t)) _otHooked.Add(o);
        foreach (var (o, t) in vs) if (HookAttach.Installed(t)) _vsHooked.Add(o);
        foreach (var (a, t) in stages) if (HookAttach.Installed(t)) _stageHooked.Add(a);
        if (gate != null && HookAttach.Installed(gate)) _gateHooked = true;

        Console.WriteLine($"[KF3] pacing: {(Uncapped ? "uncapped" : $"{TargetFps:0.#} fps")}, " +
                          $"boundary {_otHooked.Count}/{DrawOTag.Length} DrawOTag + " +
                          $"{_vsHooked.Count}/{VSyncThunk.Length} VSync, " +
                          $"{_stageHooked.Count}/{Stages.Length} stage(s), " +
                          $"frame gate {(_gateHooked ? "skipped" : "IN PLACE")}, world at {LogicHz:0.#} Hz");

        bool complete = _otHooked.Count == DrawOTag.Length && _vsHooked.Count == VSyncThunk.Length
                        && _stageHooked.Count == Stages.Length && _gateHooked;
        if (!complete) Console.Error.WriteLine("[KF3] pacing: attach incomplete -- will try the rest on the next overlay load.");
        return complete;
    }

    /// <summary>The game's frame gate, skipped; the count it would have zeroed is.</summary>
    public static bool BeforeFrameGate(CpuContext c, IMemory m)
    {
        if (!Enabled) return true;
        m.WriteU32(VBlankCount, 0u);
        return false;
    }

    public static void BeforeVSync(CpuContext c, IMemory m) => _vsyncCalls++;

    /// <summary>The frame boundary: the ordering table drawn after a VSync call.</summary>
    public static void AfterDrawOTag(CpuContext c, IMemory m)
    {
        if (!Enabled || _vsyncCalls == 0 || !_inGame) return;
        _vsyncCalls = 0;
        _frames++;

        double now = _clock.Elapsed.TotalMilliseconds;
        _lastBoundaryMs = now;

        if (_probe) Probe(m, now);

        AdvanceLogicClock(now);
        if (!Uncapped) Floor(1000.0 / TargetFps);
    }

    static void AdvanceLogicClock(double nowMs)
    {
        bool first = _logicClockMs < 0.0;
        double dt = first ? 0.0 : nowMs - _logicClockMs;
        _logicClockMs = nowMs;

        // Over a quarter second is the game not drawing (a disc read), not lateness.
        if (first || dt > 250.0) _logicCredit = 1.0;
        else if (dt > 0.0) _logicCredit = Math.Min(_logicCredit + dt * LogicHz / 1000.0, 2.0);

        _tickThisFrame = _logicCredit >= 1.0;
        if (_tickThisFrame) { _logicCredit -= 1.0; _windowTicks++; Ticks++; }
    }

    /// <summary>Skip a main-loop stage on an iteration the world clock did not tick.</summary>
    public static bool BeforeStage(CpuContext c, IMemory m)
    {
        if (!Enabled || !_sites.Contains(c.RA)) return true;
        if (c.RA == FirstSite)
        {
            double now = _clock.Elapsed.TotalMilliseconds;
            bool live = _lastBoundaryMs >= 0.0 && now - _lastBoundaryMs <= BoundaryDeadMs;
            _iterationTicks = live ? _tickThisFrame : FallbackTick(now);
        }
        if (_iterationTicks) _windowStageRuns++; else _windowStageSkips++;
        return _iterationTicks;
    }

    /// <summary>No boundary for half a second: the world ticks from the wall clock
    /// here instead, and the loop is paced here, so a lost hook cannot run the world
    /// at the render rate.</summary>
    static bool FallbackTick(double now)
    {
        if (!_fallbackSaid)
        {
            _fallbackSaid = true;
            Console.Error.WriteLine($"[KF3] pacing: no frame boundary reached -- ticking at {LogicHz:0.#} Hz " +
                                    "from the wall clock at stage 1 instead. See \"Frame pacing\" in docs/DEVELOPMENT.md.");
        }
        double period = 1000.0 / LogicHz;
        if (_fallbackNextMs < 0.0 || now - _fallbackNextMs > 4.0 * period) _fallbackNextMs = now;
        bool tick = now >= _fallbackNextMs;
        if (tick) { _fallbackNextMs += period; _fallbackTicks++; Ticks++; }
        _tickThisFrame = tick;
        if (!Uncapped) Floor(1000.0 / TargetFps);
        return tick;
    }

    static void Floor(double min)
    {
        double now = _clock.Elapsed.TotalMilliseconds;
        if (_due < now - min) _due = now;
        if (now < _due)
        {
            int profile = RecompOne.Runtime.Diagnostics.Profiler.Begin(FloorWait);
            double sleepUntil = _due - SpinMs;
            if (now < sleepUntil && (int)(sleepUntil - now) is > 0 and var ms) Thread.Sleep(ms);
            while (_clock.Elapsed.TotalMilliseconds < _due) Thread.SpinWait(48);
            RecompOne.Runtime.Diagnostics.Profiler.End(profile);
        }
        _due += min;
    }

    // ---- KF3_FPS_PROBE ----------------------------------------------------------

    static void Probe(IMemory m, double now)
    {
        // Packets the frame just drawn carried, split by whether the world ticked on
        // it: a gated stage that fed the picture would show as fewer on idle frames.
        long packets = CountPackets(m);
        if (_iterationTicks) { _packetsTick += packets; _packetsTickFrames++; }
        else { _packetsIdle += packets; _packetsIdleFrames++; }

        _windowFrames++;
        double elapsed = now - _windowStart;
        if (elapsed < 1000.0) return;
        Measured = _windowFrames * 1000.0 / elapsed;
        long presents = LibEtc.VSyncCalls - _windowPresentsMark;
        _windowPresentsMark = LibEtc.VSyncCalls;
        Console.WriteLine($"[KF3] pacing: {Measured:0.0} fps drawn of {(Uncapped ? "uncapped" : $"{TargetFps:0.#}")}, " +
                          $"{presents * 1000.0 / elapsed:0.0} VSync call(s)/s, " +
                          $"{_windowTicks * 1000.0 / elapsed:0.0} tick(s)/s of {LogicHz:0.#} Hz, " +
                          $"stages {_windowStageRuns} run/{_windowStageSkips} skipped, " +
                          $"packets/frame {Avg(_packetsTick, _packetsTickFrames)} ticked, " +
                          $"{Avg(_packetsIdle, _packetsIdleFrames)} idle");
        _windowStart = now;
        _windowFrames = _windowTicks = _windowStageRuns = _windowStageSkips = 0;
        _packetsTick = _packetsTickFrames = _packetsIdle = _packetsIdleFrames = 0;
    }

    static string Avg(long sum, long n) => n == 0 ? "-" : (sum / (double)n).ToString("0", CultureInfo.InvariantCulture);

    // The table is drawn from its last entry (ot + 0x7FFC) and linked by the low 24
    // bits of each tag; the top byte is the packet's length in words.
    static long CountPackets(IMemory m)
    {
        uint ot = m.ReadU32(OtPointer);
        if ((ot & 0xFF000000) != 0x80000000) return 0;
        uint p = (ot + 0x7FFC) & 0xFFFFFF;
        long n = 0;
        for (int guard = 0; guard < 200000 && p != 0xFFFFFF; guard++)
        {
            uint tag = m.ReadU32(0x80000000 | p);
            if ((tag >> 24) != 0) n++;
            p = tag & 0xFFFFFF;
        }
        return n;
    }

    /// <summary>The probe's half from the vblank, when the boundary is lost.</summary>
    static double _wdStart = -1;
    static void WatchdogProbe()
    {
        if (!_probe) return;
        double now = _clock.Elapsed.TotalMilliseconds;
        if (_wdStart < 0) { _wdStart = now; return; }
        if (now - _wdStart < 1000.0) return;
        if (_fallbackTicks > 0)
            Console.WriteLine($"[KF3] pacing: no boundary, {_fallbackTicks * 1000.0 / (now - _wdStart):0.0} tick(s)/s from the watchdog");
        _fallbackTicks = 0;
        _wdStart = now;
    }
}
