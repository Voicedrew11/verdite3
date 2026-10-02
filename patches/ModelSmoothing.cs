using System.Diagnostics;
using RecompOne.Runtime.Events;
using RecompOne.Runtime.Memory;

namespace Kf3;

/// <summary>
/// The per-record carry between world ticks: positions, facings and the MO clip
/// clock, keyed by table and slot. No hooks live here -- the C# model walk and
/// the C# MO blender call in, so a carried value is handed to code that already
/// places the geometry and never written into a record and put back.
///
///     KF3_SMOOTH_MODELS=1     carry the records between ticks (needs KF3_MODELWALK=1 and pacing; not judged)
///     KF3_SMOOTH_PROBE=1      a line a second: models carried, snaps, clip frames, wraps, turns, seeks, backward steps
///
/// <see cref="Carry"/> is the root, sampled and interpolated exactly as
/// <see cref="ViewSmoothing"/> carries the camera. <see cref="CarryClip"/> is
/// Verdite2's <c>Mode.Timeline</c>: the clip time is a point on a circle whose
/// circumference is the clip's own length, so the step is unwrapped against the
/// settled rate and folded forward through a wrap or a turn. See "3d. The clip
/// time carried" in docs/SMOOTHING.md.
/// </summary>
public static class ModelSmoothing
{
    /// <summary>A position step in one tick past which a record snaps (a warp, a spawn).</summary>
    public const int SnapUnits = 1536;

    /// <summary>An angle step in one tick past which a record snaps, 0x1000 a turn.</summary>
    public const int SnapAngle = 0x300;

    // The four tables the walk draws, and their record counts ("The models" in
    // docs/GAME_INTERNALS.md). Slots are flattened into one index space so every
    // sample array is a single allocation.
    static readonly int[] TableCount = [200, 396, 128, 128];
    static readonly int[] TableBase = [0, 200, 596, 724];
    const int Total = 852;

    // A small fixed number of clip states per slot: one submit usually runs the
    // blender once, and the index is the CarryClip call since Enter.
    const int ClipStates = 4;

    const int MaxSegments = 4096;

    const double RateTolRel = 0.5;
    const double RateTolAbs = 2.0;
    const double RateTolCap = 0.05;
    const double FirstStepFrac = 0.25;

    enum Fold { Circle, Mirror }
    enum Verdict { Still, Play, Hold }

    sealed class ClipState
    {
        public int Clip = -1;
        public uint Bank;
        public int PrevTime, CurTime;
        public bool HasCur, HasPrev;
        public int Length;
        public double Rate;
        public bool HasRate;
        public bool FirstStep = true;
        public double Delta;
        public Fold Path;
        public Verdict Say;
        public long Tick = -1;

        // The probe's backward-step test: the last unfolded path point drawn for
        // this slot, the tick it was drawn on, and whether there is one yet.
        public bool HasDrawn;
        public double LastDraw;
        public long LastDrawTick;
    }

    public static bool Enabled { get; set; }

    /// <summary>Carrying now: on and under pacing.</summary>
    public static bool Active => Enabled && FramePacing.Enabled;

    public static bool ProbeOn { get => _probe; set => _probe = value; }
    static bool _probe;

    // ---- the root carry ----
    static readonly long[] _tick = new long[Total];
    static readonly uint[] _id = new uint[Total];
    static readonly bool[] _primed = new bool[Total];
    static readonly int[] _px = new int[Total], _py = new int[Total], _pz = new int[Total];
    static readonly int[] _cx = new int[Total], _cy = new int[Total], _cz = new int[Total];
    static readonly short[] _pp = new short[Total], _pw = new short[Total], _pr = new short[Total];
    static readonly short[] _cp = new short[Total], _cw = new short[Total], _cr = new short[Total];

    // ---- the clip carry ----
    static readonly ClipState[] _clips = new ClipState[Total * ClipStates];
    static readonly Dictionary<(uint Bank, uint Clip), int> _lengths = [];

    // The record whose submit is in progress; -1 outside Enter/Leave.
    static int _inTable = -1, _inSlot = -1, _clipCalls;

