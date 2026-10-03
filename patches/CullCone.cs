using System.Reflection;
using RecompOne.Runtime.Context;
using RecompOne.Runtime.Memory;
using RecompOne.Runtime.Modding;

namespace Kf3;

/// <summary>
/// Widens the visibility grid's view cone to the widescreen aspect.
///
/// The grid is 25x25 bytes at scratchpad 0x1F800120, built every frame by
/// func_80034BF4 from camera pitch (0x801AEC5C) and yaw (0x801AEC5E). The
/// trapezoid is an inlined per-cell classifier: each cell gets 0x1E, 0x1A, 0x08
/// or 0, from a squared-distance band and two trig half-plane tests. Only the
/// occlusion flood (func_800345F4 / func_800348F4) is a separate call, so the
/// cone is complete and unshadowed exactly there. A pre-hook on both floods
/// re-runs the classifier -- stock S5 inside func_80034BF4's own frame is at the
/// caller's SP -- and ORs the cells the stock cone left 0 that a wider cone
/// lights.
///
/// S5 is the lateral half-angle in 4096-per-turn units; the draw radius T5 is
/// kept from the stock S5. The widening angle is atan(Factor*tan(S5)), so the
/// rasterised cone matches the widened picture's half-width, and Factor is 1 at
/// 4:3, where the hook returns at once.
///
///     KF3_WIDESCREEN_CULL=0       leave the cone at its 4:3 shape
///     KF3_WIDESCREEN_CULL=1.5     force a widening factor instead of the aspect's
///     KF3_WIDESCREEN_CULL_PROBE=1 the cone's shape and the oracle mismatch count
///     KF3_WIDESCREEN_CULL_PROBE=2 also print the last grid as ASCII once a window
///
/// The oracle re-runs the classifier at the stock S5 and counts cells where it
/// disagrees with the game's own grid (the epilogue's five cells excluded); it
/// must be 0, and is how the C# transcription is known to be exact.
/// </summary>
public static class CullCone
{
    // func_80034BF4's grid, in the scratchpad: 25x25 bytes, stride 25.
    const uint Grid = 0x1F800120;
    const int Span = 25;
    const int Cells = Span * Span;

    // The camera block func_80034BF4 reads. pitch is a u16, yaw an s16, d the
    // u8 the draw radius is squared from (func_80034BF4:146, V0-0x1517).
    const uint PitchAddr = 0x801AEC5C;
    const uint YawAddr = 0x801AEC5E;
    const uint RadiusAddr = 0x801AEAE9;
    /// <summary>u8: 0x11 skips the epilogue's second cross (the cells behind the eye cleared).</summary>
    const uint BehindFlag = 0x801B25E5;

    // The two occlusion floods; func_80034BF4 calls exactly one of them.
    const uint FloodA = 0x800345F4;
    const uint FloodB = 0x800348F4;

    // The game's trig, called on a separate context so the live registers are
    // untouched: rsin is sine, func_80076DA0 is its cosine.
    const uint SinAddr = 0x80076CC4;
    const uint CosAddr = 0x80076DA0;

    /// <summary>Widen with the aspect. On unless KF3_WIDESCREEN_CULL=0; a number
    /// pins the factor. At 4:3 the computed factor is 1 and nothing is added.</summary>
    public static bool Enabled { get; private set; } = true;

    /// <summary>The factor in force, 1 when the cone is the game's own.</summary>
    public static float Factor { get; private set; } = 1f;

    // KF3_WIDESCREEN_CULL: "0"/"off" switches off, "1"/"on" follows the aspect,
    // a positive number pins the factor.
    static bool? _forced;
    static float? _forcedFactor;

    // KF3_WIDESCREEN_CULL_PROBE.
    static bool _measure;
    static bool _map;

    // The stock and widened classifiers, rebuilt per frame. 0 when no widening.
    static readonly int[] _stock = new int[Cells];
    static readonly int[] _wide = new int[Cells];
    static readonly bool[] _epilogue = new bool[Cells];
    static (int Row, int Col) _behind;
    static readonly byte[] _last = new byte[Cells];

    // Trig delegates, bound on the game overlay load.
    static Action<CpuContext, IMemory>? _sin, _cos;
    static readonly CpuContext _trigCtx = new();

    // Last seen inputs for the report.
    static int _s5, _s5w = -1;

    // Per window.
    static double _windowStart;
    static long _frames, _lit, _added, _oracle;
    static double Now => Environment.TickCount64 / 1000.0;

