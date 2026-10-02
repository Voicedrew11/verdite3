using System.Reflection;
using RecompOne.Runtime.Context;
using RecompOne.Runtime.Events;
using RecompOne.Runtime.Hardware;
using RecompOne.Runtime.Memory;
using RecompOne.Runtime.Modding;
using Game = Recompiled.KingsField3_game;

namespace Kf3;

/// <summary>
/// Boot straight into a save on card A, or into a New Game, with nobody at the pad:
///
///     KF3_AUTOSTART=1..15   load BASLUS-00255&lt;n&gt;
///     KF3_AUTOSTART=new     start a New Game
///
/// Start is pulsed through OPEN.EXE's intro and title (through PAD_dr, as in
/// Verdite2). GAME.EXE's start menu then takes the title's choice from the byte at
/// 0x800102FA (1 = Load, set here either way), and for a slot the chooser
/// func_8001FC5C is replaced by the game's own card loader, func_8002860C(slot).
/// See "Driving the game without a person" in docs/DEVELOPMENT.md.
/// </summary>
public static class AutoStart
{
    const uint StartMenu   = 0x8001FA60;   // returns the loaded slot, or -1 for a New Game
    const uint SlotChooser = 0x8001FC5C;   // called by the start menu when the title chose Load
    const uint TitleChoice = 0x800102FA;   // u8 in the boot stub's data: 1 = Load

    public static int Slot { get; private set; }
    public static bool NewGame { get; private set; }
    public static string Status { get; private set; } = "off";

    static volatile string _overlay = "boot";
    static volatile ushort _inject;
    static bool _done;

    static readonly ModInfo _self = new() { Id = "kf3.autostart", Name = "Auto start", Version = "1.0" };

    public static void Configure(string? spec)
    {
        if (string.IsNullOrWhiteSpace(spec)) return;
        if (spec.Trim().Equals("new", StringComparison.OrdinalIgnoreCase)) { NewGame = true; return; }
        if (!int.TryParse(spec.Trim(), out int slot) || slot < 1 || slot > 15)
            throw new ArgumentException($"KF3_AUTOSTART: '{spec}' is not 1..15 or new");
        Slot = slot;
    }

    public static void Install()
    {
        if (Slot == 0 && !NewGame) return;

        Event.AddListener<OverlayLoadedEvent>(e => _overlay = e.Name);

        // Active-low, bytes swapped against Controller's layout (Verdite2's Mouse.cs).
        Event.AddListener<PadReadEvent>(e =>
        {
            if (e.Port != 0) return;
            ushort press = _inject;
            if (press != 0) e.Buttons &= (ushort)~(ushort)((press >> 8) | (press << 8));
        });

        Event.AddListener<VSyncEvent>(_ => Announce());

        HookAttach.OnOverlayLoad("autostart", Attach);

        new Thread(Drive) { IsBackground = true, Name = "kf3-autostart" }.Start();
        Console.WriteLine(NewGame ? "[KF3] autostart: booting into a New Game"
                                  : $"[KF3] autostart: booting into slot {Slot}");
    }

    /// <summary>Start through OPEN.EXE, until GAME.EXE takes over; then nothing.</summary>
    static void Drive()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!_done)
        {
            double t = sw.Elapsed.TotalSeconds;
            _inject = _overlay == "open" && (long)(t * 60) % 30 < 14 ? Controller.Start : (ushort)0;
            Thread.Sleep(1);
        }
        _inject = 0;
    }

    static bool Attach()
    {
        SymbolRegistry.Build();
        var menu = SymbolRegistry.Resolve("game", null, StartMenu);
        var chooser = SymbolRegistry.Resolve("game", null, SlotChooser);
        if (menu == null || chooser == null) return false;

        var self = typeof(AutoStart);
        HookManager.AddPre(_self, menu, self.GetMethod(nameof(BeforeStartMenu), BindingFlags.Public | BindingFlags.Static)!);
        HookManager.AddPre(_self, chooser, self.GetMethod(nameof(ChooseSlot), BindingFlags.Public | BindingFlags.Static)!);
        HookManager.Commit();

        bool ok = HookAttach.Installed(menu) && HookAttach.Installed(chooser);
        if (!ok) Console.Error.WriteLine("[KF3] autostart: the start menu hooks did not install");
        return ok;
    }

    public static void BeforeStartMenu(CpuContext c, IMemory m)
    {
        if (!_done) m.WriteU8(TitleChoice, NewGame ? (byte)0 : (byte)1);
    }

    /// <summary>The slot chooser, replaced once: the card loader on the chosen slot.
    /// It returns 0 when loaded, 1 when there is no such file, 2 on a bad checksum.</summary>
    public static bool ChooseSlot(CpuContext c, IMemory m)
    {
        if (_done || Slot == 0) return true;
        c.A0 = (uint)Slot;
        Game.func_8002860C(c, m);
        uint result = c.V0;
        c.V0 = result == 0 ? (uint)Slot : 0xFFFFFFFFu;
        Status = result switch
        {
            0 => $"loaded slot {Slot}",
            1 => $"slot {Slot}: no such save, starting a New Game",
            _ => $"slot {Slot}: checksum failed ({result}), starting a New Game",
        };
        Console.WriteLine($"[KF3] autostart: {Status}");
        return false;
    }

    /// <summary>Once the main loop turns in an area, say what was loaded and stop.
    /// A New Game plays its opening movie first, about 30 s.</summary>
    static void Announce()
    {
        if (_done || !_overlay.StartsWith("fdat", StringComparison.Ordinal)) return;
        var m = RecompOne.Runtime.Runtime.Mem;
        if (m == null || m.ReadU16(AgentBeacon.MaxHp) == 0 || !AgentBeacon.LoopLive) return;
        _done = true;
        Console.WriteLine($"[KF3] autostart: in {_overlay}, area {m.ReadU8(AgentBeacon.Area)}, " +
                          $"HP {m.ReadU16(AgentBeacon.Hp)}/{m.ReadU16(AgentBeacon.MaxHp)}, " +
                          $"LV {m.ReadU8(AgentBeacon.Level)}, slot {m.ReadU8(AgentBeacon.Slot)}");
    }
}
