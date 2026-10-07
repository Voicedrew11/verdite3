// ModCompiler compiles mods with no implicit usings, so every namespace the
// file needs must be named here -- including System.
using System;
using RecompOne.Runtime.Context;
using RecompOne.Runtime.Hardware;
using RecompOne.Runtime.Memory;
using RecompOne.Runtime.Modding;
using Recompiled;
// Upstream 0409bc2 emits one class per overlay, because CoreCLR caps a class
// at 65535 methods. Every func_ named here is GAME.EXE's, so the alias names the
// overlay once and the call sites below are unchanged.
using KingsField3 = Recompiled.KingsField3_game;

namespace Kf3.Mods.Debug;

/// <summary>
/// Noclip flight: fly through walls, with the body coming along.
///
/// Forward is where the *camera* points, pitch included, so looking down and
/// pushing forward descends; the speed is units a second against real elapsed
/// time, because the hook below runs on the world tick when the frame gate is
/// paced and at the render rate when it is not. Input comes from two places at
/// once -- the left stick, and the pad word's own direction bits, which the
/// keyboard fills through the player's own bindings -- so a keyboard, a pad and
/// a rebind all reach it without a second table.
///
/// The obvious implementation is to skip the collision queries -- stage 4's
/// func_8002E3F8 calls func_80033F38 with the player's position triple, radius
/// 0x320, height 0x6A4, and a [PreHook] returning false would stop it
/// answering. That is wrong. func_80033F38 (and the floor queries func_80033B10/
/// func_80033B8C and the map test func_80033D08 it dispatches to) has some
/// thirty call sites across the object and creature stages, so switching them
/// off drops every enemy and item through the floor along with the walls.
///
/// So the mod integrates its own position instead and writes the triple
/// directly, after the game's own movement has run. The vector is the game's
/// own, lifted from func_8002E3F8 -- the player's walk/collision integrator:
///
///     rsin (func_80076CC4) and its companion func_80076DA0, 1.12 fixed point
///     dX = (-sin(angle) * dist) >> 12
///     dZ = ( cos(angle) * dist) >> 12
///
/// which is why forward is -sin/+cos here and not the other way round. Angles
/// are 12-bit: 0x1000 is a full turn, and func_8002F5C0 masks yaw with 0xFFF
/// after every add.
///
/// ---- where the write goes ----
///
/// A [PostHook] on main-loop stage 4 (func_80030FCC) is the last word on the
/// player before the frame is drawn, and that is worth recording because it is
/// the one thing that could quietly break this.
///
/// Stage 4 runs before stage 10 (func_8002B330, which copies the player into the
/// camera args) and stage 15 (func_800422B8, the renderer) in the same main-loop
/// iteration, so a write made at the end of stage 4 reaches the camera and the
/// drawn frame that frame. The main loop is func_80014BD4; the stage-4 call is
/// at 0x80014F3C.
///
/// Nothing in stage 4 after the commit stores rewrites the triple, so the
/// post-hook wins. The writers of the position triple elsewhere are the area
/// loader, the map/door transition placement (func_8005C864 from stages 7/9) and
/// the respawn init (func_8002B468), all of which run outside the player stage;
/// while flying they are allowed to run and are then re-seeded (see Resync). The
/// stage-3 object stage func_80047010 can also place the player by an event tile
/// before stage 4, and a post-stage-4 write simply discards that placement the
/// same frame -- the desired "safely suppressed" event tile.
///
/// ---- nothing is skipped, and that is deliberate ----
///
/// Those routines are also the game's own bookkeeping -- the surface id, the
/// floor reference, the fall state -- and switching them off makes the engine
/// less consistent with itself, not more, for no gain once the mod keeps its own
/// authoritative position.
///
/// So they run, and this hook overwrites the result. Two consequences are
/// handled rather than avoided:
///
///   * The floor clamp writes Y every frame. That is why the flight integrates
///     from its OWN position rather than from whatever is in memory: reading
///     back a floor-snapped Y and adding to it would leave you hovering one
///     step above the ground instead of climbing.
///
///   * func_8002ED60 carries the landing/fall state and the vertical velocity.
///     Flight holds the fall velocity, the root-motion Y delta, the landing-dip
///     offset and the vertical state at zero so no landing is ever booked and no
///     fall damage comes due.
///
/// ---- the limit that is not a bug ----
///
/// Flying far enough leaves the area the game has loaded, and the renderer then
/// walks an entity table full of stale pointers and dies on an unmapped read.
/// That is not something a noclip can fix from outside: the neighbouring area's
/// module and data are simply not in RAM. Changing area is what the area warp is
/// for. The panel says so, and the entry position is kept so one keypress
/// undoes a flight that went too far.
/// </summary>
internal static class Noclip
{
    // The radius and height the player's own collision calls pass (func_8002E3F8
    // and func_8002ED60 both use 0x320/0x6A4), reused for the floor query so
    // "snap to floor" lands where walking would have.
    const int PlayerRadius = 0x320;
    const int PlayerHeight = 0x06A4;

