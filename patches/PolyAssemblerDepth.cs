using System.Reflection;
using System.Runtime.CompilerServices;
using RecompOne.Runtime;
using RecompOne.Runtime.Context;
using RecompOne.Runtime.Memory;
using RecompOne.Runtime.Modding;

namespace Kf3;

/// <summary>
/// Each finished C# packet's corner depths, recorded for the Z-buffer
/// (<see cref="ZBuffer"/>, <see cref="GtePacketDepth"/>). The map's bulk assembler
/// (`func_80039D50`) and the lit models' (`func_80035CA4`) are recorded; the HUD's
/// models, the menu's item preview (`func_8004290C`) and the first-person arm are not,
/// so they keep painter's order, as do the sky, the front table and the near path
/// (docs/PICTURE.md, "Unit 3").
///
/// The map's vertex pass is C# (<see cref="Transform"/>), so each corner's full SZ3
/// is kept. The models' pass is the submitter's, still recompiled, so only the
/// cache's `otz` (`SZ3 >> 2`) is readable there: their records lose the low two bits.
/// </summary>
public static partial class PolyAssembler
{
    // The vertex cache's slots: one 8-byte entry each, offsets from the cache fit in
    // 16 bits (docs/GAME_INTERNALS.md, "The map").
    const int CacheSlots = 1 << 13;

    // Full SZ3 per cache slot, beside the two words it was written with.
    struct CacheDepth { public uint W0, W1; public float Z; }
    static readonly CacheDepth[] _cacheDepth = new CacheDepth[CacheSlots];

    /// <summary>Set while func_8003C35C draws the HUD's 3D models.</summary>
    public static bool InHud;

    /// <summary>Set while func_8003DF50 draws the first-person arm.</summary>
    public static bool InArm;

    /// <summary>Set while func_8004290C draws a menu's item model.</summary>
    public static bool InPreview;

    /// <summary>HUD, arm and preview calls left unrecorded, for the probe.</summary>
    public static long HudCalls, ArmCalls, PreviewCalls;

    static bool DepthOn() => GtePacketDepth.Active && !InHud && !InArm && !InPreview;

    static uint _rangeSize;

    /// <summary>The record table covers RAM, since the packet buffer can move above
    /// 2 MB; allocated once, and again if the run mode gives more RAM. Public so the
    /// near path records into the same table.</summary>
    public static void EnsureRange()
    {
        uint ram = RecompOne.Runtime.Runtime.RamSize;
        if (_rangeSize == ram) return;
        GtePacketDepth.SetRange(0, ram);
        _rangeSize = ram;
    }

    /// <summary>The map's C# vertex pass, once both cache words are written; the full
    /// SZ3 the divide used is still in the GTE. A corner at or behind the camera is
    /// left at zero, so its face is not recorded and keeps painter's order.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static void NoteDepth(ref Frame fr, uint dst, uint sxy, uint sz3)
    {
        uint i = (dst - fr.Cache) >> 3;
        if (i >= CacheSlots) return;
        ref var e = ref _cacheDepth[i];
        e.W0 = sxy;
        e.W1 = fr.Mem.ReadU32(dst + 4u);
        e.Z = sz3 == 0u ? 0f : sz3;
    }

    /// <summary>A cached vertex's depth, if the cache still holds what our pass wrote;
    /// otherwise the models' recompiled pass left only the otz, and two bits are lost.</summary>
    static float CacheZ(ref Frame fr, uint p)
    {
        uint i = (p - fr.Cache) >> 3;
        if (i < CacheSlots)
        {
            ref var e = ref _cacheDepth[i];
            if (e.W0 == fr.Mem.ReadU32(p) && e.W1 == fr.Mem.ReadU32(p + 4u) && e.Z > 0f) return e.Z;
        }
        int otz = (short)R16(ref fr, p + 4u);
        return otz > 0 ? otz << 2 : 0f;
    }

