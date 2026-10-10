using RecompOne.Runtime.Memory;
using Recompiled;

// Entry point for the King's Field II (SLUS-00255) port: King's Field III in the
// Japanese numbering. Hand-owned, so RecompOne does not generate one into
// generated/. Init and hooks go here, before Entry.Run.

Verdite.Core.Game.Configure(tag: "KF3");
Verdite.Core.Kept.BoolsAsInts = true;

// What a crash, a hook's fault or a hang leaves in crashes/: the runtime's report
// with this port's version, place and switches in it. KF3_HANG=seconds without a
// frame before a hang report (15; 0 turns it off); KF3_FAULT=hook|crash|hang[:s]
// makes one on purpose. See "When it crashes" in docs/DEVELOPMENT.md.
Verdite.Core.CrashReports.Configure(Verdite.Core.Game.Env("HANG"), "Verdite3", Kf3.AgentBeacon.Snapshot, Kf3.AgentBeacon.FirstStage);
Verdite.Core.CrashReports.InstallFault(Verdite.Core.Game.Env("FAULT"));

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

// The keyboard layout the port ships, as the default bindings rather than an
// override: Configure must run before ConfigManager.Load so a fresh install gets
// it and a later launch keeps the player's file. See "The keyboard layout" in
// docs/INPUT.md.
Kf3.KeyLayout.Configure();
Kf3.KeyLayout.Install();

// The top menu bar starts hidden whatever the last session saved; F1 shows it.
// Once, at the first ready: a hard reset raises the event again.
bool topBarHidden = false;
RecompOne.Runtime.Events.Event.AddListener<RecompOne.Runtime.Events.RuntimeReadyEvent>(_ =>
{
    if (topBarHidden) return;
    topBarHidden = true;
    RecompOne.Runtime.Config.ConfigManager.View.HideTopBar = true;
    Console.WriteLine("[KF3] top bar: hidden at startup (F1 shows it)");
});

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
// The ending: straight into END.EXE for a diagnostic, and its last frame held with
// a button back to the title instead of a spin no frame leaves. See "The ending"
// in docs/GAME_INTERNALS.md.
Kf3.BootExe.Configure(Environment.GetEnvironmentVariable("KF3_BOOTEXE"));
Kf3.BootExe.Install();
Kf3.EndingHold.Configure(Environment.GetEnvironmentVariable("KF3_ENDINGHOLD"),
                         Environment.GetEnvironmentVariable("KF3_ENDINGEXIT"));
Kf3.EndingHold.Install();

// A full card: both executables' card check fails when it cannot create a
// scratch file, which five saves guarantee, so the title leaves Continue off and
// the in-game Save offers to format the card. KF3_FULLCARD=0 compares. See
// "Saves and the start menu" in docs/GAME_INTERNALS.md.
Kf3.FullCard.Configure(Environment.GetEnvironmentVariable("KF3_FULLCARD"));
Kf3.FullCard.Install();

// Reload the last save on death, through the in-game menu's own Load. A setting
// under Gameplay; the variables win over it:
//     KF3_AUTORELOAD=1          on (the default); 0 leaves the death alone
//     KF3_AUTORELOAD_DELAY=2.5  seconds of the death sequence first
//     KF3_AUTORELOAD_SLOT=0     0 = the game's last used slot, 1..5 pins one
// See "Death and auto reload" in docs/GAME_INTERNALS.md.
Kf3.AutoReload.Configure(Environment.GetEnvironmentVariable("KF3_AUTORELOAD"),
                         Environment.GetEnvironmentVariable("KF3_AUTORELOAD_DELAY"),
                         Environment.GetEnvironmentVariable("KF3_AUTORELOAD_SLOT"));
Kf3.AutoReload.Install();

// Verdite2's GearCompare: every stat an equip or a purchase would change, beside
// the equipment and shop lists, drawn with the game's menu routines. On by default;
// Gameplay ▸ Compare gear. KF3_GEARCOMPARE=0 off, =probe a line for each item
// compared. See "Comparing gear" in docs/GAME_INTERNALS.md.
Kf3.GearCompare.Configure(Environment.GetEnvironmentVariable("KF3_GEARCOMPARE"));
Kf3.GearCompare.Install();

