using RecompOne.Runtime;
using RecompOne.Runtime.Dispatch;
using RecompOne.Runtime.Context;
using RecompOne.Runtime.Memory;
using Game = Recompiled.KingsField3_game;

namespace Kf3;

public static partial class NativeScene
{
    // Literal reference for func_80039428; see docs/GPU_RENDERER.md.
    static void RunSkyAssembler(CpuContext c, PSMemory mem)
    {

        c.SP = c.SP - 0x18u;
        c.A0 = c.A0 & 0xFFFFu;
        c.V1 = c.A0 << 3;
        c.V1 = c.V1 - c.A0;
        c.V1 = c.V1 << 2;
        c.A0 = 0x1F800000u;
        c.A0 = mem.ReadU32((c.A0 + 0x10u));
        c.V1 = c.V1 + 0xCu;
        mem.WriteU32((c.SP + 0x10u), c.S4);
        mem.WriteU32((c.SP + 0xCu), c.S3);
        mem.WriteU32((c.SP + 0x8u), c.S2);
        mem.WriteU32((c.SP + 0x4u), c.S1);
        mem.WriteU32(c.SP, c.S0);
        c.V1 = c.V1 + c.A0;
        c.At = 0x1F800000u;
        mem.WriteU32((c.At + 0x24u), c.V1);
        c.V0 = mem.ReadU32((c.V1 + 0x8u));
        c.V0 = c.V0 + 0xCu;
        c.V0 = c.V0 + c.A0;
        c.At = 0x1F800000u;
        mem.WriteU32((c.At + 0x28u), c.V0);
        c.V0 = mem.ReadU32((c.V1 + 0x10u));
        c.T0 = 0x1F800000u;
        c.V0 = c.V0 + 0xCu;
        c.V0 = c.V0 + c.A0;
        c.At = 0x1F800000u;
        mem.WriteU32((c.At + 0x20u), c.V0);
        c.S0 = mem.ReadU32((c.V1 + 0x14u));
        c.V0 = 0x1F800000u;
        c.V0 = mem.ReadU32((c.V0 + 0x78u));
        c.S3 = c.A2 << 5;
        c.V0 = c.S0 + c.V0;
        c.V1 = c.S0;
        c.At = 0x1F800000u;
        mem.WriteU32((c.At + 0x78u), c.V0);
        if (c.V1 == 0u) {
            c.S0 = c.S0 - 0x1u;
            goto L80039D30;
        }
        c.S0 = c.S0 - 0x1u;
        c.S1 = 0x1F800000u;
        c.S1 = c.S1 | 0x0070u;
        c.S2 = 0x1F800000u;
        c.S2 = c.S2 | 0x0064u;
        c.A2 = c.A1 << 16;
        c.T8 = 0x00FF0000u;
        c.T8 = c.T8 | 0xFFFFu;
        c.T9 = 0xFF000000u;
        L800394E0: ;
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
            goto L80039544;
        }
        c.V0 = (int)c.V1 < 53 ? 1u : 0u;
        if (c.V0 == 0u) {
            c.V0 = 0x0030u;
            goto L80039528;
        }
        c.V0 = 0x0030u;
        if (c.V1 == c.V0) {
            c.V0 = c.S0;
            goto L8003996C;
        }
        c.V0 = c.S0;
        goto L80039D14;
        L80039528: ;
        c.V0 = 0x0038u;
        if (c.V1 == c.V0) {
            c.V0 = 0x003Cu;
            goto L80039AD8;
        }
        c.V0 = 0x003Cu;
        if (c.V1 == c.V0) {
            c.V0 = c.S0;
            goto L80039720;
        }
        c.V0 = c.S0;
        goto L80039D14;
        L80039544: ;
        c.V1 = mem.ReadU16((c.A0 + 0x12u));
        c.V0 = mem.ReadU32((c.T0 + 0x44u));
        c.T2 = c.V1 + c.V0;
        c.V1 = mem.ReadU16((c.A0 + 0x16u));
        c.A0 = mem.ReadU16((c.A0 + 0x1Au));
        c.S4 = mem.ReadU32(c.T2);
        c.T1 = c.V1 + c.V0;
        c.T3 = c.A0 + c.V0;
        c.T4 = c.S4;
        c.S4 = mem.ReadU32(c.T1);
        c.T5 = c.S4;
        c.S4 = mem.ReadU32(c.T3);
        c.T6 = c.S4;
        RecompOne.Runtime.Gte.Write(12, c.T4);
        RecompOne.Runtime.Gte.Write(14, c.T6);
        RecompOne.Runtime.Gte.Write(13, c.T5);
        RecompOne.Runtime.Gte.Nclip();
        c.T4 = c.S1;
        { var _sw = RecompOne.Runtime.Gte.Read(24); mem.WriteU32(c.T4, _sw); }
        c.V0 = mem.ReadU32((c.T0 + 0x70u));
        if ((int)c.V0 <= 0) {
            c.V0 = c.S0;
            goto L80039D14;
        }
        c.V0 = c.S0;
        c.A3 = mem.ReadU32((c.T0 + 0x14u));
        c.V0 = mem.ReadU32((c.T0 + 0x18u));
        c.V1 = c.A3 + 0x28u;
        c.V0 = c.V0 < c.V1 ? 1u : 0u;
        if (c.V0 != 0u) {
            mem.WriteU32((c.T0 + 0x14u), c.V1);
            goto L80039D30;
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
        c.V0 = c.V0 & 0xFF9Fu;
        c.V0 = c.V0 | c.S3;
        mem.WriteU16((c.A3 + 0x1Au), (ushort)c.V0);
        c.V0 = mem.ReadU32(c.T2);
        mem.WriteU32((c.A3 + 0x8u), c.V0);
        c.V0 = mem.ReadU32(c.T1);
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
        RecompOne.Runtime.Gte.NcctOp(12, true);
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
        c.V0 = c.V0 & 0x0002u;
        c.V0 = c.V0 | 0x0034u;
        goto L80039CA4;
        L80039720: ;
        c.V1 = mem.ReadU16((c.A0 + 0x16u));
        c.V0 = mem.ReadU32((c.T0 + 0x44u));
        c.T2 = c.V1 + c.V0;
        c.V1 = mem.ReadU16((c.A0 + 0x1Au));
        c.A0 = mem.ReadU16((c.A0 + 0x1Eu));
        c.S4 = mem.ReadU32(c.T2);
        c.T1 = c.V1 + c.V0;
        c.T3 = c.A0 + c.V0;
        c.T4 = c.S4;
        c.S4 = mem.ReadU32(c.T1);
        c.T5 = c.S4;
        c.S4 = mem.ReadU32(c.T3);
        c.T6 = c.S4;
        RecompOne.Runtime.Gte.Write(12, c.T4);
        RecompOne.Runtime.Gte.Write(14, c.T6);
        RecompOne.Runtime.Gte.Write(13, c.T5);
        RecompOne.Runtime.Gte.Nclip();
        c.T4 = c.S1;
        { var _sw = RecompOne.Runtime.Gte.Read(24); mem.WriteU32(c.T4, _sw); }
        c.V0 = mem.ReadU32((c.T0 + 0x70u));
        if ((int)c.V0 <= 0) {
            c.V0 = c.S0;
            goto L80039D14;
        }
        c.V0 = c.S0;
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
            goto L80039D30;
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
        c.V0 = c.V0 & 0xFF9Fu;
        c.V0 = c.V0 | c.S3;
        mem.WriteU16((c.A3 + 0x1Au), (ushort)c.V0);
        c.V0 = mem.ReadU32(c.T2);
        mem.WriteU32((c.A3 + 0x8u), c.V0);
        c.V0 = mem.ReadU32(c.T1);
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
        RecompOne.Runtime.Gte.NcctOp(12, true);
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
        RecompOne.Runtime.Gte.NccsOp(12, true);
        c.V0 = c.A3 + 0x28u;
        c.T4 = c.V0;
        { var _sw = RecompOne.Runtime.Gte.Read(22); mem.WriteU32(c.T4, _sw); }
        c.V0 = 0x000Cu;
        mem.WriteU8((c.A3 + 0x3u), (byte)c.V0);
        c.V0 = mem.ReadU8((c.T0 + 0x1Fu));
        c.V0 = c.V0 & 0x0002u;
        c.V0 = c.V0 | 0x003Cu;
        goto L80039CA4;
        L8003996C: ;
        c.V1 = mem.ReadU16((c.A0 + 0xAu));
        c.V0 = mem.ReadU32((c.T0 + 0x44u));
        c.T2 = c.V1 + c.V0;
        c.V1 = mem.ReadU16((c.A0 + 0xEu));
        c.A0 = mem.ReadU16((c.A0 + 0x12u));
        c.S4 = mem.ReadU32(c.T2);
        c.T1 = c.V1 + c.V0;
        c.T3 = c.A0 + c.V0;
        c.T4 = c.S4;
        c.S4 = mem.ReadU32(c.T1);
        c.T5 = c.S4;
        c.S4 = mem.ReadU32(c.T3);
        c.T6 = c.S4;
        RecompOne.Runtime.Gte.Write(12, c.T4);
        RecompOne.Runtime.Gte.Write(14, c.T6);
        RecompOne.Runtime.Gte.Write(13, c.T5);
        RecompOne.Runtime.Gte.Nclip();
        c.T4 = c.S1;
        { var _sw = RecompOne.Runtime.Gte.Read(24); mem.WriteU32(c.T4, _sw); }
        c.V0 = mem.ReadU32((c.T0 + 0x70u));
        if ((int)c.V0 <= 0) {
            c.V0 = c.S0;
            goto L80039D14;
        }
        c.V0 = c.S0;
        c.A3 = mem.ReadU32((c.T0 + 0x14u));
        c.V0 = mem.ReadU32((c.T0 + 0x18u));
        c.V1 = c.A3 + 0x1Cu;
        c.V0 = c.V0 < c.V1 ? 1u : 0u;
        if (c.V0 != 0u) {
            mem.WriteU32((c.T0 + 0x14u), c.V1);
            goto L80039D30;
        }
        mem.WriteU32((c.T0 + 0x14u), c.V1);
        c.V0 = mem.ReadU32((c.T0 + 0x80u));
        c.V0 = c.V0 + 0x1u;
        mem.WriteU32((c.T0 + 0x80u), c.V0);
        c.V0 = mem.ReadU32(c.T2);
        mem.WriteU32((c.A3 + 0x8u), c.V0);
        c.V0 = mem.ReadU32(c.T1);
        mem.WriteU32((c.A3 + 0x10u), c.V0);
        c.V0 = mem.ReadU32(c.T3);
        mem.WriteU32((c.A3 + 0x18u), c.V0);
        c.V0 = mem.ReadU32((c.T0 + 0x20u));
        c.V1 = mem.ReadU32((c.T0 + 0x28u));
        c.V0 = mem.ReadU16((c.V0 + 0x4u));
        c.V0 = c.V0 + c.V1;
        c.T4 = c.V0;
        c.V0 = mem.ReadU32((c.T0 + 0x20u));
        c.V1 = mem.ReadU32((c.T0 + 0x28u));
        c.V0 = mem.ReadU16((c.V0 + 0x8u));
        c.V0 = c.V0 + c.V1;
        c.T5 = c.V0;
        c.V0 = mem.ReadU32((c.T0 + 0x20u));
        c.V1 = mem.ReadU32((c.T0 + 0x28u));
        c.V0 = mem.ReadU16((c.V0 + 0xCu));
        c.V0 = c.V0 + c.V1;
        c.T6 = c.V0;
        { var _lw = mem.ReadU32(c.T4); RecompOne.Runtime.Gte.Write(0, _lw); }
        { var _lw = mem.ReadU32((c.T4 + 0x4u)); RecompOne.Runtime.Gte.Write(1, _lw); }
        { var _lw = mem.ReadU32(c.T5); RecompOne.Runtime.Gte.Write(2, _lw); }
        { var _lw = mem.ReadU32((c.T5 + 0x4u)); RecompOne.Runtime.Gte.Write(3, _lw); }
        { var _lw = mem.ReadU32(c.T6); RecompOne.Runtime.Gte.Write(4, _lw); }
        { var _lw = mem.ReadU32((c.T6 + 0x4u)); RecompOne.Runtime.Gte.Write(5, _lw); }
        c.S4 = mem.ReadU32((c.T0 + 0x20u));
        c.T4 = c.S4;
        { var _lw = mem.ReadU32(c.T4); RecompOne.Runtime.Gte.Write(6, _lw); }
        RecompOne.Runtime.Gte.NcctOp(12, true);
        c.V0 = c.A3 + 0x4u;
        c.T4 = c.V0;
        c.V0 = c.A3 + 0xCu;
        c.T5 = c.V0;
        c.V0 = c.A3 + 0x14u;
        c.T6 = c.V0;
        { var _sw = RecompOne.Runtime.Gte.Read(20); mem.WriteU32(c.T4, _sw); }
        { var _sw = RecompOne.Runtime.Gte.Read(21); mem.WriteU32(c.T5, _sw); }
        { var _sw = RecompOne.Runtime.Gte.Read(22); mem.WriteU32(c.T6, _sw); }
        c.V0 = 0x0006u;
        mem.WriteU8((c.A3 + 0x3u), (byte)c.V0);
        c.V0 = 0x0030u;
        goto L80039CA4;
        L80039AD8: ;
        c.V1 = mem.ReadU16((c.A0 + 0xAu));
        c.V0 = mem.ReadU32((c.T0 + 0x44u));
        c.T2 = c.V1 + c.V0;
        c.V1 = mem.ReadU16((c.A0 + 0xEu));
        c.A0 = mem.ReadU16((c.A0 + 0x12u));
        c.S4 = mem.ReadU32(c.T2);
        c.T1 = c.V1 + c.V0;
        c.T3 = c.A0 + c.V0;
        c.T4 = c.S4;
        c.S4 = mem.ReadU32(c.T1);
        c.T5 = c.S4;
        c.S4 = mem.ReadU32(c.T3);
        c.T6 = c.S4;
        RecompOne.Runtime.Gte.Write(12, c.T4);
        RecompOne.Runtime.Gte.Write(14, c.T6);
        RecompOne.Runtime.Gte.Write(13, c.T5);
        RecompOne.Runtime.Gte.Nclip();
        c.T4 = c.S1;
        { var _sw = RecompOne.Runtime.Gte.Read(24); mem.WriteU32(c.T4, _sw); }
        c.V0 = mem.ReadU32((c.T0 + 0x70u));
        if ((int)c.V0 <= 0) {
            c.V0 = c.S0;
            goto L80039D14;
        }
        c.V0 = c.S0;
        c.V0 = mem.ReadU32((c.T0 + 0x20u));
        c.A3 = mem.ReadU32((c.T0 + 0x14u));
        c.V1 = mem.ReadU32((c.T0 + 0x44u));
        c.A1 = mem.ReadU16((c.V0 + 0x12u));
        c.V0 = mem.ReadU32((c.T0 + 0x18u));
        c.A0 = c.A3 + 0x24u;
        mem.WriteU32((c.T0 + 0x14u), c.A0);
        c.V0 = c.V0 < c.A0 ? 1u : 0u;
        if (c.V0 != 0u) {
            c.A1 = c.A1 + c.V1;
            goto L80039D30;
        }
        c.A1 = c.A1 + c.V1;
        c.V0 = mem.ReadU32((c.T0 + 0x80u));
        c.V0 = c.V0 + 0x1u;
        mem.WriteU32((c.T0 + 0x80u), c.V0);
        c.V0 = mem.ReadU32(c.T2);
        mem.WriteU32((c.A3 + 0x8u), c.V0);
        c.V0 = mem.ReadU32(c.T1);
        mem.WriteU32((c.A3 + 0x10u), c.V0);
        c.V0 = mem.ReadU32(c.T3);
        mem.WriteU32((c.A3 + 0x18u), c.V0);
        c.V0 = mem.ReadU32(c.A1);
        mem.WriteU32((c.A3 + 0x20u), c.V0);
        c.V0 = mem.ReadU32((c.T0 + 0x20u));
        c.V1 = mem.ReadU32((c.T0 + 0x28u));
        c.V0 = mem.ReadU16((c.V0 + 0x4u));
        c.V0 = c.V0 + c.V1;
        c.T4 = c.V0;
        c.V0 = mem.ReadU32((c.T0 + 0x20u));
        c.V1 = mem.ReadU32((c.T0 + 0x28u));
        c.V0 = mem.ReadU16((c.V0 + 0x8u));
        c.V0 = c.V0 + c.V1;
        c.T5 = c.V0;
        c.V0 = mem.ReadU32((c.T0 + 0x20u));
        c.V1 = mem.ReadU32((c.T0 + 0x28u));
        c.V0 = mem.ReadU16((c.V0 + 0xCu));
        c.V0 = c.V0 + c.V1;
        c.T6 = c.V0;
        { var _lw = mem.ReadU32(c.T4); RecompOne.Runtime.Gte.Write(0, _lw); }
        { var _lw = mem.ReadU32((c.T4 + 0x4u)); RecompOne.Runtime.Gte.Write(1, _lw); }
        { var _lw = mem.ReadU32(c.T5); RecompOne.Runtime.Gte.Write(2, _lw); }
        { var _lw = mem.ReadU32((c.T5 + 0x4u)); RecompOne.Runtime.Gte.Write(3, _lw); }
        { var _lw = mem.ReadU32(c.T6); RecompOne.Runtime.Gte.Write(4, _lw); }
        { var _lw = mem.ReadU32((c.T6 + 0x4u)); RecompOne.Runtime.Gte.Write(5, _lw); }
        c.S4 = mem.ReadU32((c.T0 + 0x20u));
        c.T4 = c.S4;
        { var _lw = mem.ReadU32(c.T4); RecompOne.Runtime.Gte.Write(6, _lw); }
        RecompOne.Runtime.Gte.NcctOp(12, true);
        c.V0 = c.A3 + 0x4u;
        c.T4 = c.V0;
        c.V0 = c.A3 + 0xCu;
        c.T5 = c.V0;
        c.V0 = c.A3 + 0x14u;
        c.T6 = c.V0;
        { var _sw = RecompOne.Runtime.Gte.Read(20); mem.WriteU32(c.T4, _sw); }
        { var _sw = RecompOne.Runtime.Gte.Read(21); mem.WriteU32(c.T5, _sw); }
        { var _sw = RecompOne.Runtime.Gte.Read(22); mem.WriteU32(c.T6, _sw); }
        c.V0 = mem.ReadU32((c.T0 + 0x20u));
        c.V1 = mem.ReadU32((c.T0 + 0x28u));
        c.V0 = mem.ReadU16((c.V0 + 0x10u));
        c.V0 = c.V0 + c.V1;
        c.T4 = c.V0;
        { var _lw = mem.ReadU32(c.T4); RecompOne.Runtime.Gte.Write(0, _lw); }
        { var _lw = mem.ReadU32((c.T4 + 0x4u)); RecompOne.Runtime.Gte.Write(1, _lw); }
        c.S4 = mem.ReadU32((c.T0 + 0x20u));
        c.T4 = c.S4;
        { var _lw = mem.ReadU32(c.T4); RecompOne.Runtime.Gte.Write(6, _lw); }
        RecompOne.Runtime.Gte.NccsOp(12, true);
        c.V0 = c.A3 + 0x1Cu;
        c.T4 = c.V0;
        { var _sw = RecompOne.Runtime.Gte.Read(22); mem.WriteU32(c.T4, _sw); }
        c.V0 = 0x0008u;
        mem.WriteU8((c.A3 + 0x3u), (byte)c.V0);
        c.V0 = 0x0038u;
        L80039CA4: ;
        mem.WriteU8((c.A3 + 0x7u), (byte)c.V0);
        c.V0 = (uint)((int)c.A2 >> 16);
        if ((int)c.V0 <= 0) {
            c.A0 = c.V0 & 0x0007u;
            goto L80039D10;
        }
        c.A0 = c.V0 & 0x0007u;
        c.V0 = c.A0 < 0x00000008u ? 1u : 0u;
        if (c.V0 == 0u) {
            c.A0 = c.A0 << 2;
            goto L80039D10;
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
        c.V0 = mem.ReadU32(c.A0);
        c.V1 = c.A3 & c.T8;
        c.V0 = c.V0 & c.T9;
        c.V0 = c.V0 | c.V1;
        mem.WriteU32(c.A0, c.V0);
        c.V0 = mem.ReadU32((c.T0 + 0x7Cu));
        c.V0 = c.V0 + 0x1u;
        mem.WriteU32((c.T0 + 0x7Cu), c.V0);
        L80039D10: ;
        c.V0 = c.S0;
        L80039D14: ;
        c.S0 = c.S0 - 0x1u;
        c.V1 = mem.ReadU8((c.T0 + 0x1Du));
        c.A0 = mem.ReadU32((c.T0 + 0x20u));
        c.V1 = c.V1 << 2;
        c.V1 = c.V1 + c.A0;
        if (c.V0 != 0u) {
            mem.WriteU32((c.T0 + 0x20u), c.V1);
            goto L800394E0;
        }
        mem.WriteU32((c.T0 + 0x20u), c.V1);
        L80039D30: ;
        c.S4 = mem.ReadU32((c.SP + 0x10u));
        c.S3 = mem.ReadU32((c.SP + 0xCu));
        c.S2 = mem.ReadU32((c.SP + 0x8u));
        c.S1 = mem.ReadU32((c.SP + 0x4u));
        c.S0 = mem.ReadU32(c.SP);
        c.SP = c.SP + 0x18u;
        return;
    }
}
