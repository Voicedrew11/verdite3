using RecompOne.Runtime;
using RecompOne.Runtime.Context;
using RecompOne.Runtime.Memory;
using Game = Recompiled.KingsField3_game;

namespace Kf3;

/// <summary>
/// `func_8003C35C`, the HUD's 3D models, transcribed literally from generated/game.cs,
/// with the fraction its orthographic transform drops handed to the vertex map.
/// The transform is inline: two copies of one loop (`0x8003C6FC`, `0x8003C784`) that
/// MVMVA each vertex with no divide and store the result's X and Y into the vertex
/// cache `func_80035CA4` reads. On PolyAssembler's switch and its verify mode;
/// <c>KF3_POLYASM_HUD=0</c> leaves this one recompiled. See "The HUD's transform"
/// in docs/PICTURE.md.
/// </summary>
public static partial class PolyAssembler
{
    const uint HudModels = 0x8003C35C;

    static bool _queuedHud;

    public static bool HudEnabled { get; set; } = true;

    public static long HudModelCalls;

    /// <summary>HUD vertices offered to the vertex map, those with a fraction to
    /// offer, and those on unturned pieces kept whole; <c>KF3_SUBPIXEL_PROBE</c>
    /// reads and clears them.</summary>
    public static long ScreenVertices, ScreenFractional, ScreenAligned;

    static void ReplaceHud(Action<CpuContext, IMemory> orig, CpuContext c, IMemory m)
    {
        if (Recompiled(HudEnabled) || m is not PSMemory mem) { orig(c, m); return; }
        if (_mode == Mode.Verify) _hudCheck.Run(orig, c, mem, RunHud);
        else RunHud(c, mem);
    }

    static void RunHud(CpuContext c, PSMemory mem)
    {
        HudModelCalls++;
        HudBody(c, mem);
    }

    static readonly Differential _hudCheck = new("polyasm", "func_8003C35C", StackWindow);

    /// <summary>
    /// Offer a HUD vertex's fraction for the store that follows: the part of
    /// <c>(TR &lt;&lt; 12) + R·V</c> below the GTE's shift of 12. Published with a
    /// depth of 0, which says it was placed on the screen rather than projected: it
    /// takes no W and no depth, and the HUD stays 2D. A product that disagrees with
    /// the GTE's integer (an overflow the GTE saturated) is offered with no fraction.
    ///
    /// A piece the matrix does not <paramref name="turned"/> is offered a fraction of
    /// 0: its art was drawn for the whole-pixel snap (Verdite2's gauges lost their
    /// shadow to it). Only while sub-pixel is on, since nothing else reads it.
    /// </summary>
    static void PublishScreenVertex(uint xy, int x, int y, uint v01, uint v2, bool turned)
    {
        if (!turned)
        {
            GteVertexMap.Publish(xy, 0f, 0f, 0f, false);
            ScreenVertices++;
            ScreenAligned++;
            return;
        }

        short vx = (short)v01, vy = (short)(v01 >> 16), vz = (short)v2;
        // Rows 1 and 2 of R: control registers 0-2, two s16 elements a word.
        uint r0 = Gte.ReadControl(0), r1 = Gte.ReadControl(1), r2 = Gte.ReadControl(2);
        long sx = ((long)(int)Gte.ReadControl(5) << 12) + (short)r0 * vx + (short)(r0 >> 16) * vy + (short)r1 * vz;
        long sy = ((long)(int)Gte.ReadControl(6) << 12) + (short)(r1 >> 16) * vx + (short)r2 * vy + (short)(r2 >> 16) * vz;
        float fx = (sx >> 12) == x ? (sx & 0xFFF) / 4096f : 0f;
        float fy = (sy >> 12) == y ? (sy & 0xFFF) / 4096f : 0f;
        GteVertexMap.Publish(xy, 0f, fx, fy, false);
        ScreenVertices++;
        if (fx != 0f || fy != 0f) ScreenFractional++;
    }