    // ---- the probe ----
    static readonly Stopwatch _clock = Stopwatch.StartNew();
    static double _probeAt;
    static readonly long[] _drawnBy = new long[4];
    static long _drawn, _clipCalls2, _carried, _snaps, _clipFrames, _wraps, _turns, _reseek, _backward;

    static ModelSmoothing()
    {
        Array.Fill(_tick, -1L);
        for (int i = 0; i < _clips.Length; i++) _clips[i] = new ClipState();
    }

    public static void Configure(string? mode, string? probe)
    {
        // Off until judged by eye; the model walk must be C# for anything to call in.
        Enabled = mode?.Trim().ToLowerInvariant() is "1" or "on";
        _probe = probe?.Trim() == "1";
    }

    public static void Install()
    {
        // Installed in every state; Active decides per call, so the Testing tab can switch it.
        Event.AddListener<OverlayLoadedEvent>(_ => Reprime());
        // The clip time is carried only through the C# blender; positions need only the walk.
        MoPose.ClipCarry = CarryClip;
        Console.WriteLine($"[KF3] model smoothing: {(Enabled ? "on" : "off")}");
    }

    /// <summary>Forget every slot and every cached clip length: an overlay load
    /// re-links the banks, so a cached length is a read of someone else's memory.</summary>
    public static void Reprime()
    {
        Array.Fill(_tick, -1L);
        Array.Fill(_id, 0u);
        Array.Fill(_primed, false);
        foreach (var s in _clips)
        {
            s.Tick = -1;
            s.Clip = -1;
            s.Bank = 0;
            Reset(s);
        }
        _lengths.Clear();
        _inTable = _inSlot = -1;
        _clipCalls = 0;
    }

    /// <summary>
    /// One drawn record's position and facing, carried between its last two tick
    /// samples at <see cref="FramePacing.TickFraction"/>. The root is the record
    /// the walk draws, so the sample is taken here rather than restored later.
    /// </summary>
    public static void Carry(int table, int slot, uint identity,
        ref int x, ref int y, ref int z, ref short pitch, ref short yaw, ref short roll)
    {
        if (!Enabled) return;
        if (!FramePacing.Enabled) return;
        int i = Index(table, slot);
        if (i < 0) return;
        _drawn++;
        _drawnBy[table]++;

        int rx = x, ry = y, rz = z;
        short rp = pitch, rw = yaw, rr = roll;

        long ticks = FramePacing.Ticks;
        if (ticks != _tick[i] && FramePacing.IterationTicked)
        {
            // A missed tick (the record was culled) or a new tenant in the slot
            // has no pair to carry: prime. Otherwise roll the pair forward.
            if (!_primed[i] || _tick[i] != ticks - 1 || _id[i] != identity)
            {
                Prime(i, rx, ry, rz, rp, rw, rr);
            }
            else
            {
                _px[i] = _cx[i]; _py[i] = _cy[i]; _pz[i] = _cz[i];
                _cx[i] = rx; _cy[i] = ry; _cz[i] = rz;
                _pp[i] = _cp[i]; _pw[i] = _cw[i]; _pr[i] = _cr[i];
                _cp[i] = rp; _cw[i] = rw; _cr[i] = rr;
                if (Jump(i)) { _px[i] = _cx[i]; _py[i] = _cy[i]; _pz[i] = _cz[i];
                    _pp[i] = _cp[i]; _pw[i] = _cw[i]; _pr[i] = _cr[i]; _snaps++; }
            }
            _tick[i] = ticks;
            _id[i] = identity;
        }
        else if (_primed[i] && (rx != _cx[i] || ry != _cy[i] || rz != _cz[i] ||
                                rp != _cp[i] || rw != _cw[i] || rr != _cr[i]))
        {
            // Moved without a tick of the world (a stage outside pacing): no pair.
            Prime(i, rx, ry, rz, rp, rw, rr);
            _snaps++;
        }

        if (!_primed[i]) return;

        if (_px[i] != _cx[i] || _py[i] != _cy[i] || _pz[i] != _cz[i] ||
            _pp[i] != _cp[i] || _pw[i] != _cw[i] || _pr[i] != _cr[i]) _carried++;

        double t = FramePacing.TickFraction;
        x = Mix(_px[i], _cx[i], t);
        y = Mix(_py[i], _cy[i], t);
        z = Mix(_pz[i], _cz[i], t);
        pitch = Turn(_pp[i], _cp[i], t);
        yaw = Turn(_pw[i], _cw[i], t);
        roll = Turn(_pr[i], _cr[i], t);
    }

