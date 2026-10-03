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
/// locked right now and which key changes that: mouse look does nothing until it
/// is, so a player who switched it on and saw no change needs that sentence before
/// a sensitivity. And the **buttons** are named as pad buttons rather than as
/// actions, because the game's own control configuration decides what each does.
/// MenuMouse is Verdite2's and has no equivalent here.
///
/// See "Mouse look" in docs/INPUT.md.
/// </summary>
public static class MousePage
{
    public static void Draw()
    {
        bool was = Mouse.Enabled;
        Check("Mouse look", Mouse.OnKey, ref Mouse.Enabled,
              "Turns and looks with the mouse, through the same per-frame velocities the sticks " +
              "drive — so the game's own movement code, collision and pitch limit are untouched. " +
              "The pointer has to be captured before anything happens, and the mouse buttons only " +
              "reach the game while it is.");
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
             "Triangle (magic) and middle is Circle (examine); the game's own control configuration " +
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
                ? $"The pointer is captured — press {Mouse.CaptureKey} to get it back."
                : $"The pointer is free — press {Mouse.CaptureKey} with the game in front to capture it. " +
                  "Opening any of these settings gives it back on its own.");
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
        var names = keys.Select(k => k.ToString()).ToArray();
        int index = Array.IndexOf(keys, Mouse.CaptureKey);
        if (index < 0) index = 0;

        ImGui.SetNextItemWidth(160);
        if (ImGui.Combo("Capture key", ref index, names, names.Length))
        {
            Mouse.CaptureKey = keys[index];
            Set(Mouse.CaptureKeyKey, (int)Mouse.CaptureKey);
        }
        Tip("Locks the pointer to the window and hides it, and gives it back again. The list is " +
            "deliberately short — a key the game's controls or the port's shortcuts already use " +
            "would let someone lock themselves in.");
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
