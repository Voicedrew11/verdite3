using System.Reflection;
using RecompOne.Runtime.Context;
using RecompOne.Runtime.Events;
using RecompOne.Runtime.Memory;
using RecompOne.Runtime.Modding;
using Rt = RecompOne.Runtime.Runtime;
using Game = Recompiled.KingsField3_game;

namespace Kf3;

/// <summary>
/// Reload the last save on death. Ported from Verdite2's Kf2.AutoReload, on this
/// game's addresses:
///
///     KF3_AUTORELOAD=1          on (the default); 0 leaves the death alone
///     KF3_AUTORELOAD_DELAY=2.5  seconds of the game's death sequence first
///     KF3_AUTORELOAD_SLOT=0     0 = the game's own "last used" slot, 1..5 pins one
///
/// The switch and the slot are settings under Gameplay (<see cref="GameplaySection"/>),
/// kept in interface.ini; a set variable wins over them.
///
/// It adds no loading path of its own: it is the in-game menu's Load, without the
/// menu. The card session `func_80028034`, the loader `func_8002860C(slot)`, the
/// session's close `func_80028090`, then the menu's "loaded a save" arm (result -3,
/// at 0x80030740) transcribed: `func_80028F90`, `func_80029188(area, area, area,
/// u8[0x8018FADB], u8[0x8018FADC], 0xFF)` with the area `u8[0x8018FAD8]`, and
/// `func_8002B6F0`. The death sequence is held at the end of its animation while
/// the delay runs, so the game's own fade and respawn never come due.
///
/// One death is the game's own: holding a DRAGON CRYSTAL (item 0x6B), the death
/// handler uses it up and revives the player in area 0 with everything kept. That
/// is a rule of play rather than a trip through the menus, so it is left alone.
/// See "Death and auto reload" in docs/GAME_INTERNALS.md.
/// </summary>
public static class AutoReload
{
    // ---- GAME.EXE player state ("The player" in docs/GAME_INTERNALS.md) ----
    const uint Level = 0x801B24F0;         // u8
    const uint MaxHp = 0x801B24FA;         // u16
    const uint Hp    = 0x801B24FC;         // u16

    // The action byte, dispatched through the jump table at 0x80011AC0. 0x11 is
    // dead: the latch func_80030A6C is its only writer, and the state's handler
    // (0x80031C54) forces HP to 0 every tick.
    const uint State     = 0x801B25E5;     // u8
    const byte StateDead = 0x11;

    // The death clock: zeroed by the latch, +1 a tick in the state-17 handler.
    //     1..31   the death animation
    //     32..64  the fade, amount (n - 32) << 7
    //     65      the respawn: a DRAGON CRYSTAL revives, otherwise a New Game
    // At 15 Hz, 65 ticks is 4.3 s. Holding it at 31 is what makes the delay ours.
    const uint DeathFrames = 0x801B261E;   // s16
    const ushort HoldAt    = 31;

    // The card file last loaded or saved (gp+0xAC), written by the loader and the
    // saver. Zero in the executable image, so zero means neither has run.
    const uint CurrentSlot = 0x8009C2C0;   // u8

    // Inventory counts, one byte an id; 0x6B is the DRAGON CRYSTAL the death
    // handler checks with func_8005D7BC before it respawns.
    const uint Inventory    = 0x800C85E8;
    const uint DragonCrystal = 0x6B;

    // The loaded area descriptor the menu's -3 arm reads (0x8018FAD4 + 4, 7, 8).
    const uint LoadedArea = 0x8018FAD8;
    const uint LoadedArg4 = 0x8018FADB;
    const uint LoadedArg5 = 0x8018FADC;

    // Main-loop stage 4, the player's tick: it runs while dead, since the death
    // sequence is one arm of its own state machine.
    const uint PlayerStage = 0x80030FCC;

    public const int MaxSlot = 5;          // a save is 3 blocks; a card holds five

    /// <summary>interface.ini keys, read and written by the Gameplay tab.</summary>
    public const string OnKey   = "kf3.autoreload.enabled";
    public const string SlotKey = "kf3.autoreload.slot";

    /// <summary>Live: the hook stays attached and does nothing when this is off.</summary>
    public static bool Enabled { get; private set; } = true;

    /// <summary>Seconds of the death sequence before the reload. Not a setting, as
    /// in Verdite2: at 0 the reload lands in the animation and reads as a glitch,
    /// and a long one is the wait this exists to remove. The animation here is 31
    /// ticks at 15 Hz, 2.1 s, so 2.5 s lets it finish.</summary>
    public static float Delay { get; private set; } = 2.5f;

    /// <summary>0 = the game's last used slot; 1..5 pins one.</summary>
    public static int Slot { get; private set; }

