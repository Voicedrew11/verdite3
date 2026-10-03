using System.Reflection;
using ImGuiNET;
using RecompOne.Runtime.Context;
using RecompOne.Runtime.Memory;
using RecompOne.Runtime.Modding;

namespace Kf3;

/// <summary>
/// The mouse and the sticks spent through the game's own turn and look routine,
/// func_8002F5C0 (stage 4, once a world tick under pacing). Each axis is
/// Verdite2's three branches: a held button adds <c>accel</c> to a velocity and
/// clamps it, no button decays it by <c>accel</c> and applies what is left
/// unclamped, then the base angle takes <c>(angle + vel) &amp; 0xFFF</c>. So
/// writing <c>step ± accel</c> into the velocity with the axis's buttons masked
/// out lands on <c>step</c> exactly, through the game's own pitch limit.
///
///     yaw    vel 0x801B264C, accel rate>>2 (rate s16 0x801B2668), base 0x801B2612
///     pitch  vel 0x801B264E, accel 3, limit ±0x2BC (0xD44 at 12 bits), base 0x801B2610
///
/// L2 and R2 held together recentre the pitch, so a pitch step masks both. The
/// pad word is put back after the call. A tick the mouse drove is followed by a
/// zero velocity, so the view does not coast on the game's decay when the hand
/// stops. See "Mouse look" in docs/INPUT.md.
///
/// **The sticks ride the same hook.** <see cref="Analog.BeforeLook"/> is handed
/// the mouse's whole steps here and returns the pad word to run the routine with,
/// because the mouse and the sticks are two ways of asking for the same per-frame
/// step; one routine deciding it is what keeps them from writing the same
/// velocity twice. The mouse path is unchanged when the sticks are centred.
/// </summary>
public static class MouseLook
{
    /// <summary>This game's values for Verdite.Core.Mouse: the look routine's
    /// units, limits and base angles, and where the pointer's absence is noticed.</summary>
    public static readonly MouseGame Game = new(
        UnitsPerDegree: 4096f / 360f,   // 12 bits to yaw's circle
        DegreesPerPixel: 0.15f,         // a quarter turn is about 600 px at sensitivity 1
        StepCap: 1024,                  // the most one tick may turn, in yaw units
        PitchLimit: 0x2BC,              // func_8002F5C0's limit, 0x2BC and 0xD44 at 12 bits
        YawAddress: 0x801B2612,         // u16, the base yaw the look routine accumulates
        PitchAddress: 0x801B2610,       // u16, the base pitch, a 12-bit angle
        DefaultLeftButton: 3,           // Square: attack (a New Game's preset 3)
        DefaultRightButton: 4,          // Triangle: magic
        DefaultMiddleButton: 1,         // Cross: examine, open, talk
        TextEditing: () => ImGui.GetCurrentContext() != nint.Zero && ImGui.GetIO().WantTextInput,
        Frames: () => FramePacing.Frames,
        LogicHz: () => FramePacing.LogicHz);

    const uint Routine = 0x8002F5C0;
    const uint Pad = 0x801B265C;            // u16, the pad word stage 4 tests

    static readonly ModInfo _self = new() { Id = "kf3.mouselook", Name = "Mouse look", Version = "1.0" };
    static bool _queued;
    static float _fracTurn, _fracPitch;

    /// <summary>The guest memory, for readers outside a hook (the view's lead).</summary>
    internal static IMemory? Memory { get; private set; }

    public static void Install() => HookAttach.OnOverlayLoad("mouse look", Attach);

    static bool Attach()
    {
        SymbolRegistry.Build();
        var target = SymbolRegistry.Resolve("game", null, Routine);
        if (target == null) return false;
        if (!_queued)
        {
            var impl = typeof(MouseLook).GetMethod(nameof(Replace), BindingFlags.NonPublic | BindingFlags.Static)!;
            _queued = HookManager.AddReplace(_self, target, impl);
            if (!_queued) return false;
        }
        HookManager.Commit();
        bool ok = HookAttach.Installed(target);
        Console.WriteLine(ok ? "[KF3] mouse look: attached" : "[KF3] mouse look: not installed");
        return ok;
    }

    static void Replace(Action<CpuContext, IMemory> orig, CpuContext c, IMemory m)
    {
        Memory = m;
        var (turn, pitch) = Mouse.TakeLook();
        bool mouse = Mouse.Enabled && Mouse.Captured;
        if (!mouse) _fracTurn = _fracPitch = 0f;

        int yaw = mouse ? Whole(turn, ref _fracTurn) : 0;
        int look = mouse ? Whole(pitch, ref _fracPitch) : 0;

        // Analog owns the stick share and the shared pad word; with the sticks
        // centred this is the mouse-only path it always was.
        ushort pad = m.ReadU16(Pad);
        ushort held = Analog.BeforeLook(m, pad, turn, pitch, yaw, look, mouse);
        if (held != pad) m.WriteU16(Pad, held);
        orig(c, m);
        if (held != pad) m.WriteU16(Pad, pad);
    }

    /// <summary>The whole units of a step, carrying the fraction to the next tick,
    /// so a slow hand still turns.</summary>
    static int Whole(float step, ref float carry)
    {
        float v = step + carry;
        int whole = (int)MathF.Truncate(v);
        whole = Math.Clamp(whole, -Mouse.StepCap, Mouse.StepCap);
        carry = whole == (int)MathF.Truncate(v) ? v - whole : 0f;
        return whole;
    }
}
