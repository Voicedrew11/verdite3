using System.Reflection;
using System.Text.Json;
using RecompOne.Runtime;
using RecompOne.Runtime.Context;
using RecompOne.Runtime.Memory;
using RecompOne.Runtime.Modding;
using Game = Recompiled.KingsField3_game;

namespace SceneProbe;

/// <summary>Compare synthetic front/cell native calls with recompiled calls,
/// including guest state, packet output and nested callee order.</summary>
public static class NativeInnerFixtures
{
    const uint Pad = 0x1F800000;
    const uint MeshBase = 0x80040000;      // 28-byte mesh-table entries, one per kind
    const uint VertexBase = 0x80042000;    // source 8-byte vertex records
    const uint ProjectedBase = 0x80043000; // projected 8-byte vertex cache (pad+0x44)
    const uint NormalBase = 0x80044000;    // 8-byte light records (3 halves + pad)
    const uint FaceBase = 0x80046000;      // face records: command word + body
    const uint ColourTable = 0x8004C000;   // the front assembler's two OT heads
    const uint PacketBase = 0x80050000;    // emitted primitive packets
    const uint PacketLimit = 0x8005C000;
    const uint StackTop = 0x801FF000;
    const uint StackSpan = 0x4000;
    const uint CellBase = 0x801D4464;      // the map cell table (10 bytes per cell)
    const uint CellX = 3 * 2048 + 1024, CellZ = 2 * 2048 + 1024, CellY = unchecked((uint)-(3 * 128));
    const uint LightBase = 0x801AEEFC;     // 108-byte cell light/rotation records
    const uint CameraBase = 0x801AEB4C;    // the camera rotation/translation blocks
    const uint MeshTablePointer = 0x801A929C;
    const uint KindSpan = 0x1000;          // per-kind face/vertex/normal region
    const uint RecordSpan = 0x44;          // 4-byte command word + 16 words of body
    const byte PacketMark = 0xAA;          // pre-fill, so "nothing written" is visible
    const byte KindByte = 0x24;            // RGBC's code: what each packet's command is
    const int PadWords = 256;

    sealed record Call(int Id, uint Ra, uint Sp, uint A0, uint A1, uint A2, uint A3);

    sealed class Observation
    {
        public CpuSnapshot Cpu;
        public byte[] Ram = [];
        public uint[] Pad = [];
        public Gte.State Gte = new();
        public long Polls;
    }

    static Action<bool, string> _check = (_, _) => { };
    static int _assertions, _cases;
    static int _phase;
    static readonly List<Call> Reference = [], Native = [];
    static readonly string[] Callees = ["func_8003BB04", "func_80035358", "func_80039D50", "func_8003AB04"];
    static bool _hooked;
    static MethodInfo _runCell = null!, _runFrontAssembler = null!;

    static void Assert(bool result, string reason)
    {
        _assertions++;
        _check(result, reason);
    }

