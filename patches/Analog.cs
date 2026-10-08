using System.Reflection;
using RecompOne.Runtime.Context;
using RecompOne.Runtime.Events;
using RecompOne.Runtime.Hardware;
using RecompOne.Runtime.Memory;
using RecompOne.Runtime.Modding;

namespace Kf3;

/// <summary>
/// Analog camera and movement, bound as a modern twin-stick scheme.
///
///     KF3_ANALOG=1              on (the default); 0 hands the sticks back
///     KF3_ANALOG_LOOK=1         right stick turns and looks
///     KF3_ANALOG_MOVE_ENABLE=1  left stick walks and strafes
///     KF3_ANALOG_TURN/PITCH/MOVE=1.0            sensitivities
///     KF3_ANALOG_DEADZONE/MOVEDEADZONE=0.15     deadzones
///     KF3_ANALOG_CURVE/MOVECURVE=1.35/1.0       response curves
///     KF3_ANALOG_ACCEL=1 ACCELMAX=2.2 ACCELTIME=0.5   the look ramp
///     KF3_ANALOG_INSTANTSTOP=1                  camera stops on release
///     KF3_ANALOG_INVERTY/INVERTTURN/INVERTSTRAFE/INVERTFWD=1
///     KF3_ANALOG_PROBE=1        the control-state report; see AnalogProbe
///
/// King's Field is a digital-pad game: one 16-bit button word a frame and a
/// fixed step for everything. The port has had real sticks all along --
/// InputManager fills Controller.LeftX/LeftY/RightX/RightY -- but the game asks
/// the BIOS for PAD_dr and sees a digital pad, so nothing consumed them; the
/// runtime's default binding just wires the left stick to the D-pad, which means
/// the stick *turns* at the game's fixed rate.
///
/// What makes real analog cheap here is the shape of the game's own control
/// code. Turning, looking and walking are all velocity based, and every one of
/// them has the same three-branch form (read out of the emitted C#, see the
/// evidence block below):
///
///     if      (pad &amp; maskInc)  vel += accel, clamp to plus or minus rate
///     else if (pad &amp; maskDec)  vel -= accel, clamp
///     else                     vel decays toward 0 and snaps
///     angle_or_position += vel
///
/// So the patch does not replace the position. It pre-loads the velocity word
/// with `target - accel` and asserts the matching button in the game's own pad
/// global; the game's next instruction adds `accel` and lands exactly on
/// `target`, then applies it through its own path -- collision, the pitch limit,
/// the walk normalisation, footsteps and animation all run as before, on an
/// amount the stick chose.
///
/// The buttons are read out of the game's action-mask table at 0x80081868..80
/// rather than hardcoded, so it follows whatever the player set in the game's own
/// control-config screen.
///
/// The left stick also leaks into turning, because the runtime binds it to the
/// D-pad and the D-pad *is* the turn control. <see cref="BeforeLook"/> therefore
/// takes the turn bits away from it whenever the left stick is deflected, and
/// leaves them alone when both sticks are centred -- so the D-pad still plays
/// exactly as it did.
///
/// The look hook is shared with <see cref="MouseLook"/>, which owns the one
/// replace hook on func_8002F5C0: the mouse and the sticks are two ways of asking
/// for the same per-frame step, and one routine deciding it is what keeps them
/// from writing the same word twice. The walk hook is this class's own replace on
/// func_8002F9BC.
///
/// Sticks idle == patch idle: the hooks return before touching anything, so the
/// D-pad, keyboard and mouse behave exactly as they do with <see cref="Enabled"/>
/// off. That is also why this can default to *on*: a keyboard player never
/// reaches the stick code, and a pad player without it has a left stick wired to
/// the D-pad, which in this game turns rather than walks. It lives in `patches/`
/// rather than `mods/` for the reason under "What belongs in a mod, and what does
/// not": working sticks on a modern pad are not a taste a player should have to
/// find a package to fix.
/// </summary>
public static class Analog
{
    // ---- the evidence, read from generated/game.cs (2026-10-03) --------------
    //
    // func_8002F9BC (walk) begins by reading the pad at 0x801B265C and the u32
    // walk max at 0x801B2664 (line ~36122; 200 = 0xC8, re-derived in stage 4).
    // Both axes have KF2's three branches, with accel = max >> 2 in the button
    // branches:
    //
    //   forward   vel 0x801B2648 (s16)
    //     Up   0x80081868 -> vel += max>>2, clamp to +max         (lines 36122..38)
    //     Down 0x8008186A -> vel -= max>>2, clamp to -max         (lines 36147..70)
    //     neither         -> decay by max>>3, snap to 0 past it   (lines 36172..206)
    //   strafe    vel 0x801B2646 (s16)
    //     R1   0x8008187C -> vel += max>>2, clamp to +max         (lines 36217..33)
    //     L1   0x80081878 -> vel -= max>>2, clamp to -max         (lines 36242..65)
    //     neither         -> decay by max>>2, snap to 0 past it   (lines 36267..301)
    //
    // So +forward is Up and +strafe is R1 (right), and each axis reads only its
    // two direction masks -- func_8002F9BC never touches the turn masks. That is
    // why nothing objectionable happens when the left stick, wired to the D-pad,
    // is presented as an analog strafe/forward: the walk routine ignores the
    // turn bits, and <see cref="BeforeLook"/> owns them.
    //
    // The walk's two decay steps differ (forward max>>3, strafe max>>2), which is
    // the game's own momentum and only matters on release; this patch never takes
    // the neither branch for walk, because <see cref="Step"/> clamps the target to
    // max and <see cref="Drive"/> then always lands in a button branch. So there
    // is no overspeed path for walk -- it walks no faster than the D-pad, which is
    // right: the overspeed trick is a camera affordance.
    //
    // func_8002F5C0 (look) has the same shape at 0x801B264C (yaw, accel rate>>2,
    // rate s16 0x801B2668; Left 0x8008186C increases, Right 0x8008186E decreases)
    // and 0x801B264E (pitch, accel 3, clamp 32; R2 0x8008187E adds, L2 0x8008187A
    // subtracts, both recentre). Its neither branch decays by the same accel and
    // snaps to 0 (yaw ~35854, pitch ~36017), so writing target+accel with no
    // button asserted lands on target however large it is: the overspeed path
    // holds for both look axes and is what the mouse and a fast stick use.

