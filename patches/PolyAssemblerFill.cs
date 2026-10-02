using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using RecompOne.Runtime;
using RecompOne.Runtime.Context;
using RecompOne.Runtime.Memory;

namespace Kf3;

/// <summary>
/// The packet fill both bulk assemblers share: the allocator, the POLY_GT3 and
/// POLY_GT4 the map writes, the GTE lighting and the link. Copied from Verdite2's
/// PolyAssembler.cs and kept textually close to it, since a later unit diffs the
/// two games' fills to extract one. See "The geometry path in C#" in docs/GEOMETRY.md.
/// </summary>
public static partial class PolyAssembler
{
    const uint Pad = 0x1F800000;

    /// <summary>A 16-bit access below this physical address is inside RAM, so it can
    /// go straight to the array.</summary>
    static uint DirectLimit => RecompOne.Runtime.Runtime.RamSize - 1u;

    /// <summary>
    /// One call's state. The routines keep their working state in the scratchpad
    /// (0x1F800000) and store to it between almost every step; nothing else reads it
    /// while they run, so it lives here and <see cref="LeaveMap"/> or
    /// <see cref="LeaveLit"/> writes back what the routine would have left.
    /// </summary>
    ref struct Frame
    {
        public readonly PSMemory Mem;
        public readonly ref byte Ram;
        public uint Lim, Epoch;

        // Parameters: the ordering table (+0x08), the vertex cache (+0x44), the colour
        // the lighting starts from, the primitive cursor and end (+0x14, +0x18).
        public uint Ot, Cache, Colour, Cursor, End;
        // Working state: the face header (+0x1C), the face cursor (+0x20), the mesh
        // entry (+0x24), the normals (+0x28), the packet (+0x2C), the corners
        // (+0x30..+0x3C), the NCCS result (+0x40), the NCLIP result, the otz (+0x64).
        public uint Word, Face, Header, Normals, Pkt, P0, P1, P2, P3, Lit, Nclip;
        public ushort Otz;
        // Counters: faces, links, packets.
        public uint Faces, Links, Packets;
        // The vertex pass: its cursors and the fog's near and far.
        public uint Dst, Src;
        public int Near, Far;
        // The map's near limit (+0x66), the models' CLUT offset (+0x84).
        public short Limit;
        public ushort Clut;
        // LO and HI, left as the last mult or div left them.
        public uint Lo, Hi;

        public Frame(PSMemory mem, CpuContext c)
        {
            Mem = mem;
            Ram = ref Unsafe.AsRef(in MemoryMarshal.GetReference(mem.Ram));
            Lo = c.LO;
            Hi = c.HI;
            Refresh();
        }

        public void Refresh()
        {
            Epoch = Interrupts.SlowPolls;
            Lim = Mem.DirectRam ? DirectLimit : 0u;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Check()
        {
            if (Interrupts.SlowPolls != Epoch) Refresh();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static ushort R16(ref Frame fr, uint a)
    {
        uint o = a & 0x1FFFFFFFu;
        return o < fr.Lim ? Unsafe.ReadUnaligned<ushort>(ref Unsafe.Add(ref fr.Ram, (nint)o)) : SlowR16(fr.Mem, a);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static void W16(ref Frame fr, uint a, ushort v)
    {
        uint o = a & 0x1FFFFFFFu;
        if (o < fr.Lim) Unsafe.WriteUnaligned(ref Unsafe.Add(ref fr.Ram, (nint)o), v);
        else SlowW16(fr.Mem, a, v);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static void W8(ref Frame fr, uint a, byte v)
    {
        uint o = a & 0x1FFFFFFFu;
        if (o < fr.Lim) Unsafe.Add(ref fr.Ram, (nint)o) = v;
        else SlowW8(fr.Mem, a, v);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static ushort SlowR16(PSMemory mem, uint a) => mem.ReadU16(a);

    [MethodImpl(MethodImplOptions.NoInlining)]
    static void SlowW16(PSMemory mem, uint a, ushort v) => mem.WriteU16(a, v);

    [MethodImpl(MethodImplOptions.NoInlining)]
    static void SlowW8(PSMemory mem, uint a, byte v) => mem.WriteU8(a, v);

    /// <summary>A POLY_GT4 from four cached vertices, one NCCS and a DPCS per corner.</summary>
    static void FillQuad(ref Frame fr, uint pkt, uint f, uint cmd, uint normals, uint p0, uint p1, uint p2, uint p3)
    {
        var mem = fr.Mem;
        W16(ref fr, pkt + 0xEu, R16(ref fr, f + 2u));
        W16(ref fr, pkt + 0x1Au, R16(ref fr, f + 6u));
        mem.WriteU32(pkt + 0x08u, mem.ReadU32(p0));
        mem.WriteU32(pkt + 0x14u, mem.ReadU32(p1));
        mem.WriteU32(pkt + 0x20u, mem.ReadU32(p2));
        mem.WriteU32(pkt + 0x2Cu, mem.ReadU32(p3));
        W16(ref fr, pkt + 0x0Cu, R16(ref fr, f));
        W16(ref fr, pkt + 0x18u, R16(ref fr, f + 4u));
        W16(ref fr, pkt + 0x24u, R16(ref fr, f + 8u));
        W16(ref fr, pkt + 0x30u, R16(ref fr, f + 0xCu));

        uint normal = normals + R16(ref fr, f + 0x10u);
        uint colour = Light(ref fr, normal);
        Fog(ref fr, colour, p0, pkt + 0x04u);
        Fog(ref fr, colour, p1, pkt + 0x10u);
        Fog(ref fr, colour, p2, pkt + 0x1Cu);
        Fog(ref fr, colour, p3, pkt + 0x28u);

        W8(ref fr, pkt + 3u, 0x0C);
        W8(ref fr, pkt + 7u, (byte)((cmd & 2u) | 0x3Cu));
    }

    /// <summary>A POLY_GT3. The game runs NCCS twice on the same normal.</summary>
    static void FillTriangle(ref Frame fr, uint pkt, uint f, uint cmd, uint normals, uint p0, uint p1, uint p2)
    {
        var mem = fr.Mem;
        W16(ref fr, pkt + 0xEu, R16(ref fr, f + 2u));
        W16(ref fr, pkt + 0x1Au, R16(ref fr, f + 6u));
        mem.WriteU32(pkt + 0x08u, mem.ReadU32(p0));
        mem.WriteU32(pkt + 0x14u, mem.ReadU32(p1));
        mem.WriteU32(pkt + 0x20u, mem.ReadU32(p2));
        W16(ref fr, pkt + 0x0Cu, R16(ref fr, f));
        W16(ref fr, pkt + 0x18u, R16(ref fr, f + 4u));
        W16(ref fr, pkt + 0x24u, R16(ref fr, f + 8u));

        Light(ref fr, normals + R16(ref fr, f + 0x0Cu));
        uint colour = Light(ref fr, normals + R16(ref fr, f + 0x0Cu));
        Fog(ref fr, colour, p0, pkt + 0x04u);
        Fog(ref fr, colour, p1, pkt + 0x10u);
        Fog(ref fr, colour, p2, pkt + 0x1Cu);

        W8(ref fr, pkt + 3u, 0x09);
        W8(ref fr, pkt + 7u, (byte)((cmd & 2u) | 0x34u));
    }

    /// <summary>The map's 0x34 kind: a POLY_GT3 with an NCDS per corner on three
    /// normals. The corners are read where a 0x24 face keeps them (+0xE, +0x10,
    /// +0x12), so the second corner's index is the second normal's: the game's own
    /// reading, kept. The code byte is the face's own.</summary>
    static void FillGouraudTriangle(ref Frame fr, uint pkt, uint f, uint cmd, uint normals, uint p0, uint p1, uint p2)
    {
        var mem = fr.Mem;
        W16(ref fr, pkt + 0xEu, R16(ref fr, f + 2u));
        W16(ref fr, pkt + 0x1Au, R16(ref fr, f + 6u));
        mem.WriteU32(pkt + 0x08u, mem.ReadU32(p0));
        mem.WriteU32(pkt + 0x14u, mem.ReadU32(p1));
        mem.WriteU32(pkt + 0x20u, mem.ReadU32(p2));
        W16(ref fr, pkt + 0x0Cu, R16(ref fr, f));
        W16(ref fr, pkt + 0x18u, R16(ref fr, f + 4u));
        W16(ref fr, pkt + 0x24u, R16(ref fr, f + 8u));

        Shade(ref fr, normals + R16(ref fr, f + 0x0Cu), (uint)(short)R16(ref fr, p0 + 6u), pkt + 0x04u);
        Shade(ref fr, normals + R16(ref fr, f + 0x10u), (uint)(short)R16(ref fr, p1 + 6u), pkt + 0x10u);
        Shade(ref fr, normals + R16(ref fr, f + 0x14u), (uint)(short)R16(ref fr, p2 + 6u), pkt + 0x1Cu);

        W8(ref fr, pkt + 3u, 0x09);
        W8(ref fr, pkt + 7u, (byte)cmd);
    }

    /// <summary>NCLIP on the three cached screen words; the result is kept, as the
    /// game stores it.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static bool Visible(ref Frame fr, uint p0, uint p1, uint p2)
    {
        var mem = fr.Mem;
        uint w0 = mem.ReadU32(p0), w1 = mem.ReadU32(p1), w2 = mem.ReadU32(p2);
        Gte.Write(12, w0);
        Gte.Write(14, w2);
        Gte.Write(13, w1);
        Gte.Nclip();
        fr.Nclip = Gte.Read(24);
        return (int)fr.Nclip > 0;
    }

    /// <summary>The bump allocator on the scratchpad's cursor. The cursor moves even
    /// when the packet does not fit, and the call then ends.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static bool Allocate(ref Frame fr, uint size, out uint pkt)
    {
        pkt = fr.Pkt = fr.Cursor;
        fr.Cursor = pkt + size;
        return fr.End >= fr.Cursor;
    }

    /// <summary>NCCS. The game stores the result in the scratchpad and reads it back
    /// for each corner.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static uint Light(ref Frame fr, uint normal)
    {
        var mem = fr.Mem;
        Gte.Write(0, mem.ReadU32(normal));
        Gte.Write(1, mem.ReadU32(normal + 4u));
        Gte.Write(6, fr.Colour);
        Gte.NccsOp(12, true);
        return fr.Lit = Gte.Read(22);
    }

    /// <summary>DPCS at the corner's fog weight.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static void Fog(ref Frame fr, uint colour, uint vertex, uint dest)
    {
        Gte.Write(6, colour);
        Gte.Write(8, (uint)(short)R16(ref fr, vertex + 6u));
        Gte.Dpcs(12, false);
        fr.Mem.WriteU32(dest, Gte.Read(22));
    }

    /// <summary>NCDS.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static void Shade(ref Frame fr, uint normal, uint fog, uint dest)
    {
        var mem = fr.Mem;
        Gte.Write(0, mem.ReadU32(normal));
        Gte.Write(1, mem.ReadU32(normal + 4u));
        Gte.Write(6, fr.Colour);
        Gte.Write(8, fog);
        Gte.NcdsOp(12, true);
        mem.WriteU32(dest, Gte.Read(22));
    }

    /// <summary>NCDT.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static void Shade3(ref Frame fr, uint n0, uint n1, uint n2, uint fog, uint d0, uint d1, uint d2)
    {
        var mem = fr.Mem;
        Gte.Write(0, mem.ReadU32(n0));
        Gte.Write(1, mem.ReadU32(n0 + 4u));
        Gte.Write(2, mem.ReadU32(n1));
        Gte.Write(3, mem.ReadU32(n1 + 4u));
        Gte.Write(4, mem.ReadU32(n2));
        Gte.Write(5, mem.ReadU32(n2 + 4u));
        Gte.Write(6, fr.Colour);
        Gte.Write(8, fog);
        Gte.NcdtOp(12, true);
        mem.WriteU32(d0, Gte.Read(20));
        mem.WriteU32(d1, Gte.Read(21));
        mem.WriteU32(d2, Gte.Read(22));
    }

    /// <summary>The map's link: the otz the face stored, clamped to 0x1F0F already; a
    /// negative one is not linked, though its packet was allocated.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static void Link(ref Frame fr, uint pkt)
    {
        uint otz = (uint)(int)(short)fr.Otz;
        if (otz >= 0x1F10u) return;
        AddPrim(fr.Mem, fr.Ot + (otz << 2), pkt);
        fr.Links++;
    }

    /// <summary>The models' slot: dropped out of range rather than clamped. The
    /// packet is still allocated.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static void Place(ref Frame fr, uint otz, uint pkt)
    {
        if (otz >= 0x2000u) return;
        AddPrim(fr.Mem, fr.Ot + (otz << 2), pkt);
        fr.Links++;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static void AddPrim(PSMemory mem, uint slot, uint pkt)
    {
        uint tag = mem.ReadU32(pkt);
        uint next = mem.ReadU32(slot);
        mem.WriteU32(pkt, (tag & 0xFF000000u) | (next & 0x00FFFFFFu));
        next = mem.ReadU32(slot);
        mem.WriteU32(slot, (next & 0xFF000000u) | (pkt & 0x00FFFFFFu));
    }

    /// <summary>The /3 the game takes as mult by 0x55555556, leaving LO and HI.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static int Third(ref Frame fr, int sum)
    {
        long r = (long)sum * 0x55555556L;
        fr.Lo = (uint)r;
        fr.Hi = (uint)(r >> 32);
        return (int)fr.Hi - (sum >> 31);
    }

    /// <summary>The recompiler's div: a zero divisor leaves LO and HI as they were.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static void Divide(ref Frame fr, uint s, uint t)
    {
        if (t == 0u) return;
        if ((int)s == int.MinValue && (int)t == -1) { fr.Lo = 0x80000000u; fr.Hi = 0u; return; }
        fr.Lo = (uint)((int)s / (int)t);
        fr.Hi = (uint)((int)s % (int)t);
    }
}
