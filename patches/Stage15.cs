using System.Reflection;
using RecompOne.Runtime;
using RecompOne.Runtime.Context;
using RecompOne.Runtime.Memory;
using RecompOne.Runtime.Modding;

namespace Kf3;

/// <summary>
/// Stage 15, the frame builder <c>func_800422B8(VECTOR *pos, SVECTOR *rot)</c>, in C#.
///
///     KF3_STAGE15=1         this transcription (the default); 0 the recompiled routine
///     KF3_STAGE15=verify    the recompiled routine draws the frame; this one is then
///                           run against a record of it and the two compared
///     KF3_STAGE15_NEEDLE=0  step the compass needle every drawn frame (held to the tick by default)
///
/// The routine is twenty-two calls in a fixed order (the compass call conditional),
/// each through a delegate to its recompiled callee so every hook on it still fires,
/// with one block of arithmetic between <see cref="Site.Arm"/> and
/// <see cref="Site.HudModels"/> that builds the HUD records. <see cref="ViewOverride"/>
/// draws the frame from a camera of the port's instead of the one the main loop hands
/// over. <see cref="Verifier"/> records the recompiled run at every call and replays
/// this transcription against it.
/// </summary>
public static class Stage15
{
    const uint Routine = 0x800422B8;

    /// <summary>The calls, in the order the routine makes them.</summary>
    public enum Site
    {
        View,             // the camera block, from a0/a1
        AnimatedTextures,
        Fade,
        CullGrid,
        FrameHead,
        SoundMark,
        Arm,
        CompassError,     // the wrapped difference of two angles; skipped when u8[0x801B25DE] is 0
        HudModels,
        Overlays,
        Overlays2,
        Tiles,
        Models,
        Quad0,
        Quad1,
        Quad2,
        Quad3,
        Quad4,
        Quad5,
        Present,
        FrameGate,
        SoundService,
    }

    /// <summary>Each site's callee, and the return address its <c>jal</c> leaves in RA.</summary>
    static readonly (uint Callee, uint Return)[] Calls =
    [
        (0x800357E8, 0x800422E0),
        (0x800351FC, 0x800422E8),
        (0x80041F9C, 0x800422F0),
        (0x80034BF4, 0x800422F8),
        (0x80035630, 0x80042300),
        (0x80043858, 0x80042308),
        (0x8003DF50, 0x80042310),
        (0x80016A98, 0x80042478),
        (0x8003C35C, 0x80042890),
        (0x80041E68, 0x80042898),
        (0x80041D9C, 0x800428A0),
        (0x8003BFD0, 0x800428A8),
        (0x80040AE4, 0x800428B0),
        (0x8003D280, 0x800428B8),
        (0x8003D38C, 0x800428C0),
        (0x8003D41C, 0x800428C8),
        (0x8003D568, 0x800428D0),
        (0x8003D64C, 0x800428D8),
        (0x8003D79C, 0x800428E0),
        (0x80035700, 0x800428E8),
        (0x80019614, 0x800428F0),
        (0x80043940, 0x800428F8),
    ];

    enum Mode { Off, On, Verify }
    static Mode _mode = Mode.On;

    /// <summary>The mode as the Testing tab sets it: 0 recompiled, 1 C#, 2 verify.</summary>
    public static int Setting { get => (int)_mode; set => _mode = (Mode)Math.Clamp(value, 0, 2); }
    static long _needleTick = -1;
    static bool _needleHeld = true;

    // The needle's yaw at the last two ticks, drawn between them while the view is carried.
    static short _needlePrev, _needleCur;
    static long _needleSampled = -1;
    static uint _needleA, _needleB;
    static bool _needleDrawn;

    // The gauges' eased HP and MP followers, stepped by stage 4 on the tick: their last
    // two tick samples, the tick of the last, and whether this frame wrote a carried one.
    static readonly uint[] Followers = [0x801B2502u, 0x801B2506u];
    static readonly ushort[] _followPrev = new ushort[2], _followCur = new ushort[2];
    static long _followTick = -1, _followSampled = -1;
    static bool _followDrawn;

    /// <summary>Frames a gauge was drawn between its tick samples, for the view probe.</summary>
    public static long GaugeCarried;

    /// <summary>Frames the needle was drawn between its tick samples, for the view probe.</summary>
    public static long NeedleCarried;
    static bool _queued;
    static Action<CpuContext, IMemory>[]? _callees;

    /// <summary>Whether the compass needle's spring is held to the tick (KF3_STAGE15_NEEDLE).</summary>
    public static bool NeedleHeld { get => _needleHeld; set => _needleHeld = value; }

    /// <summary>Whether the C# routine draws (KF3_STAGE15 unset or 1), so the override is read.</summary>
    public static bool InCSharp => _mode == Mode.On;

    /// <summary>How many times the On-mode routine has run.</summary>
    public static long Frames;

    /// <summary>A camera to draw the frame from instead of the one stage 15 is handed,
    /// or null for the game's. Needs the C# routine; the recompiled one ignores it.</summary>
    public static Camera? ViewOverride { get; set; }

    /// <summary>The camera the main loop handed stage 15 last, override or not.</summary>
    public static Camera? Handed { get; private set; }

    /// <summary>Called with each camera handed to stage 15.</summary>
    public static Action<Camera>? OnHanded { get; set; }