    // ---- the game's per-frame control state (GAME.EXE, all off 0x801B0000) ----
    const uint Pad        = 0x801B265C;   // u16, the word PadRead stored this frame; active high
    const uint StrafeVel  = 0x801B2646;   // s16, + moves the R1 strafe (right)
    const uint FwdVel     = 0x801B2648;   // s16, + moves the Up walk (forward)
    const uint TurnVel    = 0x801B264C;   // s16, added to yaw each frame
    const uint PitchVel   = 0x801B264E;   // s16, added to pitch each frame
    const uint MoveSpeed  = 0x801B2664;   // s32, this frame's walk speed  (0xC8 = 200)
    const uint TurnRate   = 0x801B2668;   // s16, this frame's turn rate   (32 / 40)

    // The view angles, for the probe. base pitch/yaw/roll; the composed triple the
    // renderer reads is at 0x801B2608/0A/0C.
    internal const uint Pitch = 0x801B2610;
    internal const uint Yaw   = 0x801B2612;

    // ---- the action -> button mask table (GAME.EXE .data, u16 entries) -------
    // Named for what the branch does to the velocity, not for a direction: which
    // way "+" points on screen is a convention, and the invert toggles settle it.
    internal const uint MaskTurnInc   = 0x8008186C;   // Left
    internal const uint MaskTurnDec   = 0x8008186E;   // Right
    internal const uint MaskPitchInc  = 0x8008187E;   // R2
    internal const uint MaskPitchDec  = 0x8008187A;   // L2
    internal const uint MaskFwdInc    = 0x80081868;   // Up
    internal const uint MaskFwdDec    = 0x8008186A;   // Down
    internal const uint MaskStrafeInc = 0x8008187C;   // R1
    internal const uint MaskStrafeDec = 0x80081878;   // L1

    // Walking and strafing: the one routine out of stage 4 this class replaces.
    // The look routine func_8002F5C0 is MouseLook's hook, which calls into here.
    const uint MoveRoutine = 0x8002F9BC;