// M, or the touchpad of a DualShock 4 or DualSense, opens the map of the area the
// player is in when its map item is held: PIXY'S MAP, else MAP OF VERDITE in the
// areas it covers. KF3_MAPKEY=0 off, KF3_MAPKEY_TEST=sec opens it once that long
// into the first area. See "The maps" in docs/GAME_INTERNALS.md.
Kf3.MapKey.Configure(Environment.GetEnvironmentVariable("KF3_MAPKEY"),
                     Environment.GetEnvironmentVariable("KF3_MAPKEY_TEST"));
Kf3.MapKey.Install();
Kf3.StageProbe.Install();
Kf3.GeometryProbe.Install();
Kf3.SceneCensus.Install();
Kf3.SceneDriver.Install();
// The primitive buffer's per-frame use, a measurement only. See
// "The primitive buffer" in docs/GAME_INTERNALS.md.
Kf3.PrimBufferProbe.Configure(Environment.GetEnvironmentVariable("KF3_PRIMBUF_PROBE"));
Kf3.PrimBufferProbe.Install();

// Frame pacing: on at 144 fps unless KF3_FPS says otherwise (KF3_FPS=off is uncapped;
// Settings ▸ Testing turns it off). See "Frame pacing" in docs/DEVELOPMENT.md.
Kf3.FramePacing.Configure(Environment.GetEnvironmentVariable("KF3_FPS"),
                          Environment.GetEnvironmentVariable("KF3_TICKRATE"),
                          Environment.GetEnvironmentVariable("KF3_FPS_PROBE"));
Kf3.FramePacing.Install();
Kf3.RateCensus.Install();
// Whether the full-screen tints are drawn between ticks: "The tints between
// ticks" in docs/SMOOTHING.md.
Kf3.TintProbe.Install();
// Every presented picture across an area load, as numbers: "Crossing between areas" in docs/DEVELOPMENT.md.
Kf3.CrossProbe.Install();

// The frame profiler (Verdite2's): where each frame's time goes, by section. Every
// hooked function is timed inside HookManager (its recompiled body and each patch's
// delegate apart), the runtime times the present path, and the pacers' sleeps are
// sections of their own so a capped frame reads as work plus waiting. Shift+P opens
// the panel, and recording runs while it is open. See "Profiling a frame" in
// docs/DEVELOPMENT.md.
//
//     KF3_PROFILE=1             record from boot, a console summary every 5 s
//     KF3_PROFILE=panel         record from boot and open the panel
//     KF3_PROFILE_OUT=path.csv  every frame's sections; scripts/profile_report.py
//     KF3_PROFILE_SPIKE=12      a console line per frame over 12 ms of work
//     KF3_PROFILE_FUNCS=stages  time all fifteen main-loop stages, or name
//                               functions: game:80030FCC+8003BFD0
Verdite.Core.FrameProfiler.Configure(Environment.GetEnvironmentVariable("KF3_PROFILE"),
                                     Environment.GetEnvironmentVariable("KF3_PROFILE_OUT"),
                                     Environment.GetEnvironmentVariable("KF3_PROFILE_SPIKE"),
                                     Environment.GetEnvironmentVariable("KF3_PROFILE_FUNCS"),
                                     Kf3.ProfilerKnown.Table);
Verdite.Core.FrameProfiler.Install();
Verdite.Core.ProfilerPanel.Configure(() => Kf3.FramePacing.Enabled && !Kf3.FramePacing.Uncapped ? 1000.0 / Kf3.FramePacing.TargetFps : 0,
                                     defaultProbe: "game:8003BFD0", stages: 15);

// VSync calls outside stage 15 wait a real vblank, as the console's did. On by
// default; KF3_VBLANKPACING=0 compares against the runtime's clock. See "Menus and
// loading screens wait for a vblank" in docs/DEVELOPMENT.md.
Kf3.VBlankPacing.Configure(Environment.GetEnvironmentVariable("KF3_VBLANKPACING"),
                           Environment.GetEnvironmentVariable("KF3_VBLANKPACING_PROBE"));
Kf3.VBlankPacing.Install();