    static readonly ModInfo _self = new()
    {
        Id = "kf3.stage15",
        Name = "Stage 15",
        Version = "1.0",
        Description = "func_800422B8, the frame builder, in C#.",
    };

    public static void Configure(string? mode, string? needle)
    {
        _mode = mode?.Trim().ToLowerInvariant() switch
        {
            "0" or "off" => Mode.Off,
            "verify" => Mode.Verify,
            _ => Mode.On,
        };
        _needleHeld = needle?.Trim() is not ("0" or "off");
    }

    public static void Install()
    {
        // Attached in every mode, so the Testing tab can switch it live; off runs the recompiled routine.
        HookAttach.OnOverlayLoad("stage 15", Attach);
    }

    static bool Attach()
    {
        SymbolRegistry.Build();
        var target = SymbolRegistry.Resolve("game", null, Routine);
        if (target == null) return false;
        if (!Bind())
        {
            Console.Error.WriteLine("[KF3] stage 15: a callee is not mapped; the recompiled routine stays.");
            return false;
        }
        if (!_queued)
        {
            var impl = typeof(Stage15).GetMethod(nameof(Replace), BindingFlags.NonPublic | BindingFlags.Static)!;
            _queued = HookManager.AddReplace(_self, target, impl);
            if (!_queued) return false;
        }
        HookManager.Commit();
        bool ok = HookAttach.Installed(target);
        Console.WriteLine(ok ? $"[KF3] stage 15: {_mode.ToString().ToLowerInvariant()}"
                             : "[KF3] stage 15: not installed");
        return ok;
    }

    /// <summary>A delegate per callee. Calling one runs the function's detour, so
    /// every hook on it fires as it does for the recompiled body's direct call.</summary>
    static bool Bind()
    {
        if (_callees != null) return true;
        if (Calls.Length != Enum.GetValues<Site>().Length) return false;
        var fns = new Action<CpuContext, IMemory>[Calls.Length];
        for (int i = 0; i < Calls.Length; i++)
        {
            var mi = SymbolRegistry.Resolve("game", null, Calls[i].Callee);
            if (mi == null) return false;
            fns[i] = mi.CreateDelegate<Action<CpuContext, IMemory>>();
        }
        _callees = fns;
        return true;
    }

    static void Replace(Action<CpuContext, IMemory> orig, CpuContext c, IMemory m)
    {
        // PGXP's RAM shadow is kept by the recompiled stores, which C# stores skip. A
        // stage 15 inside one being recorded is the recompiled one's too.
        if (_mode == Mode.Off || RecompOne.Runtime.Pgxp.Pgxp.CpuTracking || m is not PSMemory mem
            || Verifier.Recording)
        {
            orig(c, m);
            return;
        }
        if (_mode == Mode.Verify)
        {
            Verifier.Run(orig, c, mem);
        }
        else
        {
            Frames++;
            // Under pacing the needle's spring steps once a tick, not once a drawn frame.
            Run(c, mem, !_needleHeld || FramePacing.FirstWalkOfTick(ref _needleTick));
        }
    }

    /// <summary>The routine, transcribed. With <paramref name="stepNeedle"/> false the
    /// needle holds; true is the routine as the game wrote it.</summary>
    static void Run(CpuContext c, PSMemory mem, bool stepNeedle)
    {
        // The camera the frame would have used, put back in the block after an override
        // so the world's stages never read the drawn one.
        Camera? real = null;
        if (!Verifier.Replaying)
        {
            bool handedNow = c.A0 != 0u && c.A1 != 0u;
            if (handedNow)
            {
                Handed = Camera.Read(mem, c.A0, c.A1);
                if (Handed is { } handed) OnHanded?.Invoke(handed);
            }
            if (ViewOverride is { } view)
            {
                real = handedNow ? Handed : Camera.Read(mem);
                CameraBlock.Store(mem, view);
                c.A0 = 0u;
                c.A1 = 0u;
            }
        }

        c.V0 = mem.ReadU8(0x801B25E1u);
        uint sp = c.SP - 0x38u;
        c.SP = sp;
        mem.WriteU32(sp + 0x34u, c.RA);
        mem.WriteU32(sp + 0x30u, c.S0);
        mem.WriteU8(c.GP + 0xCCu, (byte)c.V0);
        mem.WriteU8(c.GP + 0xCDu, (byte)c.V0);
        mem.WriteU8(c.GP + 0xCEu, (byte)c.V0);

        Call(c, mem, Site.View);
        Call(c, mem, Site.AnimatedTextures);
        Call(c, mem, Site.Fade);
        Call(c, mem, Site.CullGrid);
        Call(c, mem, Site.FrameHead);
        Call(c, mem, Site.SoundMark);
        ModelSmoothing.EnterArm();
        Call(c, mem, Site.Arm);
        ModelSmoothing.Leave();
        Hud(c, mem, stepNeedle);
        Call(c, mem, Site.HudModels);
        if (_needleDrawn) PutNeedleBack(mem);
        Call(c, mem, Site.Overlays);
        Call(c, mem, Site.Overlays2);
        Call(c, mem, Site.Tiles);
        Call(c, mem, Site.Models);
        Call(c, mem, Site.Quad0);
        Call(c, mem, Site.Quad1);
        Call(c, mem, Site.Quad2);
        Call(c, mem, Site.Quad3);
        Call(c, mem, Site.Quad4);
        Call(c, mem, Site.Quad5);
        Call(c, mem, Site.Present);
        Call(c, mem, Site.FrameGate);
        Call(c, mem, Site.SoundService);
        if (real is { } back) CameraBlock.Build(c, mem, back);

        c.RA = mem.ReadU32(sp + 0x34u);
        c.S0 = mem.ReadU32(sp + 0x30u);
        c.SP = sp + 0x38u;
    }