    // The accelerations the game applies once we have asserted the button. Turn
    // and walk are a quarter of that frame's rate; pitch is a flat 3.
    const int PitchAccel = 3;
    const int PitchVelMax = 32;

    // How far past the game's own per-frame limit the camera may be driven. The
    // limit only exists in the two branches that read a button; the branch that
    // runs with neither button down applies the velocity unclamped, having first
    // decayed it by the same step. Four times is a ceiling to keep a bad
    // sensitivity from spinning the view.
    const int OverspeedCap = 4;

    // ---- the settings, and where they are kept between runs -------------------

    public const string OnKey          = "kf3.analog.on";
    public const string LookKey        = "kf3.analog.look";
    public const string MoveKey        = "kf3.analog.move";
    public const string StopKey        = "kf3.analog.instantstop";
    public const string AccelKey       = "kf3.analog.accel";
    public const string AccelMaxKey    = "kf3.analog.accelmax";
    public const string AccelTimeKey   = "kf3.analog.acceltime";
    public const string TurnSensKey    = "kf3.analog.turn";
    public const string PitchSensKey   = "kf3.analog.pitch";
    public const string MoveSensKey    = "kf3.analog.movesens";
    public const string LookDeadKey    = "kf3.analog.lookdeadzone";
    public const string MoveDeadKey    = "kf3.analog.movedeadzone";
    public const string LookCurveKey   = "kf3.analog.lookcurve";
    public const string MoveCurveKey   = "kf3.analog.movecurve";
    public const string InvertPitchKey = "kf3.analog.invertpitch";
    public const string InvertTurnKey  = "kf3.analog.invertturn";
    public const string InvertStrafeKey = "kf3.analog.invertstrafe";
    public const string InvertFwdKey   = "kf3.analog.invertforward";

    /// <summary>Live rather than fixed at startup: the hooks stay attached and
    /// return immediately when this is off, so it can be taken back mid-session.</summary>
    public static bool Enabled = true;
    public static bool AnalogLook = true;
    public static bool AnalogMove = true;

    // Look acceleration: hold the stick out and the camera keeps speeding up for
    // the first half second, instead of sitting at one rate the moment you touch
    // it. This is what a modern shooter's stick does and what the game, built for
    // a d-pad that is either down or not, has no notion of. Fine aim near centre
    // stays fine -- the ramp only starts past LookAccelThreshold -- and it falls
    // away three times faster than it builds, so easing off is immediate.
    public static bool LookAccel = true;
    public static float LookAccelMax = 2.2f;
    public static float LookAccelTime = 0.5f;
    const float LookAccelThreshold = 0.8f;
    static float _accelT;
    static long _accelTick;

    public static float LookDeadzone = 0.15f;
    public static float MoveDeadzone = 0.15f;
    // Nearly linear. A steeper curve plus a hard speed cap is what "stiff" is:
    // slow to get going and never fast.
    public static float LookCurve = 1.35f;
    public static float MoveCurve = 1.0f;
    public static float TurnSens = 1.25f;   // the user's, 2026-10-06
    public static float PitchSens = 1.25f;
    public static float MoveSens = 1.0f;

    public static bool InvertTurn;
    public static bool InvertPitch;
    public static bool InvertStrafe;
    public static bool InvertForward;

    // The camera stops when the stick is released rather than coasting.
    //
    // The game ramps a released velocity down instead of dropping it -- pitch by
    // 3 a frame from a limit of 32, so a released stick keeps looking for about
    // eleven frames. That is fine for a held button, which cannot be released
    // halfway, and wrong for a stick: it reads as inertia on the look axis.
    //
    // Movement is deliberately *not* included. Its ramp-down is the walking
    // momentum the game has always had, and stopping the player dead would be a
    // change to how the game plays rather than to how the stick reads.
    public static bool CameraInstantStop = true;

    // Which camera axes the sticks were driving last frame, so the release can be
    // handed back to the D-pad and L2/R2 after exactly one zeroing frame, and the
    // same for the mouse -- kept apart because the mouse's release does not go
    // through <see cref="CameraInstantStop"/>.
    static bool _ownedTurn, _ownedPitch;
    static bool _mouseTurn, _mousePitch;