// The bulk polygon assemblers in C#: on unless KF3_POLYASM=0; KF3_POLYASM=verify
// runs both and compares. See "The geometry path in C#" in docs/GEOMETRY.md.
Kf3.PolyAssembler.Configure(Environment.GetEnvironmentVariable("KF3_POLYASM"),
                            Environment.GetEnvironmentVariable("KF3_POLYASM_MAP"),
                            Environment.GetEnvironmentVariable("KF3_POLYASM_LIT"),
                            Environment.GetEnvironmentVariable("KF3_POLYASM_HUD"));
Kf3.PolyAssembler.Install();
Kf3.PolyAssembler.InstallDepth();

// Stage 15 and its camera block in C#: on unless =0; KF3_STAGE15=verify and
// KF3_CAMERABLOCK=verify compare them with the recompiled routines. The view carried between ticks under
// pacing (KF3_SMOOTH=0 to compare), and the billboard clock held to the tick. See docs/SMOOTHING.md.
Kf3.CameraBlock.Configure(Environment.GetEnvironmentVariable("KF3_CAMERABLOCK"));
Kf3.CameraBlock.Install();
Kf3.Stage15.Configure(Environment.GetEnvironmentVariable("KF3_STAGE15"),
                      Environment.GetEnvironmentVariable("KF3_STAGE15_NEEDLE"));
Kf3.Stage15.Install();
// Loops entered from a gated stage that call stage 15 themselves run once per
// world tick, the picture drawn at the render rate. See "Loops that draw their
// own frames" in docs/SMOOTHING.md.
Kf3.LoopPacing.Configure(Environment.GetEnvironmentVariable("KF3_LOOPPACING"),
                         Environment.GetEnvironmentVariable("KF3_LOOPPACING_PROBE"));
Kf3.LoopPacing.Install();
Kf3.ViewSmoothing.Configure(Environment.GetEnvironmentVariable("KF3_SMOOTH"),
                            Environment.GetEnvironmentVariable("KF3_SMOOTH_PROBE"));
Kf3.ViewSmoothing.Install();
Kf3.MessageBoxHold.Configure(Environment.GetEnvironmentVariable("KF3_MSGBOX"));
Kf3.MessageBoxHold.Install();
Kf3.SpriteAnim.Configure(Environment.GetEnvironmentVariable("KF3_SPRITEANIM"));
Kf3.SpriteAnim.Install();
Kf3.TextureScroll.Configure(Environment.GetEnvironmentVariable("KF3_TEXSCROLL"));
Kf3.TextureScroll.Install();

// The model walk func_80040AE4 in C#, verified 2026-10-02;
// KF3_MODELWALK=verify compares it with the recompiled routine. Installed after
// SpriteAnim so its pre/post pair on the same routine is registered first.
Kf3.ModelWalk.Configure(Environment.GetEnvironmentVariable("KF3_MODELWALK"));
Kf3.ModelWalk.Install();
Kf3.GpuWorld.Install();
Kf3.NativeScene.Install();
Kf3.SceneFeatures.Install();
Kf3.CullCone.Configure(Environment.GetEnvironmentVariable("KF3_WIDESCREEN_CULL"),
                       Environment.GetEnvironmentVariable("KF3_WIDESCREEN_CULL_PROBE"));
Kf3.CullCone.Install();
Kf3.RenderDistance.Configure();
// The water, Verdite2's: which faces are water, the murk, the waves and the planar
// mirror, all on the retained renderer; the three switches are SceneFeatures'. See
// docs/WATER.md.
Kf3.WaterRects.Install();
Kf3.Murk.Configure(Environment.GetEnvironmentVariable("KF3_MURK_DISTANCE"),
                   Environment.GetEnvironmentVariable("KF3_MURK_TILT"));
Kf3.Waves.Configure(Environment.GetEnvironmentVariable("KF3_WAVES_PROBE"));
Kf3.Waves.Install();
Kf3.PlanarMirror.Configure(Environment.GetEnvironmentVariable("KF3_PLANAR_TOLERANCE"),
                           Environment.GetEnvironmentVariable("KF3_PLANAR_RIPPLE"),
                           Environment.GetEnvironmentVariable("KF3_PLANAR_BIAS"),
                           Environment.GetEnvironmentVariable("KF3_PLANAR_FOG"),
                           Environment.GetEnvironmentVariable("KF3_PLANAR_PROBE"));