    /// <summary>Whether every callee is bound, so <see cref="DrawScene"/> can draw.</summary>
    public static bool CanDraw => Bind();

    /// <summary>
    /// The routine's drawing half -- the view from the stored camera, the cull grid,
    /// and every call that adds to the ordering tables -- into whatever table, front
    /// table and primitive descriptor are current. Nothing that advances the world,
    /// steps the HUD state, flips a buffer, marks or services a sound slot or
    /// presents. The list <see cref="MenuWorld"/> draws behind a menu. False when a
    /// callee is not mapped. <paramref name="atFrameHead"/> runs where the frame head
    /// would have, after the cull grid (the retained scene begins its frame there).
    /// </summary>
    public static bool DrawScene(CpuContext c, PSMemory mem, Action? atFrameHead = null)
    {
        if (!Bind()) return false;
        c.A0 = 0u;
        c.A1 = 0u;
        Call(c, mem, Site.View);
        Call(c, mem, Site.CullGrid);
        atFrameHead?.Invoke();
        ModelSmoothing.EnterArm();
        Call(c, mem, Site.Arm);
        ModelSmoothing.Leave();
        Call(c, mem, Site.HudModels);
        Call(c, mem, Site.Overlays);
        Call(c, mem, Site.Overlays2);
        Call(c, mem, Site.Tiles);
        Call(c, mem, Site.Models);
        Call(c, mem, Site.Quad0);
        Call(c, mem, Site.Quad1);
        Call(c, mem, Site.Quad2);
        Call(c, mem, Site.Quad3);
        Call(c, mem, Site.Quad4);
        Call(c, mem, Site.Quad5);
        return true;
    }

    static void Call(CpuContext c, PSMemory mem, Site site)
    {
        c.RA = Calls[(int)site].Return;
        if (Verifier.Replaying) Verifier.Take(c, mem, site);
        else _callees![(int)site](c, mem);
    }

    // ---- the HUD block: the only body work that is not a call -------------------