    // Fractional remainders. Not optional: at a 15 Hz tick a small stick
    // deflection rounds to a zero step every tick, and the player would simply
    // not move.
    static float _turnCarry, _pitchCarry, _fwdCarry, _strafeCarry;

    /// <summary>Keys a KF3_ANALOG* variable set, which the saved settings must
    /// not overwrite -- the same precedence the other patches keep.</summary>
    static readonly HashSet<string> _fromEnv = new(StringComparer.Ordinal);

    // HookManager attributes hooks to a mod so they can be removed again. This is
    // in-project rather than a loaded package, so it declares its own identity.
    static readonly ModInfo _self = new()
    {
        Id = "kf3.analog",
        Name = "Analog twin-stick control",
        Version = "1.0",
        Description = "Drives the game's own control velocities from the sticks.",
    };

    // ---- diagnostics, read by the separate AnalogProbe class -----------------
    //
    // The probe (patches/AnalogProbe.cs, KF3_ANALOG_PROBE) reads these rather than
    // hooking anything itself; this class updates them where KF2's Analog called
    // AnalogProbe.NoteLook/NoteMove/NoteMouse.

    /// <summary>The last shaped right stick and the rate it drove.</summary>
    public static (float X, float Y, int Rate) LastLook;
    /// <summary>The last shaped left stick and the speed it drove.</summary>
    public static (float X, float Y, int Speed) LastMove;
    /// <summary>The last mouse step the look routine spent, in yaw units.</summary>
    public static (float Turn, float Pitch) LastMouse;
    /// <summary>Ticks the sticks drove the look / walk routine.</summary>
    public static long LookFrames, MoveFrames;

    static bool _queued;

    /// <summary>
    /// The environment variables, read before anything else. This one reads them
    /// itself rather than being handed their values by Program.cs: there are
    /// eighteen of them, and eighteen string parameters would say less about
    /// which variable sets what than the table above does.
    /// </summary>
    public static void Configure()
    {
        Env("KF3_ANALOG", OnKey, ref Enabled);
        Env("KF3_ANALOG_LOOK", LookKey, ref AnalogLook);
        Env("KF3_ANALOG_MOVE_ENABLE", MoveKey, ref AnalogMove);
        Env("KF3_ANALOG_INVERTY", InvertPitchKey, ref InvertPitch);
        Env("KF3_ANALOG_INVERTTURN", InvertTurnKey, ref InvertTurn);
        Env("KF3_ANALOG_INVERTSTRAFE", InvertStrafeKey, ref InvertStrafe);
        Env("KF3_ANALOG_INVERTFWD", InvertFwdKey, ref InvertForward);

        // One deadzone variable for both sticks, since wanting different ones is
        // the rarer case; MOVEDEADZONE below is how you say so.
        Env("KF3_ANALOG_DEADZONE", LookDeadKey, ref LookDeadzone);
        if (_fromEnv.Contains(LookDeadKey))
        {
            MoveDeadzone = LookDeadzone;
            _fromEnv.Add(MoveDeadKey);
        }
        Env("KF3_ANALOG_MOVEDEADZONE", MoveDeadKey, ref MoveDeadzone);

        Env("KF3_ANALOG_CURVE", LookCurveKey, ref LookCurve);
        Env("KF3_ANALOG_MOVECURVE", MoveCurveKey, ref MoveCurve);
        Env("KF3_ANALOG_TURN", TurnSensKey, ref TurnSens);
        Env("KF3_ANALOG_PITCH", PitchSensKey, ref PitchSens);
        Env("KF3_ANALOG_MOVE", MoveSensKey, ref MoveSens);
        Env("KF3_ANALOG_INSTANTSTOP", StopKey, ref CameraInstantStop);
        Env("KF3_ANALOG_ACCEL", AccelKey, ref LookAccel);
        Env("KF3_ANALOG_ACCELMAX", AccelMaxKey, ref LookAccelMax);
        Env("KF3_ANALOG_ACCELTIME", AccelTimeKey, ref LookAccelTime);
    }

