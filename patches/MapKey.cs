using System.Reflection;
using RecompOne.Runtime.Context;
using RecompOne.Runtime.Events;
using RecompOne.Runtime.Hardware;
using RecompOne.Runtime.Host.Window;
using RecompOne.Runtime.Memory;
using RecompOne.Runtime.Modding;
using Silk.NET.Input;
using Game = Recompiled.KingsField3_game;

namespace Kf3;

/// <summary>
/// M, or the touchpad of a DualShock 4 or DualSense, opens the map of the area the
/// player is in, as using the map item from the menu would:
///
///     KF3_MAPKEY=1      on (the default); 0 leaves M and the touchpad alone
///     KF3_MAPKEY_TEST=10  open the map ten seconds into the first area, as a press would
///
/// The game has two maps, both picture viewers called from USE ITEM
/// (<c>func_8001AA84</c>): PIXY'S MAP (item 0x60) is <c>func_8001BABC</c>, the
/// picture <c>0x2D1 + area</c> with the player marked on it, and MAP OF VERDITE
/// (0x5F) is <c>func_8001B5C4(0x5F)</c>, one picture, <c>0x2F1</c>, which marks the
/// player only in areas 0..11. PIXY'S MAP is opened when it is held; otherwise MAP
/// OF VERDITE, in an area it covers; otherwise nothing. Select's handler, which may
/// once have been the map, is an empty stub in this build (docs/INPUT.md).
///
/// The viewer is modal and runs inside the menu framework, so the press is taken in
/// the player's tick, where examine opens the in-game menu, and the wrapping the
/// menu and its item page give it is reproduced: the menu's enter
/// <c>func_80027198</c>; the page's <c>func_80027BB4</c>, which gives the CD to the
/// menu's ready callback <c>func_80027EA8</c> (without it the viewer's picture never
/// finishes loading and it waits forever); the item page's settle of its preview
/// load, <c>func_80027E60</c> and <c>func_80027B5C</c>; the viewer; the close sound
/// <c>func_8002792C(0xE)</c>; the page's <c>func_80027C8C</c>, which gives the CD back
/// to the world's callback; the menu's leave <c>func_80027310(0)</c>. The viewer
/// closes on any pad button; M or the touchpad again presses Circle for it.
/// See "The maps" in docs/GAME_INTERNALS.md.
/// </summary>
public static class MapKey
{
    const uint PlayerStage = 0x80030FCC;

    const uint PixyMap = 0x60, VerditeMap = 0x5F;
    const int VerditeAreas = 12;            // func_8001B5C4 marks the player in areas below this

    const uint Inventory = 0x800C85E8;      // a count byte an id
    const uint Overflow = 0x800C867E;       // func_8005D898's second home for a count

    const uint Area = 0x8018FADD;           // u8, what both viewers read
    const uint PendingLoad = 0x8018FAD4;    // s16, non-zero while an area loads
    const uint MaxHp = 0x801B24FA;          // u16, 0 with no character
    const uint State = 0x801B25E5;          // u8, the action byte
    const byte StateDead = 0x11;
    const uint MessageBox = 0x801AEAF7;     // u8, 0 when the bottom box is idle

    const int PadTouchpad = 20;             // SDL_CONTROLLER_BUTTON_TOUCHPAD

    public static bool Enabled { get; private set; } = true;

    static volatile bool _request, _open, _closeHeld;
    static long _requestedAt;
    static double _testAt;
    static readonly System.Diagnostics.Stopwatch _testClock = new();

    static readonly ModInfo _self = new()
    {
        Id = "kf3.mapkey",
        Name = "Map key",
        Version = "1.0",
        Description = "M or the touchpad opens the map of the current area, if it is held.",
    };

    public static void Configure(string? enabled, string? test)
    {
        if (!string.IsNullOrWhiteSpace(enabled)) Enabled = enabled.Trim() is not ("0" or "off");
        if (!string.IsNullOrWhiteSpace(test) &&
            double.TryParse(test, System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out double seconds))
            _testAt = Math.Max(seconds, 0.1);
    }

