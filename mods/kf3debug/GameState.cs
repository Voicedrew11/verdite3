// ModCompiler compiles mods with no implicit usings, so every namespace the
// file needs must be named here -- including System.
using System;
using RecompOne.Runtime.Memory;

namespace Kf3.Mods.Debug;

/// <summary>
/// GAME.EXE's player state, by address, with signed accessors.
///
/// The block at 0x801B24E4 is the player: "The player" in docs/GAME_INTERNALS.md
/// has the map. Main-loop stage 4, func_80030FCC, is the player's tick (pad,
/// look, walk, gravity, the action-state dispatch), so a post-hook on it is the
/// last word before stage 10 copies the player into the camera.
/// </summary>
internal static class GameState
{
    /// <summary>Main-loop stage 4: the player's tick. Every feature here posts on it.</summary>
    internal const uint PlayerStage = 0x80030FCC;

    // ---- position and view ----
    internal const uint PosX      = 0x801B25F0;   // s32
    internal const uint PosY      = 0x801B25F4;   // s32, height; negative is up
    internal const uint PosZ      = 0x801B25F8;   // s32

    // Base angles: what the look and turn routines step. 12-bit, 0x1000 a turn.
    internal const uint Pitch     = 0x801B2610;   // s16
    internal const uint Yaw       = 0x801B2612;   // s16, increases turning LEFT
    internal const uint Roll      = 0x801B2614;   // s16

    // The composed view (base plus deltas), which stage 10 hands the camera.
    internal const uint ViewPitch = 0x801B2608;   // s16
    internal const uint ViewYaw   = 0x801B260A;   // s16
    internal const uint ViewRoll  = 0x801B260C;   // s16

    // Per-tick control velocities, applied by the game's own code.
    internal const uint StrafeVel = 0x801B2646;   // s16
    internal const uint FwdVel    = 0x801B2648;   // s16
    internal const uint WalkMag   = 0x801B264A;   // s16
    internal const uint TurnVel   = 0x801B264C;   // s16
    internal const uint PitchVel  = 0x801B264E;   // s16

    // This tick's rates, re-derived by stage 4 before the walk and look calls:
    // walk 0xC8; turn 0x28 standing, 0x20 moving.
    internal const uint MoveSpeed = 0x801B2664;   // s32
    internal const uint TurnRate  = 0x801B2668;   // s32

    internal const uint FallVel   = 0x801B2656;   // s16
    internal const uint VertState = 0x801B25E8;   // u8, the fall/rise sub-state

    // The pad word stage 4 stored this tick: active high, bytes swapped against
    // the PSX order (Up 0x1000, Right 0x2000, Down 0x4000, Left 0x8000).
    internal const uint Pad       = 0x801B265C;   // u16

    // The camera's copies, written by stage 10; only needed for a move made
    // outside stage 4.
    internal const uint CamPos    = 0x801AEC4C;   // s32 x, y, z
    internal const uint CamAngles = 0x801AEC5C;   // s16 pitch, yaw, roll

    // ---- character ----
    internal const uint Exp       = 0x801B24E4;   // u32, capped at 999999
    internal const uint ExpNext   = 0x801B24E8;   // u32
    internal const uint Level     = 0x801B24F0;   // u8
    internal const uint MaxHp     = 0x801B24FA;   // u16
    internal const uint Hp        = 0x801B24FC;   // u16
    internal const uint MaxMp     = 0x801B24FE;   // u16
    internal const uint Mp        = 0x801B2500;   // u16

    // The action state, dispatched through the table at 0x80011AC0 in stage 4.
    // 0x11 is dead, written only by the death latch func_80030A6C.
    internal const uint State     = 0x801B25E5;   // u8
    internal const byte StateDead = 0x11;
    internal const uint DeathClock = 0x801B261E;  // s16, +1 a tick while dead

    // The area descriptor's first byte: FDAT entries 3n..3n+2. 0 is fdat02.
    internal const uint Area        = 0x8018FAE4; // u8
    internal const uint CurrentSlot = 0x8009C2C0; // u8, the card slot last loaded

    internal const int AngleMask = 0xFFF;
    internal const int AngleFull = 0x1000;

    /// <summary>The pitch limit the look routine func_8002F5C0 holds itself to.</summary>
    internal const int PitchLimit = 0x2BC;

    // ---- typed reads ----

    internal static int   ReadS32(IMemory m, uint a) => (int)m.ReadU32(a);
    internal static short ReadS16(IMemory m, uint a) => (short)m.ReadU16(a);

    internal static void WriteS32(IMemory m, uint a, int v) => m.WriteU32(a, (uint)v);
    internal static void WriteS16(IMemory m, uint a, int v) => m.WriteU16(a, (ushort)(short)v);

    /// <summary>A view angle as 12-bit signed, in [-2048, 2047].</summary>
    internal static int ReadAngle12(IMemory m, uint a)
    {
        int v = m.ReadU16(a) & AngleMask;
        return v >= AngleFull / 2 ? v - AngleFull : v;
    }

    internal static void WriteAngle12(IMemory m, uint a, int v) => m.WriteU16(a, (ushort)(v & AngleMask));

    internal static (int X, int Y, int Z) Position(IMemory m) =>
        (ReadS32(m, PosX), ReadS32(m, PosY), ReadS32(m, PosZ));

    internal static void SetPosition(IMemory m, int x, int y, int z)
    {
        WriteS32(m, PosX, x);
        WriteS32(m, PosY, y);
        WriteS32(m, PosZ, z);
    }

    /// <summary>The base angles, which is what a warp should write.</summary>
    internal static (int Pitch, int Yaw, int Roll) Angles(IMemory m) =>
        (ReadS16(m, Pitch), ReadS16(m, Yaw), ReadS16(m, Roll));

    /// <summary>Set the base angles and the composed view with them, so the next frame is already right.</summary>
    internal static void SetAngles(IMemory m, int pitch, int yaw, int roll)
    {
        yaw &= AngleMask;
        WriteS16(m, Pitch, pitch);
        WriteS16(m, Yaw, yaw);
        WriteS16(m, Roll, roll);
        WriteS16(m, ViewPitch, pitch);
        WriteS16(m, ViewYaw, yaw);
        WriteS16(m, ViewRoll, roll);
    }

    /// <summary>Drop every movement velocity, so a teleport does not arrive still walking.</summary>
    internal static void StopMotion(IMemory m)
    {
        m.WriteU16(StrafeVel, 0);
        m.WriteU16(FwdVel, 0);
        m.WriteU16(WalkMag, 0);
        m.WriteU16(TurnVel, 0);
        m.WriteU16(PitchVel, 0);
        m.WriteU16(FallVel, 0);
    }

    /// <summary>
    /// Is there a character in an area? Max HP, not HP: the block is cleared
    /// until a session starts, and HP is legitimately 0 on the frame you die.
    /// </summary>
    internal static bool IsInGame(IMemory m) => m.ReadU16(MaxHp) != 0;

    internal static bool IsDead(IMemory m) => m.ReadU8(State) == StateDead;

    internal static float AngleToRadians(int angle) =>
        angle * (2f * MathF.PI / AngleFull);
}
