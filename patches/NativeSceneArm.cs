using RecompOne.Runtime;
using RecompOne.Runtime.Dispatch;
using RecompOne.Runtime.Context;
using RecompOne.Runtime.Memory;
using Game = Recompiled.KingsField3_game;

namespace Kf3;

public static partial class NativeScene
{
    // Literal reference for func_8003DF50; see docs/GPU_RENDERER.md.
    static void RunArm(CpuContext c, PSMemory mem)
    {

        c.SP = c.SP - 0x50u;
        mem.WriteU32((c.SP + 0x40u), c.S2);
        c.S2 = 0x801B0000u;
        c.S2 = c.S2 + 0x25A4u;
        mem.WriteU32((c.SP + 0x4Cu), c.RA);
        mem.WriteU32((c.SP + 0x48u), c.S4);
        mem.WriteU32((c.SP + 0x44u), c.S3);
        mem.WriteU32((c.SP + 0x3Cu), c.S1);
        mem.WriteU32((c.SP + 0x38u), c.S0);
        c.V0 = (uint)(short)mem.ReadU16(c.S2);
        c.S4 = 0xFFFFFFFFu;
        if (c.V0 == c.S4) {
            c.S3 = 0x1F800000u;
            goto L8003E328;
        }
        c.S3 = 0x1F800000u;
        c.V1 = 0x801B0000u;
        c.V1 = mem.ReadU32((c.V1 + 0x25F8u));
        c.A0 = 0x801B0000u;
        c.A0 = mem.ReadU32((c.A0 + 0x25F0u));
        c.V1 = (uint)((int)c.V1 >> 11);
        c.A0 = (uint)((int)c.A0 >> 11);
        c.V0 = c.V1 << 1;
        c.V0 = c.V0 + c.V1;
        c.V0 = c.V0 << 3;
        c.V0 = c.V0 + c.V1;
        c.V0 = c.V0 << 5;
        c.V1 = c.A0 << 2;
        c.V1 = c.V1 + c.A0;
        c.V1 = c.V1 << 1;
        c.A0 = 0x801B0000u;
        c.A0 = mem.ReadU16((c.A0 + 0x2644u));
        c.V0 = c.V0 + c.V1;
        c.V0 = c.V0 + c.A0;
        c.At = 0x801D0000u;
        c.At = c.At + 0x4468u;
        c.At = c.At + c.V0;
        c.V0 = mem.ReadU8(c.At);
        c.S1 = 0x801B0000u;
        c.S1 = c.S1 - 0x1104u;
        c.V0 = c.V0 & 0x003Fu;
        c.S0 = c.V0 << 3;
        c.S0 = c.S0 - c.V0;
        c.S0 = c.S0 << 2;
        c.S0 = c.S0 - c.V0;
        c.S0 = c.S0 << 2;
        c.S0 = c.S0 + c.S1;
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
        c.T4 = c.S0;
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
        c.A0 = mem.ReadU16((c.S0 + 0x68u));
        c.A1 = mem.ReadU16((c.S0 + 0x6Au));
        c.RA = 0x8003E06Cu;
        Game.func_80035358(c, mem);
        c.T8 = mem.ReadU8((c.S0 + 0x64u));
        c.T4 = c.T8;
        c.T8 = mem.ReadU8((c.S0 + 0x65u));
        c.T5 = c.T8;
        c.S0 = mem.ReadU8((c.S0 + 0x66u));
        c.T6 = c.S0;
        c.T4 = c.T4 << 4;
        c.T5 = c.T5 << 4;
        c.T6 = c.T6 << 4;
        RecompOne.Runtime.Gte.WriteControl(13, c.T4);
        RecompOne.Runtime.Gte.WriteControl(14, c.T5);
        RecompOne.Runtime.Gte.WriteControl(15, c.T6);
        c.A0 = 0x801B0000u;
        c.A0 = mem.ReadU32((c.A0 + 0x2594u));
        c.V0 = (uint)(short)mem.ReadU16((c.A0 + 0x34u));
        mem.WriteU32((c.SP + 0x2Cu), c.V0);
        c.V0 = (uint)(short)mem.ReadU16((c.A0 + 0x36u));
        c.A1 = c.SP + 0x18u;
        mem.WriteU32((c.SP + 0x30u), c.V0);
        c.V0 = (uint)(short)mem.ReadU16((c.A0 + 0x38u));
        c.A0 = c.A0 + 0x3Cu;
        mem.WriteU32((c.SP + 0x34u), c.V0);
        c.RA = 0x8003E0DCu;
        Game.RotMatrix(c, mem);
        c.V0 = c.SP + 0x18u;
        c.T4 = c.V0;
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
        c.T4 = c.V0;
        c.T5 = mem.ReadU32((c.T4 + 0x14u));
        c.T6 = mem.ReadU32((c.T4 + 0x18u));
        RecompOne.Runtime.Gte.WriteControl(5, c.T5);
        c.T7 = mem.ReadU32((c.T4 + 0x1Cu));
        RecompOne.Runtime.Gte.WriteControl(6, c.T6);
        RecompOne.Runtime.Gte.WriteControl(7, c.T7);
        c.V0 = 0x801B0000u;
        c.V0 = mem.ReadU32((c.V0 - 0x6CD0u));
        c.V1 = mem.ReadU32((c.V0 + 0x8u));
        c.A2 = 0x801B0000u;
        c.A2 = mem.ReadU8((c.A2 + 0x25AEu));
        c.S0 = c.V0 + c.V1;
        c.At = 0x1F800000u;
        mem.WriteU32((c.At + 0x10u), c.S0);
        c.V0 = mem.ReadU32((c.S0 + 0x10u));
        c.A0 = c.S2 - 0x8u;
        mem.WriteU32((c.SP + 0x10u), c.V0);
        c.A3 = (uint)(short)mem.ReadU16(c.S2);
        c.A1 = 0x0020u;
        c.RA = 0x8003E164u;
        Game.func_800431E8(c, mem);
        if (c.V0 == 0u) {
            goto L8003E328;
        }
        c.V0 = 0x801B0000u;
        c.V0 = mem.ReadU32((c.V0 - 0x6E8Cu));
        c.T1 = 0x1F800000u;
        c.T1 = mem.ReadU32((c.T1 + 0x4Cu));
        c.A0 = 0x801B0000u;
        c.A0 = mem.ReadU32((c.A0 - 0x14F0u));
        c.A1 = 0x801B0000u;
        c.A1 = mem.ReadU32((c.A1 - 0x14ECu));
        c.A2 = 0x801B0000u;
        c.A2 = mem.ReadU32((c.A2 - 0x14E8u));
        c.V1 = 0x801A0000u;
        c.V1 = mem.ReadU32((c.V1 - 0x6E90u));
        c.At = 0x1F800000u;
        mem.WriteU32((c.At + 0x8u), c.V0);
        c.At = 0x1F800000u;
        mem.WriteU32((c.At + 0x78u), c.A0);
        c.At = 0x1F800000u;
        mem.WriteU32((c.At + 0x7Cu), c.A1);
        c.At = 0x1F800000u;
        mem.WriteU32((c.At + 0x80u), c.A2);
        c.V0 = mem.ReadU32((c.V1 + 0x8u));
        c.At = 0x1F800000u;
        mem.WriteU32((c.At + 0x14u), c.V0);
        c.V0 = mem.ReadU32((c.V1 + 0x4u));
        c.V1 = c.S1 - 0x42A8u;
        c.At = 0x1F800000u;
        mem.WriteU32((c.At + 0x44u), c.V1);
        c.At = 0x1F800000u;
        mem.WriteU16((c.At + 0x84u), (ushort)0u);
        c.At = 0x1F800000u;
        mem.WriteU32((c.At + 0x18u), c.V0);
        c.A3 = mem.ReadU32((c.S0 + 0x10u));
        if (RetainedModels.Submit(mem, 0, 0, 0, true, mem.ReadU32(c.SP + 0x4C) - 8, 0x8003DF50)) goto L8003E2FC;
        MoPose.Materialize(mem);
        c.V0 = mem.ReadU32((c.GP + 0xCCu));
        c.A3 = c.A3 - 0x1u;
        c.At = 0x1F800000u;
        mem.WriteU32((c.At + 0x64u), c.V0);
        if (c.A3 == c.S4) {
            c.T0 = c.V1;
            goto L8003E2F0;
        }
        c.T0 = c.V1;
        c.T3 = c.S1 - 0x280u;
        c.T2 = 0x1F0Fu;
        c.A1 = c.S1 - 0x42A2u;
        L8003E214: ;
        RecompOne.Runtime.Interrupts.Poll(c, mem);
        c.T4 = c.T1;
        { var _lw = mem.ReadU32(c.T4); RecompOne.Runtime.Gte.Write(0, _lw); }
        { var _lw = mem.ReadU32((c.T4 + 0x4u)); RecompOne.Runtime.Gte.Write(1, _lw); }
        RecompOne.Runtime.Gte.Rtps(12, false);
        c.T4 = c.T0;
        { var _sw = RecompOne.Runtime.Gte.Read(14); mem.WriteU32(c.T4, _sw); }
        c.V0 = c.T0 + 0x4u;
        c.T4 = c.V0;
        c.T5 = RecompOne.Runtime.Gte.Read(19);
        c.T5 = (uint)((int)c.T5 >> 2);
        mem.WriteU32(c.T4, c.T5);
        c.A0 = mem.ReadU32(c.T3);
        c.A2 = mem.ReadU32((c.T3 + 0x4u));
        if (c.A0 == c.A2) {
            c.V1 = (uint)((int)c.A0 >> 2);
            goto L8003E2A8;
        }
        c.V1 = (uint)((int)c.A0 >> 2);
        c.V0 = (uint)(short)mem.ReadU16((c.A1 - 0x2u));
        c.V0 = c.V0 - c.V1;
        c.V0 = c.V0 << 14;
        c.V1 = c.A2 - c.A0;
        { var _s = c.V0; var _t = c.V1; if (_t != 0u) { if ((int)_s == int.MinValue && (int)_t == -1) { c.LO = 0x80000000u; c.HI = 0u; } else { c.LO = (uint)((int)_s / (int)_t); c.HI = (uint)((int)_s % (int)_t); } } }
        if (c.V1 != 0u) {
            goto L8003E284;
        }
        Recompiled.Bios.Break(c, mem);
        L8003E284: ;
        c.At = 0xFFFFFFFFu;
        if (c.V1 != c.At) {
            c.At = 0x80000000u;
            goto L8003E29C;
        }
        c.At = 0x80000000u;
        if (c.V0 != c.At) {
            goto L8003E29C;
        }
        Recompiled.Bios.Break(c, mem);
        L8003E29C: ;
        c.V0 = c.LO;
        mem.WriteU16(c.A1, (ushort)c.V0);
        goto L8003E2AC;
        L8003E2A8: ;
        mem.WriteU16(c.A1, (ushort)0u);
        L8003E2AC: ;
        c.V0 = (uint)(short)mem.ReadU16(c.A1);
        c.V0 = (int)c.V0 < 7952 ? 1u : 0u;
        if (c.V0 != 0u) {
            goto L8003E2C8;
        }
        mem.WriteU16(c.A1, (ushort)c.T2);
        mem.WriteU16((c.A1 - 0x2u), (ushort)c.T2);
        L8003E2C8: ;
        c.V0 = (uint)(short)mem.ReadU16(c.A1);
        if ((int)c.V0 >= 0) {
            c.T0 = c.T0 + 0x8u;
            goto L8003E2DC;
        }
        c.T0 = c.T0 + 0x8u;
        mem.WriteU16(c.A1, (ushort)0u);
        L8003E2DC: ;
        c.A1 = c.A1 + 0x8u;
        c.A3 = c.A3 - 0x1u;
        c.V0 = 0xFFFFFFFFu;
        if (c.A3 != c.V0) {
            c.T1 = c.T1 + 0x8u;
            goto L8003E214;
        }
        c.T1 = c.T1 + 0x8u;
        L8003E2F0: ;
        c.A0 = 0u;
        c.A1 = 0x0018u;
        c.RA = 0x8003E2FCu;
        Game.func_80035CA4(c, mem);
        L8003E2FC: ;
        c.V1 = 0x801A0000u;
        c.V1 = mem.ReadU32((c.V1 - 0x6E90u));
        c.V0 = mem.ReadU32((c.S3 + 0x14u));
        mem.WriteU32((c.V1 + 0x8u), c.V0);
        c.V0 = mem.ReadU32((c.S3 + 0x78u));
        c.V1 = mem.ReadU32((c.S3 + 0x7Cu));
        c.At = 0x801B0000u;
        mem.WriteU32((c.At - 0x14F0u), c.V0);
        c.At = 0x801B0000u;
        mem.WriteU32((c.At - 0x14ECu), c.V1);
        L8003E328: ;
        c.RA = mem.ReadU32((c.SP + 0x4Cu));
        c.S4 = mem.ReadU32((c.SP + 0x48u));
        c.S3 = mem.ReadU32((c.SP + 0x44u));
        c.S2 = mem.ReadU32((c.SP + 0x40u));
        c.S1 = mem.ReadU32((c.SP + 0x3Cu));
        c.S0 = mem.ReadU32((c.SP + 0x38u));
        c.SP = c.SP + 0x50u;
        return;
    }
}
