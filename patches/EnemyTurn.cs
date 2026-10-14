using System.Reflection;
using RecompOne.Runtime.Context;
using RecompOne.Runtime.Events;
using RecompOne.Runtime.Memory;
using RecompOne.Runtime.Modding;
using Rt = RecompOne.Runtime.Runtime;
using Game = Recompiled.KingsField3_game;

namespace Kf3;

/// <summary>
/// Enemies turn faster: a multiplier on the top speed and the acceleration a
/// creature turns toward the player with, so a player who turns faster than the
/// D-pad (look acceleration, the mouse) is not circling something that turns at a
/// pad's pace. Ported from Verdite2's EnemyTurn, on this game's addresses.
///
/// Every creature turn goes through one routine,
/// <c>func_8004E928(rec, target, maxSpeed, accel)</c>, Verdite2's
/// <c>func_8003C8D8</c> instruction for instruction: the turn velocity at
/// <c>rec+0x58</c> gains <c>accel</c> toward the target, is clamped to
/// <c>±maxSpeed</c> and is added to the yaw at <c>rec+0x42</c>; a step that carries
/// the yaw past the target puts it on the target. The creature tick
/// (<c>func_800500A8</c>) passes the rule's speed and the type's acceleration
/// (definition <c>+0x4</c>); two wrappers (<c>func_8004EA7C</c>,
/// <c>func_8004ED50</c>) call it with their own. A pre-hook scales the speed by the
/// multiplier and the acceleration by its square, so a turn of any size takes 1/k
/// the time; every caller is covered and nothing is stored: x1 is the game's own turn.
///
/// Only a turn at the player is scaled. The chase writes its heading as
/// <c>rec+0x64 = func_80016AB8(player - creature)</c> (<c>0x80051774</c>), and a
/// wander heading is <c>rand &gt;&gt; 3</c> (<c>0x800520B0</c>), so a target within
/// <see cref="AtPlayer"/> of the player's bearing is a chase.
///
/// **Tracking while standing**, as Verdite2's: with <see cref="Track"/> on and the
/// multiplier above x1, once a tick each awake, living creature that the game
/// neither turned nor moved, and is engaged (from the game's own turn at the player
/// until its next turn elsewhere), is turned at the player through the same routine,
/// at <see cref="TrackSpeed"/> and its type's acceleration. Verdite2 measured why;
/// this game's creatures have not been watched. See "Enemies turn faster" in
/// docs/GAME_INTERNALS.md.
///
///     KF3_ENEMYTURN=1..8        the multiplier (Gameplay ▸ Enemy turn speed)
///     KF3_ENEMYTURN_TRACK=0     no turning at the player while standing (on)
///     KF3_ENEMYTURN_ALL=1       scale every turn, wandering too -- comparison only
///     KF3_ENEMYTURN_PROBE=1     a census of the turn's callers every 5 s
///     KF3_ENEMYTURN_PROBE=2     also a line a tick for each awake creature in reach
/// </summary>
public static class EnemyTurn
{
    const uint Turn = 0x8004E928;

    // The creature record, as the turn reads it.
    const int YawOff = 0x42, VelOff = 0x58, ActionOff = 0xE, TypeOff = 0x2;
    const int PosXOff = 0x2C, PosZOff = 0x34;
    const uint PlayerX = 0x801B25F0, PlayerZ = 0x801B25F8;

    /// <summary>The overshoot test compares the half-turn either side of the target,
    /// so a step must stay well under 0x800 or a turn could pass the target without
    /// seeing it.</summary>
    const int MaxStep = 0x400;

    /// <summary>How far a target may be from the player's bearing and still be a
    /// chase, 1.4 degrees: Verdite2's, whose chase heading is computed the same way.</summary>
    const int AtPlayer = 16;

    public const string Key = "kf3.enemyturn";
    public const float Min = 1f, Max = 8f;
    public const string TrackKey = "kf3.enemyturn.track";

    /// <summary>The top speed of a turn made while standing, before the multiplier;
    /// Verdite2's.</summary>
    const int TrackSpeed = 16;

    /// <summary>1 is the game's own turn.</summary>
    public static float Multiplier { get; private set; } = 1f;

    public static void SetMultiplier(float k) => Multiplier = Math.Clamp(k, Min, Max);

