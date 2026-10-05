using RecompOne.Runtime;
using RecompOne.Runtime.Dispatch;
using RecompOne.Runtime.Context;
using RecompOne.Runtime.Memory;
using Game = Recompiled.KingsField3_game;

namespace Kf3;

public static partial class NativeScene
{
    // Literal reference for func_8003BB04; see docs/GPU_RENDERER.md.
    static void RunHalf(CpuContext c, PSMemory mem)
    {

        c.SP = c.SP - 0x40u;
        mem.WriteU32((c.SP + 0x24u), c.S3);
        c.S3 = c.A0;
        mem.WriteU32((c.SP + 0x28u), c.S4);
        c.S4 = c.A1;
        mem.WriteU32((c.SP + 0x34u), c.S7);
        c.S7 = c.A2;
        mem.WriteU32((c.SP + 0x2Cu), c.S5);
        c.S5 = 0x1F800000u;
        c.S5 = c.S5 | 0x0074u;
        mem.WriteU32((c.SP + 0x30u), c.S6);
        c.S6 = 0x1F800000u;
        mem.WriteU32((c.SP + 0x20u), c.S2);
        c.S2 = 0x801B0000u;
        c.S2 = c.S2 - 0x1104u;
        mem.WriteU32((c.SP + 0x38u), c.RA);
        mem.WriteU32((c.SP + 0x1Cu), c.S1);
        mem.WriteU32((c.SP + 0x18u), c.S0);
        c.S1 = mem.ReadU8((c.S3 + 0x2u));
        c.V0 = mem.ReadU8((c.S3 + 0x4u));
        c.S1 = c.S1 & 0x0003u;
        c.V0 = c.V0 & 0x003Fu;
        c.S0 = c.V0 << 3;
        c.S0 = c.S0 - c.V0;
        c.S0 = c.S0 << 2;
        c.S0 = c.S0 - c.V0;
        c.S0 = c.S0 << 2;
        c.S0 = c.S0 + c.S2;
        c.V0 = c.S1 << 2;
        c.V0 = c.V0 + c.S1;
        c.V0 = c.V0 << 2;
        c.V0 = c.S0 + c.V0;
        c.T4 = c.V0;
        c.T5 = mem.ReadU32(c.T4);
        c.T6 = mem.ReadU32((c.T4 + 0x4u));
        RecompOne.Runtime.Gte.WriteControl(8, c.T5);
        RecompOne.Runtime.Gte.WriteControl(9, c.T6);
        c.T5 = mem.ReadU32((c.T4 + 0x8u));
        c.T6 = mem.ReadU32((c.T4 + 0xCu));
        c.T7 = mem.ReadU32((c.T4 + 0x10u));
        RecompOne.Runtime.Gte.WriteControl(10, c.T5);
        RecompOne.Runtime.Gte.WriteControl(11, c.T6);
        RecompOne.Runtime.Gte.WriteControl(12, c.T7);
        c.V0 = c.S0 + 0x50u;
        c.T4 = c.V0;
        c.T5 = mem.ReadU32(c.T4);
        c.T6 = mem.ReadU32((c.T4 + 0x4u));
        RecompOne.Runtime.Gte.WriteControl(16, c.T5);
        RecompOne.Runtime.Gte.WriteControl(17, c.T6);
        c.T5 = mem.ReadU32((c.T4 + 0x8u));
        c.T6 = mem.ReadU32((c.T4 + 0xCu));
        c.T7 = mem.ReadU32((c.T4 + 0x10u));
        RecompOne.Runtime.Gte.WriteControl(18, c.T5);
        RecompOne.Runtime.Gte.WriteControl(19, c.T6);
        RecompOne.Runtime.Gte.WriteControl(20, c.T7);
        c.A0 = mem.ReadU16((c.S0 + 0x68u));
        c.A1 = mem.ReadU16((c.S0 + 0x6Au));
        c.RA = 0x8003BBF0u;
        Game.func_80035358(c, mem);
        c.A3 = mem.ReadU8((c.S0 + 0x64u));
        c.T4 = c.A3;
        c.A3 = mem.ReadU8((c.S0 + 0x65u));
        c.T5 = c.A3;
        c.S0 = mem.ReadU8((c.S0 + 0x66u));
        c.T6 = c.S0;
        c.T4 = c.T4 << 4;
        c.T5 = c.T5 << 4;
        c.T6 = c.T6 << 4;
        RecompOne.Runtime.Gte.WriteControl(13, c.T4);
        RecompOne.Runtime.Gte.WriteControl(14, c.T5);
        RecompOne.Runtime.Gte.WriteControl(15, c.T6);
        c.A3 = 0x801B0000u;
        c.A3 = c.A3 - 0x14B4u;
        c.T4 = c.A3;
        c.T5 = mem.ReadU32(c.T4);
        c.T6 = mem.ReadU32((c.T4 + 0x4u));
        RecompOne.Runtime.Gte.WriteControl(0, c.T5);
        RecompOne.Runtime.Gte.WriteControl(1, c.T6);
        c.T5 = mem.ReadU32((c.T4 + 0x8u));
        c.T6 = mem.ReadU32((c.T4 + 0xCu));
        c.T7 = mem.ReadU32((c.T4 + 0x10u));
        RecompOne.Runtime.Gte.WriteControl(2, c.T5);
        RecompOne.Runtime.Gte.WriteControl(3, c.T6);
        RecompOne.Runtime.Gte.WriteControl(4, c.T7);
        c.T4 = c.A3;
        c.T5 = mem.ReadU32((c.T4 + 0x14u));
        c.T6 = mem.ReadU32((c.T4 + 0x18u));
        RecompOne.Runtime.Gte.WriteControl(5, c.T5);
        c.T7 = mem.ReadU32((c.T4 + 0x1Cu));
        RecompOne.Runtime.Gte.WriteControl(6, c.T6);
        RecompOne.Runtime.Gte.WriteControl(7, c.T7);
        c.T4 = c.S4;
        { var _lw = mem.ReadU32(c.T4); RecompOne.Runtime.Gte.Write(0, _lw); }
        { var _lw = mem.ReadU32((c.T4 + 0x4u)); RecompOne.Runtime.Gte.Write(1, _lw); }
        RecompOne.Runtime.Gte.MvmvaOp(12, false, 0, 0, 0);
        c.A3 = 0x1F800000u;
        c.A3 = c.A3 | 0x0088u;
        c.T4 = c.A3;
        { var _sw = RecompOne.Runtime.Gte.Read(25); mem.WriteU32(c.T4, _sw); }
        { var _sw = RecompOne.Runtime.Gte.Read(26); mem.WriteU32((c.T4 + 0x4u), _sw); }
        { var _sw = RecompOne.Runtime.Gte.Read(27); mem.WriteU32((c.T4 + 0x8u), _sw); }
        c.V0 = c.SP + 0x10u;
        c.T4 = c.V0;
        c.T5 = RecompOne.Runtime.Gte.ReadControl(31);
        mem.WriteU32(c.T4, c.T5);
        c.S1 = c.S1 << 5;
        c.S2 = c.S2 - 0x370u;
        c.S1 = c.S1 + c.S2;
        c.T4 = c.S1;
        c.T5 = mem.ReadU32(c.T4);
        c.T6 = mem.ReadU32((c.T4 + 0x4u));
        RecompOne.Runtime.Gte.WriteControl(0, c.T5);
        RecompOne.Runtime.Gte.WriteControl(1, c.T6);
        c.T5 = mem.ReadU32((c.T4 + 0x8u));
        c.T6 = mem.ReadU32((c.T4 + 0xCu));
        c.T7 = mem.ReadU32((c.T4 + 0x10u));
        RecompOne.Runtime.Gte.WriteControl(2, c.T5);
        RecompOne.Runtime.Gte.WriteControl(3, c.T6);
        RecompOne.Runtime.Gte.WriteControl(4, c.T7);
        c.T4 = c.S5;
        c.T5 = mem.ReadU32((c.T4 + 0x14u));
        c.T6 = mem.ReadU32((c.T4 + 0x18u));
        RecompOne.Runtime.Gte.WriteControl(5, c.T5);
        c.T7 = mem.ReadU32((c.T4 + 0x1Cu));
        RecompOne.Runtime.Gte.WriteControl(6, c.T6);
        RecompOne.Runtime.Gte.WriteControl(7, c.T7);
        c.V1 = 0x0001u;
        c.V0 = 0x80190000u;
        c.V0 = (uint)(short)mem.ReadU16((c.V0 - 0x52Cu));
        c.A0 = mem.ReadU8(c.S3);
        if (c.V0 != c.V1) {
            goto L8003BD64;
        }
        c.V0 = 0x80190000u;
        c.V0 = mem.ReadU8((c.V0 - 0x516u));
        if (c.V0 == 0u) {
            goto L8003BD64;
        }
        c.V0 = 0x1F800000u;
        c.V0 = mem.ReadU32((c.V0 + 0x10u));
        c.V0 = mem.ReadU32((c.V0 + 0x4u));
        c.V0 = c.V0 >> 1;
        c.V0 = c.A0 < c.V0 ? 1u : 0u;
        if (c.V0 == 0u) {
            goto L8003BE04;
        }
        L8003BD64: ;
        c.V0 = mem.ReadU32((c.S6 + 0x18u));
        c.V1 = mem.ReadU32((c.S6 + 0x14u));
        c.V0 = c.V0 - 0x2800u;
        c.V0 = c.V0 < c.V1 ? 1u : 0u;
        if (c.V0 == 0u) {
            c.V0 = c.S7 & 0x0004u;
            goto L8003BD84;
        }
        c.V0 = c.S7 & 0x0004u;
        c.S7 = c.S7 & 0x00FBu;
        c.V0 = c.S7 & 0x0004u;
        L8003BD84: ;
        if (c.V0 == 0u) {
            goto L8003BDCC;
        }
        c.A0 = c.A0 & 0xFFFFu;
        c.V0 = c.A0 << 3;
        c.V0 = c.V0 - c.A0;
        c.V0 = c.V0 << 2;
        c.V1 = mem.ReadU32((c.S6 + 0x10u));
        c.V0 = c.V0 + 0xCu;
        c.V0 = c.V0 + c.V1;
        mem.WriteU32((c.S6 + 0x24u), c.V0);
        c.V0 = mem.ReadU32(c.V0);
        c.V1 = mem.ReadU32((c.S6 + 0x10u));
        c.V0 = c.V0 + 0xCu;
        c.V0 = c.V0 + c.V1;
        mem.WriteU32((c.S6 + 0x4Cu), c.V0);
        c.RA = 0x8003BDC4u;
        if (RetainedMap.Submit(mem, c.S3, true, mem.ReadU32(c.SP + 0x38) - 8)) goto L8003BE04;
        Game.func_8003AB04(c, mem);
        goto L8003BE04;
        L8003BDCC: ;
        c.A0 = c.A0 & 0xFFFFu;
        c.V0 = c.A0 << 3;
        c.V0 = c.V0 - c.A0;
        c.V0 = c.V0 << 2;
        c.V1 = mem.ReadU32((c.S6 + 0x10u));
        c.V0 = c.V0 + 0xCu;
        c.V0 = c.V0 + c.V1;
        mem.WriteU32((c.S6 + 0x24u), c.V0);
        c.V0 = mem.ReadU32(c.V0);
        c.V1 = mem.ReadU32((c.S6 + 0x10u));
        c.V0 = c.V0 + 0xCu;
        c.V0 = c.V0 + c.V1;
        mem.WriteU32((c.S6 + 0x4Cu), c.V0);
        c.RA = 0x8003BE04u;
        if (RetainedMap.Submit(mem, c.S3, false, mem.ReadU32(c.SP + 0x38) - 8)) goto L8003BE04;
        Game.func_80039D50(c, mem);
        L8003BE04: ;
        c.RA = mem.ReadU32((c.SP + 0x38u));
        c.S7 = mem.ReadU32((c.SP + 0x34u));
        c.S6 = mem.ReadU32((c.SP + 0x30u));
        c.S5 = mem.ReadU32((c.SP + 0x2Cu));
        c.S4 = mem.ReadU32((c.SP + 0x28u));
        c.S3 = mem.ReadU32((c.SP + 0x24u));
        c.S2 = mem.ReadU32((c.SP + 0x20u));
        c.S1 = mem.ReadU32((c.SP + 0x1Cu));
        c.S0 = mem.ReadU32((c.SP + 0x18u));
        c.SP = c.SP + 0x40u;
        return;
    }
}