    /// <summary>
    /// Attach the walk hook, and read the saved settings once the config is up.
    /// The walk routine is resolved on the first overlay load, the earliest moment
    /// every overlay is resolvable; the look half is MouseLook's hook, which calls
    /// <see cref="BeforeLook"/>.
    /// </summary>
    public static void Install()
    {
        // The saved choices can only be read once ConfigManager has loaded, which
        // happens inside HostWindow.Initialize -- after Program.cs called Configure.
        Event.AddListener<RuntimeReadyEvent>(_ =>
        {
            Saved(OnKey, ref Enabled);
            Saved(LookKey, ref AnalogLook);
            Saved(MoveKey, ref AnalogMove);
            Saved(StopKey, ref CameraInstantStop);
            Saved(AccelKey, ref LookAccel);
            Saved(AccelMaxKey, ref LookAccelMax);
            Saved(AccelTimeKey, ref LookAccelTime);
            Saved(TurnSensKey, ref TurnSens);
            Saved(PitchSensKey, ref PitchSens);
            Saved(MoveSensKey, ref MoveSens);
            Saved(LookDeadKey, ref LookDeadzone);
            Saved(MoveDeadKey, ref MoveDeadzone);
            Saved(LookCurveKey, ref LookCurve);
            Saved(MoveCurveKey, ref MoveCurve);
            Saved(InvertPitchKey, ref InvertPitch);
            Saved(InvertTurnKey, ref InvertTurn);
            Saved(InvertStrafeKey, ref InvertStrafe);
            Saved(InvertFwdKey, ref InvertForward);
        });

        HookAttach.OnOverlayLoad("analog", Attach);
    }

    static bool Attach()
    {
        SymbolRegistry.Build();
        var target = SymbolRegistry.Resolve("game", null, MoveRoutine);
        if (target == null)
        {
            Console.Error.WriteLine($"[KF3] analog: no function at game/0x{MoveRoutine:X8}; " +
                                    "the sticks will only turn and look, not walk");
            return false;
        }
        if (!_queued)
        {
            var impl = typeof(Analog).GetMethod(nameof(ReplaceMove), BindingFlags.NonPublic | BindingFlags.Static)!;
            _queued = HookManager.AddReplace(_self, target, impl);
            if (!_queued) return false;
        }
        HookManager.Commit();
        bool ok = HookAttach.Installed(target);
        Console.WriteLine(ok
            ? $"[KF3] analog: {(Enabled ? "on" : "off")}, 1 hook(s) " +
              $"(deadzone {LookDeadzone:0.##}, turn x{TurnSens:0.##}, move x{MoveSens:0.##}); " +
              "left stick walks and strafes, right stick turns and looks through the mouse " +
              "look hook, and the D-pad is untouched while both are centred"
            : "[KF3] analog: walk hook not installed");
        return ok;
    }