    /// <summary>A packet built from cached vertices; <paramref name="last"/> is the
    /// offset of its last vertex word.</summary>
    static void DepthFace(ref Frame fr, uint pkt, uint last, int n, uint p0, uint p1, uint p2, uint p3)
    {
        ref var r = ref GtePacketDepth.Slot(pkt);
        r.Z0 = CacheZ(ref fr, p0);
        r.Z1 = CacheZ(ref fr, p1);
        r.Z2 = CacheZ(ref fr, p2);
        r.Z3 = n == 4 ? CacheZ(ref fr, p3) : 0f;
        if (r.Z0 <= 0f || r.Z1 <= 0f || r.Z2 <= 0f || (n == 4 && r.Z3 <= 0f)) { r.Cmd = 0; return; }
        SealDepth(ref fr, ref r, pkt, last);
    }

    /// <summary>A finished packet: recorded, or its address's old record dropped so a
    /// stale seal cannot match.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static void RecordDepth(ref Frame fr, uint pkt, uint last, int n, uint p0, uint p1, uint p2, uint p3)
    {
        if (fr.Depth) DepthFace(ref fr, pkt, last, n, p0, p1, p2, p3);
        else if (fr.DepthTable) NoDepth(pkt);
    }

    /// <summary>Drop whatever record a packet at this address had.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static void NoDepth(uint pkt) => GtePacketDepth.Slot(pkt).Cmd = 0;

    /// <summary>Seal with the command word and the first and last vertex words; the
    /// GPU believes a record only while all three still match.</summary>
    static void SealDepth(ref Frame fr, ref GtePacketDepth.Rec r, uint pkt, uint last)
    {
        var mem = fr.Mem;
        r.Cmd = mem.ReadU32(pkt + 4u);
        r.Xy0 = mem.ReadU32(pkt + 8u);
        r.XyLast = mem.ReadU32(pkt + last);
        GtePacketDepth.Recorded++;
    }

    // ---- func_8003C35C (the HUD), func_8003DF50 (the arm), func_8004290C (the preview)

    const uint Hud = 0x8003C35C, Arm = 0x8003DF50, Preview = 0x8004290C;

    /// <summary>Attach the HUD, arm and preview flags. Called once, from Program.cs
    /// after PolyAssembler.Install; the record table sizes itself on first recording.</summary>
    public static void InstallDepth() => HookAttach.OnOverlayLoad("depth", AttachDepth);

    static bool _depthQueued;

    static bool AttachDepth()
    {
        SymbolRegistry.Build();
        var hud = SymbolRegistry.Resolve("game", null, Hud);
        var arm = SymbolRegistry.Resolve("game", null, Arm);
        var preview = SymbolRegistry.Resolve("game", null, Preview);
        if (hud == null || arm == null || preview == null) return false;

        if (!_depthQueued)
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.Static;
            var self = typeof(PolyAssembler);
            HookManager.AddPre(_self, hud, self.GetMethod(nameof(BeforeHud), flags)!);
            HookManager.AddPost(_self, hud, self.GetMethod(nameof(AfterHud), flags)!);
            HookManager.AddPre(_self, arm, self.GetMethod(nameof(BeforeArm), flags)!);
            HookManager.AddPost(_self, arm, self.GetMethod(nameof(AfterArm), flags)!);
            HookManager.AddPre(_self, preview, self.GetMethod(nameof(BeforePreview), flags)!);
            HookManager.AddPost(_self, preview, self.GetMethod(nameof(AfterPreview), flags)!);
            _depthQueued = true;
        }

        HookManager.Commit();
        int hooked = (HookAttach.Installed(hud) ? 1 : 0) + (HookAttach.Installed(arm) ? 1 : 0)
                   + (HookAttach.Installed(preview) ? 1 : 0);
        Console.WriteLine($"[KF3] depth: record table over {_rangeSize >> 10} KB, {hooked}/3 hook(s)");
        return hooked == 3;
    }

    public static void BeforeHud(CpuContext c, IMemory m) => InHud = true;
    public static void AfterHud(CpuContext c, IMemory m) { InHud = false; HudCalls++; }
    public static void BeforeArm(CpuContext c, IMemory m) => InArm = true;
    public static void AfterArm(CpuContext c, IMemory m) { InArm = false; ArmCalls++; }
    public static void BeforePreview(CpuContext c, IMemory m) => InPreview = true;
    public static void AfterPreview(CpuContext c, IMemory m) { InPreview = false; PreviewCalls++; }
}
