using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using RecompOne.Runtime;
using RecompOne.Runtime.Events;
using RecompOne.Runtime.Hardware;

namespace Kf3;

/// <summary>
/// The command channel: KF3_SHELL=1 listens on 127.0.0.1:27903 (KF3_SHELL=&lt;port&gt;
/// picks another). One request per line, one single-line JSON reply. Verdite2's
/// AgentServer, with this game's verbs. Commands run on the game thread, drained on
/// VSync. See "Driving the game without a person" in docs/DEVELOPMENT.md.
/// </summary>
public static class AgentServer
{
    public const int DefaultPort = 27903;

    public static int Port { get; private set; }

    const int MaxLineLength = 256;
    const int ReplyTimeoutMs = 5000;
    const int DefaultHoldMs = 150;
    const int QueueCap = 16;

    sealed record Cmd(string Name, string Arg1, string Arg2, TaskCompletionSource<string> Reply)
    {
        public string[] Args { get; init; } = [];
    }

    static readonly ConcurrentQueue<Cmd> _fast = new();

    static volatile ushort _pressBits;
    static long _pressUntil;

    /// <summary>Whether a <c>press</c> is being held, for a loop that reads the host
    /// pad rather than <c>PAD_dr</c> (<see cref="EndingHold"/>).</summary>
    public static bool Pressing => _pressBits != 0 && Environment.TickCount64 < _pressUntil;

