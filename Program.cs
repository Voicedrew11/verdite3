using RecompOne.Runtime.Memory;
using Recompiled;

// Entry point for the King's Field II (SLUS-00255) port: King's Field III in the
// Japanese numbering. Hand-owned, so RecompOne does not generate one into
// generated/. Init and hooks go here, before Entry.Run.

// The runtime's log channels, through an env var:
//     KF3_LOG=bios,cd,gpu,dma,sdk,spu,mdec,irq   (or KF3_LOG=all)
var channels = (Environment.GetEnvironmentVariable("KF3_LOG") ?? "")
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
    .Select(s => s.ToLowerInvariant())
    .ToHashSet();

if (channels.Count > 0)
{
    bool all = channels.Contains("all");
    RecompOne.Runtime.Log.BiosOn = all || channels.Contains("bios");
    RecompOne.Runtime.Log.CdOn = all || channels.Contains("cd");
    RecompOne.Runtime.Log.GpuOn = all || channels.Contains("gpu");
    RecompOne.Runtime.Log.DmaOn = all || channels.Contains("dma");
    RecompOne.Runtime.Log.SdkOn = all || channels.Contains("sdk");
    RecompOne.Runtime.Log.SpuOn = all || channels.Contains("spu");
    RecompOne.Runtime.Log.MdecOn = all || channels.Contains("mdec");
    RecompOne.Runtime.Log.IrqOn = all || channels.Contains("irq");
    Console.WriteLine($"[KF3] log channels: {string.Join(",", channels)}");
}

// libapi's interrupt-callback table, per executable: the table setIntr indexes by
// irq*4, intrEnv + 4. See "The interrupt-callback table" in docs/RECOMPILATION.md.
RecompOne.Runtime.Events.Event.AddListener<RecompOne.Runtime.Events.OverlayLoadedEvent>(e =>
{
    uint table = e.Name switch
    {
        "open" => 0x8003E948u,
        "game" => 0x8009AF9Cu,
        "end" => 0x80035A10u,
        _ => 0u,
    };
    if (table == 0) return;
    RecompOne.Runtime.Interrupts.CallbackTable = table;
    Console.WriteLine($"[KF3] irq callback table: {e.Name} 0x{table:X8}");
});

// The agent harness: a state beacon and a command channel. See "Driving the game
// without a person" in docs/DEVELOPMENT.md.
Kf3.AgentBeacon.Configure(Environment.GetEnvironmentVariable("KF3_AGENT"));
Kf3.AgentBeacon.Install();
Kf3.AgentServer.Configure(Environment.GetEnvironmentVariable("KF3_SHELL"));
Kf3.AgentServer.Install();
Kf3.AutoStart.Configure(Environment.GetEnvironmentVariable("KF3_AUTOSTART"));
Kf3.AutoStart.Install();
Kf3.StageProbe.Install();
Kf3.GeometryProbe.Install();

// Frame pacing: off unless KF3_FPS is set. See "Frame pacing" in docs/DEVELOPMENT.md.
Kf3.FramePacing.Configure(Environment.GetEnvironmentVariable("KF3_FPS"),
                          Environment.GetEnvironmentVariable("KF3_TICKRATE"),
                          Environment.GetEnvironmentVariable("KF3_FPS_PROBE"));
Kf3.FramePacing.Install();
Kf3.RateCensus.Install();

// The bulk polygon assemblers in C#: on unless KF3_POLYASM=0; KF3_POLYASM=verify
// runs both and compares. See "The geometry path in C#" in docs/GEOMETRY.md.
Kf3.PolyAssembler.Configure(Environment.GetEnvironmentVariable("KF3_POLYASM"),
                            Environment.GetEnvironmentVariable("KF3_POLYASM_MAP"),
                            Environment.GetEnvironmentVariable("KF3_POLYASM_LIT"));
Kf3.PolyAssembler.Install();

// Stage 15 and its camera block in C#: on unless =0; KF3_STAGE15=verify and
// KF3_CAMERABLOCK=verify compare them with the recompiled routines. The view carried between ticks under
// pacing (KF3_SMOOTH=0 to compare), and the billboard clock held to the tick. See docs/SMOOTHING.md.
Kf3.CameraBlock.Configure(Environment.GetEnvironmentVariable("KF3_CAMERABLOCK"));
Kf3.CameraBlock.Install();
Kf3.Stage15.Configure(Environment.GetEnvironmentVariable("KF3_STAGE15"),
                      Environment.GetEnvironmentVariable("KF3_STAGE15_NEEDLE"));
Kf3.Stage15.Install();
Kf3.ViewSmoothing.Configure(Environment.GetEnvironmentVariable("KF3_SMOOTH"),
                            Environment.GetEnvironmentVariable("KF3_SMOOTH_PROBE"));