    static void Hud(CpuContext c, PSMemory mem, bool stepNeedle)
    {
        c.V1 = mem.ReadU8(0x801B25DDu);
        c.V0 = mem.ReadU8(0x801B25DEu);
        mem.WriteU8(0x80081ACCu, 0);
        mem.WriteU8(0x80081AB8u, 0);
        c.A0 = c.V1 & 1u;
        c.V1 = c.V1 & 2u;
        c.V1 = c.V1 >> 1;
        c.A1 = c.V0 & 1u;
        c.V0 = c.V0 & 2u;
        c.V0 = c.V0 >> 1;
        mem.WriteU8(0x80081C20u, (byte)c.A1);
        mem.WriteU8(0x80081C44u, (byte)c.V0);
        mem.WriteU8(0x80081AA4u, (byte)c.A0);
        mem.WriteU8(0x80081A90u, (byte)c.A0);
        mem.WriteU8(0x80081A7Cu, (byte)c.A0);
        mem.WriteU8(0x80081A68u, (byte)c.A0);
        mem.WriteU8(0x80081A54u, (byte)c.A0);
        mem.WriteU8(0x80081A40u, (byte)c.A0);
        mem.WriteU8(0x80081A2Cu, (byte)c.A0);
        mem.WriteU8(0x80081A18u, (byte)c.A0);
        mem.WriteU8(0x80081A04u, (byte)c.A0);
        mem.WriteU8(0x800819F0u, (byte)c.A0);
        mem.WriteU8(0x800819DCu, (byte)c.A0);
        mem.WriteU8(0x800819C8u, (byte)c.A0);
        mem.WriteU8(0x800819B4u, (byte)c.A0);
        mem.WriteU8(0x80081BF8u, (byte)c.V1);
        mem.WriteU8(0x80081BE4u, (byte)c.V1);
        mem.WriteU8(0x80081BD0u, (byte)c.V1);
        mem.WriteU8(0x80081BBCu, (byte)c.V1);
        mem.WriteU8(0x80081BA8u, (byte)c.V1);
        mem.WriteU8(0x80081B94u, (byte)c.V1);
        mem.WriteU8(0x80081B80u, (byte)c.V1);
        mem.WriteU8(0x80081B6Cu, (byte)c.V1);
        mem.WriteU8(0x80081B58u, (byte)c.V1);
        mem.WriteU8(0x80081B44u, (byte)c.V1);
        mem.WriteU8(0x80081B30u, (byte)c.V1);
        mem.WriteU8(0x80081B1Cu, (byte)c.V1);
        mem.WriteU8(0x80081B08u, (byte)c.V1);
        mem.WriteU8(0x80081AF4u, (byte)c.V1);
        mem.WriteU8(0x80081AE0u, (byte)c.V1);

        // The compass record: s0 = u8 - 1; a negative index skips the needle entirely.
        c.V0 = mem.ReadU8(0x801B25DEu);
        c.S0 = c.V0 - 1u;
        if ((int)c.S0 >= 0)
        {
            c.V0 = c.S0 << 3;
            { var _s = c.V0; var _t = c.S0; c.V0 = _s + _t; }
            c.V0 = c.V0 << 2;
            c.At = 0x80081C3Au;
            { var _s = c.At; var _t = c.V0; c.At = _s + _t; }
            c.A0 = (uint)(short)mem.ReadU16(c.At);
            c.A1 = (uint)(short)mem.ReadU16(0x801AEC5Eu);
            Call(c, mem, Site.CompassError);

            if (stepNeedle)
            {
                c.V1 = mem.ReadU32(c.GP + 0xD8u);
                { var _s = c.V0; var _t = c.V1; c.V1 = _s + _t; }
                mem.WriteU32(c.GP + 0xD8u, c.V1);
                if (c.V1 != 0u)
                {
                    c.V0 = (int)c.V1 > 0 ? c.V1 + 7u : c.V1 - 7u;
                    c.V0 = (uint)((int)c.V0 >> 3);
                    { var _s = c.V1; var _t = c.V0; c.V0 = _s - _t; }
                    mem.WriteU32(c.GP + 0xD8u, c.V0);
                }
            }

            c.V1 = c.S0 << 3;
            { var _s = c.V1; var _t = c.S0; c.V1 = _s + _t; }
            c.V1 = c.V1 << 2;
            c.At = 0x80081C3Au;
            { var _s = c.At; var _t = c.V1; c.At = _s + _t; }
            c.A0 = mem.ReadU16(c.At);
            if (stepNeedle)
            {
                c.V0 = mem.ReadU32(c.GP + 0xD8u);
                c.V0 = (uint)((int)c.V0 >> 6);
                { var _s = c.A0; var _t = c.V0; c.A0 = _s + _t; }
            }
            c.V0 = 0x80081C20u;
            { var _s = c.V1; var _t = c.V0; c.V1 = _s + _t; }
            c.V0 = c.S0 < 1u ? 1u : 0u;
            { var _s = 0u; var _t = c.V0; c.V0 = _s - _t; }
            c.V0 = c.V0 & 0x0024u;
            mem.WriteU16(c.V1 + 0x1Au, (ushort)c.A0);
            c.At = 0x80081C3Au;
            { var _s = c.At; var _t = c.V0; c.At = _s + _t; }
            mem.WriteU16(c.At, (ushort)c.A0);
            CarryNeedle(mem, c.V1 + 0x1Au, c.At, (short)c.A0, stepNeedle);
            c.A0 = mem.ReadU16(0x801AEC5Cu);
            mem.WriteU16(c.V1 + 0x18u, (ushort)c.A0);
            c.At = 0x80081C38u;
            { var _s = c.At; var _t = c.V0; c.At = _s + _t; }
            mem.WriteU16(c.At, (ushort)c.A0);
        }

        // u8[0x801B25DD]: 1 and 2 are the two gauge/digit layouts; anything else leaves
        // the records as built above.
        CarryFollowers(mem);
        c.V1 = mem.ReadU8(0x801B25DDu);
        if (c.V1 == 1u)
        {
            c.V0 = 2u;
            Gauge1(c, mem);
        }
        else
        {
            c.V0 = 2u;
            c.T3 = 0x10620000u;
            if (c.V1 == c.V0) Gauge2(c, mem);
        }
        if (_followDrawn)
        {
            for (int i = 0; i < Followers.Length; i++) mem.WriteU16(Followers[i], _followCur[i]);
            _followDrawn = false;
        }
    }

    /// <summary>With the view carried, the gauges are built from their followers
    /// interpolated between the last two ticks; the tick's values go back after.</summary>
    static void CarryFollowers(PSMemory mem)
    {
        if (!ViewSmoothing.Active || Verifier.Replaying) return;
        long tick = FramePacing.Ticks;
        bool first = FramePacing.FirstWalkOfTick(ref _followTick);
        for (int i = 0; i < Followers.Length; i++)
        {
            ushort v = mem.ReadU16(Followers[i]);
            if (first)
            {
                _followPrev[i] = _followSampled < 0 || tick - _followSampled > 1 ? v : _followCur[i];
                _followCur[i] = v;
            }
            else if (v != _followCur[i])
            {
                _followPrev[i] = _followCur[i] = v;
            }
        }
        if (first) _followSampled = tick;

        double f = FramePacing.TickFraction;
        for (int i = 0; i < Followers.Length; i++)
        {
            int a = _followPrev[i], b = _followCur[i];
            if (a == b) continue;
            ushort drawn = (ushort)(a + (int)Math.Round((b - a) * f));
            if (drawn == b) continue;
            mem.WriteU16(Followers[i], drawn);
            _followDrawn = true;
        }
        if (_followDrawn) GaugeCarried++;
    }

