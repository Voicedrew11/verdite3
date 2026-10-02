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