    // Turning and looking. func_8002F5C0 reads the pad, accumulates turn and
    // pitch velocity, then does `yaw = (yaw + turnVel) & 0xFFF` and the same for
    // pitch with a +-0x2BC limit -- so setting the velocity here is the whole job.
    //
    // MouseLook.Replace owns the one hook on that routine and hands the mouse's
    // whole steps in; this class owns the stick share and the shared word, so the
    // two devices cannot fight over it. It runs with the sticks switched off too:
    // keyboard and mouse is a scheme of its own.
    //
    // The gyro (GyroAim, a drawn bow) is a third way of asking: an amount, as the
    // mouse's is, spent beside the stick's share so it is smoothed as the stick is.
    public static ushort BeforeLook(IMemory m, ushort pad, float mouseTurn, float mousePitch,
                                    int yaw, int look, bool mouseActive, float gyroTurn = 0f, float gyroPitch = 0f)
    {
        bool sticks = Enabled && AnalogLook;
        bool gyro = gyroTurn != 0f || gyroPitch != 0f;

        // The accumulator has to be emptied by MouseLook whether or not anything
        // here spends it; this early-out is the sticks-idle fast path.
        if (!sticks && !gyro && yaw == 0 && look == 0 && !_mouseTurn && !_mousePitch) return pad;

        var (x, y) = sticks ? Shape(Controller.RightX, Controller.RightY, LookDeadzone, LookCurve)
                            : (0f, 0f);

        // The runtime binds the left stick to the D-pad by default, and the game's
        // turn actions *are* D-pad left/right -- so a left stick pushed sideways
        // turns as well as strafes unless the turn bits are taken away from it.
        // Owning them with a zero step is exactly that: buttons cleared, velocity
        // zeroed, and the D-pad still turns when neither stick is deflected.
        var (lx, ly) = sticks ? Shape(Controller.LeftX, Controller.LeftY, MoveDeadzone, MoveCurve)
                              : (0f, 0f);
        bool leftActive = sticks && AnalogMove && (lx != 0f || ly != 0f);

        // A released axis still needs one frame to stop the velocity the game
        // would otherwise ramp down; _ownedTurn/_ownedPitch are what keep us in
        // the hook for that frame and out of it afterwards.
        //
        // For the mouse that frame is not optional. A stick can be *held* still at
        // the centre, so leaving the ramp-down alone is a defensible feel and an
        // option; a mouse that has stopped moving has said nothing at all, and a
        // camera that carries on afterwards is simply wrong. Hence the separate
        // flags, which ignore CameraInstantStop.
        bool releaseTurn  = (CameraInstantStop && _ownedTurn)  || _mouseTurn;
        bool releasePitch = (CameraInstantStop && _ownedPitch) || _mousePitch;

        if (x == 0f && y == 0f && !leftActive && yaw == 0 && look == 0 && !gyro &&
            !releaseTurn && !releasePitch)
        {
            _accelT = 0f;
            _accelTick = 0;
            return pad;
        }

        // The ramp is a held stick's, so it is neither fed nor applied by a mouse.
        float mult = sticks ? Accelerate(RawMag(Controller.RightX, Controller.RightY, LookDeadzone)) : 1f;

        int rate = (short)m.ReadU16(TurnRate);
        if (rate <= 0) return pad;

        // The stick's share of each step, so FrameSmoothing can tell how much of
        // what the game turns by this tick was the mouse's (Mouse.NoteSpent).
        //
        // The yaw sign is read: stick right is +x and turning right is yaw
        // *decreasing* (Left 0x8008186C increases, Right 0x8008186E decreases),
        // hence the negation, and Mouse.TakeLook already uses that convention.
        //
        // The pitch sign is unconfirmed by eye -- the mask table names R2/L2 but
        // not which way the view tips -- so this follows Mouse.Poll, which feeds
        // +y the same velocity word that stick-down (+y) does: the stick and the
        // mouse agree, and the R2-adding branch is what both select. Whether that
        // is "down on screen" is in the not-yet-judged list in docs/INPUT.md.
        float stickTurn  = -x * rate * TurnSens * mult * (InvertTurn ? -1f : 1f);
        float stickPitch =  y * PitchVelMax * PitchSens * mult * (InvertPitch ? -1f : 1f);
        stickTurn += gyroTurn;
        stickPitch += gyroPitch;
        Mouse.NoteSpent(m, yaw, stickTurn, look, stickPitch);

        // A gyro step is an amount, so it gets the mouse's ceiling and the mouse's
        // stop: a hand that has stopped turning the pad has asked for nothing more.
        if (x != 0f || leftActive || yaw != 0 || gyroTurn != 0f || releaseTurn)
        {
            int step = Step(stickTurn + yaw, ref _turnCarry,
                            Ceiling(rate * OverspeedCap, yaw != 0 || gyroTurn != 0f));
            pad = Drive(m, pad, TurnVel, step, rate >> 2, rate, MaskTurnInc, MaskTurnDec);
            _ownedTurn = step != 0;
            _mouseTurn = yaw != 0 || gyroTurn != 0f;
        }

        if (y != 0f || look != 0 || gyroPitch != 0f || releasePitch)
        {
            int step = Step(stickPitch + look, ref _pitchCarry,
                            Ceiling(PitchVelMax * OverspeedCap, look != 0 || gyroPitch != 0f));
            pad = Drive(m, pad, PitchVel, step, PitchAccel, PitchVelMax, MaskPitchInc, MaskPitchDec);
            _ownedPitch = step != 0;
            _mousePitch = look != 0 || gyroPitch != 0f;
        }

        if (x != 0f || y != 0f) { LastLook = (x, y, rate); LookFrames++; }
        if (mouseActive && (mouseTurn != 0f || mousePitch != 0f)) LastMouse = (mouseTurn, mousePitch);
        return pad;
    }