    /// <summary>What the last death did.</summary>
    public static string Status { get; private set; } = "no death yet";

    public static long Deaths { get; private set; }
    public static long Reloads { get; private set; }

    static bool _onFromEnv, _slotFromEnv;

    static long _deadSince;      // TickCount64 at the death edge, 0 when alive
    static bool _fired;          // one reload per death
    static bool _deferred;       // this death is the DRAGON CRYSTAL's
    static bool _warnedNoSave;

    // A death is a transition from alive. Cleared whenever GAME.EXE is loaded,
    // since that is a new character or none.
    static bool _sawAlive;

    static readonly ModInfo _self = new()
    {
        Id = "kf3.autoreload",
        Name = "Auto reload",
        Version = "1.0",
        Description = "Reloads the last save on death.",
    };

    public static void Configure(string? enabled, string? delay, string? slot)
    {
        if (!string.IsNullOrWhiteSpace(enabled))
        {
            Enabled = enabled.Trim().ToLowerInvariant() is "1" or "on" or "true" or "yes";
            _onFromEnv = true;
        }

        if (!string.IsNullOrWhiteSpace(delay))
        {
            if (!float.TryParse(delay, System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out float seconds))
                throw new ArgumentException($"KF3_AUTORELOAD_DELAY: cannot read '{delay}'");
            Delay = Math.Clamp(seconds, 0f, 10f);
        }

        if (!string.IsNullOrWhiteSpace(slot))
        {
            if (!int.TryParse(slot, System.Globalization.NumberStyles.Integer,
                              System.Globalization.CultureInfo.InvariantCulture, out int which))
                throw new ArgumentException($"KF3_AUTORELOAD_SLOT: cannot read '{slot}'");
            SetSlot(which);
            _slotFromEnv = true;
        }
    }

    public static void Install()
    {
        Event.AddListener<RuntimeReadyEvent>(_ =>
        {
            if (!_onFromEnv) Enabled = Rt.View.GetInt(OnKey, Enabled ? 1 : 0) != 0;
            if (!_slotFromEnv) SetSlot(Rt.View.GetInt(SlotKey, Slot));
        });

        Event.AddListener<OverlayLoadedEvent>(e =>
        {
            if (!e.Name.Equals("game", StringComparison.Ordinal)) return;
            _deadSince = 0;
            _fired = _deferred = _sawAlive = false;
        });

        // Attached whether or not the setting is on, so it can be turned on later.
        HookAttach.OnOverlayLoad("autoreload", Attach,
            "A death will cost the menu, whatever the setting says. See \"Auto reload\" in docs/GAME_INTERNALS.md.");
    }

    public static void SetEnabled(bool on) => Enabled = on;

    public static void SetSlot(int slot) => Slot = Math.Clamp(slot, 0, MaxSlot);

    static bool Attach()
    {
        SymbolRegistry.Build();
        var target = SymbolRegistry.Resolve("game", null, PlayerStage);
        if (target == null) return false;
        var impl = typeof(AutoReload).GetMethod(nameof(AfterPlayerStage), BindingFlags.Public | BindingFlags.Static)!;
        if (!HookManager.AddPost(_self, target, impl)) return false;
        HookManager.Commit();

        bool ok = HookAttach.Installed(target);
        Console.WriteLine(ok
            ? $"[KF3] autoreload: {(Enabled ? "on" : "off")}, {Delay:0.#}s after death, " +
              $"slot {(Slot == 0 ? "last used" : Slot.ToString())}"
            : "[KF3] autoreload: the stage 4 hook did not install");
        return ok;
    }

    /// <summary>
    /// Kill the player the way the game does: HP to zero and the death latch
    /// func_80030A6C, with a zeroed knockback source. The shell's <c>kill</c>.
    /// </summary>
    public static string Simulate()
    {
        var cpu = Rt.Cpu;
        var mem = Rt.Mem;
        if (cpu == null || mem == null) return Status = "not running";
        if (mem.ReadU16(MaxHp) == 0) return Status = "no area running; load a save first";

        var saved = cpu.Snapshot();
        cpu.SP -= 0x20u;
        mem.WriteU32(cpu.SP + 0x10u, 0);
        mem.WriteU32(cpu.SP + 0x14u, 0);
        mem.WriteU16(Hp, 0);
        cpu.A0 = cpu.SP + 0x10u;
        Game.func_80030A6C(cpu, mem);
        cpu.Restore(saved);

        Console.WriteLine("[KF3] autoreload: simulated death");
        return Status = "simulated death";
    }