    public static readonly Dictionary<string, ushort> Buttons = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Select"] = Controller.Select, ["Start"] = Controller.Start,
        ["Cross"] = Controller.Cross, ["Circle"] = Controller.Circle,
        ["Square"] = Controller.Square, ["Triangle"] = Controller.Triangle,
        ["L1"] = Controller.L1, ["R1"] = Controller.R1,
        ["L2"] = Controller.L2, ["R2"] = Controller.R2,
        ["L3"] = Controller.L3, ["R3"] = Controller.R3,
        ["Up"] = Controller.Up, ["Down"] = Controller.Down,
        ["Left"] = Controller.Left, ["Right"] = Controller.Right,
    };

    static readonly string[] HelpCommands =
    [
        "state - the player/area snapshot as JSON",
        "press <button> [holdMs=150] - press a pad button; one press at a time, replaced by the next",
        "peek <addr> [bytes=16] - read guest memory, hex",
        "poke <addr> <hex bytes> - write guest memory, up to 64 bytes (e.g. poke 8009C3F8 03000000)",
        "dump <file> - write the 2 MB of guest RAM to a file",
        "vram <file> - write the 1024x512 16-bit VRAM shadow to a file, row by row",
        "settings [open|save|discard|step <key> <-1|1>|reset <key>] - the game menu's settings session without its page; alone, every setting",
        "view [<x> <y> <z> <pitch> <yaw> <roll> | off] - the camera stage 15 drew with; with one, draw from it",
        "renderdist <tiles> [fadeTiles] - the retained render distance and its fade (0 the game's, none)",
        "aspect [4:3|16:9|16:10|21:9|<ratio>] - the widescreen aspect, or the current one",
        "scale [1..8] - the render scale, taken at the next present, unsaved; alone, the current one",
        "warp <area 0..27> - opt-in scene corpus driver; confirm loaded area with state",
        "scene-yaw <0..4095|off> - opt-in scene corpus driver; hold the guest view yaw the front submit reads, while physics is held",
        "gpu - the retained renderer's cumulative draw and model-mask counters",
        "murk [on|off|tilt X|distance X] - murky water, unsaved",
        "waves [on|off|swell|swellsize|ripple|ripplesize|shade|speed <value>] - water waves, unsaved",
        "planar [on|off] - planar reflections, unsaved, and the mirror's cumulative counters",
        "kill - kill the player through the game's death latch (tests auto reload)",
        "hurt <amount> - damage the player through the game's take-damage routine (the damage flash)",
        "point [<x> <y>|left|right|off] - the menu pointer at game pixels, a click, or the host's pointer again; alone, what it saw",
    ];

    public static void Configure(string? spec)
    {
        Port = 0;
        if (string.IsNullOrWhiteSpace(spec)) return;
        var s = spec.Trim().ToLowerInvariant();
        if (s is "0" or "off") return;
        if (s is "1" or "on" or "true" or "yes") { Port = DefaultPort; return; }
        if (!int.TryParse(s, out int port))
            throw new ArgumentException($"KF3_SHELL: cannot read '{spec}'");
        Port = Math.Clamp(port, 1, 65535);
    }

    public static void Install()
    {
        if (Port == 0) return;

        Event.AddListener<VSyncEvent>(_ => Drain(_fast));

        // PAD_dr's buffer is active-low with its two button bytes swapped against
        // Controller's layout, as in Verdite Core's Mouse.
        Event.AddListener<PadReadEvent>(e =>
        {
            if (e.Port != 0) return;
            ushort bits = _pressBits;
            if (bits != 0 && Environment.TickCount64 < _pressUntil)
                e.Buttons &= (ushort)~(ushort)((bits >> 8) | (bits << 8));
        });

        try
        {
            var listener = new TcpListener(IPAddress.Loopback, Port);
            listener.Start();
            new Thread(AcceptLoop) { IsBackground = true, Name = "kf3-agent-server" }.Start(listener);
            Console.WriteLine($"[KF3] agent server: listening on 127.0.0.1:{Port}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[KF3] agent server: could not listen on port {Port}: {ex.Message}");
        }
    }

    static void AcceptLoop(object? obj)
    {
        var listener = (TcpListener)obj!;
        while (true)
        {
            TcpClient client;
            try { client = listener.AcceptTcpClient(); }
            catch (SocketException) { continue; }
            catch (IOException) { continue; }
            catch (ObjectDisposedException) { return; }
            new Thread(() => Serve(client)) { IsBackground = true, Name = "kf3-agent-client" }.Start();
        }
    }

    static void Serve(TcpClient client)
    {
        try
        {
            using (client)
            {
                Stream stream = client.GetStream();
                using var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };

                // Decoded by hand so the length cap binds while the line arrives.
                var decoder = Encoding.UTF8.GetDecoder();
                var bytes = new byte[256];
                var chars = new char[256];
                var line = new char[MaxLineLength];
                int len = 0;
                bool over = false;

                while (stream.Read(bytes, 0, bytes.Length) is int got && got > 0)
                {
                    int consumed = 0;
                    while (consumed < got)
                    {
                        decoder.Convert(bytes, consumed, got - consumed, chars, 0, chars.Length, false,
                                        out int bUsed, out int cUsed, out _);
                        consumed += bUsed;
                        for (int i = 0; i < cUsed; i++)
                        {
                            char ch = chars[i];
                            if (ch is '\n' or '\r')
                            {
                                if (over) writer.WriteLine(Err("line too long (256 max)"));
                                else if (new string(line, 0, len).Trim() is { Length: > 0 } text) writer.WriteLine(Route(text));
                                len = 0;
                                over = false;
                            }
                            else if (!over)
                            {
                                if (len == MaxLineLength) over = true;
                                else line[len++] = ch;
                            }
                        }
                    }
                }
            }
        }
        catch
        {
            // The client went away; the game carries on.
        }
    }

    static string Route(string line)
    {
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var cmd = new Cmd(parts[0].ToLowerInvariant(),
                          parts.Length > 1 ? parts[1] : "",
                          parts.Length > 2 ? parts[2] : "",
                          new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously))
        { Args = parts[1..] };

        switch (cmd.Name)
        {
            case "state" or "press" or "help" or "peek" or "dump" or "vram" or "settings" or "view" or "aspect" or "warp" or "scene-yaw" or "gpu" or "kill" or "hurt" or "poke"
                or "renderdist" or "murk" or "waves" or "planar" or "scale" or "point":
                Enqueue(_fast, cmd);
                break;
            default:
                return Err($"unknown command '{cmd.Name}'; try help");
        }

        if (!cmd.Reply.Task.Wait(ReplyTimeoutMs))
            return Err("timed out after 5s");
        return cmd.Reply.Task.Result;
    }

    static void Drain(ConcurrentQueue<Cmd> queue)
    {
        while (queue.TryDequeue(out var cmd))
        {
            string reply;
            try { reply = Execute(cmd); }
            catch (Exception ex) { reply = "{\"ok\":false,\"cmd\":" + Q(cmd.Name) + ",\"error\":" + Q(ex.Message) + "}"; }
            cmd.Reply.TrySetResult(reply);
        }
    }

    static void Enqueue(ConcurrentQueue<Cmd> queue, Cmd cmd)
    {
        if (queue.Count >= QueueCap)
        {
            cmd.Reply.TrySetResult(Err($"too many queued '{cmd.Name}' commands ({QueueCap} max)"));
            return;
        }
        queue.Enqueue(cmd);
    }

    static string Execute(Cmd cmd) => cmd.Name switch
    {
        "state" => "{\"ok\":true,\"cmd\":\"state\",\"state\":" + AgentBeacon.Snapshot() + "}",
        "press" => DoPress(cmd.Arg1, cmd.Arg2),
        "help" => "{\"ok\":true,\"cmd\":\"help\",\"commands\":[" + string.Join(',', HelpCommands.Select(Q)) + "]}",
        "peek" => DoPeek(cmd.Arg1, cmd.Arg2),
        "dump" => DoDump(cmd.Arg1),
        "vram" => DoVram(cmd.Arg1),
        "settings" => DoSettings(cmd.Args),
        "view" => DoView(cmd.Args),
        "aspect" => Widescreen.Shell(cmd.Arg1),
        "kill" => "{\"ok\":true,\"cmd\":\"kill\",\"status\":" + Q(AutoReload.Simulate()) + "}",
        "hurt" => DoHurt(cmd.Arg1),
        "poke" => DoPoke(cmd.Arg1, cmd.Arg2),
        "warp" => SceneDriver.Warp(cmd.Arg1),
        "renderdist" => RenderDistance.Shell(cmd.Arg1, cmd.Arg2),
        "murk" => Murk.Shell(cmd.Args),
        "waves" => Waves.Shell(cmd.Args),
        "planar" => DoPlanar(cmd.Arg1),
        "scale" => DoScale(cmd.Arg1),
        "point" => MenuMouse.Shell(cmd.Args),
        "scene-yaw" => SceneDriver.Yaw(cmd.Arg1, RecompOne.Runtime.Runtime.Mem),
        "gpu" => "{\"ok\":true,\"cmd\":\"gpu\",\"mode\":" + GpuWorld.Mode + ",\"blocker\":" + Q(GpuWorld.Blocker ?? "none") +
                 ",\"mainDraws\":" + RetainedScene.MainDraws + ",\"mainMissed\":" + RetainedScene.MainMissed +
                 ",\"underTriangles\":" + RetainedScene.UnderTriangles +
                 ",\"clipFaces\":" + RetainedModels.ClipFaces + ",\"clipped\":" + RetainedModels.Clipped +
                 ",\"clipBillboardFaces\":" + RetainedModels.ClipBillboardFaces +
                 ",\"clipBillboardClipped\":" + RetainedModels.ClipBillboardClipped +
                 ",\"underSamples\":" + RetainedScene.UnderSamples + ",\"underShown\":" + RetainedScene.UnderShown +
                 ",\"instances\":" + RetainedScene.InstancesDrawn + ",\"maskOn\":" + (RetainedScene.ModelMask ? "true" : "false") +
                 ",\"maskFrames\":" + RetainedScene.MaskFrames + ",\"maskBatches\":" + RetainedScene.MaskBatches +
                 ",\"maskSamples\":" + RetainedScene.MaskSamples + ",\"maskBehind\":" + RetainedScene.MaskBehind +
                 ",\"maskAhead\":" + RetainedScene.MaskAhead + ",\"modelSamples\":" + RetainedScene.ModelSamples +
                 ",\"modelUnderMap\":" + RetainedScene.ModelUnderMap +
                 ",\"modelUnderSlack\":[" + string.Join(',', RetainedScene.ModelUnderSlack) + "]" +
                 ",\"toleranceSamples\":[" + string.Join(',', RetainedScene.ToleranceSamples) + "]" +
                 ",\"toleranceBehind\":[" + string.Join(',', RetainedScene.ToleranceBehind) + "]" +
                 "," + RenderDistance.Counters() + "}",
        _ => Err($"unknown command '{cmd.Name}'; try help"),
    };

    // The planar mirror's switch, unsaved, and its cumulative counters.
    static string DoPlanar(string arg)
    {
        if (arg is "on" or "off") PlanarMirror.SetEnabled(arg == "on");
        else if (arg.Length > 0) return Err("planar [on|off]");
        // The next reflection pass reads its map back (with KF3_PLANAR_PROBE=1).
        ScreenReflections.WantMap = true;
        return "{\"ok\":true,\"cmd\":\"planar\",\"rects\":" + Q(WaterRects.Describe()) + "," + PlanarMirror.Counters() + "}";
    }

    static string DoScale(string arg)
    {
        if (arg.Length > 0)
        {
            if (!int.TryParse(arg, out int s) || s < 1 || s > 8) return Err("scale [1..8]");
            RecompOne.Runtime.Hle.GlVram.Requested = s;
        }
        return "{\"ok\":true,\"cmd\":\"scale\",\"scale\":" + RecompOne.Runtime.Hle.GlVram.Scale + ",\"requested\":" + RecompOne.Runtime.Hle.GlVram.Requested +
               ",\"max\":" + RecompOne.Runtime.Hle.GlVram.MaxScale + "}";
    }

    static string DoPress(string name, string msArg)
    {
        if (!Buttons.TryGetValue(name, out ushort bits))
            return Err($"no such button '{name}'");
        long hold = DefaultHoldMs;
        if (msArg.Length > 0 && (!long.TryParse(msArg, out hold) || hold < 1 || hold > 5000))
            return Err("holdMs must be 1..5000");
        _pressBits = bits;
        _pressUntil = Environment.TickCount64 + hold;
        return "{\"ok\":true,\"cmd\":\"press\",\"button\":" + Q(name) + ",\"holdMs\":" + hold + "}";
    }

    // The take-damage routine func_8002A6F4(sourcePos, amount, flags), called as
    // "Damage and death" in docs/GAME_INTERNALS.md measured it: a null source, no
    // flags. HP falls, the hurt countdown and the knockback are written, and HP 0
    // calls the death latch, all as a blow would.
    static string DoHurt(string amountArg)
    {
        if (!int.TryParse(amountArg, out int amount) || amount <= 0 || amount > 0xFFFF)
            return Err("usage: hurt <amount 1..65535>");
        var cpu = RecompOne.Runtime.Runtime.Cpu;
        var mem = RecompOne.Runtime.Runtime.Mem;
        if (cpu == null || mem == null) return Err("not running");
        if (mem.ReadU16(0x801B24FAu) == 0) return Err("no area running; load a save first");

        uint before = mem.ReadU16(0x801B24FCu);
        var saved = cpu.Snapshot();
        cpu.SP -= 0x20u;
        cpu.A0 = 0;
        cpu.A1 = (uint)amount;
        cpu.A2 = 0;
        Recompiled.KingsField3_game.func_8002A6F4(cpu, mem);
        cpu.Restore(saved);
        uint after = mem.ReadU16(0x801B24FCu);
        return "{\"ok\":true,\"cmd\":\"hurt\",\"hpBefore\":" + before + ",\"hpAfter\":" + after + "}";
    }

    static string DoPeek(string addrArg, string lenArg)
    {
        var m = RecompOne.Runtime.Runtime.Mem;
        if (m == null) return Err("not running");
        if (!uint.TryParse(addrArg.Replace("0x", ""), System.Globalization.NumberStyles.HexNumber, null, out uint addr))
            return Err("peek <hex addr> [bytes]");
        int n = 16;
        if (lenArg.Length > 0 && (!int.TryParse(lenArg, out n) || n < 1 || n > 256))
            return Err("bytes must be 1..256");
        var sb = new StringBuilder();
        for (int i = 0; i < n; i++) sb.Append(m.ReadU8(addr + (uint)i).ToString("x2"));
        return "{\"ok\":true,\"cmd\":\"peek\",\"addr\":" + Q($"0x{addr:X8}") + ",\"hex\":" + Q(sb.ToString()) + "}";
    }

    static string DoPoke(string addrArg, string hexArg)
    {
        var m = RecompOne.Runtime.Runtime.Mem;
        if (m == null) return Err("not running");
        if (!uint.TryParse(addrArg.Replace("0x", ""), System.Globalization.NumberStyles.HexNumber, null, out uint addr)
            || hexArg.Length == 0 || hexArg.Length % 2 != 0 || hexArg.Length > 128)
            return Err("poke <hex addr> <hex bytes, 1..64>");
        byte[] bytes;
        try { bytes = Convert.FromHexString(hexArg); }
        catch (FormatException) { return Err("poke <hex addr> <hex bytes, 1..64>"); }
        for (int i = 0; i < bytes.Length; i++) m.WriteU8(addr + (uint)i, bytes[i]);
        return "{\"ok\":true,\"cmd\":\"poke\",\"addr\":" + Q($"0x{addr:X8}") + ",\"bytes\":" + bytes.Length + "}";
    }

    static string DoDump(string path)
    {
        if (RecompOne.Runtime.Runtime.Mem is not RecompOne.Runtime.Memory.PSMemory m) return Err("not running");
        if (path.Length == 0) return Err("dump <file>");
        File.WriteAllBytes(path, m.Ram[..0x200000].ToArray());
        return "{\"ok\":true,\"cmd\":\"dump\",\"file\":" + Q(Path.GetFullPath(path)) + "}";
    }

    static string DoVram(string path)
    {
        if (RecompOne.Runtime.Runtime.Gpu is not { } gpu) return Err("not running");
        if (path.Length == 0) return Err("vram <file>");
        File.WriteAllBytes(path, System.Runtime.InteropServices.MemoryMarshal.AsBytes(gpu.Vram.AsSpan()).ToArray());
        return "{\"ok\":true,\"cmd\":\"vram\",\"file\":" + Q(Path.GetFullPath(path)) + "}";
    }

    /// <summary>A <see cref="SettingsSession"/> driven from the game thread, as the menu
    /// page drives it, so the rules in docs/SETTINGS.md can be checked against the file.</summary>
    static string DoSettings(string[] args)
    {
        string verb = args.Length > 0 ? args[0] : "list";
        var session = SettingsSession.Current;
        string? why = null;
        switch (verb)
        {
            case "list": break;
            case "open": SettingsSession.Open(); break;
            case "save" or "discard" or "step" or "reset" when session is null: return Err("no session; settings open");
            case "save": session!.Save(); break;
            case "discard": session!.Discard(); break;
            case "step" or "reset":
                if (args.Length < 2 || PortSettings.Find(args[1]) is not { } s) return Err($"settings {verb} <key>: no such key");
                if (verb == "reset") why = session!.Reset(s);
                else if (args.Length < 3 || !int.TryParse(args[2], out int dir) || dir == 0) return Err("settings step <key> <-1|1>");
                else why = session!.Step(s, dir);
                break;
            default: return Err($"settings: unknown '{verb}'");
        }
        session = SettingsSession.Current;
        var rows = PortSettings.Listed.Select(s =>
            "{\"key\":" + Q(s.Key) + ",\"page\":" + Q(s.Page ?? "") +
            ",\"live\":" + s.Live().ToString(System.Globalization.CultureInfo.InvariantCulture) +
            ",\"shown\":" + Q(session is null ? "" : s.MenuValue(session.Shown(s))) +
            ",\"changed\":" + (session?.IsChanged(s) == true ? "true" : "false") +
            ",\"locked\":" + Q(s.LockedBy ?? "") + "}");
        return "{\"ok\":" + (why is null ? "true" : "false") + ",\"cmd\":\"settings\",\"verb\":" + Q(verb) +
               (why is null ? "" : ",\"error\":" + Q(why)) + ",\"session\":" + (session is null ? "false" : "true") +
               ",\"writes\":" + SettingsStore.Writes + ",\"settings\":[" + string.Join(',', rows) + "]}";
    }

    static string DoView(string[] args)
    {
        var m = RecompOne.Runtime.Runtime.Mem;
        if (m == null) return Err("not running");
        if (args.Length == 1 && args[0].Equals("off", StringComparison.OrdinalIgnoreCase))
            Stage15.ViewOverride = null;
        else if (args.Length == 6)
        {
            var v = new int[6];
            for (int i = 0; i < 6; i++)
                if (!int.TryParse(args[i], out v[i])) return Err("view <x> <y> <z> <pitch> <yaw> <roll> | off");
            if (!Stage15.InCSharp) return Err("the override needs stage 15 in C# (KF3_STAGE15 unset or 1)");
            ViewSmoothing.Suspended = true;
            Stage15.ViewOverride = new Camera(v[0], v[1], v[2], (short)v[3], (short)v[4], (short)v[5]);
        }
        else if (args.Length != 0) return Err("view <x> <y> <z> <pitch> <yaw> <roll> | off");
        if (args.Length == 1) ViewSmoothing.Suspended = false;

        var cam = Camera.Read(m);
        string handed = Stage15.Handed is { } h ? $"[{h.X},{h.Y},{h.Z},{h.Pitch},{h.Yaw},{h.Roll}]" : "null";
        return "{\"ok\":true,\"cmd\":\"view\",\"camera\":[" + $"{cam.X},{cam.Y},{cam.Z},{cam.Pitch},{cam.Yaw},{cam.Roll}" +
               "],\"handed\":" + handed + ",\"override\":" + (Stage15.ViewOverride != null ? "true" : "false") + "}";
    }

    public static string Err(string message) => "{\"ok\":false,\"error\":" + Q(message) + "}";

    public static string Q(string s)
    {
        var sb = new StringBuilder(s.Length + 2);
        sb.Append('"');
        foreach (char ch in s)
        {
            switch (ch)
            {
                case '\"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (ch < 0x20) sb.Append("\\u").Append(((int)ch).ToString("x4"));
                    else sb.Append(ch);
                    break;
            }
        }
        sb.Append('"');
        return sb.ToString();
    }
}
