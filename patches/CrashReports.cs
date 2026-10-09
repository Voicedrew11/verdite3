using System.Reflection;
using RecompOne.Runtime.Context;
using RecompOne.Runtime.Diagnostics;
using RecompOne.Runtime.Events;
using RecompOne.Runtime.Memory;
using RecompOne.Runtime.Modding;

namespace Kf3;

/// <summary>
/// What this port adds to the runtime's crash, fault and hang reports (runtime
/// 0107): its version, where the player is, and every switch that is not the
/// default. And a way to make each kind of report on purpose, so the paths can
/// be run without waiting for a real one. See "When it crashes" in
/// docs/DEVELOPMENT.md.
/// </summary>
public static class CrashReports
{
    public static void Configure(string? hang)
    {
        var version = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                          ?.InformationalVersion
                      ?? typeof(CrashReports).Assembly.GetName().Version?.ToString() ?? "?";
        CrashReport.Title = $"Verdite3 {version}";
        if (double.TryParse(hang, System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out var seconds))
            Watchdog.HangSeconds = seconds;

        CrashReport.AddSection("Game", AgentBeacon.Snapshot);
        CrashReport.AddSection("Settings (kf3.*)", () =>
        {
            var values = RecompOne.Runtime.Config.ConfigManager.View.Values;
            return string.Join('\n', values.Where(v => v.Key.StartsWith("kf3.", StringComparison.OrdinalIgnoreCase))
                                           .OrderBy(v => v.Key, StringComparer.Ordinal)
                                           .Select(v => $"{v.Key}={v.Value}"));
        });
        CrashReport.AddSection("Environment (KF3_*)", () =>
        {
            var set = Environment.GetEnvironmentVariables().Keys.Cast<string>()
                                 .Where(k => k.StartsWith("KF3_", StringComparison.Ordinal))
                                 .OrderBy(k => k, StringComparer.Ordinal)
                                 .Select(k => $"{k}={Environment.GetEnvironmentVariable(k)}");
            return string.Join('\n', set) is { Length: > 0 } s ? s : "(none)";
        });
    }

    // KF3_FAULT=hook|crash|hang[:seconds], timed from the first area module: a
    // test of each report's path, nothing a player sets.
    //     hook   a hook throws: a fault report, a notice, this hook off, the game goes on
    //     hook@id   the same hook, owned by the patch with that mod id (kf3.zbuffer, say,
    //            one the Testing tab has a row for), so that patch is the one turned off
    //     crash  the game's own code throws: a crash report and the crash screen
    //     hang   the game thread spins on a hardware register: a hang report with its stack
    static readonly ModInfo _self = new() { Id = "kf3.faultinject", Name = "Fault injection", Version = "1.0" };
    static string _mode = "";
    static string? _target;
    static double _after = 20;
    static readonly System.Diagnostics.Stopwatch _clock = new();

    public static void InstallFault(string? spec)
    {
        if (string.IsNullOrWhiteSpace(spec)) return;
        var parts = spec.Trim().ToLowerInvariant().Split(':');
        _mode = parts[0];
        if (_mode.StartsWith("hook@", StringComparison.Ordinal))
        {
            _target = _mode[5..];
            _mode = "hook";
        }
        if (_mode is not ("hook" or "crash" or "hang"))
        {
            Console.Error.WriteLine($"[KF3] KF3_FAULT: unknown '{spec}', want hook, crash or hang");
            return;
        }

        if (parts.Length > 1) double.TryParse(parts[1], System.Globalization.NumberStyles.Float,
                                               System.Globalization.CultureInfo.InvariantCulture, out _after);
        Event.AddListener<OverlayLoadedEvent>(e =>
        {
            if (!_clock.IsRunning && e.Name.StartsWith("fdat", StringComparison.Ordinal)) _clock.Start();
        });
        HookAttach.OnOverlayLoad("fault", () =>
        {
            SymbolRegistry.Build();
            if (SymbolRegistry.Resolve("game", null, AgentBeacon.FirstStage) is not { } t) return false;
            var owner = _target == null ? _self : TestingSection.OwnerById(_target);
            if (owner == null)
            {
                Console.Error.WriteLine($"[KF3] KF3_FAULT: no patch '{_target}' in the Testing tab");
                return true;
            }
            HookManager.AddPre(owner, t, typeof(CrashReports).GetMethod(nameof(Fire),
                                         BindingFlags.Public | BindingFlags.Static)!);
            HookManager.Commit();
            return HookAttach.Installed(t);
        });
        Console.WriteLine($"[KF3] fault injection: {_mode}{(_target == null ? "" : $" as {_target}")} {_after:F0}s after the first area");
    }

    public static void Fire(CpuContext c, IMemory m)
    {
        if (!_clock.IsRunning || _clock.Elapsed.TotalSeconds < _after) return;
        _clock.Reset();
        Console.WriteLine($"[KF3] fault injection: {_mode} now");
        switch (_mode)
        {
            case "hook":
                throw new InvalidOperationException("KF3_FAULT=hook: a hook throwing on purpose");
            case "crash":
                throw HookManager.NotAHookFault(new InvalidOperationException("KF3_FAULT=crash: a crash on purpose"));
            case "hang":
                while (true) m.ReadU32(0x1F801070u);
        }
    }
}