    /// <summary>End of main-loop stage 4. Under pacing the post runs every drawn
    /// frame and the stage only on a world tick, and the death clock moves only
    /// on a tick, so the rest waits for one: the game's own respawn runs from
    /// inside this stage on a tick too.</summary>
    public static void AfterPlayerStage(CpuContext c, IMemory m)
    {
        if (!Enabled || !FramePacing.IterationTicked) return;

        if (m.ReadU8(State) != StateDead)
        {
            // A live player pays a byte read and a halfword read a tick.
            _deadSince = 0;
            _fired = _deferred = false;
            // Max HP rather than HP: HP is legitimately 0 on the tick you die.
            if (m.ReadU16(MaxHp) != 0) _sawAlive = true;
            return;
        }

        if (_deadSince == 0)
        {
            if (!_sawAlive || _deferred) return;
            _deadSince = Environment.TickCount64;
            Deaths++;
            if (m.ReadU8(Inventory + DragonCrystal) != 0)
            {
                _deferred = true;
                _deadSince = 0;
                Status = "died holding a DRAGON CRYSTAL; the game revives";
                Console.WriteLine($"[KF3] autoreload: {Status}");
                return;
            }
            Console.WriteLine($"[KF3] autoreload: death (LV {m.ReadU8(Level)}, max HP {m.ReadU16(MaxHp)}); " +
                              $"reloading in {Delay:0.#}s");
            return;
        }

        if (_fired) return;

        // Held at the animation's last tick. The handler has already run this
        // tick, so the next one steps to 32, whose fade amount is exactly zero,
        // before this clamps it back.
        if (m.ReadU16(DeathFrames) > HoldAt) m.WriteU16(DeathFrames, HoldAt);

        if (Environment.TickCount64 - _deadSince < (long)(Delay * 1000f)) return;

        // Released either way: a failed reload hands the death back to the game.
        _fired = true;
        Reload(c, m);
    }

    /// <summary>
    /// The in-game menu's Load on <paramref name="slot"/>, without the menu: the
    /// card session around the loader, then the menu's -3 arm transcribed. Returns
    /// the loader's code, 0 loaded, 1 no such file, 2 bad checksum. Runs on the
    /// caller's CpuContext, bracketed in Snapshot/Restore; the stack window carries
    /// func_80029188's fifth and sixth arguments at sp+0x10 and sp+0x14.
    /// </summary>
    internal static uint LoadSlot(CpuContext c, IMemory m, byte slot, out uint area)
    {
        area = 0;
        var saved = c.Snapshot();

        Game.func_80028034(c, m);
        c.A0 = slot;
        Game.func_8002860C(c, m);
        uint result = c.V0;
        Game.func_80028090(c, m);

        if (result == 0)
        {
            Game.func_80028F90(c, m);

            area = m.ReadU8(LoadedArea);
            c.SP -= 0x20u;
            m.WriteU32(c.SP + 0x10u, m.ReadU8(LoadedArg5));
            m.WriteU32(c.SP + 0x14u, 0xFFu);
            c.A0 = area;
            c.A1 = area;
            c.A2 = area;
            c.A3 = m.ReadU8(LoadedArg4);
            Game.func_80029188(c, m);
            c.SP += 0x20u;

            Game.func_8002B6F0(c, m);

            // The menu's arm runs from a live state; the respawn's placement
            // func_8002B760 clears the action byte, and func_8003078C, the game's
            // own reset of it, is kept for a death it did not clear.
            if (m.ReadU8(State) == StateDead)
                Game.func_8003078C(c, m);
        }

        c.Restore(saved);
        return result;
    }

    static void Reload(CpuContext c, IMemory m)
    {
        byte slot = Slot != 0 ? (byte)Slot : m.ReadU8(CurrentSlot);
        if (slot == 0)
        {
            Status = "no save to reload: nothing has been saved or loaded this session";
            if (!_warnedNoSave)
            {
                _warnedNoSave = true;
                Console.WriteLine("[KF3] autoreload: died with no save on record; leaving the game alone");
            }
            return;
        }

        ushort held = m.ReadU16(DeathFrames);
        uint result = LoadSlot(c, m, slot, out uint area);

        if (result == 0)
        {
            Reloads++;
            Status = $"reloaded slot {slot} into area {area} " +
                     $"(HP {m.ReadU16(Hp)}/{m.ReadU16(MaxHp)}, LV {m.ReadU8(Level)}, " +
                     $"state 0x{m.ReadU8(State):X2}, held at tick {held})";
        }
        else
        {
            // The loader unpacks only after the sum checks, so the game's state is
            // untouched and its death sequence carries on.
            Status = $"slot {slot} would not load: {(result == 1 ? "no such save file" : "checksum failed")} ({result})";
        }
        Console.WriteLine($"[KF3] autoreload: {Status}");
    }
}