    public static void Install()
    {
        if (!Enabled) return;
        Event.AddListener<KeyboardEvent>(e =>
        {
            if (e.Key != (int)Key.M || e.Repeat) return;
            if (e.Pressed && (PopupManager.AnyOpen || MouseLook.Game.TextEditing())) return;
            Press(e.Pressed);
        });
        Event.AddListener<ControllerEvent>(e =>
        {
            if (e.Button != PadTouchpad) return;
            if (e.Pressed && PopupManager.AnyOpen) return;
            Press(e.Pressed);
        });
        Event.AddListener<PadReadEvent>(e =>
        {
            if (e.Port != 0 || !_open || !_closeHeld) return;
            // Active low, the two button bytes swapped from Controller's layout.
            ushort bit = Controller.Circle;
            e.Buttons &= (ushort)~(ushort)((bit >> 8) | (bit << 8));
        });
        Event.AddListener<OverlayLoadedEvent>(e =>
        {
            if (_testAt > 0 && !_testClock.IsRunning && e.Name.StartsWith("fdat", StringComparison.Ordinal))
                _testClock.Start();
        });
        HookAttach.OnOverlayLoad("map key", Attach);
    }

    /// <summary>A press opens the map, or closes the one open; a release ends the
    /// Circle it held down.</summary>
    static void Press(bool pressed)
    {
        if (!pressed) { _closeHeld = false; return; }
        if (_open) { _closeHeld = true; return; }
        _requestedAt = Environment.TickCount64;
        _request = true;
    }

    static bool Attach()
    {
        SymbolRegistry.Build();
        var target = SymbolRegistry.Resolve("game", null, PlayerStage);
        if (target == null) return false;
        var impl = typeof(MapKey).GetMethod(nameof(AfterPlayerStage), BindingFlags.Public | BindingFlags.Static)!;
        if (!HookManager.AddPost(_self, target, impl)) return false;
        HookManager.Commit();

        bool ok = HookAttach.Installed(target);
        Console.WriteLine(ok ? "[KF3] map key: on (M, touchpad)" : "[KF3] map key: the stage 4 hook did not install");
        return ok;
    }

    /// <summary>End of main-loop stage 4, on a world tick: the in-game menu opens
    /// from inside this stage too, so nothing modal is open here. A press older
    /// than half a second (made in a menu, or while loading) is dropped.</summary>
    public static void AfterPlayerStage(CpuContext c, IMemory m)
    {
        if (!FramePacing.IterationTicked || m is not PSMemory mem) return;
        if (_testAt > 0 && _testClock.IsRunning && _testClock.Elapsed.TotalSeconds >= _testAt)
        {
            _testAt = 0;
            _testClock.Stop();
            _requestedAt = Environment.TickCount64;
            _request = true;
        }
        if (!_request) return;
        _request = false;
        if (Environment.TickCount64 - _requestedAt > 500) return;

        if (m.ReadU16(MaxHp) == 0 || m.ReadU8(State) == StateDead ||
            m.ReadU16(PendingLoad) != 0 || m.ReadU8(MessageBox) != 0)
            return;

        int area = m.ReadU8(Area);
        uint? map = Has(m, PixyMap) ? PixyMap
                  : Has(m, VerditeMap) && area < VerditeAreas ? VerditeMap
                  : null;
        if (map == null)
        {
            Console.WriteLine($"[KF3] map key: no map held for area {area}");
            return;
        }

        Console.WriteLine($"[KF3] map key: {(map == PixyMap ? "PIXY'S MAP" : "MAP OF VERDITE")}, area {area}");
        var saved = c.Snapshot();
        _open = true;
        try
        {
            Game.func_80027198(c, mem);
            Game.func_80027BB4(c, mem);
            Game.func_80027E60(c, mem);
            Game.func_80027B5C(c, mem);
            if (map == PixyMap)
                Game.func_8001BABC(c, mem);
            else
            {
                c.A0 = VerditeMap;
                Game.func_8001B5C4(c, mem);
            }
            c.A0 = 0xE;
            Game.func_8002792C(c, mem);
            Game.func_80027C8C(c, mem);
            c.A0 = 0;
            Game.func_80027310(c, mem);
        }
        finally
        {
            _open = false;
            _closeHeld = false;
            c.Restore(saved);
        }
        Console.WriteLine("[KF3] map key: closed");
    }

    static bool Has(IMemory m, uint id) => m.ReadU8(Inventory + id) != 0 || m.ReadU8(Overflow + id) != 0;
}
