using RecompOne.Runtime;
using RecompOne.Runtime.Context;
using RecompOne.Runtime.Memory;
using RecompOne.Runtime.Modding;
using Game = Recompiled.KingsField3_game;

namespace Kf3;

/// <summary>
/// The near map assembler func_8003AB04 and the models' near submit func_800366A8,
/// transcribed literally from the recompiled bodies in generated/game.cs. Both
/// transform a vertex cache with RTPS, then walk the mesh's faces and hand each to
/// one of libgte's four division routines (patches/NearPathDivide.cs). Registers
/// stay in the CpuContext and every memory access keeps the generated order.
///
///     KF3_NEARPATH=0|off   both recompiled
///     KF3_NEARPATH=1       both in C# (the default)
///     KF3_NEARPATH=verify  run both on every call and compare RAM, scratchpad,
///                          registers and GTE, a report every 2 s
///     KF3_NEARPATH_MAP=0     func_8003AB04 recompiled
///     KF3_NEARPATH_MODELS=0  func_800366A8 recompiled
/// </summary>
public static partial class NearPath
{
    const uint Map = 0x8003AB04u;
    const uint Models = 0x800366A8u;

    enum Mode { Off, On, Verify }
    static Mode _mode = Mode.On;

    /// <summary>The mode as the Testing tab sets it: 0 recompiled, 1 C#, 2 verify.</summary>
    public static int Setting { get => (int)_mode; set => _mode = (Mode)Math.Clamp(value, 0, 2); }

    public static bool MapEnabled { get; set; } = true;
    public static bool ModelsEnabled { get; set; } = true;

    /// <summary>Running totals; never reset.</summary>
    public static long MapCalls;
    public static long ModelCalls;

    static bool _queuedMap, _queuedModels;

    // ---- depth records (docs/PICTURE.md, "Unit 4") --------------------------

    struct NearSz { public uint W; public float Z; }

    /// <summary>The SZ an RTPT produced for a subdivided corner, kept beside the
    /// 0x18-byte stack record it wrote that corner's screen word into. Host-side
    /// only; the records themselves are never touched.</summary>
    static readonly Dictionary<uint, NearSz> _sz = new();

    /// <summary>Records only while the Z-buffer is on and this routine is the C# one.</summary>
    static bool DepthRecording => _mode == Mode.On && GtePacketDepth.Active;

    /// <summary>Keep the SZ an RTPT produced next to the record's screen word.</summary>
    static void NoteSz(uint rec, uint w, uint sz)
    {
        if (!DepthRecording) return;
        _sz[rec] = new NearSz { W = w, Z = sz == 0u ? 0f : sz };
    }

    /// <summary>The full SZ3 of each vertex the two vertex passes cached, by its screen
    /// word: the cache itself keeps only the otz, <c>SZ3 &gt;&gt; 2</c>.</summary>
    static readonly Dictionary<uint, uint> _cacheSz = new();
    static uint _cacheSxy;

    static void NoteCacheSz(uint sxy, uint sz3)
    {
        if (!DepthRecording) return;
        _cacheSz[sxy] = sz3;
    }

    /// <summary>A face's own corners, as the near assembler filled them before the
    /// division: the screen word at +0x10 and the otz at +0x14, from the vertex cache.
    /// Only the division's RTPTs were kept before, so every sub-polygon that touched an
    /// original corner (all of an undivided face's, a quarter of a divided one's) had
    /// no record and drew in painter's order, over the retained models.
    ///
    /// The otz is a quarter of the SZ the division's own corners carry, and a triangle
    /// mixing the two had its W off by four at some corners: its texture and depth
    /// were bent across it. The pass's full SZ3 is used while its otz agrees, else the
    /// otz times four, as the models' records do.</summary>
    static void NoteCorners(PSMemory mem, uint list, int n)
    {
        if (!DepthRecording) return;
        for (uint k = 0; k < n; k++)
        {
            uint rec = mem.ReadU32(list + k * 4u);
            uint sxy = mem.ReadU32(rec + 0x10u);
            // The otz is a halfword: the models' path sign-extends it into the word.
            uint otz = mem.ReadU32(rec + 0x14u) & 0xFFFFu;
            uint sz = _cacheSz.TryGetValue(sxy, out uint full) && full >> 2 == otz ? full : otz << 2;
            NoteSz(rec, sxy, sz);
        }
    }

    /// <summary>A record's corner depth, if the kept SZ still matches the screen word
    /// the record holds; otherwise no depth.</summary>
    static float CornerZ(PSMemory mem, uint rec)
    {
        uint w = mem.ReadU32(rec + 0x10u);
        return _sz.TryGetValue(rec, out var e) && e.W == w && e.Z > 0f ? e.Z : 0f;
    }

    /// <summary>Seal an emitter's finished packet as PolyAssemblerDepth does (command
    /// word, first and last vertex words), or drop its address's old record so a stale
    /// seal cannot match.</summary>
    static void RecordNear(uint pkt, uint r0, uint r1, uint r2, uint r3, PSMemory mem)
    {
        if (!DepthRecording) return;
        uint cmd = mem.ReadU32(pkt + 4u);
        uint op = cmd >> 24;
        if (op < 0x20u || op >= 0x40u) { GtePacketDepth.Slot(pkt).Cmd = 0u; return; }
        int tex = (op & 4u) != 0 ? 1 : 0, g = (op & 0x10u) != 0 ? 1 : 0;
        int n = (op & 8u) != 0 ? 4 : 3;
        int per = 1 + tex + g;

        ref var r = ref GtePacketDepth.Slot(pkt);
        r.Z0 = CornerZ(mem, r0);
        r.Z1 = CornerZ(mem, r1);
        r.Z2 = CornerZ(mem, r2);
        r.Z3 = n == 4 ? CornerZ(mem, r3) : 0f;
        if (r.Z0 <= 0f || r.Z1 <= 0f || r.Z2 <= 0f || (n == 4 && r.Z3 <= 0f)) { r.Cmd = 0u; return; }
        r.Cmd = cmd;
        r.Xy0 = mem.ReadU32(pkt + 8u);
        r.XyLast = mem.ReadU32(pkt + 4u + (uint)(1 + (n - 1) * per) * 4u);
        GtePacketDepth.Recorded++;
    }

    internal static ModInfo Mod => _self;
    static readonly ModInfo _self = new()
    {
        Id = "kf3.nearpath",
        Name = "Near path assemblers",
        Version = "1.0",
        Description = "func_8003AB04 and func_800366A8 in C#.",
    };

    public static void Configure(string? mode)
    {
        _mode = (string.IsNullOrWhiteSpace(mode) ? null : mode.Trim().ToLowerInvariant()) switch
        {
            "verify" => Mode.Verify,
            "0" or "off" => Mode.Off,
            null or "" => Mode.On,
            _ => Mode.On,
        };
        if (Environment.GetEnvironmentVariable("KF3_NEARPATH_MAP") is { } map) MapEnabled = map.Trim() != "0";
        if (Environment.GetEnvironmentVariable("KF3_NEARPATH_MODELS") is { } models) ModelsEnabled = models.Trim() != "0";
    }

    public static void Install()
    {
        HookAttach.OnOverlayLoad("nearpath", Attach);
    }

    static bool Attach()
    {
        SymbolRegistry.Build();
        var map = SymbolRegistry.Resolve("game", null, Map);
        var models = SymbolRegistry.Resolve("game", null, Models);
        if (map == null || models == null) return false;

        if (!Queue(ref _queuedMap, map, nameof(ReplaceMap))) return false;
        if (!Queue(ref _queuedModels, models, nameof(ReplaceModels))) return false;

        HookManager.Commit();
        bool ok = HookAttach.Installed(map) && HookAttach.Installed(models);
        Console.WriteLine(!ok
            ? "[KF3] nearpath: not installed"
            : $"[KF3] nearpath: map {State(MapEnabled)}, models {State(ModelsEnabled)}");
        return ok;
    }