    static readonly ModInfo _self = new()
    {
        Id = "kf3.cullcone",
        Name = "Cull cone",
        Version = "1.0",
        Description = "Opens the game's own tile-visibility cone to the widescreen aspect.",
    };

    public static void Configure(string? widen, string? probe)
    {
        if (!string.IsNullOrWhiteSpace(widen))
        {
            if (widen.Equals("0", StringComparison.Ordinal) ||
                widen.Equals("off", StringComparison.OrdinalIgnoreCase))
                _forced = false;
            else if (widen.Equals("1", StringComparison.Ordinal) ||
                     widen.Equals("on", StringComparison.OrdinalIgnoreCase))
                _forced = true;
            else if (float.TryParse(widen, System.Globalization.NumberStyles.Float,
                                    System.Globalization.CultureInfo.InvariantCulture, out float f) && f > 0f)
            {
                _forced = true;
                _forcedFactor = Math.Clamp(f, 1f, 4f);
            }
        }

        if (!string.IsNullOrWhiteSpace(probe) && !probe.Equals("0", StringComparison.Ordinal))
        {
            _measure = true;
            _map = probe.Equals("2", StringComparison.Ordinal);
        }

        _windowStart = Now;
    }

    /// <summary>Attach on the game overlay load, like the other patches.</summary>
    public static void Install()
    {
        HookAttach.OnOverlayLoad("cull cone", Attach);
    }

    static bool Attach()
    {
        SymbolRegistry.Build();
        var floodA = SymbolRegistry.Resolve("game", null, FloodA);
        var floodB = SymbolRegistry.Resolve("game", null, FloodB);
        var sin = SymbolRegistry.Resolve("game", "rsin", 0);
        var cos = SymbolRegistry.Resolve("game", null, CosAddr);
        if (floodA == null || floodB == null || sin == null || cos == null)
        {
            Console.Error.WriteLine("[KF3] cull cone: a routine is not mapped; the cone stays 4:3");
            return false;
        }

        _sin = sin.CreateDelegate<Action<CpuContext, IMemory>>();
        _cos = cos.CreateDelegate<Action<CpuContext, IMemory>>();

        var impl = typeof(CullCone).GetMethod(nameof(Before),
            BindingFlags.Public | BindingFlags.Static)!;
        int n = 0;
        if (HookManager.AddPre(_self, floodA, impl)) n++;
        if (HookManager.AddPre(_self, floodB, impl)) n++;
        if (n < 2)
        {
            Console.Error.WriteLine("[KF3] cull cone: a flood did not hook; the cone stays 4:3");
            return false;
        }

        HookManager.Commit();
        if (!HookAttach.Installed(floodA) || !HookAttach.Installed(floodB))
        {
            Console.Error.WriteLine("[KF3] cull cone: the flood pair did not install; the cone stays 4:3");
            return false;
        }

        Console.WriteLine($"[KF3] cull cone: {(Enabled ? "follows the aspect" : "off")}");
        return true;
    }

    /// <summary>
    /// Pre-hook on either flood: the stock cone has just been built into the grid
    /// and the flood has not run. Recompute it in C# -- inputs from guest memory,
    /// the wide angle from <see cref="Factor"/> -- verify it against the game's
    /// grid, and add the cells the widening lights.
    /// </summary>
    public static void Before(CpuContext c, IMemory m)
    {
        if (_sin == null || _cos == null) return;

        // The game's trig runs on its own stack below this frame; the live one is
        // untouched (the flood has not pushed its frame yet).
        _stackBase = c.SP - 0x400u;
        _mem = m;
        Apply();

        // At the stock angle with nothing asked of the probe, the hook is a no-op.
        if (Factor <= 1f && !_measure) return;

        int pitch = m.ReadU16(PitchAddr) & 0xFFF;
        int yaw = (short)m.ReadU16(YawAddr);
        int d = m.ReadU8(RadiusAddr);
        int s7 = yaw + 0x400;

        int s5 = StockAngle(pitch);
        int s5w = s5;
        if (Factor > 1f && s5 < 1024) s5w = WidenAngle(s5, Factor);

        var centre = Rebuild(m, d, s7, s5, _stock);
        if (s5w != s5) Rebuild(m, d, s7, s5w, _wide);
        else Array.Copy(_stock, _wide, Cells);

        MarkEpilogue(centre, m.ReadU8(BehindFlag) != 0x11);
        _s5 = s5;
        _s5w = s5w;

        // The oracle: the stock classifier against the game's own grid, the
        // epilogue's five cells excluded (it always overrides theirs).
        if (_measure)
        {
            long mismatch = 0;
            for (int i = 0; i < Cells; i++)
            {
                if (_epilogue[i]) continue;
                if ((byte)_stock[i] != m.ReadU8(Grid + (uint)i)) mismatch++;
            }
            _oracle += mismatch;
        }

        int lit = 0, added = 0;
        for (int i = 0; i < Cells; i++)
        {
            byte cur = m.ReadU8(Grid + (uint)i);
            if ((cur & 0x02) != 0) lit++;
            if (Factor <= 1f || _epilogue[i] || cur != 0) { _last[i] = cur; continue; }
            int w = _wide[i];
            if (w == 0x1E || w == 0x1A)
            {
                m.WriteU8(Grid + (uint)i, (byte)w);
                cur = (byte)w;
                added++;
            }
            _last[i] = cur;
        }

        _frames++;
        _lit += lit;
        _added += added;
        Report();
    }

