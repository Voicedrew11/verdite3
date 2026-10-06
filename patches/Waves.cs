using System.Globalization;
using RecompOne.Runtime;
using RecompOne.Runtime.Events;

namespace Kf3;

/// <summary>
/// Water that moves, Verdite2's <c>Waves</c>: a slow swell that lifts and lowers the
/// water's own vertices (<see cref="WaterSwell"/>), and ripples drawn per pixel over it
/// (runtime 0078, <c>WaterWaves</c>), which push the texture and shade it by a moving
/// slope, so the one small image every water tile repeats stops reading as a grid.
///
///     KF3_WAVES=1          on (Video ▸ World enhancements ▸ Water waves; off by default)
///     KF3_WAVES_PROBE=1    a line every 5 s: the rect, the clock, rippled batches, the swell
///
/// The tuning is Verdite2's, judged there by eye, and the <c>waves</c> shell verb
/// (live, unsaved). Both run on one clock, the world's own: it advances with the ticks
/// and the fraction between them, so it stands still when the world does (a menu) and
/// stays smooth at any frame rate. Speed scales the clock's rate, not its value.
/// See "Waves" in docs/WATER.md.
/// </summary>
public static class Waves
{
    public const float DefaultSwell = 338f, DefaultSwellSize = 6114f;
    public const float DefaultRipple = 139f, DefaultRippleSize = 700f, DefaultShade = 0.51f, DefaultSpeed = 1f;

    static bool _probe;

    public static bool Enabled { get; private set; }

    /// <summary>The swell's height at its crest, in world units (a tile is 2048, a
    /// height step 128), and the length of its longest wave.</summary>
    public static float Swell = DefaultSwell, SwellSize = DefaultSwellSize;

    /// <summary>How fast both move; 1 as authored.</summary>
    public static float Speed = DefaultSpeed;

    /// <summary>The field's clock, seconds at the authored speed.</summary>
    public static double Time { get; private set; }

    public static void Configure(string? probe) => _probe = probe is not (null or "" or "0");

    public static void Install()
    {
        WaterWaves.Distort = DefaultRipple;
        WaterWaves.Scale = DefaultRippleSize;
        WaterWaves.Shade = DefaultShade;
        Event.AddListener<OverlayLoadedEvent>(_ => _lastWorld = -1.0);
    }

    public static void SetEnabled(bool on)
    {
        Enabled = on;
        WaterWaves.Enabled = on;
        WaterWaves.Generation++;
    }

    static long _seen = -1, _ticks;
    static double _lastWorld = -1.0;

    /// <summary>From GpuWorld.Begin, the frame begun: the clock, the camera the ripples
    /// take a fragment back to the world by, and the swell.</summary>
    public static void Frame(in RetainedScene.View v)
    {
        WaterWaves.Enabled = Enabled;
        // The swell lifts the water off the plane it is mirrored in by up to its
        // height, and the reflection pass takes a surface only that near it.
        PlanarReflections.Tolerance = PlanarMirror.BaseTolerance + (Enabled ? Swell : 0f);
        if (!Enabled) return;

        if (FramePacing.FirstWalkOfTick(ref _seen)) _ticks++;
        double world = (_ticks + (FramePacing.Enabled ? FramePacing.TickFraction : 0.0)) / Math.Max(1.0, FramePacing.LogicHz);
        if (_lastWorld >= 0.0 && world > _lastWorld) Time += (world - _lastWorld) * Speed;
        _lastWorld = world;

        var r = WaterWaves.R;
        r[0] = v.R00; r[1] = v.R01; r[2] = v.R02;
        r[3] = v.R10; r[4] = v.R11; r[5] = v.R12;
        r[6] = v.R20; r[7] = v.R21; r[8] = v.R22;
        WaterWaves.CamX = (float)v.CamX; WaterWaves.CamY = (float)v.CamY; WaterWaves.CamZ = (float)v.CamZ;
        WaterWaves.Tx = v.Tx; WaterWaves.Ty = v.Ty; WaterWaves.Tz = v.Tz;
        WaterWaves.Time = (float)(Time % 100000.0);
        WaterWaves.Generation++;
        WaterSwell.Publish();
        if (_probe) Report();
    }

    /// <summary>The <c>waves</c> verb: the state, the switch, or one setting, live and
    /// not saved.</summary>
    public static string Shell(string[] parts)
    {
        var ci = CultureInfo.InvariantCulture;
        if (parts.Length == 1 && parts[0] is "on" or "off") SetEnabled(parts[0] == "on");
        else if (parts.Length == 2 && float.TryParse(parts[1], NumberStyles.Float, ci, out float v))
        {
            switch (parts[0])
            {
                case "swell": Swell = Math.Clamp(v, 0f, 512f); break;
                case "swellsize": SwellSize = Math.Clamp(v, 2048f, 65536f); break;
                case "ripple": WaterWaves.Distort = Math.Clamp(v, 0f, 200f); break;
                case "ripplesize": WaterWaves.Scale = Math.Clamp(v, 100f, 4096f); break;
                case "shade": WaterWaves.Shade = Math.Clamp(v, 0f, 1f); break;
                case "speed": Speed = Math.Clamp(v, 0f, 4f); break;
                default: return AgentServer.Err($"waves: unknown setting '{parts[0]}'");
            }
            WaterWaves.Generation++;
        }
        else if (parts.Length != 0) return AgentServer.Err("waves [on|off|swell|swellsize|ripple|ripplesize|shade|speed <value>]");
        return string.Create(ci, $"{{\"ok\":true,\"cmd\":\"waves\",\"on\":{(Enabled ? "true" : "false")}," +
                                 $"\"swell\":{Swell},\"swellsize\":{SwellSize},\"ripple\":{WaterWaves.Distort}," +
                                 $"\"ripplesize\":{WaterWaves.Scale},\"shade\":{WaterWaves.Shade},\"speed\":{Speed}," +
                                 $"\"supported\":{(WaterWaves.Supported ? "true" : "false")},\"rects\":{WaterWaves.RectN}," +
                                 $"\"batches\":{WaterWaves.Batches},\"clock\":{Time:F2},\"free\":{WaterSwell.FreePositions}," +
                                 $"\"water\":{WaterSwell.WaterPositions},\"rim\":{WaterSwell.RimPositions},\"shared\":{WaterSwell.SharedPositions}}}");
    }

    static long _reportAt, _batchesAt;

    static void Report()
    {
        long now = Environment.TickCount64;
        if (now < _reportAt) return;
        double dt = _reportAt == 0 ? 5.0 : (now - _reportAt + 5000) / 1000.0;
        _reportAt = now + 5000;
        long batches = WaterWaves.Batches - _batchesAt;
        _batchesAt = WaterWaves.Batches;
        Console.WriteLine($"[KF3] waves: {WaterRects.Describe()}, clock {Time:F2}s, " +
                          $"ripples {(WaterWaves.Supported ? $"{batches / dt:F0} batches/s" : "NOT supported")}; {WaterSwell.Describe()}");
        Console.Out.Flush();
    }
}
