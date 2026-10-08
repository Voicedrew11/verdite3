// ModCompiler compiles mods with no implicit usings, so every namespace the
// file needs must be named here -- including System.
using System;
using System.Collections.Generic;
// HostWindow is in RecompOne.Runtime.Host, not .Host.Window -- the file lives in
// the Window folder but declares the parent namespace. ToastNotifications is in
// .Window.
using RecompOne.Runtime.Host;
using RecompOne.Runtime.Host.Window;
using Silk.NET.Input;

namespace Kf3.Mods.Debug;

/// <summary>
/// Keyboard hotkeys, and the pad button the game itself does not use.
///
/// HostWindow.IsKeyDown reads the live keyboard, so the toggles are polled once a
/// frame from the mod's stage-4 hook and edge-detected here. That is simpler than
/// the KeyboardEvent bus and it gives held keys -- which flight needs -- for free.
///
/// The pad half is the same poll through HostWindow.IsPadButtonDown, which
/// answers about one named button -- GetFirstPressedPadButton reports only the
/// lowest index held, so the mute key would be invisible behind anything else.
///
/// ---- the keys, and why they are not the reference's ----
///
/// The KF2 original flew on Space / Left Ctrl / Left Shift. This game's shipped
/// layout (patches/KeyLayout.cs) binds Space to Triangle -- attack -- so that pair
/// cannot be reused. Everything else the shipped layout claims is W A S D for the
/// walk and the strafes, the arrows for turn and walk, F, Q, Tab, Enter and Right
/// Shift; Page Up and Page Down are free in both the shipped layout and
/// RecompOne's stock one, and Left Shift is too (only the right one is bound), so
/// those are the flight keys. The toggles stay on F2..F8, which no layout touches.
/// V, which no layout touches either, is film mode: the cinematic camera, enemies
/// that ignore you and noclip, together.
///
/// </summary>
internal static class Hotkeys
{
    internal static bool Enabled = true;

    internal static Key TogglePanel   = Key.F2;
    internal static Key ToggleNoclip  = Key.F3;
    internal static Key ToggleGodMode = Key.F4;
    internal static Key SaveBookmark  = Key.F5;
    internal static Key LoadBookmark  = Key.F6;
    internal static Key SnapToFloor   = Key.F7;
    internal static Key ReturnToEntry = Key.F8;
    internal static Key FilmMode      = Key.V;
    internal static Key FlyUp         = Key.PageUp;
    internal static Key FlyDown       = Key.PageDown;
    internal static Key FlyFastKey    = Key.ShiftLeft;

    // SDL's GameControllerButton indices, which is the encoding
    // HostWindow.IsPadButtonDown takes. Misc1 is the extra button a pad has
    // beyond the PlayStation layout -- the DualSense's mute key, an Xbox Series
    // pad's share button -- and nothing in the game is bound to it. The sticks
    // and shoulders are listed so a panel can name them; this game reads the
    // flight pad buttons (R2/L2/R3) in Noclip, where they are already read, and
    // leaves the shoulders to strafe.
    internal const int PadMute        = 15;   // Misc1
    internal const int PadLeftStick   = 7;    // L3
    internal const int PadRightStick  = 8;    // R3
    internal const int PadLShoulder   = 9;    // L1
    internal const int PadRShoulder   = 10;   // R1

    static readonly HashSet<Key> _held = [];
    static readonly HashSet<int> _padHeld = [];

    /// <summary>
    /// True on the frame the key goes down, false while it stays down. Polled,
    /// so a key tapped and released between two ticks is missed -- at the
    /// world's 15 ticks a second that needs a 67 ms tap.
    /// </summary>
    static bool Pressed(Key key)
    {
        if (!Down(key))
        {
            _held.Remove(key);
            return false;
        }
        return _held.Add(key);
    }

    internal static bool Down(Key key) => Enabled && HostWindow.IsKeyDown(key);

    /// <summary>The pad's own edge detect, the same shape as Pressed(Key).</summary>
    static bool PadPressed(int button)
    {
        if (!HostWindow.IsPadButtonDown(button))
        {
            _padHeld.Remove(button);
            return false;
        }
        return _padHeld.Add(button);
    }

    internal static bool PadDown(int button) => Enabled && HostWindow.IsPadButtonDown(button);

    internal static bool FlyFast() => Enabled && Down(FlyFastKey);

    /// <summary>
    /// +1 for up, -1 for down, 0 for neither.
    ///
    /// Keyboard only: the pad's own R2/L2 stay read in Noclip, where this game
    /// already reads them (and where the shoulders are strafe, not vertical).
    /// </summary>
    internal static float FlyVertical()
    {
        if (!Enabled) return 0f;
        float v = 0f;
        if (Down(FlyUp)) v += 1f;
        if (Down(FlyDown)) v -= 1f;
        return v;
    }

    /// <summary>
    /// Run the toggles. Called once a frame from the mod's stage-4 hook.
    /// </summary>
    internal static void Poll()
    {
        if (!Enabled) return;

        if (Pressed(TogglePanel))
            DebugPanel.Instance.IsOpen = !DebugPanel.Instance.IsOpen;

        // F3, or the pad's spare button -- the DualSense's mute key -- so a
        // player on a controller never has to reach for the keyboard to fly.
        if (Pressed(ToggleNoclip) || PadPressed(PadMute))
        {
            Noclip.Enabled = !Noclip.Enabled;
            Notify("Noclip", Noclip.Enabled);
        }

        if (Pressed(ToggleGodMode))
        {
            Cheats.Invincible = !Cheats.Invincible;
            Notify("Invincibility", Cheats.Invincible);
        }

        if (Pressed(SaveBookmark)) Warp.Save(0);
        if (Pressed(LoadBookmark)) Warp.Restore(0);
        if (Pressed(SnapToFloor)) Noclip.SnapToFloor();
        if (Pressed(ReturnToEntry)) Noclip.ReturnToEntry();

        // Film mode: all three on, or all three off when they already are. No
        // toast either way -- it is for filming, and one would land in the shot.
        if (Pressed(FilmMode))
        {
            bool on = !(Noclip.Cinematic && Cheats.Peaceful && Noclip.Enabled);
            Noclip.Cinematic = on;
            Cheats.Peaceful = on;
            Noclip.Enabled = on;
        }
    }

    /// <summary>
    /// A toast, so a hotkey press is visible without opening the panel. There is
    /// no way to draw text into the emulated 320x240 picture short of building
    /// GPU primitives, so this sits on top of it instead.
    /// </summary>
    static void Notify(string what, bool on)
    {
        // The cinematic camera is for filming a flythrough: nothing may fade in
        // over the picture while it is on, and that includes these.
        if (Noclip.Cinematic) return;
        ToastNotifications.ShowText("Debug Tools", $"{what} {(on ? "on" : "off")}");
    }

    internal static void Reset()
    {
        _held.Clear();
        _padHeld.Clear();
    }
}