    /// <summary>The record whose submit is in progress; clip calls between
    /// <see cref="Enter"/> and <see cref="Leave"/> belong to it.</summary>
    public static void Enter(int table, int slot)
    {
        _inTable = table;
        _inSlot = slot;
        _clipCalls = 0;
    }

    public static void Leave()
    {
        _inTable = _inSlot = -1;
        _clipCalls = 0;
    }

    /// <summary>
    /// The carried clip time for the blender's clock, or false to draw the game's
    /// own time. <paramref name="floorTime"/> is the integer time and
    /// <paramref name="frac"/> the sub-unit fraction, both valid only on true.
    /// </summary>
    public static bool CarryClip(IMemory m, uint bank, uint clip, int time,
        out int floorTime, out double frac)
    {
        floorTime = time;
        frac = 0.0;
        if (!Active) return false;
        if (_inTable < 0) return false;
        _clipCalls2++;

        int ci = Index(_inTable, _inSlot);
        if (ci < 0 || (uint)_clipCalls >= ClipStates) return false;
        var s = _clips[ci * ClipStates + _clipCalls];
        _clipCalls++;

        long ticks = FramePacing.Ticks;

        // A changed clip byte or bank is a hard cut: show the game's own pose and
        // start a fresh rate.
        if (s.Clip != (int)clip || s.Bank != bank)
        {
            Reset(s);
            s.Clip = (int)clip; s.Bank = bank;
            s.Length = Duration(m, bank, clip);
            s.PrevTime = s.CurTime = time;
            s.HasCur = true;
            s.Tick = ticks;
            s.Say = Verdict.Hold;
            return false;
        }

        if (ticks != s.Tick && FramePacing.IterationTicked)
        {
            // A missed tick (the record was culled) has no pair to carry: prime.
            if (s.Tick != ticks - 1 || !s.HasCur)
            {
                Reset(s);
                s.Clip = (int)clip; s.Bank = bank;
                s.Length = Duration(m, bank, clip);
                s.PrevTime = s.CurTime = time;
                s.HasCur = true;
                s.Tick = ticks;
                s.Say = Verdict.Hold;
                return false;
            }

            s.Tick = ticks;
            s.PrevTime = s.CurTime;
            s.CurTime = time;
            s.HasPrev = true;
            if (s.Length <= 0) s.Length = Duration(m, bank, clip);
            Predict(s);
        }
        else if (s.HasCur && time != s.CurTime)
        {
            // Moved outside a tick: no pair to carry.
            Reset(s);
            s.Clip = (int)clip; s.Bank = bank;
            s.PrevTime = s.CurTime = time;
            s.HasCur = true;
            s.Say = Verdict.Hold;
            return false;
        }

        if (s.Say != Verdict.Play) return false;

        double raw = s.PrevTime + s.Delta * FramePacing.TickFraction;
        double folded = s.Path == Fold.Mirror ? Mirror(raw, s.Length) : Circle(raw, s.Length);
        int f = (int)Math.Floor(folded);
        double fr = folded - f;
        if (f < 0) { f = 0; fr = 0.0; }
        else if (f >= s.Length) { f = s.Length - 1; fr = 0.0; }
        floorTime = f;
        frac = fr;

        // The defect this mode fixes: a carried time that goes backwards against
        // its own tick's motion between two drawn frames of the same slot. The
        // unfolded path point `raw` advances with the tick fraction regardless of
        // any fold, so a same-tick comparison against `Delta` reads 0 for a wrap
        // and only fires if a frame actually stepped the clock backwards. A turn
        // is deliberate reverse motion and is counted separately.
        if (s.Path == Fold.Circle && s.Delta != 0.0)
        {
            if (s.HasDrawn && s.LastDrawTick == s.Tick && (raw - s.LastDraw) * s.Delta < 0.0) _backward++;
            s.LastDraw = raw;
            s.LastDrawTick = s.Tick;
            s.HasDrawn = true;
        }

        _clipFrames++;
        return true;
    }