    /// <summary>Runs every inner fixture; returns the number of assertions.</summary>
    public static int Run(Action<bool, string> check, string output)
    {
        _check = check; _assertions = 0; _cases = 0;
        _runFrontAssembler = typeof(Kf3.NativeScene).GetMethod("RunFrontAssembler", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("NativeScene.RunFrontAssembler is missing");
        _runCell = typeof(Kf3.NativeScene).GetMethod("RunCell", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("NativeScene.RunCell is missing");
        InstallCalleeHooks();
        var report = new List<object>();
        FrontAssemblerFixtures(report);
        CellFixtures(report);
        File.WriteAllText(Path.Combine(output, "inner-fixtures.json"),
            JsonSerializer.Serialize(new { calleeHooks = _hooked, callees = Callees, cases = report },
                new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Native inner fixtures: {_cases} recompiled/native comparisons, {_assertions} assertions; " +
                          $"nested callee order {( _hooked ? "checked" : "unavailable")}");
        return _assertions;
    }

    // --- the comparison ---------------------------------------------------------

    /// <summary>Both routines are run from the identical seeded state in their own
    /// memory instance, so the comparison has no restore step to get wrong.</summary>
    static void Compare(string name, string routine, Action<PSMemory> seed, CpuSnapshot entry,
        Action<CpuContext, PSMemory> original, Action<CpuContext, PSMemory> native, List<object> report)
    {
        _cases++;
        var gte = EntryGte();
        // Both memories exist before either run: the runtime's timers/GPU globals
        // then point at the same instance for both.
        var first = new PSMemory();
        var second = new PSMemory();
        Reference.Clear(); Native.Clear();
        Observation reference = Observe(seed, entry, original, 1, gte, first);
        Observation actual = Observe(seed, entry, native, 2, gte, second);

        string? ram = FirstRamDifference(reference.Ram, actual.Ram);
        string? pad = FirstPadDifference(reference.Pad, actual.Pad);
        string? cpu = FirstCpuDifference(reference.Cpu, actual.Cpu);
        string? gteDiff = Gte.Diff(reference.Gte, actual.Gte);
        string? calls = _hooked ? FirstCallDifference() : null;
        string? stack = FirstStackDifference(reference.Ram, actual.Ram, entry.SP, StackSpan);
        Assert(ram == null, $"{name}: original {routine} and the native reference differ in RAM: {ram}");
        Assert(pad == null, $"{name}: original {routine} and the native reference differ in the scratchpad: {pad}");
        Assert(cpu == null, $"{name}: original {routine} and the native reference differ in the CPU snapshot: {cpu}");
        Assert(gteDiff == null, $"{name}: original {routine} and the native reference differ in the GTE: {gteDiff}");
        Assert(stack == null, $"{name}: original {routine} and the native reference differ in the {StackSpan:X} stack window: {stack}");
        Assert(calls == null, $"{name}: original {routine} and the native reference call their nested routines differently: {calls}");
        Assert(reference.Polls == 0 && actual.Polls == 0,
            $"{name}: an interrupt poll ran its slow path ({reference.Polls}/{actual.Polls}), so the state is not testable");

        report.Add(new
        {
            name, routine, ramBytes = ram == null ? reference.Ram.Length : 0,
            scratchWords = pad == null ? PadWords : 0, cpuFields = cpu == null ? typeof(CpuSnapshot).GetFields().Length : 0,
            calls = _hooked ? Reference.Count : -1, stackWindow = StackSpan,
        });
    }

    static Observation Observe(Action<PSMemory> seed, CpuSnapshot entry, Action<CpuContext, PSMemory> body,
        int phase, Gte.State gte, PSMemory memory)
    {
        seed(memory);
        Gte.Load(gte);
        var cpu = new CpuContext();
        cpu.Restore(entry);
        long polls = Interrupts.SlowPolls;
        _phase = phase;
        try { body(cpu, memory); }
        finally { _phase = 0; }
        var observation = new Observation { Cpu = cpu.Snapshot(), Ram = memory.Ram.ToArray(), Pad = new uint[PadWords] };
        Gte.Save(observation.Gte);
        for (int i = 0; i < PadWords; i++) observation.Pad[i] = memory.ReadU32(Pad + (uint)i * 4);
        observation.Polls = Interrupts.SlowPolls - polls;
        return observation;
    }

    /// <summary>A non-zero GTE state, so a compared GTE register carries meaning.</summary>
    static Gte.State EntryGte()
    {
        for (int r = 0; r < 32; r++) Gte.Write(r, 0x0003_0000u + (uint)r * 0x0001_0101u);
        for (int r = 0; r < 31; r++) Gte.WriteControl(r, 0x0000_0400u + (uint)r * 0x0000_1111u);
        Gte.WriteControl(8, 0x0000_1000u); Gte.WriteControl(9, 0x0000_1000u); Gte.WriteControl(10, 0x0000_1000u);
        Gte.WriteControl(11, 0x0000_1000u); Gte.WriteControl(12, 0x0000_0200u);
        Gte.WriteControl(16, 0x0000_1000u); Gte.WriteControl(17, 0x0000_1000u); Gte.WriteControl(18, 0x0000_1000u);
        Gte.WriteControl(19, 0x0000_1000u); Gte.WriteControl(20, 0x0000_1000u);
        Gte.WriteControl(21, 0x0000_0800u); Gte.WriteControl(22, 0x0000_0400u);
        Gte.WriteControl(24, 160); Gte.WriteControl(25, 120); Gte.WriteControl(26, 200);
        var state = new Gte.State();
        Gte.Save(state);
        return state;
    }

    static CpuSnapshot EntryCpu(uint a0, uint a1, uint a2, uint a3)
    {
        var cpu = new CpuContext();
        for (int r = 1; r <= 31; r++) cpu[r] = 0x1111_0000u + (uint)r * 0x0001_0101u;
        cpu.SP = StackTop; cpu.FP = StackTop - 0x100; cpu.RA = 0x8001_2340u; cpu.HI = 0x0000_00FEu; cpu.LO = 0x0000_00FDu;
        cpu.A0 = a0; cpu.A1 = a1; cpu.A2 = a2; cpu.A3 = a3;
        return cpu.Snapshot();
    }

    static string? FirstRamDifference(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return $"length {a.Length}/{b.Length}";
        int first = a.AsSpan().CommonPrefixLength(b);
        if (first == a.Length) return null;
        int differing = 0;
        for (int i = first; i < a.Length; i++) if (a[i] != b[i]) differing++;
        return $"{differing} byte(s), first at RAM offset 0x{first:X6} (0x80000000|0x{first:X6}): original {a[first]:X2} native {b[first]:X2}";
    }

    static string? FirstPadDifference(uint[] a, uint[] b)
    {
        int i = a.AsSpan().CommonPrefixLength(b);
        return i == PadWords ? null : $"scratchpad +0x{i * 4:X3}: original {a[i]:X8} native {b[i]:X8}";
    }

    static string? FirstCpuDifference(in CpuSnapshot reference, in CpuSnapshot actual)
    {
        CpuSnapshot a = reference, b = actual;
        foreach (var field in typeof(CpuSnapshot).GetFields())
        {
            uint x = (uint)field.GetValue(a)!, y = (uint)field.GetValue(b)!;
            if (x != y) return $"{field.Name}: original 0x{x:X8} native 0x{y:X8}";
        }
        return null;
    }

    static string? FirstStackDifference(byte[] a, byte[] b, uint sp, uint span)
    {
        int hi = (int)(sp & 0x1FFFFF), lo = Math.Max(0, hi - (int)span);
        int first = a.AsSpan(lo, hi - lo).CommonPrefixLength(b.AsSpan(lo, hi - lo));
        return first == hi - lo ? null
            : $"0x{0x80000000u + (uint)(lo + first):X8}: original {a[lo + first]:X2} native {b[lo + first]:X2}";
    }

    static string? FirstCallDifference()
    {
        int at = 0;
        while (at < Reference.Count && at < Native.Count && Reference[at] == Native[at]) at++;
        if (at == Reference.Count && at == Native.Count) return null;
        return $"{Reference.Count}/{Native.Count} calls, first mismatch at {at}: " +
               $"original {(at < Reference.Count ? Reference[at].ToString() : "end")} " +
               $"native {(at < Native.Count ? Native[at].ToString() : "end")}";
    }

    // --- nested callee order ----------------------------------------------------

    static void InstallCalleeHooks()
    {
        var mod = new ModInfo { Id = "kf3.scene-probe-inner", Name = "Synthetic inner fixtures", Version = "1.0" };
        var markers = new[] { typeof(Site<C0>), typeof(Site<C1>), typeof(Site<C2>), typeof(Site<C3>) };
        var queued = new List<MethodInfo>();
        for (int i = 0; i < Callees.Length; i++)
        {
            var target = typeof(Game).GetMethod(Callees[i]);
            if (target == null) continue;
            markers[i].GetField(nameof(Site<C0>.Id))!.SetValue(null, i);
            queued.Add(target);
            HookManager.AddPre(mod, target, markers[i].GetMethod(nameof(Site<C0>.Pre))!);
        }
        HookManager.Commit();
        _hooked = queued.Count > 0 && queued.All(HookManager.IsCommitted);
        if (!_hooked) Console.Error.WriteLine("[KF3] inner fixtures: nested callee hooks could not be installed");
    }

    static void Note(int id, CpuContext c, IMemory m)
    {
        if (_phase == 0) return;
        (_phase == 1 ? Reference : Native).Add(new(id, c.RA, c.SP, c.A0, c.A1, c.A2, c.A3));
    }

    struct C0; struct C1; struct C2; struct C3;
    static class Site<T>
    {
        public static int Id = -1;
        public static void Pre(CpuContext c, IMemory m) => Note(Id, c, m);
    }

    // --- shared seeding ---------------------------------------------------------

    static void SeedCommon(PSMemory m)
    {
        for (uint i = 0; i < StackSpan; i++) m.WriteU8(StackTop - StackSpan + i, (byte)(0x40 + i * 7));
        for (uint i = 0; i < PacketLimit - PacketBase; i++) m.WriteU8(PacketBase + i, PacketMark);
        m.WriteU32(Pad + 0x08, ColourTable);
        m.WriteU32(Pad + 0x0C, ColourTable + 0x40);
        m.WriteU32(Pad + 0x10, MeshBase);
        m.WriteU32(Pad + 0x14, PacketBase);
        m.WriteU32(Pad + 0x18, PacketLimit);
        m.WriteU32(Pad + 0x44, ProjectedBase);
        m.WriteU32(Pad + 0x64, (uint)KindByte << 24 | 0x0030_3030u);
        m.WriteU32(Pad + 0x54, 0x0080_8080u);   // the map cell's source colour
        m.WriteU32(Pad + 0x78, 0x11u);
        m.WriteU32(Pad + 0x7C, 0x22u);
        m.WriteU32(Pad + 0x80, 0x33u);
        m.WriteU16(Pad + 0x84, 0x0002);
        for (uint i = 0; i < 32; i++)
            m.WriteU32(ColourTable + i * 4, 0x0800_0000u | (0x0000_0100u + i * 0x40u));
        // PSY-Q identity 3x3 packing: +0=r11r12, +4=r13r21, +8=r22r23, +12=r31r32,
        // +16=r33. The view matrix carries the translation that puts the viewed
        // cell (column 3, row 2) 4096 in front of the camera.
        Identity(m, CameraBase + 0x00);
        for (uint t = 0; t < 4; t++) Identity(m, CameraBase + 0x40 + t * 0x20);
        m.WriteU32(CameraBase + 0x100, CellX);
        m.WriteU32(CameraBase + 0x104, CellY);
        m.WriteU32(CameraBase + 0x108, CellZ - 0x1000);
        m.WriteU32(Pad + 0x88, CellX);
        m.WriteU32(Pad + 0x8C, CellY);
        m.WriteU32(Pad + 0x90, CellZ - 0x1000);
        m.WriteU32(MeshTablePointer, MeshBase);
    }

    /// <summary>One identity 3x3 in the packed layout the GTE control loads use.</summary>
    static void Identity(PSMemory m, uint at)
    {
        m.WriteU32(at + 0x00, 0x0000_1000u);
        m.WriteU32(at + 0x04, 0x0000_0000u);
        m.WriteU32(at + 0x08, 0x0000_1000u);
        m.WriteU32(at + 0x0C, 0x0000_0000u);
        m.WriteU32(at + 0x10, 0x0000_1000u);
    }

    // --- func_80038844, the front-table assembler -------------------------------

    static void FrontAssemblerFixtures(List<object> report)
    {
        var original = (CpuContext cpu, PSMemory m) => Game.func_80038844(cpu, m);
        CpuSnapshot Entry(uint flags) => EntryCpu(0u, 0x801F_F000u, flags, 0u);

        // Kind and rejecting cases: [name, commands, backfacing, z, room, flags, expected packet sizes]
        var cases = new (string Name, byte[] Commands, bool Backfacing, uint Z, uint Room, uint Flags, int[] Sizes, uint Cursor)[]
        {
            ("front-kinds", [0x24, 0x2C, 0x34, 0x3C], false, 0x0100u, 0x2000u, 0u, [0x20, 0x28, 0x28, 0x34], 0xA4u),
            ("front-kinds-semi", [0x26, 0x2E, 0x36, 0x3E], false, 0x0100u, 0x2000u, 0u, [0x20, 0x28, 0x28, 0x34], 0xA4u),
            ("front-backface", [0x24, 0x2C, 0x34, 0x3C], true, 0x0100u, 0x2000u, 0u, [], 0u),
            ("front-unknown-commands", [0x30, 0x38, 0x20, 0x25, 0x24], false, 0x0100u, 0x2000u, 0u, [0x20], 0x20u),
            ("front-count-zero", [], false, 0x0100u, 0x2000u, 0u, [], 0u),
            ("front-no-room", [0x24, 0x2C], false, 0x0100u, 0x0010u, 0u, [], 0x20u),
            ("front-blend-table", [0x24, 0x2C, 0x34, 0x3C], false, 0x3000u, 0x2000u, 2u, [0x20, 0x28, 0x28, 0x34], 0xA4u),
            ("front-blend-index-zero", [0x24, 0x2C], false, 0x0100u, 0x2000u, 2u, [0x20, 0x28], 0x48u),
        };
        foreach (var c in cases)
        {
            int records = c.Commands.Length;
            var entry = Entry(c.Flags);
            void Seed(PSMemory m)
            {
                SeedCommon(m);
                SeedFrontMesh(m, 0, c.Commands, c.Backfacing, c.Z);
                m.WriteU32(Pad + 0x18, PacketBase + c.Room);
            }
            Compare($"front/{c.Name}", "func_80038844", Seed, entry, original,
                (cpu, m) => _runFrontAssembler.Invoke(null, [cpu, m]), report);

            // The accepted cases must have emitted real, walkable packets.
            var observation = Observe(Seed, entry, (cpu, m) => _runFrontAssembler.Invoke(null, [cpu, m]), 0, EntryGte(), new PSMemory());
            uint cursor = observation.ReadCursor();
            int expected = c.Sizes.Length;
            Assert(cursor == PacketBase + c.Cursor,
                $"front/{c.Name}: the primitive cursor ended at 0x{cursor:X8}, expected 0x{PacketBase + c.Cursor:X8}");
            Assert(observation.PacketChain(cursor).SequenceEqual(c.Sizes),
                $"front/{c.Name}: emitted packet sizes [{string.Join(",", observation.PacketChain(cursor))}], expected [{string.Join(",", c.Sizes)}]");
            report.Add(new
            {
                caseName = $"front/{c.Name}", commands = c.Commands, packets = observation.PacketChain(cursor).Count,
                packetSizes = observation.PacketChain(cursor), cursor = cursor, packetBytes = cursor - PacketBase,
                expectedSizes = c.Sizes,
            });
            if (expected > 0)
            {
                int at = (int)(PacketBase & 0x1FFFFF);
                Assert(observation.Ram.AsSpan(at, (int)(cursor - PacketBase)).ToArray().Any(b => b != PacketMark),
                    $"front/{c.Name}: the accepted case wrote no packet bytes");
                Assert(observation.Ram[at + 3] == c.Sizes[0] / 4 - 1,
                    $"front/{c.Name}: the first packet's tag byte {observation.Ram[at + 3]} does not encode its {c.Sizes[0]}-byte length");
                Assert(observation.Ram[at + 7] == FirstAccepted(c.Commands),
                    $"front/{c.Name}: the first packet's command byte is {observation.Ram[at + 7]:X2}, not its first accepted face kind {FirstAccepted(c.Commands):X2}");
            }
            else
            {
                Assert(observation.Ram.AsSpan((int)(PacketBase & 0x1FFFFF), 0x400).ToArray().All(b => b == PacketMark),
                    $"front/{c.Name}: a rejected case wrote packet bytes");
            }
        }
    }

    static byte FirstAccepted(byte[] commands) =>
        commands.FirstOrDefault(c => (c & 0xFD) is 0x24 or 0x2C or 0x34 or 0x3C);

    /// <summary>A front-table mesh of the given kind: 28-byte table entry, face
    /// records at a fixed 0x44 stride, and their 8-byte vertex/light records.</summary>
    static void SeedFrontMesh(PSMemory m, uint kind, byte[] commands, bool backfacing, uint z)
    {
        uint header = MeshBase + 12 + kind * 28;
        m.WriteU32(header + 0x00, (VertexBase + kind * KindSpan) - (MeshBase + 12));
        m.WriteU32(header + 0x04, 64);
        m.WriteU32(header + 0x08, (NormalBase + kind * KindSpan) - (MeshBase + 12));
        m.WriteU32(header + 0x0C, 64);
        m.WriteU32(header + 0x10, (FaceBase + kind * KindSpan) - (MeshBase + 12));
        m.WriteU32(header + 0x14, (uint)commands.Length);
        for (int record = 0; record < commands.Length; record++)
        {
            uint at = FaceBase + kind * KindSpan + (uint)record * RecordSpan;
            (int vertexBase, int normalBase, int step) = commands[record] switch
            {
                var k when (k & 0xFD) == 0x2C => (18, 16, 2),
                var k when (k & 0xFD) == 0x34 => (14, 12, 4),
                var k when (k & 0xFD) == 0x3C => (18, 16, 4),
                _ => (14, 12, 2),
            };
            // Body length is the command word's byte 1 in words (the walk reads
            // +0x1D and shifts left twice); the low u16 rides into the packet.
            m.WriteU32(at, (uint)commands[record] << 24 | 16u << 8 | 0x0002u);
            uint body = at + 4;
            m.WriteU16(body + 0x00, (ushort)(0x0400 + record * 0x20));
            m.WriteU16(body + 0x02, (ushort)(0x7C00 + record));
            m.WriteU16(body + 0x04, (ushort)(0x0800 + record * 0x20));
            m.WriteU16(body + 0x06, (ushort)(0x0040 + record));
            for (int k = 0; k < 4; k++)
            {
                m.WriteU16(body + (uint)(vertexBase + k * step), (ushort)((record * 8 + k) * 8));
                // A flat 24/2C record carries one normal, at normalBase; writing
                // four would land on the vertex references above and move them.
                if (step == 4 || k == 0) m.WriteU16(body + (uint)(normalBase + k * step), (ushort)((record * 8 + k) * 8));
            }
            for (int k = 0; k < 4; k++)
            {
                (int x, int y) = k switch
                {
                    0 => (0, 0),
                    1 => backfacing ? (0, 0x100) : (0x100, 0),
                    2 => backfacing ? (0x100, 0) : (0, 0x100),
                    _ => (0x100, 0x100),   // the quad's fourth corner, not a degenerate point
                };
                uint vertex = ProjectedBase + kind * KindSpan + (uint)(record * 8 + k) * 8;
                m.WriteU16(vertex + 0, (ushort)x);
                m.WriteU16(vertex + 2, (ushort)y);
                m.WriteU16(vertex + 4, (ushort)z);
                m.WriteU16(vertex + 6, (ushort)(0x0010 + k * 0x10));
                uint source = VertexBase + kind * KindSpan + (uint)(record * 8 + k) * 8;
                // The source quad is a wall facing the camera at CellZ, so the
                // projection is non-degenerate and the cell lies in front of it.
                m.WriteU16(source + 0, (ushort)(CellX - 1024 + (k & 1) * 2048));
                m.WriteU16(source + 2, unchecked((ushort)(CellY + (k >> 1) * 1024)));
                m.WriteU16(source + 4, (ushort)CellZ);
                m.WriteU16(source + 6, 0);
                uint normal = NormalBase + kind * KindSpan + (uint)(record * 8 + k) * 8;
                m.WriteU16(normal + 0, (ushort)(0x0100 + k * 0x40));
                m.WriteU16(normal + 2, (ushort)(0x0080 + k * 0x20));
                m.WriteU16(normal + 4, (ushort)(0x00C0 + k * 0x10));
            }
        }
    }

    // --- func_8003BE34, the independent map-cell helper -------------------------

    static void CellFixtures(List<object> report)
    {
        var original = (CpuContext cpu, PSMemory m) => Game.func_8003BE34(cpu, m);
        // Column 3, row 2 of the 80x80 cell table; each cell holds two 5-byte halves.
        const uint column = 3, row = 2;
        uint cell = CellBase + column * 10 + row * 800;
        var cases = new (string Name, uint Flags, byte Kind, byte Second, uint Room, bool Near)[]
        {
            ("cell-accepted-upper", 2u, 0x00, 0xFF, 0x4000u, false),
            ("cell-accepted-both-halves", 2u, 0x00, 0x00, 0x4000u, false),
            ("cell-accepted-near", 6u, 0x00, 0xFF, 0x4000u, true),
            ("cell-flags-zero", 0u, 0x00, 0x00, 0x4000u, false),
            ("cell-kind-rejected", 2u, 0xF0, 0xF0, 0x4000u, false),
        };
        foreach (var c in cases)
        {
            var entry = EntryCpu(column, row, c.Flags, 0u);
            void Seed(PSMemory m)
            {
                SeedCommon(m);
                SeedFrontMesh(m, 0, [0x24, 0x2C], false, 0x0100u);
                m.WriteU8(cell + 0, c.Kind);
                m.WriteU8(cell + 1, 3);
                m.WriteU8(cell + 2, 0);
                m.WriteU8(cell + 4, 0);
                m.WriteU8(cell + 5, c.Second);
                m.WriteU8(cell + 6, 4);
                m.WriteU8(cell + 7, 1);
                m.WriteU8(cell + 9, 0);
                for (uint r = 0; r < 2; r++)
                {
                    m.WriteU16(LightBase + r * 0x6C + 0x64, 0x0010);
                    m.WriteU16(LightBase + r * 0x6C + 0x66, 0x0020);
                    m.WriteU16(LightBase + r * 0x6C + 0x68, 0x0100);
                    m.WriteU16(LightBase + r * 0x6C + 0x6A, 0x0800);
                    for (uint t = 0; t < 4; t++)
                        for (uint k = 0; k < 5; k++)
                            m.WriteU32(LightBase + r * 0x6C + t * 20 + k * 4,
                                k == 0 || k == 3 || k == 4 ? 0x0000_1000u : 0x0000_0000u);
                    for (uint k = 0; k < 5; k++)
                        m.WriteU32(LightBase + r * 0x6C + 0x50 + k * 4,
                            k == 0 || k == 3 || k == 4 ? 0x0000_1000u : 0x0000_0000u);
                }
                m.WriteU32(Pad + 0x18, PacketBase + c.Room);
                // The map assembler's own entry: the OTZ limit lives at pad+0x66,
                // so a high RGBC word there would skip every near cell.
                m.WriteU16(Pad + 0x64, 0x0000);
                m.WriteU16(Pad + 0x66, 100);
                m.WriteU32(Pad + 0x1C, 0x0000_0000u);
                m.WriteU8(Pad + 0x1D, 0x10);
                m.WriteU8(Pad + 0x1E, 0x00);
                m.WriteU8(Pad + 0x1F, 0x2C);
                m.WriteU32(Pad + 0x20, FaceBase);
                m.WriteU32(Pad + 0x24, MeshBase + 12);
                m.WriteU32(Pad + 0x28, NormalBase);
                m.WriteU32(Pad + 0x4C, VertexBase);
                m.WriteU32(Pad + 0x90, 0x0000_0100u);
            }
            Compare($"cell/{c.Name}", "func_8003BE34", Seed, entry, original,
                (cpu, m) => _runCell.Invoke(null, [cpu, m]), report);

            var observation = Observe(Seed, entry, (cpu, m) => _runCell.Invoke(null, [cpu, m]), 0, EntryGte(), new PSMemory());
            uint cursor = observation.ReadCursor();
            bool accepted = c.Kind < 240 && (c.Flags & 2u) != 0;
            if (accepted)
            {
                // The cell's own scratchpad offsets: +0x100 x, +0x102 z, +0x104 y.
                Assert(observation.Pad[0x100 / 4] != 0 || observation.Pad[0x102 / 4] != 0 || observation.Pad[0x104 / 4] != 0,
                    $"cell/{c.Name}: the cell wrote no vertex offset into the scratchpad");
                if (c.Near)
                    Assert(observation.PacketChain(cursor).Count == 0 && cursor == PacketBase,
                        $"cell/{c.Name}: the near path is an acknowledged explicit reject and must not emit");
                else
                {
                    Assert(cursor > PacketBase && observation.PacketChain(cursor).Count > 0,
                        $"cell/{c.Name}: the accepted bulk cell emitted no packet (cursor 0x{cursor:X8})");
                    int halves = c.Second < 240 ? 2 : 1;
                    Assert(observation.PacketChain(cursor).Count == halves * 2,
                        $"cell/{c.Name}: emitted {observation.PacketChain(cursor).Count} bulk packet(s) for {halves} half/halves, expected {halves * 2}");
                    Assert(observation.PacketChain(cursor).Sum() == cursor - PacketBase,
                        $"cell/{c.Name}: the bulk packet chain [{string.Join(",", observation.PacketChain(cursor))}] does not reach the cursor 0x{cursor:X8}");
                    Assert(observation.Ram.AsSpan((int)(PacketBase & 0x1FFFFF), (int)(cursor - PacketBase)).ToArray().Any(b => b != PacketMark),
                        $"cell/{c.Name}: the accepted bulk cell wrote no packet bytes");
                    for (uint at = PacketBase; at < cursor;)
                    {
                        int words = observation.Ram[(int)(at & 0x1FFFFF) + 3] + 1;
                        Assert(words > 1 && at + (uint)words * 4 <= cursor,
                            $"cell/{c.Name}: the bulk packet at 0x{at:X8} has tag byte {words - 1}, which runs past the cursor 0x{cursor:X8}");
                        at += (uint)words * 4;
                    }
                }
                report.Add(new
                {
                    caseName = $"cell/{c.Name}", flags = c.Flags, kind = c.Kind,
                    cursor = cursor, packetBytes = cursor - PacketBase,
                    packets = observation.PacketChain(cursor).Count, calls = Reference.Count,
                    projected = new[] { Gte.Read(12), Gte.Read(13), Gte.Read(14) },
                    depths = new[] { Gte.Read(16), Gte.Read(17), Gte.Read(18) },
                    nclipSlot = observation.ReadPad(0x60), emittedSlot = observation.ReadPad(0x7C),
                });
            }
            else
            {
                Assert(cursor == PacketBase, $"cell/{c.Name}: the rejected cell moved the packet cursor to 0x{cursor:X8}");
                Assert(Reference.Count == 0, $"cell/{c.Name}: the rejected cell called {Reference.Count} nested routine(s)");
            }
            if (_hooked) Assert(Reference.Count > 0 || !accepted,
                $"cell/{c.Name}: the accepted cell called no nested routine");
        }
    }

    static uint ReadCursor(this Observation o) => o.ReadPad(0x14);
    static uint ReadPad(this Observation o, int offset) => o.Pad[offset / 4];

    /// <summary>The emitted packet lengths, walking each packet's own tag byte.</summary>
    static List<int> PacketChain(this Observation o, uint cursor)
    {
        var sizes = new List<int>();
        uint at = PacketBase;
        while (at + 4 <= cursor && sizes.Count < 64)
        {
            int words = o.Ram[(int)(at & 0x1FFFFF) + 3] + 1;
            if (words <= 0 || words > 64) break;
            sizes.Add(words * 4);
            at += (uint)(words * 4);
        }
        return sizes;
    }
}