Kf3.PlanarMirror.Install();
// The near divisions' screen block widened to the aspect. See patches/NearScreen.cs.
Kf3.NearScreen.Configure(Environment.GetEnvironmentVariable("KF3_NEARSCREEN"),
                         Environment.GetEnvironmentVariable("KF3_NEARSCREEN_PROBE"));
Kf3.NearScreen.Install();
// The near path (func_8003AB04, func_800366A8 and libgte's division) in C#;
// KF3_NEARPATH=verify compares it. See "Unit 4" in docs/PICTURE.md.
Kf3.NearPath.Configure(Environment.GetEnvironmentVariable("KF3_NEARPATH"));
Kf3.NearPath.Install();
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
Kf3.MapCoverage.Configure(Environment.GetEnvironmentVariable("KF3_MAPCOVERAGE"));
Kf3.MapCoverage.Install();

// Widescreen: off (4:3) until judged, like the rest of the picture. The runtime
// renders the margin; this sets the aspect, clears the margin latch on an
// executable load, stretches the game's full-screen tints and, when chosen, moves
// the HUD out to the new edges.
Kf3.Widescreen.Configure(Environment.GetEnvironmentVariable("KF3_WIDESCREEN"),
                         Environment.GetEnvironmentVariable("KF3_WIDESCREEN_PROBE"),
                         Environment.GetEnvironmentVariable("KF3_WIDESCREEN_EFFECTS"),
                         Environment.GetEnvironmentVariable("KF3_WIDESCREEN_HUD"));
Kf3.Widescreen.Install();
// The world drawn live behind menus and full-screen messages (signs, dialogue)
// instead of the frozen 320-wide copy; KF3_MENUWORLD=0 compares. See "Menus and
// messages draw the world live" in docs/WIDESCREEN.md.
Kf3.MenuWorld.Configure(Environment.GetEnvironmentVariable("KF3_MENUWORLD"),
                        Environment.GetEnvironmentVariable("KF3_MENUWORLD_PROBE"),
                        Environment.GetEnvironmentVariable("KF3_MENUWORLD_TEST"));
Kf3.MenuWorld.Install();
var pp = Environment.GetEnvironmentVariable("KF3_PRESENT_PROBE");
if (pp == "1" || pp == "2") RecompOne.Runtime.Hle.GpuHle.PresentProbe = true;

// Mouse look, spent through the game's own turn and look routine (MouseLook), and
// the mouse buttons pressed as pad buttons inside PAD_dr. Escape captures and
// releases; the settings are under Input. "Instant mouse look" (KF3_MOUSE_LEAD)
// shows the motion before the tick spends it, in ViewSmoothing. See "Mouse look"
// in docs/INPUT.md.
Mouse.Configure(Kf3.MouseLook.Game);
Mouse.Install();
Kf3.MouseLook.Install();

// The menu pointer: point at the game's menus, left click confirms, right click
// backs out, the wheel pages a list (KF3_MENUMOUSE, on; KF3_MENUMOUSE_PROBE=1 logs
// what it saw). Verdite2's MenuMouse. See "The menu pointer" in docs/INPUT.md.
Kf3.MenuMouse.Configure();
Kf3.MenuMouse.Install();

// Analog twin-stick control: the sticks drive the game's own turn/look velocity
// through MouseLook's shared hook, and walking through a replace on func_8002F9BC.
// See "Analog twin-stick control" in docs/INPUT.md.
Kf3.Analog.Configure();
Kf3.Analog.Install();
// KF3_ANALOG_PROBE=1: what the sticks drove, every few seconds.
Kf3.AnalogProbe.Configure();
Kf3.AnalogProbe.Install();

// Turning a picked-up item while the game holds it up: the mouse, the right stick
// and the gyroscope (KF3_ITEMTURN, on; KF3_ITEMTURN_GYRO, off). Drawn by the model
// walk (ModelWalk.Carry). See "Turning a picked-up item" in docs/INPUT.md.
Kf3.ItemTurn.Configure();
Kf3.ItemTurn.Install();
Kf3.ItemEye.Configure(Environment.GetEnvironmentVariable("KF3_ITEMEYE"));
Kf3.ItemEye.Install();