    /// <summary>Recompute <see cref="Factor"/> from the switch and the aspect, the
    /// way the picture's own margin is computed: 1 at 4:3, wider with the margin.</summary>
    static void Apply()
    {
        Enabled = _forced ?? true;
        if (!Enabled) { Factor = 1f; return; }

        // The same ratio the picture widens by, asked about this game's 320-pixel
        // screen, so the cone and the render target cannot disagree.
        if (_forcedFactor is { } pinned) Factor = pinned;
        else
        {
            int margin = RecompOne.Runtime.Hle.Display.WideMargin(320);
            Factor = (320f + 2f * margin) / 320f;
        }
    }

    // ---- the build, transcribed from func_80034BF4 ---------------------------

    /// <summary>func_80034BF4:5-50 -- fold the pitch to a quarter turn and scale
    /// it into the lateral half-angle, 440..584 of 4096 units.</summary>
    static int StockAngle(int p)
    {
        int q = p;
        if (q >= 2049) q = 0x1000 - q;
        if (q >= 1025) q = 0x800 - q;
        int v = q << 3;
        v = unchecked(v + q);
        v = v << 3;
        v = unchecked(v + q);
        v = v << 3;
        if (v < 0) v = unchecked(v + 0x3FF);
        v = v >> 10;
        return unchecked(v + 0x1B8);
    }

