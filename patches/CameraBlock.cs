using RecompOne.Runtime;
using RecompOne.Runtime.Context;
using RecompOne.Runtime.Memory;
using RecompOne.Runtime.Modding;
using Game = Recompiled.KingsField3_game;

namespace Kf3;

/// <summary>
/// The camera stage 15 draws with, as a value: <c>func_800357E8</c>'s two arguments,
/// a VECTOR position in world units (Y down) and an SVECTOR of angles, 0x1000 a turn.
/// </summary>
public readonly record struct Camera(int X, int Y, int Z, short Pitch, short Yaw, short Roll)
{
    /// <summary>The camera the block holds: the one the last frame was drawn with.</summary>
    public static Camera Read(IMemory m) => new(
        (int)m.ReadU32(CameraBlock.Position),
        (int)m.ReadU32(CameraBlock.Position + 4u),
        (int)m.ReadU32(CameraBlock.Position + 8u),
        (short)m.ReadU16(CameraBlock.Angles),
        (short)m.ReadU16(CameraBlock.Angles + 2u),
        (short)m.ReadU16(CameraBlock.Angles + 4u));

    /// <summary>The camera a VECTOR and an SVECTOR in guest RAM hold.</summary>
    public static Camera Read(IMemory m, uint pos, uint rot) => new(
        (int)m.ReadU32(pos), (int)m.ReadU32(pos + 4u), (int)m.ReadU32(pos + 8u),
        (short)m.ReadU16(rot), (short)m.ReadU16(rot + 2u), (short)m.ReadU16(rot + 4u));
}

/// <summary>
/// <c>func_800357E8(VECTOR *pos, SVECTOR *rot)</c> in C#: stage 15's first call, and
/// the one place the frame's view comes from.
///
///     KF3_CAMERABLOCK=1        this transcription
///     KF3_CAMERABLOCK=0        the recompiled routine (the default until verify reads clean)
///     KF3_CAMERABLOCK=verify   run both on every call and compare
///
/// A non-null <c>pos</c> is copied to <see cref="Position"/> and its tile derived; a
/// non-null <c>rot</c> to <see cref="Angles"/>; then <c>RotMatrix</c> builds the view
/// from the stored angles (no yaw negation, unlike Verdite2's), <c>func_80016598</c>
/// the pitch matrix, and <c>func_80016290</c> the four view-times-rotation matrices
/// the map's halves load. See "The camera block" in docs/GAME_INTERNALS.md.
/// </summary>
public static class CameraBlock
{
    const uint Routine = 0x800357E8;

    /// <summary>The view matrix: rotation, then the translation at +0x14.</summary>
    public const uint ViewMatrix = 0x801AEB4C;

    /// <summary>The four view-times-rotation matrices, 0x20 bytes each.</summary>
    public const uint Turned = 0x801AEB8C;

    /// <summary>The pitch-only matrix.</summary>
    public const uint PitchMatrix = 0x801AEC0C;

    /// <summary>VECTOR: X, Y, Z, and a pad word copied with them.</summary>
    public const uint Position = 0x801AEC4C;

    /// <summary>SVECTOR: pitch, yaw, roll, and a pad halfword copied with them.</summary>
    public const uint Angles = 0x801AEC5C;

    /// <summary>The eye's tile, <c>X &gt;&gt; 11</c> and <c>Z &gt;&gt; 11</c>.</summary>
    public const uint TileX = 0x801AEC64, TileZ = 0x801AEC68;

    public const uint Start = ViewMatrix, Bytes = TileZ + 4u - Start;

    enum Mode { Off, On, Verify }
    static Mode _mode = Mode.Off;
    static bool _queued;

    static readonly Differential _check = new("camerablock", "func_800357E8", 0x400);

    static readonly ModInfo _self = new()
    {
        Id = "kf3.camerablock",
        Name = "Camera block",
        Version = "1.0",
        Description = "func_800357E8, the view stage 15 draws with, in C#.",
    };

    public static bool InCSharp => _mode == Mode.On;

    public static void Configure(string? mode)
    {
        _mode = mode?.Trim().ToLowerInvariant() switch
        {
            "1" or "on" => Mode.On,
            "verify" => Mode.Verify,
            _ => Mode.Off,
        };
    }

    public static void Install()
    {
        if (_mode == Mode.Off) return;
        HookAttach.OnOverlayLoad("camera block", Attach);
    }

