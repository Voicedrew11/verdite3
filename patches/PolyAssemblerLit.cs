using System.Runtime.CompilerServices;
using RecompOne.Runtime;
using RecompOne.Runtime.Context;
using RecompOne.Runtime.Memory;

namespace Kf3;

/// <summary>
/// `func_80035CA4`, the lit assembler: the creatures and objects the model walk
/// submits, and the HUD's models. The map's loop with GTE lighting per face, a
/// caller's bias on the slot and a CLUT offset from the scratchpad, on a vertex
/// cache its caller filled. Verdite2's `func_8002F214`, kept textually close to its
/// PolyAssemblerLit.cs. See "The geometry path in C#" in docs/GEOMETRY.md.
/// </summary>
public static partial class PolyAssembler
{
    const uint Lit = 0x80035CA4;

    static bool _queuedLit;
    static long _litExhausted;

    /// <summary>Faces by kind (0x24, 0x2C, 0x34, 0x3C) that reached the fill, for verify's report.</summary>
    static readonly long[] _litKinds = new long[4];

    public static bool LitEnabled { get; set; } = true;

    public static long LitCalls;

    static void ReplaceLit(Action<CpuContext, IMemory> orig, CpuContext c, IMemory m)
    {
        if (Recompiled(LitEnabled) || m is not PSMemory mem) { orig(c, m); return; }
        if (_mode == Mode.Verify) _litCheck.Run(orig, c, mem, RunLit);
        else RunLit(c, mem);
    }

    /// <summary>a0 the model, a1 the slot bias. A frame of 0x18 bytes that saves
    /// S0-S4, none of which the C# touches.</summary>
    static void RunLit(CpuContext c, PSMemory mem)
    {
        uint sp = c.SP;
        LitCalls++;
        var fr = new Frame(mem, c);
        EnterLit(ref fr);
        try
        {
            c.SP = sp - 0x18u;
            LitBody(c, ref fr);
        }
        finally { c.SP = sp; }
        LeaveLit(ref fr);
        c.LO = fr.Lo;
        c.HI = fr.Hi;
    }

    static readonly Differential _litCheck = new("polyasm", "func_80035CA4", StackWindow, () =>
    {
        string s = $"; {_litExhausted} buffer exhaustion(s); filled 0x24 {_litKinds[0]}, 0x2C {_litKinds[1]}, " +
                   $"0x34 {_litKinds[2]}, 0x3C {_litKinds[3]}";
        _litExhausted = 0;
        Array.Clear(_litKinds);
        return s;
    });

    static void EnterLit(ref Frame fr)
    {
        var mem = fr.Mem;
        fr.Ot = mem.ReadU32(Pad + 0x08u);
        fr.Cursor = mem.ReadU32(Pad + 0x14u);
        fr.End = mem.ReadU32(Pad + 0x18u);
        fr.Word = mem.ReadU32(Pad + 0x1Cu);
        fr.Cache = mem.ReadU32(Pad + 0x44u);
        fr.Colour = mem.ReadU32(Pad + 0x64u);
        fr.Nclip = mem.ReadU32(Pad + 0x70u);
        fr.Faces = mem.ReadU32(Pad + 0x78u);
        fr.Links = mem.ReadU32(Pad + 0x7Cu);
        fr.Packets = mem.ReadU32(Pad + 0x80u);
        fr.Clut = mem.ReadU16(Pad + 0x84u);
    }

    static void LeaveLit(ref Frame fr)
    {
        var mem = fr.Mem;
        mem.WriteU32(Pad + 0x14u, fr.Cursor);
        mem.WriteU32(Pad + 0x1Cu, fr.Word);
        mem.WriteU32(Pad + 0x20u, fr.Face);
        mem.WriteU32(Pad + 0x24u, fr.Header);
        mem.WriteU32(Pad + 0x28u, fr.Normals);
        mem.WriteU32(Pad + 0x70u, fr.Nclip);
        mem.WriteU32(Pad + 0x78u, fr.Faces);
        mem.WriteU32(Pad + 0x7Cu, fr.Links);
        mem.WriteU32(Pad + 0x80u, fr.Packets);
    }

