using System.Globalization;
using RecompOne.Runtime;

namespace Kf3;

/// <summary>
/// Murky water, Verdite2's <c>Murk</c>: water darkens with the distance the view ray
/// runs through it to the floor, so a shallow edge stays clear and a deep pool goes dark.
///
///     KF3_MURK=1              on (Video ▸ World enhancements ▸ Murky water; off by default)
///     KF3_MURK_DISTANCE=1886  the distance through water that takes 63% of the way to the murk
///     KF3_MURK_TILT=0.75      the cosine a murked surface may lean to (0 murks any)
///
/// Composited by the runtime's reflection pass (<c>WaterMurk</c>), which runs for it on
/// its own: no reflection needs to be on. The water is the retained map's faces flagged
/// as water (<see cref="WaterRects"/>). The colour and distance are Verdite2's, judged
/// there. See "Murky water" in docs/WATER.md.
/// </summary>
public static class Murk
{
    public const float DefaultDistance = 1886f;

    public static void Configure(string? distance, string? tilt)
    {
        var ci = CultureInfo.InvariantCulture;
        WaterMurk.Distance = float.TryParse(distance, NumberStyles.Float, ci, out float d) && d > 0f ? d : DefaultDistance;
        WaterMurk.R = 0.03f; WaterMurk.G = 0.05f; WaterMurk.B = 0.06f;
        if (float.TryParse(tilt, NumberStyles.Float, ci, out float t)) WaterMurk.MaxTilt = Math.Clamp(t, 0f, 1f);
    }

    public static bool Enabled => WaterMurk.Enabled;

    public static void SetEnabled(bool on) => WaterMurk.Enabled = on;

    /// <summary>From GpuWorld.Begin: the world's vertical in the frame's view space,
    /// column 1 of the view matrix (world Y is down), for the murk's tilt test.</summary>
    public static void Frame(in RetainedScene.View v)
    {
        float x = v.R01, y = v.R11, z = v.R21, len = MathF.Sqrt(x * x + y * y + z * z);
        if (len < 1e-3f) return;
        WaterMurk.UpX = x / len; WaterMurk.UpY = y / len; WaterMurk.UpZ = z / len;
    }

    /// <summary>The <c>murk</c> shell verb: on, off, <c>tilt X</c> or <c>distance X</c>, unsaved.</summary>
    public static string Shell(string[] parts)
    {
        var ci = CultureInfo.InvariantCulture;
        if (parts.Length == 1 && parts[0] is "on" or "off") SetEnabled(parts[0] == "on");
        else if (parts.Length == 2 && parts[0] == "tilt" && float.TryParse(parts[1], NumberStyles.Float, ci, out float t))
            WaterMurk.MaxTilt = Math.Clamp(t, 0f, 1f);
        else if (parts.Length == 2 && parts[0] == "distance" && float.TryParse(parts[1], NumberStyles.Float, ci, out float d) && d > 0f)
            WaterMurk.Distance = d;
        else if (parts.Length != 0) return AgentServer.Err("murk [on|off|tilt X|distance X]");
        return string.Create(ci, $"{{\"ok\":true,\"cmd\":\"murk\",\"on\":{(WaterMurk.Enabled ? "true" : "false")}," +
                                 $"\"tilt\":{WaterMurk.MaxTilt},\"distance\":{WaterMurk.Distance}}}");
    }
}