Kf3.ViewSmoothing.Install();
Kf3.SpriteAnim.Configure(Environment.GetEnvironmentVariable("KF3_SPRITEANIM"));
Kf3.SpriteAnim.Install();
Kf3.TextureScroll.Configure(Environment.GetEnvironmentVariable("KF3_TEXSCROLL"));
Kf3.TextureScroll.Install();

// The model walk func_80040AE4 in C#, verified 2026-10-02;
// KF3_MODELWALK=verify compares it with the recompiled routine. Installed after
// SpriteAnim so its pre/post pair on the same routine is registered first.
Kf3.ModelWalk.Configure(Environment.GetEnvironmentVariable("KF3_MODELWALK"));
Kf3.ModelWalk.Install();
Kf3.MoPose.Configure(Environment.GetEnvironmentVariable("KF3_MOPOSE"));
Kf3.MoPose.Install();
Kf3.ModelSmoothing.Configure(Environment.GetEnvironmentVariable("KF3_SMOOTH_MODELS"),
    Environment.GetEnvironmentVariable("KF3_SMOOTH_PROBE"));
Kf3.ModelSmoothing.Install();

// The picture: 24-bit shading, no dither, perspective, sub-pixel and the Z-buffer, each off until judged. See docs/PICTURE.md.
Kf3.TrueColor.Configure(Environment.GetEnvironmentVariable("KF3_TRUECOLOR"));
Kf3.NoDither.Configure(Environment.GetEnvironmentVariable("KF3_NODITHER"),
                       Environment.GetEnvironmentVariable("KF3_NODITHER_PROBE"));
Kf3.NoDither.Install();
Kf3.Perspective.Configure(Environment.GetEnvironmentVariable("KF3_PERSPECTIVE"),
                          Environment.GetEnvironmentVariable("KF3_PERSPECTIVE_PROBE"));
Kf3.Perspective.Install();
Kf3.Subpixel.Configure(Environment.GetEnvironmentVariable("KF3_SUBPIXEL"),
                       Environment.GetEnvironmentVariable("KF3_SUBPIXEL_PROBE"),
                       Environment.GetEnvironmentVariable("KF3_SUBPIXEL_CULL"));
Kf3.Subpixel.Install();
Kf3.ZBuffer.Configure(Environment.GetEnvironmentVariable("KF3_ZBUFFER"),
                      Environment.GetEnvironmentVariable("KF3_ZBUFFER_PROBE"));
Kf3.ZBuffer.Install();

// The Testing tab in Settings: every switch above, live.
Kf3.TestingSection.Install();

// Scripted pad input, seconds:button:holdMs, timed from the first area module load
// (the one moment that means "in game"):
//     KF3_AUTOPAD=5:Start:1000,8:Circle:200
// Written through PAD_dr, the path that reaches the game's menus too.
var autopad = Environment.GetEnvironmentVariable("KF3_AUTOPAD");
if (!string.IsNullOrWhiteSpace(autopad))
{
    var press = new List<(double At, double Until, ushort Bit)>();
    foreach (var step in autopad.Split(',', StringSplitOptions.RemoveEmptyEntries))
    {
        var f = step.Split(':');
        if (f.Length != 3 || !Kf3.AgentServer.Buttons.TryGetValue(f[1].Trim(), out var bit))
            throw new ArgumentException($"KF3_AUTOPAD: bad step '{step}'");
        double at = double.Parse(f[0], System.Globalization.CultureInfo.InvariantCulture);
        double hold = double.Parse(f[2], System.Globalization.CultureInfo.InvariantCulture) / 1000.0;
        press.Add((at, at + hold, bit));
    }

    var clock = new System.Diagnostics.Stopwatch();
    RecompOne.Runtime.Events.Event.AddListener<RecompOne.Runtime.Events.OverlayLoadedEvent>(e =>
    {
        if (clock.IsRunning || !e.Name.StartsWith("fdat", StringComparison.Ordinal)) return;
        clock.Start();
        Console.WriteLine($"[KF3] autopad: {press.Count} step(s) armed");
    });

    ushort last = 0;
    RecompOne.Runtime.Events.Event.AddListener<RecompOne.Runtime.Events.PadReadEvent>(e =>
    {
        if (e.Port != 0 || !clock.IsRunning) return;
        double t = clock.Elapsed.TotalSeconds;
        ushort held = 0;
        foreach (var (at, until, bit) in press)
            if (t >= at && t < until) held |= bit;
        if (held != last)
        {
            Console.WriteLine($"[KF3] autopad t={t:F1}s held=0x{held:X4}");
            last = held;
        }
        if (held != 0) e.Buttons &= (ushort)~(ushort)((held >> 8) | (held << 8));
    });
}

RecompOne.Runtime.Runtime.AppId = "verdite3";

var memory = new PSMemory();
Entry.Run(memory, args.Length > 0 ? args[0] : null);
return 0;