    /// <summary>With the view carried, the needle is drawn between its last two tick
    /// samples and put back after the HUD's call, so the spring reads the tick's yaw.
    /// <paramref name="a"/> and <paramref name="b"/> are the two records it is written to.</summary>
    static void CarryNeedle(PSMemory mem, uint a, uint b, short yaw, bool stepped)
    {
        _needleDrawn = false;
        if (!ViewSmoothing.Active || !_needleHeld || Verifier.Replaying) return;
        long tick = FramePacing.Ticks;
        if (stepped || a != _needleA)
        {
            bool fresh = a != _needleA || _needleSampled < 0 || tick - _needleSampled > 1;
            _needlePrev = fresh ? yaw : _needleCur;
            _needleCur = yaw;
            _needleA = a;
            _needleSampled = tick;
            if (ViewSmoothing.Jumped(_needlePrev, _needleCur)) _needlePrev = _needleCur;
        }
        else if (yaw != _needleCur)
        {
            _needlePrev = _needleCur = yaw;
        }

        short drawn = ViewSmoothing.Turn(_needlePrev, _needleCur, FramePacing.TickFraction);
        if (drawn == yaw) return;
        mem.WriteU16(a, (ushort)drawn);
        mem.WriteU16(b, (ushort)drawn);
        _needleB = b;
        _needleDrawn = true;
        NeedleCarried++;
    }

    static void PutNeedleBack(PSMemory mem)
    {
        mem.WriteU16(_needleA, (ushort)_needleCur);
        mem.WriteU16(_needleB, (ushort)_needleCur);
        _needleDrawn = false;
    }

    static void Gauge1(CpuContext c, PSMemory mem)
    {
        c.V0 = mem.ReadU16(0x801B24FCu);
        c.A1 = c.V0 << 1;
        { var _s = c.A1; var _t = c.V0; c.A1 = _s + _t; }
        c.V0 = mem.ReadU16(0x801B24FAu);
        c.A1 = c.A1 << 4;
        DivS(c, c.A1, c.V0);
        if (c.V0 == 0u) Recompiled.Bios.Break(c, mem);
        c.At = 0xFFFFFFFFu;
        if (c.V0 != c.At)
        {
            c.At = 0x80000000u;
            goto L80042574;
        }
        c.At = 0x80000000u;
        if (c.A1 == c.At) Recompiled.Bios.Break(c, mem);
        L80042574:
        c.A1 = c.LO;
        c.A3 = 0x68DB8BADu;
        c.V1 = mem.ReadU16(0x801B2502u);
        c.V0 = c.V1 << 1;
        { var _s = c.V0; var _t = c.V1; c.V0 = _s + _t; }
        c.V0 = c.V0 << 4;
        MultS(c, c.V0, c.A3);
        c.V0 = mem.ReadU16(0x801B2500u);
        c.V1 = c.V0 << 1;
        { var _s = c.V1; var _t = c.V0; c.V1 = _s + _t; }
        c.A2 = c.HI;
        c.V0 = mem.ReadU16(0x801B24FEu);
        c.V1 = c.V1 << 4;
        DivS(c, c.V1, c.V0);
        if (c.V0 == 0u) Recompiled.Bios.Break(c, mem);
        c.At = 0xFFFFFFFFu;
        if (c.V0 != c.At)
        {
            c.At = 0x80000000u;
            goto L800425E4;
        }
        c.At = 0x80000000u;
        if (c.V1 == c.At) Recompiled.Bios.Break(c, mem);
        L800425E4:
        c.V1 = c.LO;
        c.A0 = mem.ReadU16(0x801B2506u);
        c.V0 = c.A0 << 1;
        { var _s = c.V0; var _t = c.A0; c.V0 = _s + _t; }
        c.V0 = c.V0 << 4;
        MultS(c, c.V0, c.A3);
        c.A2 = c.A2 >> 11;
        mem.WriteU8(0x80081A1Cu, (byte)c.A2);
        c.A2 = c.A2 & 0x00FFu;
        mem.WriteU16(0x80081A22u, (ushort)c.A2);
        c.V0 = c.HI;
        c.V0 = c.V0 >> 11;
        mem.WriteU8(0x80081A44u, (byte)c.V0);
        c.V0 = c.V0 & 0x00FFu;
        mem.WriteU16(0x80081A4Au, (ushort)c.V0);
        mem.WriteU8(0x80081A08u, (byte)c.A1);
        c.A1 = c.A1 & 0x00FFu;
        mem.WriteU16(0x80081A0Eu, (ushort)c.A1);
        mem.WriteU8(0x80081A30u, (byte)c.V1);
        c.V1 = c.V1 & 0x00FFu;
        mem.WriteU16(0x80081A36u, (ushort)c.V1);
    }

