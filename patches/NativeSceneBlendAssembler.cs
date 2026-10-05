using RecompOne.Runtime;
using RecompOne.Runtime.Dispatch;
using RecompOne.Runtime.Context;
using RecompOne.Runtime.Memory;
using Game = Recompiled.KingsField3_game;

namespace Kf3;

public static partial class NativeScene
{
    // Literal reference for func_80037BEC; see docs/GPU_RENDERER.md.
    static void RunBlendAssembler(CpuContext c, PSMemory mem)
    {

        c.SP = c.SP - 0x8u;
        c.A0 = c.A0 & 0xFFFFu;
        c.V1 = c.A0 << 3;
        c.V1 = c.V1 - c.A0;
        c.V1 = c.V1 << 2;
        c.A0 = 0x1F800000u;
        c.A0 = mem.ReadU32((c.A0 + 0x10u));
        c.V1 = c.V1 + 0xCu;
        mem.WriteU32(c.SP, c.S0);
        c.V1 = c.V1 + c.A0;
        c.At = 0x1F800000u;
        mem.WriteU32((c.At + 0x24u), c.V1);
        c.V0 = mem.ReadU32((c.V1 + 0x8u));
        c.T9 = c.A1;
        c.V0 = c.V0 + 0xCu;
        c.V0 = c.V0 + c.A0;
        c.At = 0x1F800000u;
        mem.WriteU32((c.At + 0x28u), c.V0);
        c.V0 = mem.ReadU32((c.V1 + 0x10u));
        c.A2 = c.A2 << 5;
        c.V0 = c.V0 + 0xCu;
        c.V0 = c.V0 + c.A0;
        c.At = 0x1F800000u;
        mem.WriteU32((c.At + 0x20u), c.V0);
        c.T3 = mem.ReadU32((c.V1 + 0x14u));
        c.V0 = 0x1F800000u;
        c.V0 = mem.ReadU32((c.V0 + 0x78u));
        c.A3 = 0x1F800000u;
        c.V0 = c.T3 + c.V0;
        c.V1 = c.T3;
        c.At = 0x1F800000u;
        mem.WriteU32((c.At + 0x78u), c.V0);
        if (c.V1 == 0u) {
            c.T3 = c.T3 - 0x1u;
            goto L80038834;
        }
        c.T3 = c.T3 - 0x1u;
        c.T8 = 0x1F800000u;
        c.T8 = c.T8 | 0x0070u;
        c.T2 = 0x1F800000u;
        c.T2 = c.T2 | 0x0064u;
        c.S0 = 0x55550000u;
        c.S0 = c.S0 | 0x5556u;
        c.T0 = 0x00FF0000u;
        c.T0 = c.T0 | 0xFFFFu;
        c.T1 = 0xFF000000u;
        L80037C98: ;
        RecompOne.Runtime.Interrupts.Poll(c, mem);
        c.V0 = mem.ReadU32((c.A3 + 0x20u));
        c.A0 = mem.ReadU32((c.A3 + 0x20u));
        c.V0 = mem.ReadU32(c.V0);
        mem.WriteU32((c.A3 + 0x1Cu), c.V0);
        c.V1 = mem.ReadU8((c.A3 + 0x1Fu));
        c.V0 = c.A0 + 0x4u;
        mem.WriteU32((c.A3 + 0x20u), c.V0);
        c.V0 = 0x0034u;
        c.V1 = c.V1 & 0x00FDu;
        if (c.V1 == c.V0) {
            c.V0 = (int)c.V1 < 53 ? 1u : 0u;
            goto L800381C4;
        }
        c.V0 = (int)c.V1 < 53 ? 1u : 0u;
        if (c.V0 == 0u) {
            c.V0 = 0x0024u;
            goto L80037CE8;
        }
        c.V0 = 0x0024u;
        if (c.V1 == c.V0) {
            c.V0 = 0x002Cu;
            goto L80037CFC;
        }
        c.V0 = 0x002Cu;
        if (c.V1 == c.V0) {
            c.V0 = c.T3;
            goto L80037F6C;
        }
        c.V0 = c.T3;
        goto L80038818;
        L80037CE8: ;
        c.V0 = 0x003Cu;
        if (c.V1 == c.V0) {
            c.V0 = c.T3;
            goto L80038478;
        }
        c.V0 = c.T3;
        goto L80038818;
        L80037CFC: ;
        c.V0 = mem.ReadU16((c.A0 + 0x12u));
        c.V1 = mem.ReadU32((c.A3 + 0x44u));
        c.A0 = mem.ReadU32((c.A3 + 0x20u));
        c.V0 = c.V0 + c.V1;
        mem.WriteU32((c.A3 + 0x30u), c.V0);
        c.V0 = mem.ReadU16((c.A0 + 0x10u));
        c.V1 = mem.ReadU32((c.A3 + 0x44u));
        c.A0 = mem.ReadU32((c.A3 + 0x20u));
        c.V0 = c.V0 + c.V1;
        mem.WriteU32((c.A3 + 0x34u), c.V0);
        c.V0 = mem.ReadU16((c.A0 + 0x12u));
        c.V1 = mem.ReadU32((c.A3 + 0x44u));
        c.V0 = c.V0 + c.V1;
        mem.WriteU32((c.A3 + 0x38u), c.V0);
        c.V0 = mem.ReadU32((c.A3 + 0x30u));
        c.V0 = mem.ReadU32(c.V0);
        c.T4 = c.V0;
        c.V0 = mem.ReadU32((c.A3 + 0x34u));
        c.V0 = mem.ReadU32(c.V0);
        c.T5 = c.V0;
        c.V0 = mem.ReadU32((c.A3 + 0x38u));
        c.V0 = mem.ReadU32(c.V0);
        c.T6 = c.V0;
        RecompOne.Runtime.Gte.Write(12, c.T4);
        RecompOne.Runtime.Gte.Write(14, c.T6);
        RecompOne.Runtime.Gte.Write(13, c.T5);
        RecompOne.Runtime.Gte.Nclip();
        c.T4 = c.T8;
        { var _sw = RecompOne.Runtime.Gte.Read(24); mem.WriteU32(c.T4, _sw); }
        c.V0 = mem.ReadU32((c.A3 + 0x70u));
        if ((int)c.V0 <= 0) {
            c.V0 = c.T3;
            goto L80038818;
        }
        c.V0 = c.T3;
        c.V0 = mem.ReadU32((c.A3 + 0x14u));
        mem.WriteU32((c.A3 + 0x2Cu), c.V0);
        c.V0 = mem.ReadU32((c.A3 + 0x14u));
        c.V1 = mem.ReadU32((c.A3 + 0x18u));
        c.V0 = c.V0 + 0x20u;
        c.V1 = c.V1 < c.V0 ? 1u : 0u;
        if (c.V1 != 0u) {
            mem.WriteU32((c.A3 + 0x14u), c.V0);
            goto L80038834;
        }
        mem.WriteU32((c.A3 + 0x14u), c.V0);
        c.V0 = mem.ReadU32((c.A3 + 0x80u));
        c.V1 = mem.ReadU32((c.A3 + 0x20u));
        c.V0 = c.V0 + 0x1u;
        mem.WriteU32((c.A3 + 0x80u), c.V0);
        c.V0 = mem.ReadU16((c.V1 + 0x2u));
        c.V1 = mem.ReadU16((c.A3 + 0x84u));
        c.A0 = mem.ReadU32((c.A3 + 0x2Cu));
        c.V0 = c.V0 + c.V1;
        mem.WriteU16((c.A0 + 0xEu), (ushort)c.V0);
        c.V0 = mem.ReadU32((c.A3 + 0x20u));
        c.V0 = mem.ReadU16((c.V0 + 0x6u));
        c.V1 = mem.ReadU32((c.A3 + 0x2Cu));
        c.V0 = c.V0 & 0xFF9Fu;
        c.V0 = c.A2 | c.V0;
        mem.WriteU16((c.V1 + 0x16u), (ushort)c.V0);
        c.V0 = mem.ReadU32((c.A3 + 0x30u));
        c.V1 = mem.ReadU32((c.A3 + 0x2Cu));
        c.V0 = mem.ReadU32(c.V0);
        mem.WriteU32((c.V1 + 0x8u), c.V0);
        c.V0 = mem.ReadU32((c.A3 + 0x34u));
        c.V1 = mem.ReadU32((c.A3 + 0x2Cu));
        c.V0 = mem.ReadU32(c.V0);
        mem.WriteU32((c.V1 + 0x10u), c.V0);
        c.V0 = mem.ReadU32((c.A3 + 0x38u));
        c.V1 = mem.ReadU32((c.A3 + 0x2Cu));
        c.V0 = mem.ReadU32(c.V0);
        mem.WriteU32((c.V1 + 0x18u), c.V0);
        c.V0 = mem.ReadU32((c.A3 + 0x20u));
        c.V1 = mem.ReadU32((c.A3 + 0x2Cu));
        c.V0 = mem.ReadU16(c.V0);
        mem.WriteU16((c.V1 + 0xCu), (ushort)c.V0);
        c.V0 = mem.ReadU32((c.A3 + 0x20u));
        c.V1 = mem.ReadU32((c.A3 + 0x2Cu));
        c.V0 = mem.ReadU16((c.V0 + 0x4u));
        mem.WriteU16((c.V1 + 0x14u), (ushort)c.V0);
        c.V0 = mem.ReadU32((c.A3 + 0x20u));
        c.V1 = mem.ReadU32((c.A3 + 0x2Cu));
        c.V0 = mem.ReadU16((c.V0 + 0x8u));
        mem.WriteU16((c.V1 + 0x1Cu), (ushort)c.V0);
        c.V0 = mem.ReadU32((c.A3 + 0x20u));
        c.V1 = mem.ReadU32((c.A3 + 0x28u));
        c.V0 = mem.ReadU16((c.V0 + 0xCu));
        c.V0 = c.V0 + c.V1;
        c.T4 = c.V0;
        { var _lw = mem.ReadU32(c.T4); RecompOne.Runtime.Gte.Write(0, _lw); }
        { var _lw = mem.ReadU32((c.T4 + 0x4u)); RecompOne.Runtime.Gte.Write(1, _lw); }
        c.T4 = c.T2;
        { var _lw = mem.ReadU32(c.T4); RecompOne.Runtime.Gte.Write(6, _lw); }
        c.V0 = mem.ReadU32((c.A3 + 0x30u));
        c.V1 = (uint)(short)mem.ReadU16((c.V0 + 0x6u));
        c.V0 = mem.ReadU32((c.A3 + 0x34u));
        c.A0 = mem.ReadU32((c.A3 + 0x38u));
        c.V0 = (uint)(short)mem.ReadU16((c.V0 + 0x6u));
        c.A0 = (uint)(short)mem.ReadU16((c.A0 + 0x6u));
        c.V1 = c.V1 + c.V0;
        c.V1 = c.V1 + c.A0;
        { var _r = (long)(int)c.V1 * (int)c.S0; c.LO = (uint)_r; c.HI = (uint)(_r >> 32); }
        c.V1 = (uint)((int)c.V1 >> 31);
        c.V0 = c.HI;
        c.V0 = c.V0 - c.V1;
        c.T4 = c.V0;
        RecompOne.Runtime.Gte.Write(8, c.T4);
        RecompOne.Runtime.Gte.NcdsOp(12, true);
        c.V0 = mem.ReadU32((c.A3 + 0x2Cu));
        c.V0 = c.V0 + 0x4u;
        c.T4 = c.V0;
        { var _sw = RecompOne.Runtime.Gte.Read(22); mem.WriteU32(c.T4, _sw); }
        c.V1 = mem.ReadU32((c.A3 + 0x2Cu));
        c.V0 = 0x0007u;
        mem.WriteU8((c.V1 + 0x3u), (byte)c.V0);
        c.V1 = mem.ReadU32((c.A3 + 0x2Cu));
        c.V0 = 0x0026u;
        mem.WriteU8((c.V1 + 0x7u), (byte)c.V0);
        c.V0 = mem.ReadU32((c.A3 + 0x30u));
        c.V1 = (uint)(short)mem.ReadU16((c.V0 + 0x4u));
        c.V0 = mem.ReadU32((c.A3 + 0x34u));
        c.A0 = mem.ReadU32((c.A3 + 0x38u));
        c.V0 = (uint)(short)mem.ReadU16((c.V0 + 0x4u));
        c.A0 = (uint)(short)mem.ReadU16((c.A0 + 0x4u));
        c.V1 = c.V1 + c.V0;
        c.V1 = c.V1 + c.A0;
        { var _r = (long)(int)c.V1 * (int)c.S0; c.LO = (uint)_r; c.HI = (uint)(_r >> 32); }
        c.V1 = (uint)((int)c.V1 >> 31);
        c.V0 = c.HI;
        c.V0 = c.V0 - c.V1;
        if ((int)c.V0 <= 0) {
            c.A0 = c.V0 + c.T9;
            goto L80038814;
        }
        c.A0 = c.V0 + c.T9;
        c.V0 = c.A0 < 0x00002000u ? 1u : 0u;
        if (c.V0 == 0u) {
            c.A0 = c.A0 << 2;
            goto L80038814;
        }
        c.A0 = c.A0 << 2;
        goto L800387BC;
        L80037F6C: ;
        c.V0 = mem.ReadU16((c.A0 + 0x16u));
        c.V1 = mem.ReadU32((c.A3 + 0x44u));
        c.A0 = mem.ReadU32((c.A3 + 0x20u));
        c.V0 = c.V0 + c.V1;
        mem.WriteU32((c.A3 + 0x30u), c.V0);
        c.V0 = mem.ReadU16((c.A0 + 0x14u));
        c.V1 = mem.ReadU32((c.A3 + 0x44u));
        c.A0 = mem.ReadU32((c.A3 + 0x20u));
        c.V0 = c.V0 + c.V1;
        mem.WriteU32((c.A3 + 0x34u), c.V0);
        c.V0 = mem.ReadU16((c.A0 + 0x16u));
        c.V1 = mem.ReadU32((c.A3 + 0x44u));
        c.V0 = c.V0 + c.V1;
        mem.WriteU32((c.A3 + 0x38u), c.V0);
        c.V0 = mem.ReadU32((c.A3 + 0x30u));
        c.V0 = mem.ReadU32(c.V0);
        c.T4 = c.V0;
        c.V0 = mem.ReadU32((c.A3 + 0x34u));
        c.V0 = mem.ReadU32(c.V0);
        c.T5 = c.V0;
        c.V0 = mem.ReadU32((c.A3 + 0x38u));
        c.V0 = mem.ReadU32(c.V0);
        c.T6 = c.V0;
        RecompOne.Runtime.Gte.Write(12, c.T4);
        RecompOne.Runtime.Gte.Write(14, c.T6);
        RecompOne.Runtime.Gte.Write(13, c.T5);
        RecompOne.Runtime.Gte.Nclip();
        c.T4 = c.T8;
        { var _sw = RecompOne.Runtime.Gte.Read(24); mem.WriteU32(c.T4, _sw); }
        c.V0 = mem.ReadU32((c.A3 + 0x70u));
        if ((int)c.V0 <= 0) {
            c.V0 = c.T3;
            goto L80038818;
        }
        c.V0 = c.T3;
        c.V0 = mem.ReadU32((c.A3 + 0x20u));
        c.V1 = mem.ReadU32((c.A3 + 0x14u));
        c.A0 = mem.ReadU32((c.A3 + 0x14u));
        c.A1 = mem.ReadU16((c.V0 + 0x18u));
        mem.WriteU32((c.A3 + 0x2Cu), c.V1);
        c.V1 = mem.ReadU32((c.A3 + 0x44u));
        c.V0 = mem.ReadU32((c.A3 + 0x18u));
        c.A0 = c.A0 + 0x28u;
        mem.WriteU32((c.A3 + 0x14u), c.A0);
        c.A1 = c.A1 + c.V1;
        c.V0 = c.V0 < c.A0 ? 1u : 0u;
        if (c.V0 != 0u) {
            mem.WriteU32((c.A3 + 0x3Cu), c.A1);
            goto L80038834;
        }
        mem.WriteU32((c.A3 + 0x3Cu), c.A1);
        c.V0 = mem.ReadU32((c.A3 + 0x80u));
        c.V1 = mem.ReadU32((c.A3 + 0x20u));
        c.V0 = c.V0 + 0x1u;
        mem.WriteU32((c.A3 + 0x80u), c.V0);
        c.V0 = mem.ReadU16((c.V1 + 0x2u));
        c.V1 = mem.ReadU16((c.A3 + 0x84u));
        c.A0 = mem.ReadU32((c.A3 + 0x2Cu));
        c.V0 = c.V0 + c.V1;
        mem.WriteU16((c.A0 + 0xEu), (ushort)c.V0);
        c.V0 = mem.ReadU32((c.A3 + 0x20u));
        c.V0 = mem.ReadU16((c.V0 + 0x6u));
        c.V1 = mem.ReadU32((c.A3 + 0x2Cu));
        c.V0 = c.V0 & 0xFF9Fu;
        c.V0 = c.A2 | c.V0;
        mem.WriteU16((c.V1 + 0x16u), (ushort)c.V0);
        c.V0 = mem.ReadU32((c.A3 + 0x30u));
        c.V1 = mem.ReadU32((c.A3 + 0x2Cu));
        c.V0 = mem.ReadU32(c.V0);
        mem.WriteU32((c.V1 + 0x8u), c.V0);
        c.V0 = mem.ReadU32((c.A3 + 0x34u));
        c.V1 = mem.ReadU32((c.A3 + 0x2Cu));
        c.V0 = mem.ReadU32(c.V0);
        mem.WriteU32((c.V1 + 0x10u), c.V0);
        c.V0 = mem.ReadU32((c.A3 + 0x38u));
        c.V1 = mem.ReadU32((c.A3 + 0x2Cu));
        c.V0 = mem.ReadU32(c.V0);
        mem.WriteU32((c.V1 + 0x18u), c.V0);
        c.V0 = mem.ReadU32((c.A3 + 0x3Cu));
        c.V1 = mem.ReadU32((c.A3 + 0x2Cu));
        c.V0 = mem.ReadU32(c.V0);
        mem.WriteU32((c.V1 + 0x20u), c.V0);
        c.V0 = mem.ReadU32((c.A3 + 0x20u));
        c.V1 = mem.ReadU32((c.A3 + 0x2Cu));
        c.V0 = mem.ReadU16(c.V0);
        mem.WriteU16((c.V1 + 0xCu), (ushort)c.V0);
        c.V0 = mem.ReadU32((c.A3 + 0x20u));
        c.V1 = mem.ReadU32((c.A3 + 0x2Cu));
        c.V0 = mem.ReadU16((c.V0 + 0x4u));
        mem.WriteU16((c.V1 + 0x14u), (ushort)c.V0);
        c.V0 = mem.ReadU32((c.A3 + 0x20u));
        c.V1 = mem.ReadU32((c.A3 + 0x2Cu));
        c.V0 = mem.ReadU16((c.V0 + 0x8u));
        mem.WriteU16((c.V1 + 0x1Cu), (ushort)c.V0);
        c.V0 = mem.ReadU32((c.A3 + 0x20u));
        c.V1 = mem.ReadU32((c.A3 + 0x2Cu));
        c.V0 = mem.ReadU16((c.V0 + 0xCu));
        mem.WriteU16((c.V1 + 0x24u), (ushort)c.V0);
        c.V0 = mem.ReadU32((c.A3 + 0x20u));
        c.V1 = mem.ReadU32((c.A3 + 0x28u));
        c.V0 = mem.ReadU16((c.V0 + 0x10u));
        c.V0 = c.V0 + c.V1;
        c.T4 = c.V0;
        { var _lw = mem.ReadU32(c.T4); RecompOne.Runtime.Gte.Write(0, _lw); }
        { var _lw = mem.ReadU32((c.T4 + 0x4u)); RecompOne.Runtime.Gte.Write(1, _lw); }
        c.T4 = c.T2;
        { var _lw = mem.ReadU32(c.T4); RecompOne.Runtime.Gte.Write(6, _lw); }
        c.V0 = mem.ReadU32((c.A3 + 0x30u));
        c.V1 = mem.ReadU32((c.A3 + 0x34u));
        c.V0 = (uint)(short)mem.ReadU16((c.V0 + 0x6u));
        c.V1 = (uint)(short)mem.ReadU16((c.V1 + 0x6u));
        c.V0 = c.V0 + c.V1;
        c.V1 = mem.ReadU32((c.A3 + 0x38u));
        c.A0 = mem.ReadU32((c.A3 + 0x3Cu));
        c.V1 = (uint)(short)mem.ReadU16((c.V1 + 0x6u));
        c.A0 = (uint)(short)mem.ReadU16((c.A0 + 0x6u));
        c.V0 = c.V0 + c.V1;
        c.V0 = c.V0 + c.A0;
        c.V0 = (uint)((int)c.V0 >> 2);
        c.T4 = c.V0;
        RecompOne.Runtime.Gte.Write(8, c.T4);
        RecompOne.Runtime.Gte.NcdsOp(12, true);
        c.V0 = mem.ReadU32((c.A3 + 0x2Cu));
        c.V0 = c.V0 + 0x4u;
        c.T4 = c.V0;
        { var _sw = RecompOne.Runtime.Gte.Read(22); mem.WriteU32(c.T4, _sw); }
        c.V1 = mem.ReadU32((c.A3 + 0x2Cu));
        c.V0 = 0x0009u;
        mem.WriteU8((c.V1 + 0x3u), (byte)c.V0);
        c.V1 = mem.ReadU32((c.A3 + 0x2Cu));
        c.V0 = 0x002Eu;
        goto L80038770;
        L800381C4: ;
        c.V0 = mem.ReadU16((c.A0 + 0x12u));
        c.V1 = mem.ReadU32((c.A3 + 0x44u));
        c.A0 = mem.ReadU32((c.A3 + 0x20u));
        c.V0 = c.V0 + c.V1;
        mem.WriteU32((c.A3 + 0x30u), c.V0);
        c.V0 = mem.ReadU16((c.A0 + 0x12u));
        c.V1 = mem.ReadU32((c.A3 + 0x44u));
        c.A0 = mem.ReadU32((c.A3 + 0x20u));
        c.V0 = c.V0 + c.V1;
        mem.WriteU32((c.A3 + 0x34u), c.V0);
        c.V0 = mem.ReadU16((c.A0 + 0x16u));
        c.V1 = mem.ReadU32((c.A3 + 0x44u));
        c.V0 = c.V0 + c.V1;
        mem.WriteU32((c.A3 + 0x38u), c.V0);
        c.V0 = mem.ReadU32((c.A3 + 0x30u));
        c.V0 = mem.ReadU32(c.V0);
        c.T4 = c.V0;
        c.V0 = mem.ReadU32((c.A3 + 0x34u));
        c.V0 = mem.ReadU32(c.V0);
        c.T5 = c.V0;
        c.V0 = mem.ReadU32((c.A3 + 0x38u));
        c.V0 = mem.ReadU32(c.V0);
        c.T6 = c.V0;
        RecompOne.Runtime.Gte.Write(12, c.T4);
        RecompOne.Runtime.Gte.Write(14, c.T6);
        RecompOne.Runtime.Gte.Write(13, c.T5);
        RecompOne.Runtime.Gte.Nclip();
        c.T4 = c.T8;
        { var _sw = RecompOne.Runtime.Gte.Read(24); mem.WriteU32(c.T4, _sw); }
        c.V0 = mem.ReadU32((c.A3 + 0x70u));
        if ((int)c.V0 <= 0) {
            c.V0 = c.T3;
            goto L80038818;
        }
        c.V0 = c.T3;
        c.V0 = mem.ReadU32((c.A3 + 0x14u));
        mem.WriteU32((c.A3 + 0x2Cu), c.V0);
        c.V0 = mem.ReadU32((c.A3 + 0x14u));
        c.V1 = mem.ReadU32((c.A3 + 0x18u));
        c.V0 = c.V0 + 0x28u;
        c.V1 = c.V1 < c.V0 ? 1u : 0u;
        if (c.V1 != 0u) {
            mem.WriteU32((c.A3 + 0x14u), c.V0);
            goto L80038834;
        }
        mem.WriteU32((c.A3 + 0x14u), c.V0);
        c.V0 = mem.ReadU32((c.A3 + 0x80u));
        c.V1 = mem.ReadU32((c.A3 + 0x20u));
        c.V0 = c.V0 + 0x1u;
        mem.WriteU32((c.A3 + 0x80u), c.V0);
        c.V0 = mem.ReadU16((c.V1 + 0x2u));
        c.V1 = mem.ReadU16((c.A3 + 0x84u));
        c.A0 = mem.ReadU32((c.A3 + 0x2Cu));
        c.V0 = c.V0 + c.V1;
        mem.WriteU16((c.A0 + 0xEu), (ushort)c.V0);
        c.V0 = mem.ReadU32((c.A3 + 0x20u));
        c.V0 = mem.ReadU16((c.V0 + 0x6u));
        c.V1 = mem.ReadU32((c.A3 + 0x2Cu));
        c.V0 = c.V0 & 0xFF9Fu;
        c.V0 = c.A2 | c.V0;
        mem.WriteU16((c.V1 + 0x1Au), (ushort)c.V0);
        c.V0 = mem.ReadU32((c.A3 + 0x30u));
        c.V1 = mem.ReadU32((c.A3 + 0x2Cu));
        c.V0 = mem.ReadU32(c.V0);
        mem.WriteU32((c.V1 + 0x8u), c.V0);
        c.V0 = mem.ReadU32((c.A3 + 0x34u));
        c.V1 = mem.ReadU32((c.A3 + 0x2Cu));
        c.V0 = mem.ReadU32(c.V0);
        mem.WriteU32((c.V1 + 0x14u), c.V0);
        c.V0 = mem.ReadU32((c.A3 + 0x38u));
        c.V1 = mem.ReadU32((c.A3 + 0x2Cu));
        c.V0 = mem.ReadU32(c.V0);
        mem.WriteU32((c.V1 + 0x20u), c.V0);
        c.V0 = mem.ReadU32((c.A3 + 0x20u));
        c.V1 = mem.ReadU32((c.A3 + 0x2Cu));
        c.V0 = mem.ReadU16(c.V0);
        mem.WriteU16((c.V1 + 0xCu), (ushort)c.V0);
        c.V0 = mem.ReadU32((c.A3 + 0x20u));
        c.V1 = mem.ReadU32((c.A3 + 0x2Cu));
        c.V0 = mem.ReadU16((c.V0 + 0x4u));
        mem.WriteU16((c.V1 + 0x18u), (ushort)c.V0);
        c.V0 = mem.ReadU32((c.A3 + 0x20u));
        c.V1 = mem.ReadU32((c.A3 + 0x2Cu));
        c.V0 = mem.ReadU16((c.V0 + 0x8u));
        mem.WriteU16((c.V1 + 0x24u), (ushort)c.V0);
        c.V0 = mem.ReadU32((c.A3 + 0x20u));
        c.V1 = mem.ReadU32((c.A3 + 0x28u));
        c.V0 = mem.ReadU16((c.V0 + 0xCu));
        c.V0 = c.V0 + c.V1;
        c.T4 = c.V0;
        c.V0 = mem.ReadU32((c.A3 + 0x20u));
        c.V1 = mem.ReadU32((c.A3 + 0x28u));
        c.V0 = mem.ReadU16((c.V0 + 0x10u));
        c.V0 = c.V0 + c.V1;
        c.T5 = c.V0;
        c.V0 = mem.ReadU32((c.A3 + 0x20u));
        c.V1 = mem.ReadU32((c.A3 + 0x28u));
        c.V0 = mem.ReadU16((c.V0 + 0x14u));
        c.V0 = c.V0 + c.V1;
        c.T6 = c.V0;
        { var _lw = mem.ReadU32(c.T4); RecompOne.Runtime.Gte.Write(0, _lw); }
        { var _lw = mem.ReadU32((c.T4 + 0x4u)); RecompOne.Runtime.Gte.Write(1, _lw); }
        { var _lw = mem.ReadU32(c.T5); RecompOne.Runtime.Gte.Write(2, _lw); }
        { var _lw = mem.ReadU32((c.T5 + 0x4u)); RecompOne.Runtime.Gte.Write(3, _lw); }
        { var _lw = mem.ReadU32(c.T6); RecompOne.Runtime.Gte.Write(4, _lw); }
        { var _lw = mem.ReadU32((c.T6 + 0x4u)); RecompOne.Runtime.Gte.Write(5, _lw); }
        c.T4 = c.T2;
        { var _lw = mem.ReadU32(c.T4); RecompOne.Runtime.Gte.Write(6, _lw); }
        c.V0 = mem.ReadU32((c.A3 + 0x30u));
        c.V0 = mem.ReadU16((c.V0 + 0x6u));
        c.T4 = c.V0;
        RecompOne.Runtime.Gte.Write(8, c.T4);
        RecompOne.Runtime.Gte.NcdtOp(12, true);
        c.V0 = mem.ReadU32((c.A3 + 0x2Cu));
        c.V0 = c.V0 + 0x4u;
        c.T4 = c.V0;
        c.V0 = mem.ReadU32((c.A3 + 0x2Cu));
        c.V0 = c.V0 + 0x10u;
        c.T5 = c.V0;
        c.V0 = mem.ReadU32((c.A3 + 0x2Cu));
        c.V0 = c.V0 + 0x1Cu;
        c.T6 = c.V0;
        { var _sw = RecompOne.Runtime.Gte.Read(20); mem.WriteU32(c.T4, _sw); }
        { var _sw = RecompOne.Runtime.Gte.Read(21); mem.WriteU32(c.T5, _sw); }
        { var _sw = RecompOne.Runtime.Gte.Read(22); mem.WriteU32(c.T6, _sw); }
        c.V1 = mem.ReadU32((c.A3 + 0x2Cu));
        c.V0 = 0x0009u;
        mem.WriteU8((c.V1 + 0x3u), (byte)c.V0);
        c.V1 = mem.ReadU32((c.A3 + 0x2Cu));
        c.V0 = 0x0036u;
        mem.WriteU8((c.V1 + 0x7u), (byte)c.V0);
        c.V0 = mem.ReadU32((c.A3 + 0x30u));
        c.V1 = (uint)(short)mem.ReadU16((c.V0 + 0x4u));
        c.V0 = mem.ReadU32((c.A3 + 0x34u));
        c.A0 = mem.ReadU32((c.A3 + 0x38u));
        c.V0 = (uint)(short)mem.ReadU16((c.V0 + 0x4u));
        c.A0 = (uint)(short)mem.ReadU16((c.A0 + 0x4u));
        c.V1 = c.V1 + c.V0;
        c.V1 = c.V1 + c.A0;
        { var _r = (long)(int)c.V1 * (int)c.S0; c.LO = (uint)_r; c.HI = (uint)(_r >> 32); }
        c.V1 = (uint)((int)c.V1 >> 31);
        c.V0 = c.HI;
        c.V0 = c.V0 - c.V1;
        if ((int)c.V0 <= 0) {
            c.A0 = c.V0 + c.T9;
            goto L80038814;
        }
        c.A0 = c.V0 + c.T9;
        c.V0 = c.A0 < 0x00002000u ? 1u : 0u;
        if (c.V0 == 0u) {
            c.A0 = c.A0 << 2;
            goto L80038814;
        }
        c.A0 = c.A0 << 2;
        goto L800387BC;
        L80038478: ;
        c.V0 = mem.ReadU16((c.A0 + 0x16u));
        c.V1 = mem.ReadU32((c.A3 + 0x44u));
        c.A0 = mem.ReadU32((c.A3 + 0x20u));
        c.V0 = c.V0 + c.V1;
        mem.WriteU32((c.A3 + 0x30u), c.V0);
        c.V0 = mem.ReadU16((c.A0 + 0x16u));
        c.V1 = mem.ReadU32((c.A3 + 0x44u));
        c.A0 = mem.ReadU32((c.A3 + 0x20u));
        c.V0 = c.V0 + c.V1;
        mem.WriteU32((c.A3 + 0x34u), c.V0);
        c.V0 = mem.ReadU16((c.A0 + 0x1Au));
        c.V1 = mem.ReadU32((c.A3 + 0x44u));
        c.V0 = c.V0 + c.V1;
        mem.WriteU32((c.A3 + 0x38u), c.V0);
        c.V0 = mem.ReadU32((c.A3 + 0x30u));
        c.V0 = mem.ReadU32(c.V0);
        c.T4 = c.V0;
        c.V0 = mem.ReadU32((c.A3 + 0x34u));
        c.V0 = mem.ReadU32(c.V0);
        c.T5 = c.V0;
        c.V0 = mem.ReadU32((c.A3 + 0x38u));
        c.V0 = mem.ReadU32(c.V0);
        c.T6 = c.V0;
        RecompOne.Runtime.Gte.Write(12, c.T4);
        RecompOne.Runtime.Gte.Write(14, c.T6);
        RecompOne.Runtime.Gte.Write(13, c.T5);
        RecompOne.Runtime.Gte.Nclip();
        c.T4 = c.T8;
        { var _sw = RecompOne.Runtime.Gte.Read(24); mem.WriteU32(c.T4, _sw); }
        c.V0 = mem.ReadU32((c.A3 + 0x70u));
        if ((int)c.V0 <= 0) {
            c.V0 = c.T3;
            goto L80038818;
        }
        c.V0 = c.T3;
        c.V0 = mem.ReadU32((c.A3 + 0x20u));
        c.V1 = mem.ReadU32((c.A3 + 0x14u));
        c.A0 = mem.ReadU32((c.A3 + 0x14u));
        c.A1 = mem.ReadU16((c.V0 + 0x1Eu));
        mem.WriteU32((c.A3 + 0x2Cu), c.V1);
        c.V1 = mem.ReadU32((c.A3 + 0x44u));
        c.V0 = mem.ReadU32((c.A3 + 0x18u));
        c.A0 = c.A0 + 0x34u;
        mem.WriteU32((c.A3 + 0x14u), c.A0);
        c.A1 = c.A1 + c.V1;
        c.V0 = c.V0 < c.A0 ? 1u : 0u;
        if (c.V0 != 0u) {
            mem.WriteU32((c.A3 + 0x3Cu), c.A1);
            goto L80038834;
        }
        mem.WriteU32((c.A3 + 0x3Cu), c.A1);
        c.V0 = mem.ReadU32((c.A3 + 0x80u));
        c.V1 = mem.ReadU32((c.A3 + 0x20u));
        c.V0 = c.V0 + 0x1u;
        mem.WriteU32((c.A3 + 0x80u), c.V0);
        c.V0 = mem.ReadU16((c.V1 + 0x2u));
        c.V1 = mem.ReadU16((c.A3 + 0x84u));
        c.A0 = mem.ReadU32((c.A3 + 0x2Cu));
        c.V0 = c.V0 + c.V1;
        mem.WriteU16((c.A0 + 0xEu), (ushort)c.V0);
        c.V0 = mem.ReadU32((c.A3 + 0x20u));
        c.V0 = mem.ReadU16((c.V0 + 0x6u));
        c.V1 = mem.ReadU32((c.A3 + 0x2Cu));
        c.V0 = c.V0 & 0xFF9Fu;
        c.V0 = c.A2 | c.V0;
        mem.WriteU16((c.V1 + 0x1Au), (ushort)c.V0);
        c.V0 = mem.ReadU32((c.A3 + 0x30u));
        c.V1 = mem.ReadU32((c.A3 + 0x2Cu));
        c.V0 = mem.ReadU32(c.V0);
        mem.WriteU32((c.V1 + 0x8u), c.V0);
        c.V0 = mem.ReadU32((c.A3 + 0x34u));
        c.V1 = mem.ReadU32((c.A3 + 0x2Cu));
        c.V0 = mem.ReadU32(c.V0);
        mem.WriteU32((c.V1 + 0x14u), c.V0);
        c.V0 = mem.ReadU32((c.A3 + 0x38u));
        c.V1 = mem.ReadU32((c.A3 + 0x2Cu));
        c.V0 = mem.ReadU32(c.V0);
        mem.WriteU32((c.V1 + 0x20u), c.V0);
        c.V0 = mem.ReadU32((c.A3 + 0x3Cu));
        c.V1 = mem.ReadU32((c.A3 + 0x2Cu));
        c.V0 = mem.ReadU32(c.V0);
        mem.WriteU32((c.V1 + 0x2Cu), c.V0);
        c.V0 = mem.ReadU32((c.A3 + 0x20u));
        c.V1 = mem.ReadU32((c.A3 + 0x2Cu));
        c.V0 = mem.ReadU16(c.V0);
        mem.WriteU16((c.V1 + 0xCu), (ushort)c.V0);
        c.V0 = mem.ReadU32((c.A3 + 0x20u));
        c.V1 = mem.ReadU32((c.A3 + 0x2Cu));
        c.V0 = mem.ReadU16((c.V0 + 0x4u));
        mem.WriteU16((c.V1 + 0x18u), (ushort)c.V0);
        c.V0 = mem.ReadU32((c.A3 + 0x20u));
        c.V1 = mem.ReadU32((c.A3 + 0x2Cu));
        c.V0 = mem.ReadU16((c.V0 + 0x8u));
        mem.WriteU16((c.V1 + 0x24u), (ushort)c.V0);
        c.V0 = mem.ReadU32((c.A3 + 0x20u));
        c.V1 = mem.ReadU32((c.A3 + 0x2Cu));
        c.V0 = mem.ReadU16((c.V0 + 0xCu));
        mem.WriteU16((c.V1 + 0x30u), (ushort)c.V0);
        c.V0 = mem.ReadU32((c.A3 + 0x20u));
        c.V1 = mem.ReadU32((c.A3 + 0x28u));
        c.V0 = mem.ReadU16((c.V0 + 0x10u));
        c.V0 = c.V0 + c.V1;
        c.T4 = c.V0;
        c.V0 = mem.ReadU32((c.A3 + 0x20u));
        c.V1 = mem.ReadU32((c.A3 + 0x28u));
        c.V0 = mem.ReadU16((c.V0 + 0x14u));
        c.V0 = c.V0 + c.V1;
        c.T5 = c.V0;
        c.V0 = mem.ReadU32((c.A3 + 0x20u));
        c.V1 = mem.ReadU32((c.A3 + 0x28u));
        c.V0 = mem.ReadU16((c.V0 + 0x18u));
        c.V0 = c.V0 + c.V1;
        c.T6 = c.V0;
        { var _lw = mem.ReadU32(c.T4); RecompOne.Runtime.Gte.Write(0, _lw); }
        { var _lw = mem.ReadU32((c.T4 + 0x4u)); RecompOne.Runtime.Gte.Write(1, _lw); }
        { var _lw = mem.ReadU32(c.T5); RecompOne.Runtime.Gte.Write(2, _lw); }
        { var _lw = mem.ReadU32((c.T5 + 0x4u)); RecompOne.Runtime.Gte.Write(3, _lw); }
        { var _lw = mem.ReadU32(c.T6); RecompOne.Runtime.Gte.Write(4, _lw); }
        { var _lw = mem.ReadU32((c.T6 + 0x4u)); RecompOne.Runtime.Gte.Write(5, _lw); }
        c.T4 = c.T2;
        { var _lw = mem.ReadU32(c.T4); RecompOne.Runtime.Gte.Write(6, _lw); }
        c.V0 = mem.ReadU32((c.A3 + 0x30u));
        c.V0 = mem.ReadU16((c.V0 + 0x6u));
        c.T4 = c.V0;
        RecompOne.Runtime.Gte.Write(8, c.T4);
        RecompOne.Runtime.Gte.NcdtOp(12, true);
        c.V0 = mem.ReadU32((c.A3 + 0x2Cu));
        c.V0 = c.V0 + 0x4u;
        c.T4 = c.V0;
        c.V0 = mem.ReadU32((c.A3 + 0x2Cu));
        c.V0 = c.V0 + 0x10u;
        c.T5 = c.V0;
        c.V0 = mem.ReadU32((c.A3 + 0x2Cu));
        c.V0 = c.V0 + 0x1Cu;
        c.T6 = c.V0;
        { var _sw = RecompOne.Runtime.Gte.Read(20); mem.WriteU32(c.T4, _sw); }
        { var _sw = RecompOne.Runtime.Gte.Read(21); mem.WriteU32(c.T5, _sw); }
        { var _sw = RecompOne.Runtime.Gte.Read(22); mem.WriteU32(c.T6, _sw); }
        c.V0 = mem.ReadU32((c.A3 + 0x20u));
        c.V1 = mem.ReadU32((c.A3 + 0x28u));
        c.V0 = mem.ReadU16((c.V0 + 0x1Cu));
        c.V0 = c.V0 + c.V1;
        c.T4 = c.V0;
        { var _lw = mem.ReadU32(c.T4); RecompOne.Runtime.Gte.Write(0, _lw); }
        { var _lw = mem.ReadU32((c.T4 + 0x4u)); RecompOne.Runtime.Gte.Write(1, _lw); }
        c.T4 = c.T2;
        { var _lw = mem.ReadU32(c.T4); RecompOne.Runtime.Gte.Write(6, _lw); }
        c.V0 = mem.ReadU32((c.A3 + 0x30u));
        c.V0 = mem.ReadU16((c.V0 + 0x6u));
        c.T4 = c.V0;
        RecompOne.Runtime.Gte.Write(8, c.T4);
        RecompOne.Runtime.Gte.NcdsOp(12, true);
        c.V0 = mem.ReadU32((c.A3 + 0x2Cu));
        c.V0 = c.V0 + 0x28u;
        c.T4 = c.V0;
        { var _sw = RecompOne.Runtime.Gte.Read(22); mem.WriteU32(c.T4, _sw); }
        c.V1 = mem.ReadU32((c.A3 + 0x2Cu));
        c.V0 = 0x000Cu;
        mem.WriteU8((c.V1 + 0x3u), (byte)c.V0);
        c.V1 = mem.ReadU32((c.A3 + 0x2Cu));
        c.V0 = 0x003Eu;
        L80038770: ;
        mem.WriteU8((c.V1 + 0x7u), (byte)c.V0);
        c.V0 = mem.ReadU32((c.A3 + 0x30u));
        c.A0 = mem.ReadU32((c.A3 + 0x34u));
        c.V1 = (uint)(short)mem.ReadU16((c.V0 + 0x4u));
        c.V0 = (uint)(short)mem.ReadU16((c.A0 + 0x4u));
        c.V1 = c.V1 + c.V0;
        c.V0 = mem.ReadU32((c.A3 + 0x38u));
        c.A0 = mem.ReadU32((c.A3 + 0x3Cu));
        c.V0 = (uint)(short)mem.ReadU16((c.V0 + 0x4u));
        c.A0 = (uint)(short)mem.ReadU16((c.A0 + 0x4u));
        c.V1 = c.V1 + c.V0;
        c.V1 = c.V1 + c.A0;
        c.V0 = (uint)((int)c.V1 >> 2);
        if ((int)c.V0 <= 0) {
            c.V1 = c.V0 + c.T9;
            goto L80038814;
        }
        c.V1 = c.V0 + c.T9;
        c.V0 = c.V1 < 0x00002000u ? 1u : 0u;
        if (c.V0 == 0u) {
            c.A0 = c.V1 << 2;
            goto L80038814;
        }
        c.A0 = c.V1 << 2;
        L800387BC: ;
        c.V0 = mem.ReadU32((c.A3 + 0x8u));
        c.A1 = mem.ReadU32((c.A3 + 0x2Cu));
        c.V0 = c.A0 + c.V0;
        c.V1 = mem.ReadU32(c.A1);
        c.V0 = mem.ReadU32(c.V0);
        c.V1 = c.V1 & c.T1;
        c.V0 = c.V0 & c.T0;
        c.V1 = c.V1 | c.V0;
        mem.WriteU32(c.A1, c.V1);
        c.V0 = mem.ReadU32((c.A3 + 0x8u));
        c.A0 = c.A0 + c.V0;
        c.V1 = mem.ReadU32(c.A0);
        c.V0 = mem.ReadU32((c.A3 + 0x2Cu));
        c.V1 = c.V1 & c.T1;
        c.V0 = c.V0 & c.T0;
        c.V1 = c.V1 | c.V0;
        mem.WriteU32(c.A0, c.V1);
        c.V0 = mem.ReadU32((c.A3 + 0x7Cu));
        c.V0 = c.V0 + 0x1u;
        mem.WriteU32((c.A3 + 0x7Cu), c.V0);
        L80038814: ;
        c.V0 = c.T3;
        L80038818: ;
        c.T3 = c.T3 - 0x1u;
        c.V1 = mem.ReadU8((c.A3 + 0x1Du));
        c.A0 = mem.ReadU32((c.A3 + 0x20u));
        c.V1 = c.V1 << 2;
        c.V1 = c.V1 + c.A0;
        if (c.V0 != 0u) {
            mem.WriteU32((c.A3 + 0x20u), c.V1);
            goto L80037C98;
        }
        mem.WriteU32((c.A3 + 0x20u), c.V1);
        L80038834: ;
        c.S0 = mem.ReadU32(c.SP);
        c.SP = c.SP + 0x8u;
        return;
    }
}