    static bool Attach()
    {
        SymbolRegistry.Build();
        var target = SymbolRegistry.Resolve("game", null, Routine);
        if (target == null) return false;
        if (!_queued)
        {
            var impl = typeof(CameraBlock).GetMethod(nameof(Replace),
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
            _queued = HookManager.AddReplace(_self, target, impl);
            if (!_queued) return false;
        }
        HookManager.Commit();
        bool ok = HookAttach.Installed(target);
        Console.WriteLine(ok ? $"[KF3] camera block: {_mode.ToString().ToLowerInvariant()}"
                             : "[KF3] camera block: not installed");
        return ok;
    }

    static void Replace(Action<CpuContext, IMemory> orig, CpuContext c, IMemory m)
    {
        // PGXP's RAM shadow is kept by the recompiled stores, which C# stores skip.
        if (RecompOne.Runtime.Pgxp.Pgxp.CpuTracking || m is not PSMemory mem)
        {
            orig(c, m);
            return;
        }
        if (_mode == Mode.Verify) _check.Run(orig, c, mem, Run);
        else Run(c, mem);
    }

    /// <summary>Write a camera into the block as the routine's two copies would, the
    /// pad fields left alone. The view is not rebuilt; <see cref="Build"/> does that.</summary>
    public static void Store(IMemory m, in Camera cam)
    {
        m.WriteU32(Position, (uint)cam.X);
        m.WriteU32(Position + 4u, (uint)cam.Y);
        m.WriteU32(Position + 8u, (uint)cam.Z);
        m.WriteU32(TileX, (uint)(cam.X >> 11));
        m.WriteU32(TileZ, (uint)(cam.Z >> 11));
        m.WriteU16(Angles, (ushort)cam.Pitch);
        m.WriteU16(Angles + 2u, (ushort)cam.Yaw);
        m.WriteU16(Angles + 4u, (ushort)cam.Roll);
    }

    /// <summary>Make <paramref name="cam"/> the view: store it and rebuild the matrices
    /// through the routine, so every hook on it sees the call.</summary>
    public static void Build(CpuContext c, IMemory m, in Camera cam)
    {
        Store(m, cam);
        c.A0 = 0u;
        c.A1 = 0u;
        Game.func_800357E8(c, m);
    }

    /// <summary>The routine, transcribed: its frame, its two copies, its six calls.</summary>
    static void Run(CpuContext c, PSMemory mem)
    {
        uint sp = c.SP - 0x38u;
        c.SP = sp;
        uint pos = c.A0, rot = c.A1;
        mem.WriteU32(sp + 0x30u, c.RA);
        mem.WriteU32(sp + 0x2Cu, c.S1);
        mem.WriteU32(sp + 0x28u, c.S0);

        if (pos != 0u)
        {
            uint x = mem.ReadU32(pos), y = mem.ReadU32(pos + 4u), z = mem.ReadU32(pos + 8u), pad = mem.ReadU32(pos + 12u);
            mem.WriteU32(Position, x);
            mem.WriteU32(Position + 4u, y);
            mem.WriteU32(Position + 8u, z);
            mem.WriteU32(Position + 12u, pad);
            mem.WriteU32(TileX, (uint)((int)mem.ReadU32(Position) >> 11));
            mem.WriteU32(TileZ, (uint)((int)mem.ReadU32(Position + 8u) >> 11));
        }

        if (rot != 0u)
        {
            // lwl/lwr pairs: the SVECTOR need not be word-aligned.
            Span<byte> angles = stackalloc byte[8];
            for (int i = 0; i < 8; i++) angles[i] = mem.ReadU8(rot + (uint)i);
            for (int i = 0; i < 8; i++) mem.WriteU8(Angles + (uint)i, angles[i]);
        }

        uint turn = sp + 0x10u;
        c.S0 = Angles;
        c.S1 = ViewMatrix;
        ushort pitch = mem.ReadU16(Angles), roll = mem.ReadU16(Angles + 4u), yaw = mem.ReadU16(Angles + 2u);
        mem.WriteU16(turn, pitch);
        mem.WriteU16(turn + 4u, roll);
        mem.WriteU16(turn + 2u, yaw);

        c.A0 = turn;
        c.A1 = ViewMatrix;
        c.RA = 0x800358B8u;
        Game.RotMatrix(c, mem);
        c.A0 = (uint)(short)mem.ReadU16(turn);
        c.A1 = PitchMatrix;
        c.RA = 0x800358C4u;
        Game.func_80016598(c, mem);
        for (uint k = 0; k < 4; k++)
        {
            c.A0 = ViewMatrix;
            c.A1 = Turned + 0x20u * k;
            c.A2 = k;
            c.RA = 0x800358D4u + 0x10u * k;
            Game.func_80016290(c, mem);
        }

        c.RA = mem.ReadU32(sp + 0x30u);
        c.S1 = mem.ReadU32(sp + 0x2Cu);
        c.S0 = mem.ReadU32(sp + 0x28u);
        c.SP = sp + 0x38u;
    }
}
