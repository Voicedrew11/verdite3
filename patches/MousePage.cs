using ImGuiNET;
using RecompOne.Runtime.Host;
using Rt = RecompOne.Runtime.Runtime;

namespace Kf3;

/// <summary>
/// The mouse knobs, on Input's Mouse tab. Ported from Verdite2's
/// Kf2.Settings.MousePage, and from this port's old Gameplay tab, whose mouse
/// controls now live here.
///
/// Two things are not settings. The **capture line** says whether the pointer is
/// locked right now and how that changes: mouse look does nothing until it is, so
/// a player who switched it on and saw no change needs that sentence before a
/// sensitivity. And the **buttons** are named as pad buttons rather than as
/// actions, because the game's own control configuration decides what each does.
/// The menu pointer (<see cref="MenuMouse"/>) comes first and is not under mouse
/// look: it needs no captured pointer.
///
/// See "Mouse look" in docs/INPUT.md.
/// </summary>
public static class MousePage
{
    public static void Draw()
    {
        Check("Point at the menus", MenuMouse.OnKey, ref MenuMouse.Enabled,
              "Point at an item in the game's menus and the game's own cursor goes to it: left click " +
              "chooses, right click backs out, the wheel scrolls a long list. A click clear of the " +
              "menu backs out too. The menus give the pointer back, so this needs no mouse look.");
        ImGui.Spacing();

        bool was = Mouse.Enabled;
        Check("Mouse look", Mouse.OnKey, ref Mouse.Enabled,
              "Turns and looks with the mouse, through the same per-frame velocities the sticks " +
              "drive — so the game's own movement code, collision and pitch limit are untouched. " +
              "Click the game to capture the pointer; the mouse buttons only reach the game while " +
              "it is captured, and the click that captures is not a press.");
        if (was && !Mouse.Enabled) Mouse.SetCaptured(false);

        ImGui.BeginDisabled(!Mouse.Enabled);

        ImGui.SetNextItemWidth(200);
        Slider("Turn sensitivity", Mouse.TurnKey, ref Mouse.TurnSens, 0.1f, 5f,
               "At 1.0 a quarter turn takes about 600 pixels of movement. This is in window " +
               "pixels, so a larger window turns a little slower for the same movement of the hand.");
        ImGui.SetNextItemWidth(200);
        Slider("Look sensitivity", Mouse.LookKey, ref Mouse.LookSens, 0.1f, 5f);
        Check("Invert look Y", Mouse.InvertKey, ref Mouse.InvertY);

        ImGui.Spacing();
        Note("Mouse buttons press pad buttons. By default left is Square (attack), right is " +
             "Triangle (magic) and middle is Cross (examine); the game's own control configuration " +
             "decides what each button does and can reassign it.");
        Button("Left button", Mouse.LeftKey, ref Mouse.LeftButton);
        Button("Right button", Mouse.RightKey, ref Mouse.RightButton);
        Button("Middle button", Mouse.MiddleKey, ref Mouse.MiddleButton);

        ImGui.Spacing();
        CaptureKey();

        ImGui.EndDisabled();

        if (!HostWindow.MouseAvailable)
            Note("No mouse is attached to the window, so nothing here will do anything.");
        else if (!Mouse.Enabled)
            Note("Mouse look is off; the pointer stays a pointer.");
        else
            Note(Mouse.Captured
                ? "The pointer is captured — Escape opens the game's menu, which gives it back, " +
                  "and so does switching to another window."
                : "The pointer is free — click the game to capture it. The game's menu, these " +
                  "settings and switching to another window give it back; closing the game's menu " +
                  "captures it again.");
    }

    /// <summary>
    /// Which pad button a mouse button presses. "None" is index 0 and is a real
    /// choice — a player who wants the mouse for looking only.
    /// </summary>
    static void Button(string label, string key, ref int index)
    {
        var names = Mouse.PadButtons.Select(b => b.Name).ToArray();
        ImGui.SetNextItemWidth(160);
        if (ImGui.Combo(label, ref index, names, names.Length)) Set(key, index);
    }

    /// <summary>
    /// The capture key, as a short list rather than as a binding widget: the
    /// runtime's own "press a key" capture belongs to its pad-binding table and is
    /// internal to it, and a page that offered every key would let someone bind
    /// capture to a key the game already uses and lock themselves in.
    /// </summary>
    static void CaptureKey()
    {
        var keys = Mouse.CaptureKeys;
        var names = keys.Select(Mouse.KeyName).ToArray();
        int index = Array.IndexOf(keys, Mouse.CaptureKey);
        if (index < 0) index = 0;

        ImGui.SetNextItemWidth(160);
        if (ImGui.Combo("Capture key", ref index, names, names.Length))
        {
            Mouse.CaptureKey = keys[index];
            Set(Mouse.CaptureKeyKey, (int)Mouse.CaptureKey);
        }
        Tip("A key that locks the pointer and gives it back, besides the click. None by default: " +
            "Escape opens the game's menu, which already gives it back. The list is deliberately " +
            "short — a key the game's controls or the port's shortcuts already use would let " +
            "someone lock themselves in. Escape here takes it from the menu.");
    }

    static void Check(string label, string key, ref bool value, string? tip = null)
    {
        if (ImGui.Checkbox(label, ref value)) Set(key, value);
        Tip(tip);
    }

    static void Slider(string label, string key, ref float value, float min, float max, string? tip = null)
    {
        if (ImGui.SliderFloat(label, ref value, min, max, "%.2f", ImGuiSliderFlags.AlwaysClamp))
            Set(key, value);
        Tip(tip);
    }

    static void Set(string key, bool value)
    {
        Rt.View.SetInt(key, value ? 1 : 0);
        Rt.SaveView();
    }

    static void Set(string key, int value)
    {
        Rt.View.SetInt(key, value);
        Rt.SaveView();
    }

    static void Set(string key, float value)
    {
        Rt.View.SetFloat(key, value);
        Rt.SaveView();
    }

    static void Tip(string? tip)
    {
        if (tip != null && ImGui.IsItemHovered()) ImGui.SetTooltip(tip);
    }

    static void Note(string text)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]);
        ImGui.TextWrapped(text);
        ImGui.PopStyleColor();
    }
}