    /// <summary>Turn a standing creature at the player each tick. Inert at x1.</summary>
    public static bool Track { get; private set; } = true;

    public static void SetTrack(bool on) => Track = on;

    static bool _fromEnv, _trackFromEnv, _probe, _trace, _all, _queued, _hooked, _stageHooked;

    static readonly ModInfo _self = new()
    {
        Id = "kf3.enemyturn",
        Name = "Enemy turn speed",
        Version = "1.0",
        Description = "Scales how fast creatures turn.",
    };

    public static void Configure(string? multiplier, string? track, string? all, string? probe)
    {
        if (!string.IsNullOrWhiteSpace(multiplier))
        {
            if (float.TryParse(multiplier, System.Globalization.NumberStyles.Float,
                               System.Globalization.CultureInfo.InvariantCulture, out float k) && k >= Min && k <= Max)
            {
                SetMultiplier(k);
                _fromEnv = true;
            }
            else Console.WriteLine($"[KF3] enemy turn: KF3_ENEMYTURN expects {Min}..{Max}, not '{multiplier}'; ignored");
        }
        if (!string.IsNullOrWhiteSpace(track))
        {
            Track = track != "0";
            _trackFromEnv = true;
        }
        if (!string.IsNullOrWhiteSpace(all)) _all = all != "0";
        if (!string.IsNullOrWhiteSpace(probe))
        {
            _probe = probe != "0";
            _trace = probe == "2";
        }
    }

    public static void Install()
    {
        // The saved value is readable only once ConfigManager has loaded; the variable wins.
        Event.AddListener<RuntimeReadyEvent>(_ =>
        {
            if (!_fromEnv) SetMultiplier(Rt.View.GetFloat(Key, Multiplier));
            if (!_trackFromEnv) Track = Rt.View.GetInt(TrackKey, Track ? 1 : 0) != 0;
        });
        // An area's creatures reuse the table's records.
        Event.AddListener<OverlayLoadedEvent>(_ =>
        {
            _engaged.Clear();
            _lastPos.Clear();
        });
        HookAttach.OnOverlayLoad("enemy turn", Attach,
            "Creatures will turn at the game's speed. See \"Enemies turn faster\" in docs/GAME_INTERNALS.md.");
    }

    static MethodInfo M(string name) => typeof(EnemyTurn).GetMethod(name, BindingFlags.Public | BindingFlags.Static)!;