    static void Gauge2(CpuContext c, PSMemory mem)
    {
        c.A0 = mem.ReadU16(0x801B24FCu);
        c.T3 = c.T3 | 0x4DD3u;
        MultU(c, c.A0, c.T3);
        c.T4 = 0x51EB851Fu;
        c.V1 = c.HI;
        c.V1 = c.V1 >> 6;
        c.V0 = c.V1 << 5;
        { var _s = c.V0; var _t = c.V1; c.V0 = _s - _t; }
        c.V0 = c.V0 << 2;
        { var _s = c.V0; var _t = c.V1; c.V0 = _s + _t; }
        c.V0 = c.V0 << 3;
        { var _s = c.A0; var _t = c.V0; c.A0 = _s - _t; }
        c.T5 = c.A0 & 0xFFFFu;
        MultS(c, c.T5, c.T4);
        c.T2 = 0x66666667u;
        c.T0 = (uint)((int)c.T5 >> 31);
        c.V0 = c.HI;
        c.V0 = (uint)((int)c.V0 >> 5);
        { var _s = c.V0; var _t = c.T0; c.V0 = _s - _t; }
        c.A3 = c.V0 << 1;
        { var _s = c.A3; var _t = c.V0; c.A3 = _s + _t; }
        c.A2 = c.A3 << 3;
        { var _s = c.A2; var _t = c.V0; c.A2 = _s + _t; }
        c.A2 = c.A2 << 2;
        { var _s = c.T5; var _t = c.A2; c.A2 = _s - _t; }
        MultS(c, c.A2, c.T2);
        c.T1 = c.HI;
        MultS(c, c.T5, c.T2);
        c.A0 = c.HI;
        c.A1 = mem.ReadU16(0x801B2500u);
        MultU(c, c.A1, c.T3);
        c.A0 = (uint)((int)c.A0 >> 2);
        { var _s = c.A0; var _t = c.T0; c.A0 = _s - _t; }
        c.V1 = c.HI;
        c.V1 = c.V1 >> 6;
        c.V0 = c.V1 << 5;
        { var _s = c.V0; var _t = c.V1; c.V0 = _s - _t; }
        c.V0 = c.V0 << 2;
        { var _s = c.V0; var _t = c.V1; c.V0 = _s + _t; }
        c.V0 = c.V0 << 3;
        { var _s = c.A1; var _t = c.V0; c.A1 = _s - _t; }
        c.V1 = c.A0 << 2;
        { var _s = c.V1; var _t = c.A0; c.V1 = _s + _t; }
        c.V1 = c.V1 << 1;
        { var _s = c.T5; var _t = c.V1; c.V1 = _s - _t; }
        c.T5 = c.A1 & 0xFFFFu;
        MultS(c, c.T5, c.T4);
        c.T4 = (uint)((int)c.T5 >> 31);
        c.V0 = c.HI;
        c.V0 = (uint)((int)c.V0 >> 5);
        { var _s = c.V0; var _t = c.T4; c.V0 = _s - _t; }
        c.A1 = c.V0 << 1;
        { var _s = c.A1; var _t = c.V0; c.A1 = _s + _t; }
        c.A0 = c.A1 << 3;
        { var _s = c.A0; var _t = c.V0; c.A0 = _s + _t; }
        c.A0 = c.A0 << 2;
        { var _s = c.T5; var _t = c.A0; c.A0 = _s - _t; }
        MultS(c, c.A0, c.T2);
        c.A3 = c.A3 << 2;
        c.A3 = c.A3 - 0x80u;
        c.A2 = (uint)((int)c.A2 >> 31);
        mem.WriteU8(0x80081B0Au, (byte)c.A3);
        c.T1 = (uint)((int)c.T1 >> 2);
        { var _s = c.T1; var _t = c.A2; c.T1 = _s - _t; }
        c.T3 = 0x68DB8BADu;
        c.T0 = c.HI;
        c.V0 = c.T1 << 1;
        MultS(c, c.T5, c.T2);
        { var _s = c.V0; var _t = c.T1; c.V0 = _s + _t; }
        c.V0 = c.V0 << 2;
        c.V0 = c.V0 - 0x80u;
        mem.WriteU8(0x80081B1Eu, (byte)c.V0);
        c.V0 = c.V1 << 1;
        { var _s = c.V0; var _t = c.V1; c.V0 = _s + _t; }
        c.V0 = c.V0 << 2;
        c.V0 = c.V0 - 0x80u;
        c.A1 = c.A1 << 2;
        c.A1 = c.A1 - 0x80u;
        c.V1 = mem.ReadU16(0x801B2502u);
        c.A2 = c.HI;
        c.A0 = (uint)((int)c.A0 >> 31);
        c.V1 = c.V1 << 6;
        MultS(c, c.V1, c.T3);
        mem.WriteU8(0x80081B32u, (byte)c.V0);
        mem.WriteU8(0x80081B6Eu, (byte)c.A1);
        c.T0 = (uint)((int)c.T0 >> 2);
        { var _s = c.T0; var _t = c.A0; c.T0 = _s - _t; }
        c.V0 = c.T0 << 1;
        { var _s = c.V0; var _t = c.T0; c.V0 = _s + _t; }
        c.V0 = c.V0 << 2;
        c.V0 = c.V0 - 0x80u;
        mem.WriteU8(0x80081B82u, (byte)c.V0);
        c.A2 = (uint)((int)c.A2 >> 2);
        c.V1 = mem.ReadU16(0x801B2506u);
        c.A0 = c.HI;
        { var _s = c.A2; var _t = c.T4; c.A2 = _s - _t; }
        c.V1 = c.V1 << 6;
        MultS(c, c.V1, c.T3);
        c.V1 = c.A2 << 2;
        { var _s = c.V1; var _t = c.A2; c.V1 = _s + _t; }
        c.V1 = c.V1 << 1;
        { var _s = c.T5; var _t = c.V1; c.V1 = _s - _t; }
        c.V0 = c.V1 << 1;
        { var _s = c.V0; var _t = c.V1; c.V0 = _s + _t; }
        c.V0 = c.V0 << 2;
        c.V0 = c.V0 - 0x80u;
        c.A0 = c.A0 >> 11;
        mem.WriteU8(0x80081BACu, (byte)c.A0);
        c.A0 = c.A0 & 0x00FFu;
        mem.WriteU8(0x80081B96u, (byte)c.V0);
        mem.WriteU16(0x80081BB2u, (ushort)c.A0);
        c.V0 = c.HI;
        c.V0 = c.V0 >> 11;
        mem.WriteU8(0x80081BC0u, (byte)c.V0);
        c.V0 = c.V0 & 0x00FFu;
        mem.WriteU16(0x80081BC6u, (ushort)c.V0);
    }

