using RecompOne.Runtime;
using RecompOne.Runtime.Context;
using RecompOne.Runtime.Memory;
using Game = Recompiled.KingsField3_game;

namespace Kf3;

/// <summary>
/// The four libgte division routines and their packet emitters, transcribed
/// literally from the recompiled bodies in generated/game.cs (the entries and
/// the bodies func_80074D90, func_80075190, func_800756B0, func_80075B50, and
/// the emitters func_80075104, func_80075618, func_80075AB4, func_800760B4).
/// Registers stay in the CpuContext; the same memory accesses, GTE operations
/// and branches run in the same order. See patches/NearPath.cs.
/// </summary>
public static partial class NearPath
{
    static void BiosBreak(CpuContext c, IMemory m) { }

    // transcribed from generated/game.cs:167809
    static void DivTri(CpuContext c, IMemory m)
    {
        NearScreen.Widen((PSMemory)m, c.A1, NearScreen.Kind.Tri);
        { var _v = c.A1; c.A3 = c.A1 + 0x60u; }
        NoteCorners((PSMemory)m, c.A3 + 0x48u, 3);
        c.A2 = 0x00000000u;
        DivTriBody(c, m);
    }
    // transcribed from generated/game.cs:167816
    static void DivTriBody(CpuContext c, IMemory m)
    {
        var mem = (PSMemory)m;
        { var _a = (c.A3 + 0x48u); c.T0 = mem.ReadU32(_a); }
        { var _a = (c.A3 + 0x4Cu); c.T1 = mem.ReadU32(_a); }
        { var _a = (c.A3 + 0x50u); c.T2 = mem.ReadU32(_a); }
        c.T9 = Gte.ReadControl(26);
        { var _a = (c.T0 + 0x14u); c.T4 = mem.ReadU32(_a); }
        { var _a = (c.T1 + 0x14u); c.T5 = mem.ReadU32(_a); }
        { var _a = (c.T2 + 0x14u); c.T6 = mem.ReadU32(_a); }
        { var _v = c.T9; c.T8 = (uint)((int)c.T9 >> 1); }
        { var _s = c.T4; var _t = c.T8; c.At = _s < _t ? 1u : 0u; }
        if (c.At == 0u) {
            { var _s = c.T5; var _t = c.T8; c.At = _s < _t ? 1u : 0u; }
            goto L80074DD8;
        }
        { var _s = c.T5; var _t = c.T8; c.At = _s < _t ? 1u : 0u; }
        if (c.At == 0u) {
            { var _s = c.T6; var _t = c.T8; c.At = _s < _t ? 1u : 0u; }
            goto L80074DD8;
        }
        { var _s = c.T6; var _t = c.T8; c.At = _s < _t ? 1u : 0u; }
        if (c.At == 0u) {
            goto L80074DD8;
        }
        c.V0 = c.A0;
        return;
        L80074DD8: ;
        c.T9 = Gte.ReadControl(24);
        { var _a = (c.A1 + 0x4u); c.V0 = mem.ReadU32(_a); }
        { var _a = (c.A1 + 0x8u); c.V1 = mem.ReadU32(_a); }
        { var _v = c.T9; c.T9 = (uint)((int)c.T9 >> 16); }
        { var _v = c.V0; c.V0 = c.V0 >> 1; }
        { var _v = c.V1; c.V1 = c.V1 >> 1; }
        { var _s = c.T9; var _t = c.V0; c.T8 = _s + _t; }
        { var _a = (c.T0 + 0x10u); c.T4 = (uint)(short)mem.ReadU16(_a); }
        { var _a = (c.T1 + 0x10u); c.T5 = (uint)(short)mem.ReadU16(_a); }
        { var _a = (c.T2 + 0x10u); c.T6 = (uint)(short)mem.ReadU16(_a); }
        { var _s = c.T8; var _t = c.T4; c.At = (int)_s < (int)_t ? 1u : 0u; }
        if (c.At == 0u) {
            { var _s = c.T8; var _t = c.T5; c.At = (int)_s < (int)_t ? 1u : 0u; }
            goto L80074E28;
        }
        { var _s = c.T8; var _t = c.T5; c.At = (int)_s < (int)_t ? 1u : 0u; }
        if (c.At == 0u) {
            { var _s = c.T8; var _t = c.T6; c.At = (int)_s < (int)_t ? 1u : 0u; }
            goto L80074E28;
        }
        { var _s = c.T8; var _t = c.T6; c.At = (int)_s < (int)_t ? 1u : 0u; }
        if (c.At == 0u) {
            goto L80074E28;
        }
        c.V0 = c.A0;
        return;
        L80074E28: ;
        { var _s = c.T9; var _t = c.V0; c.T8 = _s - _t; }
        { var _s = c.T4; var _t = c.T8; c.At = (int)_s < (int)_t ? 1u : 0u; }
        if (c.At == 0u) {
            { var _s = c.T5; var _t = c.T8; c.At = (int)_s < (int)_t ? 1u : 0u; }
            goto L80074E54;
        }
        { var _s = c.T5; var _t = c.T8; c.At = (int)_s < (int)_t ? 1u : 0u; }
        if (c.At == 0u) {
            { var _s = c.T6; var _t = c.T8; c.At = (int)_s < (int)_t ? 1u : 0u; }
            goto L80074E54;
        }
        { var _s = c.T6; var _t = c.T8; c.At = (int)_s < (int)_t ? 1u : 0u; }
        if (c.At == 0u) {
            goto L80074E54;
        }
        c.V0 = c.A0;
        return;
        L80074E54: ;
        c.T9 = Gte.ReadControl(25);
        { var _a = (c.T0 + 0x12u); c.T4 = (uint)(short)mem.ReadU16(_a); }
        { var _a = (c.T1 + 0x12u); c.T5 = (uint)(short)mem.ReadU16(_a); }
        { var _a = (c.T2 + 0x12u); c.T6 = (uint)(short)mem.ReadU16(_a); }
        { var _v = c.T9; c.T9 = (uint)((int)c.T9 >> 16); }
        { var _s = c.T9; var _t = c.V1; c.T8 = _s + _t; }
        { var _s = c.T8; var _t = c.T4; c.At = (int)_s < (int)_t ? 1u : 0u; }
        if (c.At == 0u) {
            { var _s = c.T8; var _t = c.T5; c.At = (int)_s < (int)_t ? 1u : 0u; }
            goto L80074E94;
        }
        { var _s = c.T8; var _t = c.T5; c.At = (int)_s < (int)_t ? 1u : 0u; }
        if (c.At == 0u) {
            { var _s = c.T8; var _t = c.T6; c.At = (int)_s < (int)_t ? 1u : 0u; }
            goto L80074E94;
        }
        { var _s = c.T8; var _t = c.T6; c.At = (int)_s < (int)_t ? 1u : 0u; }
        if (c.At == 0u) {
            goto L80074E94;
        }
        c.V0 = c.A0;
        return;
        L80074E94: ;
        { var _s = c.T9; var _t = c.V1; c.T8 = _s - _t; }
        { var _s = c.T4; var _t = c.T8; c.At = (int)_s < (int)_t ? 1u : 0u; }
        if (c.At == 0u) {
            { var _s = c.T5; var _t = c.T8; c.At = (int)_s < (int)_t ? 1u : 0u; }
            goto L80074EC0;
        }
        { var _s = c.T5; var _t = c.T8; c.At = (int)_s < (int)_t ? 1u : 0u; }
        if (c.At == 0u) {
            { var _s = c.T6; var _t = c.T8; c.At = (int)_s < (int)_t ? 1u : 0u; }
            goto L80074EC0;
        }
        { var _s = c.T6; var _t = c.T8; c.At = (int)_s < (int)_t ? 1u : 0u; }
        if (c.At == 0u) {
            goto L80074EC0;
        }
        c.V0 = c.A0;
        return;
        L80074EC0: ;
        { var _a = c.T0; c.T4 = (uint)(short)mem.ReadU16(_a); }
        { var _a = c.T1; c.T5 = (uint)(short)mem.ReadU16(_a); }
        { var _a = c.T2; c.T6 = (uint)(short)mem.ReadU16(_a); }
        { var _s = c.T4; var _t = c.T5; c.T7 = _s + _t; }
        { var _s = c.T5; var _t = c.T6; c.T8 = _s + _t; }
        { var _s = c.T6; var _t = c.T4; c.T9 = _s + _t; }
        { var _v = c.T7; c.T7 = (uint)((int)c.T7 >> 1); }
        { var _v = c.T8; c.T8 = (uint)((int)c.T8 >> 1); }
        { var _v = c.T9; c.T9 = (uint)((int)c.T9 >> 1); }
        { var _a = c.A3; mem.WriteU16(_a, (ushort)c.T7); }
        { var _a = (c.A3 + 0x18u); mem.WriteU16(_a, (ushort)c.T8); }
        { var _a = (c.A3 + 0x30u); mem.WriteU16(_a, (ushort)c.T9); }
        { var _a = (c.T0 + 0x2u); c.T4 = (uint)(short)mem.ReadU16(_a); }
        { var _a = (c.T1 + 0x2u); c.T5 = (uint)(short)mem.ReadU16(_a); }
        { var _a = (c.T2 + 0x2u); c.T6 = (uint)(short)mem.ReadU16(_a); }
        { var _s = c.T4; var _t = c.T5; c.T7 = _s + _t; }
        { var _s = c.T5; var _t = c.T6; c.T8 = _s + _t; }
        { var _s = c.T6; var _t = c.T4; c.T9 = _s + _t; }
        { var _v = c.T7; c.T7 = (uint)((int)c.T7 >> 1); }
        { var _v = c.T8; c.T8 = (uint)((int)c.T8 >> 1); }
        { var _v = c.T9; c.T9 = (uint)((int)c.T9 >> 1); }
        { var _a = (c.A3 + 0x2u); mem.WriteU16(_a, (ushort)c.T7); }
        { var _a = (c.A3 + 0x1Au); mem.WriteU16(_a, (ushort)c.T8); }
        { var _a = (c.A3 + 0x32u); mem.WriteU16(_a, (ushort)c.T9); }
        { var _a = (c.T0 + 0x4u); c.T4 = (uint)(short)mem.ReadU16(_a); }
        { var _a = (c.T1 + 0x4u); c.T5 = (uint)(short)mem.ReadU16(_a); }
        { var _a = (c.T2 + 0x4u); c.T6 = (uint)(short)mem.ReadU16(_a); }
        { var _s = c.T4; var _t = c.T5; c.T7 = _s + _t; }
        { var _s = c.T5; var _t = c.T6; c.T8 = _s + _t; }
        { var _s = c.T6; var _t = c.T4; c.T9 = _s + _t; }
        { var _v = c.T7; c.T7 = (uint)((int)c.T7 >> 1); }
        { var _v = c.T8; c.T8 = (uint)((int)c.T8 >> 1); }
        { var _v = c.T9; c.T9 = (uint)((int)c.T9 >> 1); }
        { var _a = (c.A3 + 0x4u); mem.WriteU16(_a, (ushort)c.T7); }
        { var _a = (c.A3 + 0x1Cu); mem.WriteU16(_a, (ushort)c.T8); }
        { var _a = (c.A3 + 0x34u); mem.WriteU16(_a, (ushort)c.T9); }
        { var _a = c.A3; var _lw = mem.ReadU32(_a); Gte.Write(0, _lw); }
        { var _a = (c.A3 + 0x4u); var _lw = mem.ReadU32(_a); Gte.Write(1, _lw); }
        { var _a = (c.A3 + 0x18u); var _lw = mem.ReadU32(_a); Gte.Write(2, _lw); }
        { var _a = (c.A3 + 0x1Cu); var _lw = mem.ReadU32(_a); Gte.Write(3, _lw); }
        { var _a = (c.A3 + 0x30u); var _lw = mem.ReadU32(_a); Gte.Write(4, _lw); }
        { var _a = (c.A3 + 0x34u); var _lw = mem.ReadU32(_a); Gte.Write(5, _lw); }
        { var _a = (c.T0 + 0x8u); c.T4 = mem.ReadU8(_a); }
        Gte.Rtpt(12, false);
        if (DepthRecording)
        {
            NoteSz(c.A3, Gte.Read(12), Gte.Read(17));
            NoteSz(c.A3 + 0x18u, Gte.Read(13), Gte.Read(18));
            NoteSz(c.A3 + 0x30u, Gte.Read(14), Gte.Read(19));
        }
        { var _a = (c.T1 + 0x8u); c.T5 = mem.ReadU8(_a); }
        { var _a = (c.T2 + 0x8u); c.T6 = mem.ReadU8(_a); }
        { var _s = c.T4; var _t = c.T5; c.T7 = _s + _t; }
        { var _s = c.T5; var _t = c.T6; c.T8 = _s + _t; }
        { var _s = c.T6; var _t = c.T4; c.T9 = _s + _t; }
        { var _v = c.T7; c.T7 = c.T7 >> 1; }
        { var _v = c.T8; c.T8 = c.T8 >> 1; }
        { var _v = c.T9; c.T9 = c.T9 >> 1; }
        { var _a = (c.A3 + 0x8u); mem.WriteU8(_a, (byte)c.T7); }
        { var _a = (c.A3 + 0x20u); mem.WriteU8(_a, (byte)c.T8); }
        { var _a = (c.A3 + 0x38u); mem.WriteU8(_a, (byte)c.T9); }
        { var _a = (c.T0 + 0x9u); c.T4 = mem.ReadU8(_a); }
        { var _a = (c.T1 + 0x9u); c.T5 = mem.ReadU8(_a); }
        { var _a = (c.T2 + 0x9u); c.T6 = mem.ReadU8(_a); }
        { var _s = c.T4; var _t = c.T5; c.T7 = _s + _t; }
        { var _s = c.T5; var _t = c.T6; c.T8 = _s + _t; }
        { var _s = c.T6; var _t = c.T4; c.T9 = _s + _t; }
        { var _v = c.T7; c.T7 = c.T7 >> 1; }
        { var _v = c.T8; c.T8 = c.T8 >> 1; }
        { var _v = c.T9; c.T9 = c.T9 >> 1; }
        { var _a = (c.A3 + 0x9u); mem.WriteU8(_a, (byte)c.T7); }
        { var _a = (c.A3 + 0x21u); mem.WriteU8(_a, (byte)c.T8); }
        { var _a = (c.A3 + 0x39u); mem.WriteU8(_a, (byte)c.T9); }
        { var _a = c.A1; c.T4 = mem.ReadU32(_a); }
        { var _v = c.A2; c.A2 = c.A2 + 0x1u; }
        if (c.T4 != c.A2) {
            goto L8007504C;
        }
        { var _a = (c.A3 + 0x10u); var _sw = Gte.Read(12); mem.WriteU32(_a, _sw); }
        { var _a = (c.A3 + 0x28u); var _sw = Gte.Read(13); mem.WriteU32(_a, _sw); }
        { var _a = (c.A3 + 0x40u); var _sw = Gte.Read(14); mem.WriteU32(_a, _sw); }
        c.V1 = c.RA;
        { var _a = (c.A3 + 0x4Cu); c.T0 = mem.ReadU32(_a); }
        { var _v = c.A3; c.T1 = c.A3 + 0x18u; }
        { var _v = c.A3; c.T2 = c.A3 + 0x0u; }
        c.RA = 0x80075000u;
        EmitTriMap(c, m);
        { var _v = c.A3; c.T0 = c.A3 + 0x0u; }
        { var _v = c.A3; c.T1 = c.A3 + 0x18u; }
        { var _v = c.A3; c.T2 = c.A3 + 0x30u; }
        c.RA = 0x80075014u;
        EmitTriMap(c, m);
        { var _a = (c.A3 + 0x48u); c.T0 = mem.ReadU32(_a); }
        { var _v = c.A3; c.T1 = c.A3 + 0x0u; }
        { var _v = c.A3; c.T2 = c.A3 + 0x30u; }
        c.RA = 0x80075028u;
        EmitTriMap(c, m);
        { var _a = (c.A3 + 0x50u); c.T0 = mem.ReadU32(_a); }
        { var _v = c.A3; c.T1 = c.A3 + 0x30u; }
        { var _v = c.A3; c.T2 = c.A3 + 0x18u; }
        c.RA = 0x8007503Cu;
        EmitTriMap(c, m);
        c.RA = c.V1;
        { var _v = c.A2; c.A2 = c.A2 - 0x1u; }
        goto L800750F8;
        L8007504C: ;
        { var _a = (c.A3 + 0x14u); var _sw = Gte.Read(17); mem.WriteU32(_a, _sw); }
        { var _a = (c.A3 + 0x2Cu); var _sw = Gte.Read(18); mem.WriteU32(_a, _sw); }
        { var _a = (c.A3 + 0x44u); var _sw = Gte.Read(19); mem.WriteU32(_a, _sw); }
        { var _a = (c.A3 + 0x10u); var _sw = Gte.Read(12); mem.WriteU32(_a, _sw); }
        { var _a = (c.A3 + 0x28u); var _sw = Gte.Read(13); mem.WriteU32(_a, _sw); }
        { var _a = (c.A3 + 0x40u); var _sw = Gte.Read(14); mem.WriteU32(_a, _sw); }
        { var _v = c.A3; c.A3 = c.A3 + 0x58u; }
        { var _a = (c.A3 + 0x54u); mem.WriteU32(_a, c.RA); }
        { var _a = (c.A3 - 0x10u); c.T4 = mem.ReadU32(_a); }
        { var _v = c.A3; c.T5 = c.A3 - 0x58u; }
        { var _v = c.A3; c.T6 = c.A3 - 0x28u; }
        { var _a = (c.A3 + 0x48u); mem.WriteU32(_a, c.T4); }
        { var _a = (c.A3 + 0x4Cu); mem.WriteU32(_a, c.T5); }
        { var _a = (c.A3 + 0x50u); mem.WriteU32(_a, c.T6); }
        c.RA = 0x8007508Cu;
        DivTriBody(c, m);
        { var _a = (c.A3 - 0xCu); c.T4 = mem.ReadU32(_a); }
        { var _v = c.A3; c.T5 = c.A3 - 0x40u; }
        { var _v = c.A3; c.T6 = c.A3 - 0x58u; }
        { var _a = (c.A3 + 0x48u); mem.WriteU32(_a, c.T4); }
        { var _a = (c.A3 + 0x4Cu); mem.WriteU32(_a, c.T5); }
        { var _a = (c.A3 + 0x50u); mem.WriteU32(_a, c.T6); }
        c.RA = 0x800750ACu;
        DivTriBody(c, m);
        { var _a = (c.A3 - 0x8u); c.T4 = mem.ReadU32(_a); }
        { var _v = c.A3; c.T5 = c.A3 - 0x28u; }
        { var _v = c.A3; c.T6 = c.A3 - 0x40u; }
        { var _a = (c.A3 + 0x48u); mem.WriteU32(_a, c.T4); }
        { var _a = (c.A3 + 0x4Cu); mem.WriteU32(_a, c.T5); }
        { var _a = (c.A3 + 0x50u); mem.WriteU32(_a, c.T6); }
        c.RA = 0x800750CCu;
        DivTriBody(c, m);
        { var _v = c.A3; c.T4 = c.A3 - 0x58u; }
        { var _v = c.A3; c.T5 = c.A3 - 0x40u; }
        { var _v = c.A3; c.T6 = c.A3 - 0x28u; }
        { var _a = (c.A3 + 0x48u); mem.WriteU32(_a, c.T4); }
        { var _a = (c.A3 + 0x4Cu); mem.WriteU32(_a, c.T5); }
        { var _a = (c.A3 + 0x50u); mem.WriteU32(_a, c.T6); }
        c.RA = 0x800750ECu;
        DivTriBody(c, m);
        { var _a = (c.A3 + 0x54u); c.RA = mem.ReadU32(_a); }
        { var _v = c.A3; c.A3 = c.A3 - 0x58u; }
        { var _v = c.A2; c.A2 = c.A2 - 0x1u; }
        L800750F8: ;
        c.V0 = c.A0;
        return;
    }
    // transcribed from generated/game.cs:168078
    static void EmitTriMap(CpuContext c, IMemory m)
    {
        var mem = (PSMemory)m;
        { var _a = (c.A1 + 0xCu); c.T4 = mem.ReadU16(_a); }
        { var _a = (c.T0 + 0x8u); c.T5 = mem.ReadU16(_a); }
        { var _v = c.T4; c.T4 = c.T4 << 16; }
        { var _s = c.T5; var _t = c.T4; c.T5 = _s + _t; }
        { var _a = (c.A1 + 0xEu); c.T4 = mem.ReadU16(_a); }
        { var _a = (c.T1 + 0x8u); c.T6 = mem.ReadU16(_a); }
        { var _v = c.T4; c.T4 = c.T4 << 16; }
        { var _s = c.T6; var _t = c.T4; c.T6 = _s + _t; }
        { var _a = (c.T2 + 0x8u); c.T7 = mem.ReadU16(_a); }
        { var _a = (c.A1 + 0x10u); c.T4 = mem.ReadU32(_a); }
        { var _a = (c.A0 + 0xCu); mem.WriteU32(_a, c.T5); }
        { var _a = (c.A0 + 0x14u); mem.WriteU32(_a, c.T6); }
        { var _a = (c.A0 + 0x1Cu); mem.WriteU32(_a, c.T7); }
        { var _a = (c.A0 + 0x4u); mem.WriteU32(_a, c.T4); }
        { var _a = (c.T0 + 0x10u); c.T4 = mem.ReadU32(_a); }
        { var _a = (c.T1 + 0x10u); c.T5 = mem.ReadU32(_a); }
        { var _a = (c.T2 + 0x10u); c.T6 = mem.ReadU32(_a); }
        { var _a = (c.A0 + 0x8u); mem.WriteU32(_a, c.T4); }
        { var _a = (c.A0 + 0x10u); mem.WriteU32(_a, c.T5); }
        { var _a = (c.A0 + 0x18u); mem.WriteU32(_a, c.T6); }
        { var _a = (c.A1 + 0x14u); c.T4 = mem.ReadU32(_a); }
        { var _v = c.A0; c.T9 = c.A0 << 8; }
        { var _v = c.T9; c.T9 = c.T9 >> 8; }
        { var _a = c.T4; c.T8 = mem.ReadU32(_a); }
        { var _a = c.T4; mem.WriteU32(_a, c.T9); }
        c.T6 = 0x07000000u;
        { var _s = c.T8; var _t = c.T6; c.T8 = _s | _t; }
        { var _a = c.A0; mem.WriteU32(_a, c.T8); }
        if (DepthRecording) RecordNear(c.A0, c.T0, c.T1, c.T2, 0u, mem);
        { var _v = c.A0; c.A0 = c.A0 + 0x20u; }
        return;
    }
    // transcribed from generated/game.cs:168113
    static void DivQuad(CpuContext c, IMemory m)
    {
        NearScreen.Widen((PSMemory)m, c.A1, NearScreen.Kind.Quad);
        { var _v = c.A1; c.A3 = c.A1 + 0x78u; }
        NoteCorners((PSMemory)m, c.A3 + 0x78u, 4);
        c.A2 = 0x00000000u;
        DivQuadBody(c, m);
    }
    // transcribed from generated/game.cs:168120
    static void DivQuadBody(CpuContext c, IMemory m)
    {
        var mem = (PSMemory)m;
        { var _a = (c.A3 + 0x78u); c.T0 = mem.ReadU32(_a); }
        { var _a = (c.A3 + 0x7Cu); c.T1 = mem.ReadU32(_a); }
        { var _a = (c.A3 + 0x80u); c.T2 = mem.ReadU32(_a); }
        { var _a = (c.A3 + 0x84u); c.T3 = mem.ReadU32(_a); }
        c.T9 = Gte.ReadControl(26);
        { var _a = (c.T0 + 0x14u); c.T4 = mem.ReadU32(_a); }
        { var _a = (c.T1 + 0x14u); c.T5 = mem.ReadU32(_a); }
        { var _a = (c.T2 + 0x14u); c.T6 = mem.ReadU32(_a); }
        { var _a = (c.T3 + 0x14u); c.T7 = mem.ReadU32(_a); }
        { var _v = c.T9; c.T8 = (uint)((int)c.T9 >> 1); }
        { var _s = c.T4; var _t = c.T8; c.At = _s < _t ? 1u : 0u; }
        if (c.At == 0u) {
            { var _s = c.T5; var _t = c.T8; c.At = _s < _t ? 1u : 0u; }
            goto L800751E8;
        }
        { var _s = c.T5; var _t = c.T8; c.At = _s < _t ? 1u : 0u; }
        if (c.At == 0u) {
            { var _s = c.T6; var _t = c.T8; c.At = _s < _t ? 1u : 0u; }
            goto L800751E8;
        }
        { var _s = c.T6; var _t = c.T8; c.At = _s < _t ? 1u : 0u; }
        if (c.At == 0u) {
            { var _s = c.T7; var _t = c.T8; c.At = _s < _t ? 1u : 0u; }
            goto L800751E8;
        }
        { var _s = c.T7; var _t = c.T8; c.At = _s < _t ? 1u : 0u; }
        if (c.At == 0u) {
            goto L800751E8;
        }
        c.V0 = c.A0;
        return;
        L800751E8: ;
        c.T9 = Gte.ReadControl(24);
        { var _a = (c.A1 + 0x4u); c.V0 = mem.ReadU32(_a); }
        { var _a = (c.A1 + 0x8u); c.V1 = mem.ReadU32(_a); }
        { var _v = c.T9; c.T9 = (uint)((int)c.T9 >> 16); }
        { var _v = c.V0; c.V0 = c.V0 >> 1; }
        { var _v = c.V1; c.V1 = c.V1 >> 1; }
        { var _s = c.T9; var _t = c.V0; c.T8 = _s + _t; }
        { var _a = (c.T0 + 0x10u); c.T4 = (uint)(short)mem.ReadU16(_a); }
        { var _a = (c.T1 + 0x10u); c.T5 = (uint)(short)mem.ReadU16(_a); }
        { var _a = (c.T2 + 0x10u); c.T6 = (uint)(short)mem.ReadU16(_a); }
        { var _a = (c.T3 + 0x10u); c.T7 = (uint)(short)mem.ReadU16(_a); }
        { var _s = c.T8; var _t = c.T4; c.At = (int)_s < (int)_t ? 1u : 0u; }
        if (c.At == 0u) {
            { var _s = c.T8; var _t = c.T5; c.At = (int)_s < (int)_t ? 1u : 0u; }
            goto L80075244;
        }
        { var _s = c.T8; var _t = c.T5; c.At = (int)_s < (int)_t ? 1u : 0u; }
        if (c.At == 0u) {
            { var _s = c.T8; var _t = c.T6; c.At = (int)_s < (int)_t ? 1u : 0u; }
            goto L80075244;
        }
        { var _s = c.T8; var _t = c.T6; c.At = (int)_s < (int)_t ? 1u : 0u; }
        if (c.At == 0u) {
            { var _s = c.T8; var _t = c.T7; c.At = (int)_s < (int)_t ? 1u : 0u; }
            goto L80075244;
        }
        { var _s = c.T8; var _t = c.T7; c.At = (int)_s < (int)_t ? 1u : 0u; }
        if (c.At == 0u) {
            goto L80075244;
        }
        c.V0 = c.A0;
        return;
        L80075244: ;
        { var _s = c.T9; var _t = c.V0; c.T8 = _s - _t; }
        { var _s = c.T4; var _t = c.T8; c.At = (int)_s < (int)_t ? 1u : 0u; }
        if (c.At == 0u) {
            { var _s = c.T5; var _t = c.T8; c.At = (int)_s < (int)_t ? 1u : 0u; }
            goto L80075278;
        }
        { var _s = c.T5; var _t = c.T8; c.At = (int)_s < (int)_t ? 1u : 0u; }
        if (c.At == 0u) {
            { var _s = c.T6; var _t = c.T8; c.At = (int)_s < (int)_t ? 1u : 0u; }
            goto L80075278;
        }
        { var _s = c.T6; var _t = c.T8; c.At = (int)_s < (int)_t ? 1u : 0u; }
        if (c.At == 0u) {
            { var _s = c.T7; var _t = c.T8; c.At = (int)_s < (int)_t ? 1u : 0u; }
            goto L80075278;
        }
        { var _s = c.T7; var _t = c.T8; c.At = (int)_s < (int)_t ? 1u : 0u; }
        if (c.At == 0u) {
            goto L80075278;
        }
        c.V0 = c.A0;
        return;
        L80075278: ;
        c.T9 = Gte.ReadControl(25);
        { var _a = (c.T0 + 0x12u); c.T4 = (uint)(short)mem.ReadU16(_a); }
        { var _a = (c.T1 + 0x12u); c.T5 = (uint)(short)mem.ReadU16(_a); }
        { var _a = (c.T2 + 0x12u); c.T6 = (uint)(short)mem.ReadU16(_a); }
        { var _a = (c.T3 + 0x12u); c.T7 = (uint)(short)mem.ReadU16(_a); }
        { var _v = c.T9; c.T9 = (uint)((int)c.T9 >> 16); }
        { var _s = c.T9; var _t = c.V1; c.T8 = _s + _t; }
        { var _s = c.T8; var _t = c.T4; c.At = (int)_s < (int)_t ? 1u : 0u; }
        if (c.At == 0u) {
            { var _s = c.T8; var _t = c.T5; c.At = (int)_s < (int)_t ? 1u : 0u; }
            goto L800752C4;
        }
        { var _s = c.T8; var _t = c.T5; c.At = (int)_s < (int)_t ? 1u : 0u; }
        if (c.At == 0u) {
            { var _s = c.T8; var _t = c.T6; c.At = (int)_s < (int)_t ? 1u : 0u; }
            goto L800752C4;
        }
        { var _s = c.T8; var _t = c.T6; c.At = (int)_s < (int)_t ? 1u : 0u; }
        if (c.At == 0u) {
            { var _s = c.T8; var _t = c.T7; c.At = (int)_s < (int)_t ? 1u : 0u; }
            goto L800752C4;
        }
        { var _s = c.T8; var _t = c.T7; c.At = (int)_s < (int)_t ? 1u : 0u; }
        if (c.At == 0u) {
            goto L800752C4;
        }
        c.V0 = c.A0;
        return;
        L800752C4: ;
        { var _s = c.T9; var _t = c.V1; c.T8 = _s - _t; }
        { var _s = c.T4; var _t = c.T8; c.At = (int)_s < (int)_t ? 1u : 0u; }
        if (c.At == 0u) {
            { var _s = c.T5; var _t = c.T8; c.At = (int)_s < (int)_t ? 1u : 0u; }
            goto L800752F8;
        }
        { var _s = c.T5; var _t = c.T8; c.At = (int)_s < (int)_t ? 1u : 0u; }
        if (c.At == 0u) {
            { var _s = c.T6; var _t = c.T8; c.At = (int)_s < (int)_t ? 1u : 0u; }
            goto L800752F8;
        }
        { var _s = c.T6; var _t = c.T8; c.At = (int)_s < (int)_t ? 1u : 0u; }
        if (c.At == 0u) {
            { var _s = c.T7; var _t = c.T8; c.At = (int)_s < (int)_t ? 1u : 0u; }
            goto L800752F8;
        }
        { var _s = c.T7; var _t = c.T8; c.At = (int)_s < (int)_t ? 1u : 0u; }
        if (c.At == 0u) {
            goto L800752F8;
        }
        c.V0 = c.A0;
        return;
        L800752F8: ;
        { var _a = c.T0; c.T4 = (uint)(short)mem.ReadU16(_a); }
        { var _a = c.T1; c.T5 = (uint)(short)mem.ReadU16(_a); }
        { var _a = c.T2; c.T6 = (uint)(short)mem.ReadU16(_a); }
        { var _a = c.T3; c.T7 = (uint)(short)mem.ReadU16(_a); }
        { var _s = c.T4; var _t = c.T6; c.T9 = _s + _t; }
        { var _s = c.T7; var _t = c.T5; c.T8 = _s + _t; }
        { var _v = c.T8; c.T8 = (uint)((int)c.T8 >> 1); }
        { var _v = c.T9; c.T9 = (uint)((int)c.T9 >> 1); }
        { var _a = (c.A3 + 0x30u); mem.WriteU16(_a, (ushort)c.T8); }
        { var _a = (c.A3 + 0x18u); mem.WriteU16(_a, (ushort)c.T9); }
        { var _s = c.T4; var _t = c.T5; c.T8 = _s + _t; }
        { var _s = c.T7; var _t = c.T6; c.T9 = _s + _t; }
        { var _s = c.T8; var _t = c.T9; c.V1 = _s + _t; }
        { var _v = c.T8; c.T8 = (uint)((int)c.T8 >> 1); }
        { var _v = c.T9; c.T9 = (uint)((int)c.T9 >> 1); }
        { var _v = c.V1; c.V1 = (uint)((int)c.V1 >> 2); }
        { var _a = c.A3; mem.WriteU16(_a, (ushort)c.T8); }
        { var _a = (c.A3 + 0x48u); mem.WriteU16(_a, (ushort)c.T9); }
        { var _a = (c.A3 + 0x60u); mem.WriteU16(_a, (ushort)c.V1); }
        { var _a = (c.T0 + 0x2u); c.T4 = (uint)(short)mem.ReadU16(_a); }
        { var _a = (c.T1 + 0x2u); c.T5 = (uint)(short)mem.ReadU16(_a); }
        { var _a = (c.T2 + 0x2u); c.T6 = (uint)(short)mem.ReadU16(_a); }
        { var _a = (c.T3 + 0x2u); c.T7 = (uint)(short)mem.ReadU16(_a); }
        { var _s = c.T4; var _t = c.T6; c.T9 = _s + _t; }
        { var _s = c.T7; var _t = c.T5; c.T8 = _s + _t; }
        { var _v = c.T8; c.T8 = (uint)((int)c.T8 >> 1); }
        { var _v = c.T9; c.T9 = (uint)((int)c.T9 >> 1); }
        { var _a = (c.A3 + 0x32u); mem.WriteU16(_a, (ushort)c.T8); }
        { var _a = (c.A3 + 0x1Au); mem.WriteU16(_a, (ushort)c.T9); }
        { var _s = c.T4; var _t = c.T5; c.T8 = _s + _t; }
        { var _s = c.T7; var _t = c.T6; c.T9 = _s + _t; }
        { var _s = c.T8; var _t = c.T9; c.V1 = _s + _t; }
        { var _v = c.T8; c.T8 = (uint)((int)c.T8 >> 1); }
        { var _v = c.T9; c.T9 = (uint)((int)c.T9 >> 1); }
        { var _v = c.V1; c.V1 = (uint)((int)c.V1 >> 2); }
        { var _a = (c.A3 + 0x2u); mem.WriteU16(_a, (ushort)c.T8); }
        { var _a = (c.A3 + 0x4Au); mem.WriteU16(_a, (ushort)c.T9); }
        { var _a = (c.A3 + 0x62u); mem.WriteU16(_a, (ushort)c.V1); }
        { var _a = (c.T0 + 0x4u); c.T4 = (uint)(short)mem.ReadU16(_a); }
        { var _a = (c.T1 + 0x4u); c.T5 = (uint)(short)mem.ReadU16(_a); }
        { var _a = (c.T2 + 0x4u); c.T6 = (uint)(short)mem.ReadU16(_a); }
        { var _a = (c.T3 + 0x4u); c.T7 = (uint)(short)mem.ReadU16(_a); }
        { var _s = c.T4; var _t = c.T6; c.T9 = _s + _t; }
        { var _s = c.T7; var _t = c.T5; c.T8 = _s + _t; }
        { var _v = c.T8; c.T8 = (uint)((int)c.T8 >> 1); }
        { var _v = c.T9; c.T9 = (uint)((int)c.T9 >> 1); }
        { var _a = (c.A3 + 0x34u); mem.WriteU16(_a, (ushort)c.T8); }
        { var _a = (c.A3 + 0x1Cu); mem.WriteU16(_a, (ushort)c.T9); }
        { var _s = c.T4; var _t = c.T5; c.T8 = _s + _t; }
        { var _s = c.T7; var _t = c.T6; c.T9 = _s + _t; }
        { var _s = c.T8; var _t = c.T9; c.V1 = _s + _t; }
        { var _v = c.T8; c.T8 = (uint)((int)c.T8 >> 1); }
        { var _v = c.T9; c.T9 = (uint)((int)c.T9 >> 1); }
        { var _v = c.V1; c.V1 = (uint)((int)c.V1 >> 2); }
        { var _a = (c.A3 + 0x4u); mem.WriteU16(_a, (ushort)c.T8); }
        { var _a = (c.A3 + 0x4Cu); mem.WriteU16(_a, (ushort)c.T9); }
        { var _a = (c.A3 + 0x64u); mem.WriteU16(_a, (ushort)c.V1); }
        { var _a = c.A3; var _lw = mem.ReadU32(_a); Gte.Write(0, _lw); }
        { var _a = (c.A3 + 0x4u); var _lw = mem.ReadU32(_a); Gte.Write(1, _lw); }
        { var _a = (c.A3 + 0x18u); var _lw = mem.ReadU32(_a); Gte.Write(2, _lw); }
        { var _a = (c.A3 + 0x1Cu); var _lw = mem.ReadU32(_a); Gte.Write(3, _lw); }
        { var _a = (c.A3 + 0x60u); var _lw = mem.ReadU32(_a); Gte.Write(4, _lw); }
        { var _a = (c.A3 + 0x64u); var _lw = mem.ReadU32(_a); Gte.Write(5, _lw); }
        { var _a = (c.T0 + 0x8u); c.T4 = mem.ReadU8(_a); }
        Gte.Rtpt(12, false);
        if (DepthRecording)
        {
            NoteSz(c.A3, Gte.Read(12), Gte.Read(17));
            NoteSz(c.A3 + 0x18u, Gte.Read(13), Gte.Read(18));
            NoteSz(c.A3 + 0x60u, Gte.Read(14), Gte.Read(19));
        }
        { var _a = (c.T1 + 0x8u); c.T5 = mem.ReadU8(_a); }
        { var _a = (c.T2 + 0x8u); c.T6 = mem.ReadU8(_a); }
        { var _a = (c.T3 + 0x8u); c.T7 = mem.ReadU8(_a); }
        { var _s = c.T4; var _t = c.T6; c.T9 = _s + _t; }
        { var _s = c.T7; var _t = c.T5; c.T8 = _s + _t; }
        { var _v = c.T8; c.T8 = (uint)((int)c.T8 >> 1); }
        { var _v = c.T9; c.T9 = (uint)((int)c.T9 >> 1); }
        { var _a = (c.A3 + 0x38u); mem.WriteU8(_a, (byte)c.T8); }
        { var _a = (c.A3 + 0x20u); mem.WriteU8(_a, (byte)c.T9); }
        { var _s = c.T4; var _t = c.T5; c.T8 = _s + _t; }
        { var _s = c.T7; var _t = c.T6; c.T9 = _s + _t; }
        { var _s = c.T8; var _t = c.T9; c.V1 = _s + _t; }
        { var _v = c.T8; c.T8 = (uint)((int)c.T8 >> 1); }
        { var _v = c.T9; c.T9 = (uint)((int)c.T9 >> 1); }
        { var _v = c.V1; c.V1 = (uint)((int)c.V1 >> 2); }
        { var _a = (c.A3 + 0x8u); mem.WriteU8(_a, (byte)c.T8); }
        { var _a = (c.A3 + 0x50u); mem.WriteU8(_a, (byte)c.T9); }
        { var _a = (c.A3 + 0x68u); mem.WriteU8(_a, (byte)c.V1); }
        { var _a = (c.A3 + 0x10u); var _sw = Gte.Read(12); mem.WriteU32(_a, _sw); }
        { var _a = (c.A3 + 0x28u); var _sw = Gte.Read(13); mem.WriteU32(_a, _sw); }
        { var _a = (c.A3 + 0x70u); var _sw = Gte.Read(14); mem.WriteU32(_a, _sw); }
        { var _a = (c.A3 + 0x14u); var _sw = Gte.Read(17); mem.WriteU32(_a, _sw); }
        { var _a = (c.A3 + 0x2Cu); var _sw = Gte.Read(18); mem.WriteU32(_a, _sw); }
        { var _a = (c.A3 + 0x74u); var _sw = Gte.Read(19); mem.WriteU32(_a, _sw); }
        { var _a = (c.A3 + 0x30u); var _lw = mem.ReadU32(_a); Gte.Write(0, _lw); }
        { var _a = (c.A3 + 0x34u); var _lw = mem.ReadU32(_a); Gte.Write(1, _lw); }
        { var _a = (c.A3 + 0x48u); var _lw = mem.ReadU32(_a); Gte.Write(2, _lw); }
        { var _a = (c.A3 + 0x4Cu); var _lw = mem.ReadU32(_a); Gte.Write(3, _lw); }
        { var _a = (c.T0 + 0x9u); c.T4 = mem.ReadU8(_a); }
        Gte.Rtpt(12, false);
        if (DepthRecording)
        {
            NoteSz(c.A3 + 0x30u, Gte.Read(12), Gte.Read(17));
            NoteSz(c.A3 + 0x48u, Gte.Read(13), Gte.Read(18));
            NoteSz(c.A3 + 0x60u, Gte.Read(14), Gte.Read(19));
        }
        { var _a = (c.T1 + 0x9u); c.T5 = mem.ReadU8(_a); }
        { var _a = (c.T2 + 0x9u); c.T6 = mem.ReadU8(_a); }
        { var _a = (c.T3 + 0x9u); c.T7 = mem.ReadU8(_a); }
        { var _s = c.T4; var _t = c.T6; c.T9 = _s + _t; }
        { var _s = c.T7; var _t = c.T5; c.T8 = _s + _t; }
        { var _v = c.T8; c.T8 = (uint)((int)c.T8 >> 1); }
        { var _v = c.T9; c.T9 = (uint)((int)c.T9 >> 1); }
        { var _a = (c.A3 + 0x39u); mem.WriteU8(_a, (byte)c.T8); }
        { var _a = (c.A3 + 0x21u); mem.WriteU8(_a, (byte)c.T9); }
        { var _s = c.T4; var _t = c.T5; c.T8 = _s + _t; }
        { var _s = c.T7; var _t = c.T6; c.T9 = _s + _t; }
        { var _s = c.T8; var _t = c.T9; c.V1 = _s + _t; }
        { var _v = c.T8; c.T8 = (uint)((int)c.T8 >> 1); }
        { var _v = c.T9; c.T9 = (uint)((int)c.T9 >> 1); }
        { var _v = c.V1; c.V1 = (uint)((int)c.V1 >> 2); }
        { var _a = (c.A3 + 0x9u); mem.WriteU8(_a, (byte)c.T8); }
        { var _a = (c.A3 + 0x51u); mem.WriteU8(_a, (byte)c.T9); }
        { var _a = (c.A3 + 0x69u); mem.WriteU8(_a, (byte)c.V1); }
        { var _a = c.A1; c.T4 = mem.ReadU32(_a); }
        { var _v = c.A2; c.A2 = c.A2 + 0x1u; }
        if (c.T4 != c.A2) {
            goto L80075548;
        }
        c.V1 = c.RA;
        { var _a = (c.A3 + 0x78u); c.T0 = mem.ReadU32(_a); }
        { var _v = c.A3; c.T1 = c.A3 + 0x0u; }
        { var _v = c.A3; c.T2 = c.A3 + 0x18u; }
        { var _v = c.A3; c.T3 = c.A3 + 0x60u; }
        { var _a = (c.A3 + 0x40u); var _sw = Gte.Read(12); mem.WriteU32(_a, _sw); }
        { var _a = (c.A3 + 0x58u); var _sw = Gte.Read(13); mem.WriteU32(_a, _sw); }
        c.RA = 0x800754F0u;
        EmitQuadMap(c, m);
        { var _a = (c.A3 + 0x7Cu); c.T0 = mem.ReadU32(_a); }
        { var _v = c.A3; c.T1 = c.A3 + 0x30u; }
        { var _v = c.A3; c.T2 = c.A3 + 0x0u; }
        { var _v = c.A3; c.T3 = c.A3 + 0x60u; }
        c.RA = 0x80075508u;
        EmitQuadMap(c, m);
        { var _a = (c.A3 + 0x80u); c.T0 = mem.ReadU32(_a); }
        { var _v = c.A3; c.T1 = c.A3 + 0x18u; }
        { var _v = c.A3; c.T2 = c.A3 + 0x48u; }
        { var _v = c.A3; c.T3 = c.A3 + 0x60u; }
        c.RA = 0x80075520u;
        EmitQuadMap(c, m);
        { var _a = (c.A3 + 0x84u); c.T0 = mem.ReadU32(_a); }
        { var _v = c.A3; c.T1 = c.A3 + 0x48u; }
        { var _v = c.A3; c.T2 = c.A3 + 0x30u; }
        { var _v = c.A3; c.T3 = c.A3 + 0x60u; }
        c.RA = 0x80075538u;
        EmitQuadMap(c, m);
        c.RA = c.V1;
        { var _v = c.A2; c.A2 = c.A2 - 0x1u; }
        goto L8007560C;
        L80075548: ;
        { var _v = c.A3; c.A3 = c.A3 + 0x8Cu; }
        { var _a = (c.A3 + 0x88u); mem.WriteU32(_a, c.RA); }
        { var _a = (c.A3 - 0x14u); c.T4 = mem.ReadU32(_a); }
        { var _v = c.A3; c.T5 = c.A3 - 0x8Cu; }
        { var _v = c.A3; c.T6 = c.A3 - 0x74u; }
        { var _v = c.A3; c.T7 = c.A3 - 0x2Cu; }
        { var _a = (c.A3 + 0x78u); mem.WriteU32(_a, c.T4); }
        { var _a = (c.A3 + 0x7Cu); mem.WriteU32(_a, c.T5); }
        { var _a = (c.A3 + 0x80u); mem.WriteU32(_a, c.T6); }
        { var _a = (c.A3 + 0x84u); mem.WriteU32(_a, c.T7); }
        { var _a = (c.A3 - 0x4Cu); var _sw = Gte.Read(12); mem.WriteU32(_a, _sw); }
        { var _a = (c.A3 - 0x34u); var _sw = Gte.Read(13); mem.WriteU32(_a, _sw); }
        { var _a = (c.A3 - 0x48u); var _sw = Gte.Read(17); mem.WriteU32(_a, _sw); }
        { var _a = (c.A3 - 0x30u); var _sw = Gte.Read(18); mem.WriteU32(_a, _sw); }
        c.RA = 0x80075588u;
        DivQuadBody(c, m);
        { var _a = (c.A3 - 0x10u); c.T4 = mem.ReadU32(_a); }
        { var _v = c.A3; c.T5 = c.A3 - 0x5Cu; }
        { var _v = c.A3; c.T6 = c.A3 - 0x8Cu; }
        { var _v = c.A3; c.T7 = c.A3 - 0x2Cu; }
        { var _a = (c.A3 + 0x78u); mem.WriteU32(_a, c.T4); }
        { var _a = (c.A3 + 0x7Cu); mem.WriteU32(_a, c.T5); }
        { var _a = (c.A3 + 0x80u); mem.WriteU32(_a, c.T6); }
        { var _a = (c.A3 + 0x84u); mem.WriteU32(_a, c.T7); }
        c.RA = 0x800755B0u;
        DivQuadBody(c, m);
        { var _a = (c.A3 - 0xCu); c.T4 = mem.ReadU32(_a); }
        { var _v = c.A3; c.T5 = c.A3 - 0x74u; }
        { var _v = c.A3; c.T6 = c.A3 - 0x44u; }
        { var _v = c.A3; c.T7 = c.A3 - 0x2Cu; }
        { var _a = (c.A3 + 0x78u); mem.WriteU32(_a, c.T4); }
        { var _a = (c.A3 + 0x7Cu); mem.WriteU32(_a, c.T5); }
        { var _a = (c.A3 + 0x80u); mem.WriteU32(_a, c.T6); }
        { var _a = (c.A3 + 0x84u); mem.WriteU32(_a, c.T7); }
        c.RA = 0x800755D8u;
        DivQuadBody(c, m);
        { var _a = (c.A3 - 0x8u); c.T4 = mem.ReadU32(_a); }
        { var _v = c.A3; c.T5 = c.A3 - 0x44u; }
        { var _v = c.A3; c.T6 = c.A3 - 0x5Cu; }
        { var _v = c.A3; c.T7 = c.A3 - 0x2Cu; }
        { var _a = (c.A3 + 0x78u); mem.WriteU32(_a, c.T4); }
        { var _a = (c.A3 + 0x7Cu); mem.WriteU32(_a, c.T5); }
        { var _a = (c.A3 + 0x80u); mem.WriteU32(_a, c.T6); }
        { var _a = (c.A3 + 0x84u); mem.WriteU32(_a, c.T7); }
        c.RA = 0x80075600u;
        DivQuadBody(c, m);
        { var _a = (c.A3 + 0x88u); c.RA = mem.ReadU32(_a); }
        { var _v = c.A3; c.A3 = c.A3 - 0x8Cu; }
        { var _v = c.A2; c.A2 = c.A2 - 0x1u; }
        L8007560C: ;
        c.V0 = c.A0;
        return;
    }
    // transcribed from generated/game.cs:168466
    static void EmitQuadMap(CpuContext c, IMemory m)
    {
        var mem = (PSMemory)m;
        { var _a = (c.A1 + 0xCu); c.T4 = mem.ReadU16(_a); }
        { var _a = (c.T0 + 0x8u); c.T5 = mem.ReadU16(_a); }
        { var _v = c.T4; c.T4 = c.T4 << 16; }
        { var _s = c.T5; var _t = c.T4; c.T5 = _s + _t; }
        { var _a = (c.A1 + 0xEu); c.T4 = mem.ReadU16(_a); }
        { var _a = (c.T1 + 0x8u); c.T6 = mem.ReadU16(_a); }
        { var _v = c.T4; c.T4 = c.T4 << 16; }
        { var _s = c.T6; var _t = c.T4; c.T6 = _s + _t; }
        { var _a = (c.T2 + 0x8u); c.T7 = mem.ReadU16(_a); }
        { var _a = (c.T3 + 0x8u); c.T8 = mem.ReadU16(_a); }
        { var _a = (c.A1 + 0x10u); c.T4 = mem.ReadU32(_a); }
        { var _a = (c.A0 + 0xCu); mem.WriteU32(_a, c.T5); }
        { var _a = (c.A0 + 0x14u); mem.WriteU32(_a, c.T6); }
        { var _a = (c.A0 + 0x1Cu); mem.WriteU32(_a, c.T7); }
        { var _a = (c.A0 + 0x24u); mem.WriteU32(_a, c.T8); }
        { var _a = (c.A0 + 0x4u); mem.WriteU32(_a, c.T4); }
        { var _a = (c.T0 + 0x10u); c.T4 = mem.ReadU32(_a); }
        { var _a = (c.T1 + 0x10u); c.T5 = mem.ReadU32(_a); }
        { var _a = (c.T2 + 0x10u); c.T6 = mem.ReadU32(_a); }
        { var _a = (c.T3 + 0x10u); c.T7 = mem.ReadU32(_a); }
        { var _a = (c.A0 + 0x8u); mem.WriteU32(_a, c.T4); }
        { var _a = (c.A0 + 0x10u); mem.WriteU32(_a, c.T5); }
        { var _a = (c.A0 + 0x18u); mem.WriteU32(_a, c.T6); }
        { var _a = (c.A0 + 0x20u); mem.WriteU32(_a, c.T7); }
        { var _a = (c.A1 + 0x14u); c.T4 = mem.ReadU32(_a); }
        { var _v = c.A0; c.T9 = c.A0 << 8; }
        { var _v = c.T9; c.T9 = c.T9 >> 8; }
        { var _a = c.T4; c.T8 = mem.ReadU32(_a); }
        { var _a = c.T4; mem.WriteU32(_a, c.T9); }
        c.T6 = 0x09000000u;
        { var _s = c.T8; var _t = c.T6; c.T8 = _s | _t; }
        { var _a = c.A0; mem.WriteU32(_a, c.T8); }
        if (DepthRecording) RecordNear(c.A0, c.T0, c.T1, c.T2, c.T3, mem);
        { var _v = c.A0; c.A0 = c.A0 + 0x28u; }
        return;
    }
    // transcribed from generated/game.cs:168505
    static void DivTri2(CpuContext c, IMemory m)
    {
        NearScreen.Widen((PSMemory)m, c.A1, NearScreen.Kind.Tri2);
        { var _v = c.A1; c.A3 = c.A1 + 0x60u; }
        NoteCorners((PSMemory)m, c.A3 + 0x48u, 3);
        c.A2 = 0x00000000u;
        DivTri2Body(c, m);
    }
    // transcribed from generated/game.cs:168512
    static void DivTri2Body(CpuContext c, IMemory m)
    {
        var mem = (PSMemory)m;
        { var _a = (c.A3 + 0x48u); c.T0 = mem.ReadU32(_a); }
        { var _a = (c.A3 + 0x4Cu); c.T1 = mem.ReadU32(_a); }
        { var _a = (c.A3 + 0x50u); c.T2 = mem.ReadU32(_a); }
        c.T9 = Gte.ReadControl(26);
        { var _a = (c.T0 + 0x14u); c.T4 = mem.ReadU32(_a); }
        { var _a = (c.T1 + 0x14u); c.T5 = mem.ReadU32(_a); }
        { var _a = (c.T2 + 0x14u); c.T6 = mem.ReadU32(_a); }
        { var _v = c.T9; c.T8 = (uint)((int)c.T9 >> 1); }
        { var _s = c.T4; var _t = c.T8; c.At = _s < _t ? 1u : 0u; }
        if (c.At == 0u) {
            { var _s = c.T5; var _t = c.T8; c.At = _s < _t ? 1u : 0u; }
            goto L800756F8;
        }
        { var _s = c.T5; var _t = c.T8; c.At = _s < _t ? 1u : 0u; }
        if (c.At == 0u) {
            { var _s = c.T6; var _t = c.T8; c.At = _s < _t ? 1u : 0u; }
            goto L800756F8;
        }
        { var _s = c.T6; var _t = c.T8; c.At = _s < _t ? 1u : 0u; }
        if (c.At == 0u) {
            goto L800756F8;
        }
        c.V0 = c.A0;
        return;
        L800756F8: ;
        c.T9 = Gte.ReadControl(24);
        { var _a = (c.A1 + 0x4u); c.V0 = mem.ReadU32(_a); }
        { var _a = (c.A1 + 0x8u); c.V1 = mem.ReadU32(_a); }
        { var _v = c.T9; c.T9 = (uint)((int)c.T9 >> 16); }
        { var _v = c.V0; c.V0 = c.V0 >> 1; }
        { var _v = c.V1; c.V1 = c.V1 >> 1; }
        { var _s = c.T9; var _t = c.V0; c.T8 = _s + _t; }
        { var _a = (c.T0 + 0x10u); c.T4 = (uint)(short)mem.ReadU16(_a); }
        { var _a = (c.T1 + 0x10u); c.T5 = (uint)(short)mem.ReadU16(_a); }
        { var _a = (c.T2 + 0x10u); c.T6 = (uint)(short)mem.ReadU16(_a); }
        { var _s = c.T8; var _t = c.T4; c.At = (int)_s < (int)_t ? 1u : 0u; }
        if (c.At == 0u) {
            { var _s = c.T8; var _t = c.T5; c.At = (int)_s < (int)_t ? 1u : 0u; }
            goto L80075748;
        }
        { var _s = c.T8; var _t = c.T5; c.At = (int)_s < (int)_t ? 1u : 0u; }
        if (c.At == 0u) {
            { var _s = c.T8; var _t = c.T6; c.At = (int)_s < (int)_t ? 1u : 0u; }
            goto L80075748;
        }
        { var _s = c.T8; var _t = c.T6; c.At = (int)_s < (int)_t ? 1u : 0u; }
        if (c.At == 0u) {
            goto L80075748;
        }
        c.V0 = c.A0;
        return;
        L80075748: ;
        { var _s = c.T9; var _t = c.V0; c.T8 = _s - _t; }
        { var _s = c.T4; var _t = c.T8; c.At = (int)_s < (int)_t ? 1u : 0u; }
        if (c.At == 0u) {
            { var _s = c.T5; var _t = c.T8; c.At = (int)_s < (int)_t ? 1u : 0u; }
            goto L80075774;
        }
        { var _s = c.T5; var _t = c.T8; c.At = (int)_s < (int)_t ? 1u : 0u; }
        if (c.At == 0u) {
            { var _s = c.T6; var _t = c.T8; c.At = (int)_s < (int)_t ? 1u : 0u; }
            goto L80075774;
        }
        { var _s = c.T6; var _t = c.T8; c.At = (int)_s < (int)_t ? 1u : 0u; }
        if (c.At == 0u) {
            goto L80075774;
        }
        c.V0 = c.A0;
        return;
        L80075774: ;
        c.T9 = Gte.ReadControl(25);
        { var _a = (c.T0 + 0x12u); c.T4 = (uint)(short)mem.ReadU16(_a); }
        { var _a = (c.T1 + 0x12u); c.T5 = (uint)(short)mem.ReadU16(_a); }
        { var _a = (c.T2 + 0x12u); c.T6 = (uint)(short)mem.ReadU16(_a); }
        { var _v = c.T9; c.T9 = (uint)((int)c.T9 >> 16); }
        { var _s = c.T9; var _t = c.V1; c.T8 = _s + _t; }
        { var _s = c.T8; var _t = c.T4; c.At = (int)_s < (int)_t ? 1u : 0u; }
        if (c.At == 0u) {
            { var _s = c.T8; var _t = c.T5; c.At = (int)_s < (int)_t ? 1u : 0u; }
            goto L800757B4;
        }
        { var _s = c.T8; var _t = c.T5; c.At = (int)_s < (int)_t ? 1u : 0u; }
        if (c.At == 0u) {
            { var _s = c.T8; var _t = c.T6; c.At = (int)_s < (int)_t ? 1u : 0u; }
            goto L800757B4;
        }
        { var _s = c.T8; var _t = c.T6; c.At = (int)_s < (int)_t ? 1u : 0u; }
        if (c.At == 0u) {
            goto L800757B4;
        }
        c.V0 = c.A0;
        return;
        L800757B4: ;
        { var _s = c.T9; var _t = c.V1; c.T8 = _s - _t; }
        { var _s = c.T4; var _t = c.T8; c.At = (int)_s < (int)_t ? 1u : 0u; }
        if (c.At == 0u) {
            { var _s = c.T5; var _t = c.T8; c.At = (int)_s < (int)_t ? 1u : 0u; }
            goto L800757E0;
        }
        { var _s = c.T5; var _t = c.T8; c.At = (int)_s < (int)_t ? 1u : 0u; }
        if (c.At == 0u) {
            { var _s = c.T6; var _t = c.T8; c.At = (int)_s < (int)_t ? 1u : 0u; }
            goto L800757E0;
        }
        { var _s = c.T6; var _t = c.T8; c.At = (int)_s < (int)_t ? 1u : 0u; }
        if (c.At == 0u) {
            goto L800757E0;
        }
        c.V0 = c.A0;
        return;
        L800757E0: ;
        { var _a = c.T0; c.T4 = (uint)(short)mem.ReadU16(_a); }
        { var _a = c.T1; c.T5 = (uint)(short)mem.ReadU16(_a); }
        { var _a = c.T2; c.T6 = (uint)(short)mem.ReadU16(_a); }
        { var _s = c.T4; var _t = c.T5; c.T7 = _s + _t; }
        { var _s = c.T5; var _t = c.T6; c.T8 = _s + _t; }
        { var _s = c.T6; var _t = c.T4; c.T9 = _s + _t; }
        { var _v = c.T7; c.T7 = (uint)((int)c.T7 >> 1); }
        { var _v = c.T8; c.T8 = (uint)((int)c.T8 >> 1); }
        { var _v = c.T9; c.T9 = (uint)((int)c.T9 >> 1); }
        { var _a = c.A3; mem.WriteU16(_a, (ushort)c.T7); }
        { var _a = (c.A3 + 0x18u); mem.WriteU16(_a, (ushort)c.T8); }
        { var _a = (c.A3 + 0x30u); mem.WriteU16(_a, (ushort)c.T9); }
        { var _a = (c.T0 + 0x2u); c.T4 = (uint)(short)mem.ReadU16(_a); }
        { var _a = (c.T1 + 0x2u); c.T5 = (uint)(short)mem.ReadU16(_a); }
        { var _a = (c.T2 + 0x2u); c.T6 = (uint)(short)mem.ReadU16(_a); }
        { var _s = c.T4; var _t = c.T5; c.T7 = _s + _t; }
        { var _s = c.T5; var _t = c.T6; c.T8 = _s + _t; }
        { var _s = c.T6; var _t = c.T4; c.T9 = _s + _t; }
        { var _v = c.T7; c.T7 = (uint)((int)c.T7 >> 1); }
        { var _v = c.T8; c.T8 = (uint)((int)c.T8 >> 1); }
        { var _v = c.T9; c.T9 = (uint)((int)c.T9 >> 1); }
        { var _a = (c.A3 + 0x2u); mem.WriteU16(_a, (ushort)c.T7); }
        { var _a = (c.A3 + 0x1Au); mem.WriteU16(_a, (ushort)c.T8); }
        { var _a = (c.A3 + 0x32u); mem.WriteU16(_a, (ushort)c.T9); }
        { var _a = (c.T0 + 0x4u); c.T4 = (uint)(short)mem.ReadU16(_a); }
        { var _a = (c.T1 + 0x4u); c.T5 = (uint)(short)mem.ReadU16(_a); }
        { var _a = (c.T2 + 0x4u); c.T6 = (uint)(short)mem.ReadU16(_a); }
        { var _s = c.T4; var _t = c.T5; c.T7 = _s + _t; }
        { var _s = c.T5; var _t = c.T6; c.T8 = _s + _t; }
        { var _s = c.T6; var _t = c.T4; c.T9 = _s + _t; }
        { var _v = c.T7; c.T7 = (uint)((int)c.T7 >> 1); }
        { var _v = c.T8; c.T8 = (uint)((int)c.T8 >> 1); }
        { var _v = c.T9; c.T9 = (uint)((int)c.T9 >> 1); }
        { var _a = (c.A3 + 0x4u); mem.WriteU16(_a, (ushort)c.T7); }
        { var _a = (c.A3 + 0x1Cu); mem.WriteU16(_a, (ushort)c.T8); }
        { var _a = (c.A3 + 0x34u); mem.WriteU16(_a, (ushort)c.T9); }
        { var _a = c.A3; var _lw = mem.ReadU32(_a); Gte.Write(0, _lw); }
        { var _a = (c.A3 + 0x4u); var _lw = mem.ReadU32(_a); Gte.Write(1, _lw); }
        { var _a = (c.A3 + 0x18u); var _lw = mem.ReadU32(_a); Gte.Write(2, _lw); }
        { var _a = (c.A3 + 0x1Cu); var _lw = mem.ReadU32(_a); Gte.Write(3, _lw); }
        { var _a = (c.A3 + 0x30u); var _lw = mem.ReadU32(_a); Gte.Write(4, _lw); }
        { var _a = (c.A3 + 0x34u); var _lw = mem.ReadU32(_a); Gte.Write(5, _lw); }
        { var _a = (c.T0 + 0x8u); c.T4 = mem.ReadU8(_a); }
        { var _a = (c.T1 + 0x8u); c.T5 = mem.ReadU8(_a); }
        { var _a = (c.T2 + 0x8u); c.T6 = mem.ReadU8(_a); }
        { var _s = c.T4; var _t = c.T5; c.T7 = _s + _t; }
        { var _s = c.T5; var _t = c.T6; c.T8 = _s + _t; }
        { var _s = c.T6; var _t = c.T4; c.T9 = _s + _t; }
        { var _v = c.T7; c.T7 = c.T7 >> 1; }
        { var _v = c.T8; c.T8 = c.T8 >> 1; }
        { var _v = c.T9; c.T9 = c.T9 >> 1; }
        { var _a = (c.A3 + 0x8u); mem.WriteU8(_a, (byte)c.T7); }
        { var _a = (c.A3 + 0x20u); mem.WriteU8(_a, (byte)c.T8); }
        { var _a = (c.A3 + 0x38u); mem.WriteU8(_a, (byte)c.T9); }
        Gte.Rtpt(12, false);
        if (DepthRecording)
        {
            NoteSz(c.A3, Gte.Read(12), Gte.Read(17));
            NoteSz(c.A3 + 0x18u, Gte.Read(13), Gte.Read(18));
            NoteSz(c.A3 + 0x30u, Gte.Read(14), Gte.Read(19));
        }
        { var _a = (c.T0 + 0x9u); c.T4 = mem.ReadU8(_a); }
        { var _a = (c.T1 + 0x9u); c.T5 = mem.ReadU8(_a); }
        { var _a = (c.T2 + 0x9u); c.T6 = mem.ReadU8(_a); }
        { var _s = c.T4; var _t = c.T5; c.T7 = _s + _t; }
        { var _s = c.T5; var _t = c.T6; c.T8 = _s + _t; }
        { var _s = c.T6; var _t = c.T4; c.T9 = _s + _t; }
        { var _v = c.T7; c.T7 = c.T7 >> 1; }
        { var _v = c.T8; c.T8 = c.T8 >> 1; }
        { var _v = c.T9; c.T9 = c.T9 >> 1; }
        { var _a = (c.A3 + 0x9u); mem.WriteU8(_a, (byte)c.T7); }
        { var _a = (c.A3 + 0x21u); mem.WriteU8(_a, (byte)c.T8); }
        { var _a = (c.A3 + 0x39u); mem.WriteU8(_a, (byte)c.T9); }
        { var _a = (c.T0 + 0xCu); c.T4 = mem.ReadU8(_a); }
        { var _a = (c.T1 + 0xCu); c.T5 = mem.ReadU8(_a); }
        { var _a = (c.T2 + 0xCu); c.T6 = mem.ReadU8(_a); }
        { var _s = c.T4; var _t = c.T5; c.T7 = _s + _t; }
        { var _s = c.T5; var _t = c.T6; c.T8 = _s + _t; }
        { var _s = c.T6; var _t = c.T4; c.T9 = _s + _t; }
        { var _v = c.T7; c.T7 = c.T7 >> 1; }
        { var _v = c.T8; c.T8 = c.T8 >> 1; }
        { var _v = c.T9; c.T9 = c.T9 >> 1; }
        { var _a = (c.A3 + 0xCu); mem.WriteU8(_a, (byte)c.T7); }
        { var _a = (c.A3 + 0x24u); mem.WriteU8(_a, (byte)c.T8); }
        { var _a = (c.A3 + 0x3Cu); mem.WriteU8(_a, (byte)c.T9); }
        { var _a = (c.T0 + 0xDu); c.T4 = mem.ReadU8(_a); }
        { var _a = (c.T1 + 0xDu); c.T5 = mem.ReadU8(_a); }
        { var _a = (c.T2 + 0xDu); c.T6 = mem.ReadU8(_a); }
        { var _s = c.T4; var _t = c.T5; c.T7 = _s + _t; }
        { var _s = c.T5; var _t = c.T6; c.T8 = _s + _t; }
        { var _s = c.T6; var _t = c.T4; c.T9 = _s + _t; }
        { var _v = c.T7; c.T7 = c.T7 >> 1; }
        { var _v = c.T8; c.T8 = c.T8 >> 1; }
        { var _v = c.T9; c.T9 = c.T9 >> 1; }
        { var _a = (c.A3 + 0xDu); mem.WriteU8(_a, (byte)c.T7); }
        { var _a = (c.A3 + 0x25u); mem.WriteU8(_a, (byte)c.T8); }
        { var _a = (c.A3 + 0x3Du); mem.WriteU8(_a, (byte)c.T9); }
        { var _a = (c.T0 + 0xEu); c.T4 = mem.ReadU8(_a); }
        { var _a = (c.T1 + 0xEu); c.T5 = mem.ReadU8(_a); }
        { var _a = (c.T2 + 0xEu); c.T6 = mem.ReadU8(_a); }
        { var _s = c.T4; var _t = c.T5; c.T7 = _s + _t; }
        { var _s = c.T5; var _t = c.T6; c.T8 = _s + _t; }
        { var _s = c.T6; var _t = c.T4; c.T9 = _s + _t; }
        { var _v = c.T7; c.T7 = c.T7 >> 1; }
        { var _v = c.T8; c.T8 = c.T8 >> 1; }
        { var _v = c.T9; c.T9 = c.T9 >> 1; }
        { var _a = (c.A3 + 0xEu); mem.WriteU8(_a, (byte)c.T7); }
        { var _a = (c.A3 + 0x26u); mem.WriteU8(_a, (byte)c.T8); }
        { var _a = (c.A3 + 0x3Eu); mem.WriteU8(_a, (byte)c.T9); }
        { var _a = c.A1; c.T4 = mem.ReadU32(_a); }
        { var _v = c.A2; c.A2 = c.A2 + 0x1u; }
        if (c.T4 != c.A2) {
            goto L800759FC;
        }
        { var _a = (c.A3 + 0x10u); var _sw = Gte.Read(12); mem.WriteU32(_a, _sw); }
        { var _a = (c.A3 + 0x28u); var _sw = Gte.Read(13); mem.WriteU32(_a, _sw); }
        { var _a = (c.A3 + 0x40u); var _sw = Gte.Read(14); mem.WriteU32(_a, _sw); }
        c.V1 = c.RA;
        { var _a = (c.A3 + 0x4Cu); c.T0 = mem.ReadU32(_a); }
        { var _v = c.A3; c.T1 = c.A3 + 0x18u; }
        { var _v = c.A3; c.T2 = c.A3 + 0x0u; }
        c.RA = 0x800759B0u;
        EmitTriModels(c, m);
        { var _v = c.A3; c.T0 = c.A3 + 0x0u; }
        { var _v = c.A3; c.T1 = c.A3 + 0x18u; }
        { var _v = c.A3; c.T2 = c.A3 + 0x30u; }
        c.RA = 0x800759C4u;
        EmitTriModels(c, m);
        { var _a = (c.A3 + 0x48u); c.T0 = mem.ReadU32(_a); }
        { var _v = c.A3; c.T1 = c.A3 + 0x0u; }
        { var _v = c.A3; c.T2 = c.A3 + 0x30u; }
        c.RA = 0x800759D8u;
        EmitTriModels(c, m);
        { var _a = (c.A3 + 0x50u); c.T0 = mem.ReadU32(_a); }
        { var _v = c.A3; c.T1 = c.A3 + 0x30u; }
        { var _v = c.A3; c.T2 = c.A3 + 0x18u; }
        c.RA = 0x800759ECu;
        EmitTriModels(c, m);
        c.RA = c.V1;
        { var _v = c.A2; c.A2 = c.A2 - 0x1u; }
        goto L80075AA8;
        L800759FC: ;
        { var _a = (c.A3 + 0x14u); var _sw = Gte.Read(17); mem.WriteU32(_a, _sw); }
        { var _a = (c.A3 + 0x2Cu); var _sw = Gte.Read(18); mem.WriteU32(_a, _sw); }
        { var _a = (c.A3 + 0x44u); var _sw = Gte.Read(19); mem.WriteU32(_a, _sw); }
        { var _a = (c.A3 + 0x10u); var _sw = Gte.Read(12); mem.WriteU32(_a, _sw); }
        { var _a = (c.A3 + 0x28u); var _sw = Gte.Read(13); mem.WriteU32(_a, _sw); }
        { var _a = (c.A3 + 0x40u); var _sw = Gte.Read(14); mem.WriteU32(_a, _sw); }
        { var _v = c.A3; c.A3 = c.A3 + 0x58u; }
        { var _a = (c.A3 + 0x54u); mem.WriteU32(_a, c.RA); }
        { var _a = (c.A3 - 0x10u); c.T4 = mem.ReadU32(_a); }
        { var _v = c.A3; c.T5 = c.A3 - 0x58u; }
        { var _v = c.A3; c.T6 = c.A3 - 0x28u; }
        { var _a = (c.A3 + 0x48u); mem.WriteU32(_a, c.T4); }
        { var _a = (c.A3 + 0x4Cu); mem.WriteU32(_a, c.T5); }
        { var _a = (c.A3 + 0x50u); mem.WriteU32(_a, c.T6); }
        c.RA = 0x80075A3Cu;
        DivTri2Body(c, m);
        { var _a = (c.A3 - 0xCu); c.T4 = mem.ReadU32(_a); }
        { var _v = c.A3; c.T5 = c.A3 - 0x40u; }
        { var _v = c.A3; c.T6 = c.A3 - 0x58u; }
        { var _a = (c.A3 + 0x48u); mem.WriteU32(_a, c.T4); }
        { var _a = (c.A3 + 0x4Cu); mem.WriteU32(_a, c.T5); }
        { var _a = (c.A3 + 0x50u); mem.WriteU32(_a, c.T6); }
        c.RA = 0x80075A5Cu;
        DivTri2Body(c, m);
        { var _a = (c.A3 - 0x8u); c.T4 = mem.ReadU32(_a); }
        { var _v = c.A3; c.T5 = c.A3 - 0x28u; }
        { var _v = c.A3; c.T6 = c.A3 - 0x40u; }
        { var _a = (c.A3 + 0x48u); mem.WriteU32(_a, c.T4); }
        { var _a = (c.A3 + 0x4Cu); mem.WriteU32(_a, c.T5); }
        { var _a = (c.A3 + 0x50u); mem.WriteU32(_a, c.T6); }
        c.RA = 0x80075A7Cu;
        DivTri2Body(c, m);
        { var _v = c.A3; c.T4 = c.A3 - 0x58u; }
        { var _v = c.A3; c.T5 = c.A3 - 0x40u; }
        { var _v = c.A3; c.T6 = c.A3 - 0x28u; }
        { var _a = (c.A3 + 0x48u); mem.WriteU32(_a, c.T4); }
        { var _a = (c.A3 + 0x4Cu); mem.WriteU32(_a, c.T5); }
        { var _a = (c.A3 + 0x50u); mem.WriteU32(_a, c.T6); }
        c.RA = 0x80075A9Cu;
        DivTri2Body(c, m);
        { var _a = (c.A3 + 0x54u); c.RA = mem.ReadU32(_a); }
        { var _v = c.A3; c.A3 = c.A3 - 0x58u; }
        { var _v = c.A2; c.A2 = c.A2 - 0x1u; }
        L80075AA8: ;
        c.V0 = c.A0;
        return;
    }
    // transcribed from generated/game.cs:168810
    static void EmitTriModels(CpuContext c, IMemory m)
    {
        var mem = (PSMemory)m;
        { var _a = (c.A1 + 0xCu); c.T4 = mem.ReadU16(_a); }
        { var _a = (c.T0 + 0x8u); c.T5 = mem.ReadU16(_a); }
        { var _v = c.T4; c.T4 = c.T4 << 16; }
        { var _s = c.T5; var _t = c.T4; c.T5 = _s + _t; }
        { var _a = (c.A1 + 0xEu); c.T4 = mem.ReadU16(_a); }
        { var _a = (c.T1 + 0x8u); c.T6 = mem.ReadU16(_a); }
        { var _v = c.T4; c.T4 = c.T4 << 16; }
        { var _s = c.T6; var _t = c.T4; c.T6 = _s + _t; }
        { var _a = (c.T2 + 0x8u); c.T7 = mem.ReadU16(_a); }
        { var _a = (c.A1 + 0x13u); c.T4 = (uint)(sbyte)mem.ReadU8(_a); }
        { var _a = (c.A0 + 0xCu); mem.WriteU32(_a, c.T5); }
        { var _a = (c.A0 + 0x18u); mem.WriteU32(_a, c.T6); }
        { var _a = (c.A0 + 0x24u); mem.WriteU32(_a, c.T7); }
        { var _a = (c.T0 + 0xFu); mem.WriteU8(_a, (byte)c.T4); }
        { var _a = (c.T0 + 0x10u); c.T4 = mem.ReadU32(_a); }
        { var _a = (c.T1 + 0x10u); c.T5 = mem.ReadU32(_a); }
        { var _a = (c.T2 + 0x10u); c.T6 = mem.ReadU32(_a); }
        { var _a = (c.A0 + 0x8u); mem.WriteU32(_a, c.T4); }
        { var _a = (c.A0 + 0x14u); mem.WriteU32(_a, c.T5); }
        { var _a = (c.A0 + 0x20u); mem.WriteU32(_a, c.T6); }
        { var _a = (c.A1 + 0x14u); c.T4 = mem.ReadU32(_a); }
        { var _v = c.A0; c.T9 = c.A0 << 8; }
        { var _v = c.T9; c.T9 = c.T9 >> 8; }
        { var _a = c.T4; c.T8 = mem.ReadU32(_a); }
        { var _a = c.T4; mem.WriteU32(_a, c.T9); }
        c.T6 = 0x09000000u;
        { var _s = c.T8; var _t = c.T6; c.T8 = _s | _t; }
        { var _a = (c.T0 + 0xCu); c.T4 = mem.ReadU32(_a); }
        { var _a = (c.T1 + 0xCu); c.T5 = mem.ReadU32(_a); }
        { var _a = (c.T2 + 0xCu); c.T6 = mem.ReadU32(_a); }
        { var _a = (c.A0 + 0x4u); mem.WriteU32(_a, c.T4); }
        { var _a = (c.A0 + 0x10u); mem.WriteU32(_a, c.T5); }
        { var _a = (c.A0 + 0x1Cu); mem.WriteU32(_a, c.T6); }
        { var _a = c.A0; mem.WriteU32(_a, c.T8); }
        if (DepthRecording) RecordNear(c.A0, c.T0, c.T1, c.T2, 0u, mem);
        { var _v = c.A0; c.A0 = c.A0 + 0x28u; }
        return;
    }
    // transcribed from generated/game.cs:168851
    static void DivQuad2(CpuContext c, IMemory m)
    {
        NearScreen.Widen((PSMemory)m, c.A1, NearScreen.Kind.Quad2);
        { var _v = c.A1; c.A3 = c.A1 + 0x78u; }
        NoteCorners((PSMemory)m, c.A3 + 0x78u, 4);
        c.A2 = 0x00000000u;
        DivQuad2Body(c, m);
    }
    // transcribed from generated/game.cs:168858
    static void DivQuad2Body(CpuContext c, IMemory m)
    {
        var mem = (PSMemory)m;
        { var _a = (c.A3 + 0x78u); c.T0 = mem.ReadU32(_a); }
        { var _a = (c.A3 + 0x7Cu); c.T1 = mem.ReadU32(_a); }
        { var _a = (c.A3 + 0x80u); c.T2 = mem.ReadU32(_a); }
        { var _a = (c.A3 + 0x84u); c.T3 = mem.ReadU32(_a); }
        c.T9 = Gte.ReadControl(26);
        { var _a = (c.T0 + 0x14u); c.T4 = mem.ReadU32(_a); }
        { var _a = (c.T1 + 0x14u); c.T5 = mem.ReadU32(_a); }
        { var _a = (c.T2 + 0x14u); c.T6 = mem.ReadU32(_a); }
        { var _a = (c.T3 + 0x14u); c.T7 = mem.ReadU32(_a); }
        { var _v = c.T9; c.T8 = (uint)((int)c.T9 >> 1); }
        { var _s = c.T4; var _t = c.T8; c.At = _s < _t ? 1u : 0u; }
        if (c.At == 0u) {
            { var _s = c.T5; var _t = c.T8; c.At = _s < _t ? 1u : 0u; }
            goto L80075BA8;
        }
        { var _s = c.T5; var _t = c.T8; c.At = _s < _t ? 1u : 0u; }
        if (c.At == 0u) {
            { var _s = c.T6; var _t = c.T8; c.At = _s < _t ? 1u : 0u; }
            goto L80075BA8;
        }
        { var _s = c.T6; var _t = c.T8; c.At = _s < _t ? 1u : 0u; }
        if (c.At == 0u) {
            { var _s = c.T7; var _t = c.T8; c.At = _s < _t ? 1u : 0u; }
            goto L80075BA8;
        }
        { var _s = c.T7; var _t = c.T8; c.At = _s < _t ? 1u : 0u; }
        if (c.At == 0u) {
            goto L80075BA8;
        }
        c.V0 = c.A0;
        return;
        L80075BA8: ;
        c.T9 = Gte.ReadControl(24);
        { var _a = (c.A1 + 0x4u); c.V0 = mem.ReadU32(_a); }
        { var _a = (c.A1 + 0x8u); c.V1 = mem.ReadU32(_a); }
        { var _v = c.T9; c.T9 = (uint)((int)c.T9 >> 16); }
        { var _v = c.V0; c.V0 = c.V0 >> 1; }
        { var _v = c.V1; c.V1 = c.V1 >> 1; }
        { var _s = c.T9; var _t = c.V0; c.T8 = _s + _t; }
        { var _a = (c.T0 + 0x10u); c.T4 = (uint)(short)mem.ReadU16(_a); }
        { var _a = (c.T1 + 0x10u); c.T5 = (uint)(short)mem.ReadU16(_a); }
        { var _a = (c.T2 + 0x10u); c.T6 = (uint)(short)mem.ReadU16(_a); }
        { var _a = (c.T3 + 0x10u); c.T7 = (uint)(short)mem.ReadU16(_a); }
        { var _s = c.T8; var _t = c.T4; c.At = (int)_s < (int)_t ? 1u : 0u; }
        if (c.At == 0u) {
            { var _s = c.T8; var _t = c.T5; c.At = (int)_s < (int)_t ? 1u : 0u; }
            goto L80075C04;
        }
        { var _s = c.T8; var _t = c.T5; c.At = (int)_s < (int)_t ? 1u : 0u; }
        if (c.At == 0u) {
            { var _s = c.T8; var _t = c.T6; c.At = (int)_s < (int)_t ? 1u : 0u; }
            goto L80075C04;
        }
        { var _s = c.T8; var _t = c.T6; c.At = (int)_s < (int)_t ? 1u : 0u; }
        if (c.At == 0u) {
            { var _s = c.T8; var _t = c.T7; c.At = (int)_s < (int)_t ? 1u : 0u; }
            goto L80075C04;
        }
        { var _s = c.T8; var _t = c.T7; c.At = (int)_s < (int)_t ? 1u : 0u; }
        if (c.At == 0u) {
            goto L80075C04;
        }
        c.V0 = c.A0;
        return;
        L80075C04: ;
        { var _s = c.T9; var _t = c.V0; c.T8 = _s - _t; }
        { var _s = c.T4; var _t = c.T8; c.At = (int)_s < (int)_t ? 1u : 0u; }
        if (c.At == 0u) {
            { var _s = c.T5; var _t = c.T8; c.At = (int)_s < (int)_t ? 1u : 0u; }
            goto L80075C38;
        }
        { var _s = c.T5; var _t = c.T8; c.At = (int)_s < (int)_t ? 1u : 0u; }
        if (c.At == 0u) {
            { var _s = c.T6; var _t = c.T8; c.At = (int)_s < (int)_t ? 1u : 0u; }
            goto L80075C38;
        }
        { var _s = c.T6; var _t = c.T8; c.At = (int)_s < (int)_t ? 1u : 0u; }
        if (c.At == 0u) {
            { var _s = c.T7; var _t = c.T8; c.At = (int)_s < (int)_t ? 1u : 0u; }
            goto L80075C38;
        }
        { var _s = c.T7; var _t = c.T8; c.At = (int)_s < (int)_t ? 1u : 0u; }
        if (c.At == 0u) {
            goto L80075C38;
        }
        c.V0 = c.A0;
        return;
        L80075C38: ;
        c.T9 = Gte.ReadControl(25);
        { var _a = (c.T0 + 0x12u); c.T4 = (uint)(short)mem.ReadU16(_a); }
        { var _a = (c.T1 + 0x12u); c.T5 = (uint)(short)mem.ReadU16(_a); }
        { var _a = (c.T2 + 0x12u); c.T6 = (uint)(short)mem.ReadU16(_a); }
        { var _a = (c.T3 + 0x12u); c.T7 = (uint)(short)mem.ReadU16(_a); }
        { var _v = c.T9; c.T9 = (uint)((int)c.T9 >> 16); }
        { var _s = c.T9; var _t = c.V1; c.T8 = _s + _t; }
        { var _s = c.T8; var _t = c.T4; c.At = (int)_s < (int)_t ? 1u : 0u; }
        if (c.At == 0u) {
            { var _s = c.T8; var _t = c.T5; c.At = (int)_s < (int)_t ? 1u : 0u; }
            goto L80075C84;
        }
        { var _s = c.T8; var _t = c.T5; c.At = (int)_s < (int)_t ? 1u : 0u; }
        if (c.At == 0u) {
            { var _s = c.T8; var _t = c.T6; c.At = (int)_s < (int)_t ? 1u : 0u; }
            goto L80075C84;
        }
        { var _s = c.T8; var _t = c.T6; c.At = (int)_s < (int)_t ? 1u : 0u; }
        if (c.At == 0u) {
            { var _s = c.T8; var _t = c.T7; c.At = (int)_s < (int)_t ? 1u : 0u; }
            goto L80075C84;
        }
        { var _s = c.T8; var _t = c.T7; c.At = (int)_s < (int)_t ? 1u : 0u; }
        if (c.At == 0u) {
            goto L80075C84;
        }
        c.V0 = c.A0;
        return;
        L80075C84: ;
        { var _s = c.T9; var _t = c.V1; c.T8 = _s - _t; }
        { var _s = c.T4; var _t = c.T8; c.At = (int)_s < (int)_t ? 1u : 0u; }
        if (c.At == 0u) {
            { var _s = c.T5; var _t = c.T8; c.At = (int)_s < (int)_t ? 1u : 0u; }
            goto L80075CB8;
        }
        { var _s = c.T5; var _t = c.T8; c.At = (int)_s < (int)_t ? 1u : 0u; }
        if (c.At == 0u) {
            { var _s = c.T6; var _t = c.T8; c.At = (int)_s < (int)_t ? 1u : 0u; }
            goto L80075CB8;
        }
        { var _s = c.T6; var _t = c.T8; c.At = (int)_s < (int)_t ? 1u : 0u; }
        if (c.At == 0u) {
            { var _s = c.T7; var _t = c.T8; c.At = (int)_s < (int)_t ? 1u : 0u; }
            goto L80075CB8;
        }
        { var _s = c.T7; var _t = c.T8; c.At = (int)_s < (int)_t ? 1u : 0u; }
        if (c.At == 0u) {
            goto L80075CB8;
        }
        c.V0 = c.A0;
        return;
        L80075CB8: ;
        { var _a = c.T0; c.T4 = (uint)(short)mem.ReadU16(_a); }
        { var _a = c.T1; c.T5 = (uint)(short)mem.ReadU16(_a); }
        { var _a = c.T2; c.T6 = (uint)(short)mem.ReadU16(_a); }
        { var _a = c.T3; c.T7 = (uint)(short)mem.ReadU16(_a); }
        { var _s = c.T4; var _t = c.T6; c.T9 = _s + _t; }
        { var _s = c.T7; var _t = c.T5; c.T8 = _s + _t; }
        { var _v = c.T8; c.T8 = (uint)((int)c.T8 >> 1); }
        { var _v = c.T9; c.T9 = (uint)((int)c.T9 >> 1); }
        { var _a = (c.A3 + 0x30u); mem.WriteU16(_a, (ushort)c.T8); }
        { var _a = (c.A3 + 0x18u); mem.WriteU16(_a, (ushort)c.T9); }
        { var _s = c.T4; var _t = c.T5; c.T8 = _s + _t; }
        { var _s = c.T7; var _t = c.T6; c.T9 = _s + _t; }
        { var _s = c.T8; var _t = c.T9; c.V1 = _s + _t; }
        { var _v = c.T8; c.T8 = (uint)((int)c.T8 >> 1); }
        { var _v = c.T9; c.T9 = (uint)((int)c.T9 >> 1); }
        { var _v = c.V1; c.V1 = (uint)((int)c.V1 >> 2); }
        { var _a = c.A3; mem.WriteU16(_a, (ushort)c.T8); }
        { var _a = (c.A3 + 0x48u); mem.WriteU16(_a, (ushort)c.T9); }
        { var _a = (c.A3 + 0x60u); mem.WriteU16(_a, (ushort)c.V1); }
        { var _a = (c.T0 + 0x2u); c.T4 = (uint)(short)mem.ReadU16(_a); }
        { var _a = (c.T1 + 0x2u); c.T5 = (uint)(short)mem.ReadU16(_a); }
        { var _a = (c.T2 + 0x2u); c.T6 = (uint)(short)mem.ReadU16(_a); }
        { var _a = (c.T3 + 0x2u); c.T7 = (uint)(short)mem.ReadU16(_a); }
        { var _s = c.T4; var _t = c.T6; c.T9 = _s + _t; }
        { var _s = c.T7; var _t = c.T5; c.T8 = _s + _t; }
        { var _v = c.T8; c.T8 = (uint)((int)c.T8 >> 1); }
        { var _v = c.T9; c.T9 = (uint)((int)c.T9 >> 1); }
        { var _a = (c.A3 + 0x32u); mem.WriteU16(_a, (ushort)c.T8); }
        { var _a = (c.A3 + 0x1Au); mem.WriteU16(_a, (ushort)c.T9); }
        { var _s = c.T4; var _t = c.T5; c.T8 = _s + _t; }
        { var _s = c.T7; var _t = c.T6; c.T9 = _s + _t; }
        { var _s = c.T8; var _t = c.T9; c.V1 = _s + _t; }
        { var _v = c.T8; c.T8 = (uint)((int)c.T8 >> 1); }
        { var _v = c.T9; c.T9 = (uint)((int)c.T9 >> 1); }
        { var _v = c.V1; c.V1 = (uint)((int)c.V1 >> 2); }
        { var _a = (c.A3 + 0x2u); mem.WriteU16(_a, (ushort)c.T8); }
        { var _a = (c.A3 + 0x4Au); mem.WriteU16(_a, (ushort)c.T9); }
        { var _a = (c.A3 + 0x62u); mem.WriteU16(_a, (ushort)c.V1); }
        { var _a = (c.T0 + 0x4u); c.T4 = (uint)(short)mem.ReadU16(_a); }
        { var _a = (c.T1 + 0x4u); c.T5 = (uint)(short)mem.ReadU16(_a); }
        { var _a = (c.T2 + 0x4u); c.T6 = (uint)(short)mem.ReadU16(_a); }
        { var _a = (c.T3 + 0x4u); c.T7 = (uint)(short)mem.ReadU16(_a); }
        { var _s = c.T4; var _t = c.T6; c.T9 = _s + _t; }
        { var _s = c.T7; var _t = c.T5; c.T8 = _s + _t; }
        { var _v = c.T8; c.T8 = (uint)((int)c.T8 >> 1); }
        { var _v = c.T9; c.T9 = (uint)((int)c.T9 >> 1); }
        { var _a = (c.A3 + 0x34u); mem.WriteU16(_a, (ushort)c.T8); }
        { var _a = (c.A3 + 0x1Cu); mem.WriteU16(_a, (ushort)c.T9); }
        { var _s = c.T4; var _t = c.T5; c.T8 = _s + _t; }
        { var _s = c.T7; var _t = c.T6; c.T9 = _s + _t; }
        { var _s = c.T8; var _t = c.T9; c.V1 = _s + _t; }
        { var _v = c.T8; c.T8 = (uint)((int)c.T8 >> 1); }
        { var _v = c.T9; c.T9 = (uint)((int)c.T9 >> 1); }
        { var _v = c.V1; c.V1 = (uint)((int)c.V1 >> 2); }
        { var _a = (c.A3 + 0x4u); mem.WriteU16(_a, (ushort)c.T8); }
        { var _a = (c.A3 + 0x4Cu); mem.WriteU16(_a, (ushort)c.T9); }
        { var _a = (c.A3 + 0x64u); mem.WriteU16(_a, (ushort)c.V1); }
        { var _a = c.A3; var _lw = mem.ReadU32(_a); Gte.Write(0, _lw); }
        { var _a = (c.A3 + 0x4u); var _lw = mem.ReadU32(_a); Gte.Write(1, _lw); }
        { var _a = (c.A3 + 0x18u); var _lw = mem.ReadU32(_a); Gte.Write(2, _lw); }
        { var _a = (c.A3 + 0x1Cu); var _lw = mem.ReadU32(_a); Gte.Write(3, _lw); }
        { var _a = (c.A3 + 0x60u); var _lw = mem.ReadU32(_a); Gte.Write(4, _lw); }
        { var _a = (c.A3 + 0x64u); var _lw = mem.ReadU32(_a); Gte.Write(5, _lw); }
        { var _a = (c.T0 + 0x8u); c.T4 = mem.ReadU8(_a); }
        Gte.Rtpt(12, false);
        if (DepthRecording)
        {
            NoteSz(c.A3, Gte.Read(12), Gte.Read(17));
            NoteSz(c.A3 + 0x18u, Gte.Read(13), Gte.Read(18));
            NoteSz(c.A3 + 0x60u, Gte.Read(14), Gte.Read(19));
        }
        { var _a = (c.T1 + 0x8u); c.T5 = mem.ReadU8(_a); }
        { var _a = (c.T2 + 0x8u); c.T6 = mem.ReadU8(_a); }
        { var _a = (c.T3 + 0x8u); c.T7 = mem.ReadU8(_a); }
        { var _s = c.T4; var _t = c.T6; c.T9 = _s + _t; }
        { var _s = c.T7; var _t = c.T5; c.T8 = _s + _t; }
        { var _v = c.T8; c.T8 = (uint)((int)c.T8 >> 1); }
        { var _v = c.T9; c.T9 = (uint)((int)c.T9 >> 1); }
        { var _a = (c.A3 + 0x38u); mem.WriteU8(_a, (byte)c.T8); }
        { var _a = (c.A3 + 0x20u); mem.WriteU8(_a, (byte)c.T9); }
        { var _s = c.T4; var _t = c.T5; c.T8 = _s + _t; }
        { var _s = c.T7; var _t = c.T6; c.T9 = _s + _t; }
        { var _s = c.T8; var _t = c.T9; c.V1 = _s + _t; }
        { var _v = c.T8; c.T8 = (uint)((int)c.T8 >> 1); }
        { var _v = c.T9; c.T9 = (uint)((int)c.T9 >> 1); }
        { var _v = c.V1; c.V1 = (uint)((int)c.V1 >> 2); }
        { var _a = (c.A3 + 0x8u); mem.WriteU8(_a, (byte)c.T8); }
        { var _a = (c.A3 + 0x50u); mem.WriteU8(_a, (byte)c.T9); }
        { var _a = (c.A3 + 0x68u); mem.WriteU8(_a, (byte)c.V1); }
        { var _a = (c.T0 + 0x9u); c.T4 = mem.ReadU8(_a); }
        { var _a = (c.T1 + 0x9u); c.T5 = mem.ReadU8(_a); }
        { var _a = (c.T2 + 0x9u); c.T6 = mem.ReadU8(_a); }
        { var _a = (c.T3 + 0x9u); c.T7 = mem.ReadU8(_a); }
        { var _s = c.T4; var _t = c.T6; c.T9 = _s + _t; }
        { var _s = c.T7; var _t = c.T5; c.T8 = _s + _t; }
        { var _v = c.T8; c.T8 = (uint)((int)c.T8 >> 1); }
        { var _v = c.T9; c.T9 = (uint)((int)c.T9 >> 1); }
        { var _a = (c.A3 + 0x39u); mem.WriteU8(_a, (byte)c.T8); }
        { var _a = (c.A3 + 0x21u); mem.WriteU8(_a, (byte)c.T9); }
        { var _s = c.T4; var _t = c.T5; c.T8 = _s + _t; }
        { var _s = c.T7; var _t = c.T6; c.T9 = _s + _t; }
        { var _s = c.T8; var _t = c.T9; c.V1 = _s + _t; }
        { var _v = c.T8; c.T8 = (uint)((int)c.T8 >> 1); }
        { var _v = c.T9; c.T9 = (uint)((int)c.T9 >> 1); }
        { var _v = c.V1; c.V1 = (uint)((int)c.V1 >> 2); }
        { var _a = (c.A3 + 0x9u); mem.WriteU8(_a, (byte)c.T8); }
        { var _a = (c.A3 + 0x51u); mem.WriteU8(_a, (byte)c.T9); }
        { var _a = (c.A3 + 0x69u); mem.WriteU8(_a, (byte)c.V1); }
        { var _a = (c.A3 + 0x10u); var _sw = Gte.Read(12); mem.WriteU32(_a, _sw); }
        { var _a = (c.A3 + 0x28u); var _sw = Gte.Read(13); mem.WriteU32(_a, _sw); }
        { var _a = (c.A3 + 0x70u); var _sw = Gte.Read(14); mem.WriteU32(_a, _sw); }
        { var _a = (c.A3 + 0x14u); var _sw = Gte.Read(17); mem.WriteU32(_a, _sw); }
        { var _a = (c.A3 + 0x2Cu); var _sw = Gte.Read(18); mem.WriteU32(_a, _sw); }
        { var _a = (c.A3 + 0x74u); var _sw = Gte.Read(19); mem.WriteU32(_a, _sw); }
        { var _a = (c.A3 + 0x30u); var _lw = mem.ReadU32(_a); Gte.Write(0, _lw); }
        { var _a = (c.A3 + 0x34u); var _lw = mem.ReadU32(_a); Gte.Write(1, _lw); }
        { var _a = (c.A3 + 0x48u); var _lw = mem.ReadU32(_a); Gte.Write(2, _lw); }
        { var _a = (c.A3 + 0x4Cu); var _lw = mem.ReadU32(_a); Gte.Write(3, _lw); }
        { var _a = (c.T0 + 0xCu); c.T4 = mem.ReadU8(_a); }
        Gte.Rtpt(12, false);
        if (DepthRecording)
        {
            NoteSz(c.A3 + 0x30u, Gte.Read(12), Gte.Read(17));
            NoteSz(c.A3 + 0x48u, Gte.Read(13), Gte.Read(18));
            NoteSz(c.A3 + 0x60u, Gte.Read(14), Gte.Read(19));
        }
        { var _a = (c.T1 + 0xCu); c.T5 = mem.ReadU8(_a); }
        { var _a = (c.T2 + 0xCu); c.T6 = mem.ReadU8(_a); }
        { var _a = (c.T3 + 0xCu); c.T7 = mem.ReadU8(_a); }
        { var _s = c.T4; var _t = c.T6; c.T9 = _s + _t; }
        { var _s = c.T7; var _t = c.T5; c.T8 = _s + _t; }
        { var _v = c.T8; c.T8 = (uint)((int)c.T8 >> 1); }
        { var _v = c.T9; c.T9 = (uint)((int)c.T9 >> 1); }
        { var _a = (c.A3 + 0x3Cu); mem.WriteU8(_a, (byte)c.T8); }
        { var _a = (c.A3 + 0x24u); mem.WriteU8(_a, (byte)c.T9); }
        { var _s = c.T4; var _t = c.T5; c.T8 = _s + _t; }
        { var _s = c.T7; var _t = c.T6; c.T9 = _s + _t; }
        { var _s = c.T8; var _t = c.T9; c.V1 = _s + _t; }
        { var _v = c.T8; c.T8 = (uint)((int)c.T8 >> 1); }
        { var _v = c.T9; c.T9 = (uint)((int)c.T9 >> 1); }
        { var _v = c.V1; c.V1 = (uint)((int)c.V1 >> 2); }
        { var _a = (c.A3 + 0xCu); mem.WriteU8(_a, (byte)c.T8); }
        { var _a = (c.A3 + 0x54u); mem.WriteU8(_a, (byte)c.T9); }
        { var _a = (c.A3 + 0x6Cu); mem.WriteU8(_a, (byte)c.V1); }
        { var _a = (c.T0 + 0xDu); c.T4 = mem.ReadU8(_a); }
        { var _a = (c.T1 + 0xDu); c.T5 = mem.ReadU8(_a); }
        { var _a = (c.T2 + 0xDu); c.T6 = mem.ReadU8(_a); }
        { var _a = (c.T3 + 0xDu); c.T7 = mem.ReadU8(_a); }
        { var _s = c.T4; var _t = c.T6; c.T9 = _s + _t; }
        { var _s = c.T7; var _t = c.T5; c.T8 = _s + _t; }
        { var _v = c.T8; c.T8 = (uint)((int)c.T8 >> 1); }
        { var _v = c.T9; c.T9 = (uint)((int)c.T9 >> 1); }
        { var _a = (c.A3 + 0x3Du); mem.WriteU8(_a, (byte)c.T8); }
        { var _a = (c.A3 + 0x25u); mem.WriteU8(_a, (byte)c.T9); }
        { var _s = c.T4; var _t = c.T5; c.T8 = _s + _t; }
        { var _s = c.T7; var _t = c.T6; c.T9 = _s + _t; }
        { var _s = c.T8; var _t = c.T9; c.V1 = _s + _t; }
        { var _v = c.T8; c.T8 = (uint)((int)c.T8 >> 1); }
        { var _v = c.T9; c.T9 = (uint)((int)c.T9 >> 1); }
        { var _v = c.V1; c.V1 = (uint)((int)c.V1 >> 2); }
        { var _a = (c.A3 + 0xDu); mem.WriteU8(_a, (byte)c.T8); }
        { var _a = (c.A3 + 0x55u); mem.WriteU8(_a, (byte)c.T9); }
        { var _a = (c.A3 + 0x6Du); mem.WriteU8(_a, (byte)c.V1); }
        { var _a = (c.T0 + 0xEu); c.T4 = mem.ReadU8(_a); }
        { var _a = (c.T1 + 0xEu); c.T5 = mem.ReadU8(_a); }
        { var _a = (c.T2 + 0xEu); c.T6 = mem.ReadU8(_a); }
        { var _a = (c.T3 + 0xEu); c.T7 = mem.ReadU8(_a); }
        { var _s = c.T4; var _t = c.T6; c.T9 = _s + _t; }
        { var _s = c.T7; var _t = c.T5; c.T8 = _s + _t; }
        { var _v = c.T8; c.T8 = (uint)((int)c.T8 >> 1); }
        { var _v = c.T9; c.T9 = (uint)((int)c.T9 >> 1); }
        { var _a = (c.A3 + 0x3Eu); mem.WriteU8(_a, (byte)c.T8); }
        { var _a = (c.A3 + 0x26u); mem.WriteU8(_a, (byte)c.T9); }
        { var _s = c.T4; var _t = c.T5; c.T8 = _s + _t; }
        { var _s = c.T7; var _t = c.T6; c.T9 = _s + _t; }
        { var _s = c.T8; var _t = c.T9; c.V1 = _s + _t; }
        { var _v = c.T8; c.T8 = (uint)((int)c.T8 >> 1); }
        { var _v = c.T9; c.T9 = (uint)((int)c.T9 >> 1); }
        { var _v = c.V1; c.V1 = (uint)((int)c.V1 >> 2); }
        { var _a = (c.A3 + 0xEu); mem.WriteU8(_a, (byte)c.T8); }
        { var _a = (c.A3 + 0x56u); mem.WriteU8(_a, (byte)c.T9); }
        { var _a = (c.A3 + 0x6Eu); mem.WriteU8(_a, (byte)c.V1); }
        { var _a = (c.A3 + 0x40u); var _sw = Gte.Read(12); mem.WriteU32(_a, _sw); }
        { var _a = (c.A3 + 0x58u); var _sw = Gte.Read(13); mem.WriteU32(_a, _sw); }
        { var _a = (c.A3 + 0x44u); var _sw = Gte.Read(17); mem.WriteU32(_a, _sw); }
        { var _a = (c.A3 + 0x5Cu); var _sw = Gte.Read(18); mem.WriteU32(_a, _sw); }
        { var _a = c.A1; c.T4 = mem.ReadU32(_a); }
        { var _v = c.A2; c.A2 = c.A2 + 0x1u; }
        if (c.T4 != c.A2) {
            goto L80075FF4;
        }
        c.V1 = c.RA;
        { var _a = (c.A3 + 0x78u); c.T0 = mem.ReadU32(_a); }
        { var _v = c.A3; c.T1 = c.A3 + 0x0u; }
        { var _v = c.A3; c.T2 = c.A3 + 0x18u; }
        { var _v = c.A3; c.T3 = c.A3 + 0x60u; }
        c.RA = 0x80075F9Cu;
        EmitQuadModels(c, m);
        { var _a = (c.A3 + 0x7Cu); c.T0 = mem.ReadU32(_a); }
        { var _v = c.A3; c.T1 = c.A3 + 0x30u; }
        { var _v = c.A3; c.T2 = c.A3 + 0x0u; }
        { var _v = c.A3; c.T3 = c.A3 + 0x60u; }
        c.RA = 0x80075FB4u;
        EmitQuadModels(c, m);
        { var _a = (c.A3 + 0x80u); c.T0 = mem.ReadU32(_a); }
        { var _v = c.A3; c.T1 = c.A3 + 0x18u; }
        { var _v = c.A3; c.T2 = c.A3 + 0x48u; }
        { var _v = c.A3; c.T3 = c.A3 + 0x60u; }
        c.RA = 0x80075FCCu;
        EmitQuadModels(c, m);
        { var _a = (c.A3 + 0x84u); c.T0 = mem.ReadU32(_a); }
        { var _v = c.A3; c.T1 = c.A3 + 0x48u; }
        { var _v = c.A3; c.T2 = c.A3 + 0x30u; }
        { var _v = c.A3; c.T3 = c.A3 + 0x60u; }
        c.RA = 0x80075FE4u;
        EmitQuadModels(c, m);
        c.RA = c.V1;
        { var _v = c.A2; c.A2 = c.A2 - 0x1u; }
        goto L800760A8;
        L80075FF4: ;
        { var _v = c.A3; c.A3 = c.A3 + 0x8Cu; }
        { var _a = (c.A3 + 0x88u); mem.WriteU32(_a, c.RA); }
        { var _a = (c.A3 - 0x14u); c.T4 = mem.ReadU32(_a); }
        { var _v = c.A3; c.T5 = c.A3 - 0x8Cu; }
        { var _v = c.A3; c.T6 = c.A3 - 0x74u; }
        { var _v = c.A3; c.T7 = c.A3 - 0x2Cu; }
        { var _a = (c.A3 + 0x78u); mem.WriteU32(_a, c.T4); }
        { var _a = (c.A3 + 0x7Cu); mem.WriteU32(_a, c.T5); }
        { var _a = (c.A3 + 0x80u); mem.WriteU32(_a, c.T6); }
        { var _a = (c.A3 + 0x84u); mem.WriteU32(_a, c.T7); }
        c.RA = 0x80076024u;
        DivQuad2Body(c, m);
        { var _a = (c.A3 - 0x10u); c.T4 = mem.ReadU32(_a); }
        { var _v = c.A3; c.T5 = c.A3 - 0x5Cu; }
        { var _v = c.A3; c.T6 = c.A3 - 0x8Cu; }
        { var _v = c.A3; c.T7 = c.A3 - 0x2Cu; }
        { var _a = (c.A3 + 0x78u); mem.WriteU32(_a, c.T4); }
        { var _a = (c.A3 + 0x7Cu); mem.WriteU32(_a, c.T5); }
        { var _a = (c.A3 + 0x80u); mem.WriteU32(_a, c.T6); }
        { var _a = (c.A3 + 0x84u); mem.WriteU32(_a, c.T7); }
        c.RA = 0x8007604Cu;
        DivQuad2Body(c, m);
        { var _a = (c.A3 - 0xCu); c.T4 = mem.ReadU32(_a); }
        { var _v = c.A3; c.T5 = c.A3 - 0x74u; }
        { var _v = c.A3; c.T6 = c.A3 - 0x44u; }
        { var _v = c.A3; c.T7 = c.A3 - 0x2Cu; }
        { var _a = (c.A3 + 0x78u); mem.WriteU32(_a, c.T4); }
        { var _a = (c.A3 + 0x7Cu); mem.WriteU32(_a, c.T5); }
        { var _a = (c.A3 + 0x80u); mem.WriteU32(_a, c.T6); }
        { var _a = (c.A3 + 0x84u); mem.WriteU32(_a, c.T7); }
        c.RA = 0x80076074u;
        DivQuad2Body(c, m);
        { var _a = (c.A3 - 0x8u); c.T4 = mem.ReadU32(_a); }
        { var _v = c.A3; c.T5 = c.A3 - 0x44u; }
        { var _v = c.A3; c.T6 = c.A3 - 0x5Cu; }
        { var _v = c.A3; c.T7 = c.A3 - 0x2Cu; }
        { var _a = (c.A3 + 0x78u); mem.WriteU32(_a, c.T4); }
        { var _a = (c.A3 + 0x7Cu); mem.WriteU32(_a, c.T5); }
        { var _a = (c.A3 + 0x80u); mem.WriteU32(_a, c.T6); }
        { var _a = (c.A3 + 0x84u); mem.WriteU32(_a, c.T7); }
        c.RA = 0x8007609Cu;
        DivQuad2Body(c, m);
        { var _a = (c.A3 + 0x88u); c.RA = mem.ReadU32(_a); }
        { var _v = c.A3; c.A3 = c.A3 - 0x8Cu; }
        { var _v = c.A2; c.A2 = c.A2 - 0x1u; }
        L800760A8: ;
        c.V0 = c.A0;
        return;
    }
    // transcribed from generated/game.cs:169259
    static void EmitQuadModels(CpuContext c, IMemory m)
    {
        var mem = (PSMemory)m;
        { var _a = (c.A1 + 0xCu); c.T4 = mem.ReadU16(_a); }
        { var _a = (c.T0 + 0x8u); c.T5 = mem.ReadU16(_a); }
        { var _v = c.T4; c.T4 = c.T4 << 16; }
        { var _s = c.T5; var _t = c.T4; c.T5 = _s + _t; }
        { var _a = (c.A1 + 0xEu); c.T4 = mem.ReadU16(_a); }
        { var _a = (c.T1 + 0x8u); c.T6 = mem.ReadU16(_a); }
        { var _v = c.T4; c.T4 = c.T4 << 16; }
        { var _s = c.T6; var _t = c.T4; c.T6 = _s + _t; }
        { var _a = (c.T2 + 0x8u); c.T7 = mem.ReadU16(_a); }
        { var _a = (c.T3 + 0x8u); c.T8 = mem.ReadU16(_a); }
        { var _a = (c.A1 + 0x13u); c.T4 = (uint)(sbyte)mem.ReadU8(_a); }
        { var _a = (c.A0 + 0xCu); mem.WriteU32(_a, c.T5); }
        { var _a = (c.A0 + 0x18u); mem.WriteU32(_a, c.T6); }
        { var _a = (c.A0 + 0x24u); mem.WriteU32(_a, c.T7); }
        { var _a = (c.A0 + 0x30u); mem.WriteU32(_a, c.T8); }
        { var _a = (c.T0 + 0xFu); mem.WriteU8(_a, (byte)c.T4); }
        { var _a = (c.T0 + 0x10u); c.T4 = mem.ReadU32(_a); }
        { var _a = (c.T1 + 0x10u); c.T5 = mem.ReadU32(_a); }
        { var _a = (c.T2 + 0x10u); c.T6 = mem.ReadU32(_a); }
        { var _a = (c.T3 + 0x10u); c.T7 = mem.ReadU32(_a); }
        { var _a = (c.A0 + 0x8u); mem.WriteU32(_a, c.T4); }
        { var _a = (c.A0 + 0x14u); mem.WriteU32(_a, c.T5); }
        { var _a = (c.A0 + 0x20u); mem.WriteU32(_a, c.T6); }
        { var _a = (c.A0 + 0x2Cu); mem.WriteU32(_a, c.T7); }
        { var _a = (c.A1 + 0x14u); c.T4 = mem.ReadU32(_a); }
        { var _v = c.A0; c.T9 = c.A0 << 8; }
        { var _v = c.T9; c.T9 = c.T9 >> 8; }
        { var _a = c.T4; c.T8 = mem.ReadU32(_a); }
        { var _a = c.T4; mem.WriteU32(_a, c.T9); }
        c.T6 = 0x0C000000u;
        { var _s = c.T8; var _t = c.T6; c.T8 = _s | _t; }
        { var _a = (c.T0 + 0xCu); c.T4 = mem.ReadU32(_a); }
        { var _a = (c.T1 + 0xCu); c.T5 = mem.ReadU32(_a); }
        { var _a = (c.T2 + 0xCu); c.T6 = mem.ReadU32(_a); }
        { var _a = (c.T3 + 0xCu); c.T7 = mem.ReadU32(_a); }
        { var _a = (c.A0 + 0x4u); mem.WriteU32(_a, c.T4); }
        { var _a = (c.A0 + 0x10u); mem.WriteU32(_a, c.T5); }
        { var _a = (c.A0 + 0x1Cu); mem.WriteU32(_a, c.T6); }
        { var _a = (c.A0 + 0x28u); mem.WriteU32(_a, c.T7); }
        { var _a = c.A0; mem.WriteU32(_a, c.T8); }
        if (DepthRecording) RecordNear(c.A0, c.T0, c.T1, c.T2, c.T3, mem);
        { var _v = c.A0; c.A0 = c.A0 + 0x34u; }
        return;
    }
}
