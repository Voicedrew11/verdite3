using System.Reflection;
using RecompOne.Runtime.Context;
using RecompOne.Runtime.Events;
using RecompOne.Runtime.Memory;
using RecompOne.Runtime.Modding;

namespace Kf3;

/// <summary>
/// KF3_AGENT=1: a [KF3-AGENT] line on every overlay load and a JSON snapshot about
/// once a second, so a program can tell "at the title" from "in an area" without
/// a screenshot. Verdite2's AgentBeacon; the fields are this game's. Read-only,
/// on the game thread (VSync). See "Driving the game without a person" in docs/DEVELOPMENT.md.
/// </summary>
public static class AgentBeacon
{
    // The player block GAME.EXE clears at 0x801B24E4 (0x67 words) for each session.
    // See "The player" in docs/GAME_INTERNALS.md.
    public const uint Exp   = 0x801B24E4;   // s32
    public const uint Level = 0x801B24F0;   // u8
    public const uint MaxHp = 0x801B24FA;   // u16
    public const uint Hp    = 0x801B24FC;   // u16
    public const uint MaxMp = 0x801B24FE;   // u16
    public const uint Mp    = 0x801B2500;   // u16
    const uint PosX  = 0x801B25F0;          // s32, the player's position
    const uint PosY  = 0x801B25F4;
    const uint PosZ  = 0x801B25F8;
    const uint Yaw   = 0x801B260A;          // s16, 0x1000 a turn
    public const uint Area = 0x8018FAE4;    // u8, n for FDAT.T entries 3n..3n+2
    public const uint Slot = 0x8009C2C0;    // u8, the card file last loaded or saved (gp+0xAC)

    const long PeriodMs = 1000;

    // Stage 1 of the main loop: seen within a second means the loop is turning,
    // not a movie, a menu's own loop or a load.
    const uint FirstStage = 0x800341E8;
    static long _loopMs = -1;
    static readonly ModInfo _self = new() { Id = "kf3.beacon", Name = "Agent beacon", Version = "1.0" };

    /// <summary>Whether the main loop ran in the last second.</summary>
    public static bool LoopLive => _loopMs >= 0 && Environment.TickCount64 - _loopMs < 1000;

    public static void MainLoopTurned(CpuContext c, IMemory m) => _loopMs = Environment.TickCount64;

    static bool _on;
    static volatile string _overlay = "boot";
    static long _lastEmit;

    public static string Overlay => _overlay;

    public static void Configure(string? on)
    {
        _on = !string.IsNullOrWhiteSpace(on)
              && on.Trim().ToLowerInvariant() is "1" or "on" or "true" or "yes";
    }

    public static void Install()
    {
        Event.AddListener<OverlayLoadedEvent>(e =>
        {
            _overlay = e.Name;
            if (_on) Console.WriteLine($"[KF3-AGENT] overlay {e.Name}");
        });
        HookAttach.OnOverlayLoad("beacon", () =>
        {
            SymbolRegistry.Build();
            if (SymbolRegistry.Resolve("game", null, FirstStage) is not { } t) return false;
            HookManager.AddPre(_self, t, typeof(AgentBeacon).GetMethod(nameof(MainLoopTurned),
                                         BindingFlags.Public | BindingFlags.Static)!);
            HookManager.Commit();
            return HookAttach.Installed(t);
        });
        if (!_on) return;

        Event.AddListener<VSyncEvent>(_ =>
        {
            long now = Environment.TickCount64;
            if (now - _lastEmit < PeriodMs) return;
            _lastEmit = now;
            if (RecompOne.Runtime.Runtime.Mem != null) Console.WriteLine("[KF3-AGENT] " + Snapshot());
        });
        Console.WriteLine("[KF3-AGENT] beacon on");
    }

    /// <summary>The bare {...} JSON, shared with the command channel's state.</summary>
    public static string Snapshot()
    {
        var m = RecompOne.Runtime.Runtime.Mem;
        if (m == null) return "{\"overlay\":\"boot\",\"inGame\":false}";

        // The block is GAME.EXE's and is cleared when a session starts, so a max HP
        // of zero, or any other executable, is not in an area.
        if (!_overlay.StartsWith("fdat", StringComparison.Ordinal) || m.ReadU16(MaxHp) == 0)
            return $"{{\"overlay\":\"{_overlay}\",\"inGame\":false}}";

        int x = (int)m.ReadU32(PosX), y = (int)m.ReadU32(PosY), z = (int)m.ReadU32(PosZ);
        return
            $"{{\"overlay\":\"{_overlay}\",\"inGame\":true,\"loop\":{(LoopLive ? "true" : "false")}," +
            $"\"hp\":{m.ReadU16(Hp)},\"maxHp\":{m.ReadU16(MaxHp)}," +
            $"\"mp\":{m.ReadU16(Mp)},\"maxMp\":{m.ReadU16(MaxMp)}," +
            $"\"level\":{m.ReadU8(Level)},\"exp\":{(int)m.ReadU32(Exp)}," +
            $"\"area\":{m.ReadU8(Area)},\"slot\":{m.ReadU8(Slot)}," +
            $"\"pos\":[{x},{y},{z}],\"yaw\":{(short)m.ReadU16(Yaw)}}}";
    }
}