    /// <summary>The high word of a signed 32x32 multiply, into LO and HI.</summary>
    static void MultS(CpuContext c, uint a, uint b)
    {
        long r = (long)(int)a * (int)b;
        c.LO = (uint)r;
        c.HI = (uint)(r >> 32);
    }

    /// <summary>The high word of an unsigned 32x32 multiply, into LO and HI.</summary>
    static void MultU(CpuContext c, uint a, uint b)
    {
        ulong r = (ulong)a * b;
        c.LO = (uint)r;
        c.HI = (uint)(r >> 32);
    }

    /// <summary>A signed divide, into LO and HI; MIPS leaves them alone for a zero
    /// divisor and clamps the one overflowing pair.</summary>
    static void DivS(CpuContext c, uint a, uint b)
    {
        if (b == 0u) return;
        int s = (int)a, t = (int)b;
        if (s == int.MinValue && t == -1) { c.LO = 0x80000000u; c.HI = 0u; }
        else { c.LO = (uint)(s / t); c.HI = (uint)(s % t); }
    }

    // ---- KF3_STAGE15=verify -----------------------------------------------------

    /// <summary>
    /// Records the recompiled routine at each call, then runs <see cref="Run"/> with
    /// every call replayed from the record.
    ///
    /// A pre and a post on each of the twenty-two callees take what that call was
    /// handed and what it left: the registers always, and RAM and the scratchpad where
    /// the body's own work comes next -- on entry to <see cref="Site.View"/>,
    /// <see cref="Site.CompassError"/> and <see cref="Site.HudModels"/>, which is where
    /// the two runs are compared, and on exit from <see cref="Site.Arm"/> and
    /// <see cref="Site.CompassError"/>, which is where the replay picks RAM back up.
    /// The compass call is conditional, so the record is the sequence of sites actually
    /// called; the replay must match that sequence.
    /// </summary>
    static class Verifier
    {
        static readonly int N = Calls.Length;

        public static bool Recording { get; private set; }
        public static bool Replaying { get; private set; }

        static bool? _hooked;

        static readonly int[] _seq = new int[N];
        static int _nseq;
        static readonly CpuSnapshot[] _entry = new CpuSnapshot[N], _exit = new CpuSnapshot[N];
        static readonly byte[]?[] _entryRam = new byte[]?[N], _exitRam = new byte[]?[N];
        static readonly uint[]?[] _entryPad = new uint[]?[N], _exitPad = new uint[]?[N];
        static int _next, _open, _nested;

        static byte[] _before = [], _final = [];
        static readonly uint[] _padBefore = new uint[Differential.PadWords], _padNow = new uint[Differential.PadWords];
        static readonly uint[] _finalPad = new uint[Differential.PadWords];
        static int _taken;

        static long _calls, _bad, _incomplete, _stray;
        static readonly List<string> _samples = [];
        static double _reportAt;

        static bool Compared(int site) => site is (int)Site.View or (int)Site.CompassError or (int)Site.HudModels;
        static bool Resumed(int site) => site is (int)Site.Arm or (int)Site.CompassError;

        public static void Run(Action<CpuContext, IMemory> orig, CpuContext c, PSMemory mem)
        {
            _hooked ??= Hook();
            if (_hooked == false)
            {
                orig(c, mem);
                return;
            }

            var ram = Differential.Ram(mem);
            if (_before.Length != ram.Length)
            {
                _before = new byte[ram.Length];
                _final = new byte[ram.Length];
            }
            ram.CopyTo(_before);
            Differential.SavePad(mem, _padBefore);
            var entry = c.Snapshot();

            _next = 0;
            _open = -1;
            _nested = 0;
            _nseq = 0;
            Recording = true;
            try { orig(c, mem); }
            finally { Recording = false; }
            ram.CopyTo(_final);
            Differential.SavePad(mem, _finalPad);
            var theirs = c.Snapshot();

            _calls++;
            if (_nseq < 21 || _open >= 0) _incomplete++;

            _before.CopyTo(ram);
            Differential.LoadPad(mem, _padBefore);
            c.Restore(entry);
            _taken = 0;
            Replaying = true;
            try { Stage15.Run(c, mem, stepNeedle: true); }
            finally { Replaying = false; }

            if (_taken != _nseq) Mismatch($"ours made {_taken} of {_nseq} recorded calls");
            var ours = c.Snapshot();
            if (!Differential.CalleeSavedEqual(ours, theirs))
                Mismatch($"on return: sp {theirs.SP:X}/{ours.SP:X} ra {theirs.RA:X}/{ours.RA:X}");

            // The recompiled result stands.
            _final.CopyTo(ram);
            Differential.LoadPad(mem, _finalPad);
            c.Restore(theirs);
            Report();
        }

