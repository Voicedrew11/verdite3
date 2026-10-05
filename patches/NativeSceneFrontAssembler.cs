using RecompOne.Runtime;
using RecompOne.Runtime.Dispatch;
using RecompOne.Runtime.Context;
using RecompOne.Runtime.Memory;
using Game = Recompiled.KingsField3_game;

namespace Kf3;

public static partial class NativeScene
{
    // Literal reference for func_80038844; see docs/GPU_RENDERER.md.
    static void RunFrontAssembler(CpuContext c, PSMemory mem)
    {

        c.SP = c.SP - 0x20u;
        c.A0 = c.A0 & 0xFFFFu;
        c.V1 = c.A0 << 3;
        c.V1 = c.V1 - c.A0;
        c.V1 = c.V1 << 2;
        c.A0 = 0x1F800000u;
        c.A0 = mem.ReadU32((c.A0 + 0x10u));
        c.V1 = c.V1 + 0xCu;
        mem.WriteU32((c.SP + 0x18u), c.S6);
        mem.WriteU32((c.SP + 0x14u), c.S5);
        mem.WriteU32((c.SP + 0x10u), c.S4);
        mem.WriteU32((c.SP + 0xCu), c.S3);
        mem.WriteU32((c.SP + 0x8u), c.S2);
        mem.WriteU32((c.SP + 0x4u), c.S1);
        mem.WriteU32(c.SP, c.S0);
        c.V1 = c.V1 + c.A0;
        c.At = 0x1F800000u;
        mem.WriteU32((c.At + 0x24u), c.V1);
        c.V0 = mem.ReadU32((c.V1 + 0x8u));
        c.S0 = c.A1;
        c.V0 = c.V0 + 0xCu;
        c.V0 = c.V0 + c.A0;
        c.At = 0x1F800000u;
        mem.WriteU32((c.At + 0x28u), c.V0);
        c.V0 = mem.ReadU32((c.V1 + 0x10u));
        c.S4 = c.A2;
        c.V0 = c.V0 + 0xCu;
        c.V0 = c.V0 + c.A0;
        c.At = 0x1F800000u;
        mem.WriteU32((c.At + 0x20u), c.V0);
        c.S3 = mem.ReadU32((c.V1 + 0x14u));
        c.V0 = 0x1F800000u;
        c.V0 = mem.ReadU32((c.V0 + 0x78u));
        c.T0 = 0x1F800000u;
        c.V0 = c.S3 + c.V0;
        c.V1 = c.S3;
        c.At = 0x1F800000u;
        mem.WriteU32((c.At + 0x78u), c.V0);
        if (c.V1 == 0u) {
            c.S3 = c.S3 - 0x1u;
            goto L80039400;
        }
        c.S3 = c.S3 - 0x1u;
        c.S5 = 0x1F800000u;
        c.S5 = c.S5 | 0x0070u;
        c.S2 = 0x1F800000u;
        c.S2 = c.S2 | 0x0064u;
        c.S1 = 0x55550000u;
        c.S1 = c.S1 | 0x5556u;
        c.V0 = 0x0003u;
        c.A2 = c.V0 - c.S4;
        c.T8 = 0x00FF0000u;
        c.T8 = c.T8 | 0xFFFFu;
        c.T9 = 0xFF000000u;
        L80038910: ;
        RecompOne.Runtime.Interrupts.Poll(c, mem);
        c.V0 = mem.ReadU32((c.T0 + 0x20u));
        c.A0 = mem.ReadU32((c.T0 + 0x20u));
        c.V0 = mem.ReadU32(c.V0);
        mem.WriteU32((c.T0 + 0x1Cu), c.V0);
        c.V1 = mem.ReadU8((c.T0 + 0x1Fu));
        c.V0 = c.A0 + 0x4u;
        mem.WriteU32((c.T0 + 0x20u), c.V0);
        c.V0 = 0x0034u;
        c.V1 = c.V1 & 0x00FDu;
        if (c.V1 == c.V0) {
            c.V0 = (int)c.V1 < 53 ? 1u : 0u;
            goto L80038E00;
        }
        c.V0 = (int)c.V1 < 53 ? 1u : 0u;
        if (c.V0 == 0u) {
            c.V0 = 0x0024u;
            goto L80038960;
        }
        c.V0 = 0x0024u;
        if (c.V1 == c.V0) {
            c.V0 = 0x002Cu;
            goto L80038974;
        }
        c.V0 = 0x002Cu;
        if (c.V1 == c.V0) {
            c.V0 = c.S3;
            goto L80038BC4;
        }
        c.V0 = c.S3;
        goto L800393E4;
        L80038960: ;
        c.V0 = 0x003Cu;
        if (c.V1 == c.V0) {
            c.V0 = c.S3;
            goto L8003908C;
        }
        c.V0 = c.S3;
        goto L800393E4;
        L80038974: ;
        c.V1 = mem.ReadU16((c.A0 + 0x12u));
        c.V0 = mem.ReadU32((c.T0 + 0x44u));
        c.T1 = c.V1 + c.V0;
        c.V1 = mem.ReadU16((c.A0 + 0x14u));
        c.A0 = mem.ReadU16((c.A0 + 0x16u));
        c.S6 = mem.ReadU32(c.T1);
        c.T2 = c.V1 + c.V0;
        c.T3 = c.A0 + c.V0;
        c.T4 = c.S6;
        c.S6 = mem.ReadU32(c.T2);
        c.T5 = c.S6;
        c.S6 = mem.ReadU32(c.T3);
        c.T6 = c.S6;
        RecompOne.Runtime.Gte.Write(12, c.T4);
        RecompOne.Runtime.Gte.Write(14, c.T6);
        RecompOne.Runtime.Gte.Write(13, c.T5);
        RecompOne.Runtime.Gte.Nclip();
        c.T4 = c.S5;
        { var _sw = RecompOne.Runtime.Gte.Read(24); mem.WriteU32(c.T4, _sw); }
        c.V0 = mem.ReadU32((c.T0 + 0x70u));
        if ((int)c.V0 <= 0) {
            c.V0 = c.S3;
            goto L800393E4;
        }
        c.V0 = c.S3;
        c.A3 = mem.ReadU32((c.T0 + 0x14u));
        c.V0 = mem.ReadU32((c.T0 + 0x18u));
        c.V1 = c.A3 + 0x20u;
        c.V0 = c.V0 < c.V1 ? 1u : 0u;
        if (c.V0 != 0u) {
            mem.WriteU32((c.T0 + 0x14u), c.V1);
            goto L80039400;
        }
        mem.WriteU32((c.T0 + 0x14u), c.V1);
        c.V0 = mem.ReadU32((c.T0 + 0x80u));
        c.V1 = mem.ReadU32((c.T0 + 0x20u));
        c.V0 = c.V0 + 0x1u;
        mem.WriteU32((c.T0 + 0x80u), c.V0);
        c.V0 = mem.ReadU16((c.V1 + 0x2u));
        c.V1 = mem.ReadU16((c.T0 + 0x84u));
        c.V0 = c.V0 + c.V1;
        mem.WriteU16((c.A3 + 0xEu), (ushort)c.V0);
        c.V0 = mem.ReadU32((c.T0 + 0x20u));
        c.V0 = mem.ReadU16((c.V0 + 0x6u));
        mem.WriteU16((c.A3 + 0x16u), (ushort)c.V0);
        c.V0 = mem.ReadU32(c.T1);
        mem.WriteU32((c.A3 + 0x8u), c.V0);
        c.V0 = mem.ReadU32(c.T2);
        mem.WriteU32((c.A3 + 0x10u), c.V0);
        c.V0 = mem.ReadU32(c.T3);
        mem.WriteU32((c.A3 + 0x18u), c.V0);
        c.V0 = mem.ReadU32((c.T0 + 0x20u));
        c.V0 = mem.ReadU16(c.V0);
        mem.WriteU16((c.A3 + 0xCu), (ushort)c.V0);
        c.V0 = mem.ReadU32((c.T0 + 0x20u));
        c.V0 = mem.ReadU16((c.V0 + 0x4u));
        mem.WriteU16((c.A3 + 0x14u), (ushort)c.V0);
        c.V0 = mem.ReadU32((c.T0 + 0x20u));
        c.V0 = mem.ReadU16((c.V0 + 0x8u));
        mem.WriteU16((c.A3 + 0x1Cu), (ushort)c.V0);
        c.V0 = mem.ReadU32((c.T0 + 0x20u));
        c.V1 = mem.ReadU32((c.T0 + 0x28u));
        c.V0 = mem.ReadU16((c.V0 + 0xCu));
        c.V0 = c.V0 + c.V1;
        c.T4 = c.V0;
        { var _lw = mem.ReadU32(c.T4); RecompOne.Runtime.Gte.Write(0, _lw); }
        { var _lw = mem.ReadU32((c.T4 + 0x4u)); RecompOne.Runtime.Gte.Write(1, _lw); }
        c.T4 = c.S2;
        { var _lw = mem.ReadU32(c.T4); RecompOne.Runtime.Gte.Write(6, _lw); }
        c.V1 = (uint)(short)mem.ReadU16((c.T1 + 0x6u));
        c.V0 = (uint)(short)mem.ReadU16((c.T2 + 0x6u));
        c.A0 = (uint)(short)mem.ReadU16((c.T3 + 0x6u));
        c.V1 = c.V1 + c.V0;
        c.V1 = c.V1 + c.A0;
        { var _r = (long)(int)c.V1 * (int)c.S1; c.LO = (uint)_r; c.HI = (uint)(_r >> 32); }
        c.V1 = (uint)((int)c.V1 >> 31);
        c.V0 = c.HI;
        c.V0 = c.V0 - c.V1;
        c.V0 = (uint)((int)c.V0 >> (int)(c.A2 & 31u));
        c.T4 = c.V0;
        RecompOne.Runtime.Gte.Write(8, c.T4);
        RecompOne.Runtime.Gte.NcdsOp(12, true);
        c.V0 = c.A3 + 0x4u;
        c.T4 = c.V0;
        { var _sw = RecompOne.Runtime.Gte.Read(22); mem.WriteU32(c.T4, _sw); }
        c.V0 = 0x0007u;
        mem.WriteU8((c.A3 + 0x3u), (byte)c.V0);
        c.V0 = mem.ReadU8((c.T0 + 0x1Fu));
        if (c.S4 == 0u) {
            mem.WriteU8((c.A3 + 0x7u), (byte)c.V0);
            goto L80038B84;
        }
        mem.WriteU8((c.A3 + 0x7u), (byte)c.V0);
        c.V1 = (uint)(short)mem.ReadU16((c.T1 + 0x4u));
        c.V0 = (uint)(short)mem.ReadU16((c.T2 + 0x4u));
        c.A0 = (uint)(short)mem.ReadU16((c.T3 + 0x4u));
        c.V1 = c.V1 + c.V0;
        c.V1 = c.V1 + c.A0;
        { var _r = (long)(int)c.V1 * (int)c.S1; c.LO = (uint)_r; c.HI = (uint)(_r >> 32); }
        c.V1 = (uint)((int)c.V1 >> 31);
        c.V0 = c.HI;
        c.V0 = c.V0 - c.V1;
        c.V0 = (uint)((int)c.V0 >> 11);
        if ((int)c.V0 <= 0) {
            c.V0 = c.V0 + c.S0;
            goto L800393E0;
        }
        c.V0 = c.V0 + c.S0;
        c.A0 = c.V0 & 0x0007u;
        c.V0 = c.A0 < 0x00000008u ? 1u : 0u;
        if (c.V0 == 0u) {
            c.A0 = c.A0 << 2;
            goto L800393E0;
        }
        c.A0 = c.A0 << 2;
        c.V0 = mem.ReadU32((c.T0 + 0xCu));
        c.V1 = mem.ReadU32(c.A3);
        c.V0 = c.A0 + c.V0;
        c.V0 = mem.ReadU32(c.V0);
        c.V1 = c.V1 & c.T9;
        c.V0 = c.V0 & c.T8;
        c.V1 = c.V1 | c.V0;
        mem.WriteU32(c.A3, c.V1);
        c.V0 = mem.ReadU32((c.T0 + 0xCu));
        c.A0 = c.A0 + c.V0;
        goto L800393BC;
        L80038B84: ;
        c.V0 = (uint)(short)mem.ReadU16((c.T1 + 0x4u));
        c.V1 = (uint)(short)mem.ReadU16((c.T2 + 0x4u));
        c.A0 = (uint)(short)mem.ReadU16((c.T3 + 0x4u));
        c.V0 = c.V0 + c.V1;
        c.V0 = c.V0 + c.A0;
        { var _r = (long)(int)c.V0 * (int)c.S1; c.LO = (uint)_r; c.HI = (uint)(_r >> 32); }
        c.V0 = (uint)((int)c.V0 >> 31);
        c.V1 = c.HI;
        c.V0 = c.V1 - c.V0;
        if ((int)c.V0 <= 0) {
            c.V1 = c.V0 + c.S0;
            goto L800393E0;
        }
        c.V1 = c.V0 + c.S0;
        c.V0 = c.V1 < 0x00002000u ? 1u : 0u;
        if (c.V0 == 0u) {
            c.A0 = c.V1 << 2;
            goto L800393E0;
        }
        c.A0 = c.V1 << 2;
        goto L80039390;
        L80038BC4: ;
        c.V1 = mem.ReadU16((c.A0 + 0x16u));
        c.V0 = mem.ReadU32((c.T0 + 0x44u));
        c.T1 = c.V1 + c.V0;
        c.V1 = mem.ReadU16((c.A0 + 0x18u));
        c.A0 = mem.ReadU16((c.A0 + 0x1Au));
        c.S6 = mem.ReadU32(c.T1);
        c.T2 = c.V1 + c.V0;
        c.T3 = c.A0 + c.V0;
        c.T4 = c.S6;
        c.S6 = mem.ReadU32(c.T2);
        c.T5 = c.S6;
        c.S6 = mem.ReadU32(c.T3);
        c.T6 = c.S6;
        RecompOne.Runtime.Gte.Write(12, c.T4);
        RecompOne.Runtime.Gte.Write(14, c.T6);
        RecompOne.Runtime.Gte.Write(13, c.T5);
        RecompOne.Runtime.Gte.Nclip();
        c.T4 = c.S5;
        { var _sw = RecompOne.Runtime.Gte.Read(24); mem.WriteU32(c.T4, _sw); }
        c.V0 = mem.ReadU32((c.T0 + 0x70u));
        if ((int)c.V0 <= 0) {
            c.V0 = c.S3;
            goto L800393E4;
        }
        c.V0 = c.S3;
        c.V0 = mem.ReadU32((c.T0 + 0x20u));
        c.A3 = mem.ReadU32((c.T0 + 0x14u));
        c.V1 = mem.ReadU32((c.T0 + 0x44u));
        c.A1 = mem.ReadU16((c.V0 + 0x18u));
        c.V0 = mem.ReadU32((c.T0 + 0x18u));
        c.A0 = c.A3 + 0x28u;
        mem.WriteU32((c.T0 + 0x14u), c.A0);
        c.V0 = c.V0 < c.A0 ? 1u : 0u;
        if (c.V0 != 0u) {
            c.A1 = c.A1 + c.V1;
            goto L80039400;
        }
        c.A1 = c.A1 + c.V1;
        c.V0 = mem.ReadU32((c.T0 + 0x80u));
        c.V1 = mem.ReadU32((c.T0 + 0x20u));
        c.V0 = c.V0 + 0x1u;
        mem.WriteU32((c.T0 + 0x80u), c.V0);
        c.V0 = mem.ReadU16((c.V1 + 0x2u));
        c.V1 = mem.ReadU16((c.T0 + 0x84u));
        c.V0 = c.V0 + c.V1;
        mem.WriteU16((c.A3 + 0xEu), (ushort)c.V0);
        c.V0 = mem.ReadU32((c.T0 + 0x20u));
        c.V0 = mem.ReadU16((c.V0 + 0x6u));
        mem.WriteU16((c.A3 + 0x16u), (ushort)c.V0);
        c.V0 = mem.ReadU32(c.T1);
        mem.WriteU32((c.A3 + 0x8u), c.V0);
        c.V0 = mem.ReadU32(c.T2);
        mem.WriteU32((c.A3 + 0x10u), c.V0);
        c.V0 = mem.ReadU32(c.T3);
        mem.WriteU32((c.A3 + 0x18u), c.V0);
        c.V0 = mem.ReadU32(c.A1);
        mem.WriteU32((c.A3 + 0x20u), c.V0);
        c.V0 = mem.ReadU32((c.T0 + 0x20u));
        c.V0 = mem.ReadU16(c.V0);
        mem.WriteU16((c.A3 + 0xCu), (ushort)c.V0);
        c.V0 = mem.ReadU32((c.T0 + 0x20u));
        c.V0 = mem.ReadU16((c.V0 + 0x4u));
        mem.WriteU16((c.A3 + 0x14u), (ushort)c.V0);
        c.V0 = mem.ReadU32((c.T0 + 0x20u));
        c.V0 = mem.ReadU16((c.V0 + 0x8u));
        mem.WriteU16((c.A3 + 0x1Cu), (ushort)c.V0);
        c.V0 = mem.ReadU32((c.T0 + 0x20u));
        c.V0 = mem.ReadU16((c.V0 + 0xCu));
        mem.WriteU16((c.A3 + 0x24u), (ushort)c.V0);
        c.V0 = mem.ReadU32((c.T0 + 0x20u));
        c.V1 = mem.ReadU32((c.T0 + 0x28u));
        c.V0 = mem.ReadU16((c.V0 + 0x10u));
        c.V0 = c.V0 + c.V1;
        c.T4 = c.V0;
        { var _lw = mem.ReadU32(c.T4); RecompOne.Runtime.Gte.Write(0, _lw); }
        { var _lw = mem.ReadU32((c.T4 + 0x4u)); RecompOne.Runtime.Gte.Write(1, _lw); }
        c.T4 = c.S2;
        { var _lw = mem.ReadU32(c.T4); RecompOne.Runtime.Gte.Write(6, _lw); }
        c.V0 = (uint)(short)mem.ReadU16((c.T1 + 0x6u));
        c.V1 = (uint)(short)mem.ReadU16((c.T2 + 0x6u));
        c.V0 = c.V0 + c.V1;
        c.V1 = (uint)(short)mem.ReadU16((c.T3 + 0x6u));
        c.A0 = (uint)(short)mem.ReadU16((c.A1 + 0x6u));
        c.V0 = c.V0 + c.V1;
        c.V0 = c.V0 + c.A0;
        c.V0 = (uint)((int)c.V0 >> 2);
        c.V0 = (uint)((int)c.V0 >> (int)(c.A2 & 31u));
        c.T4 = c.V0;
        RecompOne.Runtime.Gte.Write(8, c.T4);
        RecompOne.Runtime.Gte.NcdsOp(12, true);
        c.V0 = c.A3 + 0x4u;
        c.T4 = c.V0;
        { var _sw = RecompOne.Runtime.Gte.Read(22); mem.WriteU32(c.T4, _sw); }
        c.V0 = 0x0009u;
        mem.WriteU8((c.A3 + 0x3u), (byte)c.V0);
        c.V0 = mem.ReadU8((c.T0 + 0x1Fu));
        if (c.S4 == 0u) {
            mem.WriteU8((c.A3 + 0x7u), (byte)c.V0);
            goto L80039358;
        }
        mem.WriteU8((c.A3 + 0x7u), (byte)c.V0);
        c.V0 = (uint)(short)mem.ReadU16((c.T1 + 0x4u));
        c.V1 = (uint)(short)mem.ReadU16((c.T2 + 0x4u));
        c.V0 = c.V0 + c.V1;
        c.V1 = (uint)(short)mem.ReadU16((c.T3 + 0x4u));
        c.A0 = (uint)(short)mem.ReadU16((c.A1 + 0x4u));
        c.V0 = c.V0 + c.V1;
        c.V0 = c.V0 + c.A0;
        c.V0 = (uint)((int)c.V0 >> 13);
        if ((int)c.V0 <= 0) {
            c.V0 = c.V0 + c.S0;
            goto L800393E0;
        }
        c.V0 = c.V0 + c.S0;
        c.A0 = c.V0 & 0x0007u;
        c.V0 = c.A0 < 0x00000008u ? 1u : 0u;
        if (c.V0 == 0u) {
            c.A0 = c.A0 << 2;
            goto L800393E0;
        }
        c.A0 = c.A0 << 2;
        c.V0 = mem.ReadU32((c.T0 + 0xCu));
        c.V1 = mem.ReadU32(c.A3);
        c.V0 = c.A0 + c.V0;
        c.V0 = mem.ReadU32(c.V0);
        c.V1 = c.V1 & c.T9;
        c.V0 = c.V0 & c.T8;
        c.V1 = c.V1 | c.V0;
        mem.WriteU32(c.A3, c.V1);
        c.V0 = mem.ReadU32((c.T0 + 0xCu));
        c.A0 = c.A0 + c.V0;
        goto L800393BC;
        L80038E00: ;
        c.V1 = mem.ReadU16((c.A0 + 0x12u));
        c.V0 = mem.ReadU32((c.T0 + 0x44u));
        c.T1 = c.V1 + c.V0;
        c.V1 = mem.ReadU16((c.A0 + 0x16u));
        c.A0 = mem.ReadU16((c.A0 + 0x1Au));
        c.S6 = mem.ReadU32(c.T1);
        c.T2 = c.V1 + c.V0;
        c.T3 = c.A0 + c.V0;
        c.T4 = c.S6;
        c.S6 = mem.ReadU32(c.T2);
        c.T5 = c.S6;
        c.S6 = mem.ReadU32(c.T3);
        c.T6 = c.S6;
        RecompOne.Runtime.Gte.Write(12, c.T4);
        RecompOne.Runtime.Gte.Write(14, c.T6);
        RecompOne.Runtime.Gte.Write(13, c.T5);
        RecompOne.Runtime.Gte.Nclip();
        c.T4 = c.S5;
        { var _sw = RecompOne.Runtime.Gte.Read(24); mem.WriteU32(c.T4, _sw); }
        c.V0 = mem.ReadU32((c.T0 + 0x70u));
        if ((int)c.V0 <= 0) {
            c.V0 = c.S3;
            goto L800393E4;
        }
        c.V0 = c.S3;
        c.A3 = mem.ReadU32((c.T0 + 0x14u));
        c.V0 = mem.ReadU32((c.T0 + 0x18u));
        c.V1 = c.A3 + 0x28u;
        c.V0 = c.V0 < c.V1 ? 1u : 0u;
        if (c.V0 != 0u) {
            mem.WriteU32((c.T0 + 0x14u), c.V1);
            goto L80039400;
        }
        mem.WriteU32((c.T0 + 0x14u), c.V1);
        c.V0 = mem.ReadU32((c.T0 + 0x80u));
        c.V1 = mem.ReadU32((c.T0 + 0x20u));
        c.V0 = c.V0 + 0x1u;
        mem.WriteU32((c.T0 + 0x80u), c.V0);
        c.V0 = mem.ReadU16((c.V1 + 0x2u));
        c.V1 = mem.ReadU16((c.T0 + 0x84u));
        c.V0 = c.V0 + c.V1;
        mem.WriteU16((c.A3 + 0xEu), (ushort)c.V0);
        c.V0 = mem.ReadU32((c.T0 + 0x20u));
        c.V0 = mem.ReadU16((c.V0 + 0x6u));
        mem.WriteU16((c.A3 + 0x1Au), (ushort)c.V0);
        c.V0 = mem.ReadU32(c.T1);
        mem.WriteU32((c.A3 + 0x8u), c.V0);
        c.V0 = mem.ReadU32(c.T2);
        mem.WriteU32((c.A3 + 0x14u), c.V0);
        c.V0 = mem.ReadU32(c.T3);
        mem.WriteU32((c.A3 + 0x20u), c.V0);
        c.V0 = mem.ReadU32((c.T0 + 0x20u));
        c.V0 = mem.ReadU16(c.V0);
        mem.WriteU16((c.A3 + 0xCu), (ushort)c.V0);
        c.V0 = mem.ReadU32((c.T0 + 0x20u));
        c.V0 = mem.ReadU16((c.V0 + 0x4u));
        mem.WriteU16((c.A3 + 0x18u), (ushort)c.V0);
        c.V0 = mem.ReadU32((c.T0 + 0x20u));
        c.V0 = mem.ReadU16((c.V0 + 0x8u));
        mem.WriteU16((c.A3 + 0x24u), (ushort)c.V0);
        c.V0 = mem.ReadU32((c.T0 + 0x20u));
        c.V1 = mem.ReadU32((c.T0 + 0x28u));
        c.V0 = mem.ReadU16((c.V0 + 0xCu));
        c.V0 = c.V0 + c.V1;
        c.T4 = c.V0;
        c.V0 = mem.ReadU32((c.T0 + 0x20u));
        c.V1 = mem.ReadU32((c.T0 + 0x28u));
        c.V0 = mem.ReadU16((c.V0 + 0x10u));
        c.V0 = c.V0 + c.V1;
        c.T5 = c.V0;
        c.V0 = mem.ReadU32((c.T0 + 0x20u));
        c.V1 = mem.ReadU32((c.T0 + 0x28u));
        c.V0 = mem.ReadU16((c.V0 + 0x14u));
        c.V0 = c.V0 + c.V1;
        c.T6 = c.V0;
        { var _lw = mem.ReadU32(c.T4); RecompOne.Runtime.Gte.Write(0, _lw); }
        { var _lw = mem.ReadU32((c.T4 + 0x4u)); RecompOne.Runtime.Gte.Write(1, _lw); }
        { var _lw = mem.ReadU32(c.T5); RecompOne.Runtime.Gte.Write(2, _lw); }
        { var _lw = mem.ReadU32((c.T5 + 0x4u)); RecompOne.Runtime.Gte.Write(3, _lw); }
        { var _lw = mem.ReadU32(c.T6); RecompOne.Runtime.Gte.Write(4, _lw); }
        { var _lw = mem.ReadU32((c.T6 + 0x4u)); RecompOne.Runtime.Gte.Write(5, _lw); }
        c.T4 = c.S2;
        { var _lw = mem.ReadU32(c.T4); RecompOne.Runtime.Gte.Write(6, _lw); }
        c.V0 = (uint)(short)mem.ReadU16((c.T1 + 0x6u));
        c.V0 = (uint)((int)c.V0 >> (int)(c.A2 & 31u));
        c.T4 = c.V0;
        RecompOne.Runtime.Gte.Write(8, c.T4);
        RecompOne.Runtime.Gte.NcdtOp(12, true);
        c.V0 = c.A3 + 0x4u;
        c.T4 = c.V0;
        c.V0 = c.A3 + 0x10u;
        c.T5 = c.V0;
        c.V0 = c.A3 + 0x1Cu;
        c.T6 = c.V0;
        { var _sw = RecompOne.Runtime.Gte.Read(20); mem.WriteU32(c.T4, _sw); }
        { var _sw = RecompOne.Runtime.Gte.Read(21); mem.WriteU32(c.T5, _sw); }
        { var _sw = RecompOne.Runtime.Gte.Read(22); mem.WriteU32(c.T6, _sw); }
        c.V0 = 0x0009u;
        mem.WriteU8((c.A3 + 0x3u), (byte)c.V0);
        c.V0 = mem.ReadU8((c.T0 + 0x1Fu));
        if (c.S4 == 0u) {
            mem.WriteU8((c.A3 + 0x7u), (byte)c.V0);
            goto L8003904C;
        }
        mem.WriteU8((c.A3 + 0x7u), (byte)c.V0);
        c.V1 = (uint)(short)mem.ReadU16((c.T1 + 0x4u));
        c.V0 = (uint)(short)mem.ReadU16((c.T2 + 0x4u));
        c.A0 = (uint)(short)mem.ReadU16((c.T3 + 0x4u));
        c.V1 = c.V1 + c.V0;
        c.V1 = c.V1 + c.A0;
        { var _r = (long)(int)c.V1 * (int)c.S1; c.LO = (uint)_r; c.HI = (uint)(_r >> 32); }
        c.V1 = (uint)((int)c.V1 >> 31);
        c.V0 = c.HI;
        c.V0 = c.V0 - c.V1;
        c.V0 = (uint)((int)c.V0 >> 11);
        if ((int)c.V0 <= 0) {
            c.V0 = c.V0 + c.S0;
            goto L800393E0;
        }
        c.V0 = c.V0 + c.S0;
        c.A0 = c.V0 & 0x0007u;
        c.V0 = c.A0 < 0x00000008u ? 1u : 0u;
        if (c.V0 == 0u) {
            c.A0 = c.A0 << 2;
            goto L800393E0;
        }
        c.A0 = c.A0 << 2;
        c.V0 = mem.ReadU32((c.T0 + 0xCu));
        c.V1 = mem.ReadU32(c.A3);
        c.V0 = c.A0 + c.V0;
        c.V0 = mem.ReadU32(c.V0);
        c.V1 = c.V1 & c.T9;
        c.V0 = c.V0 & c.T8;
        c.V1 = c.V1 | c.V0;
        mem.WriteU32(c.A3, c.V1);
        c.V0 = mem.ReadU32((c.T0 + 0xCu));
        c.A0 = c.A0 + c.V0;
        goto L800393BC;
        L8003904C: ;
        c.V0 = (uint)(short)mem.ReadU16((c.T1 + 0x4u));
        c.V1 = (uint)(short)mem.ReadU16((c.T2 + 0x4u));
        c.A0 = (uint)(short)mem.ReadU16((c.T3 + 0x4u));
        c.V0 = c.V0 + c.V1;
        c.V0 = c.V0 + c.A0;
        { var _r = (long)(int)c.V0 * (int)c.S1; c.LO = (uint)_r; c.HI = (uint)(_r >> 32); }
        c.V0 = (uint)((int)c.V0 >> 31);
        c.V1 = c.HI;
        c.V0 = c.V1 - c.V0;
        if ((int)c.V0 <= 0) {
            c.V1 = c.V0 + c.S0;
            goto L800393E0;
        }
        c.V1 = c.V0 + c.S0;
        c.V0 = c.V1 < 0x00002000u ? 1u : 0u;
        if (c.V0 == 0u) {
            c.A0 = c.V1 << 2;
            goto L800393E0;
        }
        c.A0 = c.V1 << 2;
        goto L80039390;
        L8003908C: ;
        c.V1 = mem.ReadU16((c.A0 + 0x16u));
        c.V0 = mem.ReadU32((c.T0 + 0x44u));
        c.T1 = c.V1 + c.V0;
        c.V1 = mem.ReadU16((c.A0 + 0x1Au));
        c.A0 = mem.ReadU16((c.A0 + 0x1Eu));
        c.S6 = mem.ReadU32(c.T1);
        c.T2 = c.V1 + c.V0;
        c.T3 = c.A0 + c.V0;
        c.T4 = c.S6;
        c.S6 = mem.ReadU32(c.T2);
        c.T5 = c.S6;
        c.S6 = mem.ReadU32(c.T3);
        c.T6 = c.S6;
        RecompOne.Runtime.Gte.Write(12, c.T4);
        RecompOne.Runtime.Gte.Write(14, c.T6);
        RecompOne.Runtime.Gte.Write(13, c.T5);
        RecompOne.Runtime.Gte.Nclip();
        c.T4 = c.S5;
        { var _sw = RecompOne.Runtime.Gte.Read(24); mem.WriteU32(c.T4, _sw); }
        c.V0 = mem.ReadU32((c.T0 + 0x70u));
        if ((int)c.V0 <= 0) {
            c.V0 = c.S3;
            goto L800393E4;
        }
        c.V0 = c.S3;
        c.V0 = mem.ReadU32((c.T0 + 0x20u));
        c.A3 = mem.ReadU32((c.T0 + 0x14u));
        c.V1 = mem.ReadU32((c.T0 + 0x44u));
        c.A1 = mem.ReadU16((c.V0 + 0x1Eu));
        c.V0 = mem.ReadU32((c.T0 + 0x18u));
        c.A0 = c.A3 + 0x34u;
        mem.WriteU32((c.T0 + 0x14u), c.A0);
        c.V0 = c.V0 < c.A0 ? 1u : 0u;
        if (c.V0 != 0u) {
            c.A1 = c.A1 + c.V1;
            goto L80039400;
        }
        c.A1 = c.A1 + c.V1;
        c.V0 = mem.ReadU32((c.T0 + 0x80u));
        c.V1 = mem.ReadU32((c.T0 + 0x20u));
        c.V0 = c.V0 + 0x1u;
        mem.WriteU32((c.T0 + 0x80u), c.V0);
        c.V0 = mem.ReadU16((c.V1 + 0x2u));
        c.V1 = mem.ReadU16((c.T0 + 0x84u));
        c.V0 = c.V0 + c.V1;
        mem.WriteU16((c.A3 + 0xEu), (ushort)c.V0);
        c.V0 = mem.ReadU32((c.T0 + 0x20u));
        c.V0 = mem.ReadU16((c.V0 + 0x6u));
        mem.WriteU16((c.A3 + 0x1Au), (ushort)c.V0);
        c.V0 = mem.ReadU32(c.T1);
        mem.WriteU32((c.A3 + 0x8u), c.V0);
        c.V0 = mem.ReadU32(c.T2);
        mem.WriteU32((c.A3 + 0x14u), c.V0);
        c.V0 = mem.ReadU32(c.T3);
        mem.WriteU32((c.A3 + 0x20u), c.V0);
        c.V0 = mem.ReadU32(c.A1);
        mem.WriteU32((c.A3 + 0x2Cu), c.V0);
        c.V0 = mem.ReadU32((c.T0 + 0x20u));
        c.V0 = mem.ReadU16(c.V0);
        mem.WriteU16((c.A3 + 0xCu), (ushort)c.V0);
        c.V0 = mem.ReadU32((c.T0 + 0x20u));
        c.V0 = mem.ReadU16((c.V0 + 0x4u));
        mem.WriteU16((c.A3 + 0x18u), (ushort)c.V0);
        c.V0 = mem.ReadU32((c.T0 + 0x20u));
        c.V0 = mem.ReadU16((c.V0 + 0x8u));
        mem.WriteU16((c.A3 + 0x24u), (ushort)c.V0);
        c.V0 = mem.ReadU32((c.T0 + 0x20u));
        c.V0 = mem.ReadU16((c.V0 + 0xCu));
        mem.WriteU16((c.A3 + 0x30u), (ushort)c.V0);
        c.V0 = mem.ReadU32((c.T0 + 0x20u));
        c.V1 = mem.ReadU32((c.T0 + 0x28u));
        c.V0 = mem.ReadU16((c.V0 + 0x10u));
        c.V0 = c.V0 + c.V1;
        c.T4 = c.V0;
        c.V0 = mem.ReadU32((c.T0 + 0x20u));
        c.V1 = mem.ReadU32((c.T0 + 0x28u));
        c.V0 = mem.ReadU16((c.V0 + 0x14u));
        c.V0 = c.V0 + c.V1;
        c.T5 = c.V0;
        c.V0 = mem.ReadU32((c.T0 + 0x20u));
        c.V1 = mem.ReadU32((c.T0 + 0x28u));
        c.V0 = mem.ReadU16((c.V0 + 0x18u));
        c.V0 = c.V0 + c.V1;
        c.T6 = c.V0;
        { var _lw = mem.ReadU32(c.T4); RecompOne.Runtime.Gte.Write(0, _lw); }
        { var _lw = mem.ReadU32((c.T4 + 0x4u)); RecompOne.Runtime.Gte.Write(1, _lw); }
        { var _lw = mem.ReadU32(c.T5); RecompOne.Runtime.Gte.Write(2, _lw); }
        { var _lw = mem.ReadU32((c.T5 + 0x4u)); RecompOne.Runtime.Gte.Write(3, _lw); }
        { var _lw = mem.ReadU32(c.T6); RecompOne.Runtime.Gte.Write(4, _lw); }
        { var _lw = mem.ReadU32((c.T6 + 0x4u)); RecompOne.Runtime.Gte.Write(5, _lw); }
        c.T4 = c.S2;
        { var _lw = mem.ReadU32(c.T4); RecompOne.Runtime.Gte.Write(6, _lw); }
        c.V0 = (uint)(short)mem.ReadU16((c.T1 + 0x6u));
        c.V0 = (uint)((int)c.V0 >> (int)(c.A2 & 31u));
        c.T4 = c.V0;
        RecompOne.Runtime.Gte.Write(8, c.T4);
        RecompOne.Runtime.Gte.NcdtOp(12, true);
        c.V0 = c.A3 + 0x4u;
        c.T4 = c.V0;
        c.V0 = c.A3 + 0x10u;
        c.T5 = c.V0;
        c.V0 = c.A3 + 0x1Cu;
        c.T6 = c.V0;
        { var _sw = RecompOne.Runtime.Gte.Read(20); mem.WriteU32(c.T4, _sw); }
        { var _sw = RecompOne.Runtime.Gte.Read(21); mem.WriteU32(c.T5, _sw); }
        { var _sw = RecompOne.Runtime.Gte.Read(22); mem.WriteU32(c.T6, _sw); }
        c.V0 = mem.ReadU32((c.T0 + 0x20u));
        c.V1 = mem.ReadU32((c.T0 + 0x28u));
        c.V0 = mem.ReadU16((c.V0 + 0x1Cu));
        c.V0 = c.V0 + c.V1;
        c.T4 = c.V0;
        { var _lw = mem.ReadU32(c.T4); RecompOne.Runtime.Gte.Write(0, _lw); }
        { var _lw = mem.ReadU32((c.T4 + 0x4u)); RecompOne.Runtime.Gte.Write(1, _lw); }
        c.T4 = c.S2;
        { var _lw = mem.ReadU32(c.T4); RecompOne.Runtime.Gte.Write(6, _lw); }
        c.V0 = (uint)(short)mem.ReadU16((c.T1 + 0x6u));
        c.V0 = (uint)((int)c.V0 >> (int)(c.A2 & 31u));
        c.T4 = c.V0;
        RecompOne.Runtime.Gte.Write(8, c.T4);
        RecompOne.Runtime.Gte.NcdsOp(12, true);
        c.V0 = c.A3 + 0x28u;
        c.T4 = c.V0;
        { var _sw = RecompOne.Runtime.Gte.Read(22); mem.WriteU32(c.T4, _sw); }
        c.V0 = 0x000Cu;
        mem.WriteU8((c.A3 + 0x3u), (byte)c.V0);
        c.V0 = mem.ReadU8((c.T0 + 0x1Fu));
        if (c.S4 == 0u) {
            mem.WriteU8((c.A3 + 0x7u), (byte)c.V0);
            goto L80039358;
        }
        mem.WriteU8((c.A3 + 0x7u), (byte)c.V0);
        c.V0 = (uint)(short)mem.ReadU16((c.T1 + 0x4u));
        c.V1 = (uint)(short)mem.ReadU16((c.T2 + 0x4u));
        c.V0 = c.V0 + c.V1;
        c.V1 = (uint)(short)mem.ReadU16((c.T3 + 0x4u));
        c.A0 = (uint)(short)mem.ReadU16((c.A1 + 0x4u));
        c.V0 = c.V0 + c.V1;
        c.V0 = c.V0 + c.A0;
        c.V0 = (uint)((int)c.V0 >> 13);
        if ((int)c.V0 <= 0) {
            c.V0 = c.V0 + c.S0;
            goto L800393E0;
        }
        c.V0 = c.V0 + c.S0;
        c.A0 = c.V0 & 0x0007u;
        c.V0 = c.A0 < 0x00000008u ? 1u : 0u;
        if (c.V0 == 0u) {
            c.A0 = c.A0 << 2;
            goto L800393E0;
        }
        c.A0 = c.A0 << 2;
        c.V0 = mem.ReadU32((c.T0 + 0xCu));
        c.V1 = mem.ReadU32(c.A3);
        c.V0 = c.A0 + c.V0;
        c.V0 = mem.ReadU32(c.V0);
        c.V1 = c.V1 & c.T9;
        c.V0 = c.V0 & c.T8;
        c.V1 = c.V1 | c.V0;
        mem.WriteU32(c.A3, c.V1);
        c.V0 = mem.ReadU32((c.T0 + 0xCu));
        c.A0 = c.A0 + c.V0;
        goto L800393BC;
        L80039358: ;
        c.V0 = (uint)(short)mem.ReadU16((c.T1 + 0x4u));
        c.V1 = (uint)(short)mem.ReadU16((c.T2 + 0x4u));
        c.V0 = c.V0 + c.V1;
        c.V1 = (uint)(short)mem.ReadU16((c.T3 + 0x4u));
        c.A0 = (uint)(short)mem.ReadU16((c.A1 + 0x4u));
        c.V0 = c.V0 + c.V1;
        c.V0 = c.V0 + c.A0;
        c.V0 = (uint)((int)c.V0 >> 2);
        if ((int)c.V0 <= 0) {
            c.A0 = c.V0 + c.S0;
            goto L800393E0;
        }
        c.A0 = c.V0 + c.S0;
        c.V0 = c.A0 < 0x00002000u ? 1u : 0u;
        if (c.V0 == 0u) {
            c.A0 = c.A0 << 2;
            goto L800393E0;
        }
        c.A0 = c.A0 << 2;
        L80039390: ;
        c.V0 = mem.ReadU32((c.T0 + 0x8u));
        c.V1 = mem.ReadU32(c.A3);
        c.V0 = c.A0 + c.V0;
        c.V0 = mem.ReadU32(c.V0);
        c.V1 = c.V1 & c.T9;
        c.V0 = c.V0 & c.T8;
        c.V1 = c.V1 | c.V0;
        mem.WriteU32(c.A3, c.V1);
        c.V0 = mem.ReadU32((c.T0 + 0x8u));
        c.A0 = c.A0 + c.V0;
        L800393BC: ;
        c.V0 = mem.ReadU32(c.A0);
        c.V1 = c.A3 & c.T8;
        c.V0 = c.V0 & c.T9;
        c.V0 = c.V0 | c.V1;
        mem.WriteU32(c.A0, c.V0);
        c.V0 = mem.ReadU32((c.T0 + 0x7Cu));
        c.V0 = c.V0 + 0x1u;
        mem.WriteU32((c.T0 + 0x7Cu), c.V0);
        L800393E0: ;
        c.V0 = c.S3;
        L800393E4: ;
        c.S3 = c.S3 - 0x1u;
        c.V1 = mem.ReadU8((c.T0 + 0x1Du));
        c.A0 = mem.ReadU32((c.T0 + 0x20u));
        c.V1 = c.V1 << 2;
        c.V1 = c.V1 + c.A0;
        if (c.V0 != 0u) {
            mem.WriteU32((c.T0 + 0x20u), c.V1);
            goto L80038910;
        }
        mem.WriteU32((c.T0 + 0x20u), c.V1);
        L80039400: ;
        c.S6 = mem.ReadU32((c.SP + 0x18u));
        c.S5 = mem.ReadU32((c.SP + 0x14u));
        c.S4 = mem.ReadU32((c.SP + 0x10u));
        c.S3 = mem.ReadU32((c.SP + 0xCu));
        c.S2 = mem.ReadU32((c.SP + 0x8u));
        c.S1 = mem.ReadU32((c.SP + 0x4u));
        c.S0 = mem.ReadU32(c.SP);
        c.SP = c.SP + 0x20u;
        return;
    }
}