    // Feature-specific state not already in GameState.cs.

    // Root-motion Y delta, s16 0x801B266C/6E/70 (x/y/z), applied by func_8002F320
    // with collision. Zeroing Y stops a root-motion step from adding a downward
    // displacement on the frames that state runs.
    const uint RootMotionY = 0x801B266E;   // s16

    // Landing-dip / step-up offset, s16 0x801B2654. Stage 10 adds it into the
    // camera height (`lh a2,0x2654` at 0x8002B364, `eyeY = y + bob + land -
    // 0x640`), so while flying it must be zero or the camera dips as the game
    // decays a landing that has not happened.
    const uint LandDip = 0x801B2654;       // s16

    // "Falling fast" flag, u8 0x801B25ED, set by the fall/root-motion code; held
    // clear for the same reason as the vertical state.
    const uint FallingFast = 0x801B25ED;   // u8

    // Surface-half id of the tile being stood on (s16 0x801B2644, 0 lower /
    // 5 upper). func_8002B760 picks the floor-query mode from it, and SnapToFloor
    // mirrors that so the snap uses the same half walking would.
    const uint SurfaceHalf = 0x801B2644;   // s16

    internal static bool Enabled;

    // Units per *second* at full deflection, spent against real elapsed time
    // rather than per call. The hook is stage 4, which runs on the world's 15 Hz
    // tick when the frame gate is paced and at the render rate when it is not, so
    // a per-call step is a different speed on every machine and every setting; a
    // rate is the same flight everywhere.
    internal static float Speed = 7000f;
    internal static float FastMultiplier = 4f;

    // Held boost, for a UI switch. Keyboard flight now comes from Hotkeys
    // (Hotkeys.FlyFast), and a pad's R3 stays as the momentary boost; this flag
    // lets a caller latch it on as well.
    internal static bool Fast;

    // Wall clock between two flight frames. Clamped, because the gap across an
    // area load or a paused panel is seconds long and would fire the flight
    // across the map in one step.
    const double MaxStep = 0.1;
    static long _lastTicks;

    internal static bool InvertStrafe;

    // ---- the cinematic camera ----
    //
    // Flight and look both go through a first-order lag instead of landing on
    // the input: the flight carries a velocity that eases toward what the stick
    // is asking for, and the view is a smoothed copy of the angle the game just
    // integrated. Both time constants are seconds to about 63% of the target,
    // which is the same shape the port's view-smoothing patch uses, and both are spent
    // against real elapsed time so the feel does not change with the frame rate.
    static bool _cinematic;
    internal static bool Cinematic
    {
        get => _cinematic;
        // The look filter tracks the angle the game wrote last frame, so
        // switching it on mid-flight has to re-seed from where the view is now
        // or the first frame turns the whole way from a stale angle.
        set
        {
            if (value == _cinematic) return;
            _cinematic = value;
            _lookPrimed = false;
        }
    }

    internal static float MoveSmoothing = 0.35f;
    internal static float LookSmoothing = 0.25f;

    // The flight's velocity, units a second, eased toward the input.
    static double _vx, _vy, _vz;