    /// <summary>The angle whose tangent is <paramref name="factor"/> times the
    /// tangent of <paramref name="s5"/>, in 4096-per-turn units, rounded.</summary>
    static int WidenAngle(int s5, float factor)
    {
        double theta = s5 * (2.0 * Math.PI / 4096.0);
        double wide = Math.Atan(factor * Math.Tan(theta)) * (4096.0 / (2.0 * Math.PI));
        return (int)Math.Round(wide, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// The classifier (func_80034BF4:51-252) as pure integer arithmetic: the two
    /// edge rays at yaw+(0x400 +/- s5), the window middle, the half-plane basis
    /// and the squared-distance band. T5 is always computed from the stock angle.
    /// Returns the window middle (centreX, centreZ) the epilogue uses.
    /// </summary>
    static (int X, int Z) Rebuild(IMemory m, int d, int s7, int s5, int[] grid)
    {
        int cP = Trig(_cos!, s7), sP = Trig(_sin!, s7);
        int cL = Trig(_cos!, s7 - s5), sL = Trig(_sin!, s7 - s5);
        int cR = Trig(_cos!, s7 + s5), sR = Trig(_sin!, s7 + s5);

        const int C7FF = 0xC7FF;
        int t2 = (C7FF - 11 * cP) >> 12;
        int t1 = (C7FF - 11 * sP) >> 12;
        int amp = -11 * sP;

        int s0a = unchecked((16 * sL + amp + C7FF) << 4) >> 16;
        int t0v = (short)t1;
        int s6 = s0a - t0v;
        int a3v = (short)t2;
        int s1a = (unchecked((16 * cL + (-11 * cP) + C7FF) << 4) >> 16) - a3v;
        int s0b = (unchecked((16 * sR + amp + C7FF) << 4) >> 16) - t0v;
        int s2b = (unchecked((16 * cR + (-11 * cP) + C7FF) << 4) >> 16) - a3v;
        int s3f = (-s0b) * a3v + s2b * t0v;
        int fp = (-s6) * a3v + s1a * t0v;

        // The frame stores the window middle as two u16; the loop sign-extends
        // them (func_80034BF4:128,131,134,135,170-176).
        int centreZ = (short)(((uint)(C7FF - (cP << 3))) >> 12);
        int centreX = (short)(((uint)(C7FF - (sP << 3))) >> 12);

        int stockS5 = StockAngle(m.ReadU16(PitchAddr) & 0xFFF);
        int t5;
        if (stockS5 < 600) t5 = d * d;
        else t5 = (d * d * Trig(_cos!, stockS5 >> 1)) >> 12;

        int col = 0, row = 0;
        int t0r = 0, a3r = 0, t2r = 0, t1r = 0;
        int count = Cells;
        int idx = 0;
        while (true)
        {
            if (col >= Span)
            {
                t0r = 0; a3r = 0; col = 0;
                t2r = unchecked(t2r + s2b);
                t1r = unchecked(t1r + s1a);
                row++;
            }
            int dy = col - centreZ;
            int v1 = unchecked(dy * dy);
            int dx = row - centreX;
            v1 = unchecked(v1 + dx * dx);
            int a0 = (a3r - t1r) + fp;
            int a1 = (t0r - t2r) + s3f;

            int val;
            if ((uint)v1 < 5u) val = (a0 <= 0 && a1 >= 0) ? 0x1E : 0;
            else if ((uint)v1 < (uint)t5) val = (a0 <= 0 && a1 >= 0) ? 0x1A : 0;
            else if ((uint)v1 < 0x100u) val = 0x08;
            else val = 0;
            grid[idx++] = val;

            t0r = unchecked(t0r + s0b);
            a3r = unchecked(a3r + s6);
            if (--count == 0) break;
            col++;
        }

        _behind = ((short)t1, (short)t2);
        return (centreX, centreZ);
    }

    /// <summary>func_80034BF4:254-306 -- the five-cell cross around the window
    /// middle (func_80034BF4:265-272), always written; the widening must leave it
    /// alone.</summary>
    static void MarkEpilogue((int X, int Z) centre, bool behind)
    {
        Array.Clear(_epilogue);
        Cross(Span * centre.X + centre.Z);
        // The second cross, cleared to 0 unless the flag is 0x11 (func_80034BF4:283-306).
        if (behind) Cross(Span * _behind.Row + _behind.Col);
    }

    static void Cross(int middle)
    {
        Mark(middle - Span);
        Mark(middle - 1);
        Mark(middle);
        Mark(middle + 1);
        Mark(middle + Span);
    }

    static void Mark(int i)
    {
        if ((uint)i < Cells) _epilogue[i] = true;
    }

    static int Trig(Action<CpuContext, IMemory> fn, int a)
    {
        _trigCtx.SP = _stackBase;
        _trigCtx.A0 = (uint)a;
        fn(_trigCtx, _mem!);
        return (int)_trigCtx.V0;
    }

    static uint _stackBase;
    static IMemory? _mem;

    static void Report()
    {
        if (!_measure) return;
        double now = Now;
        if (now - _windowStart < 2.0) return;

        double frames = Math.Max(_frames, 1);
        Console.WriteLine($"[KF3] cullcone: x{Factor:0.###}, S5 {_s5} -> {_s5w}, " +
                          $"lit {_lit / frames:0.0} of {Cells}, added {_added / frames:0.0}, " +
                          $"oracle {_oracle} mismatch(es)");

        if (_map) PrintMap();

        _windowStart = now;
        _frames = _lit = _added = _oracle = 0;
    }

    static void PrintMap()
    {
        Console.WriteLine("[KF3] cullcone grid (. 0, # stock cone, + added, o 0x08):");
        for (int r = 0; r < Span; r++)
        {
            var chars = new char[Span];
            for (int x = 0; x < Span; x++)
            {
                int i = r * Span + x;
                byte b = _last[i];
                char ch = b switch
                {
                    0 => '.',
                    0x1E or 0x1A => '#',
                    0x08 => 'o',
                    _ => '?',
                };
                // A cell lit only by the widened classifier is '+' (its value is
                // the stock 0x1E/0x1A the widening wrote).
                if (b == _wide[i] && _stock[i] == 0 && !_epilogue[i] && (b == 0x1E || b == 0x1A))
                    ch = '+';
                chars[x] = ch;
            }
            Console.WriteLine("  " + new string(chars));
        }
    }
}