        /// <summary>One call of ours, answered from the record at the same place in
        /// the sequence.</summary>
        public static void Take(CpuContext c, PSMemory mem, Site site)
        {
            int p = _taken;
            if (p >= _nseq)
            {
                Mismatch($"ours called {site} after the routine's {_nseq} calls");
                _taken++;
                return;
            }
            if (_seq[p] != (int)site)
                Mismatch($"ours called {site} where the routine called {(Site)_seq[p]}");

            ref readonly var theirs = ref _entry[p];
            if (c.SP != theirs.SP || c.RA != theirs.RA || !Differential.CalleeSavedEqual(c.Snapshot(), theirs))
                Mismatch($"{site}: sp {theirs.SP:X}/{c.SP:X} ra {theirs.RA:X}/{c.RA:X}");
            if (site is Site.View or Site.CompassError && (c.A0 != theirs.A0 || c.A1 != theirs.A1))
                Mismatch($"{site}: a0 {theirs.A0:X}/{c.A0:X} a1 {theirs.A1:X}/{c.A1:X}");
            if (_entryRam[p] is { } before
                && Differential.Describe(Differential.Ram(mem), before, 0, before.Length) is { } diff)
                Mismatch($"entering {site}: {diff}");
            if (_entryPad[p] is { } padBefore)
            {
                Differential.SavePad(mem, _padNow);
                if (Differential.DescribePad(_padNow, padBefore) is { } pdiff)
                    Mismatch($"entering {site}: {pdiff}");
            }

            c.Restore(_exit[p]);
            if (site == Site.SoundService)
            {
                _final.CopyTo(Differential.Ram(mem));
                Differential.LoadPad(mem, _finalPad);
            }
            else
            {
                if (_exitRam[p] is { } rb) rb.CopyTo(Differential.Ram(mem));
                if (_exitPad[p] is { } pb) Differential.LoadPad(mem, pb);
            }
            _taken++;
        }

        public static void Enter(CpuContext c, IMemory m)
        {
            if (!Recording) return;
            if (_open >= 0) { _nested++; return; }
            int j = -1;
            for (int k = _next; k < N; k++) if (Calls[k].Return == c.RA) { j = k; break; }
            if (j < 0) { _stray++; return; }
            _next = j + 1;
            int p = _nseq++;
            _seq[p] = j;
            _entry[p] = c.Snapshot();
            if (Compared(j))
            {
                _entryRam[p] = new byte[_before.Length];
                _entryPad[p] = new uint[Differential.PadWords];
                Differential.Ram((PSMemory)m).CopyTo(_entryRam[p]!);
                Differential.SavePad((PSMemory)m, _entryPad[p]!);
            }
            else { _entryRam[p] = null; _entryPad[p] = null; }
            _open = p;
        }

        public static void Exit(CpuContext c, IMemory m)
        {
            if (!Recording) return;
            if (_nested > 0) { _nested--; return; }
            if (_open < 0) return;
            int p = _open;
            _exit[p] = c.Snapshot();
            if (Resumed(_seq[p]))
            {
                _exitRam[p] = new byte[_before.Length];
                _exitPad[p] = new uint[Differential.PadWords];
                Differential.Ram((PSMemory)m).CopyTo(_exitRam[p]!);
                Differential.SavePad((PSMemory)m, _exitPad[p]!);
            }
            else { _exitRam[p] = null; _exitPad[p] = null; }
            _open = -1;
        }

        static bool Hook()
        {
            var enter = typeof(Verifier).GetMethod(nameof(Enter), BindingFlags.Public | BindingFlags.Static)!;
            var exit = typeof(Verifier).GetMethod(nameof(Exit), BindingFlags.Public | BindingFlags.Static)!;
            var targets = new MethodInfo[N];
            for (int i = 0; i < N; i++)
            {
                var mi = SymbolRegistry.Resolve("game", null, Calls[i].Callee);
                if (mi == null)
                {
                    Console.WriteLine("[stage15] verify: a callee is not mapped; not verifying");
                    return false;
                }
                targets[i] = mi;
            }
            foreach (var t in targets)
            {
                HookManager.AddPre(_self, t, enter, int.MinValue);
                HookManager.AddPost(_self, t, exit, int.MaxValue);
            }
            HookManager.Commit();
            bool ok = targets.All(HookAttach.Installed);
            Console.WriteLine(ok ? "[stage15] verify: recording every call"
                                 : "[stage15] verify: a callee could not be hooked; not verifying");
            return ok;
        }

        static void Mismatch(string what)
        {
            _bad++;
            Sample(what);
        }

        static void Sample(string s)
        {
            if (_samples.Count < 8) _samples.Add(s);
        }

        static void Report()
        {
            double now = Environment.TickCount64 / 1000.0;
            if (now < _reportAt) return;
            _reportAt = now + 2.0;
            Console.WriteLine($"[stage15] verify func_800422B8: {_calls} call(s), {_bad} mismatch(es), " +
                              $"{_incomplete} incomplete record(s), {_stray} stray call(s) ignored");
            foreach (var s in _samples) Console.WriteLine($"[stage15]   {s}");
            _samples.Clear();
            _calls = _bad = _incomplete = _stray = 0;
        }
    }
}