    static bool Attach()
    {
        SymbolRegistry.Build();
        var turn = SymbolRegistry.Resolve("game", null, Turn);
        if (turn == null) return false;

        // Queued once: Installed() is true for a function any patch has hooked
        // (FramePacing gates stage 5), so it cannot say whether these are on it.
        var stage = SymbolRegistry.Resolve("game", null, Stage5);
        if (!_queued)
        {
            _queued = true;
            HookManager.AddPre(_self, turn, M(nameof(BeforeTurn)));
            if (stage != null) HookManager.AddPost(_self, stage, M(nameof(AfterStage5)));
        }
        HookManager.Commit();

        _hooked = HookAttach.Installed(turn);
        _stageHooked = HookAttach.Installed(stage);
        Console.WriteLine($"[KF3] enemy turn: {(_hooked ? $"x{Multiplier:0.##}" : "off (hook not committed)")}" +
                          (_all ? ", every turn" : ", turns at the player") +
                          (_stageHooked ? Track ? ", tracking while standing" : "" : ", no tracking (stage 5 not hooked)") +
                          (_probe ? ", census on" : ""));
        return _hooked && _stageHooked;
    }

    public static void BeforeTurn(CpuContext c, IMemory m)
    {
        uint rec = c.A0;
        if (rec < 0x80010000u || rec >= 0x80200000u) return;
        bool atPlayer = Math.Abs(Wrap((int)c.A1 - Bearing(rec, m))) <= AtPlayer;
        bool chase = _all || atPlayer;
        if (_probe) Census(c, m, chase);
        if (_trace || _stageHooked) _turned[rec] = (chase, (int)c.A1);
        // Only the game's own turns engage or release; ours (RA marked Stage5) are
        // at the player by construction and say nothing about what it is doing.
        if (c.RA != Stage5)
        {
            if (atPlayer) _engaged.Add(rec);
            else _engaged.Remove(rec);
        }

        float k = Multiplier;
        if (k == 1f || !chase) return;
        // A turn starts from rest and gains accel a tick until it reaches maxSpeed,
        // so a short turn takes sqrt(2 angle / accel) ticks and a long one about
        // angle / maxSpeed. Speed times k and acceleration times k squared make
        // either k times faster. A zero stays zero, so a caller that does not turn
        // keeps not turning.
        c.A2 = (uint)Scale((int)c.A2, k);
        c.A3 = (uint)Scale((int)c.A3, k * k);
    }

    static int Scale(int v, float k) =>
        v <= 0 || v >= MaxStep ? v : Math.Min(MaxStep, (int)MathF.Round(v * k));

    /// <summary>The player's bearing from the creature, as the chase computes it:
    /// <c>func_80016AB8(dx, dz)</c>, which is <c>-atan2(dx, dz)</c> in a turn of 4096.</summary>
    static int Bearing(uint rec, IMemory m)
    {
        int dx = (int)m.ReadU32(PlayerX) - (int)m.ReadU32(rec + PosXOff);
        int dz = (int)m.ReadU32(PlayerZ) - (int)m.ReadU32(rec + PosZOff);
        return -(int)MathF.Round(MathF.Atan2(dx, dz) * 2048f / MathF.PI) & 0xFFF;
    }

    // ---- The census ----

    sealed class Site
    {
        public int Calls, Turning, TowardPlayer, SpeedMin = int.MaxValue, SpeedMax, AccelMin = int.MaxValue, AccelMax, VelMax;
        public readonly SortedSet<int> Actions = [], Types = [];
    }

    static readonly Dictionary<uint, Site> _sites = [];
    static long _windowStart;

    static void Census(CpuContext c, IMemory m, bool chase)
    {
        uint rec = c.A0;
        if (!_sites.TryGetValue(c.RA, out var s)) _sites[c.RA] = s = new Site();
        s.Calls++;
        int target = (int)(c.A1 & 0xFFF);
        int yaw = m.ReadU16(rec + YawOff) & 0xFFF;
        if (Wrap(target - yaw) != 0) s.Turning++;
        if (chase) s.TowardPlayer++;

        s.SpeedMin = Math.Min(s.SpeedMin, (int)c.A2); s.SpeedMax = Math.Max(s.SpeedMax, (int)c.A2);
        s.AccelMin = Math.Min(s.AccelMin, (int)c.A3); s.AccelMax = Math.Max(s.AccelMax, (int)c.A3);
        s.VelMax = Math.Max(s.VelMax, Math.Abs((short)m.ReadU16(rec + VelOff)));
        s.Actions.Add(m.ReadU8(rec + ActionOff));
        s.Types.Add(m.ReadU8(rec + TypeOff));

        long now = Environment.TickCount64;
        if (_windowStart == 0) _windowStart = now;
        if (now - _windowStart < 5000) return;
        _windowStart = now;
        foreach (var (ra, x) in _sites.OrderBy(p => p.Key))
            Console.WriteLine($"[KF3] enemy turn: from 0x{ra:X8} {x.Calls} calls, {x.Turning} turning, " +
                              $"{x.TowardPlayer} {(_all ? "scaled" : "at the player, scaled")}; speed {x.SpeedMin}..{x.SpeedMax}, " +
                              $"accel {x.AccelMin}..{x.AccelMax}, |vel| <= {x.VelMax}; " +
                              $"actions {string.Join(',', x.Actions.Select(a => $"0x{a:X2}"))}, " +
                              $"types {string.Join(',', x.Types)} (x{Multiplier:0.##})");
        _sites.Clear();
    }

    // ---- Tracking while standing, and the trace ----

    /// <summary>Stage 5, the creature walk ("Creatures wake and sleep by distance" in
    /// docs/GAME_INTERNALS.md).</summary>
    const uint Stage5 = 0x80052E5C;
    const uint Creatures = 0x80185DA8;
    const int CreatureStride = 0x88, CreatureCount = 200, StateOff = 0x9, HpOff = 0x1A;
    const int RuleOff = 0x60, DefStride = 120;
    const uint Defs = 0x8018C7E8;
    const int DefAccelOff = 0x4;

    /// <summary>The creature tick's jump table on the action byte holds 0xF1 entries
    /// (<c>0x800128F0</c>); 0xF0 and up is no rule's: a scripted or parked creature,
    /// left as the game has it.</summary>
    const int Actions = 0xF0;

    /// <summary>Stage 5's own run count, the word the waker's one-in-four test reads;
    /// bumped once at the end of every run. The post fires on frames the stage gate
    /// skipped too, and this is how it tells them apart.</summary>
    const uint Stage5Runs = 0x8018FACC;
    const int TraceReach = 12000;

    static readonly Dictionary<uint, (bool Chase, int Target)> _turned = [];
    static readonly Dictionary<uint, (int X, int Z)> _lastPos = [];
    static readonly HashSet<uint> _engaged = [];
    static uint _lastRun = uint.MaxValue;
    static int _tick;

    public static void AfterStage5(CpuContext c, IMemory m)
    {
        uint run = m.ReadU32(Stage5Runs);
        if (run == _lastRun) return;
        _lastRun = run;
        _tick++;

        bool track = Track && Multiplier > 1f && _hooked;
        int px = (int)m.ReadU32(PlayerX), pz = (int)m.ReadU32(PlayerZ);
        for (int i = 0; i < CreatureCount; i++)
        {
            uint rec = Creatures + (uint)(i * CreatureStride);
            if (m.ReadU8(rec) == 0xFF || m.ReadU8(rec + StateOff) != 1)
            {
                _lastPos.Remove(rec);
                _engaged.Remove(rec);
                continue;
            }
            int x = (int)m.ReadU32(rec + PosXOff), z = (int)m.ReadU32(rec + PosZOff);
            bool stood = _lastPos.TryGetValue(rec, out var last) && last.X == x && last.Z == z;
            _lastPos[rec] = (x, z);
            bool turned = _turned.TryGetValue(rec, out var t);

            // HP 0 is a creature dying, which keeps the facing it fell with.
            bool engaged = _engaged.Contains(rec);
            bool tracked = track && engaged && stood && !turned && m.ReadU16(rec + HpOff) != 0 &&
                           m.ReadU8(rec + ActionOff) < Actions;
            if (tracked) TurnAtPlayer(c, m, rec);

            if (!_trace) continue;
            int dist = (int)MathF.Sqrt((float)(px - x) * (px - x) + (float)(pz - z) * (pz - z));
            if (dist > TraceReach) continue;
            int yaw = m.ReadU16(rec + YawOff) & 0xFFF;
            string turn = turned ? $"turn {(t.Chase ? "chase" : "other")} to {Wrap(t.Target - Bearing(rec, m)):+0;-0}"
                        : tracked ? "tracked" : stood ? engaged ? "no turn, standing, engaged" : "no turn, standing" : "no turn";
            Console.WriteLine($"[KF3] enemy trace: t{_tick} slot {i} type {m.ReadU8(rec + TypeOff)} " +
                              $"act 0x{m.ReadU8(rec + ActionOff):X2} rule 0x{m.ReadU32(rec + RuleOff):X8} " +
                              $"hp {m.ReadU16(rec + HpOff)} off {Wrap(Bearing(rec, m) - yaw):+0;-0} " +
                              $"vel {(short)m.ReadU16(rec + VelOff)} dist {dist} {turn}");
        }
        _turned.Clear();
    }

    /// <summary>The game's own turn, aimed at the player at <see cref="TrackSpeed"/>
    /// and the type's acceleration. It goes through the hooked routine, so
    /// <see cref="BeforeTurn"/> sees a turn at the player and scales it.</summary>
    static void TurnAtPlayer(CpuContext c, IMemory m, uint rec)
    {
        uint def = Defs + (uint)(m.ReadU8(rec + TypeOff) * DefStride);
        var saved = c.Snapshot();
        try
        {
            c.A0 = rec;
            c.A1 = (uint)Bearing(rec, m);
            c.A2 = TrackSpeed;
            c.A3 = m.ReadU8(def + DefAccelOff);
            c.RA = Stage5;   // only a marker for the census; the routine returns to C#
            Game.func_8004E928(c, m);
        }
        finally
        {
            c.Restore(saved);
        }
    }

    /// <summary>An angle difference into -2048..2047.</summary>
    static int Wrap(int d) => ((d + 0x800) & 0xFFF) - 0x800;
}