    /// <summary>
    /// The per-frame ceiling for an axis: the stick's, or the mouse's larger one
    /// while the mouse (or the gyro, which asks the same way) is driving.
    ///
    /// A stick asks for a *rate*, and four times the game's own is already faster
    /// than any button can turn. A mouse asks for an *amount*, and a flick that
    /// takes a tenth of a second arrives here as three or four very large frames,
    /// so the stick's ceiling would turn every fast turn into a slow one.
    /// </summary>
    static int Ceiling(int stick, bool amount) => amount ? Math.Max(stick, Mouse.StepCap) : stick;

    // Walking and strafing. The same three-branch shape, twice, off this frame's
    // walk speed; the two velocities are then turned into a heading off the yaw
    // and applied through the game's own collision path.
    static void ReplaceMove(Action<CpuContext, IMemory> orig, CpuContext c, IMemory m)
    {
        if (!Enabled || !AnalogMove) { orig(c, m); return; }

        var (x, y) = Shape(Controller.LeftX, Controller.LeftY, MoveDeadzone, MoveCurve);
        if (x == 0f && y == 0f) { orig(c, m); return; }

        int speed = (int)m.ReadU32(MoveSpeed);
        if (speed <= 0) { orig(c, m); return; }
        int accel = speed >> 2;

        ushort pad = m.ReadU16(Pad), held = pad;

        if (y != 0f)
        {
            int step = Step(-y * speed * MoveSens * (InvertForward ? -1f : 1f), ref _fwdCarry, speed);
            held = Drive(m, held, FwdVel, step, accel, speed, MaskFwdInc, MaskFwdDec);
        }

        if (x != 0f)
        {
            int step = Step(x * speed * MoveSens * (InvertStrafe ? -1f : 1f), ref _strafeCarry, speed);
            held = Drive(m, held, StrafeVel, step, accel, speed, MaskStrafeInc, MaskStrafeDec);
        }

        if (held != pad) m.WriteU16(Pad, held);
        LastMove = (x, y, speed);
        MoveFrames++;
        orig(c, m);
        if (held != pad) m.WriteU16(Pad, pad);
    }

    /// <summary>
    /// Pre-load a velocity word so the game's own accumulate lands on `step`, and
    /// assert the button that makes it take that branch. A zero step clears both
    /// buttons and zeroes the velocity, which is the game's idle state -- not its
    /// decay, because the stick is the authority while it is deflected.
    /// </summary>
    static ushort Drive(IMemory m, ushort pad, uint velAddr, int step, int accel, int clamp,
                        uint maskInc, uint maskDec)
    {
        ushort inc = m.ReadU16(maskInc);
        ushort dec = m.ReadU16(maskDec);
        pad = (ushort)(pad & ~(inc | dec));

        if (step == 0) { m.WriteU16(velAddr, 0); return pad; }

        if (Math.Abs(step) <= clamp)
        {
            // Inside the game's own limit: assert the button and let its
            // accumulate carry the pre-load up to the target.
            m.WriteU16(velAddr, (ushort)(short)(step > 0 ? step - accel : step + accel));
            pad |= step > 0 ? inc : dec;
        }
        else
        {
            // Past it. The limit lives only in the two button branches -- the
            // branch that runs with neither button down decays the velocity by
            // the same step (for look) and applies whatever is left, unclamped.
            // So pre-load the other way and assert nothing: the decay lands on
            // the target and the camera goes faster than any button could drive
            // it. Walk never reaches here: Step clamps to the walk max.
            m.WriteU16(velAddr, (ushort)(short)(step > 0 ? step + accel : step - accel));
        }

        return pad;
    }

    /// <summary>
    /// The look-speed multiplier for this frame: 1 at rest, rising to
    /// <see cref="LookAccelMax"/> over <see cref="LookAccelTime"/> while the stick
    /// is held past the threshold, and falling back three times faster than it
    /// built.
    /// </summary>
    static float Accelerate(float mag)
    {
        if (!LookAccel) { _accelT = 0f; return 1f; }

        long now = Environment.TickCount64;
        float dt = _accelTick == 0 ? 0f : Math.Clamp((now - _accelTick) / 1000f, 0f, 0.1f);
        _accelTick = now;

        _accelT = Math.Clamp(mag >= LookAccelThreshold ? _accelT + dt : _accelT - dt * 3f,
                             0f, LookAccelTime);

        float t = LookAccelTime <= 0f ? 1f : _accelT / LookAccelTime;
        return 1f + (LookAccelMax - 1f) * t;
    }

