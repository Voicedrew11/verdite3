using RecompOne.Runtime;
using RecompOne.Runtime.Dispatch;
using RecompOne.Runtime.Context;
using RecompOne.Runtime.Memory;
using Game = Recompiled.KingsField3_game;

namespace Kf3;

public static partial class NativeScene
{
    // Literal reference for func_8003BE34; see docs/GPU_RENDERER.md.
    static void RunCell(CpuContext c, PSMemory mem)
    {

        c.SP = c.SP - 0x20u;
        c.T0 = c.A0;
        c.T1 = c.A1;
        c.T3 = 0x1F800000u;
        c.T3 = c.T3 | 0x0100u;
        c.T2 = 0x1F800000u;
        c.V1 = c.T1 << 1;
        c.V1 = c.V1 + c.T1;
        c.V1 = c.V1 << 3;
        c.V1 = c.V1 + c.T1;
        c.V1 = c.V1 << 5;
        c.V0 = c.T0 << 2;
        c.V0 = c.V0 + c.T0;
        c.V0 = c.V0 << 1;
        c.A0 = 0x801D0000u;
        c.A0 = c.A0 + 0x4464u;
        c.V0 = c.V0 + c.A0;
        mem.WriteU32((c.SP + 0x10u), c.S0);
        c.S0 = c.V1 + c.V0;
        c.A3 = c.A2;
        c.A2 = c.A2 & 0x0002u;
        mem.WriteU32((c.SP + 0x18u), c.RA);
        mem.WriteU32((c.SP + 0x14u), c.S1);
        c.V1 = mem.ReadU8(c.S0);
        if (c.A2 == 0u) {
            c.T2 = c.T2 | 0x0108u;
            goto L8003BF50;
        }
        c.T2 = c.T2 | 0x0108u;
        c.V0 = (int)c.V1 < 240 ? 1u : 0u;
        if (c.V0 == 0u) {
            c.A0 = c.S0;
            goto L8003BF50;
        }
        c.A0 = c.S0;
        c.A1 = 0x1F800000u;
        c.A1 = c.A1 | 0x0100u;
        c.S1 = c.A3 & 0x00FFu;
        c.V1 = mem.ReadU16(c.T2);
        c.V0 = c.T0 << 11;
        c.V0 = c.V0 - c.V1;
        c.V0 = c.V0 + 0x400u;
        mem.WriteU16(c.T3, (ushort)c.V0);
        c.V0 = 0x1F800000u;
        c.V0 = mem.ReadU16((c.V0 + 0x110u));
        c.V1 = c.T1 << 11;
        c.V1 = c.V1 - c.V0;
        c.V0 = mem.ReadU8((c.S0 + 0x1u));
        c.V1 = c.V1 + 0x400u;
        c.At = 0x1F800000u;
        mem.WriteU16((c.At + 0x104u), (ushort)c.V1);
        c.V1 = 0x1F800000u;
        c.V1 = mem.ReadU16((c.V1 + 0x10Cu));
        c.V0 = 0u - c.V0;
        c.V0 = c.V0 << 7;
        c.V0 = c.V0 - c.V1;
        c.At = 0x1F800000u;
        mem.WriteU16((c.At + 0x102u), (ushort)c.V0);
        c.A2 = c.S1;
        c.RA = 0x8003BF0Cu;
        Game.func_8003BB04(c, mem);
        c.V1 = mem.ReadU8((c.S0 + 0x5u));
        c.V0 = (int)c.V1 < 240 ? 1u : 0u;
        if (c.V0 == 0u) {
            c.A0 = c.S0 + 0x5u;
            goto L8003BFB8;
        }
        c.A0 = c.S0 + 0x5u;
        c.A1 = 0x1F800000u;
        c.A1 = c.A1 | 0x0100u;
        c.V0 = mem.ReadU8((c.S0 + 0x6u));
        c.V1 = 0x1F800000u;
        c.V1 = mem.ReadU16((c.V1 + 0x10Cu));
        c.V0 = 0u - c.V0;
        c.V0 = c.V0 << 7;
        c.V0 = c.V0 - c.V1;
        c.At = 0x1F800000u;
        mem.WriteU16((c.At + 0x102u), (ushort)c.V0);
        c.A2 = c.S1;
        goto L8003BFB0;
        L8003BF50: ;
        c.V1 = mem.ReadU8((c.S0 + 0x5u));
        c.V0 = c.A3 & 0x0002u;
        if (c.V0 == 0u) {
            c.V0 = (int)c.V1 < 240 ? 1u : 0u;
            goto L8003BFB8;
        }
        c.V0 = (int)c.V1 < 240 ? 1u : 0u;
        if (c.V0 == 0u) {
            c.A0 = c.S0 + 0x5u;
            goto L8003BFB8;
        }
        c.A0 = c.S0 + 0x5u;
        c.A1 = c.T3;
        c.A2 = c.A3 & 0x00FFu;
        c.V1 = mem.ReadU16(c.T2);
        c.V0 = c.T0 << 11;
        c.V0 = c.V0 - c.V1;
        c.V0 = c.V0 + 0x400u;
        mem.WriteU16(c.A1, (ushort)c.V0);
        c.V0 = mem.ReadU16((c.T2 + 0x8u));
        c.V1 = c.T1 << 11;
        c.V1 = c.V1 - c.V0;
        c.V0 = mem.ReadU8((c.S0 + 0x6u));
        c.V1 = c.V1 + 0x400u;
        mem.WriteU16((c.A1 + 0x4u), (ushort)c.V1);
        c.V1 = mem.ReadU16((c.T2 + 0x4u));
        c.V0 = 0u - c.V0;
        c.V0 = c.V0 << 7;
        c.V0 = c.V0 - c.V1;
        mem.WriteU16((c.A1 + 0x2u), (ushort)c.V0);
        L8003BFB0: ;
        c.RA = 0x8003BFB8u;
        Game.func_8003BB04(c, mem);
        L8003BFB8: ;
        c.RA = mem.ReadU32((c.SP + 0x18u));
        c.S1 = mem.ReadU32((c.SP + 0x14u));
        c.S0 = mem.ReadU32((c.SP + 0x10u));
        c.SP = c.SP + 0x20u;
        return;
    }
}
