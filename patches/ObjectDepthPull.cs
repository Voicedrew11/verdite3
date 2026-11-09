using RecompOne.Runtime;

namespace Kf3;

/// <summary>
/// Objects sunk into the map: each model the walk submits may sink into the map by up
/// to its own size and still draw, as the ordering table drew it whole at one slot.
/// The retained renderer tests it against the map pulled that far towards the camera
/// and against other models at its true depth (runtime 0111).
///
///     KF3_OBJECT_PULL=0         off, to compare. A number is a scale on the size;
///                               unset is 1. Otherwise it follows the Z-buffer.
///     KF3_OBJECT_PULL_MAX=N     no model's allowance past N units of depth; 960 by
///                               default, the console's own (0: no ceiling)
///     KF3_OBJECT_PULL_PROBE=1   a line every 4 s: models given one, and their sizes
///
/// See "Objects sunk into the map" in docs/PICTURE.md.
/// </summary>
public static class ObjectDepthPull
{
    /// <summary>Live: on exactly while the Z-buffer is, since without the depth test
    /// nothing cuts a sunk object off.</summary>
    public static bool Enabled => _scale > 0f && ZBuffer.Enabled;

    /// <summary>The allowance as a share of the model's size (its furthest vertex from
    /// its own origin); 0 is off.</summary>
    static float _scale = 1f;

    /// <summary>The ordering table linked a map tile 0xF0 slots (of four units of depth)
    /// behind its mean, so a model's face won against the map within this much.</summary>
    const float TableAllowance = 0xF0 * 4;
    static float _max = TableAllowance;

    public static bool ProbeOn { get; set; }
    static long _models, _pulled;
    static readonly List<float> _sizes = [];
    static long _reportAt;

    public static void Configure(string? on, string? max, string? probe)
    {
        _scale = string.IsNullOrWhiteSpace(on) ? 1f : ParseFloat(on) ?? 0f;
        _max = ParseFloat(max) ?? TableAllowance;
        ProbeOn = probe?.Trim() == "1";
    }

    static float? ParseFloat(string? s) =>
        float.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture,
            out float f) && float.IsFinite(f) && f >= 0f ? f : null;

    /// <summary>The allowance for a model placed by <paramref name="m"/>, in world units:
    /// its vertices' furthest reach from the origin, scaled by the matrix's longest
    /// column. <paramref name="pose"/> is one past its first texel in
    /// <see cref="RetainedScene.PoseStore"/>; a morph's texels alternate keyframe and
    /// delta, and the keyframe is measured.</summary>
    public static float For(in RetainedScene.ModelInstance m, int pose, int count, bool morph)
    {
        if (!Enabled && !ProbeOn) return 0f;
        int stride = morph ? 2 : 1, first = pose - 1;
        var store = RetainedScene.PoseStore;
        if (first < 0 || (first + (long)count * stride) * 4 > store.Length) return 0f;
        long reach2 = 0;
        for (int i = 0; i < count; i++)
        {
            int at = (first + i * stride) * 4;
            long x = store[at], y = store[at + 1], z = store[at + 2];
            reach2 = Math.Max(reach2, x * x + y * y + z * z);
        }
        float column = MathF.Max(Length(m.R00, m.R10, m.R20), MathF.Max(Length(m.R01, m.R11, m.R21), Length(m.R02, m.R12, m.R22)));
        float size = MathF.Sqrt(reach2) * column;
        float pull = size * _scale;
        if (_max > 0f) pull = MathF.Min(pull, _max);
        if (ProbeOn) Note(size);
        return Enabled ? pull : 0f;
    }

    static float Length(float x, float y, float z) => MathF.Sqrt(x * x + y * y + z * z);

    static void Note(float size)
    {
        _models++;
        if (size > 0f) _pulled++;
        if (_sizes.Count < 100_000) _sizes.Add(size);
        long now = Environment.TickCount64;
        if (now < _reportAt) return;
        _reportAt = now + 4000;
        if (_sizes.Count == 0) return;
        _sizes.Sort();
        float At(double q) => _sizes[Math.Min(_sizes.Count - 1, (int)(q * _sizes.Count))];
        Console.WriteLine($"[KF3] object pull: {(Enabled ? "on" : "off")}, {_models} models, {_pulled} with a size; " +
                          $"size p10 {At(0.1):F0}, p50 {At(0.5):F0}, p90 {At(0.9):F0}, max {_sizes[^1]:F0} units" +
                          $"{(_max > 0f ? $", capped at {_max:F0}" : "")}, scale {_scale:0.##}");
        _sizes.Clear();
        _models = _pulled = 0;
    }
}