// Aiming a drawn bow with the gyroscope (KF3_GYROAIM, off): spent through the look
// routine beside the right stick. See "Gyro aim with a drawn bow" in docs/INPUT.md.
GyroAim.Configure(Kf3.Bow.Reads);
GyroAim.Install();

// Rumble as a bow is drawn and loosed: two motors, or HD rumble on a Switch pad
// (KF3_RUMBLE, KF3_RUMBLE_HD, both on; runtime 0104). See "Rumble" in docs/INPUT.md.
Rumble.Configure(Kf3.Bow.Reads);
Rumble.Install();

// The Input pane, the port's in place of the runtime's: Keyboard (the layout and
// the bindings), Gamepad (the sticks and the bindings), Mouse. See "The Input pane
// is the port's" in docs/INPUT.md.
Kf3.InputSection.Install();

// The Gameplay tab: Instant mouse look, beside the runtime's own sections.
Kf3.GameplaySection.Install();

// The Testing tab in Settings: every switch above, live.
Kf3.TestingSection.Install();

// The port settings as a page of the game's own menu: PORT SETTINGS, under SYSTEM.
// KF3_SETTINGSPAGE_PROBE=1 logs what it did. See "The port settings page" in
// docs/GAME_INTERNALS.md.
Kf3.SettingsPage.Configure(Environment.GetEnvironmentVariable("KF3_SETTINGSPAGE_PROBE"));
Kf3.SettingsPage.Install();

// The port settings both screens draw from, and the one writer of interface.ini's
// kf3.* keys (docs/SETTINGS.md). Last, so its boot check follows every start-up.
Kf3.PortSettings.Install();

// Compile the recompiled code ahead of the game running it. QuickJit is off (a
// tier-up loses a MonoMod detour), so every function is compiled by the full JIT
// on its first call, on the game thread. This warms the lot on background
// threads from the first overlay load. Installed last, so the patches' own attach
// listeners have run before the first method is prepared. See "The stutters" in
// docs/DEVELOPMENT.md.
//
//     KF3_PREJIT=0        leave every method to its first call -- the comparison
//     KF3_PREJIT_PROBE=1  a line per batch as the pass reaches its end
//     KF3_PREJIT_THREADS=n  threads for the pass (a quarter of the cores, 1-4)
Verdite.Core.Prejit.Configure(Verdite.Core.Game.Env("PREJIT"),
                              Verdite.Core.Game.Env("PREJIT_PROBE"),
                              Verdite.Core.Game.Env("PREJIT_THREADS"));
Verdite.Core.Prejit.Install("Kf3");

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

// What the window calls itself to the desktop. On Wayland this is the whole of
// how a compositor finds the icon, and it has to be set before the window is
// made. See "The window icon" in docs/PACKAGING.md.
RecompOne.Runtime.Runtime.AppId = "verdite3";

// What a desktop entry written for this run calls the port (Verdite Core's
// DesktopEntry writes one only when no packager has).
DesktopEntry.Name = "Verdite3";
DesktopEntry.GenericName = "King's Field II";
DesktopEntry.Comment = "A PC port of King's Field II (SLUS-00255). Requires your own disc image.";

// The icon, everywhere: the fourth save slot's memory-card icon off the player's
// disc, which the release cannot carry, so the port ships no mark at all. Until a
// run has read it once there is none.
//
//     KF3_ICON=off      no icon at all
//     KF3_ICON=0|1|2    a frame of its three (2 by default)
Verdite.Core.CardIcon.Install(args.Length > 0 ? args[0] : null, 2, Kf3.CardIconSource.Read);

// This file calls Entry.Run itself, with no loop to boot the game again, so the
// runtime's Hard Reset (F1, System) would end the process: it explains instead.
RecompOne.Runtime.Runtime.CanHardReset = false;

Thread.CurrentThread.Name ??= "game";
var memory = new PSMemory();
// The game runs on this thread, so its crash comes out here: a report, and the
// window kept up with the report's path until the player closes it.
try
{
    Entry.Run(memory, args.Length > 0 ? args[0] : null);
}
catch (Exception e)
{
    Console.Error.WriteLine($"[KF3] the game has crashed: {e}");
    RecompOne.Runtime.Runtime.HoldAfterCrash(RecompOne.Runtime.Diagnostics.CrashReport.Write("crash", e));
    return 1;
}
return 0;