    /// <summary>Whether the rotation turns a piece in the screen plane: any element
    /// of R's first two rows off the diagonal. A scale alone does not.</summary>
    static bool Turned()
    {
        uint r0 = Gte.ReadControl(0), r1 = Gte.ReadControl(1), r2 = Gte.ReadControl(2);
        return (r0 >> 16) != 0 || r1 != 0 || (r2 >> 16) != 0;
    }

    /// <summary>
    /// One vertex of either loop, from `lwc2` to the cache entry: X and Y, Z &gt;&gt; 2
    /// and Z. The routine stores X and Y as two halfwords read back from the
    /// scratchpad; here they are one word, the same bytes, so the vertex map can bind
    /// the fraction to it (it follows word stores only). Every other access is the
    /// routine's, in its order.
    /// </summary>
    static void HudVertex(CpuContext c, PSMemory mem, uint src, uint dst, bool publish, bool turned)
    {
        c.T4 = src;
        uint v01 = mem.ReadU32(c.T4), v2 = mem.ReadU32(c.T4 + 0x4u);
        Gte.Write(0, v01);
        Gte.Write(1, v2);
        Gte.MvmvaOp(12, false, 0, 0, 0);
        c.T4 = c.S6;
        uint mac1 = Gte.Read(25), mac2 = Gte.Read(26);
        mem.WriteU32(c.T4, mac1);
        mem.WriteU32(c.T4 + 0x4u, mac2);
        { var _a = (c.T4 + 0x8u); var _sw = Gte.Read(27); mem.WriteU32(_a, _sw); }
        uint xy = mem.ReadU16(c.S1 + 0x54u) | (uint)mem.ReadU16(c.S1 + 0x58u) << 16;
        if (publish) PublishScreenVertex(xy, (int)mac1, (int)mac2, v01, v2, turned);
        mem.WriteU32(dst, xy);
        c.V0 = xy >> 16;
    }

