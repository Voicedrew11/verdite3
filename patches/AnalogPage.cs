using ImGuiNET;
using Rt = RecompOne.Runtime.Runtime;

namespace Kf3;

/// <summary>
/// The twin-stick knobs, on Input's Gamepad tab. Ported from Verdite2's
/// Kf2.Settings.AnalogPage, with this game's field names and no probe.
///
/// Shaped rather than listed: the switches and the sensitivities are what a
/// player changes, and the deadzones, curves, acceleration ramp and inversions
/// live under *Fine tuning* because they are set once. The live stick readout is
/// the one thing here that is not a setting — deadzone is the setting people get
/// wrong, and the number that decides it is how far the pad in hand rests from
/// centre. See "The turn and look routine" in docs/INPUT.md.
/// </summary>
public static class AnalogPage
{
    public static void Draw()
    {
        Check("Twin-stick control", Analog.OnKey, ref Analog.Enabled,
              "Left stick walks and strafes, right stick turns and looks. The game's own movement " +
              "code still runs — this writes the per-frame velocities it would have accumulated " +
              "from held buttons, so collision, animation and speed limits are untouched and only " +
              "the amount is continuous. Off gives the sticks back to the pad bindings, where the " +
              "left stick is wired to the D-pad: up and down walk, left and right turn.");

        ImGui.BeginDisabled(!Analog.Enabled);

        Check("Right stick turns and looks", Analog.LookKey, ref Analog.AnalogLook);
        Check("Left stick walks and strafes", Analog.MoveKey, ref Analog.AnalogMove);

        ImGui.Spacing();
        Slider("Turn sensitivity", Analog.TurnSensKey, ref Analog.TurnSens, 0.1f, 3f,
               "Past 1.0 the camera is driven faster than any button on the pad can turn it: the " +
               "game's own per-frame limit only exists in the branches that read a button.");
        Slider("Look sensitivity", Analog.PitchSensKey, ref Analog.PitchSens, 0.1f, 3f);
        Slider("Move sensitivity", Analog.MoveSensKey, ref Analog.MoveSens, 0.1f, 1.5f,
               "Walking is the game's own speed at full deflection. Above 1.0 would ask for a walk " +
               "faster than it has an animation for.");

        if (ImGui.TreeNode("Fine tuning"))
        {
            Slider("Look deadzone", Analog.LookDeadKey, ref Analog.LookDeadzone, 0f, 0.5f,
                   "How far the stick must move before anything happens. Set it just past where " +
                   "the readout below rests with your hands off the pad.");
            Slider("Move deadzone", Analog.MoveDeadKey, ref Analog.MoveDeadzone, 0f, 0.5f);
            Slider("Look curve", Analog.LookCurveKey, ref Analog.LookCurve, 1f, 3f,
                   "1.0 is linear. Higher gives finer aim near centre and the same speed at the " +
                   "edge, at the cost of a slower response in between.");
            Slider("Move curve", Analog.MoveCurveKey, ref Analog.MoveCurve, 1f, 3f);

            ImGui.Spacing();
            Check("Look acceleration", Analog.AccelKey, ref Analog.LookAccel,
                  "Holding the stick out keeps speeding the camera up for the first half second, " +
                  "the way a modern shooter's does. Fine aim near centre is unaffected — the ramp " +
                  "only starts past 80% deflection.");
            ImGui.BeginDisabled(!Analog.LookAccel);
            Slider("Acceleration x", Analog.AccelMaxKey, ref Analog.LookAccelMax, 1f, 4f);
            Slider("Acceleration time (s)", Analog.AccelTimeKey, ref Analog.LookAccelTime, 0.1f, 2f);
            ImGui.EndDisabled();

            ImGui.Spacing();
            Check("Camera stops on release", Analog.StopKey, ref Analog.CameraInstantStop,
                  "The game ramps a released look velocity down over about a third of a second, " +
                  "which reads as inertia on a stick. Off restores that ramp. Walking momentum is " +
                  "the game's own and is not affected either way.");

            ImGui.Spacing();
            Note("Which way \"+\" points is the game's convention, not the port's — flip an axis " +
                 "here if it runs backwards.");
            // Look Y is Gameplay's "Inverted camera" (PortSettings.InvertedCamera).
            Check("Invert turn", Analog.InvertTurnKey, ref Analog.InvertTurn);
            Check("Invert strafe", Analog.InvertStrafeKey, ref Analog.InvertStrafe);
            ImGui.SameLine();
            Check("Invert forward", Analog.InvertFwdKey, ref Analog.InvertForward);

            ImGui.TreePop();
        }

        ImGui.EndDisabled();

        var (lx, ly, rx, ry) = Analog.Sticks;
        Note($"Sticks: L({lx:+0.00;-0.00;0.00}, {ly:+0.00;-0.00;0.00})  " +
             $"R({rx:+0.00;-0.00;0.00}, {ry:+0.00;-0.00;0.00})");
    }

    /// <summary>A checkbox that saves itself as 1/0, since interface.ini has no
    /// bool reader on the port's side.</summary>
    static void Check(string label, string key, ref bool value, string? tip = null)
    {
        if (ImGui.Checkbox(label, ref value)) Set(key, value);
        Tip(tip);
    }

    /// <summary>AlwaysClamp because ctrl+click turns a slider into a text box, and
    /// a sensitivity typed past the end of the range is one the widget can no
    /// longer show.</summary>
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

    static void Set(string key, float value)
    {
        Rt.View.SetFloat(key, value);
        Rt.SaveView();
    }

    static void Tip(string? tip)
    {
        if (tip != null && ImGui.IsItemHovered()) ImGui.SetTooltip(tip);
    }

    /// <summary>Wrapped and dimmed. TextDisabled does not wrap, and unwrapped prose
    /// runs straight out of the settings window.</summary>
    static void Note(string text)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]);
        ImGui.TextWrapped(text);
        ImGui.PopStyleColor();
    }
}