    // The look filter. The target accumulates the deltas the game's own turn
    // code applied -- reading the angle back would read what we wrote, so the
    // input has to be recovered as a difference -- and the smoothed value is
    // what gets written to both the base and the composed triple.
    static bool _lookPrimed;
    static double _targetYaw, _targetPitch, _smoothYaw, _smoothPitch;
    static int _prevYaw, _prevPitch;

    // Which way "up" is on the height axis. PSY-Q world space follows screen
    // space in having +Y point down, so "up" subtracts; the standing Y in fdat02
    // is -12800 (0x801E6470) and the area plane 0x801E6474 is -13688, more
    // negative again, which is consistent with negative-up. That is a
    // convention, not a proof, and it is exactly the kind of sign a pitch axis
    // can get backwards -- hence the toggle rather than a hardcoded sign.
    internal static bool InvertVertical;

    // The flight's own position, in floats.
    //
    // Authoritative while flying, and it has to be: the game's floor clamp
    // rewrites Y every frame, so integrating from what is in memory would mean
    // adding a step to a snapped value and hovering rather than climbing. Floats
    // rather than ints for the same reason the runtime's analog input carries a fraction
    // -- at 15 ticks a second a small stick deflection truncates to no movement
    // at all.
    static double _x, _y, _z;

    static bool _wasEnabled;
    static (int X, int Y, int Z) _entryPosition;
    internal static (int X, int Y, int Z) EntryPosition => _entryPosition;

    // Work asked for from outside the game thread. The panel runs on the UI
    // thread and the recompiled routines and the flight state are the game's, so
    // a request is latched here and run on the game thread inside the stage-4
    // hook below.
    static bool _wantReturn;
    static bool _wantSnap;

