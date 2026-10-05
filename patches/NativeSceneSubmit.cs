using RecompOne.Runtime;
using RecompOne.Runtime.Dispatch;
using RecompOne.Runtime.Context;
using RecompOne.Runtime.Memory;
using Game = Recompiled.KingsField3_game;

namespace Kf3;

public static partial class NativeScene
{
    // Literal reference for func_8003E34C; see docs/GPU_RENDERER.md.
    static void RunSubmit(CpuContext c, PSMemory mem)
    {

        c.SP = c.SP - 0x118u;
        mem.WriteU32((c.SP + 0x104u), c.S5);
        c.S5 = mem.ReadU32((c.SP + 0x148u));
        c.T8 = mem.ReadU32((c.SP + 0x128u));
        mem.WriteU32((c.SP + 0x110u), c.FP);
        c.FP = mem.ReadU32((c.SP + 0x130u));
        c.T1 = mem.ReadU32((c.SP + 0x144u));
        c.T0 = c.A2;
        mem.WriteU32((c.SP + 0xF0u), c.S0);
        c.S0 = mem.ReadU8((c.SP + 0x13Cu));
        c.T3 = c.A3;
        mem.WriteU32((c.SP + 0x10Cu), c.S7);
        c.S7 = mem.ReadU16((c.SP + 0x140u));
        c.V1 = 0x801B0000u;
        c.V1 = mem.ReadU32((c.V1 - 0x14F0u));
        c.A2 = 0x801B0000u;
        c.A2 = mem.ReadU32((c.A2 - 0x14ECu));
        c.A3 = 0x801B0000u;
        c.A3 = mem.ReadU32((c.A3 - 0x14E8u));
        mem.WriteU32((c.SP + 0xD0u), c.T8);
        c.T8 = mem.ReadU32((c.SP + 0x12Cu));
        c.V0 = 0x0064u;
        mem.WriteU32((c.SP + 0xD8u), c.T8);
        c.T8 = mem.ReadU16((c.SP + 0x134u));
        c.T2 = 0x801B0000u;
        c.T2 = c.T2 - 0x53ACu;
        mem.WriteU32((c.SP + 0x100u), c.S4);
        mem.WriteU16((c.SP + 0xE0u), (ushort)c.T8);
        c.T8 = mem.ReadU16((c.SP + 0x138u));
        c.S4 = c.SP + 0x40u;
        mem.WriteU32((c.SP + 0xFCu), c.S3);
        c.At = 0x1F800000u;
        mem.WriteU16((c.At + 0x76u), (ushort)c.V0);
        c.V0 = 0x801A0000u;
        c.V0 = mem.ReadU32((c.V0 - 0x6E90u));
        c.S3 = 0x1F800000u;
        mem.WriteU32((c.SP + 0x114u), c.RA);
        mem.WriteU32((c.SP + 0x108u), c.S6);
        mem.WriteU32((c.SP + 0xF8u), c.S2);
        mem.WriteU32((c.SP + 0xF4u), c.S1);
        c.At = 0x1F800000u;
        mem.WriteU32((c.At + 0x44u), c.T2);
        c.At = 0x1F800000u;
        mem.WriteU32((c.At + 0x48u), c.T2);
        c.At = 0x1F800000u;
        mem.WriteU32((c.At + 0x78u), c.V1);
        c.At = 0x1F800000u;
        mem.WriteU32((c.At + 0x7Cu), c.A2);
        c.At = 0x1F800000u;
        mem.WriteU32((c.At + 0x80u), c.A3);
        mem.WriteU16((c.SP + 0xE8u), (ushort)c.T8);
        c.A2 = mem.ReadU32((c.V0 + 0x8u));
        c.S6 = c.T1;
        c.At = 0x1F800000u;
        mem.WriteU32((c.At + 0x14u), c.A2);
        c.V1 = mem.ReadU32((c.GP + 0xCCu));
        c.V0 = mem.ReadU32((c.V0 + 0x4u));
        mem.WriteU16((c.SP + 0xC8u), (ushort)c.A1);
        c.At = 0x1F800000u;
        mem.WriteU32((c.At + 0x18u), c.V0);
        c.V0 = c.V0 - 0x7800u;
        c.V0 = c.V0 < c.A2 ? 1u : 0u;
        c.At = 0x1F800000u;
        mem.WriteU32((c.At + 0x64u), c.V1);
        if (c.V0 == 0u) {
            c.A3 = c.A0;
            goto L8003E458;
        }
        c.A3 = c.A0;
        c.S6 = c.T1 & 0x00BFu;
        L8003E458: ;
        c.T8 = 0x801B0000u;
        c.T8 = c.T8 - 0x14B4u;
        c.T4 = c.T8;
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
        c.T4 = c.T8;
        c.T5 = mem.ReadU32((c.T4 + 0x14u));
        c.T6 = mem.ReadU32((c.T4 + 0x18u));
        RecompOne.Runtime.Gte.WriteControl(5, c.T5);
        c.T7 = mem.ReadU32((c.T4 + 0x1Cu));
        RecompOne.Runtime.Gte.WriteControl(6, c.T6);
        RecompOne.Runtime.Gte.WriteControl(7, c.T7);
        if (c.FP == 0u) {
            goto L8003E600;
        }
        c.V0 = mem.ReadU32(c.T0);
        c.V1 = 0x801B0000u;
        c.V1 = mem.ReadU32((c.V1 - 0x13B4u));
        c.V0 = c.V0 - c.V1;
        mem.WriteU32((c.SP + 0x20u), c.V0);
        c.V0 = mem.ReadU32((c.T0 + 0x4u));
        c.V1 = 0x801B0000u;
        c.V1 = mem.ReadU32((c.V1 - 0x13B0u));
        c.V0 = c.V0 - c.V1;
        mem.WriteU32((c.SP + 0x24u), c.V0);
        c.V0 = mem.ReadU32((c.T0 + 0x8u));
        c.V1 = 0x801B0000u;
        c.V1 = mem.ReadU32((c.V1 - 0x13ACu));
        c.V0 = c.V0 - c.V1;
        mem.WriteU32((c.SP + 0x28u), c.V0);
        c.V0 = mem.ReadU16(c.T0);
        c.V1 = 0x801B0000u;
        c.V1 = mem.ReadU16((c.V1 - 0x13B4u));
        c.V0 = c.V0 - c.V1;
        mem.WriteU16((c.SP + 0x18u), (ushort)c.V0);
        c.V0 = mem.ReadU16((c.T0 + 0x4u));
        c.V1 = 0x801B0000u;
        c.V1 = mem.ReadU16((c.V1 - 0x13B0u));
        c.V0 = c.V0 - c.V1;
        mem.WriteU16((c.SP + 0x1Au), (ushort)c.V0);
        c.V0 = mem.ReadU16((c.T0 + 0x8u));
        c.V1 = 0x801B0000u;
        c.V1 = mem.ReadU16((c.V1 - 0x13ACu));
        c.V0 = c.V0 - c.V1;
        mem.WriteU16((c.SP + 0x1Cu), (ushort)c.V0);
        c.V0 = c.SP + 0x18u;
        c.T4 = c.V0;
        { var _lw = mem.ReadU32(c.T4); RecompOne.Runtime.Gte.Write(0, _lw); }
        { var _lw = mem.ReadU32((c.T4 + 0x4u)); RecompOne.Runtime.Gte.Write(1, _lw); }
        RecompOne.Runtime.Gte.MvmvaOp(12, false, 0, 0, 0);
        c.V0 = c.SP + 0x54u;
        c.T4 = c.V0;
        { var _sw = RecompOne.Runtime.Gte.Read(25); mem.WriteU32(c.T4, _sw); }
        { var _sw = RecompOne.Runtime.Gte.Read(26); mem.WriteU32((c.T4 + 0x4u), _sw); }
        { var _sw = RecompOne.Runtime.Gte.Read(27); mem.WriteU32((c.T4 + 0x8u), _sw); }
        c.V0 = c.SP + 0xC0u;
        c.T4 = c.V0;
        c.T5 = RecompOne.Runtime.Gte.ReadControl(31);
        mem.WriteU32(c.T4, c.T5);
        c.V0 = mem.ReadU32((c.T0 + 0x8u));
        c.A0 = mem.ReadU32(c.T0);
        c.V0 = (uint)((int)c.V0 >> 11);
        c.V1 = c.V0 << 1;
        c.V1 = c.V1 + c.V0;
        c.V1 = c.V1 << 3;
        c.V1 = c.V1 + c.V0;
        c.V1 = c.V1 << 5;
        c.V0 = 0x801D0000u;
        c.V0 = c.V0 + 0x4464u;
        c.V1 = c.V1 + c.V0;
        c.A0 = (uint)((int)c.A0 >> 11);
        c.V0 = c.A0 << 2;
        c.V0 = c.V0 + c.A0;
        c.V0 = c.V0 << 1;
        c.A0 = c.V1 + c.V0;
        c.V1 = c.A3 & 0x00FFu;
        c.V0 = 0x0001u;
        if (c.V1 == c.V0) {
            goto L8003E5D8;
        }
        c.A0 = c.A0 + 0x5u;
        L8003E5D8: ;
        c.V0 = mem.ReadU8((c.A0 + 0x4u));
        c.V0 = c.V0 & 0x003Fu;
        c.V1 = c.V0 << 3;
        c.V1 = c.V1 - c.V0;
        c.V1 = c.V1 << 2;
        c.V1 = c.V1 - c.V0;
        c.V1 = c.V1 << 2;
        c.V0 = c.T2 + 0x42A8u;
        goto L8003E690;
        L8003E600: ;
        c.V0 = mem.ReadU32(c.T0);
        c.V1 = 0x801B0000u;
        c.V1 = mem.ReadU32((c.V1 - 0x13ACu));
        c.A0 = 0x801B0000u;
        c.A0 = mem.ReadU32((c.A0 - 0x13B4u));
        mem.WriteU32((c.SP + 0x54u), c.V0);
        c.V0 = mem.ReadU32((c.T0 + 0x4u));
        c.V1 = (uint)((int)c.V1 >> 11);
        mem.WriteU32((c.SP + 0x58u), c.V0);
        c.V0 = mem.ReadU32((c.T0 + 0x8u));
        c.A0 = (uint)((int)c.A0 >> 11);
        mem.WriteU32((c.SP + 0x5Cu), c.V0);
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
        c.V1 = mem.ReadU8(c.At);
        c.V1 = c.V1 & 0x003Fu;
        c.V0 = c.V1 << 3;
        c.V0 = c.V0 - c.V1;
        c.V0 = c.V0 << 2;
        c.V0 = c.V0 - c.V1;
        c.V0 = c.V0 << 2;
        c.V1 = c.T2 + 0x42A8u;
        L8003E690: ;
        c.S1 = c.V0 + c.V1;
        c.V0 = c.S6 & 0x0080u;
        if (c.V0 == 0u) {
            goto L8003E6A8;
        }
        c.S6 = c.S6 & 0x007Fu;
        goto L8003E6BC;
        L8003E6A8: ;
        c.V0 = (uint)(short)mem.ReadU16((c.SP + 0x1Au));
        if ((int)c.V0 > 0) {
            c.A0 = c.T3;
            goto L8003E6C0;
        }
        c.A0 = c.T3;
        c.S5 = c.S5 + 0xF0u;
        L8003E6BC: ;
        c.A0 = c.T3;
        L8003E6C0: ;
        c.A1 = c.S4;
        c.RA = 0x8003E6C8u;
        Game.func_800166F4(c, mem);
        c.V1 = c.S0 & 0x00FFu;
        c.V0 = 0x00FFu;
        if (c.V1 == c.V0) {
            c.V0 = c.V1 << 3;
            goto L8003EAC0;
        }
        c.V0 = c.V1 << 3;
        c.V0 = c.V0 - c.V1;
        c.V0 = c.V0 << 2;
        c.V0 = c.V0 - c.V1;
        c.V0 = c.V0 << 2;
        c.V1 = 0x801B0000u;
        c.V1 = c.V1 - 0x1104u;
        c.S2 = c.V0 + c.V1;
        c.V1 = (uint)(short)mem.ReadU16((c.S2 + 0x50u));
        c.V0 = 0xFFFFFFFFu;
        if (c.V1 == c.V0) {
            c.A0 = c.S1 + 0x50u;
            goto L8003E750;
        }
        c.A0 = c.S1 + 0x50u;
        c.A1 = c.S2 + 0x50u;
        c.S0 = c.SP + 0xA0u;
        c.A2 = c.S0;
        c.A3 = c.S7 << 16;
        c.A3 = (uint)((int)c.A3 >> 16);
        c.RA = 0x8003E71Cu;
        Game.func_800171B0(c, mem);
        c.T4 = c.S0;
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
        goto L8003E780;
        L8003E750: ;
        c.V0 = c.S1 + 0x50u;
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
        L8003E780: ;
        c.V1 = (uint)(short)mem.ReadU16(c.S2);
        c.V0 = 0xFFFFFFFFu;
        if (c.V1 == c.V0) {
            goto L8003E8A4;
        }
        c.T4 = c.S2;
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
        c.T4 = c.S4;
        c.T5 = mem.ReadU16(c.T4);
        c.T6 = mem.ReadU16((c.T4 + 0x6u));
        c.T7 = mem.ReadU16((c.T4 + 0xCu));
        RecompOne.Runtime.Gte.Write(9, c.T5);
        RecompOne.Runtime.Gte.Write(10, c.T6);
        RecompOne.Runtime.Gte.Write(11, c.T7);
        RecompOne.Runtime.Gte.MvmvaOp(12, false, 0, 3, 3);
        c.V0 = c.SP + 0x80u;
        c.T4 = c.V0;
        c.T5 = RecompOne.Runtime.Gte.Read(9);
        c.T6 = RecompOne.Runtime.Gte.Read(10);
        c.T7 = RecompOne.Runtime.Gte.Read(11);
        mem.WriteU16(c.T4, (ushort)c.T5);
        mem.WriteU16((c.T4 + 0x6u), (ushort)c.T6);
        mem.WriteU16((c.T4 + 0xCu), (ushort)c.T7);
        c.V0 = c.S4 + 0x2u;
        c.T4 = c.V0;
        c.T5 = mem.ReadU16(c.T4);
        c.T6 = mem.ReadU16((c.T4 + 0x6u));
        c.T7 = mem.ReadU16((c.T4 + 0xCu));
        RecompOne.Runtime.Gte.Write(9, c.T5);
        RecompOne.Runtime.Gte.Write(10, c.T6);
        RecompOne.Runtime.Gte.Write(11, c.T7);
        RecompOne.Runtime.Gte.MvmvaOp(12, false, 0, 3, 3);
        c.V0 = c.SP + 0x82u;
        c.T4 = c.V0;
        c.T5 = RecompOne.Runtime.Gte.Read(9);
        c.T6 = RecompOne.Runtime.Gte.Read(10);
        c.T7 = RecompOne.Runtime.Gte.Read(11);
        mem.WriteU16(c.T4, (ushort)c.T5);
        mem.WriteU16((c.T4 + 0x6u), (ushort)c.T6);
        mem.WriteU16((c.T4 + 0xCu), (ushort)c.T7);
        c.V0 = c.S4 + 0x4u;
        c.T4 = c.V0;
        c.T5 = mem.ReadU16(c.T4);
        c.T6 = mem.ReadU16((c.T4 + 0x6u));
        c.T7 = mem.ReadU16((c.T4 + 0xCu));
        RecompOne.Runtime.Gte.Write(9, c.T5);
        RecompOne.Runtime.Gte.Write(10, c.T6);
        RecompOne.Runtime.Gte.Write(11, c.T7);
        RecompOne.Runtime.Gte.MvmvaOp(12, false, 0, 3, 3);
        c.V0 = c.SP + 0x84u;
        c.T4 = c.V0;
        c.T5 = RecompOne.Runtime.Gte.Read(9);
        c.T6 = RecompOne.Runtime.Gte.Read(10);
        c.T7 = RecompOne.Runtime.Gte.Read(11);
        mem.WriteU16(c.T4, (ushort)c.T5);
        mem.WriteU16((c.T4 + 0x6u), (ushort)c.T6);
        mem.WriteU16((c.T4 + 0xCu), (ushort)c.T7);
        goto L8003E9B0;
        L8003E8A4: ;
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
        c.T4 = c.S4;
        c.T5 = mem.ReadU16(c.T4);
        c.T6 = mem.ReadU16((c.T4 + 0x6u));
        c.T7 = mem.ReadU16((c.T4 + 0xCu));
        RecompOne.Runtime.Gte.Write(9, c.T5);
        RecompOne.Runtime.Gte.Write(10, c.T6);
        RecompOne.Runtime.Gte.Write(11, c.T7);
        RecompOne.Runtime.Gte.MvmvaOp(12, false, 0, 3, 3);
        c.V0 = c.SP + 0x80u;
        c.T4 = c.V0;
        c.T5 = RecompOne.Runtime.Gte.Read(9);
        c.T6 = RecompOne.Runtime.Gte.Read(10);
        c.T7 = RecompOne.Runtime.Gte.Read(11);
        mem.WriteU16(c.T4, (ushort)c.T5);
        mem.WriteU16((c.T4 + 0x6u), (ushort)c.T6);
        mem.WriteU16((c.T4 + 0xCu), (ushort)c.T7);
        c.V0 = c.S4 + 0x2u;
        c.T4 = c.V0;
        c.T5 = mem.ReadU16(c.T4);
        c.T6 = mem.ReadU16((c.T4 + 0x6u));
        c.T7 = mem.ReadU16((c.T4 + 0xCu));
        RecompOne.Runtime.Gte.Write(9, c.T5);
        RecompOne.Runtime.Gte.Write(10, c.T6);
        RecompOne.Runtime.Gte.Write(11, c.T7);
        RecompOne.Runtime.Gte.MvmvaOp(12, false, 0, 3, 3);
        c.V0 = c.SP + 0x82u;
        c.T4 = c.V0;
        c.T5 = RecompOne.Runtime.Gte.Read(9);
        c.T6 = RecompOne.Runtime.Gte.Read(10);
        c.T7 = RecompOne.Runtime.Gte.Read(11);
        mem.WriteU16(c.T4, (ushort)c.T5);
        mem.WriteU16((c.T4 + 0x6u), (ushort)c.T6);
        mem.WriteU16((c.T4 + 0xCu), (ushort)c.T7);
        c.V0 = c.S4 + 0x4u;
        c.T4 = c.V0;
        c.T5 = mem.ReadU16(c.T4);
        c.T6 = mem.ReadU16((c.T4 + 0x6u));
        c.T7 = mem.ReadU16((c.T4 + 0xCu));
        RecompOne.Runtime.Gte.Write(9, c.T5);
        RecompOne.Runtime.Gte.Write(10, c.T6);
        RecompOne.Runtime.Gte.Write(11, c.T7);
        RecompOne.Runtime.Gte.MvmvaOp(12, false, 0, 3, 3);
        c.V0 = c.SP + 0x84u;
        c.T4 = c.V0;
        c.T5 = RecompOne.Runtime.Gte.Read(9);
        c.T6 = RecompOne.Runtime.Gte.Read(10);
        c.T7 = RecompOne.Runtime.Gte.Read(11);
        mem.WriteU16(c.T4, (ushort)c.T5);
        mem.WriteU16((c.T4 + 0x6u), (ushort)c.T6);
        mem.WriteU16((c.T4 + 0xCu), (ushort)c.T7);
        L8003E9B0: ;
        c.A1 = mem.ReadU16((c.S2 + 0x68u));
        c.V1 = 0xFFFFu;
        if (c.A1 == c.V1) {
            goto L8003EA00;
        }
        c.V0 = mem.ReadU16((c.S2 + 0x6Au));
        if (c.V0 == c.V1) {
            c.S0 = c.S7 << 16;
            goto L8003EA00;
        }
        c.S0 = c.S7 << 16;
        c.S0 = (uint)((int)c.S0 >> 16);
        c.A0 = mem.ReadU16((c.S1 + 0x68u));
        c.A2 = c.S0;
        c.RA = 0x8003E9E0u;
        Game.func_80017158(c, mem);
        c.A2 = c.S0;
        c.A0 = mem.ReadU16((c.S1 + 0x6Au));
        c.A1 = mem.ReadU16((c.S2 + 0x6Au));
        c.S0 = c.V0;
        c.RA = 0x8003E9F4u;
        Game.func_80017158(c, mem);
        c.A0 = c.S0;
        c.A1 = c.V0;
        goto L8003EA08;
        L8003EA00: ;
        c.A0 = mem.ReadU16((c.S1 + 0x68u));
        c.A1 = mem.ReadU16((c.S1 + 0x6Au));
        L8003EA08: ;
        c.RA = 0x8003EA10u;
        Game.func_80035358(c, mem);
        c.A1 = mem.ReadU8((c.S2 + 0x64u));
        c.V0 = 0x00FFu;
        if (c.A1 == c.V0) {
            c.S0 = c.S7 << 16;
            goto L8003EA7C;
        }
        c.S0 = c.S7 << 16;
        c.S0 = (uint)((int)c.S0 >> 16);
        c.A0 = mem.ReadU8((c.S1 + 0x64u));
        c.A2 = c.S0;
        c.RA = 0x8003EA30u;
        Game.func_80017158(c, mem);
        c.T4 = c.V0;
        c.A0 = mem.ReadU8((c.S1 + 0x65u));
        c.A1 = mem.ReadU8((c.S2 + 0x65u));
        c.A2 = c.S0;
        c.RA = 0x8003EA44u;
        Game.func_80017158(c, mem);
        c.T5 = c.V0;
        c.A0 = mem.ReadU8((c.S1 + 0x66u));
        c.A1 = mem.ReadU8((c.S2 + 0x66u));
        c.A2 = c.S0;
        c.RA = 0x8003EA58u;
        Game.func_80017158(c, mem);
        c.T6 = c.V0;
        c.T4 = c.T4 << 4;
        c.T5 = c.T5 << 4;
        c.T6 = c.T6 << 4;
        RecompOne.Runtime.Gte.WriteControl(13, c.T4);
        RecompOne.Runtime.Gte.WriteControl(14, c.T5);
        RecompOne.Runtime.Gte.WriteControl(15, c.T6);
        c.V0 = c.SP + 0x80u;
        goto L8003EC4C;
        L8003EA7C: ;
        c.T8 = mem.ReadU8((c.S1 + 0x64u));
        c.T4 = c.T8;
        c.T8 = mem.ReadU8((c.S1 + 0x65u));
        c.T5 = c.T8;
        c.S1 = mem.ReadU8((c.S1 + 0x66u));
        c.T6 = c.S1;
        c.T4 = c.T4 << 4;
        c.T5 = c.T5 << 4;
        c.T6 = c.T6 << 4;
        RecompOne.Runtime.Gte.WriteControl(13, c.T4);
        RecompOne.Runtime.Gte.WriteControl(14, c.T5);
        RecompOne.Runtime.Gte.WriteControl(15, c.T6);
        c.V0 = c.SP + 0x80u;
        goto L8003EC4C;
        L8003EAC0: ;
        c.V0 = c.S1 + 0x50u;
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
        c.A0 = mem.ReadU16((c.S1 + 0x68u));
        c.A1 = mem.ReadU16((c.S1 + 0x6Au));
        c.RA = 0x8003EB00u;
        Game.func_80035358(c, mem);
        c.T8 = mem.ReadU8((c.S1 + 0x64u));
        c.T4 = c.T8;
        c.T8 = mem.ReadU8((c.S1 + 0x65u));
        c.T5 = c.T8;
        c.T8 = mem.ReadU8((c.S1 + 0x66u));
        c.T6 = c.T8;
        c.T4 = c.T4 << 4;
        c.T5 = c.T5 << 4;
        c.T6 = c.T6 << 4;
        RecompOne.Runtime.Gte.WriteControl(13, c.T4);
        RecompOne.Runtime.Gte.WriteControl(14, c.T5);
        RecompOne.Runtime.Gte.WriteControl(15, c.T6);
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
        c.T4 = c.S4;
        c.T5 = mem.ReadU16(c.T4);
        c.T6 = mem.ReadU16((c.T4 + 0x6u));
        c.T7 = mem.ReadU16((c.T4 + 0xCu));
        RecompOne.Runtime.Gte.Write(9, c.T5);
        RecompOne.Runtime.Gte.Write(10, c.T6);
        RecompOne.Runtime.Gte.Write(11, c.T7);
        RecompOne.Runtime.Gte.MvmvaOp(12, false, 0, 3, 3);
        c.V0 = c.SP + 0x80u;
        c.T4 = c.V0;
        c.T5 = RecompOne.Runtime.Gte.Read(9);
        c.T6 = RecompOne.Runtime.Gte.Read(10);
        c.T7 = RecompOne.Runtime.Gte.Read(11);
        mem.WriteU16(c.T4, (ushort)c.T5);
        mem.WriteU16((c.T4 + 0x6u), (ushort)c.T6);
        mem.WriteU16((c.T4 + 0xCu), (ushort)c.T7);
        c.V0 = c.S4 + 0x2u;
        c.T4 = c.V0;
        c.T5 = mem.ReadU16(c.T4);
        c.T6 = mem.ReadU16((c.T4 + 0x6u));
        c.T7 = mem.ReadU16((c.T4 + 0xCu));
        RecompOne.Runtime.Gte.Write(9, c.T5);
        RecompOne.Runtime.Gte.Write(10, c.T6);
        RecompOne.Runtime.Gte.Write(11, c.T7);
        RecompOne.Runtime.Gte.MvmvaOp(12, false, 0, 3, 3);
        c.V0 = c.SP + 0x82u;
        c.T4 = c.V0;
        c.T5 = RecompOne.Runtime.Gte.Read(9);
        c.T6 = RecompOne.Runtime.Gte.Read(10);
        c.T7 = RecompOne.Runtime.Gte.Read(11);
        mem.WriteU16(c.T4, (ushort)c.T5);
        mem.WriteU16((c.T4 + 0x6u), (ushort)c.T6);
        mem.WriteU16((c.T4 + 0xCu), (ushort)c.T7);
        c.V0 = c.S4 + 0x4u;
        c.T4 = c.V0;
        c.T5 = mem.ReadU16(c.T4);
        c.T6 = mem.ReadU16((c.T4 + 0x6u));
        c.T7 = mem.ReadU16((c.T4 + 0xCu));
        RecompOne.Runtime.Gte.Write(9, c.T5);
        RecompOne.Runtime.Gte.Write(10, c.T6);
        RecompOne.Runtime.Gte.Write(11, c.T7);
        RecompOne.Runtime.Gte.MvmvaOp(12, false, 0, 3, 3);
        c.V0 = c.SP + 0x84u;
        c.T4 = c.V0;
        c.T5 = RecompOne.Runtime.Gte.Read(9);
        c.T6 = RecompOne.Runtime.Gte.Read(10);
        c.T7 = RecompOne.Runtime.Gte.Read(11);
        mem.WriteU16(c.T4, (ushort)c.T5);
        mem.WriteU16((c.T4 + 0x6u), (ushort)c.T6);
        mem.WriteU16((c.T4 + 0xCu), (ushort)c.T7);
        c.V0 = c.SP + 0x80u;
        L8003EC4C: ;
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
        c.T8 = mem.ReadU32((c.SP + 0xD0u));
        if (c.T8 == 0u) {
            c.A0 = c.S4;
            goto L8003ECB0;
        }
        c.A0 = c.S4;
        c.V0 = (uint)(short)mem.ReadU16(c.T8);
        mem.WriteU32((c.SP + 0x30u), c.V0);
        c.V0 = (uint)(short)mem.ReadU16((c.T8 + 0x2u));
        mem.WriteU32((c.SP + 0x34u), c.V0);
        c.V0 = (uint)(short)mem.ReadU16((c.T8 + 0x4u));
        c.A1 = c.SP + 0x30u;
        mem.WriteU32((c.SP + 0x38u), c.V0);
        c.RA = 0x8003ECB0u;
        Game.ScaleMatrix(c, mem);
        L8003ECB0: ;
        if (c.FP == 0u) {
            goto L8003EDB8;
        }
        c.T4 = c.FP;
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
        c.T4 = c.S4;
        c.T5 = mem.ReadU16(c.T4);
        c.T6 = mem.ReadU16((c.T4 + 0x6u));
        c.T7 = mem.ReadU16((c.T4 + 0xCu));
        RecompOne.Runtime.Gte.Write(9, c.T5);
        RecompOne.Runtime.Gte.Write(10, c.T6);
        RecompOne.Runtime.Gte.Write(11, c.T7);
        RecompOne.Runtime.Gte.MvmvaOp(12, false, 0, 3, 3);
        c.T4 = c.S4;
        c.T5 = RecompOne.Runtime.Gte.Read(9);
        c.T6 = RecompOne.Runtime.Gte.Read(10);
        c.T7 = RecompOne.Runtime.Gte.Read(11);
        mem.WriteU16(c.T4, (ushort)c.T5);
        mem.WriteU16((c.T4 + 0x6u), (ushort)c.T6);
        mem.WriteU16((c.T4 + 0xCu), (ushort)c.T7);
        c.V0 = c.S4 + 0x2u;
        c.T4 = c.V0;
        c.T5 = mem.ReadU16(c.T4);
        c.T6 = mem.ReadU16((c.T4 + 0x6u));
        c.T7 = mem.ReadU16((c.T4 + 0xCu));
        RecompOne.Runtime.Gte.Write(9, c.T5);
        RecompOne.Runtime.Gte.Write(10, c.T6);
        RecompOne.Runtime.Gte.Write(11, c.T7);
        RecompOne.Runtime.Gte.MvmvaOp(12, false, 0, 3, 3);
        c.T4 = c.V0;
        c.T5 = RecompOne.Runtime.Gte.Read(9);
        c.T6 = RecompOne.Runtime.Gte.Read(10);
        c.T7 = RecompOne.Runtime.Gte.Read(11);
        mem.WriteU16(c.T4, (ushort)c.T5);
        mem.WriteU16((c.T4 + 0x6u), (ushort)c.T6);
        mem.WriteU16((c.T4 + 0xCu), (ushort)c.T7);
        c.V0 = c.S4 + 0x4u;
        c.T4 = c.V0;
        c.T5 = mem.ReadU16(c.T4);
        c.T6 = mem.ReadU16((c.T4 + 0x6u));
        c.T7 = mem.ReadU16((c.T4 + 0xCu));
        RecompOne.Runtime.Gte.Write(9, c.T5);
        RecompOne.Runtime.Gte.Write(10, c.T6);
        RecompOne.Runtime.Gte.Write(11, c.T7);
        RecompOne.Runtime.Gte.MvmvaOp(12, false, 0, 3, 3);
        c.T4 = c.V0;
        c.T5 = RecompOne.Runtime.Gte.Read(9);
        c.T6 = RecompOne.Runtime.Gte.Read(10);
        c.T7 = RecompOne.Runtime.Gte.Read(11);
        mem.WriteU16(c.T4, (ushort)c.T5);
        mem.WriteU16((c.T4 + 0x6u), (ushort)c.T6);
        mem.WriteU16((c.T4 + 0xCu), (ushort)c.T7);
        L8003EDB8: ;
        c.T4 = c.S4;
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
        c.T4 = c.S4;
        c.T5 = mem.ReadU32((c.T4 + 0x14u));
        c.T6 = mem.ReadU32((c.T4 + 0x18u));
        RecompOne.Runtime.Gte.WriteControl(5, c.T5);
        c.T7 = mem.ReadU32((c.T4 + 0x1Cu));
        RecompOne.Runtime.Gte.WriteControl(6, c.T6);
        RecompOne.Runtime.Gte.WriteControl(7, c.T7);
        c.A1 = mem.ReadU16((c.SP + 0xC8u));
        c.V0 = c.A1 << 2;
        c.At = 0x801B0000u;
        c.At = c.At - 0x6D50u;
        c.At = c.At + c.V0;
        c.V1 = mem.ReadU32(c.At);
        c.V0 = mem.ReadU32((c.V1 + 0x8u));
        c.V1 = c.V1 + c.V0;
        mem.WriteU32((c.S3 + 0x10u), c.V1);
        c.A2 = mem.ReadU16((c.SP + 0xE0u));
        c.V0 = c.A2 < 0x00000080u ? 1u : 0u;
        if (c.V0 == 0u) {
            c.S1 = 0u;
            goto L8003EE84;
        }
        c.S1 = 0u;
        c.S0 = c.V1 + 0xCu;
        c.A0 = mem.ReadU32((c.SP + 0xD8u));
        c.T8 = mem.ReadU16((c.SP + 0xE8u));
        c.V0 = mem.ReadU32((c.V1 + 0x10u));
        c.A3 = c.T8;
        mem.WriteU32((c.SP + 0x10u), c.V0);
        c.RA = 0x8003EE60u;
        Game.func_800431E8(c, mem);
        if (c.V0 != 0u) {
            c.V0 = c.S6 & 0x0040u;
            goto L8003EEC4;
        }
        c.V0 = c.S6 & 0x0040u;
        c.V1 = mem.ReadU32((c.S3 + 0x10u));
        c.V0 = c.V1 + 0xCu;
        mem.WriteU32((c.S3 + 0x24u), c.V0);
        c.V0 = mem.ReadU32((c.V1 + 0xCu));
        goto L8003EEB0;
        L8003EE84: ;
        c.T8 = mem.ReadU16((c.SP + 0xE0u));
        c.A0 = mem.ReadU32((c.S3 + 0x10u));
        c.S1 = c.T8 & 0x007Fu;
        c.V0 = c.S1 << 3;
        c.V0 = c.V0 - c.S1;
        c.V0 = c.V0 << 2;
        c.V0 = c.V0 + 0xCu;
        c.V1 = c.V0 + c.V1;
        c.S0 = c.V0 + c.A0;
        mem.WriteU32((c.S3 + 0x24u), c.V1);
        c.V0 = mem.ReadU32(c.V1);
        L8003EEB0: ;
        c.V1 = mem.ReadU32((c.S3 + 0x10u));
        c.V0 = c.V0 + 0xCu;
        c.V0 = c.V0 + c.V1;
        mem.WriteU32((c.S3 + 0x4Cu), c.V0);
        c.V0 = c.S6 & 0x0040u;
        L8003EEC4: ;
        if (RetainedModels.Submit(mem, c.S1, (int)c.S5, c.S6, c.FP != 0,
            mem.ReadU32(c.SP + 0x114) - 8)) goto L8003F2A4;
        MoPose.Materialize(mem);
        if (c.V0 == 0u) {
            goto L8003EF64;
        }
        if (c.FP == 0u) {
            c.V0 = 0xFFFFFFFFu;
            goto L8003EEE4;
        }
        c.V0 = 0xFFFFFFFFu;
        c.A0 = c.S1;
        c.RA = 0x8003EEDCu;
        Game.func_800366A8(c, mem);
        goto L8003F2A4;
        L8003EEE4: ;
        c.A0 = mem.ReadU32((c.S0 + 0x4u));
        c.A1 = mem.ReadU32((c.S3 + 0x44u));
        c.A2 = mem.ReadU32((c.S3 + 0x4Cu));
        c.A0 = c.A0 - 0x1u;
        if (c.A0 == c.V0) {
            c.A3 = c.S3 + 0x54u;
            goto L8003F298;
        }
        c.A3 = c.S3 + 0x54u;
        c.T0 = 0xFFFFFFFFu;
        c.V1 = c.A1 + 0x4u;
        L8003EF04: ;
        RecompOne.Runtime.Interrupts.Poll(c, mem);
        c.T4 = c.A2;
        { var _lw = mem.ReadU32(c.T4); RecompOne.Runtime.Gte.Write(0, _lw); }
        { var _lw = mem.ReadU32((c.T4 + 0x4u)); RecompOne.Runtime.Gte.Write(1, _lw); }
        RecompOne.Runtime.Gte.MvmvaOp(12, false, 0, 0, 0);
        c.T4 = c.A3;
        { var _sw = RecompOne.Runtime.Gte.Read(25); mem.WriteU32(c.T4, _sw); }
        { var _sw = RecompOne.Runtime.Gte.Read(26); mem.WriteU32((c.T4 + 0x4u), _sw); }
        { var _sw = RecompOne.Runtime.Gte.Read(27); mem.WriteU32((c.T4 + 0x8u), _sw); }
        c.V0 = mem.ReadU16((c.S3 + 0x54u));
        c.A2 = c.A2 + 0x8u;
        mem.WriteU16(c.A1, (ushort)c.V0);
        c.V0 = mem.ReadU16((c.S3 + 0x58u));
        c.A0 = c.A0 - 0x1u;
        mem.WriteU16((c.V1 - 0x2u), (ushort)c.V0);
        c.V0 = mem.ReadU16((c.S3 + 0x5Cu));
        c.A1 = c.A1 + 0x8u;
        mem.WriteU16(c.V1, (ushort)c.S5);
        mem.WriteU16((c.V1 + 0x2u), (ushort)c.V0);
        if (c.A0 != c.T0) {
            c.V1 = c.V1 + 0x8u;
            goto L8003EF04;
        }
        c.V1 = c.V1 + 0x8u;
        c.A0 = c.S1;
        goto L8003F29C;
        L8003EF64: ;
        c.V0 = c.S6 & 0x0004u;
        if (c.V0 == 0u) {
            goto L8003F110;
        }
        if (c.FP == 0u) {
            c.V0 = 0xFFFFFFFFu;
            goto L8003F080;
        }
        c.V0 = 0xFFFFFFFFu;
        c.T0 = mem.ReadU32((c.S0 + 0x4u));
        c.A3 = mem.ReadU32((c.S3 + 0x44u));
        c.T1 = mem.ReadU32((c.S3 + 0x4Cu));
        c.T0 = c.T0 - 0x1u;
        if (c.T0 == c.V0) {
            c.T2 = 0x1F0Fu;
            goto L8003F0F8;
        }
        c.T2 = 0x1F0Fu;
        c.T3 = 0x801B0000u;
        c.T3 = c.T3 - 0x1384u;
        c.A1 = c.A3 + 0x6u;
        L8003EF9C: ;
        RecompOne.Runtime.Interrupts.Poll(c, mem);
        c.T4 = c.T1;
        { var _lw = mem.ReadU32(c.T4); RecompOne.Runtime.Gte.Write(0, _lw); }
        { var _lw = mem.ReadU32((c.T4 + 0x4u)); RecompOne.Runtime.Gte.Write(1, _lw); }
        RecompOne.Runtime.Gte.Rtps(12, false);
        c.T4 = c.A3;
        { var _sw = RecompOne.Runtime.Gte.Read(14); mem.WriteU32(c.T4, _sw); }
        c.V0 = c.A3 + 0x4u;
        c.T4 = c.V0;
        c.T5 = RecompOne.Runtime.Gte.Read(19);
        c.T5 = (uint)((int)c.T5 >> 2);
        mem.WriteU32(c.T4, c.T5);
        c.A0 = mem.ReadU32(c.T3);
        c.A2 = mem.ReadU32((c.T3 + 0x4u));
        if (c.A0 == c.A2) {
            c.V1 = (uint)((int)c.A0 >> 2);
            goto L8003F030;
        }
        c.V1 = (uint)((int)c.A0 >> 2);
        c.V0 = (uint)(short)mem.ReadU16((c.A1 - 0x2u));
        c.V0 = c.V0 - c.V1;
        c.V0 = c.V0 << 14;
        c.V1 = c.A2 - c.A0;
        { var _s = c.V0; var _t = c.V1; if (_t != 0u) { if ((int)_s == int.MinValue && (int)_t == -1) { c.LO = 0x80000000u; c.HI = 0u; } else { c.LO = (uint)((int)_s / (int)_t); c.HI = (uint)((int)_s % (int)_t); } } }
        if (c.V1 != 0u) {
            goto L8003F00C;
        }
        Recompiled.Bios.Break(c, mem);
        L8003F00C: ;
        c.At = 0xFFFFFFFFu;
        if (c.V1 != c.At) {
            c.At = 0x80000000u;
            goto L8003F024;
        }
        c.At = 0x80000000u;
        if (c.V0 != c.At) {
            goto L8003F024;
        }
        Recompiled.Bios.Break(c, mem);
        L8003F024: ;
        c.V0 = c.LO;
        mem.WriteU16(c.A1, (ushort)c.V0);
        goto L8003F034;
        L8003F030: ;
        mem.WriteU16(c.A1, (ushort)0u);
        L8003F034: ;
        c.V0 = (uint)(short)mem.ReadU16(c.A1);
        c.V0 = (int)c.V0 < 7952 ? 1u : 0u;
        if (c.V0 != 0u) {
            goto L8003F050;
        }
        mem.WriteU16(c.A1, (ushort)c.T2);
        mem.WriteU16((c.A1 - 0x2u), (ushort)c.T2);
        L8003F050: ;
        c.V0 = (uint)(short)mem.ReadU16(c.A1);
        if ((int)c.V0 >= 0) {
            c.A3 = c.A3 + 0x8u;
            goto L8003F064;
        }
        c.A3 = c.A3 + 0x8u;
        mem.WriteU16(c.A1, (ushort)0u);
        L8003F064: ;
        c.A1 = c.A1 + 0x8u;
        c.T0 = c.T0 - 0x1u;
        c.V0 = 0xFFFFFFFFu;
        if (c.T0 != c.V0) {
            c.T1 = c.T1 + 0x8u;
            goto L8003EF9C;
        }
        c.T1 = c.T1 + 0x8u;
        c.A0 = c.S1;
        goto L8003F0FC;
        L8003F080: ;
        c.A0 = mem.ReadU32((c.S0 + 0x4u));
        c.A1 = mem.ReadU32((c.S3 + 0x44u));
        c.A2 = mem.ReadU32((c.S3 + 0x4Cu));
        c.A0 = c.A0 - 0x1u;
        if (c.A0 == c.V0) {
            c.A3 = c.S3 + 0x54u;
            goto L8003F0F8;
        }
        c.A3 = c.S3 + 0x54u;
        c.T0 = 0xFFFFFFFFu;
        c.V1 = c.A1 + 0x4u;
        L8003F0A0: ;
        RecompOne.Runtime.Interrupts.Poll(c, mem);
        c.T4 = c.A2;
        { var _lw = mem.ReadU32(c.T4); RecompOne.Runtime.Gte.Write(0, _lw); }
        { var _lw = mem.ReadU32((c.T4 + 0x4u)); RecompOne.Runtime.Gte.Write(1, _lw); }
        RecompOne.Runtime.Gte.MvmvaOp(12, false, 0, 0, 0);
        c.T4 = c.A3;
        { var _sw = RecompOne.Runtime.Gte.Read(25); mem.WriteU32(c.T4, _sw); }
        { var _sw = RecompOne.Runtime.Gte.Read(26); mem.WriteU32((c.T4 + 0x4u), _sw); }
        { var _sw = RecompOne.Runtime.Gte.Read(27); mem.WriteU32((c.T4 + 0x8u), _sw); }
        c.V0 = mem.ReadU16((c.S3 + 0x54u));
        c.A2 = c.A2 + 0x8u;
        mem.WriteU16(c.A1, (ushort)c.V0);
        c.V0 = mem.ReadU16((c.S3 + 0x58u));
        c.A0 = c.A0 - 0x1u;
        mem.WriteU16((c.V1 - 0x2u), (ushort)c.V0);
        c.V0 = mem.ReadU16((c.S3 + 0x5Cu));
        c.A1 = c.A1 + 0x8u;
        mem.WriteU16(c.V1, (ushort)c.S5);
        mem.WriteU16((c.V1 + 0x2u), (ushort)c.V0);
        if (c.A0 != c.T0) {
            c.V1 = c.V1 + 0x8u;
            goto L8003F0A0;
        }
        c.V1 = c.V1 + 0x8u;
        L8003F0F8: ;
        c.A0 = c.S1;
        L8003F0FC: ;
        c.A1 = c.S5;
        c.A2 = c.S6 & 0x0003u;
        c.RA = 0x8003F108u;
        Game.func_80037BEC(c, mem);
        goto L8003F2A4;
        L8003F110: ;
        if (c.FP == 0u) {
            c.V0 = 0xFFFFFFFFu;
            goto L8003F220;
        }
        c.V0 = 0xFFFFFFFFu;
        c.T0 = mem.ReadU32((c.S0 + 0x4u));
        c.A3 = mem.ReadU32((c.S3 + 0x44u));
        c.T1 = mem.ReadU32((c.S3 + 0x4Cu));
        c.T0 = c.T0 - 0x1u;
        if (c.T0 == c.V0) {
            c.T2 = 0x1F0Fu;
            goto L8003F298;
        }
        c.T2 = 0x1F0Fu;
        c.T3 = 0x801B0000u;
        c.T3 = c.T3 - 0x1384u;
        c.A1 = c.A3 + 0x6u;
        L8003F13C: ;
        RecompOne.Runtime.Interrupts.Poll(c, mem);
        c.T4 = c.T1;
        { var _lw = mem.ReadU32(c.T4); RecompOne.Runtime.Gte.Write(0, _lw); }
        { var _lw = mem.ReadU32((c.T4 + 0x4u)); RecompOne.Runtime.Gte.Write(1, _lw); }
        RecompOne.Runtime.Gte.Rtps(12, false);
        c.T4 = c.A3;
        { var _sw = RecompOne.Runtime.Gte.Read(14); mem.WriteU32(c.T4, _sw); }
        c.V0 = c.A3 + 0x4u;
        c.T4 = c.V0;
        c.T5 = RecompOne.Runtime.Gte.Read(19);
        c.T5 = (uint)((int)c.T5 >> 2);
        mem.WriteU32(c.T4, c.T5);
        c.A0 = mem.ReadU32(c.T3);
        c.A2 = mem.ReadU32((c.T3 + 0x4u));
        if (c.A0 == c.A2) {
            c.V1 = (uint)((int)c.A0 >> 2);
            goto L8003F1D0;
        }
        c.V1 = (uint)((int)c.A0 >> 2);
        c.V0 = (uint)(short)mem.ReadU16((c.A1 - 0x2u));
        c.V0 = c.V0 - c.V1;
        c.V0 = c.V0 << 14;
        c.V1 = c.A2 - c.A0;
        { var _s = c.V0; var _t = c.V1; if (_t != 0u) { if ((int)_s == int.MinValue && (int)_t == -1) { c.LO = 0x80000000u; c.HI = 0u; } else { c.LO = (uint)((int)_s / (int)_t); c.HI = (uint)((int)_s % (int)_t); } } }
        if (c.V1 != 0u) {
            goto L8003F1AC;
        }
        Recompiled.Bios.Break(c, mem);
        L8003F1AC: ;
        c.At = 0xFFFFFFFFu;
        if (c.V1 != c.At) {
            c.At = 0x80000000u;
            goto L8003F1C4;
        }
        c.At = 0x80000000u;
        if (c.V0 != c.At) {
            goto L8003F1C4;
        }
        Recompiled.Bios.Break(c, mem);
        L8003F1C4: ;
        c.V0 = c.LO;
        mem.WriteU16(c.A1, (ushort)c.V0);
        goto L8003F1D4;
        L8003F1D0: ;
        mem.WriteU16(c.A1, (ushort)0u);
        L8003F1D4: ;
        c.V0 = (uint)(short)mem.ReadU16(c.A1);
        c.V0 = (int)c.V0 < 7952 ? 1u : 0u;
        if (c.V0 != 0u) {
            goto L8003F1F0;
        }
        mem.WriteU16(c.A1, (ushort)c.T2);
        mem.WriteU16((c.A1 - 0x2u), (ushort)c.T2);
        L8003F1F0: ;
        c.V0 = (uint)(short)mem.ReadU16(c.A1);
        if ((int)c.V0 >= 0) {
            c.A3 = c.A3 + 0x8u;
            goto L8003F204;
        }
        c.A3 = c.A3 + 0x8u;
        mem.WriteU16(c.A1, (ushort)0u);
        L8003F204: ;
        c.A1 = c.A1 + 0x8u;
        c.T0 = c.T0 - 0x1u;
        c.V0 = 0xFFFFFFFFu;
        if (c.T0 != c.V0) {
            c.T1 = c.T1 + 0x8u;
            goto L8003F13C;
        }
        c.T1 = c.T1 + 0x8u;
        c.A0 = c.S1;
        goto L8003F29C;
        L8003F220: ;
        c.A0 = mem.ReadU32((c.S0 + 0x4u));
        c.A1 = mem.ReadU32((c.S3 + 0x44u));
        c.A2 = mem.ReadU32((c.S3 + 0x4Cu));
        c.A0 = c.A0 - 0x1u;
        if (c.A0 == c.V0) {
            c.A3 = c.S3 + 0x54u;
            goto L8003F298;
        }
        c.A3 = c.S3 + 0x54u;
        c.T0 = 0xFFFFFFFFu;
        c.V1 = c.A1 + 0x4u;
        L8003F240: ;
        RecompOne.Runtime.Interrupts.Poll(c, mem);
        c.T4 = c.A2;
        { var _lw = mem.ReadU32(c.T4); RecompOne.Runtime.Gte.Write(0, _lw); }
        { var _lw = mem.ReadU32((c.T4 + 0x4u)); RecompOne.Runtime.Gte.Write(1, _lw); }
        RecompOne.Runtime.Gte.MvmvaOp(12, false, 0, 0, 0);
        c.T4 = c.A3;
        { var _sw = RecompOne.Runtime.Gte.Read(25); mem.WriteU32(c.T4, _sw); }
        { var _sw = RecompOne.Runtime.Gte.Read(26); mem.WriteU32((c.T4 + 0x4u), _sw); }
        { var _sw = RecompOne.Runtime.Gte.Read(27); mem.WriteU32((c.T4 + 0x8u), _sw); }
        c.V0 = mem.ReadU16((c.S3 + 0x54u));
        c.A2 = c.A2 + 0x8u;
        mem.WriteU16(c.A1, (ushort)c.V0);
        c.V0 = mem.ReadU16((c.S3 + 0x58u));
        c.A0 = c.A0 - 0x1u;
        mem.WriteU16((c.V1 - 0x2u), (ushort)c.V0);
        c.V0 = mem.ReadU16((c.S3 + 0x5Cu));
        c.A1 = c.A1 + 0x8u;
        mem.WriteU16(c.V1, (ushort)c.S5);
        mem.WriteU16((c.V1 + 0x2u), (ushort)c.V0);
        if (c.A0 != c.T0) {
            c.V1 = c.V1 + 0x8u;
            goto L8003F240;
        }
        c.V1 = c.V1 + 0x8u;
        L8003F298: ;
        c.A0 = c.S1;
        L8003F29C: ;
        c.A1 = c.S5;
        c.RA = 0x8003F2A4u;
        Game.func_80035CA4(c, mem);
        L8003F2A4: ;
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
        c.RA = mem.ReadU32((c.SP + 0x114u));
        c.FP = mem.ReadU32((c.SP + 0x110u));
        c.S7 = mem.ReadU32((c.SP + 0x10Cu));
        c.S6 = mem.ReadU32((c.SP + 0x108u));
        c.S5 = mem.ReadU32((c.SP + 0x104u));
        c.S4 = mem.ReadU32((c.SP + 0x100u));
        c.S3 = mem.ReadU32((c.SP + 0xFCu));
        c.S2 = mem.ReadU32((c.SP + 0xF8u));
        c.S1 = mem.ReadU32((c.SP + 0xF4u));
        c.S0 = mem.ReadU32((c.SP + 0xF0u));
        c.SP = c.SP + 0x118u;
        return;
    }
}
