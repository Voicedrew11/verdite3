using RecompOne.Runtime;
using RecompOne.Runtime.Dispatch;
using RecompOne.Runtime.Context;
using RecompOne.Runtime.Memory;
using Game = Recompiled.KingsField3_game;

namespace Kf3;

public static partial class NativeScene
{
    // Literal reference for func_8003BFD0; see docs/GPU_RENDERER.md.
    static void RunWalk(CpuContext c, PSMemory mem)
    {

        c.SP = c.SP - 0x58u;
        mem.WriteU32((c.SP + 0x38u), c.S2);
        c.S2 = 0x1F800000u;
        c.S2 = c.S2 | 0x0100u;
        mem.WriteU32((c.SP + 0x40u), c.S4);
        c.S4 = 0x1F800000u;
        c.S4 = c.S4 | 0x0108u;
        c.A0 = 0x1F800000u;
        c.A0 = c.A0 | 0x0120u;
        c.T0 = 0x801B0000u;
        c.T0 = c.T0 - 0x6D64u;
        c.A1 = c.T0 + 0x59E8u;
        mem.WriteU32((c.SP + 0x54u), c.RA);
        mem.WriteU32((c.SP + 0x50u), c.FP);
        mem.WriteU32((c.SP + 0x4Cu), c.S7);
        mem.WriteU32((c.SP + 0x48u), c.S6);
        mem.WriteU32((c.SP + 0x44u), c.S5);
        mem.WriteU32((c.SP + 0x3Cu), c.S3);
        mem.WriteU32((c.SP + 0x34u), c.S1);
        mem.WriteU32((c.SP + 0x30u), c.S0);
        c.V1 = mem.ReadU32(c.T0);
        c.V0 = 0x801B0000u;
        c.V0 = mem.ReadU32((c.V0 - 0x6E8Cu));
        c.A3 = 0x801A0000u;
        c.A3 = mem.ReadU32((c.A3 - 0x6E90u));
        c.T1 = 0x801B0000u;
        c.T1 = mem.ReadU32((c.T1 - 0x13A8u));
        c.T2 = 0x801B0000u;
        c.T2 = mem.ReadU32((c.T2 - 0x14F0u));
        c.T3 = 0x801B0000u;
        c.T3 = mem.ReadU32((c.T3 - 0x14ECu));
        c.T4 = 0x801B0000u;
        c.T4 = mem.ReadU32((c.T4 - 0x14E8u));
        c.V0 = c.V0 + 0x3C0u;
        c.At = 0x1F800000u;
        mem.WriteU32((c.At + 0x8u), c.V0);
        c.V0 = mem.ReadU32((c.GP + 0xCCu));
        c.A2 = 0x009Cu;
        c.At = 0x1F800000u;
        mem.WriteU32((c.At + 0x10u), c.V1);
        c.V1 = mem.ReadU32((c.A3 + 0x8u));
        c.S1 = 0x1F800000u;
        c.At = 0x1F800000u;
        mem.WriteU32((c.At + 0x14u), c.V1);
        c.T5 = mem.ReadU32((c.A3 + 0x4u));
        c.At = 0x1F800000u;
        mem.WriteU32((c.At + 0x54u), c.V0);
        c.V0 = 0x0064u;
        c.At = 0x1F800000u;
        mem.WriteU16((c.At + 0x66u), (ushort)c.V0);
        c.V0 = 0x801B0000u;
        c.V0 = mem.ReadU32((c.V0 - 0x13B4u));
        c.V1 = 0x801B0000u;
        c.V1 = mem.ReadU32((c.V1 - 0x13B0u));
        c.A3 = 0x801B0000u;
        c.A3 = mem.ReadU32((c.A3 - 0x13ACu));
        c.T0 = c.T0 + 0x19B8u;
        c.At = 0x1F800000u;
        mem.WriteU32((c.At + 0x44u), c.T0);
        mem.WriteU32(c.S4, c.V0);
        c.At = 0x1F800000u;
        mem.WriteU32((c.At + 0x10Cu), c.V1);
        c.At = 0x1F800000u;
        mem.WriteU32((c.At + 0x110u), c.A3);
        c.At = 0x1F800000u;
        mem.WriteU32((c.At + 0x114u), c.T1);
        c.At = 0x1F800000u;
        mem.WriteU32((c.At + 0x68u), c.T2);
        c.At = 0x1F800000u;
        mem.WriteU32((c.At + 0x6Cu), c.T3);
        c.At = 0x1F800000u;
        mem.WriteU32((c.At + 0x70u), c.T4);
        c.At = 0x1F800000u;
        mem.WriteU32((c.At + 0x18u), c.T5);
        c.S1 = c.S1 | 0x0120u;
        c.RA = 0x8003C100u;
        Game.func_80018F5C(c, mem);
        c.V0 = 0x801B0000u;
        c.V0 = mem.ReadU32((c.V0 - 0x1388u));
        c.V1 = 0x801B0000u;
        c.V1 = mem.ReadU32((c.V1 - 0x138Cu));
        c.T6 = 0x0019u;
        mem.WriteU32((c.SP + 0x10u), c.T6);
        c.FP = c.V0;
        c.T6 = c.FP << 11;
        c.V0 = c.FP << 1;
        c.V0 = c.V0 + c.FP;
        c.V0 = c.V0 << 3;
        c.V0 = c.V0 + c.FP;
        c.V0 = c.V0 << 5;
        mem.WriteU32((c.SP + 0x20u), c.T6);
        mem.WriteU32((c.SP + 0x28u), c.V0);
        c.At = 0x1F800000u;
        mem.WriteU32((c.At + 0x118u), c.V1);
        c.At = 0x1F800000u;
        mem.WriteU32((c.At + 0x11Cu), c.FP);
        L8003C14C: ;
        RecompOne.Runtime.Interrupts.Poll(c, mem);
        c.V0 = c.FP < 0x00000050u ? 1u : 0u;
        if (c.V0 == 0u) {
            c.S7 = 0x0019u;
            goto L8003C2BC;
        }
        c.S7 = 0x0019u;
        c.S3 = 0x1F800000u;
        c.S3 = mem.ReadU32((c.S3 + 0x118u));
        c.T6 = mem.ReadU32((c.SP + 0x28u));
        c.S5 = c.S3 << 11;
        c.V0 = c.S3 << 2;
        c.V0 = c.V0 + c.S3;
        c.S6 = c.V0 << 1;
        mem.WriteU32((c.SP + 0x18u), c.T6);
        L8003C178: ;
        RecompOne.Runtime.Interrupts.Poll(c, mem);
        c.V0 = c.S3 < 0x00000050u ? 1u : 0u;
        if (c.V0 == 0u) {
            goto L8003C29C;
        }
        c.V0 = mem.ReadU8(c.S1);
        if (c.V0 == 0u) {
            goto L8003C29C;
        }
        c.V0 = 0x801D0000u;
        c.V0 = c.V0 + 0x4464u;
        c.T6 = mem.ReadU32((c.SP + 0x18u));
        c.V0 = c.S6 + c.V0;
        c.S0 = c.T6 + c.V0;
        c.V0 = mem.ReadU8(c.S1);
        c.V1 = mem.ReadU8(c.S0);
        c.V0 = c.V0 & 0x0002u;
        if (c.V0 == 0u) {
            c.V0 = (int)c.V1 < 240 ? 1u : 0u;
            goto L8003C238;
        }
        c.V0 = (int)c.V1 < 240 ? 1u : 0u;
        if (c.V0 == 0u) {
            c.A0 = c.S0;
            goto L8003C238;
        }
        c.A0 = c.S0;
        c.V0 = mem.ReadU16(c.S4);
        c.V1 = mem.ReadU16((c.S4 + 0x8u));
        c.V0 = c.S5 - c.V0;
        c.V0 = c.V0 + 0x400u;
        mem.WriteU16(c.S2, (ushort)c.V0);
        c.T6 = mem.ReadU32((c.SP + 0x20u));
        c.V0 = mem.ReadU8((c.S0 + 0x1u));
        c.V1 = c.T6 - c.V1;
        c.V1 = c.V1 + 0x400u;
        c.V0 = 0u - c.V0;
        mem.WriteU16((c.S2 + 0x4u), (ushort)c.V1);
        c.V1 = mem.ReadU16((c.S4 + 0x4u));
        c.V0 = c.V0 << 7;
        c.V0 = c.V0 - c.V1;
        mem.WriteU16((c.S2 + 0x2u), (ushort)c.V0);
        c.A2 = mem.ReadU8(c.S1);
        c.A1 = c.S2;
        c.RA = 0x8003C20Cu;
        Game.func_8003BB04(c, mem);
        c.V0 = mem.ReadU8(c.S1);
        c.V1 = mem.ReadU8((c.S0 + 0x5u));
        c.V0 = c.V0 & 0x0002u;
        if (c.V0 == 0u) {
            c.V0 = (int)c.V1 < 240 ? 1u : 0u;
            goto L8003C29C;
        }
        c.V0 = (int)c.V1 < 240 ? 1u : 0u;
        if (c.V0 == 0u) {
            c.A0 = c.S0 + 0x5u;
            goto L8003C29C;
        }
        c.A0 = c.S0 + 0x5u;
        c.V0 = mem.ReadU8((c.S0 + 0x6u));
        c.V1 = mem.ReadU16((c.S4 + 0x4u));
        c.V0 = 0u - c.V0;
        goto L8003C284;
        L8003C238: ;
        c.V0 = mem.ReadU8(c.S1);
        c.V1 = mem.ReadU8((c.S0 + 0x5u));
        c.V0 = c.V0 & 0x0002u;
        if (c.V0 == 0u) {
            c.V0 = (int)c.V1 < 240 ? 1u : 0u;
            goto L8003C29C;
        }
        c.V0 = (int)c.V1 < 240 ? 1u : 0u;
        if (c.V0 == 0u) {
            c.A0 = c.S0 + 0x5u;
            goto L8003C29C;
        }
        c.A0 = c.S0 + 0x5u;
        c.V0 = mem.ReadU16(c.S4);
        c.V1 = mem.ReadU16((c.S4 + 0x8u));
        c.V0 = c.S5 - c.V0;
        c.V0 = c.V0 + 0x400u;
        mem.WriteU16(c.S2, (ushort)c.V0);
        c.T6 = mem.ReadU32((c.SP + 0x20u));
        c.V0 = mem.ReadU8((c.S0 + 0x6u));
        c.V1 = c.T6 - c.V1;
        c.V1 = c.V1 + 0x400u;
        c.V0 = 0u - c.V0;
        mem.WriteU16((c.S2 + 0x4u), (ushort)c.V1);
        c.V1 = mem.ReadU16((c.S4 + 0x4u));
        L8003C284: ;
        c.V0 = c.V0 << 7;
        c.V0 = c.V0 - c.V1;
        mem.WriteU16((c.S2 + 0x2u), (ushort)c.V0);
        c.A2 = mem.ReadU8(c.S1);
        c.A1 = c.S2;
        c.RA = 0x8003C29Cu;
        Game.func_8003BB04(c, mem);
        L8003C29C: ;
        c.S5 = c.S5 + 0x800u;
        c.S6 = c.S6 + 0xAu;
        c.S3 = c.S3 + 0x1u;
        c.S7 = c.S7 - 0x1u;
        if (c.S7 != 0u) {
            c.S1 = c.S1 + 0x1u;
            goto L8003C178;
        }
        c.S1 = c.S1 + 0x1u;
        goto L8003C2C0;
        L8003C2BC: ;
        c.S1 = c.S1 + 0x19u;
        L8003C2C0: ;
        c.T6 = mem.ReadU32((c.SP + 0x20u));
        c.T6 = c.T6 + 0x800u;
        mem.WriteU32((c.SP + 0x20u), c.T6);
        c.T6 = mem.ReadU32((c.SP + 0x28u));
        c.T6 = c.T6 + 0x320u;
        mem.WriteU32((c.SP + 0x28u), c.T6);
        c.T6 = mem.ReadU32((c.SP + 0x10u));
        c.FP = c.FP + 0x1u;
        c.T6 = c.T6 - 0x1u;
        if (c.T6 != 0u) {
            mem.WriteU32((c.SP + 0x10u), c.T6);
            goto L8003C14C;
        }
        mem.WriteU32((c.SP + 0x10u), c.T6);
        c.V1 = 0x1F800000u;
        c.V1 = mem.ReadU32((c.V1 + 0x68u));
        c.A0 = 0x1F800000u;
        c.A0 = mem.ReadU32((c.A0 + 0x6Cu));
        c.A1 = 0x801A0000u;
        c.A1 = mem.ReadU32((c.A1 - 0x6E90u));
        c.V0 = 0x1F800000u;
        c.V0 = mem.ReadU32((c.V0 + 0x14u));
        c.At = 0x801B0000u;
        mem.WriteU32((c.At - 0x14F0u), c.V1);
        c.At = 0x801B0000u;
        mem.WriteU32((c.At - 0x14ECu), c.A0);
        mem.WriteU32((c.A1 + 0x8u), c.V0);
        c.RA = mem.ReadU32((c.SP + 0x54u));
        c.FP = mem.ReadU32((c.SP + 0x50u));
        c.S7 = mem.ReadU32((c.SP + 0x4Cu));
        c.S6 = mem.ReadU32((c.SP + 0x48u));
        c.S5 = mem.ReadU32((c.SP + 0x44u));
        c.S4 = mem.ReadU32((c.SP + 0x40u));
        c.S3 = mem.ReadU32((c.SP + 0x3Cu));
        c.S2 = mem.ReadU32((c.SP + 0x38u));
        c.S1 = mem.ReadU32((c.SP + 0x34u));
        c.S0 = mem.ReadU32((c.SP + 0x30u));
        c.SP = c.SP + 0x58u;
        return;
    }
}