    /// <summary>Called once per walk; with the probe on, a line a second.</summary>
    public static void ProbeFrame()
    {
        if (!_probe) return;
        double now = _clock.Elapsed.TotalMilliseconds;
        if (_probeAt <= 0.0) { _probeAt = now; return; }
        double dt = now - _probeAt;
        if (dt < 1000.0) return;
        Console.WriteLine($"[KF3] model smoothing: {_drawn * 1000.0 / dt:0} record(s) drawn/s ({_drawnBy[0] * 1000.0 / dt:0}/{_drawnBy[1] * 1000.0 / dt:0}/{_drawnBy[2] * 1000.0 / dt:0}/{_drawnBy[3] * 1000.0 / dt:0} by table), {_carried * 1000.0 / dt:0} carried, " +
                          $"{_snaps} snap(s), {_clipCalls2 * 1000.0 / dt:0} clip call(s)/s, {_clipFrames * 1000.0 / dt:0} carried, " +
                          $"{_wraps} wrap(s), {_turns} turn(s), {_reseek} re-seek(s), " +
                          $"{_backward} backward step(s)");
        _probeAt = now;
        Array.Clear(_drawnBy);
        _drawn = _clipCalls2 = _carried = _snaps = _clipFrames = _wraps = _turns = _reseek = _backward = 0;
    }

    // ---- sampling helpers ------------------------------------------------------

    static void Prime(int i, int x, int y, int z, short p, short w, short r)
    {
        _px[i] = _cx[i] = x; _py[i] = _cy[i] = y; _pz[i] = _cz[i] = z;
        _pp[i] = _cp[i] = p; _pw[i] = _cw[i] = w; _pr[i] = _cr[i] = r;
        _primed[i] = true;
    }

    static bool Jump(int i) =>
        Math.Abs((long)_cx[i] - _px[i]) > SnapUnits ||
        Math.Abs((long)_cy[i] - _py[i]) > SnapUnits ||
        Math.Abs((long)_cz[i] - _pz[i]) > SnapUnits ||
        Math.Abs(Wrap(_cp[i] - _pp[i])) > SnapAngle ||
        Math.Abs(Wrap(_cw[i] - _pw[i])) > SnapAngle ||
        Math.Abs(Wrap(_cr[i] - _pr[i])) > SnapAngle;

    static int Index(int table, int slot)
    {
        if ((uint)table >= 4u || (uint)slot >= (uint)TableCount[table]) return -1;
        return TableBase[table] + slot;
    }

    // ---- Mode.Timeline ---------------------------------------------------------

    static void Predict(ClipState s)
    {
        int step = s.CurTime - s.PrevTime;
        if (step == 0) { s.Say = Verdict.Still; return; }

        int d = s.Length;
        if (d <= 0) { s.Say = Verdict.Hold; return; }

        double best;
        Fold path = Fold.Circle;
        bool wrapped = false, mirrored = false;

        if (!s.HasRate)
        {
            best = Nearest(step, d, 0.0);
            if (!s.FirstStep || !Trusted(best, d))
            {
                // No settled rate to check against: make the step wait a tick and
                // confirm it by repetition rather than carry it on trust.
                Seed(s, best);
                s.FirstStep = false;
                s.Say = Verdict.Hold;
                return;
            }
        }
        else
        {
            double rate = s.Rate;
            double tol = Window(rate, d);
            best = Nearest(step, d, rate);
            if (Math.Abs(best - rate) > tol)
            {
                // Only now is a turn at an endpoint worth considering, and only
                // if the clip would genuinely have run off that end.
                double overshoot = s.PrevTime + rate;
                double turn =
                    rate > 0 && overshoot > d ? 2.0 * d - s.CurTime - s.PrevTime :
                    rate < 0 && overshoot < 0 ? -s.CurTime - s.PrevTime :
                    double.NaN;
                if (double.IsNaN(turn) || Math.Abs(turn - rate) > tol)
                {
                    // Not playback: a re-seek, or a clip the game is fighting
                    // over. Re-seed from what was seen and hold this tick.
                    Seed(s, Nearest(step, d, 0.0));
                    _reseek++;
                    s.Say = Verdict.Hold;
                    return;
                }
                best = turn;
                path = Fold.Mirror;
            }
            mirrored = path == Fold.Mirror;
            wrapped = !mirrored && (s.PrevTime + best < 0 || s.PrevTime + best >= d);
        }

        s.FirstStep = false;
        s.Delta = best;
        s.Path = path;
        s.Rate = mirrored ? -best : best;
        s.HasRate = true;
        s.Say = best == 0.0 ? Verdict.Still : Verdict.Play;

        if (wrapped) _wraps++;
        if (mirrored) _turns++;
    }