    /// <summary>How far the flight has strayed from where it started.</summary>
    internal static long DistanceFromEntry(IMemory m)
    {
        var (x, y, z) = GameState.Position(m);
        double dx = x - _entryPosition.X, dy = y - _entryPosition.Y, dz = z - _entryPosition.Z;
        return (long)Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    /// <summary>
    /// Ask for a return to where noclip was switched on. The undo for a bad
    /// flight. Accepted if there is a character in an area; the move itself runs
    /// in the stage-4 hook on the game thread.
    /// </summary>
    internal static bool ReturnToEntry()
    {
        var mem = RecompOne.Runtime.Runtime.Mem;
        if (mem == null || !GameState.IsInGame(mem)) return false;
        _wantReturn = true;
        return true;
    }

    /// <summary>Re-seed the flight's own position from the game's.</summary>
    static void Sync(IMemory m)
    {
        var (x, y, z) = GameState.Position(m);
        _x = x; _y = y; _z = z;

        // A teleport must not arrive carrying the drift it left with.
        _vx = _vy = _vz = 0;
        _lookPrimed = false;
    }

    /// <summary>
    /// Flying for real: switched on, and with a character in an area to fly.
    /// The second half keeps the attract demo on the ground if the flag was left
    /// set from a previous session.
    /// </summary>
    static bool Flying(IMemory m) => Enabled && GameState.IsInGame(m);

    /// <summary>
    /// The player stage's post-hook: after the game's own walk, its collision,
    /// its floor correction and its angle fold, so whatever the game decided
    /// about the position this frame, this overwrites it. It is also the game
    /// thread, which is where the UI's queued requests are run.
    /// </summary>
    [PostHook("game", Address = GameState.PlayerStage)]
    static void AfterPlayerStage(CpuContext c, IMemory m)
    {
        // UI work, on the game thread. The bool test comes first so the hook is
        // free on every ordinary tick.
        if (_wantReturn || _wantSnap)
        {
            if (GameState.IsInGame(m))
            {
                if (_wantReturn) { _wantReturn = false; DoReturnToEntry(m); }
                if (_wantSnap)   { _wantSnap = false;   DoSnapToFloor(c, m); }
            }
            else
            {
                // Drop a request that arrived while there was no player, so it
                // does not fire on whatever session starts next.
                _wantReturn = _wantSnap = false;
            }
        }

        if (!Enabled)
        {
            if (_wasEnabled) Leave(m);
            return;
        }

        // Attract demo and menus: switched on but no character, so nothing flies.
        if (!Flying(m)) return;

        if (!_wasEnabled) Enter(m);

        Fly(c, m);
    }

    static void Enter(IMemory m)
    {
        _wasEnabled = true;
        _lastTicks = System.Diagnostics.Stopwatch.GetTimestamp();
        _entryPosition = GameState.Position(m);
        Sync(m);
        Console.WriteLine($"[kf3debug] noclip on at {Format(_entryPosition)}");
    }

    static void Leave(IMemory m)
    {
        _wasEnabled = false;

        // Log where the flight ended. Landing inside geometry is the normal way
        // a noclip goes wrong, and this line plus a bookmark is how it gets
        // undone -- as is "Snap to floor", which is why the message says so.
        if (GameState.IsInGame(m))
            Console.WriteLine($"[kf3debug] noclip off at {Format(GameState.Position(m))}" +
                              " (snap to floor if you landed inside something)");
    }

    static void Fly(CpuContext c, IMemory m)
    {
        double dt = Elapsed();

        // Before the angles are read, so the flight follows the camera that is
        // actually on screen rather than the one the input asked for.
        if (_cinematic) SmoothLook(m, dt);

        // The *composed* view angles, not the base pair: that triple is what the
        // renderer reads, so it is literally where the camera points, and flight
        // follows the picture rather than the state behind it. 12-bit reads --
        // an s16 read misinterprets a negative pitch, and while sin/cos would
        // not care (they are 2PI-periodic), nothing should depend on that.
        int yaw   = GameState.ReadAngle12(m, GameState.ViewYaw);
        int pitch = GameState.ReadAngle12(m, GameState.ViewPitch);

        // Two sources, summed and clamped, so a pad and a keyboard both drive
        // this and neither has to be configured: the left stick, and the pad
        // word's own direction bits -- which the keyboard fills through the
        // player's bindings (W/S on Up/Down, A/D on L1/R1 in the port's shipped
        // layout), so this follows a rebind for free.
        var (sx, sy) = Shape(Controller.LeftX, Controller.LeftY);
        var (dfwd, dstrafe) = Digital();

        float forward = Math.Clamp(-sy + dfwd, -1f, 1f);      // stick up == forward
        float strafe  = Math.Clamp(sx + dstrafe, -1f, 1f);
        if (InvertStrafe) strafe = -strafe;
        float vertical = Vertical();                          // +1 up, -1 down

        // The velocity the input is asking for, units a second -- zero when
        // nothing is held, which is what the cinematic filter coasts down to.
        double tvx = 0, tvy = 0, tvz = 0;
        if (forward != 0f || strafe != 0f || vertical != 0f)
        {
            double rate = Speed * (Fast || Hotkeys.FlyFast() || Held(Controller.R3) ? FastMultiplier : 1f);

            // The game's own heading vector, from func_8002E3F8, with the
            // camera's pitch folded into forward: looking down and pushing
            // forward descends, which is what every other noclip does. Strafe
            // stays level -- pitch does not roll the flight -- and the up/down
            // keys stay world up, so there is always a way to climb while
            // looking level.
            float fwdAngle    = GameState.AngleToRadians(yaw);
            float strafeAngle = GameState.AngleToRadians(yaw - 0x400);
            float pitchRad    = GameState.AngleToRadians(pitch);
            float level = MathF.Cos(pitchRad);   // the horizontal share of forward
            float dive  = MathF.Sin(pitchRad);   // and the vertical one

            // The Y delta one unit of "up" is worth. Y grows downwards in this
            // game's world space, hence the negative -- the same convention
            // InvertVertical exists to let a player overrule, which is why the
            // camera's own descent is hung off the same sign rather than a
            // second guess.
            float up = InvertVertical ? 1f : -1f;

            tvx = (-MathF.Sin(fwdAngle) * forward * level + -MathF.Sin(strafeAngle) * strafe) * rate;
            tvz = ( MathF.Cos(fwdAngle) * forward * level +  MathF.Cos(strafeAngle) * strafe) * rate;
            tvy = (vertical * up - forward * dive * up) * rate;
        }

        // Instantly at the input unless the cinematic camera is on, in which
        // case the velocity eases toward it and the flight keeps its glide for
        // a moment after the stick is let go.
        double k = _cinematic ? Lag(MoveSmoothing, dt) : 1.0;
        _vx += (tvx - _vx) * k;
        _vy += (tvy - _vy) * k;
        _vz += (tvz - _vz) * k;

        _x += _vx * dt;
        _y += _vy * dt;
        _z += _vz * dt;

        // Our position is the answer, whatever the walk and the floor clamp
        // decided during the stage that just ran.
        GameState.SetPosition(m, (int)_x, (int)_y, (int)_z);

        // Hold the game's own motion and fall state down. The velocities so the
        // frame we stop flying is not the frame the game lurches; the fall
        // velocity, the root-motion Y delta, the landing dip and the vertical
        // state so no landing (and so no fall damage) is ever booked, which is
        // what would otherwise come due the moment flight ends. GameState.
        // StopMotion also clears FallVel.
        GameState.StopMotion(m);
        GameState.WriteS16(m, RootMotionY, 0);
        GameState.WriteS16(m, LandDip, 0);
        m.WriteU8(FallingFast, 0);
        m.WriteU8(GameState.VertState, 0);
    }

    /// <summary>
    /// A first-order lag's blend factor for this step: the share of the way to
    /// the target a value moves in <paramref name="dt"/> seconds, given a time
    /// constant of <paramref name="tau"/>. Framed as an exponential rather than
    /// a fixed fraction so the filter is the same at any frame rate.
    /// </summary>
    static double Lag(double tau, double dt) =>
        tau <= 1e-4 ? 1.0 : 1.0 - Math.Exp(-dt / tau);

    /// <summary>
    /// The camera, trailing the input.
    ///
    /// The angles cannot simply be lerped in place: stage 4 has already added
    /// this frame's turn velocity to the base angle, so reading it back reads
    /// what *we* wrote last frame plus the new delta. The filter therefore
    /// recovers the input as a difference from its own last write, accumulates
    /// it into an unsmoothed target, and writes the smoothed value -- so the
    /// view lags but never loses ground, however long the turn is held.
    ///
    /// Both the base pair and the composed triple are written, the composed one
    /// keeping whatever offset stage 4 put between them, so nothing else the game
    /// does to the view is thrown away.
    ///
    /// The reference carried a "cinematic camera" flag that filtered the game's
    /// look writes in KF2; this game's findings name no such routine, so there is
    /// nothing to port beyond the smoothing the flag already turns on.
    /// </summary>
    static void SmoothLook(IMemory m, double dt)
    {
        // 12-bit reads, not s16: the game stores both angles masked
        // (func_8002F5C0 folds pitch/yaw through `& 0xFFF`), so ReadS16 misreads
        // every negative pitch as +3396..+4095 -- the filter then chases a
        // phantom full-circle delta and the camera flips upside down.
        int baseYaw   = GameState.ReadAngle12(m, GameState.Yaw);
        int basePitch = GameState.ReadAngle12(m, GameState.Pitch);
        int yawOffset   = GameState.ReadAngle12(m, GameState.ViewYaw)   - baseYaw;
        int pitchOffset = GameState.ReadAngle12(m, GameState.ViewPitch) - basePitch;

        if (!_lookPrimed)
        {
            _targetYaw = _smoothYaw = baseYaw;
            _targetPitch = _smoothPitch = basePitch;
            _prevYaw = baseYaw;
            _prevPitch = basePitch;
            _lookPrimed = true;
            return;
        }

        // Shortest arc, because yaw is masked to 12 bits and a turn past zero
        // reads as a delta of almost a full circle the other way.
        int dYaw = (baseYaw - _prevYaw) & GameState.AngleMask;
        if (dYaw > GameState.AngleFull / 2) dYaw -= GameState.AngleFull;

        _targetYaw += dYaw;

        // Clamped, because the game's own base is. The look routine holds base
        // pitch inside +-PitchLimit, so while it sits at the limit the deltas
        // keep arriving and an unclamped target runs away past it -- then the
        // smoothed value overshoots on release. The bound is a no-op in steady
        // state (the target tracks a base that never leaves the range) and a
        // guard against exactly that runaway.
        _targetPitch = Math.Clamp(_targetPitch + (basePitch - _prevPitch),
                                  -GameState.PitchLimit, GameState.PitchLimit);

        double k = Lag(LookSmoothing, dt);
        _smoothYaw   += (_targetYaw - _smoothYaw) * k;
        _smoothPitch += (_targetPitch - _smoothPitch) * k;

        // Keep the pair from drifting out of a double's exact-integer range
        // over a long session, without moving the angle between them.
        if (_targetYaw > GameState.AngleFull * 64 || _targetYaw < -GameState.AngleFull * 64)
        {
            double turns = Math.Truncate(_targetYaw / GameState.AngleFull) * GameState.AngleFull;
            _targetYaw -= turns;
            _smoothYaw -= turns;
        }

        int yaw   = ((int)Math.Round(_smoothYaw)) & GameState.AngleMask;
        int pitch = (int)Math.Round(_smoothPitch);

        GameState.WriteAngle12(m, GameState.Yaw, yaw);
        GameState.WriteAngle12(m, GameState.Pitch, pitch);
        GameState.WriteAngle12(m, GameState.ViewYaw, yaw + yawOffset);
        GameState.WriteAngle12(m, GameState.ViewPitch, pitch + pitchOffset);

        _prevYaw = yaw;
        _prevPitch = pitch;
    }

    /// <summary>
    /// Ask for the player to be put on the ground at their current X/Z, using the
    /// game's own floor query. Accepted if there is a character in an area; the
    /// move itself runs in the stage-4 hook on the game thread.
    ///
    /// This is the way out of a flight that ended inside a wall.
    /// </summary>
    internal static bool SnapToFloor()
    {
        var mem = RecompOne.Runtime.Runtime.Mem;
        if (mem == null || !GameState.IsInGame(mem)) return false;
        _wantSnap = true;
        return true;
    }

    /// <summary>
    /// Put the player on the ground at their current X/Z, using the game's own
    /// floor query. Runs on the game thread from <see cref="AfterPlayerStage"/>.
    ///
    /// func_80033B8C(mode, x, z, radius, height) computes the floor Y for an X/Z
    /// column and returns it in 0x801E6474; func_8002B760 (the game's explicit
    /// snap on area/event entry) calls it with the player's own X/Z and
    /// 0x320/0x6A4, choosing the lower or upper tile half from the surface id
    /// 0x801B2644. Passing the player's own radius and height is what makes this
    /// land where walking there would have. This is the KF3 analogue of the KF2
    /// reference's func_8002C3A8.
    /// </summary>
    static void DoSnapToFloor(CpuContext cpu, IMemory m)
    {
        var (x, _, z) = GameState.Position(m);

        // mode 2 = upper half (tile+6), mode 1 = lower half (tile+1); the same
        // test func_8002B760 makes on the surface id.
        uint mode = m.ReadU16(SurfaceHalf) != 0 ? 2u : 1u;

        var saved = cpu.Snapshot();
        cpu.SP -= 0x20u;
        m.WriteU32(cpu.SP + 0x10u, (uint)PlayerHeight);
        cpu.A0 = mode;
        cpu.A1 = (uint)x;
        cpu.A2 = (uint)z;
        cpu.A3 = (uint)PlayerRadius;
        KingsField3.func_80033B8C(cpu, m);
        int floor = (int)cpu.V0;
        cpu.SP += 0x20u;
        cpu.Restore(saved);

        GameState.WriteS32(m, GameState.PosY, floor);
        GameState.StopMotion(m);
        Sync(m);
        Console.WriteLine($"[kf3debug] snapped to floor Y {floor}");
    }

    /// <summary>Back to where noclip was switched on. Runs on the game thread
    /// from <see cref="AfterPlayerStage"/>.</summary>
    static void DoReturnToEntry(IMemory m)
    {
        GameState.SetPosition(m, _entryPosition.X, _entryPosition.Y, _entryPosition.Z);
        GameState.StopMotion(m);
        Sync(m);
        Console.WriteLine($"[kf3debug] returned to {Format(_entryPosition)}");
    }

    /// <summary>
    /// Tell the flight to re-read the game's position. Anything that moves the
    /// player from outside this file -- a bookmark, a typed coordinate, an area
    /// warp -- has to call this, or the next flight frame would drag you
    /// straight back to where the flight thought you were.
    /// </summary>
    internal static void Resync()
    {
        var mem = RecompOne.Runtime.Runtime.Mem;
        if (mem != null) Sync(mem);
    }

    /// <summary>Seconds since the last flight frame, clamped.</summary>
    static double Elapsed()
    {
        long now = System.Diagnostics.Stopwatch.GetTimestamp();
        double dt = (now - _lastTicks) / (double)System.Diagnostics.Stopwatch.Frequency;
        _lastTicks = now;
        return dt <= 0 ? 0 : Math.Min(dt, MaxStep);
    }

    /// <summary>
    /// Forward and strafe off the runtime's pad word, which is active LOW and
    /// carries the keyboard's bindings as well as a pad's buttons -- so one read
    /// covers both devices and follows whatever the player has bound. (The game
    /// stores the same word at 0x801B265C, byte-swapped and active HIGH, but the
    /// runtime's copy is the shared source and needs no swap.)
    ///
    /// The reference dropped L1/R1 strafing while a pad held them for vertical
    /// flight, keyed off its Hotkeys helper. This port has no such helper, so
    /// the shoulders keep strafing; vertical comes from the game's own look
    /// buttons instead (see <see cref="Vertical"/>).
    /// </summary>
    static (float Forward, float Strafe) Digital()
    {
        float f = 0f, s = 0f;
        if (Held(Controller.Up)) f += 1f;
        if (Held(Controller.Down)) f -= 1f;
        if (Held(Controller.R1)) s += 1f;
        if (Held(Controller.L1)) s -= 1f;
        return (f, s);
    }

    /// <summary>
    /// Vertical flight, +1 up and -1 down.
    ///
    /// Keyboard flight is the hotkeys' <see cref="Hotkeys.FlyVertical"/>, as in
    /// the reference (the keys are there because this game's layout binds Space
    /// to attack -- see Hotkeys). The pad's own R2/L2 are kept: R2 is look up and
    /// L2 is look down (they drive the pitch velocity 0x801B264E; docs/INPUT.md),
    /// and the port's shipped keyboard layout leaves L2/R2 unbound, so a pad
    /// reaches them and a keyboard can bind them without colliding with the
    /// mouse's pitch.
    /// </summary>
    static float Vertical()
    {
        float v = Hotkeys.FlyVertical();
        if (Held(Controller.R2)) v += 1f;
        if (Held(Controller.L2)) v -= 1f;
        return v;
    }

    /// <summary>The runtime's pad word is active LOW: a bit set means released.</summary>
    static bool Held(ushort bit) => (Controller.State & bit) == 0;

    /// <summary>
    /// One stick as a radial-deadzoned, curved vector. Same shape as
    /// the runtime's own analog shaping -- the bytes are the runtime's 0..255 with 0x80 centre,
    /// and InputManager already applies a 1.3x gain, so the byte saturates a
    /// little before the stick does.
    /// </summary>
    static (float X, float Y) Shape(byte bx, byte by)
    {
        const float deadzone = 0.15f;

        float x = (bx - 128) / 127f;
        float y = (by - 128) / 127f;
        float mag = MathF.Sqrt(x * x + y * y);
        if (mag <= deadzone || mag <= 0f) return (0f, 0f);

        float unit = Math.Clamp((mag - deadzone) / (1f - deadzone), 0f, 1f);
        float scaled = unit / mag;
        return (x * scaled, y * scaled);
    }

    internal static string Format((int X, int Y, int Z) p) => $"({p.X}, {p.Y}, {p.Z})";

    /// <summary>Drop all flight state, for mod unload.</summary>
    internal static void Reset()
    {
        Enabled = false;
        Fast = false;
        _wasEnabled = false;
        _wantReturn = _wantSnap = false;
        _vx = _vy = _vz = 0;
        _cinematic = false;
        _lookPrimed = false;
    }
}