    /// <summary>
    /// A stick's deflection as 0..1 past the deadzone, before any curve. The
    /// acceleration ramp keys off this rather than the shaped value, so the curve
    /// and the ramp stay independent settings.
    /// </summary>
    static float RawMag(byte bx, byte by, float deadzone)
    {
        float x = (bx - 128) / 127f;
        float y = (by - 128) / 127f;
        float mag = MathF.Sqrt(x * x + y * y);
        return mag <= deadzone ? 0f : Math.Clamp((mag - deadzone) / (1f - deadzone), 0f, 1f);
    }

    /// <summary>
    /// Integer part of the wanted step, with the fraction carried to next frame
    /// and the result held inside the given ceiling.
    /// </summary>
    static int Step(float want, ref float carry, int limit)
    {
        float total = want + carry;
        int step = (int)MathF.Truncate(total);
        carry = total - step;
        return Math.Clamp(step, -limit, limit);
    }

    /// <summary>
    /// One stick, as a radial-deadzoned and curved vector. The bytes are the
    /// runtime's own 0..255 with 0x80 centre (InputManager.AxisToByte, which
    /// already applies a 1.3x gain, so the byte saturates a little before the
    /// stick does).
    /// </summary>
    static (float X, float Y) Shape(byte bx, byte by, float deadzone, float curve)
    {
        float x = (bx - 128) / 127f;
        float y = (by - 128) / 127f;
        float mag = MathF.Sqrt(x * x + y * y);
        if (mag <= deadzone || mag <= 0f) return (0f, 0f);

        float unit = Math.Clamp((mag - deadzone) / (1f - deadzone), 0f, 1f);
        float scaled = MathF.Pow(unit, curve) / mag;
        return (x * scaled, y * scaled);
    }

    /// <summary>
    /// The two sticks as the hooks see them, raw and before any deadzone: the
    /// settings page draws this so a deadzone can be set against the pad in hand
    /// rather than by guessing at where its centre rests.
    /// </summary>
    /// <summary>The right stick shaped as the look shapes it, for a turn that is not
    /// the view's (ItemTurn).</summary>
    internal static (float X, float Y) RightStick =>
        Shape(Controller.RightX, Controller.RightY, LookDeadzone, LookCurve);

    public static (float Lx, float Ly, float Rx, float Ry) Sticks => (
        (Controller.LeftX - 128) / 127f, (Controller.LeftY - 128) / 127f,
        (Controller.RightX - 128) / 127f, (Controller.RightY - 128) / 127f);

    // ---- environment, then interface.ini ------------------------------------

    // The set is a parameter rather than always this class's own, because
    // <see cref="MouseLook"/> keeps the same precedence and there is no reason for
    // two copies of the rule -- only for two records of which variables were set.

    internal static void Env(string name, string key, ref bool value) => Env(name, key, ref value, _fromEnv);

    internal static void Env(string name, string key, ref float value) => Env(name, key, ref value, _fromEnv);

    internal static void Env(string name, string key, ref bool value, HashSet<string> from)
        => Kept.Env(name, key, ref value, from);

    internal static void Env(string name, string key, ref float value, HashSet<string> from)
        => Kept.Env(name, key, ref value, from);

    /// <summary>The saved value, unless the environment already set this key.</summary>
    internal static void Saved(string key, ref bool value) => Saved(key, ref value, _fromEnv);

    internal static void Saved(string key, ref float value) => Saved(key, ref value, _fromEnv);

    internal static void Saved(string key, ref bool value, HashSet<string> from)
        => Kept.Saved(key, ref value, from);

    internal static void Saved(string key, ref float value, HashSet<string> from)
        => Kept.Saved(key, ref value, from);

    internal static void Saved(string key, ref int value, HashSet<string> from)
        => Kept.Saved(key, ref value, from);
}