    static void LitBody(CpuContext c, ref Frame fr)
    {
        var mem = fr.Mem;
        uint bias = c.A1;
        uint table = mem.ReadU32(Pad + 0x10u);
        fr.Header = (c.A0 & 0xFFFFu) * 28u + 0xCu + table;
        fr.Normals = mem.ReadU32(fr.Header + 8u) + 0xCu + table;
        fr.Face = mem.ReadU32(fr.Header + 0x10u) + 0xCu + table;
        uint count = mem.ReadU32(fr.Header + 0x14u);
        fr.Faces += count;

        for (; count != 0; count--)
        {
            Interrupts.Poll(c, mem);
            fr.Check();
            uint word = fr.Word = mem.ReadU32(fr.Face);
            uint f = fr.Face += 4u;
            uint cmd = word >> 24;

            bool ok = (cmd & 0xFDu) switch
            {
                0x24u => FlatTriangle(ref fr, f, cmd, bias),
                0x2Cu => FlatQuad(ref fr, f, cmd, bias),
                0x34u => GouraudTriangle(ref fr, f, cmd, bias),
                0x3Cu => GouraudQuad(ref fr, f, cmd, bias),
                _ => true,
            };
            if (!ok) { _litExhausted++; return; }

            fr.Face = f + ((word >> 6) & 0x3FCu);
        }
    }

    /// <summary>POLY_FT3, lit at the mean fog weight.</summary>
    static bool FlatTriangle(ref Frame fr, uint f, uint cmd, uint bias)
    {
        var mem = fr.Mem;
        uint p0 = fr.Cache + R16(ref fr, f + 0x0Eu);
        uint p1 = fr.Cache + R16(ref fr, f + 0x10u);
        uint p2 = fr.Cache + R16(ref fr, f + 0x12u);
        if (!Visible(ref fr, p0, p1, p2)) return true;
        if (!Allocate(ref fr, 0x20u, out uint pkt)) return false;
        fr.Packets++;
        _litKinds[0]++;

        W16(ref fr, pkt + 0x0Eu, Clut(ref fr, f));
        W16(ref fr, pkt + 0x16u, R16(ref fr, f + 6u));
        mem.WriteU32(pkt + 0x08u, mem.ReadU32(p0));
        mem.WriteU32(pkt + 0x10u, mem.ReadU32(p1));
        mem.WriteU32(pkt + 0x18u, mem.ReadU32(p2));
        W16(ref fr, pkt + 0x0Cu, R16(ref fr, f));
        W16(ref fr, pkt + 0x14u, R16(ref fr, f + 4u));
        W16(ref fr, pkt + 0x1Cu, R16(ref fr, f + 8u));

        uint normal = fr.Normals + R16(ref fr, f + 0x0Cu);
        int fog = Third(ref fr, (short)R16(ref fr, p0 + 6u) + (short)R16(ref fr, p1 + 6u) + (short)R16(ref fr, p2 + 6u));
        Shade(ref fr, normal, (uint)fog, pkt + 4u);

        W8(ref fr, pkt + 3u, 0x07);
        W8(ref fr, pkt + 7u, (byte)cmd);
        RecordDepth(ref fr, pkt, 0x18u, 3, p0, p1, p2, 0u);

        Insert(ref fr, Third(ref fr, (short)R16(ref fr, p0 + 4u) + (short)R16(ref fr, p1 + 4u) + (short)R16(ref fr, p2 + 4u)), bias, pkt);
        return true;
    }