    static bool Queue(ref bool queued, System.Reflection.MethodInfo target, string method)
    {
        if (queued) return true;
        var impl = typeof(NearPath).GetMethod(method,
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        queued = HookManager.AddReplace(_self, target, impl);
        return queued;
    }

    // PGXP follows values through the registers, which the shared context still has:
    // its CPU tracking is a gate on the transcription's plain reads.
    static bool Recompiled(bool on) => !on || _mode == Mode.Off || RecompOne.Runtime.Pgxp.Pgxp.CpuTracking;

    static string State(bool on) => !on ? "off" : _mode.ToString().ToLowerInvariant();

    static void ReplaceMap(Action<CpuContext, IMemory> orig, CpuContext c, IMemory m)
    {
        if (Recompiled(MapEnabled) || m is not PSMemory mem) { orig(c, m); return; }
        if (_mode == Mode.Verify) _mapCheck.Run(orig, c, mem, RunMap);
        else RunMap(c, mem);
    }

    static void ReplaceModels(Action<CpuContext, IMemory> orig, CpuContext c, IMemory m)
    {
        if (Recompiled(ModelsEnabled) || m is not PSMemory mem) { orig(c, m); return; }
        if (_mode == Mode.Verify) _modelsCheck.Run(orig, c, mem, RunModels);
        else RunModels(c, mem);
    }

    static void RunMap(CpuContext c, PSMemory mem) { MapCalls++; BeginRecording(); BodyMap(c, mem); }
    static void RunModels(CpuContext c, PSMemory mem) { ModelCalls++; BeginRecording(); BodyModels(c, mem); }

    /// <summary>Each call fills its own vertex cache before its faces read it.</summary>
    static void BeginRecording()
    {
        if (!DepthRecording) return;
        PolyAssembler.EnsureRange();
        _cacheSz.Clear();
    }

    const uint StackWindow = 0x2000;
    static readonly Differential _mapCheck = new("nearpath", "func_8003AB04", StackWindow);
    static readonly Differential _modelsCheck = new("nearpath", "func_800366A8", StackWindow);

    // transcribed from generated/game.cs:49995
    static void BodyModels(CpuContext c, IMemory m)
    {
        var mem = (PSMemory)m;
        { var _v = c.SP; c.SP = c.SP - 0x358u; }
        c.V0 = 0x1F800000u;
        { var _a = (c.V0 + 0x44u); c.V0 = mem.ReadU32(_a); }
        c.V1 = 0x1F800000u;
        { var _a = (c.V1 + 0x4Cu); c.V1 = mem.ReadU32(_a); }
        c.A1 = 0x801B0000u;
        { var _a = (c.A1 - 0x1384u); c.A1 = mem.ReadU32(_a); }
        c.A2 = 0x801B0000u;
        { var _a = (c.A2 - 0x1380u); c.A2 = mem.ReadU32(_a); }
        { var _v = c.A0; c.A0 = c.A0 & 0xFFFFu; }
        { var _a = (c.SP + 0x354u); mem.WriteU32(_a, c.RA); }
        { var _a = (c.SP + 0x350u); mem.WriteU32(_a, c.S2); }
        { var _a = (c.SP + 0x34Cu); mem.WriteU32(_a, c.S1); }
        { var _a = (c.SP + 0x348u); mem.WriteU32(_a, c.S0); }
        c.At = 0x1F800000u;
        { var _a = (c.At + 0x50u); mem.WriteU32(_a, c.V1); }
        { var _v = c.A0; c.V1 = c.A0 << 3; }
        { var _s = c.V1; var _t = c.A0; c.V1 = _s - _t; }
        { var _v = c.V1; c.V1 = c.V1 << 2; }
        c.A0 = 0x1F800000u;
        { var _a = (c.A0 + 0x10u); c.A0 = mem.ReadU32(_a); }
        { var _v = c.V1; c.V1 = c.V1 + 0xCu; }
        c.At = 0x1F800000u;
        { var _a = (c.At + 0x48u); mem.WriteU32(_a, c.V0); }
        c.At = 0x1F800000u;
        { var _a = (c.At + 0x68u); mem.WriteU32(_a, c.A1); }
        c.At = 0x1F800000u;
        { var _a = (c.At + 0x6Cu); mem.WriteU32(_a, c.A2); }
        { var _s = c.V1; var _t = c.A0; c.V1 = _s + _t; }
        c.At = 0x1F800000u;
        { var _a = (c.At + 0x24u); mem.WriteU32(_a, c.V1); }
        { var _a = (c.V1 + 0x8u); c.V0 = mem.ReadU32(_a); }
        { var _v = c.V0; c.V0 = c.V0 + 0xCu; }
        { var _s = c.V0; var _t = c.A0; c.V0 = _s + _t; }
        c.At = 0x1F800000u;
        { var _a = (c.At + 0x28u); mem.WriteU32(_a, c.V0); }
        { var _a = (c.V1 + 0x4u); c.S1 = mem.ReadU32(_a); }
        c.S0 = 0x1F800000u;
        c.V0 = c.S1;
        if (c.V0 == 0u) {
            { var _v = c.S1; c.S1 = c.S1 - 0x1u; }
            goto L80036870;
        }
        { var _v = c.S1; c.S1 = c.S1 - 0x1u; }
        L80036750: ;
        Interrupts.Poll(c, m);
        { var _a = (c.S0 + 0x50u); c.A3 = mem.ReadU32(_a); }
        c.T4 = c.A3;
        { var _a = c.T4; var _lw = mem.ReadU32(_a); Gte.Write(0, _lw); }
        { var _a = (c.T4 + 0x4u); var _lw = mem.ReadU32(_a); Gte.Write(1, _lw); }
        Gte.Rtps(12, false);
        { var _a = (c.S0 + 0x48u); c.A3 = mem.ReadU32(_a); }
        c.T4 = c.A3;
        { var _a = c.T4; var _sw = Gte.Read(14); mem.WriteU32(_a, _sw); _cacheSxy = _sw; }
        { var _a = (c.S0 + 0x48u); c.V0 = mem.ReadU32(_a); }
        { var _v = c.V0; c.V0 = c.V0 + 0x4u; }
        c.T4 = c.V0;
        c.T5 = Gte.Read(19);
        NoteCacheSz(_cacheSxy, c.T5);
        { var _v = c.T5; c.T5 = (uint)((int)c.T5 >> 2); }
        { var _a = c.T4; mem.WriteU32(_a, c.T5); }
        { var _a = (c.S0 + 0x68u); c.A1 = mem.ReadU32(_a); }
        { var _v = c.A1; c.V0 = (int)c.A1 < 32000 ? 1u : 0u; }
        if (c.V0 == 0u) {
            { var _v = c.A1; c.V1 = (uint)((int)c.A1 >> 2); }
            goto L80036808;
        }
        { var _v = c.A1; c.V1 = (uint)((int)c.A1 >> 2); }
        { var _a = (c.S0 + 0x48u); c.A0 = mem.ReadU32(_a); }
        { var _a = (c.A0 + 0x4u); c.V0 = (uint)(short)mem.ReadU16(_a); }
        { var _s = c.V0; var _t = c.V1; c.V0 = _s - _t; }
        { var _a = (c.S0 + 0x6Cu); c.V1 = mem.ReadU32(_a); }
        { var _v = c.V0; c.V0 = c.V0 << 14; }
        { var _s = c.V1; var _t = c.A1; c.V1 = _s - _t; }
        { var _s = c.V0; var _t = c.V1; if (_t != 0u) { if ((int)_s == int.MinValue && (int)_t == -1) { c.LO = 0x80000000u; c.HI = 0u; } else { c.LO = (uint)((int)_s / (int)_t); c.HI = (uint)((int)_s % (int)_t); } } }
        if (c.V1 != 0u) {
            goto L800367E4;
        }
        BiosBreak(c, m);
        L800367E4: ;
        c.At = 0xFFFFFFFFu;
        if (c.V1 != c.At) {
            c.At = 0x80000000u;
            goto L800367FC;
        }
        c.At = 0x80000000u;
        if (c.V0 != c.At) {
            goto L800367FC;
        }
        BiosBreak(c, m);
        L800367FC: ;
        c.V0 = c.LO;
        { var _a = (c.A0 + 0x6u); mem.WriteU16(_a, (ushort)c.V0); }
        goto L80036814;
        L80036808: ;
        { var _a = (c.S0 + 0x48u); c.V0 = mem.ReadU32(_a); }
        { var _a = (c.V0 + 0x6u); mem.WriteU16(_a, (ushort)0u); }
        L80036814: ;
        { var _a = (c.S0 + 0x48u); c.V1 = mem.ReadU32(_a); }
        { var _a = (c.V1 + 0x6u); c.V0 = (uint)(short)mem.ReadU16(_a); }
        { var _v = c.V0; c.V0 = (int)c.V0 < 8192 ? 1u : 0u; }
        if (c.V0 != 0u) {
            { var _v = 0u; c.V0 = 0u | 0x1FFFu; }
            goto L80036838;
        }
        { var _v = 0u; c.V0 = 0u | 0x1FFFu; }
        { var _a = (c.V1 + 0x6u); mem.WriteU16(_a, (ushort)c.V0); }
        { var _a = (c.S0 + 0x48u); c.V1 = mem.ReadU32(_a); }
        L80036838: ;
        { var _a = (c.V1 + 0x6u); c.V0 = (uint)(short)mem.ReadU16(_a); }
        if ((int)c.V0 >= 0) {
            c.V0 = c.S1;
            goto L80036850;
        }
        c.V0 = c.S1;
        { var _a = (c.V1 + 0x6u); mem.WriteU16(_a, (ushort)0u); }
        L80036850: ;
        { var _v = c.S1; c.S1 = c.S1 - 0x1u; }
        { var _a = (c.S0 + 0x48u); c.V1 = mem.ReadU32(_a); }
        { var _a = (c.S0 + 0x50u); c.A0 = mem.ReadU32(_a); }
        { var _v = c.V1; c.V1 = c.V1 + 0x8u; }
        { var _v = c.A0; c.A0 = c.A0 + 0x8u; }
        { var _a = (c.S0 + 0x48u); mem.WriteU32(_a, c.V1); }
        if (c.V0 != 0u) {
            { var _a = (c.S0 + 0x50u); mem.WriteU32(_a, c.A0); }
            goto L80036750;
        }
        { var _a = (c.S0 + 0x50u); mem.WriteU32(_a, c.A0); }
        L80036870: ;
        { var _a = (c.S0 + 0x24u); c.V0 = mem.ReadU32(_a); }
        { var _a = (c.S0 + 0x10u); c.V1 = mem.ReadU32(_a); }
        { var _a = (c.V0 + 0x10u); c.V0 = mem.ReadU32(_a); }
        { var _a = (c.S0 + 0x24u); c.A0 = mem.ReadU32(_a); }
        { var _v = c.V0; c.V0 = c.V0 + 0xCu; }
        { var _s = c.V0; var _t = c.V1; c.V0 = _s + _t; }
        { var _a = (c.S0 + 0x20u); mem.WriteU32(_a, c.V0); }
        { var _a = (c.A0 + 0x14u); c.S1 = mem.ReadU32(_a); }
        { var _a = (c.S0 + 0x78u); c.V0 = mem.ReadU32(_a); }
        { var _s = c.S1; var _t = c.V0; c.V0 = _s + _t; }
        c.V1 = c.S1;
        { var _v = c.S1; c.S1 = c.S1 - 0x1u; }
        if (c.V1 == 0u) {
            { var _a = (c.S0 + 0x78u); mem.WriteU32(_a, c.V0); }
            goto L80037BD0;
        }
        { var _a = (c.S0 + 0x78u); mem.WriteU32(_a, c.V0); }
        { var _v = c.S0; c.S2 = c.S0 + 0x64u; }
        L800368B0: ;
        Interrupts.Poll(c, m);
        { var _a = (c.S0 + 0x20u); c.V0 = mem.ReadU32(_a); }
        { var _a = (c.S0 + 0x20u); c.A0 = mem.ReadU32(_a); }
        { var _a = c.V0; c.V0 = mem.ReadU32(_a); }
        { var _a = (c.S0 + 0x1Cu); mem.WriteU32(_a, c.V0); }
        { var _a = (c.S0 + 0x1Fu); c.V1 = mem.ReadU8(_a); }
        { var _v = c.A0; c.V0 = c.A0 + 0x4u; }
        { var _a = (c.S0 + 0x20u); mem.WriteU32(_a, c.V0); }
        { var _v = 0u; c.V0 = 0u | 0x0034u; }
        { var _v = c.V1; c.V1 = c.V1 & 0x00FDu; }
        if (c.V1 == c.V0) {
            { var _v = c.V1; c.V0 = (int)c.V1 < 53 ? 1u : 0u; }
            goto L800371B4;
        }
        { var _v = c.V1; c.V0 = (int)c.V1 < 53 ? 1u : 0u; }
        if (c.V0 == 0u) {
            { var _v = 0u; c.V0 = 0u | 0x0024u; }
            goto L80036900;
        }
        { var _v = 0u; c.V0 = 0u | 0x0024u; }
        if (c.V1 == c.V0) {
            { var _v = 0u; c.V0 = 0u | 0x002Cu; }
            goto L80036914;
        }
        { var _v = 0u; c.V0 = 0u | 0x002Cu; }
        if (c.V1 == c.V0) {
            c.V0 = c.S1;
            goto L80036CA8;
        }
        c.V0 = c.S1;
        goto L80037BB4;
        L80036900: ;
        { var _v = 0u; c.V0 = 0u | 0x003Cu; }
        if (c.V1 == c.V0) {
            c.V0 = c.S1;
            goto L800375E4;
        }
        c.V0 = c.S1;
        goto L80037BB4;
        L80036914: ;
        { var _a = (c.A0 + 0x12u); c.V0 = mem.ReadU16(_a); }
        { var _a = (c.S0 + 0x44u); c.V1 = mem.ReadU32(_a); }
        { var _a = (c.S0 + 0x20u); c.A0 = mem.ReadU32(_a); }
        { var _s = c.V0; var _t = c.V1; c.V0 = _s + _t; }
        { var _a = (c.S0 + 0x30u); mem.WriteU32(_a, c.V0); }
        { var _a = (c.A0 + 0x10u); c.V0 = mem.ReadU16(_a); }
        { var _a = (c.S0 + 0x44u); c.V1 = mem.ReadU32(_a); }
        { var _a = (c.S0 + 0x20u); c.A0 = mem.ReadU32(_a); }
        { var _s = c.V0; var _t = c.V1; c.V0 = _s + _t; }
        { var _a = (c.S0 + 0x34u); mem.WriteU32(_a, c.V0); }
        { var _a = (c.A0 + 0x12u); c.V0 = mem.ReadU16(_a); }
        { var _a = (c.S0 + 0x44u); c.V1 = mem.ReadU32(_a); }
        { var _s = c.V0; var _t = c.V1; c.V0 = _s + _t; }
        { var _a = (c.S0 + 0x38u); mem.WriteU32(_a, c.V0); }
        { var _a = (c.S0 + 0x30u); c.V0 = mem.ReadU32(_a); }
        { var _a = c.V0; c.V0 = mem.ReadU32(_a); }
        c.T4 = c.V0;
        { var _a = (c.S0 + 0x34u); c.V0 = mem.ReadU32(_a); }
        { var _a = c.V0; c.V0 = mem.ReadU32(_a); }
        c.T5 = c.V0;
        { var _a = (c.S0 + 0x38u); c.V0 = mem.ReadU32(_a); }
        { var _a = c.V0; c.V0 = mem.ReadU32(_a); }
        c.T6 = c.V0;
        Gte.Write(12, c.T4);
        Gte.Write(14, c.T6);
        Gte.Write(13, c.T5);
        Gte.Nclip();
        { var _v = c.S0; c.V0 = c.S0 + 0x70u; }
        c.T4 = c.V0;
        { var _a = c.T4; var _sw = Gte.Read(24); mem.WriteU32(_a, _sw); }
        { var _a = (c.S0 + 0x70u); c.V0 = mem.ReadU32(_a); }
        if ((int)c.V0 <= 0) {
            c.V0 = c.S1;
            goto L80037BB4;
        }
        c.V0 = c.S1;
        { var _a = (c.S0 + 0x76u); c.V0 = mem.ReadU16(_a); }
        { var _a = (c.S0 + 0x30u); c.V1 = mem.ReadU32(_a); }
        { var _v = c.V0; c.V0 = c.V0 << 16; }
        { var _a = (c.V1 + 0x4u); c.V1 = (uint)(short)mem.ReadU16(_a); }
        { var _v = c.V0; c.A0 = (uint)((int)c.V0 >> 16); }
        { var _s = c.V1; var _t = c.A0; c.V1 = (int)_s < (int)_t ? 1u : 0u; }
        if (c.V1 == 0u) {
            c.A1 = 0x55550000u;
            goto L80036A18;
        }
        c.A1 = 0x55550000u;
        { var _a = (c.S0 + 0x34u); c.V0 = mem.ReadU32(_a); }
        { var _a = (c.V0 + 0x4u); c.V0 = (uint)(short)mem.ReadU16(_a); }
        { var _s = c.V0; var _t = c.A0; c.V0 = (int)_s < (int)_t ? 1u : 0u; }
        if (c.V0 == 0u) {
            goto L80036A18;
        }
        { var _a = (c.S0 + 0x38u); c.V0 = mem.ReadU32(_a); }
        { var _a = (c.V0 + 0x4u); c.V0 = (uint)(short)mem.ReadU16(_a); }
        { var _s = c.V0; var _t = c.A0; c.V0 = (int)_s < (int)_t ? 1u : 0u; }
        if (c.V0 != 0u) {
            c.V0 = c.S1;
            goto L80037BB4;
        }
        c.V0 = c.S1;
        L80036A18: ;
        { var _a = (c.S0 + 0x30u); c.V0 = mem.ReadU32(_a); }
        { var _v = c.A1; c.A1 = c.A1 | 0x5556u; }
        { var _a = (c.V0 + 0x4u); c.V1 = (uint)(short)mem.ReadU16(_a); }
        { var _a = (c.S0 + 0x34u); c.V0 = mem.ReadU32(_a); }
        { var _a = (c.S0 + 0x38u); c.A0 = mem.ReadU32(_a); }
        { var _a = (c.V0 + 0x4u); c.V0 = (uint)(short)mem.ReadU16(_a); }
        { var _a = (c.A0 + 0x4u); c.A0 = (uint)(short)mem.ReadU16(_a); }
        { var _s = c.V1; var _t = c.V0; c.V1 = _s + _t; }
        { var _s = c.V1; var _t = c.A0; c.V1 = _s + _t; }
        { var _s = c.V1; var _t = c.A1; var _r = (long)(int)_s * (int)_t; c.LO = (uint)_r; c.HI = (uint)(_r >> 32); }
        { var _v = c.V1; c.V1 = (uint)((int)c.V1 >> 31); }
        c.V0 = c.HI;
        { var _s = c.V0; var _t = c.V1; c.V0 = _s - _t; }
        { var _a = (c.S0 + 0x74u); mem.WriteU16(_a, (ushort)c.V0); }
        { var _v = c.V0; c.V0 = c.V0 << 16; }
        { var _v = c.V0; c.V1 = (uint)((int)c.V0 >> 16); }
        { var _v = c.V1; c.V0 = (int)c.V1 < 1024 ? 1u : 0u; }
        if (c.V0 == 0u) {
            { var _v = 0u; c.V0 = 0u | 0x0400u; }
            goto L80036A74;
        }
        { var _v = 0u; c.V0 = 0u | 0x0400u; }
        { var _s = c.V0; var _t = c.V1; c.V0 = _s - _t; }
        { var _v = c.V0; c.V0 = (uint)((int)c.V0 >> 9); }
        { var _v = c.V0; c.V0 = c.V0 + 0x1u; }
        goto L80036A78;
        L80036A74: ;
        { var _v = 0u; c.V0 = 0u | 0x0001u; }
        L80036A78: ;
        { var _a = (c.SP + 0x10u); mem.WriteU32(_a, c.V0); }
        { var _v = 0u; c.V0 = 0u | 0x0140u; }
        { var _a = (c.SP + 0x14u); mem.WriteU32(_a, c.V0); }
        { var _a = (c.S0 + 0x20u); c.V1 = mem.ReadU32(_a); }
        { var _v = 0u; c.V0 = 0u | 0x00F0u; }
        { var _a = (c.SP + 0x18u); mem.WriteU32(_a, c.V0); }
        { var _a = (c.V1 + 0x2u); c.V0 = mem.ReadU16(_a); }
        { var _a = (c.S0 + 0x84u); c.V1 = mem.ReadU16(_a); }
        { var _a = (c.S0 + 0x20u); c.A0 = mem.ReadU32(_a); }
        { var _s = c.V0; var _t = c.V1; c.V0 = _s + _t; }
        { var _a = (c.SP + 0x1Cu); mem.WriteU16(_a, (ushort)c.V0); }
        { var _a = (c.A0 + 0x6u); c.V0 = mem.ReadU16(_a); }
        { var _a = (c.S0 + 0x20u); c.V1 = mem.ReadU32(_a); }
        { var _a = (c.SP + 0x1Eu); mem.WriteU16(_a, (ushort)c.V0); }
        { var _a = (c.V1 + 0xCu); c.V0 = mem.ReadU16(_a); }
        { var _a = (c.S0 + 0x28u); c.V1 = mem.ReadU32(_a); }
        { var _s = c.V0; var _t = c.V1; c.V0 = _s + _t; }
        c.T4 = c.V0;
        { var _a = c.T4; var _lw = mem.ReadU32(_a); Gte.Write(0, _lw); }
        { var _a = (c.T4 + 0x4u); var _lw = mem.ReadU32(_a); Gte.Write(1, _lw); }
        c.T4 = c.S2;
        { var _a = c.T4; var _lw = mem.ReadU32(_a); Gte.Write(6, _lw); }
        c.A1 = 0x55550000u;
        { var _a = (c.S0 + 0x30u); c.V0 = mem.ReadU32(_a); }
        { var _v = c.A1; c.A1 = c.A1 | 0x5556u; }
        { var _a = (c.V0 + 0x6u); c.V1 = (uint)(short)mem.ReadU16(_a); }
        { var _a = (c.S0 + 0x34u); c.V0 = mem.ReadU32(_a); }
        { var _a = (c.S0 + 0x38u); c.A0 = mem.ReadU32(_a); }
        { var _a = (c.V0 + 0x6u); c.V0 = (uint)(short)mem.ReadU16(_a); }
        { var _a = (c.A0 + 0x6u); c.A0 = (uint)(short)mem.ReadU16(_a); }
        { var _s = c.V1; var _t = c.V0; c.V1 = _s + _t; }
        { var _s = c.V1; var _t = c.A0; c.V1 = _s + _t; }
        { var _s = c.V1; var _t = c.A1; var _r = (long)(int)_s * (int)_t; c.LO = (uint)_r; c.HI = (uint)(_r >> 32); }
        { var _v = c.V1; c.V1 = (uint)((int)c.V1 >> 31); }
        c.V0 = c.HI;
        { var _s = c.V0; var _t = c.V1; c.V0 = _s - _t; }
        c.T4 = c.V0;
        Gte.Write(8, c.T4);
        Gte.NcdsOp(12, true);
        { var _v = c.SP; c.V0 = c.SP + 0x20u; }
        c.T4 = c.V0;
        { var _a = c.T4; var _sw = Gte.Read(22); mem.WriteU32(_a, _sw); }
        { var _v = c.SP; c.V0 = c.SP + 0x28u; }
        { var _a = (c.SP + 0xB8u); mem.WriteU32(_a, c.V0); }
        { var _v = c.SP; c.V0 = c.SP + 0x40u; }
        { var _a = (c.SP + 0xBCu); mem.WriteU32(_a, c.V0); }
        { var _v = c.SP; c.V0 = c.SP + 0x58u; }
        { var _a = (c.SP + 0xC0u); mem.WriteU32(_a, c.V0); }
        { var _a = (c.S0 + 0x1Fu); c.V1 = mem.ReadU8(_a); }
        { var _a = (c.S0 + 0x74u); c.V0 = mem.ReadU16(_a); }
        { var _v = c.V1; c.V1 = c.V1 | 0x0024u; }
        { var _v = c.V0; c.V0 = c.V0 << 16; }
        { var _a = (c.SP + 0x23u); mem.WriteU8(_a, (byte)c.V1); }
        { var _a = (c.S0 + 0x8u); c.V1 = mem.ReadU32(_a); }
        { var _v = c.V0; c.V0 = (uint)((int)c.V0 >> 14); }
        { var _s = c.V0; var _t = c.V1; c.V0 = _s + _t; }
        { var _a = (c.SP + 0x24u); mem.WriteU32(_a, c.V0); }
        { var _a = (c.S0 + 0x30u); c.V0 = mem.ReadU32(_a); }
        { var _a = (c.S0 + 0x34u); c.V1 = mem.ReadU32(_a); }
        { var _a = (c.V0 + 0x3u); c.A0 = mem.ReadWordLeft(c.A0, _a); }
        { var _a = c.V0; c.A0 = mem.ReadWordRight(c.A0, _a); }
        { var _a = (c.SP + 0x3Bu); mem.WriteWordLeft(_a, c.A0); }
        { var _a = (c.SP + 0x38u); mem.WriteWordRight(_a, c.A0); }
        { var _a = (c.V1 + 0x3u); c.V0 = mem.ReadWordLeft(c.V0, _a); }
        { var _a = c.V1; c.V0 = mem.ReadWordRight(c.V0, _a); }
        { var _a = (c.SP + 0x53u); mem.WriteWordLeft(_a, c.V0); }
        { var _a = (c.SP + 0x50u); mem.WriteWordRight(_a, c.V0); }
        { var _a = (c.S0 + 0x38u); c.V0 = mem.ReadU32(_a); }
        { var _a = (c.S0 + 0x30u); c.V1 = mem.ReadU32(_a); }
        { var _a = (c.V0 + 0x3u); c.A0 = mem.ReadWordLeft(c.A0, _a); }
        { var _a = c.V0; c.A0 = mem.ReadWordRight(c.A0, _a); }
        { var _a = (c.SP + 0x6Bu); mem.WriteWordLeft(_a, c.A0); }
        { var _a = (c.SP + 0x68u); mem.WriteWordRight(_a, c.A0); }
        { var _a = (c.V1 + 0x4u); c.V0 = (uint)(short)mem.ReadU16(_a); }
        { var _a = (c.S0 + 0x34u); c.V1 = mem.ReadU32(_a); }
        { var _a = (c.SP + 0x3Cu); mem.WriteU32(_a, c.V0); }
        { var _a = (c.V1 + 0x4u); c.V0 = (uint)(short)mem.ReadU16(_a); }
        { var _a = (c.S0 + 0x38u); c.V1 = mem.ReadU32(_a); }
        { var _a = (c.SP + 0x54u); mem.WriteU32(_a, c.V0); }
        { var _a = (c.V1 + 0x4u); c.V0 = (uint)(short)mem.ReadU16(_a); }
        { var _a = (c.S0 + 0x20u); c.V1 = mem.ReadU32(_a); }
        { var _a = (c.SP + 0x6Cu); mem.WriteU32(_a, c.V0); }
        { var _a = (c.V1 + 0xEu); c.V1 = mem.ReadU16(_a); }
        { var _a = (c.S0 + 0x4Cu); c.V0 = mem.ReadU32(_a); }
        { var _s = c.V0; var _t = c.V1; c.V0 = _s + _t; }
        { var _a = (c.S0 + 0x20u); c.V1 = mem.ReadU32(_a); }
        { var _a = (c.V0 + 0x3u); c.A0 = mem.ReadWordLeft(c.A0, _a); }
        { var _a = c.V0; c.A0 = mem.ReadWordRight(c.A0, _a); }
        { var _a = (c.V0 + 0x7u); c.A1 = mem.ReadWordLeft(c.A1, _a); }
        { var _a = (c.V0 + 0x4u); c.A1 = mem.ReadWordRight(c.A1, _a); }
        { var _a = (c.SP + 0x2Bu); mem.WriteWordLeft(_a, c.A0); }
        { var _a = (c.SP + 0x28u); mem.WriteWordRight(_a, c.A0); }
        { var _a = (c.SP + 0x2Fu); mem.WriteWordLeft(_a, c.A1); }
        { var _a = (c.SP + 0x2Cu); mem.WriteWordRight(_a, c.A1); }
        { var _a = (c.S0 + 0x4Cu); c.V0 = mem.ReadU32(_a); }
        { var _a = (c.V1 + 0x10u); c.V1 = mem.ReadU16(_a); }
        { var _s = c.V0; var _t = c.V1; c.V0 = _s + _t; }
        { var _a = (c.V0 + 0x3u); c.V1 = mem.ReadWordLeft(c.V1, _a); }
        { var _a = c.V0; c.V1 = mem.ReadWordRight(c.V1, _a); }
        { var _a = (c.V0 + 0x7u); c.A0 = mem.ReadWordLeft(c.A0, _a); }
        { var _a = (c.V0 + 0x4u); c.A0 = mem.ReadWordRight(c.A0, _a); }
        { var _a = (c.SP + 0x43u); mem.WriteWordLeft(_a, c.V1); }
        { var _a = (c.SP + 0x40u); mem.WriteWordRight(_a, c.V1); }
        { var _a = (c.SP + 0x47u); mem.WriteWordLeft(_a, c.A0); }
        { var _a = (c.SP + 0x44u); mem.WriteWordRight(_a, c.A0); }
        { var _a = (c.S0 + 0x20u); c.V0 = mem.ReadU32(_a); }
        { var _a = (c.V0 + 0x12u); c.V1 = mem.ReadU16(_a); }
        { var _a = (c.S0 + 0x4Cu); c.V0 = mem.ReadU32(_a); }
        { var _a = (c.S0 + 0x14u); c.A0 = mem.ReadU32(_a); }
        { var _s = c.V0; var _t = c.V1; c.V0 = _s + _t; }
        { var _a = (c.S0 + 0x20u); c.V1 = mem.ReadU32(_a); }
        { var _a = (c.V0 + 0x3u); c.A1 = mem.ReadWordLeft(c.A1, _a); }
        { var _a = c.V0; c.A1 = mem.ReadWordRight(c.A1, _a); }
        { var _a = (c.V0 + 0x7u); c.A2 = mem.ReadWordLeft(c.A2, _a); }
        { var _a = (c.V0 + 0x4u); c.A2 = mem.ReadWordRight(c.A2, _a); }
        { var _a = (c.SP + 0x5Bu); mem.WriteWordLeft(_a, c.A1); }
        { var _a = (c.SP + 0x58u); mem.WriteWordRight(_a, c.A1); }
        { var _a = (c.SP + 0x5Fu); mem.WriteWordLeft(_a, c.A2); }
        { var _a = (c.SP + 0x5Cu); mem.WriteWordRight(_a, c.A2); }
        { var _a = c.V1; c.V0 = mem.ReadU32(_a); }
        { var _a = (c.S0 + 0x20u); c.V1 = mem.ReadU32(_a); }
        { var _a = (c.SP + 0x30u); mem.WriteU32(_a, c.V0); }
        { var _a = (c.V1 + 0x4u); c.V0 = mem.ReadU32(_a); }
        { var _a = (c.S0 + 0x20u); c.V1 = mem.ReadU32(_a); }
        { var _a = (c.SP + 0x48u); mem.WriteU32(_a, c.V0); }
        { var _a = (c.V1 + 0x8u); c.V0 = mem.ReadU32(_a); }
        { var _v = c.SP; c.A1 = c.SP + 0x10u; }
        { var _a = (c.SP + 0x60u); mem.WriteU32(_a, c.V0); }
        c.RA = 0x80036CA0u;
        DivTri(c, m);
        goto L80037B88;
        L80036CA8: ;
        { var _a = (c.A0 + 0x16u); c.V0 = mem.ReadU16(_a); }
        { var _a = (c.S0 + 0x44u); c.V1 = mem.ReadU32(_a); }
        { var _a = (c.S0 + 0x20u); c.A0 = mem.ReadU32(_a); }
        { var _s = c.V0; var _t = c.V1; c.V0 = _s + _t; }
        { var _a = (c.S0 + 0x30u); mem.WriteU32(_a, c.V0); }
        { var _a = (c.A0 + 0x14u); c.V0 = mem.ReadU16(_a); }
        { var _a = (c.S0 + 0x44u); c.V1 = mem.ReadU32(_a); }
        { var _a = (c.S0 + 0x20u); c.A0 = mem.ReadU32(_a); }
        { var _s = c.V0; var _t = c.V1; c.V0 = _s + _t; }
        { var _a = (c.S0 + 0x34u); mem.WriteU32(_a, c.V0); }
        { var _a = (c.A0 + 0x16u); c.V0 = mem.ReadU16(_a); }
        { var _a = (c.S0 + 0x44u); c.V1 = mem.ReadU32(_a); }
        { var _a = (c.S0 + 0x20u); c.A0 = mem.ReadU32(_a); }
        { var _s = c.V0; var _t = c.V1; c.V0 = _s + _t; }
        { var _a = (c.S0 + 0x38u); mem.WriteU32(_a, c.V0); }
        { var _a = (c.A0 + 0x18u); c.V0 = mem.ReadU16(_a); }
        { var _a = (c.S0 + 0x44u); c.V1 = mem.ReadU32(_a); }
        { var _s = c.V0; var _t = c.V1; c.V0 = _s + _t; }
        { var _a = (c.S0 + 0x3Cu); mem.WriteU32(_a, c.V0); }
        { var _a = (c.S0 + 0x30u); c.V0 = mem.ReadU32(_a); }
        { var _a = c.V0; c.V0 = mem.ReadU32(_a); }
        c.T4 = c.V0;
        { var _a = (c.S0 + 0x34u); c.V0 = mem.ReadU32(_a); }
        { var _a = c.V0; c.V0 = mem.ReadU32(_a); }
        c.T5 = c.V0;
        { var _a = (c.S0 + 0x38u); c.V0 = mem.ReadU32(_a); }
        { var _a = c.V0; c.V0 = mem.ReadU32(_a); }
        c.T6 = c.V0;
        Gte.Write(12, c.T4);
        Gte.Write(14, c.T6);
        Gte.Write(13, c.T5);
        Gte.Nclip();
        c.T4 = c.S0;
        { var _a = c.T4; var _sw = Gte.Read(24); mem.WriteU32(_a, _sw); }
        { var _a = (c.S0 + 0x3Cu); c.V0 = mem.ReadU32(_a); }
        { var _a = c.V0; c.V0 = mem.ReadU32(_a); }
        c.T4 = c.V0;
        { var _a = (c.S0 + 0x38u); c.V0 = mem.ReadU32(_a); }
        { var _a = c.V0; c.V0 = mem.ReadU32(_a); }
        c.T5 = c.V0;
        { var _a = (c.S0 + 0x34u); c.V0 = mem.ReadU32(_a); }
        { var _a = c.V0; c.V0 = mem.ReadU32(_a); }
        c.T6 = c.V0;
        Gte.Write(12, c.T4);
        Gte.Write(14, c.T6);
        Gte.Write(13, c.T5);
        Gte.Nclip();
        { var _v = c.S0; c.V0 = c.S0 + 0x4u; }
        c.T4 = c.V0;
        { var _a = c.T4; var _sw = Gte.Read(24); mem.WriteU32(_a, _sw); }
        { var _a = c.S0; c.V0 = mem.ReadU32(_a); }
        if ((int)c.V0 > 0) {
            { var _v = 0u; c.V0 = 0u | 0x0001u; }
            goto L80036DD8;
        }
        { var _v = 0u; c.V0 = 0u | 0x0001u; }
        { var _a = (c.S0 + 0x4u); c.V0 = mem.ReadU32(_a); }
        if ((int)c.V0 > 0) {
            { var _v = 0u; c.V0 = 0u | 0x0001u; }
            goto L80036DD8;
        }
        { var _v = 0u; c.V0 = 0u | 0x0001u; }
        c.V0 = 0xFFFFFFFFu;
        L80036DD8: ;
        { var _a = (c.S0 + 0x70u); mem.WriteU32(_a, c.V0); }
        { var _a = (c.S0 + 0x70u); c.V0 = mem.ReadU32(_a); }
        if ((int)c.V0 <= 0) {
            c.V0 = c.S1;
            goto L80037BB4;
        }
        c.V0 = c.S1;
        { var _a = (c.S0 + 0x76u); c.V0 = mem.ReadU16(_a); }
        { var _a = (c.S0 + 0x30u); c.V1 = mem.ReadU32(_a); }
        { var _v = c.V0; c.V0 = c.V0 << 16; }
        { var _a = (c.V1 + 0x4u); c.V1 = (uint)(short)mem.ReadU16(_a); }
        { var _v = c.V0; c.A0 = (uint)((int)c.V0 >> 16); }
        { var _s = c.V1; var _t = c.A0; c.V1 = (int)_s < (int)_t ? 1u : 0u; }
        if (c.V1 == 0u) {
            goto L80036E60;
        }
        { var _a = (c.S0 + 0x34u); c.V0 = mem.ReadU32(_a); }
        { var _a = (c.V0 + 0x4u); c.V0 = (uint)(short)mem.ReadU16(_a); }
        { var _s = c.V0; var _t = c.A0; c.V0 = (int)_s < (int)_t ? 1u : 0u; }
        if (c.V0 == 0u) {
            goto L80036E60;
        }
        { var _a = (c.S0 + 0x38u); c.V0 = mem.ReadU32(_a); }
        { var _a = (c.V0 + 0x4u); c.V0 = (uint)(short)mem.ReadU16(_a); }
        { var _s = c.V0; var _t = c.A0; c.V0 = (int)_s < (int)_t ? 1u : 0u; }
        if (c.V0 == 0u) {
            goto L80036E60;
        }
        { var _a = (c.S0 + 0x3Cu); c.V0 = mem.ReadU32(_a); }
        { var _a = (c.V0 + 0x4u); c.V0 = (uint)(short)mem.ReadU16(_a); }
        { var _s = c.V0; var _t = c.A0; c.V0 = (int)_s < (int)_t ? 1u : 0u; }
        if (c.V0 != 0u) {
            c.V0 = c.S1;
            goto L80037BB4;
        }
        c.V0 = c.S1;
        L80036E60: ;
        { var _a = (c.S0 + 0x30u); c.V0 = mem.ReadU32(_a); }
        { var _a = (c.S0 + 0x34u); c.V1 = mem.ReadU32(_a); }
        { var _a = (c.V0 + 0x4u); c.V0 = (uint)(short)mem.ReadU16(_a); }
        { var _a = (c.V1 + 0x4u); c.V1 = (uint)(short)mem.ReadU16(_a); }
        { var _s = c.V0; var _t = c.V1; c.V0 = _s + _t; }
        { var _a = (c.S0 + 0x38u); c.V1 = mem.ReadU32(_a); }
        { var _a = (c.S0 + 0x3Cu); c.A0 = mem.ReadU32(_a); }
        { var _a = (c.V1 + 0x4u); c.V1 = (uint)(short)mem.ReadU16(_a); }
        { var _a = (c.A0 + 0x4u); c.A0 = (uint)(short)mem.ReadU16(_a); }
        { var _s = c.V0; var _t = c.V1; c.V0 = _s + _t; }
        { var _s = c.V0; var _t = c.A0; c.V0 = _s + _t; }
        { var _v = c.V0; c.V0 = (uint)((int)c.V0 >> 2); }
        { var _a = (c.S0 + 0x74u); mem.WriteU16(_a, (ushort)c.V0); }
        { var _v = c.V0; c.V0 = c.V0 << 16; }
        { var _v = c.V0; c.V1 = (uint)((int)c.V0 >> 16); }
        { var _v = c.V1; c.V0 = (int)c.V1 < 1024 ? 1u : 0u; }
        if (c.V0 == 0u) {
            { var _v = 0u; c.V0 = 0u | 0x0400u; }
            goto L80036EBC;
        }
        { var _v = 0u; c.V0 = 0u | 0x0400u; }
        { var _s = c.V0; var _t = c.V1; c.V0 = _s - _t; }
        { var _v = c.V0; c.V0 = (uint)((int)c.V0 >> 9); }
        { var _v = c.V0; c.V0 = c.V0 + 0x1u; }
        goto L80036EC0;
        L80036EBC: ;
        { var _v = 0u; c.V0 = 0u | 0x0001u; }
        L80036EC0: ;
        { var _a = (c.SP + 0x10u); mem.WriteU32(_a, c.V0); }
        { var _v = 0u; c.V0 = 0u | 0x0140u; }
        { var _a = (c.SP + 0x14u); mem.WriteU32(_a, c.V0); }
        { var _a = (c.S0 + 0x20u); c.V1 = mem.ReadU32(_a); }
        { var _v = 0u; c.V0 = 0u | 0x00F0u; }
        { var _a = (c.SP + 0x18u); mem.WriteU32(_a, c.V0); }
        { var _a = (c.V1 + 0x2u); c.V0 = mem.ReadU16(_a); }
        { var _a = (c.S0 + 0x84u); c.V1 = mem.ReadU16(_a); }
        { var _a = (c.S0 + 0x20u); c.A0 = mem.ReadU32(_a); }
        { var _s = c.V0; var _t = c.V1; c.V0 = _s + _t; }
        { var _a = (c.SP + 0x1Cu); mem.WriteU16(_a, (ushort)c.V0); }
        { var _a = (c.A0 + 0x6u); c.V0 = mem.ReadU16(_a); }
        { var _a = (c.S0 + 0x20u); c.V1 = mem.ReadU32(_a); }
        { var _a = (c.SP + 0x1Eu); mem.WriteU16(_a, (ushort)c.V0); }
        { var _a = (c.V1 + 0x12u); c.V0 = mem.ReadU16(_a); }
        { var _a = (c.S0 + 0x44u); c.V1 = mem.ReadU32(_a); }
        { var _a = (c.S0 + 0x20u); c.A0 = mem.ReadU32(_a); }
        { var _s = c.V0; var _t = c.V1; c.V0 = _s + _t; }
        { var _a = (c.S0 + 0x30u); mem.WriteU32(_a, c.V0); }
        { var _a = (c.A0 + 0x14u); c.V0 = mem.ReadU16(_a); }
        { var _a = (c.S0 + 0x44u); c.V1 = mem.ReadU32(_a); }
        { var _a = (c.S0 + 0x20u); c.A0 = mem.ReadU32(_a); }
        { var _s = c.V0; var _t = c.V1; c.V0 = _s + _t; }
        { var _a = (c.S0 + 0x34u); mem.WriteU32(_a, c.V0); }
        { var _a = (c.A0 + 0x16u); c.V0 = mem.ReadU16(_a); }
        { var _a = (c.S0 + 0x44u); c.V1 = mem.ReadU32(_a); }
        { var _a = (c.S0 + 0x20u); c.A0 = mem.ReadU32(_a); }
        { var _s = c.V0; var _t = c.V1; c.V0 = _s + _t; }
        { var _a = (c.S0 + 0x38u); mem.WriteU32(_a, c.V0); }
        { var _a = (c.A0 + 0x18u); c.V0 = mem.ReadU16(_a); }
        { var _a = (c.S0 + 0x44u); c.V1 = mem.ReadU32(_a); }
        { var _a = (c.S0 + 0x20u); c.A0 = mem.ReadU32(_a); }
        { var _s = c.V0; var _t = c.V1; c.V0 = _s + _t; }
        { var _a = (c.S0 + 0x3Cu); mem.WriteU32(_a, c.V0); }
        { var _a = (c.A0 + 0x10u); c.V0 = mem.ReadU16(_a); }
        { var _a = (c.S0 + 0x28u); c.V1 = mem.ReadU32(_a); }
        { var _s = c.V0; var _t = c.V1; c.V0 = _s + _t; }
        c.T4 = c.V0;
        { var _a = c.T4; var _lw = mem.ReadU32(_a); Gte.Write(0, _lw); }
        { var _a = (c.T4 + 0x4u); var _lw = mem.ReadU32(_a); Gte.Write(1, _lw); }
        c.T4 = c.S2;
        { var _a = c.T4; var _lw = mem.ReadU32(_a); Gte.Write(6, _lw); }
        { var _a = (c.S0 + 0x30u); c.V0 = mem.ReadU32(_a); }
        { var _a = (c.S0 + 0x34u); c.A0 = mem.ReadU32(_a); }
        { var _a = (c.V0 + 0x6u); c.V1 = (uint)(short)mem.ReadU16(_a); }
        { var _a = (c.A0 + 0x6u); c.V0 = (uint)(short)mem.ReadU16(_a); }
        { var _s = c.V1; var _t = c.V0; c.V1 = _s + _t; }
        { var _a = (c.S0 + 0x38u); c.V0 = mem.ReadU32(_a); }
        { var _a = (c.S0 + 0x3Cu); c.A0 = mem.ReadU32(_a); }
        { var _a = (c.V0 + 0x6u); c.V0 = (uint)(short)mem.ReadU16(_a); }
        { var _a = (c.A0 + 0x6u); c.A0 = (uint)(short)mem.ReadU16(_a); }
        { var _s = c.V1; var _t = c.V0; c.V1 = _s + _t; }
        { var _s = c.V1; var _t = c.A0; c.V0 = _s + _t; }
        if ((int)c.V0 >= 0) {
            goto L80036FA8;
        }
        { var _v = c.V0; c.V0 = c.V0 + 0x3u; }
        L80036FA8: ;
        { var _v = c.V0; c.V0 = (uint)((int)c.V0 >> 2); }
        c.T4 = c.V0;
        Gte.Write(8, c.T4);
        Gte.NcdsOp(12, true);
        { var _v = c.SP; c.V0 = c.SP + 0x20u; }
        c.T4 = c.V0;
        { var _a = c.T4; var _sw = Gte.Read(22); mem.WriteU32(_a, _sw); }
        { var _v = c.SP; c.V0 = c.SP + 0x28u; }
        { var _a = (c.SP + 0x100u); mem.WriteU32(_a, c.V0); }
        { var _v = c.SP; c.V0 = c.SP + 0x40u; }
        { var _a = (c.SP + 0x104u); mem.WriteU32(_a, c.V0); }
        { var _v = c.SP; c.V0 = c.SP + 0x58u; }
        { var _a = (c.SP + 0x108u); mem.WriteU32(_a, c.V0); }
        { var _v = c.SP; c.V0 = c.SP + 0x70u; }
        { var _a = (c.SP + 0x10Cu); mem.WriteU32(_a, c.V0); }
        { var _a = (c.S0 + 0x1Fu); c.V0 = mem.ReadU8(_a); }
        { var _a = (c.S0 + 0x74u); c.V1 = mem.ReadU16(_a); }
        { var _v = c.V0; c.V0 = c.V0 | 0x002Cu; }
        { var _v = c.V1; c.V1 = c.V1 << 16; }
        { var _v = c.V1; c.V1 = (uint)((int)c.V1 >> 14); }
        { var _a = (c.SP + 0x23u); mem.WriteU8(_a, (byte)c.V0); }
        { var _a = (c.S0 + 0x8u); c.V0 = mem.ReadU32(_a); }
        { var _a = (c.S0 + 0x30u); c.A0 = mem.ReadU32(_a); }
        { var _s = c.V1; var _t = c.V0; c.V1 = _s + _t; }
        { var _a = (c.SP + 0x24u); mem.WriteU32(_a, c.V1); }
        { var _a = (c.A0 + 0x3u); c.V0 = mem.ReadWordLeft(c.V0, _a); }
        { var _a = c.A0; c.V0 = mem.ReadWordRight(c.V0, _a); }
        { var _a = (c.SP + 0x3Bu); mem.WriteWordLeft(_a, c.V0); }
        { var _a = (c.SP + 0x38u); mem.WriteWordRight(_a, c.V0); }
        { var _a = (c.S0 + 0x34u); c.V0 = mem.ReadU32(_a); }
        { var _a = (c.S0 + 0x38u); c.V1 = mem.ReadU32(_a); }
        { var _a = (c.V0 + 0x3u); c.A0 = mem.ReadWordLeft(c.A0, _a); }
        { var _a = c.V0; c.A0 = mem.ReadWordRight(c.A0, _a); }
        { var _a = (c.SP + 0x53u); mem.WriteWordLeft(_a, c.A0); }
        { var _a = (c.SP + 0x50u); mem.WriteWordRight(_a, c.A0); }
        { var _a = (c.V1 + 0x3u); c.V0 = mem.ReadWordLeft(c.V0, _a); }
        { var _a = c.V1; c.V0 = mem.ReadWordRight(c.V0, _a); }
        { var _a = (c.SP + 0x6Bu); mem.WriteWordLeft(_a, c.V0); }
        { var _a = (c.SP + 0x68u); mem.WriteWordRight(_a, c.V0); }
        { var _a = (c.S0 + 0x3Cu); c.V0 = mem.ReadU32(_a); }
        { var _a = (c.S0 + 0x30u); c.V1 = mem.ReadU32(_a); }
        { var _a = (c.V0 + 0x3u); c.A0 = mem.ReadWordLeft(c.A0, _a); }
        { var _a = c.V0; c.A0 = mem.ReadWordRight(c.A0, _a); }
        { var _a = (c.SP + 0x83u); mem.WriteWordLeft(_a, c.A0); }
        { var _a = (c.SP + 0x80u); mem.WriteWordRight(_a, c.A0); }
        { var _a = (c.V1 + 0x4u); c.V0 = (uint)(short)mem.ReadU16(_a); }
        { var _a = (c.S0 + 0x34u); c.V1 = mem.ReadU32(_a); }
        { var _a = (c.SP + 0x3Cu); mem.WriteU32(_a, c.V0); }
        { var _a = (c.V1 + 0x4u); c.V0 = (uint)(short)mem.ReadU16(_a); }
        { var _a = (c.S0 + 0x38u); c.V1 = mem.ReadU32(_a); }
        { var _a = (c.SP + 0x54u); mem.WriteU32(_a, c.V0); }
        { var _a = (c.V1 + 0x4u); c.V0 = (uint)(short)mem.ReadU16(_a); }
        { var _a = (c.S0 + 0x3Cu); c.V1 = mem.ReadU32(_a); }
        { var _a = (c.SP + 0x6Cu); mem.WriteU32(_a, c.V0); }
        { var _a = (c.V1 + 0x4u); c.V0 = (uint)(short)mem.ReadU16(_a); }
        { var _a = (c.S0 + 0x20u); c.V1 = mem.ReadU32(_a); }
        { var _a = (c.SP + 0x84u); mem.WriteU32(_a, c.V0); }
        { var _a = (c.V1 + 0x12u); c.V1 = mem.ReadU16(_a); }
        { var _a = (c.S0 + 0x4Cu); c.V0 = mem.ReadU32(_a); }
        { var _s = c.V0; var _t = c.V1; c.V0 = _s + _t; }
        { var _a = (c.V0 + 0x3u); c.V1 = mem.ReadWordLeft(c.V1, _a); }
        { var _a = c.V0; c.V1 = mem.ReadWordRight(c.V1, _a); }
        { var _a = (c.V0 + 0x7u); c.A0 = mem.ReadWordLeft(c.A0, _a); }
        { var _a = (c.V0 + 0x4u); c.A0 = mem.ReadWordRight(c.A0, _a); }
        { var _a = (c.SP + 0x2Bu); mem.WriteWordLeft(_a, c.V1); }
        { var _a = (c.SP + 0x28u); mem.WriteWordRight(_a, c.V1); }
        { var _a = (c.SP + 0x2Fu); mem.WriteWordLeft(_a, c.A0); }
        { var _a = (c.SP + 0x2Cu); mem.WriteWordRight(_a, c.A0); }
        { var _a = (c.S0 + 0x20u); c.V0 = mem.ReadU32(_a); }
        { var _a = (c.V0 + 0x14u); c.V1 = mem.ReadU16(_a); }
        { var _a = (c.S0 + 0x4Cu); c.V0 = mem.ReadU32(_a); }
        { var _s = c.V0; var _t = c.V1; c.V0 = _s + _t; }
        { var _a = (c.S0 + 0x20u); c.V1 = mem.ReadU32(_a); }
        { var _a = (c.V0 + 0x3u); c.A0 = mem.ReadWordLeft(c.A0, _a); }
        { var _a = c.V0; c.A0 = mem.ReadWordRight(c.A0, _a); }
        { var _a = (c.V0 + 0x7u); c.A1 = mem.ReadWordLeft(c.A1, _a); }
        { var _a = (c.V0 + 0x4u); c.A1 = mem.ReadWordRight(c.A1, _a); }
        { var _a = (c.SP + 0x43u); mem.WriteWordLeft(_a, c.A0); }
        { var _a = (c.SP + 0x40u); mem.WriteWordRight(_a, c.A0); }
        { var _a = (c.SP + 0x47u); mem.WriteWordLeft(_a, c.A1); }
        { var _a = (c.SP + 0x44u); mem.WriteWordRight(_a, c.A1); }
        { var _a = (c.S0 + 0x4Cu); c.V0 = mem.ReadU32(_a); }
        { var _a = (c.V1 + 0x16u); c.V1 = mem.ReadU16(_a); }
        { var _s = c.V0; var _t = c.V1; c.V0 = _s + _t; }
        { var _a = (c.S0 + 0x20u); c.V1 = mem.ReadU32(_a); }
        { var _a = (c.V0 + 0x3u); c.A0 = mem.ReadWordLeft(c.A0, _a); }
        { var _a = c.V0; c.A0 = mem.ReadWordRight(c.A0, _a); }
        { var _a = (c.V0 + 0x7u); c.A1 = mem.ReadWordLeft(c.A1, _a); }
        { var _a = (c.V0 + 0x4u); c.A1 = mem.ReadWordRight(c.A1, _a); }
        { var _a = (c.SP + 0x5Bu); mem.WriteWordLeft(_a, c.A0); }
        { var _a = (c.SP + 0x58u); mem.WriteWordRight(_a, c.A0); }
        { var _a = (c.SP + 0x5Fu); mem.WriteWordLeft(_a, c.A1); }
        { var _a = (c.SP + 0x5Cu); mem.WriteWordRight(_a, c.A1); }
        { var _a = (c.S0 + 0x4Cu); c.V0 = mem.ReadU32(_a); }
        { var _a = (c.V1 + 0x18u); c.V1 = mem.ReadU16(_a); }
        { var _a = (c.S0 + 0x14u); c.A0 = mem.ReadU32(_a); }
        { var _s = c.V0; var _t = c.V1; c.V0 = _s + _t; }
        { var _a = (c.S0 + 0x20u); c.V1 = mem.ReadU32(_a); }
        { var _a = (c.V0 + 0x3u); c.A1 = mem.ReadWordLeft(c.A1, _a); }
        { var _a = c.V0; c.A1 = mem.ReadWordRight(c.A1, _a); }
        { var _a = (c.V0 + 0x7u); c.A2 = mem.ReadWordLeft(c.A2, _a); }
        { var _a = (c.V0 + 0x4u); c.A2 = mem.ReadWordRight(c.A2, _a); }
        { var _a = (c.SP + 0x73u); mem.WriteWordLeft(_a, c.A1); }
        { var _a = (c.SP + 0x70u); mem.WriteWordRight(_a, c.A1); }
        { var _a = (c.SP + 0x77u); mem.WriteWordLeft(_a, c.A2); }
        { var _a = (c.SP + 0x74u); mem.WriteWordRight(_a, c.A2); }
        { var _a = c.V1; c.V0 = mem.ReadU32(_a); }
        { var _a = (c.S0 + 0x20u); c.V1 = mem.ReadU32(_a); }
        { var _a = (c.SP + 0x30u); mem.WriteU32(_a, c.V0); }
        { var _a = (c.V1 + 0x4u); c.V0 = mem.ReadU32(_a); }
        { var _a = (c.S0 + 0x20u); c.V1 = mem.ReadU32(_a); }
        { var _a = (c.SP + 0x48u); mem.WriteU32(_a, c.V0); }
        { var _a = (c.V1 + 0x8u); c.V0 = mem.ReadU32(_a); }
        { var _a = (c.S0 + 0x20u); c.V1 = mem.ReadU32(_a); }
        { var _a = (c.SP + 0x60u); mem.WriteU32(_a, c.V0); }
        { var _a = (c.V1 + 0xCu); c.V0 = mem.ReadU32(_a); }
        { var _v = c.SP; c.A1 = c.SP + 0x10u; }
        { var _a = (c.SP + 0x78u); mem.WriteU32(_a, c.V0); }
        c.RA = 0x800371ACu;
        DivQuad(c, m);
        goto L80037B88;
        L800371B4: ;
        { var _a = (c.A0 + 0x12u); c.V0 = mem.ReadU16(_a); }
        { var _a = (c.S0 + 0x44u); c.V1 = mem.ReadU32(_a); }
        { var _a = (c.S0 + 0x20u); c.A0 = mem.ReadU32(_a); }
        { var _s = c.V0; var _t = c.V1; c.V0 = _s + _t; }
        { var _a = (c.S0 + 0x30u); mem.WriteU32(_a, c.V0); }
        { var _a = (c.A0 + 0x12u); c.V0 = mem.ReadU16(_a); }
        { var _a = (c.S0 + 0x44u); c.V1 = mem.ReadU32(_a); }
        { var _a = (c.S0 + 0x20u); c.A0 = mem.ReadU32(_a); }
        { var _s = c.V0; var _t = c.V1; c.V0 = _s + _t; }
        { var _a = (c.S0 + 0x34u); mem.WriteU32(_a, c.V0); }
        { var _a = (c.A0 + 0x16u); c.V0 = mem.ReadU16(_a); }
        { var _a = (c.S0 + 0x44u); c.V1 = mem.ReadU32(_a); }
        { var _s = c.V0; var _t = c.V1; c.V0 = _s + _t; }
        { var _a = (c.S0 + 0x38u); mem.WriteU32(_a, c.V0); }
        { var _a = (c.S0 + 0x30u); c.V0 = mem.ReadU32(_a); }
        { var _a = c.V0; c.V0 = mem.ReadU32(_a); }
        c.T4 = c.V0;
        { var _a = (c.S0 + 0x34u); c.V0 = mem.ReadU32(_a); }
        { var _a = c.V0; c.V0 = mem.ReadU32(_a); }
        c.T5 = c.V0;
        { var _a = (c.S0 + 0x38u); c.V0 = mem.ReadU32(_a); }
        { var _a = c.V0; c.V0 = mem.ReadU32(_a); }
        c.T6 = c.V0;
        Gte.Write(12, c.T4);
        Gte.Write(14, c.T6);
        Gte.Write(13, c.T5);
        Gte.Nclip();
        { var _v = c.S0; c.V0 = c.S0 + 0x70u; }
        c.T4 = c.V0;
        { var _a = c.T4; var _sw = Gte.Read(24); mem.WriteU32(_a, _sw); }
        { var _a = (c.S0 + 0x70u); c.V0 = mem.ReadU32(_a); }
        if ((int)c.V0 <= 0) {
            c.V0 = c.S1;
            goto L80037BB4;
        }
        c.V0 = c.S1;
        { var _a = (c.S0 + 0x76u); c.V0 = mem.ReadU16(_a); }
        { var _a = (c.S0 + 0x30u); c.V1 = mem.ReadU32(_a); }
        { var _v = c.V0; c.V0 = c.V0 << 16; }
        { var _a = (c.V1 + 0x4u); c.V1 = (uint)(short)mem.ReadU16(_a); }
        { var _v = c.V0; c.A0 = (uint)((int)c.V0 >> 16); }
        { var _s = c.V1; var _t = c.A0; c.V1 = (int)_s < (int)_t ? 1u : 0u; }
        if (c.V1 == 0u) {
            c.A1 = 0x55550000u;
            goto L800372B8;
        }
        c.A1 = 0x55550000u;
        { var _a = (c.S0 + 0x34u); c.V0 = mem.ReadU32(_a); }
        { var _a = (c.V0 + 0x4u); c.V0 = (uint)(short)mem.ReadU16(_a); }
        { var _s = c.V0; var _t = c.A0; c.V0 = (int)_s < (int)_t ? 1u : 0u; }
        if (c.V0 == 0u) {
            goto L800372B8;
        }
        { var _a = (c.S0 + 0x38u); c.V0 = mem.ReadU32(_a); }
        { var _a = (c.V0 + 0x4u); c.V0 = (uint)(short)mem.ReadU16(_a); }
        { var _s = c.V0; var _t = c.A0; c.V0 = (int)_s < (int)_t ? 1u : 0u; }
        if (c.V0 != 0u) {
            c.V0 = c.S1;
            goto L80037BB4;
        }
        c.V0 = c.S1;
        L800372B8: ;
        { var _a = (c.S0 + 0x30u); c.V0 = mem.ReadU32(_a); }
        { var _v = c.A1; c.A1 = c.A1 | 0x5556u; }
        { var _a = (c.V0 + 0x4u); c.V1 = (uint)(short)mem.ReadU16(_a); }
        { var _a = (c.S0 + 0x34u); c.V0 = mem.ReadU32(_a); }
        { var _a = (c.S0 + 0x38u); c.A0 = mem.ReadU32(_a); }
        { var _a = (c.V0 + 0x4u); c.V0 = (uint)(short)mem.ReadU16(_a); }
        { var _a = (c.A0 + 0x4u); c.A0 = (uint)(short)mem.ReadU16(_a); }
        { var _s = c.V1; var _t = c.V0; c.V1 = _s + _t; }
        { var _s = c.V1; var _t = c.A0; c.V1 = _s + _t; }
        { var _s = c.V1; var _t = c.A1; var _r = (long)(int)_s * (int)_t; c.LO = (uint)_r; c.HI = (uint)(_r >> 32); }
        { var _v = c.V1; c.V1 = (uint)((int)c.V1 >> 31); }
        c.V0 = c.HI;
        { var _s = c.V0; var _t = c.V1; c.V0 = _s - _t; }
        { var _a = (c.S0 + 0x74u); mem.WriteU16(_a, (ushort)c.V0); }
        { var _v = c.V0; c.V0 = c.V0 << 16; }
        { var _v = c.V0; c.V1 = (uint)((int)c.V0 >> 16); }
        { var _v = c.V1; c.V0 = (int)c.V1 < 1024 ? 1u : 0u; }
        if (c.V0 == 0u) {
            { var _v = 0u; c.V0 = 0u | 0x0400u; }
            goto L80037314;
        }
        { var _v = 0u; c.V0 = 0u | 0x0400u; }
        { var _s = c.V0; var _t = c.V1; c.V0 = _s - _t; }
        { var _v = c.V0; c.V0 = (uint)((int)c.V0 >> 9); }
        { var _v = c.V0; c.V0 = c.V0 + 0x1u; }
        goto L80037318;
        L80037314: ;
        { var _v = 0u; c.V0 = 0u | 0x0001u; }
        L80037318: ;
        { var _a = (c.SP + 0x10u); mem.WriteU32(_a, c.V0); }
        { var _v = 0u; c.V0 = 0u | 0x0140u; }
        { var _a = (c.SP + 0x14u); mem.WriteU32(_a, c.V0); }
        { var _a = (c.S0 + 0x20u); c.V1 = mem.ReadU32(_a); }
        { var _v = 0u; c.V0 = 0u | 0x00F0u; }
        { var _a = (c.SP + 0x18u); mem.WriteU32(_a, c.V0); }
        { var _a = (c.V1 + 0x2u); c.V0 = mem.ReadU16(_a); }
        { var _a = (c.S0 + 0x84u); c.V1 = mem.ReadU16(_a); }
        { var _a = (c.S0 + 0x20u); c.A0 = mem.ReadU32(_a); }
        { var _s = c.V0; var _t = c.V1; c.V0 = _s + _t; }
        { var _a = (c.SP + 0x1Cu); mem.WriteU16(_a, (ushort)c.V0); }
        { var _a = (c.A0 + 0x6u); c.V0 = mem.ReadU16(_a); }
        { var _a = (c.S0 + 0x20u); c.V1 = mem.ReadU32(_a); }
        { var _a = (c.SP + 0x1Eu); mem.WriteU16(_a, (ushort)c.V0); }
        { var _a = (c.V1 + 0xCu); c.V0 = mem.ReadU16(_a); }
        { var _a = (c.S0 + 0x28u); c.V1 = mem.ReadU32(_a); }
        { var _s = c.V0; var _t = c.V1; c.V0 = _s + _t; }
        c.T4 = c.V0;
        { var _a = c.T4; var _lw = mem.ReadU32(_a); Gte.Write(0, _lw); }
        { var _a = (c.T4 + 0x4u); var _lw = mem.ReadU32(_a); Gte.Write(1, _lw); }
        c.T4 = c.S2;
        { var _a = c.T4; var _lw = mem.ReadU32(_a); Gte.Write(6, _lw); }
        { var _a = (c.S0 + 0x30u); c.V0 = mem.ReadU32(_a); }
        { var _a = (c.V0 + 0x6u); c.V0 = mem.ReadU16(_a); }
        c.T4 = c.V0;
        Gte.Write(8, c.T4);
        Gte.NcdsOp(12, true);
        { var _v = c.SP; c.V0 = c.SP + 0x34u; }
        c.T4 = c.V0;
        { var _a = c.T4; var _sw = Gte.Read(22); mem.WriteU32(_a, _sw); }
        { var _a = (c.S0 + 0x20u); c.V0 = mem.ReadU32(_a); }
        { var _a = (c.S0 + 0x28u); c.V1 = mem.ReadU32(_a); }
        { var _a = (c.V0 + 0x10u); c.V0 = mem.ReadU16(_a); }
        { var _s = c.V0; var _t = c.V1; c.V0 = _s + _t; }
        c.T4 = c.V0;
        { var _a = c.T4; var _lw = mem.ReadU32(_a); Gte.Write(0, _lw); }
        { var _a = (c.T4 + 0x4u); var _lw = mem.ReadU32(_a); Gte.Write(1, _lw); }
        c.T4 = c.S2;
        { var _a = c.T4; var _lw = mem.ReadU32(_a); Gte.Write(6, _lw); }
        { var _a = (c.S0 + 0x34u); c.V0 = mem.ReadU32(_a); }
        { var _a = (c.V0 + 0x6u); c.V0 = mem.ReadU16(_a); }
        c.T4 = c.V0;
        Gte.Write(8, c.T4);
        Gte.NcdsOp(12, true);
        { var _v = c.SP; c.V0 = c.SP + 0x4Cu; }
        c.T4 = c.V0;
        { var _a = c.T4; var _sw = Gte.Read(22); mem.WriteU32(_a, _sw); }
        { var _a = (c.S0 + 0x20u); c.V0 = mem.ReadU32(_a); }
        { var _a = (c.S0 + 0x28u); c.V1 = mem.ReadU32(_a); }
        { var _a = (c.V0 + 0x14u); c.V0 = mem.ReadU16(_a); }
        { var _s = c.V0; var _t = c.V1; c.V0 = _s + _t; }
        c.T4 = c.V0;
        { var _a = c.T4; var _lw = mem.ReadU32(_a); Gte.Write(0, _lw); }
        { var _a = (c.T4 + 0x4u); var _lw = mem.ReadU32(_a); Gte.Write(1, _lw); }
        c.T4 = c.S2;
        { var _a = c.T4; var _lw = mem.ReadU32(_a); Gte.Write(6, _lw); }
        { var _a = (c.S0 + 0x38u); c.V0 = mem.ReadU32(_a); }
        { var _a = (c.V0 + 0x6u); c.V0 = mem.ReadU16(_a); }
        c.T4 = c.V0;
        Gte.Write(8, c.T4);
        Gte.NcdsOp(12, true);
        { var _v = c.SP; c.V0 = c.SP + 0x64u; }
        c.T4 = c.V0;
        { var _a = c.T4; var _sw = Gte.Read(22); mem.WriteU32(_a, _sw); }
        { var _a = (c.SP + 0x37u); c.V0 = mem.ReadWordLeft(c.V0, _a); }
        { var _a = (c.SP + 0x34u); c.V0 = mem.ReadWordRight(c.V0, _a); }
        { var _a = (c.SP + 0x23u); mem.WriteWordLeft(_a, c.V0); }
        { var _a = (c.SP + 0x20u); mem.WriteWordRight(_a, c.V0); }
        { var _v = c.SP; c.V0 = c.SP + 0x28u; }
        { var _a = (c.SP + 0xB8u); mem.WriteU32(_a, c.V0); }
        { var _v = c.SP; c.V0 = c.SP + 0x40u; }
        { var _a = (c.SP + 0xBCu); mem.WriteU32(_a, c.V0); }
        { var _v = c.SP; c.V0 = c.SP + 0x58u; }
        { var _a = (c.SP + 0xC0u); mem.WriteU32(_a, c.V0); }
        { var _a = (c.S0 + 0x1Fu); c.V1 = mem.ReadU8(_a); }
        { var _a = (c.S0 + 0x74u); c.V0 = mem.ReadU16(_a); }
        { var _v = c.V1; c.V1 = c.V1 | 0x0034u; }
        { var _v = c.V0; c.V0 = c.V0 << 16; }
        { var _a = (c.SP + 0x23u); mem.WriteU8(_a, (byte)c.V1); }
        { var _a = (c.S0 + 0x8u); c.V1 = mem.ReadU32(_a); }
        { var _v = c.V0; c.V0 = (uint)((int)c.V0 >> 14); }
        { var _s = c.V0; var _t = c.V1; c.V0 = _s + _t; }
        { var _a = (c.SP + 0x24u); mem.WriteU32(_a, c.V0); }
        { var _a = (c.S0 + 0x30u); c.V0 = mem.ReadU32(_a); }
        { var _a = (c.S0 + 0x34u); c.V1 = mem.ReadU32(_a); }
        { var _a = (c.V0 + 0x3u); c.A0 = mem.ReadWordLeft(c.A0, _a); }
        { var _a = c.V0; c.A0 = mem.ReadWordRight(c.A0, _a); }
        { var _a = (c.SP + 0x3Bu); mem.WriteWordLeft(_a, c.A0); }
        { var _a = (c.SP + 0x38u); mem.WriteWordRight(_a, c.A0); }
        { var _a = (c.V1 + 0x3u); c.V0 = mem.ReadWordLeft(c.V0, _a); }
        { var _a = c.V1; c.V0 = mem.ReadWordRight(c.V0, _a); }
        { var _a = (c.SP + 0x53u); mem.WriteWordLeft(_a, c.V0); }
        { var _a = (c.SP + 0x50u); mem.WriteWordRight(_a, c.V0); }
        { var _a = (c.S0 + 0x38u); c.V0 = mem.ReadU32(_a); }
        { var _a = (c.S0 + 0x30u); c.V1 = mem.ReadU32(_a); }
        { var _a = (c.V0 + 0x3u); c.A0 = mem.ReadWordLeft(c.A0, _a); }
        { var _a = c.V0; c.A0 = mem.ReadWordRight(c.A0, _a); }
        { var _a = (c.SP + 0x6Bu); mem.WriteWordLeft(_a, c.A0); }
        { var _a = (c.SP + 0x68u); mem.WriteWordRight(_a, c.A0); }
        { var _a = (c.V1 + 0x4u); c.V0 = (uint)(short)mem.ReadU16(_a); }
        { var _a = (c.S0 + 0x34u); c.V1 = mem.ReadU32(_a); }
        { var _a = (c.SP + 0x3Cu); mem.WriteU32(_a, c.V0); }
        { var _a = (c.V1 + 0x4u); c.V0 = (uint)(short)mem.ReadU16(_a); }
        { var _a = (c.S0 + 0x38u); c.V1 = mem.ReadU32(_a); }
        { var _a = (c.SP + 0x54u); mem.WriteU32(_a, c.V0); }
        { var _a = (c.V1 + 0x4u); c.V0 = (uint)(short)mem.ReadU16(_a); }
        { var _a = (c.S0 + 0x20u); c.V1 = mem.ReadU32(_a); }
        { var _a = (c.SP + 0x6Cu); mem.WriteU32(_a, c.V0); }
        { var _a = (c.V1 + 0xEu); c.V1 = mem.ReadU16(_a); }
        { var _a = (c.S0 + 0x4Cu); c.V0 = mem.ReadU32(_a); }
        { var _s = c.V0; var _t = c.V1; c.V0 = _s + _t; }
        { var _a = (c.S0 + 0x20u); c.V1 = mem.ReadU32(_a); }
        { var _a = (c.V0 + 0x3u); c.A0 = mem.ReadWordLeft(c.A0, _a); }
        { var _a = c.V0; c.A0 = mem.ReadWordRight(c.A0, _a); }
        { var _a = (c.V0 + 0x7u); c.A1 = mem.ReadWordLeft(c.A1, _a); }
        { var _a = (c.V0 + 0x4u); c.A1 = mem.ReadWordRight(c.A1, _a); }
        { var _a = (c.SP + 0x2Bu); mem.WriteWordLeft(_a, c.A0); }
        { var _a = (c.SP + 0x28u); mem.WriteWordRight(_a, c.A0); }
        { var _a = (c.SP + 0x2Fu); mem.WriteWordLeft(_a, c.A1); }
        { var _a = (c.SP + 0x2Cu); mem.WriteWordRight(_a, c.A1); }
        { var _a = (c.S0 + 0x4Cu); c.V0 = mem.ReadU32(_a); }
        { var _a = (c.V1 + 0x12u); c.V1 = mem.ReadU16(_a); }
        { var _s = c.V0; var _t = c.V1; c.V0 = _s + _t; }
        { var _a = (c.V0 + 0x3u); c.V1 = mem.ReadWordLeft(c.V1, _a); }
        { var _a = c.V0; c.V1 = mem.ReadWordRight(c.V1, _a); }
        { var _a = (c.V0 + 0x7u); c.A0 = mem.ReadWordLeft(c.A0, _a); }
        { var _a = (c.V0 + 0x4u); c.A0 = mem.ReadWordRight(c.A0, _a); }
        { var _a = (c.SP + 0x43u); mem.WriteWordLeft(_a, c.V1); }
        { var _a = (c.SP + 0x40u); mem.WriteWordRight(_a, c.V1); }
        { var _a = (c.SP + 0x47u); mem.WriteWordLeft(_a, c.A0); }
        { var _a = (c.SP + 0x44u); mem.WriteWordRight(_a, c.A0); }
        { var _a = (c.S0 + 0x20u); c.V0 = mem.ReadU32(_a); }
        { var _a = (c.V0 + 0x16u); c.V1 = mem.ReadU16(_a); }
        { var _a = (c.S0 + 0x4Cu); c.V0 = mem.ReadU32(_a); }
        { var _a = (c.S0 + 0x14u); c.A0 = mem.ReadU32(_a); }
        { var _s = c.V0; var _t = c.V1; c.V0 = _s + _t; }
        { var _a = (c.S0 + 0x20u); c.V1 = mem.ReadU32(_a); }
        { var _a = (c.V0 + 0x3u); c.A1 = mem.ReadWordLeft(c.A1, _a); }
        { var _a = c.V0; c.A1 = mem.ReadWordRight(c.A1, _a); }
        { var _a = (c.V0 + 0x7u); c.A2 = mem.ReadWordLeft(c.A2, _a); }
        { var _a = (c.V0 + 0x4u); c.A2 = mem.ReadWordRight(c.A2, _a); }
        { var _a = (c.SP + 0x5Bu); mem.WriteWordLeft(_a, c.A1); }
        { var _a = (c.SP + 0x58u); mem.WriteWordRight(_a, c.A1); }
        { var _a = (c.SP + 0x5Fu); mem.WriteWordLeft(_a, c.A2); }
        { var _a = (c.SP + 0x5Cu); mem.WriteWordRight(_a, c.A2); }
        { var _a = c.V1; c.V0 = mem.ReadU32(_a); }
        { var _a = (c.S0 + 0x20u); c.V1 = mem.ReadU32(_a); }
        { var _a = (c.SP + 0x30u); mem.WriteU32(_a, c.V0); }
        { var _a = (c.V1 + 0x4u); c.V0 = mem.ReadU32(_a); }
        { var _a = (c.S0 + 0x20u); c.V1 = mem.ReadU32(_a); }
        { var _a = (c.SP + 0x48u); mem.WriteU32(_a, c.V0); }
        { var _a = (c.V1 + 0x8u); c.V0 = mem.ReadU32(_a); }
        { var _v = c.SP; c.A1 = c.SP + 0x10u; }
        { var _a = (c.SP + 0x60u); mem.WriteU32(_a, c.V0); }
        c.RA = 0x800375DCu;
        DivTri2(c, m);
        goto L80037B88;
        L800375E4: ;
        { var _a = (c.A0 + 0x16u); c.V0 = mem.ReadU16(_a); }
        { var _a = (c.S0 + 0x44u); c.V1 = mem.ReadU32(_a); }
        { var _a = (c.S0 + 0x20u); c.A0 = mem.ReadU32(_a); }
        { var _s = c.V0; var _t = c.V1; c.V0 = _s + _t; }
        { var _a = (c.S0 + 0x30u); mem.WriteU32(_a, c.V0); }
        { var _a = (c.A0 + 0x16u); c.V0 = mem.ReadU16(_a); }
        { var _a = (c.S0 + 0x44u); c.V1 = mem.ReadU32(_a); }
        { var _a = (c.S0 + 0x20u); c.A0 = mem.ReadU32(_a); }
        { var _s = c.V0; var _t = c.V1; c.V0 = _s + _t; }
        { var _a = (c.S0 + 0x34u); mem.WriteU32(_a, c.V0); }
        { var _a = (c.A0 + 0x1Au); c.V0 = mem.ReadU16(_a); }
        { var _a = (c.S0 + 0x44u); c.V1 = mem.ReadU32(_a); }
        { var _a = (c.S0 + 0x20u); c.A0 = mem.ReadU32(_a); }
        { var _s = c.V0; var _t = c.V1; c.V0 = _s + _t; }
        { var _a = (c.S0 + 0x38u); mem.WriteU32(_a, c.V0); }
        { var _a = (c.A0 + 0x1Eu); c.V0 = mem.ReadU16(_a); }
        { var _a = (c.S0 + 0x44u); c.V1 = mem.ReadU32(_a); }
        { var _s = c.V0; var _t = c.V1; c.V0 = _s + _t; }
        { var _a = (c.S0 + 0x3Cu); mem.WriteU32(_a, c.V0); }
        { var _a = (c.S0 + 0x30u); c.V0 = mem.ReadU32(_a); }
        { var _a = c.V0; c.V0 = mem.ReadU32(_a); }
        c.T4 = c.V0;
        { var _a = (c.S0 + 0x34u); c.V0 = mem.ReadU32(_a); }
        { var _a = c.V0; c.V0 = mem.ReadU32(_a); }
        c.T5 = c.V0;
        { var _a = (c.S0 + 0x38u); c.V0 = mem.ReadU32(_a); }
        { var _a = c.V0; c.V0 = mem.ReadU32(_a); }
        c.T6 = c.V0;
        Gte.Write(12, c.T4);
        Gte.Write(14, c.T6);
        Gte.Write(13, c.T5);
        Gte.Nclip();
        c.T4 = c.S0;
        { var _a = c.T4; var _sw = Gte.Read(24); mem.WriteU32(_a, _sw); }
        { var _a = (c.S0 + 0x3Cu); c.V0 = mem.ReadU32(_a); }
        { var _a = c.V0; c.V0 = mem.ReadU32(_a); }
        c.T4 = c.V0;
        { var _a = (c.S0 + 0x38u); c.V0 = mem.ReadU32(_a); }
        { var _a = c.V0; c.V0 = mem.ReadU32(_a); }
        c.T5 = c.V0;
        { var _a = (c.S0 + 0x34u); c.V0 = mem.ReadU32(_a); }
        { var _a = c.V0; c.V0 = mem.ReadU32(_a); }
        c.T6 = c.V0;
        Gte.Write(12, c.T4);
        Gte.Write(14, c.T6);
        Gte.Write(13, c.T5);
        Gte.Nclip();
        { var _v = c.S0; c.V0 = c.S0 + 0x4u; }
        c.T4 = c.V0;
        { var _a = c.T4; var _sw = Gte.Read(24); mem.WriteU32(_a, _sw); }
        { var _a = c.S0; c.V0 = mem.ReadU32(_a); }
        if ((int)c.V0 > 0) {
            { var _v = 0u; c.V0 = 0u | 0x0001u; }
            goto L80037714;
        }
        { var _v = 0u; c.V0 = 0u | 0x0001u; }
        { var _a = (c.S0 + 0x4u); c.V0 = mem.ReadU32(_a); }
        if ((int)c.V0 > 0) {
            { var _v = 0u; c.V0 = 0u | 0x0001u; }
            goto L80037714;
        }
        { var _v = 0u; c.V0 = 0u | 0x0001u; }
        c.V0 = 0xFFFFFFFFu;
        L80037714: ;
        { var _a = (c.S0 + 0x70u); mem.WriteU32(_a, c.V0); }
        { var _a = (c.S0 + 0x70u); c.V0 = mem.ReadU32(_a); }
        if ((int)c.V0 <= 0) {
            c.V0 = c.S1;
            goto L80037BB4;
        }
        c.V0 = c.S1;
        { var _a = (c.S0 + 0x76u); c.V0 = mem.ReadU16(_a); }
        { var _a = (c.S0 + 0x30u); c.V1 = mem.ReadU32(_a); }
        { var _v = c.V0; c.V0 = c.V0 << 16; }
        { var _a = (c.V1 + 0x4u); c.V1 = (uint)(short)mem.ReadU16(_a); }
        { var _v = c.V0; c.A0 = (uint)((int)c.V0 >> 16); }
        { var _s = c.V1; var _t = c.A0; c.V1 = (int)_s < (int)_t ? 1u : 0u; }
        if (c.V1 == 0u) {
            c.A1 = 0x55550000u;
            goto L8003779C;
        }
        c.A1 = 0x55550000u;
        { var _a = (c.S0 + 0x34u); c.V0 = mem.ReadU32(_a); }
        { var _a = (c.V0 + 0x4u); c.V0 = (uint)(short)mem.ReadU16(_a); }
        { var _s = c.V0; var _t = c.A0; c.V0 = (int)_s < (int)_t ? 1u : 0u; }
        if (c.V0 == 0u) {
            goto L8003779C;
        }
        { var _a = (c.S0 + 0x38u); c.V0 = mem.ReadU32(_a); }
        { var _a = (c.V0 + 0x4u); c.V0 = (uint)(short)mem.ReadU16(_a); }
        { var _s = c.V0; var _t = c.A0; c.V0 = (int)_s < (int)_t ? 1u : 0u; }
        if (c.V0 == 0u) {
            goto L8003779C;
        }
        { var _a = (c.S0 + 0x3Cu); c.V0 = mem.ReadU32(_a); }
        { var _a = (c.V0 + 0x4u); c.V0 = (uint)(short)mem.ReadU16(_a); }
        { var _s = c.V0; var _t = c.A0; c.V0 = (int)_s < (int)_t ? 1u : 0u; }
        if (c.V0 != 0u) {
            c.V0 = c.S1;
            goto L80037BB4;
        }
        c.V0 = c.S1;
        L8003779C: ;
        { var _a = (c.S0 + 0x30u); c.V0 = mem.ReadU32(_a); }
        { var _v = c.A1; c.A1 = c.A1 | 0x5556u; }
        { var _a = (c.V0 + 0x4u); c.V1 = (uint)(short)mem.ReadU16(_a); }
        { var _a = (c.S0 + 0x34u); c.V0 = mem.ReadU32(_a); }
        { var _a = (c.S0 + 0x38u); c.A0 = mem.ReadU32(_a); }
        { var _a = (c.V0 + 0x4u); c.V0 = (uint)(short)mem.ReadU16(_a); }
        { var _a = (c.A0 + 0x4u); c.A0 = (uint)(short)mem.ReadU16(_a); }
        { var _s = c.V1; var _t = c.V0; c.V1 = _s + _t; }
        { var _s = c.V1; var _t = c.A0; c.V1 = _s + _t; }
        { var _s = c.V1; var _t = c.A1; var _r = (long)(int)_s * (int)_t; c.LO = (uint)_r; c.HI = (uint)(_r >> 32); }
        { var _v = c.V1; c.V1 = (uint)((int)c.V1 >> 31); }
        c.V0 = c.HI;
        { var _s = c.V0; var _t = c.V1; c.V0 = _s - _t; }
        { var _a = (c.S0 + 0x74u); mem.WriteU16(_a, (ushort)c.V0); }
        { var _v = c.V0; c.V0 = c.V0 << 16; }
        { var _v = c.V0; c.V1 = (uint)((int)c.V0 >> 16); }
        { var _v = c.V1; c.V0 = (int)c.V1 < 1024 ? 1u : 0u; }
        if (c.V0 == 0u) {
            { var _v = 0u; c.V0 = 0u | 0x0400u; }
            goto L800377F8;
        }
        { var _v = 0u; c.V0 = 0u | 0x0400u; }
        { var _s = c.V0; var _t = c.V1; c.V0 = _s - _t; }
        { var _v = c.V0; c.V0 = (uint)((int)c.V0 >> 9); }
        { var _v = c.V0; c.V0 = c.V0 + 0x1u; }
        goto L800377FC;
        L800377F8: ;
        { var _v = 0u; c.V0 = 0u | 0x0001u; }
        L800377FC: ;
        { var _a = (c.SP + 0x10u); mem.WriteU32(_a, c.V0); }
        { var _v = 0u; c.V0 = 0u | 0x0140u; }
        { var _a = (c.SP + 0x14u); mem.WriteU32(_a, c.V0); }
        { var _a = (c.S0 + 0x20u); c.V1 = mem.ReadU32(_a); }
        { var _v = 0u; c.V0 = 0u | 0x00F0u; }
        { var _a = (c.SP + 0x18u); mem.WriteU32(_a, c.V0); }
        { var _a = (c.V1 + 0x2u); c.V0 = mem.ReadU16(_a); }
        { var _a = (c.S0 + 0x84u); c.V1 = mem.ReadU16(_a); }
        { var _a = (c.S0 + 0x20u); c.A0 = mem.ReadU32(_a); }
        { var _s = c.V0; var _t = c.V1; c.V0 = _s + _t; }
        { var _a = (c.SP + 0x1Cu); mem.WriteU16(_a, (ushort)c.V0); }
        { var _a = (c.A0 + 0x6u); c.V0 = mem.ReadU16(_a); }
        { var _a = (c.S0 + 0x20u); c.V1 = mem.ReadU32(_a); }
        { var _a = (c.SP + 0x1Eu); mem.WriteU16(_a, (ushort)c.V0); }
        { var _a = (c.V1 + 0x10u); c.V0 = mem.ReadU16(_a); }
        { var _a = (c.S0 + 0x28u); c.V1 = mem.ReadU32(_a); }
        { var _s = c.V0; var _t = c.V1; c.V0 = _s + _t; }
        c.T4 = c.V0;
        { var _a = c.T4; var _lw = mem.ReadU32(_a); Gte.Write(0, _lw); }
        { var _a = (c.T4 + 0x4u); var _lw = mem.ReadU32(_a); Gte.Write(1, _lw); }
        c.T4 = c.S2;
        { var _a = c.T4; var _lw = mem.ReadU32(_a); Gte.Write(6, _lw); }
        { var _a = (c.S0 + 0x30u); c.V0 = mem.ReadU32(_a); }
        { var _a = (c.V0 + 0x6u); c.V0 = mem.ReadU16(_a); }
        c.T4 = c.V0;
        Gte.Write(8, c.T4);
        Gte.NcdsOp(12, true);
        { var _v = c.SP; c.V0 = c.SP + 0x34u; }
        c.T4 = c.V0;
        { var _a = c.T4; var _sw = Gte.Read(22); mem.WriteU32(_a, _sw); }
        { var _a = (c.S0 + 0x20u); c.V0 = mem.ReadU32(_a); }
        { var _a = (c.S0 + 0x28u); c.V1 = mem.ReadU32(_a); }
        { var _a = (c.V0 + 0x14u); c.V0 = mem.ReadU16(_a); }
        { var _s = c.V0; var _t = c.V1; c.V0 = _s + _t; }
        c.T4 = c.V0;
        { var _a = c.T4; var _lw = mem.ReadU32(_a); Gte.Write(0, _lw); }
        { var _a = (c.T4 + 0x4u); var _lw = mem.ReadU32(_a); Gte.Write(1, _lw); }
        c.T4 = c.S2;
        { var _a = c.T4; var _lw = mem.ReadU32(_a); Gte.Write(6, _lw); }
        { var _a = (c.S0 + 0x34u); c.V0 = mem.ReadU32(_a); }
        { var _a = (c.V0 + 0x6u); c.V0 = mem.ReadU16(_a); }
        c.T4 = c.V0;
        Gte.Write(8, c.T4);
        Gte.NcdsOp(12, true);
        { var _v = c.SP; c.V0 = c.SP + 0x4Cu; }
        c.T4 = c.V0;
        { var _a = c.T4; var _sw = Gte.Read(22); mem.WriteU32(_a, _sw); }
        { var _a = (c.S0 + 0x20u); c.V0 = mem.ReadU32(_a); }
        { var _a = (c.S0 + 0x28u); c.V1 = mem.ReadU32(_a); }
        { var _a = (c.V0 + 0x18u); c.V0 = mem.ReadU16(_a); }
        { var _s = c.V0; var _t = c.V1; c.V0 = _s + _t; }
        c.T4 = c.V0;
        { var _a = c.T4; var _lw = mem.ReadU32(_a); Gte.Write(0, _lw); }
        { var _a = (c.T4 + 0x4u); var _lw = mem.ReadU32(_a); Gte.Write(1, _lw); }
        c.T4 = c.S2;
        { var _a = c.T4; var _lw = mem.ReadU32(_a); Gte.Write(6, _lw); }
        { var _a = (c.S0 + 0x38u); c.V0 = mem.ReadU32(_a); }
        { var _a = (c.V0 + 0x6u); c.V0 = mem.ReadU16(_a); }
        c.T4 = c.V0;
        Gte.Write(8, c.T4);
        Gte.NcdsOp(12, true);
        { var _v = c.SP; c.V0 = c.SP + 0x64u; }
        c.T4 = c.V0;
        { var _a = c.T4; var _sw = Gte.Read(22); mem.WriteU32(_a, _sw); }
        { var _a = (c.S0 + 0x20u); c.V0 = mem.ReadU32(_a); }
        { var _a = (c.S0 + 0x28u); c.V1 = mem.ReadU32(_a); }
        { var _a = (c.V0 + 0x1Cu); c.V0 = mem.ReadU16(_a); }
        { var _s = c.V0; var _t = c.V1; c.V0 = _s + _t; }
        c.T4 = c.V0;
        { var _a = c.T4; var _lw = mem.ReadU32(_a); Gte.Write(0, _lw); }
        { var _a = (c.T4 + 0x4u); var _lw = mem.ReadU32(_a); Gte.Write(1, _lw); }
        c.T4 = c.S2;
        { var _a = c.T4; var _lw = mem.ReadU32(_a); Gte.Write(6, _lw); }
        { var _a = (c.S0 + 0x3Cu); c.V0 = mem.ReadU32(_a); }
        { var _a = (c.V0 + 0x6u); c.V0 = mem.ReadU16(_a); }
        c.T4 = c.V0;
        Gte.Write(8, c.T4);
        Gte.NcdsOp(12, true);
        { var _v = c.SP; c.V0 = c.SP + 0x7Cu; }
        c.T4 = c.V0;
        { var _a = c.T4; var _sw = Gte.Read(22); mem.WriteU32(_a, _sw); }
        { var _a = (c.SP + 0x37u); c.V0 = mem.ReadWordLeft(c.V0, _a); }
        { var _a = (c.SP + 0x34u); c.V0 = mem.ReadWordRight(c.V0, _a); }
        { var _a = (c.SP + 0x23u); mem.WriteWordLeft(_a, c.V0); }
        { var _a = (c.SP + 0x20u); mem.WriteWordRight(_a, c.V0); }
        { var _v = c.SP; c.V0 = c.SP + 0x28u; }
        { var _a = (c.SP + 0x100u); mem.WriteU32(_a, c.V0); }
        { var _v = c.SP; c.V0 = c.SP + 0x40u; }
        { var _a = (c.SP + 0x104u); mem.WriteU32(_a, c.V0); }
        { var _v = c.SP; c.V0 = c.SP + 0x58u; }
        { var _a = (c.SP + 0x108u); mem.WriteU32(_a, c.V0); }
        { var _v = c.SP; c.V0 = c.SP + 0x70u; }
        { var _a = (c.SP + 0x10Cu); mem.WriteU32(_a, c.V0); }
        { var _a = (c.S0 + 0x1Fu); c.V0 = mem.ReadU8(_a); }
        { var _a = (c.S0 + 0x74u); c.V1 = mem.ReadU16(_a); }
        { var _v = c.V0; c.V0 = c.V0 | 0x003Cu; }
        { var _v = c.V1; c.V1 = c.V1 << 16; }
        { var _v = c.V1; c.V1 = (uint)((int)c.V1 >> 14); }
        { var _a = (c.SP + 0x23u); mem.WriteU8(_a, (byte)c.V0); }
        { var _a = (c.S0 + 0x8u); c.V0 = mem.ReadU32(_a); }
        { var _a = (c.S0 + 0x30u); c.A0 = mem.ReadU32(_a); }
        { var _s = c.V1; var _t = c.V0; c.V1 = _s + _t; }
        { var _a = (c.SP + 0x24u); mem.WriteU32(_a, c.V1); }
        { var _a = (c.A0 + 0x3u); c.V0 = mem.ReadWordLeft(c.V0, _a); }
        { var _a = c.A0; c.V0 = mem.ReadWordRight(c.V0, _a); }
        { var _a = (c.SP + 0x3Bu); mem.WriteWordLeft(_a, c.V0); }
        { var _a = (c.SP + 0x38u); mem.WriteWordRight(_a, c.V0); }
        { var _a = (c.S0 + 0x34u); c.V0 = mem.ReadU32(_a); }
        { var _a = (c.S0 + 0x38u); c.V1 = mem.ReadU32(_a); }
        { var _a = (c.V0 + 0x3u); c.A0 = mem.ReadWordLeft(c.A0, _a); }
        { var _a = c.V0; c.A0 = mem.ReadWordRight(c.A0, _a); }
        { var _a = (c.SP + 0x53u); mem.WriteWordLeft(_a, c.A0); }
        { var _a = (c.SP + 0x50u); mem.WriteWordRight(_a, c.A0); }
        { var _a = (c.V1 + 0x3u); c.V0 = mem.ReadWordLeft(c.V0, _a); }
        { var _a = c.V1; c.V0 = mem.ReadWordRight(c.V0, _a); }
        { var _a = (c.SP + 0x6Bu); mem.WriteWordLeft(_a, c.V0); }
        { var _a = (c.SP + 0x68u); mem.WriteWordRight(_a, c.V0); }
        { var _a = (c.S0 + 0x3Cu); c.V0 = mem.ReadU32(_a); }
        { var _a = (c.S0 + 0x30u); c.V1 = mem.ReadU32(_a); }
        { var _a = (c.V0 + 0x3u); c.A0 = mem.ReadWordLeft(c.A0, _a); }
        { var _a = c.V0; c.A0 = mem.ReadWordRight(c.A0, _a); }
        { var _a = (c.SP + 0x83u); mem.WriteWordLeft(_a, c.A0); }
        { var _a = (c.SP + 0x80u); mem.WriteWordRight(_a, c.A0); }
        { var _a = (c.V1 + 0x4u); c.V0 = (uint)(short)mem.ReadU16(_a); }
        { var _a = (c.S0 + 0x34u); c.V1 = mem.ReadU32(_a); }
        { var _a = (c.SP + 0x3Cu); mem.WriteU32(_a, c.V0); }
        { var _a = (c.V1 + 0x4u); c.V0 = (uint)(short)mem.ReadU16(_a); }
        { var _a = (c.S0 + 0x38u); c.V1 = mem.ReadU32(_a); }
        { var _a = (c.SP + 0x54u); mem.WriteU32(_a, c.V0); }
        { var _a = (c.V1 + 0x4u); c.V0 = (uint)(short)mem.ReadU16(_a); }
        { var _a = (c.S0 + 0x3Cu); c.V1 = mem.ReadU32(_a); }
        { var _a = (c.SP + 0x6Cu); mem.WriteU32(_a, c.V0); }
        { var _a = (c.V1 + 0x4u); c.V0 = (uint)(short)mem.ReadU16(_a); }
        { var _a = (c.SP + 0x84u); mem.WriteU32(_a, c.V0); }
        { var _a = (c.S0 + 0x20u); c.V0 = mem.ReadU32(_a); }
        { var _a = (c.V0 + 0x12u); c.V1 = mem.ReadU16(_a); }
        { var _a = (c.S0 + 0x4Cu); c.V0 = mem.ReadU32(_a); }
        { var _s = c.V0; var _t = c.V1; c.V0 = _s + _t; }
        { var _a = (c.S0 + 0x20u); c.V1 = mem.ReadU32(_a); }
        { var _a = (c.V0 + 0x3u); c.A0 = mem.ReadWordLeft(c.A0, _a); }
        { var _a = c.V0; c.A0 = mem.ReadWordRight(c.A0, _a); }
        { var _a = (c.V0 + 0x7u); c.A1 = mem.ReadWordLeft(c.A1, _a); }
        { var _a = (c.V0 + 0x4u); c.A1 = mem.ReadWordRight(c.A1, _a); }
        { var _a = (c.SP + 0x2Bu); mem.WriteWordLeft(_a, c.A0); }
        { var _a = (c.SP + 0x28u); mem.WriteWordRight(_a, c.A0); }
        { var _a = (c.SP + 0x2Fu); mem.WriteWordLeft(_a, c.A1); }
        { var _a = (c.SP + 0x2Cu); mem.WriteWordRight(_a, c.A1); }
        { var _a = (c.S0 + 0x4Cu); c.V0 = mem.ReadU32(_a); }
        { var _a = (c.V1 + 0x16u); c.V1 = mem.ReadU16(_a); }
        { var _s = c.V0; var _t = c.V1; c.V0 = _s + _t; }
        { var _a = (c.S0 + 0x20u); c.V1 = mem.ReadU32(_a); }
        { var _a = (c.V0 + 0x3u); c.A0 = mem.ReadWordLeft(c.A0, _a); }
        { var _a = c.V0; c.A0 = mem.ReadWordRight(c.A0, _a); }
        { var _a = (c.V0 + 0x7u); c.A1 = mem.ReadWordLeft(c.A1, _a); }
        { var _a = (c.V0 + 0x4u); c.A1 = mem.ReadWordRight(c.A1, _a); }
        { var _a = (c.SP + 0x43u); mem.WriteWordLeft(_a, c.A0); }
        { var _a = (c.SP + 0x40u); mem.WriteWordRight(_a, c.A0); }
        { var _a = (c.SP + 0x47u); mem.WriteWordLeft(_a, c.A1); }
        { var _a = (c.SP + 0x44u); mem.WriteWordRight(_a, c.A1); }
        { var _a = (c.S0 + 0x4Cu); c.V0 = mem.ReadU32(_a); }
        { var _a = (c.V1 + 0x1Au); c.V1 = mem.ReadU16(_a); }
        { var _s = c.V0; var _t = c.V1; c.V0 = _s + _t; }
        { var _a = (c.S0 + 0x20u); c.V1 = mem.ReadU32(_a); }
        { var _a = (c.V0 + 0x3u); c.A0 = mem.ReadWordLeft(c.A0, _a); }
        { var _a = c.V0; c.A0 = mem.ReadWordRight(c.A0, _a); }
        { var _a = (c.V0 + 0x7u); c.A1 = mem.ReadWordLeft(c.A1, _a); }
        { var _a = (c.V0 + 0x4u); c.A1 = mem.ReadWordRight(c.A1, _a); }
        { var _a = (c.SP + 0x5Bu); mem.WriteWordLeft(_a, c.A0); }
        { var _a = (c.SP + 0x58u); mem.WriteWordRight(_a, c.A0); }
        { var _a = (c.SP + 0x5Fu); mem.WriteWordLeft(_a, c.A1); }
        { var _a = (c.SP + 0x5Cu); mem.WriteWordRight(_a, c.A1); }
        { var _a = (c.S0 + 0x4Cu); c.V0 = mem.ReadU32(_a); }
        { var _a = (c.V1 + 0x1Eu); c.V1 = mem.ReadU16(_a); }
        { var _a = (c.S0 + 0x14u); c.A0 = mem.ReadU32(_a); }
        { var _s = c.V0; var _t = c.V1; c.V0 = _s + _t; }
        { var _a = (c.S0 + 0x20u); c.V1 = mem.ReadU32(_a); }
        { var _a = (c.V0 + 0x3u); c.A1 = mem.ReadWordLeft(c.A1, _a); }
        { var _a = c.V0; c.A1 = mem.ReadWordRight(c.A1, _a); }
        { var _a = (c.V0 + 0x7u); c.A2 = mem.ReadWordLeft(c.A2, _a); }
        { var _a = (c.V0 + 0x4u); c.A2 = mem.ReadWordRight(c.A2, _a); }
        { var _a = (c.SP + 0x73u); mem.WriteWordLeft(_a, c.A1); }
        { var _a = (c.SP + 0x70u); mem.WriteWordRight(_a, c.A1); }
        { var _a = (c.SP + 0x77u); mem.WriteWordLeft(_a, c.A2); }
        { var _a = (c.SP + 0x74u); mem.WriteWordRight(_a, c.A2); }
        { var _a = c.V1; c.V0 = mem.ReadU32(_a); }
        { var _a = (c.S0 + 0x20u); c.V1 = mem.ReadU32(_a); }
        { var _a = (c.SP + 0x30u); mem.WriteU32(_a, c.V0); }
        { var _a = (c.V1 + 0x4u); c.V0 = mem.ReadU32(_a); }
        { var _a = (c.S0 + 0x20u); c.V1 = mem.ReadU32(_a); }
        { var _a = (c.SP + 0x48u); mem.WriteU32(_a, c.V0); }
        { var _a = (c.V1 + 0x8u); c.V0 = mem.ReadU32(_a); }
        { var _a = (c.S0 + 0x20u); c.V1 = mem.ReadU32(_a); }
        { var _a = (c.SP + 0x60u); mem.WriteU32(_a, c.V0); }
        { var _a = (c.V1 + 0xCu); c.V0 = mem.ReadU32(_a); }
        { var _v = c.SP; c.A1 = c.SP + 0x10u; }
        { var _a = (c.SP + 0x78u); mem.WriteU32(_a, c.V0); }
        c.RA = 0x80037B88u;
        DivQuad2(c, m);
        L80037B88: ;
        { var _a = (c.SP + 0x10u); c.V1 = mem.ReadU32(_a); }
        { var _a = (c.S0 + 0x7Cu); c.A0 = mem.ReadU32(_a); }
        { var _a = (c.S0 + 0x14u); mem.WriteU32(_a, c.V0); }
        { var _a = (c.S0 + 0x14u); c.A1 = mem.ReadU32(_a); }
        { var _a = (c.S0 + 0x18u); c.V0 = mem.ReadU32(_a); }
        { var _v = c.V1; c.V1 = c.V1 ^ 0x0002u; }
        { var _s = c.V1; var _t = c.A0; c.V1 = _s + _t; }
        { var _s = c.V0; var _t = c.A1; c.V0 = _s < _t ? 1u : 0u; }
        if (c.V0 != 0u) {
            { var _a = (c.S0 + 0x7Cu); mem.WriteU32(_a, c.V1); }
            goto L80037BD0;
        }
        { var _a = (c.S0 + 0x7Cu); mem.WriteU32(_a, c.V1); }
        c.V0 = c.S1;
        L80037BB4: ;
        { var _v = c.S1; c.S1 = c.S1 - 0x1u; }
        { var _a = (c.S0 + 0x1Du); c.V1 = mem.ReadU8(_a); }
        { var _a = (c.S0 + 0x20u); c.A0 = mem.ReadU32(_a); }
        { var _v = c.V1; c.V1 = c.V1 << 2; }
        { var _s = c.V1; var _t = c.A0; c.V1 = _s + _t; }
        if (c.V0 != 0u) {
            { var _a = (c.S0 + 0x20u); mem.WriteU32(_a, c.V1); }
            goto L800368B0;
        }
        { var _a = (c.S0 + 0x20u); mem.WriteU32(_a, c.V1); }
        L80037BD0: ;
        { var _a = (c.SP + 0x354u); c.RA = mem.ReadU32(_a); }
        { var _a = (c.SP + 0x350u); c.S2 = mem.ReadU32(_a); }
        { var _a = (c.SP + 0x34Cu); c.S1 = mem.ReadU32(_a); }
        { var _a = (c.SP + 0x348u); c.S0 = mem.ReadU32(_a); }
        { var _v = c.SP; c.SP = c.SP + 0x358u; }
        return;
    }
    // transcribed from generated/game.cs:54234
    static void BodyMap(CpuContext c, IMemory m)
    {
        var mem = (PSMemory)m;
        { var _v = c.SP; c.SP = c.SP - 0x358u; }
        c.V0 = 0x1F800000u;
        { var _a = (c.V0 + 0x44u); c.V0 = mem.ReadU32(_a); }
        c.V1 = 0x1F800000u;
        { var _a = (c.V1 + 0x4Cu); c.V1 = mem.ReadU32(_a); }
        c.A1 = 0x801B0000u;
        { var _a = (c.A1 - 0x1384u); c.A1 = mem.ReadU32(_a); }
        c.A2 = 0x801B0000u;
        { var _a = (c.A2 - 0x1380u); c.A2 = mem.ReadU32(_a); }
        { var _v = c.A0; c.A0 = c.A0 & 0xFFFFu; }
        { var _a = (c.SP + 0x354u); mem.WriteU32(_a, c.RA); }
        { var _a = (c.SP + 0x350u); mem.WriteU32(_a, c.S2); }
        { var _a = (c.SP + 0x34Cu); mem.WriteU32(_a, c.S1); }
        { var _a = (c.SP + 0x348u); mem.WriteU32(_a, c.S0); }
        c.At = 0x1F800000u;
        { var _a = (c.At + 0x50u); mem.WriteU32(_a, c.V1); }
        { var _v = c.A0; c.V1 = c.A0 << 3; }
        { var _s = c.V1; var _t = c.A0; c.V1 = _s - _t; }
        { var _v = c.V1; c.V1 = c.V1 << 2; }
        c.A0 = 0x1F800000u;
        { var _a = (c.A0 + 0x10u); c.A0 = mem.ReadU32(_a); }
        { var _v = c.V1; c.V1 = c.V1 + 0xCu; }
        c.At = 0x1F800000u;
        { var _a = (c.At + 0x48u); mem.WriteU32(_a, c.V0); }
        c.At = 0x1F800000u;
        { var _a = (c.At + 0x58u); mem.WriteU32(_a, c.A1); }
        c.At = 0x1F800000u;
        { var _a = (c.At + 0x5Cu); mem.WriteU32(_a, c.A2); }
        { var _s = c.V1; var _t = c.A0; c.V1 = _s + _t; }
        c.At = 0x1F800000u;
        { var _a = (c.At + 0x24u); mem.WriteU32(_a, c.V1); }
        { var _a = (c.V1 + 0x8u); c.V0 = mem.ReadU32(_a); }
        { var _v = c.V0; c.V0 = c.V0 + 0xCu; }
        { var _s = c.V0; var _t = c.A0; c.V0 = _s + _t; }
        c.At = 0x1F800000u;
        { var _a = (c.At + 0x28u); mem.WriteU32(_a, c.V0); }
        { var _a = (c.V1 + 0x4u); c.S1 = mem.ReadU32(_a); }
        c.S0 = 0x1F800000u;
        c.V0 = c.S1;
        if (c.V0 == 0u) {
            { var _v = c.S1; c.S1 = c.S1 - 0x1u; }
            goto L8003ACCC;
        }
        { var _v = c.S1; c.S1 = c.S1 - 0x1u; }
        L8003ABAC: ;
        Interrupts.Poll(c, m);
        { var _a = (c.S0 + 0x50u); c.A3 = mem.ReadU32(_a); }
        c.T4 = c.A3;
        { var _a = c.T4; var _lw = mem.ReadU32(_a); Gte.Write(0, _lw); }
        { var _a = (c.T4 + 0x4u); var _lw = mem.ReadU32(_a); Gte.Write(1, _lw); }
        Gte.Rtps(12, false);
        { var _a = (c.S0 + 0x48u); c.A3 = mem.ReadU32(_a); }
        c.T4 = c.A3;
        { var _a = c.T4; var _sw = Gte.Read(14); mem.WriteU32(_a, _sw); _cacheSxy = _sw; }
        { var _a = (c.S0 + 0x48u); c.V0 = mem.ReadU32(_a); }
        { var _v = c.V0; c.V0 = c.V0 + 0x4u; }
        c.T4 = c.V0;
        c.T5 = Gte.Read(19);
        NoteCacheSz(_cacheSxy, c.T5);
        { var _v = c.T5; c.T5 = (uint)((int)c.T5 >> 2); }
        { var _a = c.T4; mem.WriteU32(_a, c.T5); }
        { var _a = (c.S0 + 0x58u); c.A1 = mem.ReadU32(_a); }
        { var _v = c.A1; c.V0 = (int)c.A1 < 32000 ? 1u : 0u; }
        if (c.V0 == 0u) {
            { var _v = c.A1; c.V1 = (uint)((int)c.A1 >> 2); }
            goto L8003AC64;
        }
        { var _v = c.A1; c.V1 = (uint)((int)c.A1 >> 2); }
        { var _a = (c.S0 + 0x48u); c.A0 = mem.ReadU32(_a); }
        { var _a = (c.A0 + 0x4u); c.V0 = (uint)(short)mem.ReadU16(_a); }
        { var _s = c.V0; var _t = c.V1; c.V0 = _s - _t; }
        { var _a = (c.S0 + 0x5Cu); c.V1 = mem.ReadU32(_a); }
        { var _v = c.V0; c.V0 = c.V0 << 14; }
        { var _s = c.V1; var _t = c.A1; c.V1 = _s - _t; }
        { var _s = c.V0; var _t = c.V1; if (_t != 0u) { if ((int)_s == int.MinValue && (int)_t == -1) { c.LO = 0x80000000u; c.HI = 0u; } else { c.LO = (uint)((int)_s / (int)_t); c.HI = (uint)((int)_s % (int)_t); } } }
        if (c.V1 != 0u) {
            goto L8003AC40;
        }
        BiosBreak(c, m);
        L8003AC40: ;
        c.At = 0xFFFFFFFFu;
        if (c.V1 != c.At) {
            c.At = 0x80000000u;
            goto L8003AC58;
        }
        c.At = 0x80000000u;
        if (c.V0 != c.At) {
            goto L8003AC58;
        }
        BiosBreak(c, m);
        L8003AC58: ;
        c.V0 = c.LO;
        { var _a = (c.A0 + 0x6u); mem.WriteU16(_a, (ushort)c.V0); }
        goto L8003AC70;
        L8003AC64: ;
        { var _a = (c.S0 + 0x48u); c.V0 = mem.ReadU32(_a); }
        { var _a = (c.V0 + 0x6u); mem.WriteU16(_a, (ushort)0u); }
        L8003AC70: ;
        { var _a = (c.S0 + 0x48u); c.V1 = mem.ReadU32(_a); }
        { var _a = (c.V1 + 0x6u); c.V0 = (uint)(short)mem.ReadU16(_a); }
        { var _v = c.V0; c.V0 = (int)c.V0 < 7952 ? 1u : 0u; }
        if (c.V0 != 0u) {
            { var _v = 0u; c.V0 = 0u | 0x1F0Fu; }
            goto L8003AC94;
        }
        { var _v = 0u; c.V0 = 0u | 0x1F0Fu; }
        { var _a = (c.V1 + 0x6u); mem.WriteU16(_a, (ushort)c.V0); }
        { var _a = (c.S0 + 0x48u); c.V1 = mem.ReadU32(_a); }
        L8003AC94: ;
        { var _a = (c.V1 + 0x6u); c.V0 = (uint)(short)mem.ReadU16(_a); }
        if ((int)c.V0 >= 0) {
            c.V0 = c.S1;
            goto L8003ACAC;
        }
        c.V0 = c.S1;
        { var _a = (c.V1 + 0x6u); mem.WriteU16(_a, (ushort)0u); }
        L8003ACAC: ;
        { var _v = c.S1; c.S1 = c.S1 - 0x1u; }
        { var _a = (c.S0 + 0x48u); c.V1 = mem.ReadU32(_a); }
        { var _a = (c.S0 + 0x50u); c.A0 = mem.ReadU32(_a); }
        { var _v = c.V1; c.V1 = c.V1 + 0x8u; }
        { var _v = c.A0; c.A0 = c.A0 + 0x8u; }
        { var _a = (c.S0 + 0x48u); mem.WriteU32(_a, c.V1); }
        if (c.V0 != 0u) {
            { var _a = (c.S0 + 0x50u); mem.WriteU32(_a, c.A0); }
            goto L8003ABAC;
        }
        { var _a = (c.S0 + 0x50u); mem.WriteU32(_a, c.A0); }
        L8003ACCC: ;
        { var _a = (c.S0 + 0x24u); c.V0 = mem.ReadU32(_a); }
        { var _a = (c.S0 + 0x10u); c.V1 = mem.ReadU32(_a); }
        { var _a = (c.V0 + 0x10u); c.V0 = mem.ReadU32(_a); }
        { var _a = (c.S0 + 0x24u); c.A0 = mem.ReadU32(_a); }
        { var _v = c.V0; c.V0 = c.V0 + 0xCu; }
        { var _s = c.V0; var _t = c.V1; c.V0 = _s + _t; }
        { var _a = (c.S0 + 0x20u); mem.WriteU32(_a, c.V0); }
        { var _a = (c.A0 + 0x14u); c.S1 = mem.ReadU32(_a); }
        { var _a = (c.S0 + 0x68u); c.V0 = mem.ReadU32(_a); }
        { var _s = c.S1; var _t = c.V0; c.V0 = _s + _t; }
        c.V1 = c.S1;
        { var _v = c.S1; c.S1 = c.S1 - 0x1u; }
        if (c.V1 == 0u) {
            { var _a = (c.S0 + 0x68u); mem.WriteU32(_a, c.V0); }
            goto L8003BAE8;
        }
        { var _a = (c.S0 + 0x68u); mem.WriteU32(_a, c.V0); }
        { var _v = c.S0; c.S2 = c.S0 + 0x54u; }
        L8003AD0C: ;
        Interrupts.Poll(c, m);
        { var _a = (c.S0 + 0x20u); c.V0 = mem.ReadU32(_a); }
        { var _a = (c.S0 + 0x20u); c.A0 = mem.ReadU32(_a); }
        { var _a = c.V0; c.V0 = mem.ReadU32(_a); }
        { var _a = (c.S0 + 0x1Cu); mem.WriteU32(_a, c.V0); }
        { var _a = (c.S0 + 0x1Fu); c.V1 = mem.ReadU8(_a); }
        { var _v = c.A0; c.V0 = c.A0 + 0x4u; }
        { var _a = (c.S0 + 0x20u); mem.WriteU32(_a, c.V0); }
        { var _v = 0u; c.V0 = 0u | 0x002Cu; }
        { var _v = c.V1; c.V1 = c.V1 & 0x00FDu; }
        if (c.V1 == c.V0) {
            { var _v = c.V1; c.V0 = (int)c.V1 < 45 ? 1u : 0u; }
            goto L8003B114;
        }
        { var _v = c.V1; c.V0 = (int)c.V1 < 45 ? 1u : 0u; }
        if (c.V0 == 0u) {
            { var _v = 0u; c.V0 = 0u | 0x0024u; }
            goto L8003AD54;
        }
        { var _v = 0u; c.V0 = 0u | 0x0024u; }
        if (c.V1 == c.V0) {
            c.V0 = c.S1;
            goto L8003AD68;
        }
        c.V0 = c.S1;
        goto L8003BACC;
        L8003AD54: ;
        { var _v = 0u; c.V0 = 0u | 0x0034u; }
        if (c.V1 == c.V0) {
            c.V0 = c.S1;
            goto L8003B638;
        }
        c.V0 = c.S1;
        goto L8003BACC;
        L8003AD68: ;
        { var _a = (c.A0 + 0x12u); c.V0 = mem.ReadU16(_a); }
        { var _a = (c.S0 + 0x44u); c.V1 = mem.ReadU32(_a); }
        { var _a = (c.S0 + 0x20u); c.A0 = mem.ReadU32(_a); }
        { var _s = c.V0; var _t = c.V1; c.V0 = _s + _t; }
        { var _a = (c.S0 + 0x30u); mem.WriteU32(_a, c.V0); }
        { var _a = (c.A0 + 0x10u); c.V0 = mem.ReadU16(_a); }
        { var _a = (c.S0 + 0x44u); c.V1 = mem.ReadU32(_a); }
        { var _a = (c.S0 + 0x20u); c.A0 = mem.ReadU32(_a); }
        { var _s = c.V0; var _t = c.V1; c.V0 = _s + _t; }
        { var _a = (c.S0 + 0x34u); mem.WriteU32(_a, c.V0); }
        { var _a = (c.A0 + 0x12u); c.V0 = mem.ReadU16(_a); }
        { var _a = (c.S0 + 0x44u); c.V1 = mem.ReadU32(_a); }
        { var _s = c.V0; var _t = c.V1; c.V0 = _s + _t; }
        { var _a = (c.S0 + 0x38u); mem.WriteU32(_a, c.V0); }
        { var _a = (c.S0 + 0x30u); c.V0 = mem.ReadU32(_a); }
        { var _a = c.V0; c.V0 = mem.ReadU32(_a); }
        c.T4 = c.V0;
        { var _a = (c.S0 + 0x34u); c.V0 = mem.ReadU32(_a); }
        { var _a = c.V0; c.V0 = mem.ReadU32(_a); }
        c.T5 = c.V0;
        { var _a = (c.S0 + 0x38u); c.V0 = mem.ReadU32(_a); }
        { var _a = c.V0; c.V0 = mem.ReadU32(_a); }
        c.T6 = c.V0;
        Gte.Write(12, c.T4);
        Gte.Write(14, c.T6);
        Gte.Write(13, c.T5);
        Gte.Nclip();
        { var _v = c.S0; c.V0 = c.S0 + 0x60u; }
        c.T4 = c.V0;
        { var _a = c.T4; var _sw = Gte.Read(24); mem.WriteU32(_a, _sw); }
        { var _a = (c.S0 + 0x60u); c.V0 = mem.ReadU32(_a); }
        if ((int)c.V0 <= 0) {
            c.V0 = c.S1;
            goto L8003BACC;
        }
        c.V0 = c.S1;
        { var _a = (c.S0 + 0x66u); c.V0 = mem.ReadU16(_a); }
        { var _a = (c.S0 + 0x30u); c.V1 = mem.ReadU32(_a); }
        { var _v = c.V0; c.V0 = c.V0 << 16; }
        { var _a = (c.V1 + 0x4u); c.V1 = (uint)(short)mem.ReadU16(_a); }
        { var _v = c.V0; c.A0 = (uint)((int)c.V0 >> 16); }
        { var _s = c.V1; var _t = c.A0; c.V1 = (int)_s < (int)_t ? 1u : 0u; }
        if (c.V1 == 0u) {
            c.A1 = 0x55550000u;
            goto L8003AE6C;
        }
        c.A1 = 0x55550000u;
        { var _a = (c.S0 + 0x34u); c.V0 = mem.ReadU32(_a); }
        { var _a = (c.V0 + 0x4u); c.V0 = (uint)(short)mem.ReadU16(_a); }
        { var _s = c.V0; var _t = c.A0; c.V0 = (int)_s < (int)_t ? 1u : 0u; }
        if (c.V0 == 0u) {
            goto L8003AE6C;
        }
        { var _a = (c.S0 + 0x38u); c.V0 = mem.ReadU32(_a); }
        { var _a = (c.V0 + 0x4u); c.V0 = (uint)(short)mem.ReadU16(_a); }
        { var _s = c.V0; var _t = c.A0; c.V0 = (int)_s < (int)_t ? 1u : 0u; }
        if (c.V0 != 0u) {
            c.V0 = c.S1;
            goto L8003BACC;
        }
        c.V0 = c.S1;
        L8003AE6C: ;
        { var _a = (c.S0 + 0x30u); c.V0 = mem.ReadU32(_a); }
        { var _v = c.A1; c.A1 = c.A1 | 0x5556u; }
        { var _a = (c.V0 + 0x4u); c.V1 = (uint)(short)mem.ReadU16(_a); }
        { var _a = (c.S0 + 0x34u); c.V0 = mem.ReadU32(_a); }
        { var _a = (c.S0 + 0x38u); c.A0 = mem.ReadU32(_a); }
        { var _a = (c.V0 + 0x4u); c.V0 = (uint)(short)mem.ReadU16(_a); }
        { var _a = (c.A0 + 0x4u); c.A0 = (uint)(short)mem.ReadU16(_a); }
        { var _s = c.V1; var _t = c.V0; c.V1 = _s + _t; }
        { var _s = c.V1; var _t = c.A0; c.V1 = _s + _t; }
        { var _s = c.V1; var _t = c.A1; var _r = (long)(int)_s * (int)_t; c.LO = (uint)_r; c.HI = (uint)(_r >> 32); }
        { var _v = c.V1; c.V1 = (uint)((int)c.V1 >> 31); }
        c.V0 = c.HI;
        { var _s = c.V0; var _t = c.V1; c.V0 = _s - _t; }
        { var _a = (c.S0 + 0x64u); mem.WriteU16(_a, (ushort)c.V0); }
        { var _v = c.V0; c.V0 = c.V0 << 16; }
        { var _v = c.V0; c.V0 = (uint)((int)c.V0 >> 16); }
        { var _v = c.V0; c.V0 = (int)c.V0 < 7952 ? 1u : 0u; }
        if (c.V0 != 0u) {
            { var _v = 0u; c.V0 = 0u | 0x1F0Fu; }
            goto L8003AEBC;
        }
        { var _v = 0u; c.V0 = 0u | 0x1F0Fu; }
        { var _a = (c.S0 + 0x64u); mem.WriteU16(_a, (ushort)c.V0); }
        L8003AEBC: ;
        { var _a = (c.S0 + 0x64u); c.V0 = mem.ReadU16(_a); }
        { var _v = c.V0; c.V0 = c.V0 << 16; }
        { var _v = c.V0; c.V1 = (uint)((int)c.V0 >> 16); }
        { var _v = c.V1; c.V0 = (int)c.V1 < 1024 ? 1u : 0u; }
        if (c.V0 == 0u) {
            { var _v = 0u; c.V0 = 0u | 0x0400u; }
            goto L8003AEE8;
        }
        { var _v = 0u; c.V0 = 0u | 0x0400u; }
        { var _s = c.V0; var _t = c.V1; c.V0 = _s - _t; }
        { var _v = c.V0; c.V0 = (uint)((int)c.V0 >> 9); }
        { var _v = c.V0; c.V0 = c.V0 + 0x1u; }
        goto L8003AEEC;
        L8003AEE8: ;
        { var _v = 0u; c.V0 = 0u | 0x0001u; }
        L8003AEEC: ;
        { var _a = (c.SP + 0x10u); mem.WriteU32(_a, c.V0); }
        { var _v = 0u; c.V0 = 0u | 0x0140u; }
        { var _a = (c.SP + 0x14u); mem.WriteU32(_a, c.V0); }
        { var _a = (c.S0 + 0x20u); c.V1 = mem.ReadU32(_a); }
        { var _v = 0u; c.V0 = 0u | 0x00F0u; }
        { var _a = (c.SP + 0x18u); mem.WriteU32(_a, c.V0); }
        { var _a = (c.V1 + 0x2u); c.V0 = mem.ReadU16(_a); }
        { var _a = (c.S0 + 0x20u); c.V1 = mem.ReadU32(_a); }
        { var _a = (c.SP + 0x1Cu); mem.WriteU16(_a, (ushort)c.V0); }
        { var _a = (c.V1 + 0x6u); c.V0 = mem.ReadU16(_a); }
        { var _a = (c.S0 + 0x20u); c.V1 = mem.ReadU32(_a); }
        { var _a = (c.SP + 0x1Eu); mem.WriteU16(_a, (ushort)c.V0); }
        { var _a = (c.V1 + 0xCu); c.V0 = mem.ReadU16(_a); }
        { var _a = (c.S0 + 0x28u); c.V1 = mem.ReadU32(_a); }
        { var _s = c.V0; var _t = c.V1; c.V0 = _s + _t; }
        c.T4 = c.V0;
        { var _a = c.T4; var _lw = mem.ReadU32(_a); Gte.Write(0, _lw); }
        { var _a = (c.T4 + 0x4u); var _lw = mem.ReadU32(_a); Gte.Write(1, _lw); }
        c.T4 = c.S2;
        { var _a = c.T4; var _lw = mem.ReadU32(_a); Gte.Write(6, _lw); }
        c.A1 = 0x55550000u;
        { var _a = (c.S0 + 0x30u); c.V0 = mem.ReadU32(_a); }
        { var _v = c.A1; c.A1 = c.A1 | 0x5556u; }
        { var _a = (c.V0 + 0x6u); c.V1 = (uint)(short)mem.ReadU16(_a); }
        { var _a = (c.S0 + 0x34u); c.V0 = mem.ReadU32(_a); }
        { var _a = (c.S0 + 0x38u); c.A0 = mem.ReadU32(_a); }
        { var _a = (c.V0 + 0x6u); c.V0 = (uint)(short)mem.ReadU16(_a); }
        { var _a = (c.A0 + 0x6u); c.A0 = (uint)(short)mem.ReadU16(_a); }
        { var _s = c.V1; var _t = c.V0; c.V1 = _s + _t; }
        { var _s = c.V1; var _t = c.A0; c.V1 = _s + _t; }
        { var _s = c.V1; var _t = c.A1; var _r = (long)(int)_s * (int)_t; c.LO = (uint)_r; c.HI = (uint)(_r >> 32); }
        { var _v = c.V1; c.V1 = (uint)((int)c.V1 >> 31); }
        c.V0 = c.HI;
        { var _s = c.V0; var _t = c.V1; c.V0 = _s - _t; }
        c.T4 = c.V0;
        Gte.Write(8, c.T4);
        Gte.NcdsOp(12, true);
        { var _v = c.SP; c.V0 = c.SP + 0x20u; }
        c.T4 = c.V0;
        { var _a = c.T4; var _sw = Gte.Read(22); mem.WriteU32(_a, _sw); }
        { var _v = c.SP; c.V0 = c.SP + 0x28u; }
        { var _a = (c.SP + 0xB8u); mem.WriteU32(_a, c.V0); }
        { var _v = c.SP; c.V0 = c.SP + 0x40u; }
        { var _a = (c.SP + 0xBCu); mem.WriteU32(_a, c.V0); }
        { var _v = c.SP; c.V0 = c.SP + 0x58u; }
        { var _a = (c.SP + 0xC0u); mem.WriteU32(_a, c.V0); }
        { var _a = (c.S0 + 0x1Fu); c.V1 = mem.ReadU8(_a); }
        { var _a = (c.S0 + 0x64u); c.V0 = mem.ReadU16(_a); }
        { var _v = c.V1; c.V1 = c.V1 | 0x0024u; }
        { var _v = c.V0; c.V0 = c.V0 << 16; }
        { var _a = (c.SP + 0x23u); mem.WriteU8(_a, (byte)c.V1); }
        { var _a = (c.S0 + 0x8u); c.V1 = mem.ReadU32(_a); }
        { var _v = c.V0; c.V0 = (uint)((int)c.V0 >> 14); }
        { var _s = c.V0; var _t = c.V1; c.V0 = _s + _t; }
        { var _a = (c.SP + 0x24u); mem.WriteU32(_a, c.V0); }
        { var _a = (c.S0 + 0x30u); c.V0 = mem.ReadU32(_a); }
        { var _a = (c.S0 + 0x34u); c.V1 = mem.ReadU32(_a); }
        { var _a = (c.V0 + 0x3u); c.A0 = mem.ReadWordLeft(c.A0, _a); }
        { var _a = c.V0; c.A0 = mem.ReadWordRight(c.A0, _a); }
        { var _a = (c.SP + 0x3Bu); mem.WriteWordLeft(_a, c.A0); }
        { var _a = (c.SP + 0x38u); mem.WriteWordRight(_a, c.A0); }
        { var _a = (c.V1 + 0x3u); c.V0 = mem.ReadWordLeft(c.V0, _a); }
        { var _a = c.V1; c.V0 = mem.ReadWordRight(c.V0, _a); }
        { var _a = (c.SP + 0x53u); mem.WriteWordLeft(_a, c.V0); }
        { var _a = (c.SP + 0x50u); mem.WriteWordRight(_a, c.V0); }
        { var _a = (c.S0 + 0x38u); c.V0 = mem.ReadU32(_a); }
        { var _a = (c.S0 + 0x30u); c.V1 = mem.ReadU32(_a); }
        { var _a = (c.V0 + 0x3u); c.A0 = mem.ReadWordLeft(c.A0, _a); }
        { var _a = c.V0; c.A0 = mem.ReadWordRight(c.A0, _a); }
        { var _a = (c.SP + 0x6Bu); mem.WriteWordLeft(_a, c.A0); }
        { var _a = (c.SP + 0x68u); mem.WriteWordRight(_a, c.A0); }
        { var _a = (c.V1 + 0x4u); c.V0 = (uint)(short)mem.ReadU16(_a); }
        { var _a = (c.S0 + 0x34u); c.V1 = mem.ReadU32(_a); }
        { var _a = (c.SP + 0x3Cu); mem.WriteU32(_a, c.V0); }
        { var _a = (c.V1 + 0x4u); c.V0 = (uint)(short)mem.ReadU16(_a); }
        { var _a = (c.S0 + 0x38u); c.V1 = mem.ReadU32(_a); }
        { var _a = (c.SP + 0x54u); mem.WriteU32(_a, c.V0); }
        { var _a = (c.V1 + 0x4u); c.V0 = (uint)(short)mem.ReadU16(_a); }
        { var _a = (c.S0 + 0x20u); c.V1 = mem.ReadU32(_a); }
        { var _a = (c.SP + 0x6Cu); mem.WriteU32(_a, c.V0); }
        { var _a = (c.V1 + 0xEu); c.V1 = mem.ReadU16(_a); }
        { var _a = (c.S0 + 0x4Cu); c.V0 = mem.ReadU32(_a); }
        { var _s = c.V0; var _t = c.V1; c.V0 = _s + _t; }
        { var _a = (c.S0 + 0x20u); c.V1 = mem.ReadU32(_a); }
        { var _a = (c.V0 + 0x3u); c.A0 = mem.ReadWordLeft(c.A0, _a); }
        { var _a = c.V0; c.A0 = mem.ReadWordRight(c.A0, _a); }
        { var _a = (c.V0 + 0x7u); c.A1 = mem.ReadWordLeft(c.A1, _a); }
        { var _a = (c.V0 + 0x4u); c.A1 = mem.ReadWordRight(c.A1, _a); }
        { var _a = (c.SP + 0x2Bu); mem.WriteWordLeft(_a, c.A0); }
        { var _a = (c.SP + 0x28u); mem.WriteWordRight(_a, c.A0); }
        { var _a = (c.SP + 0x2Fu); mem.WriteWordLeft(_a, c.A1); }
        { var _a = (c.SP + 0x2Cu); mem.WriteWordRight(_a, c.A1); }
        { var _a = (c.S0 + 0x4Cu); c.V0 = mem.ReadU32(_a); }
        { var _a = (c.V1 + 0x10u); c.V1 = mem.ReadU16(_a); }
        { var _s = c.V0; var _t = c.V1; c.V0 = _s + _t; }
        { var _a = (c.V0 + 0x3u); c.V1 = mem.ReadWordLeft(c.V1, _a); }
        { var _a = c.V0; c.V1 = mem.ReadWordRight(c.V1, _a); }
        { var _a = (c.V0 + 0x7u); c.A0 = mem.ReadWordLeft(c.A0, _a); }
        { var _a = (c.V0 + 0x4u); c.A0 = mem.ReadWordRight(c.A0, _a); }
        { var _a = (c.SP + 0x43u); mem.WriteWordLeft(_a, c.V1); }
        { var _a = (c.SP + 0x40u); mem.WriteWordRight(_a, c.V1); }
        { var _a = (c.SP + 0x47u); mem.WriteWordLeft(_a, c.A0); }
        { var _a = (c.SP + 0x44u); mem.WriteWordRight(_a, c.A0); }
        { var _a = (c.S0 + 0x20u); c.V0 = mem.ReadU32(_a); }
        { var _a = (c.V0 + 0x12u); c.V1 = mem.ReadU16(_a); }
        { var _a = (c.S0 + 0x4Cu); c.V0 = mem.ReadU32(_a); }
        { var _a = (c.S0 + 0x14u); c.A0 = mem.ReadU32(_a); }
        { var _s = c.V0; var _t = c.V1; c.V0 = _s + _t; }
        { var _a = (c.S0 + 0x20u); c.V1 = mem.ReadU32(_a); }
        { var _a = (c.V0 + 0x3u); c.A1 = mem.ReadWordLeft(c.A1, _a); }
        { var _a = c.V0; c.A1 = mem.ReadWordRight(c.A1, _a); }
        { var _a = (c.V0 + 0x7u); c.A2 = mem.ReadWordLeft(c.A2, _a); }
        { var _a = (c.V0 + 0x4u); c.A2 = mem.ReadWordRight(c.A2, _a); }
        { var _a = (c.SP + 0x5Bu); mem.WriteWordLeft(_a, c.A1); }
        { var _a = (c.SP + 0x58u); mem.WriteWordRight(_a, c.A1); }
        { var _a = (c.SP + 0x5Fu); mem.WriteWordLeft(_a, c.A2); }
        { var _a = (c.SP + 0x5Cu); mem.WriteWordRight(_a, c.A2); }
        { var _a = c.V1; c.V0 = mem.ReadU32(_a); }
        { var _a = (c.S0 + 0x20u); c.V1 = mem.ReadU32(_a); }
        { var _a = (c.SP + 0x30u); mem.WriteU32(_a, c.V0); }
        { var _a = (c.V1 + 0x4u); c.V0 = mem.ReadU32(_a); }
        { var _a = (c.S0 + 0x20u); c.V1 = mem.ReadU32(_a); }
        { var _a = (c.SP + 0x48u); mem.WriteU32(_a, c.V0); }
        { var _a = (c.V1 + 0x8u); c.V0 = mem.ReadU32(_a); }
        { var _v = c.SP; c.A1 = c.SP + 0x10u; }
        { var _a = (c.SP + 0x60u); mem.WriteU32(_a, c.V0); }
        c.RA = 0x8003B10Cu;
        DivTri(c, m);
        goto L8003BAA0;
        L8003B114: ;
        { var _a = (c.A0 + 0x16u); c.V0 = mem.ReadU16(_a); }
        { var _a = (c.S0 + 0x44u); c.V1 = mem.ReadU32(_a); }
        { var _a = (c.S0 + 0x20u); c.A0 = mem.ReadU32(_a); }
        { var _s = c.V0; var _t = c.V1; c.V0 = _s + _t; }
        { var _a = (c.S0 + 0x30u); mem.WriteU32(_a, c.V0); }
        { var _a = (c.A0 + 0x14u); c.V0 = mem.ReadU16(_a); }
        { var _a = (c.S0 + 0x44u); c.V1 = mem.ReadU32(_a); }
        { var _a = (c.S0 + 0x20u); c.A0 = mem.ReadU32(_a); }
        { var _s = c.V0; var _t = c.V1; c.V0 = _s + _t; }
        { var _a = (c.S0 + 0x34u); mem.WriteU32(_a, c.V0); }
        { var _a = (c.A0 + 0x16u); c.V0 = mem.ReadU16(_a); }
        { var _a = (c.S0 + 0x44u); c.V1 = mem.ReadU32(_a); }
        { var _a = (c.S0 + 0x20u); c.A0 = mem.ReadU32(_a); }
        { var _s = c.V0; var _t = c.V1; c.V0 = _s + _t; }
        { var _a = (c.S0 + 0x38u); mem.WriteU32(_a, c.V0); }
        { var _a = (c.A0 + 0x18u); c.V0 = mem.ReadU16(_a); }
        { var _a = (c.S0 + 0x44u); c.V1 = mem.ReadU32(_a); }
        { var _s = c.V0; var _t = c.V1; c.V0 = _s + _t; }
        { var _a = (c.S0 + 0x3Cu); mem.WriteU32(_a, c.V0); }
        { var _a = (c.S0 + 0x30u); c.V0 = mem.ReadU32(_a); }
        { var _a = c.V0; c.V0 = mem.ReadU32(_a); }
        c.T4 = c.V0;
        { var _a = (c.S0 + 0x34u); c.V0 = mem.ReadU32(_a); }
        { var _a = c.V0; c.V0 = mem.ReadU32(_a); }
        c.T5 = c.V0;
        { var _a = (c.S0 + 0x38u); c.V0 = mem.ReadU32(_a); }
        { var _a = c.V0; c.V0 = mem.ReadU32(_a); }
        c.T6 = c.V0;
        Gte.Write(12, c.T4);
        Gte.Write(14, c.T6);
        Gte.Write(13, c.T5);
        Gte.Nclip();
        c.T4 = c.S0;
        { var _a = c.T4; var _sw = Gte.Read(24); mem.WriteU32(_a, _sw); }
        { var _a = (c.S0 + 0x3Cu); c.V0 = mem.ReadU32(_a); }
        { var _a = c.V0; c.V0 = mem.ReadU32(_a); }
        c.T4 = c.V0;
        { var _a = (c.S0 + 0x38u); c.V0 = mem.ReadU32(_a); }
        { var _a = c.V0; c.V0 = mem.ReadU32(_a); }
        c.T5 = c.V0;
        { var _a = (c.S0 + 0x34u); c.V0 = mem.ReadU32(_a); }
        { var _a = c.V0; c.V0 = mem.ReadU32(_a); }
        c.T6 = c.V0;
        Gte.Write(12, c.T4);
        Gte.Write(14, c.T6);
        Gte.Write(13, c.T5);
        Gte.Nclip();
        { var _v = c.S0; c.V0 = c.S0 + 0x4u; }
        c.T4 = c.V0;
        { var _a = c.T4; var _sw = Gte.Read(24); mem.WriteU32(_a, _sw); }
        { var _a = c.S0; c.V0 = mem.ReadU32(_a); }
        if ((int)c.V0 > 0) {
            { var _v = 0u; c.V0 = 0u | 0x0001u; }
            goto L8003B244;
        }
        { var _v = 0u; c.V0 = 0u | 0x0001u; }
        { var _a = (c.S0 + 0x4u); c.V0 = mem.ReadU32(_a); }
        if ((int)c.V0 > 0) {
            { var _v = 0u; c.V0 = 0u | 0x0001u; }
            goto L8003B244;
        }
        { var _v = 0u; c.V0 = 0u | 0x0001u; }
        c.V0 = 0xFFFFFFFFu;
        L8003B244: ;
        { var _a = (c.S0 + 0x60u); mem.WriteU32(_a, c.V0); }
        { var _a = (c.S0 + 0x60u); c.V0 = mem.ReadU32(_a); }
        if ((int)c.V0 <= 0) {
            c.V0 = c.S1;
            goto L8003BACC;
        }
        c.V0 = c.S1;
        { var _a = (c.S0 + 0x66u); c.V0 = mem.ReadU16(_a); }
        { var _a = (c.S0 + 0x30u); c.V1 = mem.ReadU32(_a); }
        { var _v = c.V0; c.V0 = c.V0 << 16; }
        { var _a = (c.V1 + 0x4u); c.V1 = (uint)(short)mem.ReadU16(_a); }
        { var _v = c.V0; c.A0 = (uint)((int)c.V0 >> 16); }
        { var _s = c.V1; var _t = c.A0; c.V1 = (int)_s < (int)_t ? 1u : 0u; }
        if (c.V1 == 0u) {
            goto L8003B2CC;
        }
        { var _a = (c.S0 + 0x34u); c.V0 = mem.ReadU32(_a); }
        { var _a = (c.V0 + 0x4u); c.V0 = (uint)(short)mem.ReadU16(_a); }
        { var _s = c.V0; var _t = c.A0; c.V0 = (int)_s < (int)_t ? 1u : 0u; }
        if (c.V0 == 0u) {
            goto L8003B2CC;
        }
        { var _a = (c.S0 + 0x38u); c.V0 = mem.ReadU32(_a); }
        { var _a = (c.V0 + 0x4u); c.V0 = (uint)(short)mem.ReadU16(_a); }
        { var _s = c.V0; var _t = c.A0; c.V0 = (int)_s < (int)_t ? 1u : 0u; }
        if (c.V0 == 0u) {
            goto L8003B2CC;
        }
        { var _a = (c.S0 + 0x3Cu); c.V0 = mem.ReadU32(_a); }
        { var _a = (c.V0 + 0x4u); c.V0 = (uint)(short)mem.ReadU16(_a); }
        { var _s = c.V0; var _t = c.A0; c.V0 = (int)_s < (int)_t ? 1u : 0u; }
        if (c.V0 != 0u) {
            c.V0 = c.S1;
            goto L8003BACC;
        }
        c.V0 = c.S1;
        L8003B2CC: ;
        { var _a = (c.S0 + 0x30u); c.V0 = mem.ReadU32(_a); }
        { var _a = (c.S0 + 0x34u); c.V1 = mem.ReadU32(_a); }
        { var _a = (c.V0 + 0x4u); c.V0 = (uint)(short)mem.ReadU16(_a); }
        { var _a = (c.V1 + 0x4u); c.V1 = (uint)(short)mem.ReadU16(_a); }
        { var _s = c.V0; var _t = c.V1; c.V0 = _s + _t; }
        { var _a = (c.S0 + 0x38u); c.V1 = mem.ReadU32(_a); }
        { var _a = (c.S0 + 0x3Cu); c.A0 = mem.ReadU32(_a); }
        { var _a = (c.V1 + 0x4u); c.V1 = (uint)(short)mem.ReadU16(_a); }
        { var _a = (c.A0 + 0x4u); c.A0 = (uint)(short)mem.ReadU16(_a); }
        { var _s = c.V0; var _t = c.V1; c.V0 = _s + _t; }
        { var _s = c.V0; var _t = c.A0; c.V0 = _s + _t; }
        { var _v = c.V0; c.V0 = (uint)((int)c.V0 >> 2); }
        { var _a = (c.S0 + 0x64u); mem.WriteU16(_a, (ushort)c.V0); }
        { var _v = c.V0; c.V0 = c.V0 << 16; }
        { var _v = c.V0; c.V0 = (uint)((int)c.V0 >> 16); }
        { var _v = c.V0; c.V0 = (int)c.V0 < 7952 ? 1u : 0u; }
        if (c.V0 != 0u) {
            { var _v = 0u; c.V0 = 0u | 0x1F0Fu; }
            goto L8003B31C;
        }
        { var _v = 0u; c.V0 = 0u | 0x1F0Fu; }
        { var _a = (c.S0 + 0x64u); mem.WriteU16(_a, (ushort)c.V0); }
        L8003B31C: ;
        { var _a = (c.S0 + 0x64u); c.V0 = mem.ReadU16(_a); }
        { var _v = c.V0; c.V0 = c.V0 << 16; }
        { var _v = c.V0; c.V1 = (uint)((int)c.V0 >> 16); }
        { var _v = c.V1; c.V0 = (int)c.V1 < 1024 ? 1u : 0u; }
        if (c.V0 == 0u) {
            { var _v = 0u; c.V0 = 0u | 0x0400u; }
            goto L8003B348;
        }
        { var _v = 0u; c.V0 = 0u | 0x0400u; }
        { var _s = c.V0; var _t = c.V1; c.V0 = _s - _t; }
        { var _v = c.V0; c.V0 = (uint)((int)c.V0 >> 9); }
        { var _v = c.V0; c.V0 = c.V0 + 0x1u; }
        goto L8003B34C;
        L8003B348: ;
        { var _v = 0u; c.V0 = 0u | 0x0001u; }
        L8003B34C: ;
        { var _a = (c.SP + 0x10u); mem.WriteU32(_a, c.V0); }
        { var _v = 0u; c.V0 = 0u | 0x0140u; }
        { var _a = (c.SP + 0x14u); mem.WriteU32(_a, c.V0); }
        { var _a = (c.S0 + 0x20u); c.V1 = mem.ReadU32(_a); }
        { var _v = 0u; c.V0 = 0u | 0x00F0u; }
        { var _a = (c.SP + 0x18u); mem.WriteU32(_a, c.V0); }
        { var _a = (c.V1 + 0x2u); c.V0 = mem.ReadU16(_a); }
        { var _a = (c.S0 + 0x20u); c.V1 = mem.ReadU32(_a); }
        { var _a = (c.SP + 0x1Cu); mem.WriteU16(_a, (ushort)c.V0); }
        { var _a = (c.V1 + 0x6u); c.V0 = mem.ReadU16(_a); }
        { var _a = (c.S0 + 0x20u); c.V1 = mem.ReadU32(_a); }
        { var _a = (c.SP + 0x1Eu); mem.WriteU16(_a, (ushort)c.V0); }
        { var _a = (c.V1 + 0x12u); c.V0 = mem.ReadU16(_a); }
        { var _a = (c.S0 + 0x44u); c.V1 = mem.ReadU32(_a); }
        { var _a = (c.S0 + 0x20u); c.A0 = mem.ReadU32(_a); }
        { var _s = c.V0; var _t = c.V1; c.V0 = _s + _t; }
        { var _a = (c.S0 + 0x30u); mem.WriteU32(_a, c.V0); }
        { var _a = (c.A0 + 0x14u); c.V0 = mem.ReadU16(_a); }
        { var _a = (c.S0 + 0x44u); c.V1 = mem.ReadU32(_a); }
        { var _a = (c.S0 + 0x20u); c.A0 = mem.ReadU32(_a); }
        { var _s = c.V0; var _t = c.V1; c.V0 = _s + _t; }
        { var _a = (c.S0 + 0x34u); mem.WriteU32(_a, c.V0); }
        { var _a = (c.A0 + 0x16u); c.V0 = mem.ReadU16(_a); }
        { var _a = (c.S0 + 0x44u); c.V1 = mem.ReadU32(_a); }
        { var _a = (c.S0 + 0x20u); c.A0 = mem.ReadU32(_a); }
        { var _s = c.V0; var _t = c.V1; c.V0 = _s + _t; }
        { var _a = (c.S0 + 0x38u); mem.WriteU32(_a, c.V0); }
        { var _a = (c.A0 + 0x18u); c.V0 = mem.ReadU16(_a); }
        { var _a = (c.S0 + 0x44u); c.V1 = mem.ReadU32(_a); }
        { var _a = (c.S0 + 0x20u); c.A0 = mem.ReadU32(_a); }
        { var _s = c.V0; var _t = c.V1; c.V0 = _s + _t; }
        { var _a = (c.S0 + 0x3Cu); mem.WriteU32(_a, c.V0); }
        { var _a = (c.A0 + 0x10u); c.V0 = mem.ReadU16(_a); }
        { var _a = (c.S0 + 0x28u); c.V1 = mem.ReadU32(_a); }
        { var _s = c.V0; var _t = c.V1; c.V0 = _s + _t; }
        c.T4 = c.V0;
        { var _a = c.T4; var _lw = mem.ReadU32(_a); Gte.Write(0, _lw); }
        { var _a = (c.T4 + 0x4u); var _lw = mem.ReadU32(_a); Gte.Write(1, _lw); }
        c.T4 = c.S2;
        { var _a = c.T4; var _lw = mem.ReadU32(_a); Gte.Write(6, _lw); }
        { var _a = (c.S0 + 0x30u); c.V0 = mem.ReadU32(_a); }
        { var _a = (c.S0 + 0x34u); c.A0 = mem.ReadU32(_a); }
        { var _a = (c.V0 + 0x6u); c.V1 = (uint)(short)mem.ReadU16(_a); }
        { var _a = (c.A0 + 0x6u); c.V0 = (uint)(short)mem.ReadU16(_a); }
        { var _s = c.V1; var _t = c.V0; c.V1 = _s + _t; }
        { var _a = (c.S0 + 0x38u); c.V0 = mem.ReadU32(_a); }
        { var _a = (c.S0 + 0x3Cu); c.A0 = mem.ReadU32(_a); }
        { var _a = (c.V0 + 0x6u); c.V0 = (uint)(short)mem.ReadU16(_a); }
        { var _a = (c.A0 + 0x6u); c.A0 = (uint)(short)mem.ReadU16(_a); }
        { var _s = c.V1; var _t = c.V0; c.V1 = _s + _t; }
        { var _s = c.V1; var _t = c.A0; c.V0 = _s + _t; }
        if ((int)c.V0 >= 0) {
            goto L8003B42C;
        }
        { var _v = c.V0; c.V0 = c.V0 + 0x3u; }
        L8003B42C: ;
        { var _v = c.V0; c.V0 = (uint)((int)c.V0 >> 2); }
        c.T4 = c.V0;
        Gte.Write(8, c.T4);
        Gte.NcdsOp(12, true);
        { var _v = c.SP; c.V0 = c.SP + 0x20u; }
        c.T4 = c.V0;
        { var _a = c.T4; var _sw = Gte.Read(22); mem.WriteU32(_a, _sw); }
        { var _v = c.SP; c.V0 = c.SP + 0x28u; }
        { var _a = (c.SP + 0x100u); mem.WriteU32(_a, c.V0); }
        { var _v = c.SP; c.V0 = c.SP + 0x40u; }
        { var _a = (c.SP + 0x104u); mem.WriteU32(_a, c.V0); }
        { var _v = c.SP; c.V0 = c.SP + 0x58u; }
        { var _a = (c.SP + 0x108u); mem.WriteU32(_a, c.V0); }
        { var _v = c.SP; c.V0 = c.SP + 0x70u; }
        { var _a = (c.SP + 0x10Cu); mem.WriteU32(_a, c.V0); }
        { var _a = (c.S0 + 0x1Fu); c.V0 = mem.ReadU8(_a); }
        { var _a = (c.S0 + 0x64u); c.V1 = mem.ReadU16(_a); }
        { var _v = c.V0; c.V0 = c.V0 | 0x002Cu; }
        { var _v = c.V1; c.V1 = c.V1 << 16; }
        { var _v = c.V1; c.V1 = (uint)((int)c.V1 >> 14); }
        { var _a = (c.SP + 0x23u); mem.WriteU8(_a, (byte)c.V0); }
        { var _a = (c.S0 + 0x8u); c.V0 = mem.ReadU32(_a); }
        { var _a = (c.S0 + 0x30u); c.A0 = mem.ReadU32(_a); }
        { var _s = c.V1; var _t = c.V0; c.V1 = _s + _t; }
        { var _a = (c.SP + 0x24u); mem.WriteU32(_a, c.V1); }
        { var _a = (c.A0 + 0x3u); c.V0 = mem.ReadWordLeft(c.V0, _a); }
        { var _a = c.A0; c.V0 = mem.ReadWordRight(c.V0, _a); }
        { var _a = (c.SP + 0x3Bu); mem.WriteWordLeft(_a, c.V0); }
        { var _a = (c.SP + 0x38u); mem.WriteWordRight(_a, c.V0); }
        { var _a = (c.S0 + 0x34u); c.V0 = mem.ReadU32(_a); }
        { var _a = (c.S0 + 0x38u); c.V1 = mem.ReadU32(_a); }
        { var _a = (c.V0 + 0x3u); c.A0 = mem.ReadWordLeft(c.A0, _a); }
        { var _a = c.V0; c.A0 = mem.ReadWordRight(c.A0, _a); }
        { var _a = (c.SP + 0x53u); mem.WriteWordLeft(_a, c.A0); }
        { var _a = (c.SP + 0x50u); mem.WriteWordRight(_a, c.A0); }
        { var _a = (c.V1 + 0x3u); c.V0 = mem.ReadWordLeft(c.V0, _a); }
        { var _a = c.V1; c.V0 = mem.ReadWordRight(c.V0, _a); }
        { var _a = (c.SP + 0x6Bu); mem.WriteWordLeft(_a, c.V0); }
        { var _a = (c.SP + 0x68u); mem.WriteWordRight(_a, c.V0); }
        { var _a = (c.S0 + 0x3Cu); c.V0 = mem.ReadU32(_a); }
        { var _a = (c.S0 + 0x30u); c.V1 = mem.ReadU32(_a); }
        { var _a = (c.V0 + 0x3u); c.A0 = mem.ReadWordLeft(c.A0, _a); }
        { var _a = c.V0; c.A0 = mem.ReadWordRight(c.A0, _a); }
        { var _a = (c.SP + 0x83u); mem.WriteWordLeft(_a, c.A0); }
        { var _a = (c.SP + 0x80u); mem.WriteWordRight(_a, c.A0); }
        { var _a = (c.V1 + 0x4u); c.V0 = (uint)(short)mem.ReadU16(_a); }
        { var _a = (c.S0 + 0x34u); c.V1 = mem.ReadU32(_a); }
        { var _a = (c.SP + 0x3Cu); mem.WriteU32(_a, c.V0); }
        { var _a = (c.V1 + 0x4u); c.V0 = (uint)(short)mem.ReadU16(_a); }
        { var _a = (c.S0 + 0x38u); c.V1 = mem.ReadU32(_a); }
        { var _a = (c.SP + 0x54u); mem.WriteU32(_a, c.V0); }
        { var _a = (c.V1 + 0x4u); c.V0 = (uint)(short)mem.ReadU16(_a); }
        { var _a = (c.S0 + 0x3Cu); c.V1 = mem.ReadU32(_a); }
        { var _a = (c.SP + 0x6Cu); mem.WriteU32(_a, c.V0); }
        { var _a = (c.V1 + 0x4u); c.V0 = (uint)(short)mem.ReadU16(_a); }
        { var _a = (c.S0 + 0x20u); c.V1 = mem.ReadU32(_a); }
        { var _a = (c.SP + 0x84u); mem.WriteU32(_a, c.V0); }
        { var _a = (c.V1 + 0x12u); c.V1 = mem.ReadU16(_a); }
        { var _a = (c.S0 + 0x4Cu); c.V0 = mem.ReadU32(_a); }
        { var _s = c.V0; var _t = c.V1; c.V0 = _s + _t; }
        { var _a = (c.V0 + 0x3u); c.V1 = mem.ReadWordLeft(c.V1, _a); }
        { var _a = c.V0; c.V1 = mem.ReadWordRight(c.V1, _a); }
        { var _a = (c.V0 + 0x7u); c.A0 = mem.ReadWordLeft(c.A0, _a); }
        { var _a = (c.V0 + 0x4u); c.A0 = mem.ReadWordRight(c.A0, _a); }
        { var _a = (c.SP + 0x2Bu); mem.WriteWordLeft(_a, c.V1); }
        { var _a = (c.SP + 0x28u); mem.WriteWordRight(_a, c.V1); }
        { var _a = (c.SP + 0x2Fu); mem.WriteWordLeft(_a, c.A0); }
        { var _a = (c.SP + 0x2Cu); mem.WriteWordRight(_a, c.A0); }
        { var _a = (c.S0 + 0x20u); c.V0 = mem.ReadU32(_a); }
        { var _a = (c.V0 + 0x14u); c.V1 = mem.ReadU16(_a); }
        { var _a = (c.S0 + 0x4Cu); c.V0 = mem.ReadU32(_a); }
        { var _s = c.V0; var _t = c.V1; c.V0 = _s + _t; }
        { var _a = (c.S0 + 0x20u); c.V1 = mem.ReadU32(_a); }
        { var _a = (c.V0 + 0x3u); c.A0 = mem.ReadWordLeft(c.A0, _a); }
        { var _a = c.V0; c.A0 = mem.ReadWordRight(c.A0, _a); }
        { var _a = (c.V0 + 0x7u); c.A1 = mem.ReadWordLeft(c.A1, _a); }
        { var _a = (c.V0 + 0x4u); c.A1 = mem.ReadWordRight(c.A1, _a); }
        { var _a = (c.SP + 0x43u); mem.WriteWordLeft(_a, c.A0); }
        { var _a = (c.SP + 0x40u); mem.WriteWordRight(_a, c.A0); }
        { var _a = (c.SP + 0x47u); mem.WriteWordLeft(_a, c.A1); }
        { var _a = (c.SP + 0x44u); mem.WriteWordRight(_a, c.A1); }
        { var _a = (c.S0 + 0x4Cu); c.V0 = mem.ReadU32(_a); }
        { var _a = (c.V1 + 0x16u); c.V1 = mem.ReadU16(_a); }
        { var _s = c.V0; var _t = c.V1; c.V0 = _s + _t; }
        { var _a = (c.S0 + 0x20u); c.V1 = mem.ReadU32(_a); }
        { var _a = (c.V0 + 0x3u); c.A0 = mem.ReadWordLeft(c.A0, _a); }
        { var _a = c.V0; c.A0 = mem.ReadWordRight(c.A0, _a); }
        { var _a = (c.V0 + 0x7u); c.A1 = mem.ReadWordLeft(c.A1, _a); }
        { var _a = (c.V0 + 0x4u); c.A1 = mem.ReadWordRight(c.A1, _a); }
        { var _a = (c.SP + 0x5Bu); mem.WriteWordLeft(_a, c.A0); }
        { var _a = (c.SP + 0x58u); mem.WriteWordRight(_a, c.A0); }
        { var _a = (c.SP + 0x5Fu); mem.WriteWordLeft(_a, c.A1); }
        { var _a = (c.SP + 0x5Cu); mem.WriteWordRight(_a, c.A1); }
        { var _a = (c.S0 + 0x4Cu); c.V0 = mem.ReadU32(_a); }
        { var _a = (c.V1 + 0x18u); c.V1 = mem.ReadU16(_a); }
        { var _a = (c.S0 + 0x14u); c.A0 = mem.ReadU32(_a); }
        { var _s = c.V0; var _t = c.V1; c.V0 = _s + _t; }
        { var _a = (c.S0 + 0x20u); c.V1 = mem.ReadU32(_a); }
        { var _a = (c.V0 + 0x3u); c.A1 = mem.ReadWordLeft(c.A1, _a); }
        { var _a = c.V0; c.A1 = mem.ReadWordRight(c.A1, _a); }
        { var _a = (c.V0 + 0x7u); c.A2 = mem.ReadWordLeft(c.A2, _a); }
        { var _a = (c.V0 + 0x4u); c.A2 = mem.ReadWordRight(c.A2, _a); }
        { var _a = (c.SP + 0x73u); mem.WriteWordLeft(_a, c.A1); }
        { var _a = (c.SP + 0x70u); mem.WriteWordRight(_a, c.A1); }
        { var _a = (c.SP + 0x77u); mem.WriteWordLeft(_a, c.A2); }
        { var _a = (c.SP + 0x74u); mem.WriteWordRight(_a, c.A2); }
        { var _a = c.V1; c.V0 = mem.ReadU32(_a); }
        { var _a = (c.S0 + 0x20u); c.V1 = mem.ReadU32(_a); }
        { var _a = (c.SP + 0x30u); mem.WriteU32(_a, c.V0); }
        { var _a = (c.V1 + 0x4u); c.V0 = mem.ReadU32(_a); }
        { var _a = (c.S0 + 0x20u); c.V1 = mem.ReadU32(_a); }
        { var _a = (c.SP + 0x48u); mem.WriteU32(_a, c.V0); }
        { var _a = (c.V1 + 0x8u); c.V0 = mem.ReadU32(_a); }
        { var _a = (c.S0 + 0x20u); c.V1 = mem.ReadU32(_a); }
        { var _a = (c.SP + 0x60u); mem.WriteU32(_a, c.V0); }
        { var _a = (c.V1 + 0xCu); c.V0 = mem.ReadU32(_a); }
        { var _v = c.SP; c.A1 = c.SP + 0x10u; }
        { var _a = (c.SP + 0x78u); mem.WriteU32(_a, c.V0); }
        c.RA = 0x8003B630u;
        DivQuad(c, m);
        goto L8003BAA0;
        L8003B638: ;
        { var _a = (c.A0 + 0x12u); c.V0 = mem.ReadU16(_a); }
        { var _a = (c.S0 + 0x44u); c.V1 = mem.ReadU32(_a); }
        { var _a = (c.S0 + 0x20u); c.A0 = mem.ReadU32(_a); }
        { var _s = c.V0; var _t = c.V1; c.V0 = _s + _t; }
        { var _a = (c.S0 + 0x30u); mem.WriteU32(_a, c.V0); }
        { var _a = (c.A0 + 0x12u); c.V0 = mem.ReadU16(_a); }
        { var _a = (c.S0 + 0x44u); c.V1 = mem.ReadU32(_a); }
        { var _a = (c.S0 + 0x20u); c.A0 = mem.ReadU32(_a); }
        { var _s = c.V0; var _t = c.V1; c.V0 = _s + _t; }
        { var _a = (c.S0 + 0x34u); mem.WriteU32(_a, c.V0); }
        { var _a = (c.A0 + 0x16u); c.V0 = mem.ReadU16(_a); }
        { var _a = (c.S0 + 0x44u); c.V1 = mem.ReadU32(_a); }
        { var _s = c.V0; var _t = c.V1; c.V0 = _s + _t; }
        { var _a = (c.S0 + 0x38u); mem.WriteU32(_a, c.V0); }
        { var _a = (c.S0 + 0x30u); c.V0 = mem.ReadU32(_a); }
        { var _a = c.V0; c.V0 = mem.ReadU32(_a); }
        c.T4 = c.V0;
        { var _a = (c.S0 + 0x34u); c.V0 = mem.ReadU32(_a); }
        { var _a = c.V0; c.V0 = mem.ReadU32(_a); }
        c.T5 = c.V0;
        { var _a = (c.S0 + 0x38u); c.V0 = mem.ReadU32(_a); }
        { var _a = c.V0; c.V0 = mem.ReadU32(_a); }
        c.T6 = c.V0;
        Gte.Write(12, c.T4);
        Gte.Write(14, c.T6);
        Gte.Write(13, c.T5);
        Gte.Nclip();
        { var _v = c.S0; c.V0 = c.S0 + 0x60u; }
        c.T4 = c.V0;
        { var _a = c.T4; var _sw = Gte.Read(24); mem.WriteU32(_a, _sw); }
        { var _a = (c.S0 + 0x60u); c.V0 = mem.ReadU32(_a); }
        if ((int)c.V0 <= 0) {
            c.V0 = c.S1;
            goto L8003BACC;
        }
        c.V0 = c.S1;
        { var _a = (c.S0 + 0x66u); c.V0 = mem.ReadU16(_a); }
        { var _a = (c.S0 + 0x30u); c.V1 = mem.ReadU32(_a); }
        { var _v = c.V0; c.V0 = c.V0 << 16; }
        { var _a = (c.V1 + 0x4u); c.V1 = (uint)(short)mem.ReadU16(_a); }
        { var _v = c.V0; c.A0 = (uint)((int)c.V0 >> 16); }
        { var _s = c.V1; var _t = c.A0; c.V1 = (int)_s < (int)_t ? 1u : 0u; }
        if (c.V1 == 0u) {
            c.A1 = 0x55550000u;
            goto L8003B73C;
        }
        c.A1 = 0x55550000u;
        { var _a = (c.S0 + 0x34u); c.V0 = mem.ReadU32(_a); }
        { var _a = (c.V0 + 0x4u); c.V0 = (uint)(short)mem.ReadU16(_a); }
        { var _s = c.V0; var _t = c.A0; c.V0 = (int)_s < (int)_t ? 1u : 0u; }
        if (c.V0 == 0u) {
            goto L8003B73C;
        }
        { var _a = (c.S0 + 0x38u); c.V0 = mem.ReadU32(_a); }
        { var _a = (c.V0 + 0x4u); c.V0 = (uint)(short)mem.ReadU16(_a); }
        { var _s = c.V0; var _t = c.A0; c.V0 = (int)_s < (int)_t ? 1u : 0u; }
        if (c.V0 != 0u) {
            c.V0 = c.S1;
            goto L8003BACC;
        }
        c.V0 = c.S1;
        L8003B73C: ;
        { var _a = (c.S0 + 0x30u); c.V0 = mem.ReadU32(_a); }
        { var _v = c.A1; c.A1 = c.A1 | 0x5556u; }
        { var _a = (c.V0 + 0x4u); c.V1 = (uint)(short)mem.ReadU16(_a); }
        { var _a = (c.S0 + 0x34u); c.V0 = mem.ReadU32(_a); }
        { var _a = (c.S0 + 0x38u); c.A0 = mem.ReadU32(_a); }
        { var _a = (c.V0 + 0x4u); c.V0 = (uint)(short)mem.ReadU16(_a); }
        { var _a = (c.A0 + 0x4u); c.A0 = (uint)(short)mem.ReadU16(_a); }
        { var _s = c.V1; var _t = c.V0; c.V1 = _s + _t; }
        { var _s = c.V1; var _t = c.A0; c.V1 = _s + _t; }
        { var _s = c.V1; var _t = c.A1; var _r = (long)(int)_s * (int)_t; c.LO = (uint)_r; c.HI = (uint)(_r >> 32); }
        { var _v = c.V1; c.V1 = (uint)((int)c.V1 >> 31); }
        c.V0 = c.HI;
        { var _s = c.V0; var _t = c.V1; c.V0 = _s - _t; }
        { var _a = (c.S0 + 0x64u); mem.WriteU16(_a, (ushort)c.V0); }
        { var _v = c.V0; c.V0 = c.V0 << 16; }
        { var _v = c.V0; c.V0 = (uint)((int)c.V0 >> 16); }
        { var _v = c.V0; c.V0 = (int)c.V0 < 7952 ? 1u : 0u; }
        if (c.V0 != 0u) {
            { var _v = 0u; c.V0 = 0u | 0x1F0Fu; }
            goto L8003B78C;
        }
        { var _v = 0u; c.V0 = 0u | 0x1F0Fu; }
        { var _a = (c.S0 + 0x64u); mem.WriteU16(_a, (ushort)c.V0); }
        L8003B78C: ;
        { var _a = (c.S0 + 0x64u); c.V0 = mem.ReadU16(_a); }
        { var _v = c.V0; c.V0 = c.V0 << 16; }
        { var _v = c.V0; c.V1 = (uint)((int)c.V0 >> 16); }
        { var _v = c.V1; c.V0 = (int)c.V1 < 1024 ? 1u : 0u; }
        if (c.V0 == 0u) {
            { var _v = 0u; c.V0 = 0u | 0x0400u; }
            goto L8003B7B8;
        }
        { var _v = 0u; c.V0 = 0u | 0x0400u; }
        { var _s = c.V0; var _t = c.V1; c.V0 = _s - _t; }
        { var _v = c.V0; c.V0 = (uint)((int)c.V0 >> 9); }
        { var _v = c.V0; c.V0 = c.V0 + 0x1u; }
        goto L8003B7BC;
        L8003B7B8: ;
        { var _v = 0u; c.V0 = 0u | 0x0001u; }
        L8003B7BC: ;
        { var _a = (c.SP + 0x10u); mem.WriteU32(_a, c.V0); }
        { var _v = 0u; c.V0 = 0u | 0x0140u; }
        { var _a = (c.SP + 0x14u); mem.WriteU32(_a, c.V0); }
        { var _a = (c.S0 + 0x20u); c.V1 = mem.ReadU32(_a); }
        { var _v = 0u; c.V0 = 0u | 0x00F0u; }
        { var _a = (c.SP + 0x18u); mem.WriteU32(_a, c.V0); }
        { var _a = (c.V1 + 0x2u); c.V0 = mem.ReadU16(_a); }
        { var _a = (c.S0 + 0x20u); c.V1 = mem.ReadU32(_a); }
        { var _a = (c.SP + 0x1Cu); mem.WriteU16(_a, (ushort)c.V0); }
        { var _a = (c.V1 + 0x6u); c.V0 = mem.ReadU16(_a); }
        { var _a = (c.S0 + 0x20u); c.V1 = mem.ReadU32(_a); }
        { var _a = (c.SP + 0x1Eu); mem.WriteU16(_a, (ushort)c.V0); }
        { var _a = (c.V1 + 0xEu); c.V0 = mem.ReadU16(_a); }
        { var _a = (c.S0 + 0x44u); c.V1 = mem.ReadU32(_a); }
        { var _a = (c.S0 + 0x20u); c.A0 = mem.ReadU32(_a); }
        { var _s = c.V0; var _t = c.V1; c.V0 = _s + _t; }
        { var _a = (c.S0 + 0x30u); mem.WriteU32(_a, c.V0); }
        { var _a = (c.A0 + 0x12u); c.V0 = mem.ReadU16(_a); }
        { var _a = (c.S0 + 0x44u); c.V1 = mem.ReadU32(_a); }
        { var _a = (c.S0 + 0x20u); c.A0 = mem.ReadU32(_a); }
        { var _s = c.V0; var _t = c.V1; c.V0 = _s + _t; }
        { var _a = (c.S0 + 0x34u); mem.WriteU32(_a, c.V0); }
        { var _a = (c.A0 + 0x16u); c.V0 = mem.ReadU16(_a); }
        { var _a = (c.S0 + 0x44u); c.V1 = mem.ReadU32(_a); }
        { var _a = (c.S0 + 0x20u); c.A0 = mem.ReadU32(_a); }
        { var _s = c.V0; var _t = c.V1; c.V0 = _s + _t; }
        { var _a = (c.S0 + 0x38u); mem.WriteU32(_a, c.V0); }
        { var _a = (c.A0 + 0xCu); c.V0 = mem.ReadU16(_a); }
        { var _a = (c.S0 + 0x28u); c.V1 = mem.ReadU32(_a); }
        { var _s = c.V0; var _t = c.V1; c.V0 = _s + _t; }
        c.T4 = c.V0;
        { var _a = c.T4; var _lw = mem.ReadU32(_a); Gte.Write(0, _lw); }
        { var _a = (c.T4 + 0x4u); var _lw = mem.ReadU32(_a); Gte.Write(1, _lw); }
        c.T4 = c.S2;
        { var _a = c.T4; var _lw = mem.ReadU32(_a); Gte.Write(6, _lw); }
        { var _a = (c.S0 + 0x30u); c.V0 = mem.ReadU32(_a); }
        { var _a = (c.V0 + 0x6u); c.V0 = mem.ReadU16(_a); }
        c.T4 = c.V0;
        Gte.Write(8, c.T4);
        Gte.NcdsOp(12, true);
        { var _v = c.SP; c.V0 = c.SP + 0x34u; }
        c.T4 = c.V0;
        { var _a = c.T4; var _sw = Gte.Read(22); mem.WriteU32(_a, _sw); }
        { var _a = (c.S0 + 0x20u); c.V0 = mem.ReadU32(_a); }
        { var _a = (c.S0 + 0x28u); c.V1 = mem.ReadU32(_a); }
        { var _a = (c.V0 + 0x10u); c.V0 = mem.ReadU16(_a); }
        { var _s = c.V0; var _t = c.V1; c.V0 = _s + _t; }
        c.T4 = c.V0;
        { var _a = c.T4; var _lw = mem.ReadU32(_a); Gte.Write(0, _lw); }
        { var _a = (c.T4 + 0x4u); var _lw = mem.ReadU32(_a); Gte.Write(1, _lw); }
        c.T4 = c.S2;
        { var _a = c.T4; var _lw = mem.ReadU32(_a); Gte.Write(6, _lw); }
        { var _a = (c.S0 + 0x34u); c.V0 = mem.ReadU32(_a); }
        { var _a = (c.V0 + 0x6u); c.V0 = mem.ReadU16(_a); }
        c.T4 = c.V0;
        Gte.Write(8, c.T4);
        Gte.NcdsOp(12, true);
        { var _v = c.SP; c.V0 = c.SP + 0x4Cu; }
        c.T4 = c.V0;
        { var _a = c.T4; var _sw = Gte.Read(22); mem.WriteU32(_a, _sw); }
        { var _a = (c.S0 + 0x20u); c.V0 = mem.ReadU32(_a); }
        { var _a = (c.S0 + 0x28u); c.V1 = mem.ReadU32(_a); }
        { var _a = (c.V0 + 0x14u); c.V0 = mem.ReadU16(_a); }
        { var _s = c.V0; var _t = c.V1; c.V0 = _s + _t; }
        c.T4 = c.V0;
        { var _a = c.T4; var _lw = mem.ReadU32(_a); Gte.Write(0, _lw); }
        { var _a = (c.T4 + 0x4u); var _lw = mem.ReadU32(_a); Gte.Write(1, _lw); }
        c.T4 = c.S2;
        { var _a = c.T4; var _lw = mem.ReadU32(_a); Gte.Write(6, _lw); }
        { var _a = (c.S0 + 0x38u); c.V0 = mem.ReadU32(_a); }
        { var _a = (c.V0 + 0x6u); c.V0 = mem.ReadU16(_a); }
        c.T4 = c.V0;
        Gte.Write(8, c.T4);
        Gte.NcdsOp(12, true);
        { var _v = c.SP; c.V0 = c.SP + 0x64u; }
        c.T4 = c.V0;
        { var _a = c.T4; var _sw = Gte.Read(22); mem.WriteU32(_a, _sw); }
        { var _v = c.SP; c.V0 = c.SP + 0x28u; }
        { var _a = (c.SP + 0xB8u); mem.WriteU32(_a, c.V0); }
        { var _v = c.SP; c.V0 = c.SP + 0x40u; }
        { var _a = (c.SP + 0xBCu); mem.WriteU32(_a, c.V0); }
        { var _v = c.SP; c.V0 = c.SP + 0x58u; }
        { var _a = (c.SP + 0xC0u); mem.WriteU32(_a, c.V0); }
        { var _a = (c.S0 + 0x1Fu); c.V1 = mem.ReadU8(_a); }
        { var _a = (c.S0 + 0x64u); c.V0 = mem.ReadU16(_a); }
        { var _v = c.V1; c.V1 = c.V1 | 0x0034u; }
        { var _v = c.V0; c.V0 = c.V0 << 16; }
        { var _a = (c.SP + 0x23u); mem.WriteU8(_a, (byte)c.V1); }
        { var _a = (c.S0 + 0x8u); c.V1 = mem.ReadU32(_a); }
        { var _v = c.V0; c.V0 = (uint)((int)c.V0 >> 14); }
        { var _s = c.V0; var _t = c.V1; c.V0 = _s + _t; }
        { var _a = (c.SP + 0x24u); mem.WriteU32(_a, c.V0); }
        { var _a = (c.S0 + 0x30u); c.V0 = mem.ReadU32(_a); }
        { var _a = (c.S0 + 0x34u); c.V1 = mem.ReadU32(_a); }
        { var _a = (c.V0 + 0x3u); c.A0 = mem.ReadWordLeft(c.A0, _a); }
        { var _a = c.V0; c.A0 = mem.ReadWordRight(c.A0, _a); }
        { var _a = (c.SP + 0x3Bu); mem.WriteWordLeft(_a, c.A0); }
        { var _a = (c.SP + 0x38u); mem.WriteWordRight(_a, c.A0); }
        { var _a = (c.V1 + 0x3u); c.V0 = mem.ReadWordLeft(c.V0, _a); }
        { var _a = c.V1; c.V0 = mem.ReadWordRight(c.V0, _a); }
        { var _a = (c.SP + 0x53u); mem.WriteWordLeft(_a, c.V0); }
        { var _a = (c.SP + 0x50u); mem.WriteWordRight(_a, c.V0); }
        { var _a = (c.S0 + 0x38u); c.V0 = mem.ReadU32(_a); }
        { var _a = (c.S0 + 0x30u); c.V1 = mem.ReadU32(_a); }
        { var _a = (c.V0 + 0x3u); c.A0 = mem.ReadWordLeft(c.A0, _a); }
        { var _a = c.V0; c.A0 = mem.ReadWordRight(c.A0, _a); }
        { var _a = (c.SP + 0x6Bu); mem.WriteWordLeft(_a, c.A0); }
        { var _a = (c.SP + 0x68u); mem.WriteWordRight(_a, c.A0); }
        { var _a = (c.V1 + 0x4u); c.V0 = (uint)(short)mem.ReadU16(_a); }
        { var _a = (c.S0 + 0x34u); c.V1 = mem.ReadU32(_a); }
        { var _a = (c.SP + 0x3Cu); mem.WriteU32(_a, c.V0); }
        { var _a = (c.V1 + 0x4u); c.V0 = (uint)(short)mem.ReadU16(_a); }
        { var _a = (c.S0 + 0x38u); c.V1 = mem.ReadU32(_a); }
        { var _a = (c.SP + 0x54u); mem.WriteU32(_a, c.V0); }
        { var _a = (c.V1 + 0x4u); c.V0 = (uint)(short)mem.ReadU16(_a); }
        { var _a = (c.S0 + 0x20u); c.V1 = mem.ReadU32(_a); }
        { var _a = (c.SP + 0x6Cu); mem.WriteU32(_a, c.V0); }
        { var _a = (c.V1 + 0xEu); c.V1 = mem.ReadU16(_a); }
        { var _a = (c.S0 + 0x4Cu); c.V0 = mem.ReadU32(_a); }
        { var _s = c.V0; var _t = c.V1; c.V0 = _s + _t; }
        { var _a = (c.S0 + 0x20u); c.V1 = mem.ReadU32(_a); }
        { var _a = (c.V0 + 0x3u); c.A0 = mem.ReadWordLeft(c.A0, _a); }
        { var _a = c.V0; c.A0 = mem.ReadWordRight(c.A0, _a); }
        { var _a = (c.V0 + 0x7u); c.A1 = mem.ReadWordLeft(c.A1, _a); }
        { var _a = (c.V0 + 0x4u); c.A1 = mem.ReadWordRight(c.A1, _a); }
        { var _a = (c.SP + 0x2Bu); mem.WriteWordLeft(_a, c.A0); }
        { var _a = (c.SP + 0x28u); mem.WriteWordRight(_a, c.A0); }
        { var _a = (c.SP + 0x2Fu); mem.WriteWordLeft(_a, c.A1); }
        { var _a = (c.SP + 0x2Cu); mem.WriteWordRight(_a, c.A1); }
        { var _a = (c.S0 + 0x4Cu); c.V0 = mem.ReadU32(_a); }
        { var _a = (c.V1 + 0x10u); c.V1 = mem.ReadU16(_a); }
        { var _s = c.V0; var _t = c.V1; c.V0 = _s + _t; }
        { var _a = (c.V0 + 0x3u); c.V1 = mem.ReadWordLeft(c.V1, _a); }
        { var _a = c.V0; c.V1 = mem.ReadWordRight(c.V1, _a); }
        { var _a = (c.V0 + 0x7u); c.A0 = mem.ReadWordLeft(c.A0, _a); }
        { var _a = (c.V0 + 0x4u); c.A0 = mem.ReadWordRight(c.A0, _a); }
        { var _a = (c.SP + 0x43u); mem.WriteWordLeft(_a, c.V1); }
        { var _a = (c.SP + 0x40u); mem.WriteWordRight(_a, c.V1); }
        { var _a = (c.SP + 0x47u); mem.WriteWordLeft(_a, c.A0); }
        { var _a = (c.SP + 0x44u); mem.WriteWordRight(_a, c.A0); }
        { var _a = (c.S0 + 0x20u); c.V0 = mem.ReadU32(_a); }
        { var _a = (c.V0 + 0x12u); c.V1 = mem.ReadU16(_a); }
        { var _a = (c.S0 + 0x4Cu); c.V0 = mem.ReadU32(_a); }
        { var _a = (c.S0 + 0x14u); c.A0 = mem.ReadU32(_a); }
        { var _s = c.V0; var _t = c.V1; c.V0 = _s + _t; }
        { var _a = (c.S0 + 0x20u); c.V1 = mem.ReadU32(_a); }
        { var _a = (c.V0 + 0x3u); c.A1 = mem.ReadWordLeft(c.A1, _a); }
        { var _a = c.V0; c.A1 = mem.ReadWordRight(c.A1, _a); }
        { var _a = (c.V0 + 0x7u); c.A2 = mem.ReadWordLeft(c.A2, _a); }
        { var _a = (c.V0 + 0x4u); c.A2 = mem.ReadWordRight(c.A2, _a); }
        { var _a = (c.SP + 0x5Bu); mem.WriteWordLeft(_a, c.A1); }
        { var _a = (c.SP + 0x58u); mem.WriteWordRight(_a, c.A1); }
        { var _a = (c.SP + 0x5Fu); mem.WriteWordLeft(_a, c.A2); }
        { var _a = (c.SP + 0x5Cu); mem.WriteWordRight(_a, c.A2); }
        { var _a = c.V1; c.V0 = mem.ReadU32(_a); }
        { var _a = (c.S0 + 0x20u); c.V1 = mem.ReadU32(_a); }
        { var _a = (c.SP + 0x30u); mem.WriteU32(_a, c.V0); }
        { var _a = (c.V1 + 0x4u); c.V0 = mem.ReadU32(_a); }
        { var _a = (c.S0 + 0x20u); c.V1 = mem.ReadU32(_a); }
        { var _a = (c.SP + 0x48u); mem.WriteU32(_a, c.V0); }
        { var _a = (c.V1 + 0x8u); c.V0 = mem.ReadU32(_a); }
        { var _v = c.SP; c.A1 = c.SP + 0x10u; }
        { var _a = (c.SP + 0x60u); mem.WriteU32(_a, c.V0); }
        c.RA = 0x8003BAA0u;
        DivTri2(c, m);
        L8003BAA0: ;
        { var _a = (c.SP + 0x10u); c.V1 = mem.ReadU32(_a); }
        { var _a = (c.S0 + 0x6Cu); c.A0 = mem.ReadU32(_a); }
        { var _a = (c.S0 + 0x14u); mem.WriteU32(_a, c.V0); }
        { var _a = (c.S0 + 0x14u); c.A1 = mem.ReadU32(_a); }
        { var _a = (c.S0 + 0x18u); c.V0 = mem.ReadU32(_a); }
        { var _v = c.V1; c.V1 = c.V1 ^ 0x0002u; }
        { var _s = c.V1; var _t = c.A0; c.V1 = _s + _t; }
        { var _s = c.V0; var _t = c.A1; c.V0 = _s < _t ? 1u : 0u; }
        if (c.V0 != 0u) {
            { var _a = (c.S0 + 0x6Cu); mem.WriteU32(_a, c.V1); }
            goto L8003BAE8;
        }
        { var _a = (c.S0 + 0x6Cu); mem.WriteU32(_a, c.V1); }
        c.V0 = c.S1;
        L8003BACC: ;
        { var _v = c.S1; c.S1 = c.S1 - 0x1u; }
        { var _a = (c.S0 + 0x1Du); c.V1 = mem.ReadU8(_a); }
        { var _a = (c.S0 + 0x20u); c.A0 = mem.ReadU32(_a); }
        { var _v = c.V1; c.V1 = c.V1 << 2; }
        { var _s = c.V1; var _t = c.A0; c.V1 = _s + _t; }
        if (c.V0 != 0u) {
            { var _a = (c.S0 + 0x20u); mem.WriteU32(_a, c.V1); }
            goto L8003AD0C;
        }
        { var _a = (c.S0 + 0x20u); mem.WriteU32(_a, c.V1); }
        L8003BAE8: ;
        { var _a = (c.SP + 0x354u); c.RA = mem.ReadU32(_a); }
        { var _a = (c.SP + 0x350u); c.S2 = mem.ReadU32(_a); }
        { var _a = (c.SP + 0x34Cu); c.S1 = mem.ReadU32(_a); }
        { var _a = (c.SP + 0x348u); c.S0 = mem.ReadU32(_a); }
        { var _v = c.SP; c.SP = c.SP + 0x358u; }
        return;
    }
}
