using RecompOne.Runtime;
using RecompOne.Runtime.Events;

namespace Kf3;

/// <summary>
/// KF3_CROSSPROBE=1: every presented picture across an area load, read back
/// (<see cref="PresentSnap"/>) and reduced to numbers, so a dark or stalled frame at a
/// crossing is named by a counter rather than by eye. Each present's luminance (the
/// world's rows, a sampled grid), its time since the last present, whether the
/// retained world drew it, whether the retained map was ready, the tick and the
/// camera; the 400 ms before an <c>fdat</c> load and the 3 s after it are printed when
/// the window closes. Reading back every present stalls the GPU, so frame times are
/// the probe's as much as the game's; lower the render scale (shell <c>scale 1</c>)
/// to keep it small.
/// </summary>
public static class CrossProbe
{
    record struct Row(double T, double Dt, int Lum, int Centre, long GpuFrames, long Retained, bool Ready, bool Main,
        long Tick, bool Ticked, int CamX, int CamZ, string Overlay, bool Tinted, long Submitted, char Same, int DispY, long Draws, long Tris, int Prims);

    static bool _on;
    static readonly Queue<Row> _before = new();
    static readonly List<Row> _after = new();
    static readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
    static double _last = -1, _loadAt = -1;
    static string _loaded = "";
    static long _gpuFrames, _retained, _submitted;
    static ulong _print1, _print2;
    static long _draws, _tris; static int _prims;
    static bool _tinted;

    public static void Install()
    {
        var v = Environment.GetEnvironmentVariable("KF3_CROSSPROBE");
        if (string.IsNullOrWhiteSpace(v) || v == "0") return;
        _on = true;
        Event.AddListener<OverlayLoadedEvent>(e =>
        {
            if (!e.Name.StartsWith("fdat", StringComparison.Ordinal)) return;
            if (_loadAt >= 0) Flush();
            _loadAt = _clock.Elapsed.TotalMilliseconds; _loaded = e.Name;
            Console.WriteLine($"[KF3] crossprobe: {e.Name} loaded at {_loadAt:0} ms, tick {FramePacing.Ticks}");
            Console.Out.Flush();
        });
        Event.AddListener<RenderPrimEvent>(e =>
        {
            _prims++;
            if (_tinted || !e.SemiTransparent || e.Gouraud) return;
            int lo = int.MaxValue, hi = int.MinValue;
            for (int i = 0; i < e.Count; i++) { lo = Math.Min(lo, e.X[i]); hi = Math.Max(hi, e.X[i]); }
            if (lo <= e.DrawLeft + 2 && hi >= e.DrawRight - 1) _tinted = true;
        });
        PresentSnap.Request(Taken);
        Console.WriteLine("[KF3] crossprobe: on");
    }

    static void Taken(byte[] rgba, int w, int h, int dispX, int dispY)
    {
        double now = _clock.Elapsed.TotalMilliseconds;
        // A grid of samples over the middle two thirds of the rows (the gauges and
        // the compass sit at the edges), and the centre ninth on its own.
        long sum = 0, centre = 0; int n = 0, nc = 0; ulong print = 14695981039346656037;
        for (int gy = 0; gy < 24; gy++)
            for (int gx = 0; gx < 32; gx++)
            {
                int y = h / 6 + gy * (h * 2 / 3) / 24, x = gx * w / 32 + w / 64;
                int p = (y * w + x) * 4;
                int l = (rgba[p] * 3 + rgba[p + 1] * 6 + rgba[p + 2]) / 10;
                sum += l; n++; print = (print ^ (uint)(rgba[p] | rgba[p + 1] << 8 | rgba[p + 2] << 16)) * 1099511628211;
                if (gx >= 11 && gx < 21 && gy >= 8 && gy < 16) { centre += l; nc++; }
            }
        var cam = RecompOne.Runtime.Runtime.Mem is { } m ? Camera.Read(m) : default;
        var row = new Row(now, _last < 0 ? 0 : now - _last, (int)(sum / n), (int)(centre / nc),
            GpuWorld.Frames - _gpuFrames, GpuWorld.Retained - _retained, RetainedMap.Ready, RetainedScene.MainView,
            FramePacing.Ticks, FramePacing.IterationTicked, cam.X, cam.Z, AgentBeacon.Overlay, _tinted,
            GpuWorld.Submissions - _submitted, print == _print1 ? '=' : print == _print2 ? '2' : '.',
            dispY, RetainedScene.MainDraws - _draws, RetainedScene.MainTriangles - _tris, _prims);
        _draws = RetainedScene.MainDraws; _tris = RetainedScene.MainTriangles; _prims = 0;
        _gpuFrames = GpuWorld.Frames; _retained = GpuWorld.Retained; _submitted = GpuWorld.Submissions; _tinted = false;
        _print2 = _print1; _print1 = print;
        _last = now;
        if (_loadAt < 0)
        {
            _before.Enqueue(row);
            while (_before.Count > 0 && now - _before.Peek().T > 400) _before.Dequeue();
        }
        else
        {
            _after.Add(row);
            if (now - _loadAt > 3000) Flush();
        }
        PresentSnap.Request(Taken);
    }

    static void Flush()
    {
        Console.WriteLine($"[KF3] crossprobe: {_loaded}, {_before.Count} present(s) before, {_after.Count} after");
        Console.WriteLine("[KF3] crossprobe:     t ms    dt   lum  ctr px  gpu submit retained ready main  tick t tint  y draws   tris prims overlay  camX camZ");
        foreach (var r in _before.Concat(_after))
            Console.WriteLine($"[KF3] crossprobe: {r.T - _loadAt,7:0.0} {r.Dt,5:0.0} {r.Lum,5} {r.Centre,4} {r.Same,2} {r.GpuFrames,4} {r.Submitted,6} {r.Retained,8} " +
                $"{(r.Ready ? 1 : 0),5} {(r.Main ? 1 : 0),4} {r.Tick,5} {(r.Ticked ? 1 : 0)} {(r.Tinted ? 1 : 0),4} {r.DispY,3} {r.Draws,4} {r.Tris,7} {r.Prims,5} {r.Overlay,-8} {r.CamX} {r.CamZ}");
        int dark = _after.Count(r => r.Lum < 4), slow = _after.Count(r => r.Dt > 30);
        Console.WriteLine($"[KF3] crossprobe: {_loaded}: {dark} present(s) under luminance 4, {slow} over 30 ms, " +
            $"longest {(_after.Count > 0 ? _after.Max(r => r.Dt) : 0):0.0} ms");
        Console.Out.Flush();
        _before.Clear(); _after.Clear(); _loadAt = -1;
    }
}