    static bool Trusted(double delta, int d) => Math.Abs(delta) <= d * FirstStepFrac;

    static double Window(double rate, int d) =>
        Math.Max(RateTolAbs, Math.Min(Math.Abs(rate) * RateTolRel, d * RateTolCap));

    static void Seed(ClipState s, double delta)
    {
        s.Rate = delta;
        s.HasRate = true;
    }

    static double Nearest(int step, int d, double rate)
    {
        double k = Math.Round((rate - step) / d);
        return step + k * d;
    }

    static double Circle(double t, int d)
    {
        double r = t % d;
        return r < 0 ? r + d : r;
    }

    static double Mirror(double t, int d)
    {
        double r = Circle(t, 2 * d);
        return r <= d ? r : 2.0 * d - r;
    }

    /// <summary>The clip's total length, summed off the same segment table the
    /// clock walks. 0 for anything that does not read as a clip record.</summary>
    static int Duration(IMemory m, uint bank, uint clip)
    {
        if (_lengths.TryGetValue((bank, clip), out int cached)) return cached;

        int total = 0;
        if (Ram(bank))
        {
            uint table = bank + m.ReadU32(bank + 0x10u);
            if (Ram(table) && Ram(table + clip * 4u))
            {
                uint rec = bank + m.ReadU32(table + clip * 4u);
                if (Ram(rec))
                {
                    int count = (int)m.ReadU16(rec);
                    if (count > 0 && count <= MaxSegments && Ram(rec + 4u + (uint)count * 4u))
                    {
                        for (int i = 0; i < count; i++)
                        {
                            uint seg = bank + m.ReadU32(rec + 4u + (uint)i * 4u);
                            if (!Ram(seg)) { total = 0; break; }
                            total += m.ReadU16(seg + 2u);
                        }
                    }
                }
            }
        }

        _lengths[(bank, clip)] = total;
        return total;
    }

    static bool Ram(uint addr) => addr >= 0x80010000u && addr < 0x80200000u;

    static void Reset(ClipState s)
    {
        s.PrevTime = s.CurTime = 0;
        s.HasCur = s.HasPrev = false;
        s.Length = 0;
        s.Rate = 0; s.HasRate = false;
        s.FirstStep = true;
        s.Delta = 0;
        s.Path = Fold.Circle;
        s.Say = Verdict.Still;
        s.HasDrawn = false;
        s.LastDraw = 0;
        s.LastDrawTick = -1;
    }

    // ---- the root lerp, copied from ViewSmoothing ------------------------------

    static int Wrap(int d) => ((d + 0x800) & 0xFFF) - 0x800;

    static int Mix(int a, int b, double t) => (int)(a + Math.Round(((long)b - a) * t));

    /// <summary>The short way round at 12 bits, kept in 0..0xFFF when both ends are.</summary>
    static short Turn(short a, short b, double t)
    {
        int v = a + (int)Math.Round(Wrap(b - a) * t);
        if ((uint)a < 0x1000u && (uint)b < 0x1000u) v &= 0xFFF;
        return (short)v;
    }
}
