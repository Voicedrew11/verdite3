using System.Reflection;
using RecompOne.Runtime.Context;
using RecompOne.Runtime.Memory;
using RecompOne.Runtime.Modding;
using Game = Recompiled.KingsField3_game;

namespace Kf3;

/// <summary>Opt-in game-thread area driving for the renderer corpus, using copied cards.</summary>
public static class SceneDriver
{
    const uint Pending = 0x8018FAD4, Map = 0x801D4464;
    // The composed view yaw the front submit (8003F304) compares its bearing
    // against; stage 4 recomposes it, so it only holds while physics is held.
    const uint GuestYaw = 0x801B260A;
    static int _target = -1;
    static int _yaw = -1;   // held guest yaw, -1 when the driver is not overriding it
    static bool _requested, _enabled, _holdingPlayer;
    static readonly ModInfo Self = new() { Id = "kf3.scene-driver", Name = "Scene verification driver", Version = "1.0" };
    public static void Install()
    {
        _enabled = Environment.GetEnvironmentVariable("KF3_SCENE_DRIVER") == "1";
        if (!_enabled) return;
        HookAttach.OnOverlayLoad("scene driver", () =>
        {
            SymbolRegistry.Build();
            var objects = SymbolRegistry.Resolve("game", null, 0x80047010);
            var player = SymbolRegistry.Resolve("game", null, 0x80030FCC);
            if (objects == null || player == null) return false;
            HookManager.AddPost(Self, objects, typeof(SceneDriver).GetMethod(nameof(AfterObjects))!, order: int.MaxValue - 4);
            HookManager.AddPre(Self, player, typeof(SceneDriver).GetMethod(nameof(BeforePlayer))!, order: int.MinValue + 4);
            HookManager.AddPost(Self, player, typeof(SceneDriver).GetMethod(nameof(AfterPlayer))!, order: int.MaxValue - 4);
            HookManager.AddReplace(Self, player, typeof(SceneDriver).GetMethod(nameof(PlayerStep))!);
            HookManager.Commit();
            return HookAttach.Installed(objects) && HookAttach.Installed(player);
        });
    }
    public static string Warp(string arg)
    {
        if (!_enabled) return AgentServer.Err("warp requires KF3_SCENE_DRIVER=1; use copied cards and settings");
        if (!int.TryParse(arg, out int area) || (uint)area >= 28) return AgentServer.Err("warp <area 0..27>");
        if (_target >= 0) return AgentServer.Err("a warp is pending; confirm state before another");
        if (!AgentBeacon.Overlay.StartsWith("fdat")) return AgentServer.Err("no area loaded");
        _target = area; _requested = false;
        _holdingPlayer = true;
        return $"{{\"ok\":true,\"cmd\":\"warp\",\"requestedArea\":{area},\"loaded\":false}}";
    }
    /// <summary>
    /// Hold the guest view yaw the front submit reads, so all-area headings can
    /// reach the front assembler. Only meaningful while physics is held: stage 4
    /// recomposes <c>0x801B260A</c> every tick otherwise. <paramref name="m"/> is
    /// the shell's game-thread memory, written immediately and re-applied in
    /// <see cref="BeforePlayer"/>; <c>off</c> clears the hold without undoing it.
    /// </summary>
    public static string Yaw(string arg, IMemory? m)
    {
        if (!_enabled) return AgentServer.Err("scene-yaw requires KF3_SCENE_DRIVER=1; use copied cards and settings");
        // Off clears the hold only: the value last written stays in RAM.
        if (arg.Equals("off", StringComparison.OrdinalIgnoreCase)) { _yaw = -1; return "{\"ok\":true,\"cmd\":\"scene-yaw\",\"guestYaw\":null}"; }
        if (!int.TryParse(arg, out int yaw) || yaw < 0 || yaw > 4095) return AgentServer.Err("scene-yaw <0..4095|off>");
        if (!_holdingPlayer) return AgentServer.Err("scene-yaw needs a warp first; stage 4 overwrites an unheld guest yaw");
        if (m == null) return AgentServer.Err("not running");
        // Write now, not only on the next tick: a following 'off' must not race
        // the tick and leave the last tested heading in RAM.
        _yaw = yaw;
        m.WriteU16(GuestYaw, (ushort)yaw);
        return $"{{\"ok\":true,\"cmd\":\"scene-yaw\",\"guestYaw\":{yaw}}}";
    }
    public static void AfterObjects(CpuContext c, IMemory m)
    {
        if (_target < 0) return;
        if (!_requested)
        {
            var saved = c.Snapshot();
            c.SP -= 0x20;
            c.A0 = c.A1 = c.A2 = (uint)_target; c.A3 = 0xFF;
            m.WriteU32(c.SP + 0x10, 0xFF);
            m.WriteU32(c.SP + 0x14, 0x7F); m.WriteU32(c.SP + 0x18, 0x7F); m.WriteU32(c.SP + 0x1C, 0x7F);
            try { Game.func_80017C78(c, m); }
            finally { c.Restore(saved); }
            _requested = true;
        }
    }
    public static void AfterPlayer(CpuContext c, IMemory m)
        => BeforePlayer(c, m);
    public static void PlayerStep(Action<CpuContext, IMemory> original, CpuContext c, IMemory m)
    {
        // Corpus driving holds player physics after a requested teleport. A carried
        // position can make the game's collision search loop indefinitely in a new
        // map. This is harness state, never a renderer or ordinary-play fallback.
        if (!_holdingPlayer) original(c, m);
    }
    public static void BeforePlayer(CpuContext c, IMemory m)
    {
        // Keep the held guest yaw in place every tick, before stage 10 and the
        // draw. Written here (not the shell reply) so it lands on the game thread.
        if (_holdingPlayer && _yaw >= 0) m.WriteU16(GuestYaw, (ushort)_yaw);
        if (_target < 0 || !_requested || m.ReadU16(Pending) != 0) return;
        int actual = m.ReadU8(AgentBeacon.Area);
        string expected = $"fdat{3 * _target + 2:D2}";
        if (actual != _target || AgentBeacon.Overlay != expected)
        {
            Console.Error.WriteLine($"[KF3] scene driver: requested {_target}, got area {actual}/{AgentBeacon.Overlay}; not coverage");
            _target = -1; _requested = false; return;
        }
        int tx = (int)m.ReadU32(0x801B25F0) >> 11, tz = (int)m.ReadU32(0x801B25F8) >> 11;
        bool found = false;
        for (int radius = 0; radius < 80 && !found; radius++)
            for (int z = Math.Max(0, tz - radius); z <= Math.Min(79, tz + radius) && !found; z++)
                for (int x = Math.Max(0, tx - radius); x <= Math.Min(79, tx + radius); x++)
                {
                    if (radius > 0 && Math.Abs(x - tx) != radius && Math.Abs(z - tz) != radius) continue;
                    uint tile = Map + (uint)((z * 80 + x) * 10);
                    if (m.ReadU8(tile) >= 240 && m.ReadU8(tile + 5) >= 240) continue;
                    m.WriteU32(0x801B25F0, (uint)((x << 11) + 1024));
                    m.WriteU32(0x801B25F8, (uint)((z << 11) + 1024));
                    m.WriteU16(0x801B2644, (ushort)(m.ReadU8(tile) < 240 ? 0 : 5));
                    found = true; break;
                }
        var saved = c.Snapshot();
        foreach (uint address in new uint[] { 0x801B2630, 0x801B2632, 0x801B2634,
            0x801B2646, 0x801B2648, 0x801B264A, 0x801B264C, 0x801B264E,
            0x801B2656, 0x801B266C, 0x801B266E, 0x801B2670 }) m.WriteU16(address, 0);
        try { Game.func_8002B760(c, m); }
        finally { c.Restore(saved); }
        Console.WriteLine($"[KF3] scene driver: confirmed area {actual}/{expected}; floor placement {(found ? "found" : "unresolved")}");
        _target = -1; _requested = false;
    }
}