    /// <summary>POLY_FT4, lit at the mean fog weight.</summary>
    static bool FlatQuad(ref Frame fr, uint f, uint cmd, uint bias)
    {
        var mem = fr.Mem;
        uint p0 = fr.Cache + R16(ref fr, f + 0x12u);
        uint p1 = fr.Cache + R16(ref fr, f + 0x14u);
        uint p2 = fr.Cache + R16(ref fr, f + 0x16u);
        if (!Visible(ref fr, p0, p1, p2)) return true;
        if (!Allocate(ref fr, 0x28u, f + 0x18u, out uint pkt, out uint p3)) return false;
        fr.Packets++;
        _litKinds[1]++;

        W16(ref fr, pkt + 0x0Eu, Clut(ref fr, f));
        W16(ref fr, pkt + 0x16u, R16(ref fr, f + 6u));
        mem.WriteU32(pkt + 0x08u, mem.ReadU32(p0));
        mem.WriteU32(pkt + 0x10u, mem.ReadU32(p1));
        mem.WriteU32(pkt + 0x18u, mem.ReadU32(p2));
        mem.WriteU32(pkt + 0x20u, mem.ReadU32(p3));
        W16(ref fr, pkt + 0x0Cu, R16(ref fr, f));
        W16(ref fr, pkt + 0x14u, R16(ref fr, f + 4u));
        W16(ref fr, pkt + 0x1Cu, R16(ref fr, f + 8u));
        W16(ref fr, pkt + 0x24u, R16(ref fr, f + 0x0Cu));

        uint normal = fr.Normals + R16(ref fr, f + 0x10u);
        int fog = ((short)R16(ref fr, p0 + 6u) + (short)R16(ref fr, p1 + 6u)
                 + (short)R16(ref fr, p2 + 6u) + (short)R16(ref fr, p3 + 6u)) >> 2;
        Shade(ref fr, normal, (uint)fog, pkt + 4u);

        W8(ref fr, pkt + 3u, 0x09);
        W8(ref fr, pkt + 7u, (byte)cmd);
        RecordDepth(ref fr, pkt, 0x20u, 4, p0, p1, p2, p3);

        Insert(ref fr, QuadDepth(ref fr, p0, p1, p2, p3), bias, pkt);
        return true;
    }

    /// <summary>POLY_GT3, each vertex lit by its own normal at the first vertex's fog.</summary>
    static bool GouraudTriangle(ref Frame fr, uint f, uint cmd, uint bias)
    {
        var mem = fr.Mem;
        uint p0 = fr.Cache + R16(ref fr, f + 0x0Eu);
        uint p1 = fr.Cache + R16(ref fr, f + 0x12u);
        uint p2 = fr.Cache + R16(ref fr, f + 0x16u);
        if (!Visible(ref fr, p0, p1, p2)) return true;
        if (!Allocate(ref fr, 0x28u, out uint pkt)) return false;
        fr.Packets++;
        _litKinds[2]++;

        W16(ref fr, pkt + 0x0Eu, Clut(ref fr, f));
        W16(ref fr, pkt + 0x1Au, R16(ref fr, f + 6u));
        mem.WriteU32(pkt + 0x08u, mem.ReadU32(p0));
        mem.WriteU32(pkt + 0x14u, mem.ReadU32(p1));
        mem.WriteU32(pkt + 0x20u, mem.ReadU32(p2));
        W16(ref fr, pkt + 0x0Cu, R16(ref fr, f));
        W16(ref fr, pkt + 0x18u, R16(ref fr, f + 4u));
        W16(ref fr, pkt + 0x24u, R16(ref fr, f + 8u));

        uint n0 = R16(ref fr, f + 0x0Cu), n1 = R16(ref fr, f + 0x10u), n2 = R16(ref fr, f + 0x14u);
        uint fog = (uint)(short)R16(ref fr, p0 + 6u);
        Shade3(ref fr, fr.Normals + n0, fr.Normals + n1, fr.Normals + n2, fog, pkt + 4u, pkt + 0x10u, pkt + 0x1Cu);

        W8(ref fr, pkt + 3u, 0x09);
        W8(ref fr, pkt + 7u, (byte)cmd);
        RecordDepth(ref fr, pkt, 0x20u, 3, p0, p1, p2, 0u);

        Insert(ref fr, Third(ref fr, (short)R16(ref fr, p0 + 4u) + (short)R16(ref fr, p1 + 4u) + (short)R16(ref fr, p2 + 4u)), bias, pkt);
        return true;
    }