    // transcribed from generated/game.cs:55875
    static void HudBody(CpuContext c, IMemory m)
    {
        var mem = (PSMemory)m;
        { var _v = c.SP; c.SP = c.SP - 0x90u; }
        { var _a = (c.SP + 0x74u); mem.WriteU32(_a, c.S3); }
        c.S3 = 0x80080000u;
        { var _v = c.S3; c.S3 = c.S3 + 0x1C20u; }
        { var _a = (c.SP + 0x6Cu); mem.WriteU32(_a, c.S1); }
        { var _a = (c.SP + 0x8Cu); mem.WriteU32(_a, c.RA); }
        { var _a = (c.SP + 0x88u); mem.WriteU32(_a, c.FP); }
        { var _a = (c.SP + 0x84u); mem.WriteU32(_a, c.S7); }
        { var _a = (c.SP + 0x80u); mem.WriteU32(_a, c.S6); }
        { var _a = (c.SP + 0x7Cu); mem.WriteU32(_a, c.S5); }
        { var _a = (c.SP + 0x78u); mem.WriteU32(_a, c.S4); }
        { var _a = (c.SP + 0x70u); mem.WriteU32(_a, c.S2); }
        { var _a = (c.SP + 0x68u); mem.WriteU32(_a, c.S0); }
        { var _a = c.S3; c.V1 = mem.ReadU8(_a); }
        { var _v = 0u; c.V0 = 0u | 0x00FFu; }
        if (c.V1 == c.V0) {
            c.S1 = 0x1F800000u;
            goto L8003C834;
        }
        c.S1 = 0x1F800000u;
        { var _v = c.SP; c.S4 = c.SP + 0x18u; }
        { var _v = c.SP; c.S7 = c.SP + 0x48u; }
        c.S5 = 0x801B0000u;
        { var _v = c.S5; c.S5 = c.S5 - 0x1104u; }
        { var _v = c.S5; c.FP = c.S5 - 0x42A8u; }
        c.S6 = 0x1F800000u;
        { var _v = c.S6; c.S6 = c.S6 | 0x0054u; }
        { var _v = c.S3; c.S2 = c.S3 + 0x6u; }
        { var _a = c.S3; c.V1 = mem.ReadU8(_a); }
        L8003C3C4: ;
        Interrupts.Poll(c, m);
        { var _v = 0u; c.V0 = 0u | 0x0001u; }
        if (c.V1 != c.V0) {
            { var _v = c.S3; c.A0 = c.S3 + 0x18u; }
            goto L8003C820;
        }
        { var _v = c.S3; c.A0 = c.S3 + 0x18u; }
        { var _v = c.SP; c.A1 = c.SP + 0x18u; }
        { var _a = (c.S2 - 0x4u); c.V1 = mem.ReadU8(_a); }
        { var _a = (c.S2 + 0xAu); c.V0 = (uint)(short)mem.ReadU16(_a); }
        { var _v = c.V1; c.S0 = c.V1 << 3; }
        { var _s = c.S0; var _t = c.V1; c.S0 = _s - _t; }
        { var _v = c.S0; c.S0 = c.S0 << 2; }
        { var _s = c.S0; var _t = c.V1; c.S0 = _s - _t; }
        { var _a = (c.SP + 0x2Cu); mem.WriteU32(_a, c.V0); }
        { var _a = (c.S2 + 0xCu); c.V0 = (uint)(short)mem.ReadU16(_a); }
        { var _v = c.S0; c.S0 = c.S0 << 2; }
        { var _a = (c.SP + 0x30u); mem.WriteU32(_a, c.V0); }
        { var _a = (c.S2 + 0xEu); c.V0 = (uint)(short)mem.ReadU16(_a); }
        { var _s = c.S0; var _t = c.S5; c.S0 = _s + _t; }
        { var _a = (c.SP + 0x34u); mem.WriteU32(_a, c.V0); }
        c.RA = 0x8003C40Cu;
        Game.RotMatrix(c, m);
        { var _a = (c.S0 + 0x68u); c.A0 = mem.ReadU16(_a); }
        { var _a = (c.S0 + 0x6Au); c.A1 = mem.ReadU16(_a); }
        c.RA = 0x8003C41Cu;
        Game.func_80035358(c, m);
        { var _a = (c.S0 + 0x64u); c.T1 = mem.ReadU8(_a); }
        c.T4 = c.T1;
        { var _a = (c.S0 + 0x65u); c.T1 = mem.ReadU8(_a); }
        c.T5 = c.T1;
        { var _a = (c.S0 + 0x66u); c.T1 = mem.ReadU8(_a); }
        c.T6 = c.T1;
        { var _v = c.T4; c.T4 = c.T4 << 4; }
        { var _v = c.T5; c.T5 = c.T5 << 4; }
        { var _v = c.T6; c.T6 = c.T6 << 4; }
        Gte.WriteControl(13, c.T4);
        Gte.WriteControl(14, c.T5);
        Gte.WriteControl(15, c.T6);
        { var _v = c.S0; c.V0 = c.S0 + 0x50u; }
        c.T4 = c.V0;
        { var _a = c.T4; c.T5 = mem.ReadU32(_a); }
        { var _a = (c.T4 + 0x4u); c.T6 = mem.ReadU32(_a); }
        Gte.WriteControl(16, c.T5);
        Gte.WriteControl(17, c.T6);
        { var _a = (c.T4 + 0x8u); c.T5 = mem.ReadU32(_a); }
        { var _a = (c.T4 + 0xCu); c.T6 = mem.ReadU32(_a); }
        { var _a = (c.T4 + 0x10u); c.T7 = mem.ReadU32(_a); }
        Gte.WriteControl(18, c.T5);
        Gte.WriteControl(19, c.T6);
        Gte.WriteControl(20, c.T7);
        c.T4 = c.S0;
        { var _a = c.T4; c.T5 = mem.ReadU32(_a); }
        { var _a = (c.T4 + 0x4u); c.T6 = mem.ReadU32(_a); }
        Gte.WriteControl(0, c.T5);
        Gte.WriteControl(1, c.T6);
        { var _a = (c.T4 + 0x8u); c.T5 = mem.ReadU32(_a); }
        { var _a = (c.T4 + 0xCu); c.T6 = mem.ReadU32(_a); }
        { var _a = (c.T4 + 0x10u); c.T7 = mem.ReadU32(_a); }
        Gte.WriteControl(2, c.T5);
        Gte.WriteControl(3, c.T6);
        Gte.WriteControl(4, c.T7);
        c.T4 = c.S4;
        { var _a = c.T4; c.T5 = mem.ReadU16(_a); }
        { var _a = (c.T4 + 0x6u); c.T6 = mem.ReadU16(_a); }
        { var _a = (c.T4 + 0xCu); c.T7 = mem.ReadU16(_a); }
        Gte.Write(9, c.T5);
        Gte.Write(10, c.T6);
        Gte.Write(11, c.T7);
        Gte.MvmvaOp(12, false, 0, 3, 3);
        c.T4 = c.S7;
        c.T5 = Gte.Read(9);
        c.T6 = Gte.Read(10);
        c.T7 = Gte.Read(11);
        { var _a = c.T4; mem.WriteU16(_a, (ushort)c.T5); }
        { var _a = (c.T4 + 0x6u); mem.WriteU16(_a, (ushort)c.T6); }
        { var _a = (c.T4 + 0xCu); mem.WriteU16(_a, (ushort)c.T7); }
        { var _v = c.SP; c.V0 = c.SP + 0x1Au; }
        c.T4 = c.V0;
        { var _a = c.T4; c.T5 = mem.ReadU16(_a); }
        { var _a = (c.T4 + 0x6u); c.T6 = mem.ReadU16(_a); }
        { var _a = (c.T4 + 0xCu); c.T7 = mem.ReadU16(_a); }
        Gte.Write(9, c.T5);
        Gte.Write(10, c.T6);
        Gte.Write(11, c.T7);
        Gte.MvmvaOp(12, false, 0, 3, 3);
        { var _v = c.SP; c.V0 = c.SP + 0x4Au; }
        c.T4 = c.V0;
        c.T5 = Gte.Read(9);
        c.T6 = Gte.Read(10);
        c.T7 = Gte.Read(11);
        { var _a = c.T4; mem.WriteU16(_a, (ushort)c.T5); }
        { var _a = (c.T4 + 0x6u); mem.WriteU16(_a, (ushort)c.T6); }
        { var _a = (c.T4 + 0xCu); mem.WriteU16(_a, (ushort)c.T7); }
        { var _v = c.SP; c.V0 = c.SP + 0x1Cu; }
        c.T4 = c.V0;
        { var _a = c.T4; c.T5 = mem.ReadU16(_a); }
        { var _a = (c.T4 + 0x6u); c.T6 = mem.ReadU16(_a); }
        { var _a = (c.T4 + 0xCu); c.T7 = mem.ReadU16(_a); }
        Gte.Write(9, c.T5);
        Gte.Write(10, c.T6);
        Gte.Write(11, c.T7);
        Gte.MvmvaOp(12, false, 0, 3, 3);
        { var _v = c.SP; c.V0 = c.SP + 0x4Cu; }
        c.T4 = c.V0;
        c.T5 = Gte.Read(9);
        c.T6 = Gte.Read(10);
        c.T7 = Gte.Read(11);
        { var _a = c.T4; mem.WriteU16(_a, (ushort)c.T5); }
        { var _a = (c.T4 + 0x6u); mem.WriteU16(_a, (ushort)c.T6); }
        { var _a = (c.T4 + 0xCu); mem.WriteU16(_a, (ushort)c.T7); }
        c.T4 = c.S7;
        { var _a = c.T4; c.T5 = mem.ReadU32(_a); }
        { var _a = (c.T4 + 0x4u); c.T6 = mem.ReadU32(_a); }
        Gte.WriteControl(8, c.T5);
        Gte.WriteControl(9, c.T6);
        { var _a = (c.T4 + 0x8u); c.T5 = mem.ReadU32(_a); }
        { var _a = (c.T4 + 0xCu); c.T6 = mem.ReadU32(_a); }
        { var _a = (c.T4 + 0x10u); c.T7 = mem.ReadU32(_a); }
        Gte.WriteControl(10, c.T5);
        Gte.WriteControl(11, c.T6);
        Gte.WriteControl(12, c.T7);
        { var _a = (c.S2 + 0x2u); c.V0 = (uint)(short)mem.ReadU16(_a); }
        { var _a = (c.SP + 0x38u); mem.WriteU32(_a, c.V0); }
        { var _a = (c.S2 + 0x4u); c.V0 = (uint)(short)mem.ReadU16(_a); }
        c.A0 = c.S4;
        { var _a = (c.SP + 0x3Cu); mem.WriteU32(_a, c.V0); }
        { var _a = (c.S2 + 0x6u); c.V0 = (uint)(short)mem.ReadU16(_a); }
        { var _v = c.SP; c.A1 = c.SP + 0x38u; }
        { var _a = (c.SP + 0x40u); mem.WriteU32(_a, c.V0); }
        c.RA = 0x8003C5E4u;
        Game.ScaleMatrix(c, m);
        c.T4 = c.S4;
        { var _a = c.T4; c.T5 = mem.ReadU32(_a); }
        { var _a = (c.T4 + 0x4u); c.T6 = mem.ReadU32(_a); }
        Gte.WriteControl(0, c.T5);
        Gte.WriteControl(1, c.T6);
        { var _a = (c.T4 + 0x8u); c.T5 = mem.ReadU32(_a); }
        { var _a = (c.T4 + 0xCu); c.T6 = mem.ReadU32(_a); }
        { var _a = (c.T4 + 0x10u); c.T7 = mem.ReadU32(_a); }
        Gte.WriteControl(2, c.T5);
        Gte.WriteControl(3, c.T6);
        Gte.WriteControl(4, c.T7);
        c.T4 = c.S4;
        { var _a = (c.T4 + 0x14u); c.T5 = mem.ReadU32(_a); }
        { var _a = (c.T4 + 0x18u); c.T6 = mem.ReadU32(_a); }
        Gte.WriteControl(5, c.T5);
        { var _a = (c.T4 + 0x1Cu); c.T7 = mem.ReadU32(_a); }
        Gte.WriteControl(6, c.T6);
        Gte.WriteControl(7, c.T7);
        { var _a = (c.S2 - 0x2u); c.V0 = mem.ReadU16(_a); }
        { var _a = (c.S5 - 0x5D88u); c.A2 = mem.ReadU32(_a); }
        { var _a = (c.S5 - 0x3ECu); c.V1 = mem.ReadU32(_a); }
        { var _a = (c.S5 - 0x3E8u); c.A0 = mem.ReadU32(_a); }
        { var _a = (c.S5 - 0x3E4u); c.A1 = mem.ReadU32(_a); }
        { var _v = c.V0; c.V0 = c.V0 << 2; }
        { var _s = c.S5; var _t = c.V0; c.V0 = _s + _t; }
        { var _a = (c.V0 - 0x5C4Cu); c.A3 = mem.ReadU32(_a); }
        { var _a = (c.GP + 0xCCu); c.V0 = mem.ReadU32(_a); }
        { var _a = (c.A3 + 0x8u); c.T0 = mem.ReadU32(_a); }
        { var _a = (c.S1 + 0x8u); mem.WriteU32(_a, c.A2); }
        { var _a = (c.S1 + 0x64u); mem.WriteU32(_a, c.V0); }
        { var _a = (c.S1 + 0x78u); mem.WriteU32(_a, c.V1); }
        { var _a = (c.S1 + 0x7Cu); mem.WriteU32(_a, c.A0); }
        { var _a = (c.S1 + 0x80u); mem.WriteU32(_a, c.A1); }
        c.V0 = 0x801A0000u;
        { var _a = (c.V0 - 0x6E90u); c.V0 = mem.ReadU32(_a); }
        { var _s = c.A3; var _t = c.T0; c.S0 = _s + _t; }
        { var _a = (c.S1 + 0x10u); mem.WriteU32(_a, c.S0); }
        { var _a = (c.V0 + 0x8u); c.V0 = mem.ReadU32(_a); }
        c.V1 = 0x801A0000u;
        { var _a = (c.V1 - 0x6E90u); c.V1 = mem.ReadU32(_a); }
        { var _a = (c.S1 + 0x14u); mem.WriteU32(_a, c.V0); }
        { var _a = (c.V1 + 0x4u); c.V0 = mem.ReadU32(_a); }
        { var _a = (c.S1 + 0x44u); mem.WriteU32(_a, c.FP); }
        { var _a = (c.S1 + 0x84u); mem.WriteU16(_a, (ushort)0u); }
        { var _a = (c.S1 + 0x18u); mem.WriteU32(_a, c.V0); }
        { var _a = (c.S2 - 0x2u); c.A1 = mem.ReadU16(_a); }
        { var _a = (c.S2 - 0x5u); c.A2 = mem.ReadU8(_a); }
        { var _a = c.S2; c.A3 = mem.ReadU16(_a); }
        { var _a = (c.S0 + 0x10u); c.V0 = mem.ReadU32(_a); }
        { var _v = c.S3; c.A0 = c.S3 + 0x20u; }
        { var _a = (c.SP + 0x10u); mem.WriteU32(_a, c.V0); }
        c.RA = 0x8003C6B4u;
        Game.func_800431E8(c, m);
        if (c.V0 == 0u) {
            c.V0 = 0xFFFFFFFFu;
            goto L8003C768;
        }
        c.V0 = 0xFFFFFFFFu;
        { var _a = (c.S1 + 0x10u); c.V1 = mem.ReadU32(_a); }
        { var _a = (c.S1 + 0x44u); c.A1 = mem.ReadU32(_a); }
        { var _v = c.V1; c.V0 = c.V1 + 0xCu; }
        { var _a = (c.S1 + 0x24u); mem.WriteU32(_a, c.V0); }
        { var _a = (c.V1 + 0xCu); c.V0 = mem.ReadU32(_a); }
        { var _a = (c.S1 + 0x10u); c.V1 = mem.ReadU32(_a); }
        { var _v = c.V0; c.V0 = c.V0 + 0xCu; }
        { var _s = c.V0; var _t = c.V1; c.V0 = _s + _t; }
        c.A0 = c.V0;
        { var _a = (c.S1 + 0x4Cu); mem.WriteU32(_a, c.V0); }
        { var _a = (c.S0 + 0x10u); c.A3 = mem.ReadU32(_a); }
        c.V0 = 0xFFFFFFFFu;
        { var _v = c.A3; c.A3 = c.A3 - 0x1u; }
        if (c.A3 == c.V0) {
            c.A2 = 0xFFFFFFFFu;
            goto L8003C7E8;
        }
        c.A2 = 0xFFFFFFFFu;
        { var _v = c.A1; c.V1 = c.A1 + 0x4u; }
        // The piece's matrix is fixed for its vertices: whether it turns, once.
        bool publish = GteDepth.Subpixel && GteVertexMap.Active;
        bool turned = publish && Turned();
        L8003C6FC: ;
        Interrupts.Poll(c, m);
        HudVertex(c, mem, c.A0, c.A1, publish, turned);
        { var _v = c.A0; c.A0 = c.A0 + 0x8u; }
        { var _a = (c.S1 + 0x5Cu); c.V0 = mem.ReadU16(_a); }
        { var _v = c.A3; c.A3 = c.A3 - 0x1u; }
        { var _a = (c.V1 + 0x2u); mem.WriteU16(_a, (ushort)c.V0); }
        { var _a = (c.S1 + 0x5Cu); c.V0 = mem.ReadU32(_a); }
        { var _v = c.A1; c.A1 = c.A1 + 0x8u; }
        { var _v = c.V0; c.V0 = (uint)((int)c.V0 >> 2); }
        { var _a = c.V1; mem.WriteU16(_a, (ushort)c.V0); }
        if (c.A3 != c.A2) {
            { var _v = c.V1; c.V1 = c.V1 + 0x8u; }
            goto L8003C6FC;
        }
        { var _v = c.V1; c.V1 = c.V1 + 0x8u; }
        c.A0 = 0u;
        goto L8003C7EC;
        L8003C768: ;
        { var _a = (c.S0 + 0x10u); c.A3 = mem.ReadU32(_a); }
        { var _a = (c.S1 + 0x44u); c.A0 = mem.ReadU32(_a); }
        { var _a = (c.S1 + 0x4Cu); c.A1 = mem.ReadU32(_a); }
        { var _v = c.A3; c.A3 = c.A3 - 0x1u; }
        if (c.A3 == c.V0) {
            c.A2 = 0xFFFFFFFFu;
            goto L8003C7E8;
        }
        c.A2 = 0xFFFFFFFFu;
        { var _v = c.A0; c.V1 = c.A0 + 0x4u; }
        publish = GteDepth.Subpixel && GteVertexMap.Active;
        turned = publish && Turned();
        L8003C784: ;
        Interrupts.Poll(c, m);
        HudVertex(c, mem, c.A1, c.A0, publish, turned);
        { var _v = c.A1; c.A1 = c.A1 + 0x8u; }
        { var _a = (c.S1 + 0x5Cu); c.V0 = mem.ReadU16(_a); }
        { var _v = c.A3; c.A3 = c.A3 - 0x1u; }
        { var _a = (c.V1 + 0x2u); mem.WriteU16(_a, (ushort)c.V0); }
        { var _a = (c.S1 + 0x5Cu); c.V0 = mem.ReadU32(_a); }
        { var _v = c.A0; c.A0 = c.A0 + 0x8u; }
        { var _v = c.V0; c.V0 = (uint)((int)c.V0 >> 2); }
        { var _a = c.V1; mem.WriteU16(_a, (ushort)c.V0); }
        if (c.A3 != c.A2) {
            { var _v = c.V1; c.V1 = c.V1 + 0x8u; }
            goto L8003C784;
        }
        { var _v = c.V1; c.V1 = c.V1 + 0x8u; }
        L8003C7E8: ;
        c.A0 = 0u;
        L8003C7EC: ;
        c.A1 = 0u;
        c.RA = 0x8003C7F4u;
        Game.func_80035CA4(c, m);
        c.V1 = 0x801A0000u;
        { var _a = (c.V1 - 0x6E90u); c.V1 = mem.ReadU32(_a); }
        { var _a = (c.S1 + 0x14u); c.V0 = mem.ReadU32(_a); }
        { var _a = (c.V1 + 0x8u); mem.WriteU32(_a, c.V0); }
        { var _a = (c.S1 + 0x78u); c.V0 = mem.ReadU32(_a); }
        { var _a = (c.S1 + 0x7Cu); c.V1 = mem.ReadU32(_a); }
        c.At = 0x801B0000u;
        { var _a = (c.At - 0x14F0u); mem.WriteU32(_a, c.V0); }
        c.At = 0x801B0000u;
        { var _a = (c.At - 0x14ECu); mem.WriteU32(_a, c.V1); }
        L8003C820: ;
        { var _v = c.S3; c.S3 = c.S3 + 0x24u; }
        { var _a = c.S3; c.V1 = mem.ReadU8(_a); }
        { var _v = 0u; c.V0 = 0u | 0x00FFu; }
        if (c.V1 != c.V0) {
            { var _v = c.S2; c.S2 = c.S2 + 0x24u; }
            goto L8003C3C4;
        }
        { var _v = c.S2; c.S2 = c.S2 + 0x24u; }
        L8003C834: ;
        { var _a = (c.SP + 0x8Cu); c.RA = mem.ReadU32(_a); }
        { var _a = (c.SP + 0x88u); c.FP = mem.ReadU32(_a); }
        { var _a = (c.SP + 0x84u); c.S7 = mem.ReadU32(_a); }
        { var _a = (c.SP + 0x80u); c.S6 = mem.ReadU32(_a); }
        { var _a = (c.SP + 0x7Cu); c.S5 = mem.ReadU32(_a); }
        { var _a = (c.SP + 0x78u); c.S4 = mem.ReadU32(_a); }
        { var _a = (c.SP + 0x74u); c.S3 = mem.ReadU32(_a); }
        { var _a = (c.SP + 0x70u); c.S2 = mem.ReadU32(_a); }
        { var _a = (c.SP + 0x6Cu); c.S1 = mem.ReadU32(_a); }
        { var _a = (c.SP + 0x68u); c.S0 = mem.ReadU32(_a); }
        { var _v = c.SP; c.SP = c.SP + 0x90u; }
        return;
    }
}
