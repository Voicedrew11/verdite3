using System.Reflection;
using RecompOne.Runtime.Context;
using RecompOne.Runtime.Memory;
using RecompOne.Runtime.Modding;

namespace Kf3;

/// <summary>
/// The mouse spent through the game's own turn and look routine, func_8002F5C0
/// (stage 4, once a world tick under pacing). Each axis is Verdite2's three
/// branches: a held button adds <c>accel</c> to a velocity and clamps it, no button
/// decays it by <c>accel</c> and applies what is left unclamped, then the base
/// angle takes <c>(angle + vel) &amp; 0xFFF</c>. So writing <c>step ± accel</c> into
/// the velocity with the axis's buttons masked out lands on <c>step</c> exactly,
/// through the game's own pitch limit.
///
///     yaw    vel 0x801B264C, accel rate>>2 (rate s16 0x801B2668), base 0x801B2612
///     pitch  vel 0x801B264E, accel 3, limit ±0x2BC (0xD44 at 12 bits), base 0x801B2610
///
/// L2 and R2 held together recentre the pitch, so a mouse pitch masks both. The
/// pad word is put back after the call. A tick the mouse drove is followed by a
/// zero velocity, so the view does not coast on the game's decay when the hand
/// stops. See "Mouse look" in docs/INPUT.md.
/// </summary>
public static class MouseLook
{
    const uint Routine = 0x8002F5C0;
    const uint Pad = 0x801B265C;            // u16, the pad word stage 4 tests
    const uint YawVel = 0x801B264C;
    const uint PitchVel = 0x801B264E;
    const uint TurnRate = 0x801B2668;       // s16, 32 walking, 40 standing
    const int PitchAccel = 3;

    // The action-mask table's turn and look entries (0x80081868 + 2i).
    const uint MaskLeft = 0x8008186C, MaskRight = 0x8008186E, MaskL2 = 0x8008187A, MaskR2 = 0x8008187E;

    static readonly ModInfo _self = new() { Id = "kf3.mouselook", Name = "Mouse look", Version = "1.0" };
    static bool _queued;
    static float _fracTurn, _fracPitch;
    static bool _droveYaw, _drovePitch;

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
        if (!Mouse.Enabled || !Mouse.Captured) { _fracTurn = _fracPitch = 0f; orig(c, m); return; }

        int yaw = Whole(turn, ref _fracTurn);
        int look = Whole(pitch, ref _fracPitch);
        ushort pad = m.ReadU16(Pad), held = pad;

        if (yaw != 0)
        {
            int accel = (short)m.ReadU16(TurnRate) >> 2;
            held &= (ushort)~(m.ReadU16(MaskLeft) | m.ReadU16(MaskRight));
            m.WriteU16(YawVel, (ushort)(yaw > 0 ? yaw + accel : yaw - accel));
            _droveYaw = true;
        }
        else if (_droveYaw)
        {
            m.WriteU16(YawVel, 0);
            _droveYaw = false;
        }

        if (look != 0)
        {
            held &= (ushort)~(m.ReadU16(MaskL2) | m.ReadU16(MaskR2));
            m.WriteU16(PitchVel, (ushort)(look > 0 ? look + PitchAccel : look - PitchAccel));
            _drovePitch = true;
        }
        else if (_drovePitch)
        {
            m.WriteU16(PitchVel, 0);
            _drovePitch = false;
        }

        Mouse.NoteSpent(m, yaw, 0f, look, 0f);
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