    /// <summary>POLY_GT4: three normals in one call and the fourth in another.</summary>
    static bool GouraudQuad(ref Frame fr, uint f, uint cmd, uint bias)
    {
        var mem = fr.Mem;
        uint p0 = fr.Cache + R16(ref fr, f + 0x12u);
        uint p1 = fr.Cache + R16(ref fr, f + 0x16u);
        uint p2 = fr.Cache + R16(ref fr, f + 0x1Au);
        if (!Visible(ref fr, p0, p1, p2)) return true;
        if (!Allocate(ref fr, 0x34u, f + 0x1Eu, out uint pkt, out uint p3)) return false;
        fr.Packets++;
        _litKinds[3]++;

        W16(ref fr, pkt + 0x0Eu, Clut(ref fr, f));
        W16(ref fr, pkt + 0x1Au, R16(ref fr, f + 6u));
        mem.WriteU32(pkt + 0x08u, mem.ReadU32(p0));
        mem.WriteU32(pkt + 0x14u, mem.ReadU32(p1));
        mem.WriteU32(pkt + 0x20u, mem.ReadU32(p2));
        mem.WriteU32(pkt + 0x2Cu, mem.ReadU32(p3));
        W16(ref fr, pkt + 0x0Cu, R16(ref fr, f));
        W16(ref fr, pkt + 0x18u, R16(ref fr, f + 4u));
        W16(ref fr, pkt + 0x24u, R16(ref fr, f + 8u));
        W16(ref fr, pkt + 0x30u, R16(ref fr, f + 0x0Cu));

        uint n0 = R16(ref fr, f + 0x10u), n1 = R16(ref fr, f + 0x14u), n2 = R16(ref fr, f + 0x18u);
        uint fog = (uint)(short)R16(ref fr, p0 + 6u);
        Shade3(ref fr, fr.Normals + n0, fr.Normals + n1, fr.Normals + n2, fog, pkt + 4u, pkt + 0x10u, pkt + 0x1Cu);
        Shade(ref fr, fr.Normals + R16(ref fr, f + 0x1Cu), fog, pkt + 0x28u);

        W8(ref fr, pkt + 3u, 0x0C);
        W8(ref fr, pkt + 7u, (byte)cmd);
        RecordDepth(ref fr, pkt, 0x2Cu, 4, p0, p1, p2, p3);

        Insert(ref fr, QuadDepth(ref fr, p0, p1, p2, p3), bias, pkt);
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static int QuadDepth(ref Frame fr, uint p0, uint p1, uint p2, uint p3) =>
        ((short)R16(ref fr, p0 + 4u) + (short)R16(ref fr, p1 + 4u)
       + (short)R16(ref fr, p2 + 4u) + (short)R16(ref fr, p3 + 4u)) >> 2;

    /// <summary>The face's CLUT plus the scratchpad's offset at +0x84.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static ushort Clut(ref Frame fr, uint f) => (ushort)(R16(ref fr, f + 2u) + fr.Clut);

    /// <summary>The allocator with the fourth vertex's index read where the game reads it.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static bool Allocate(ref Frame fr, uint size, uint index, out uint pkt, out uint vertex)
    {
        vertex = fr.Cache + R16(ref fr, index);
        return Allocate(ref fr, size, out pkt);
    }

    /// <summary>A mean depth of zero or less is dropped, then Place's range test.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static void Insert(ref Frame fr, int z, uint bias, uint pkt)
    {
        if (z <= 0) return;
        Place(ref fr, (uint)z + bias, pkt);
    }
}
