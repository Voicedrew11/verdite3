namespace Kf3;

/// <summary>
/// Reads what a GAME.EXE's menu code would build, without running the game: a small
/// interpreter over the file's MIPS, tracking only constants. The Japanese menus build
/// their label records and their card messages out of immediates in the code, so this is
/// how those words, their positions and their window sizes are read from the player's own
/// disc at run time instead of being copied into this repository.
///
/// A value is known or unknown. Unknown is not an error: a register loaded from game
/// memory is unknown, a branch on it falls through, and whatever depends on it stays
/// unknown for the caller to skip. <c>j</c> is followed; a <c>jal</c> is stepped over and
/// clobbers the caller-saved registers, as a real call may.
///
/// Pure: no game state, no I/O.
/// </summary>
internal sealed class JapaneseProgram
{
    /// <summary>File offset 0 of a GAME.EXE is this address: the start of the PS-X header.</summary>
    const uint ImageBase = 0x80010800;

    /// <summary>The address the stack pointer starts at, outside the image.</summary>
    public const uint StackAddress = 0x7F000000;

    /// <summary>An address given to a0 when a routine's writes are to be collected.</summary>
    public const uint DestinationAddress = 0x7E000000;

    const int MaxSteps = 100_000;
    const int Sp = 29, Zero = 0;

    /// <summary>v0, v1, a0..a3 and t0..t9: what a call may change.</summary>
    static readonly int[] CallerSaved = [2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 24, 25];

    readonly byte[] _image;

    public JapaneseProgram(byte[] exe)
    {
        _image = exe;
    }

    /// <summary>
    /// Runs from <paramref name="entry"/> until <paramref name="stop"/> says so of a call
    /// (given the call's address and target, with its delay slot done), or until the
    /// routine returns. Walking straight down (<paramref name="straight"/>) ignores every
    /// jump and branch: the last write to a record on any path wins, which is how a page
    /// whose records are all built in line is read. Otherwise jumps and known branches
    /// are followed and an unknown branch falls through.
    /// <paramref name="seeds"/> are register numbers with known values at the entry.
    /// </summary>
    public Trace Run(uint entry, Func<uint, uint, bool>? stop, bool straight, params (int Register, uint Value)[] seeds)
    {
        var trace = new Trace(_image);
        trace.Set(Sp, StackAddress);
        foreach (var (register, value) in seeds)
            trace.Set(register, value);

        uint pc = entry;
        for (int step = 0; step < MaxSteps; step++)
        {
            if (!TryFetch(pc, out uint word)) return trace;
            var flow = Flow.Decode(word, pc, trace);
            if (flow.Kind == FlowKind.None)
            {
                trace.Execute(word);
                pc += 4;
                continue;
            }

            if (TryFetch(pc + 4, out uint delay)) trace.Execute(delay);
            if (flow.Kind == FlowKind.Call)
            {
                if (stop?.Invoke(pc, flow.Target) == true)
                {
                    trace.Reached = true;
                    return trace;
                }
                foreach (int register in CallerSaved) trace.Clear(register);
                pc += 8;
                continue;
            }
            if (straight)
            {
                pc += 8;
                continue;
            }
            if (flow.Kind == FlowKind.Return)
            {
                trace.Reached = stop is null;
                return trace;
            }
            pc = flow.Taken ? flow.Target : pc + 8;
        }
        return trace;
    }

    bool TryFetch(uint address, out uint word)
    {
        long offset = (long)address - ImageBase;
        if (offset < 0 || offset + 4 > _image.Length)
        {
            word = 0;
            return false;
        }
        word = BitConverter.ToUInt32(_image, (int)offset);
        return true;
    }

    enum FlowKind { None, Jump, Branch, Call, Return }

    /// <summary>What one instruction does to the program counter.</summary>
    readonly record struct Flow(FlowKind Kind, uint Target, bool Taken)
    {
        public static Flow Decode(uint word, uint pc, Trace trace)
        {
            uint op = word >> 26;
            int rs = (int)(word >> 21) & 31, rt = (int)(word >> 16) & 31;
            uint branchTarget = pc + 4 + (uint)((short)word << 2);
            uint jumpTarget = (pc & 0xF0000000) | ((word & 0x03FFFFFF) << 2);
            switch (op)
            {
                case 0:
                    return (word & 63) switch
                    {
                        8 => new Flow(FlowKind.Return, 0, false),      // jr: a return, or a table the scan does not follow
                        9 => new Flow(FlowKind.Call, 0, false),        // jalr
                        _ => default,
                    };
                case 2: return new Flow(FlowKind.Jump, jumpTarget, true);
                case 3: return new Flow(FlowKind.Call, jumpTarget, false);
                case 1:
                    bool wantNegative = rt == 0;
                    return Conditional(trace.Get(rs) is { } v ? (int)v < 0 == wantNegative : null, branchTarget);
                case 4: return Conditional(Equal(trace, rs, rt), branchTarget);
                case 5: return Conditional(!Equal(trace, rs, rt), branchTarget);
                case 6: return Conditional(trace.Get(rs) is { } a ? (int)a <= 0 : null, branchTarget);
                case 7: return Conditional(trace.Get(rs) is { } b ? (int)b > 0 : null, branchTarget);
                default: return default;
            }
        }

        static bool? Equal(Trace trace, int rs, int rt)
        {
            return trace.Get(rs) is { } a && trace.Get(rt) is { } b ? a == b : null;
        }

        /// <summary>A branch on an unknown value falls through.</summary>
        static Flow Conditional(bool? taken, uint target) => new(FlowKind.Branch, target, taken == true);
    }

    /// <summary>The registers and the writes at the end of a run.</summary>
    internal sealed class Trace
    {
        readonly byte[] _image;
        readonly uint?[] _registers = new uint?[32];
        readonly Dictionary<uint, byte> _written = new();

        public Trace(byte[] image)
        {
            _image = image;
        }

        /// <summary>True when the run got to the call it was asked to stop at (or returned, when asked for no call).</summary>
        public bool Reached { get; internal set; }

        public uint? Get(int register) => register == Zero ? 0 : _registers[register];

        public void Set(int register, uint? value)
        {
            if (register != Zero) _registers[register] = value;
        }

        public void Clear(int register) => _registers[register] = null;

        /// <summary>Every byte the run wrote, by address.</summary>
        public IReadOnlyDictionary<uint, byte> Written => _written;

        public byte? ByteAt(uint address)
        {
            if (_written.TryGetValue(address, out byte value)) return value;
            long offset = (long)address - ImageBase;
            return offset >= 0 && offset < _image.Length ? _image[offset] : null;
        }

        public ushort? HalfAt(uint address)
        {
            return ByteAt(address) is { } low && ByteAt(address + 1) is { } high ? (ushort)(low | high << 8) : null;
        }

        uint? WordAt(uint address)
        {
            return HalfAt(address) is { } low && HalfAt(address + 2) is { } high ? (uint)(low | high << 16) : null;
        }

        void Store(uint address, uint value, int bytes)
        {
            for (int i = 0; i < bytes; i++)
                _written[address + (uint)i] = (byte)(value >> (i * 8));
        }

        /// <summary>Executes an instruction that changes no flow. Anything not modelled leaves its result unknown.</summary>
        public void Execute(uint word)
        {
            uint op = word >> 26;
            int rs = (int)(word >> 21) & 31, rt = (int)(word >> 16) & 31, rd = (int)(word >> 11) & 31;
            int shift = (int)(word >> 6) & 31;
            uint zeroExtended = word & 0xFFFF;
            uint signExtended = (uint)(short)word;
            switch (op)
            {
                case 0: ExecuteRegister(word & 63, rs, rt, rd, shift); return;
                case 9: SetFrom(rt, rs, a => a + signExtended); return;                  // addiu
                case 10: SetFrom(rt, rs, a => (int)a < (int)signExtended ? 1u : 0u); return;  // slti
                case 11: SetFrom(rt, rs, a => a < signExtended ? 1u : 0u); return;       // sltiu
                case 12: SetFrom(rt, rs, a => a & zeroExtended); return;                 // andi
                case 13: SetFrom(rt, rs, a => a | zeroExtended); return;                 // ori
                case 14: SetFrom(rt, rs, a => a ^ zeroExtended); return;                 // xori
                case 15: Set(rt, zeroExtended << 16); return;                            // lui
            }
            if (op >= 32 && op <= 46) ExecuteMemory(op, rs, rt, signExtended);
            else if (op == 16 || op == 18) Clear(rt);                                    // coprocessor moves
        }

        void SetFrom(int destination, int source, Func<uint, uint> compute)
        {
            Set(destination, Get(source) is { } value ? compute(value) : null);
        }

        void SetFrom(int destination, int left, int right, Func<uint, uint, uint> compute)
        {
            Set(destination, Get(left) is { } a && Get(right) is { } b ? compute(a, b) : null);
        }

        void ExecuteRegister(uint function, int rs, int rt, int rd, int shift)
        {
            switch (function)
            {
                case 0: SetFrom(rd, rt, a => a << shift); return;                        // sll
                case 2: SetFrom(rd, rt, a => a >> shift); return;                        // srl
                case 3: SetFrom(rd, rt, a => (uint)((int)a >> shift)); return;           // sra
                case 33: SetFrom(rd, rs, rt, (a, b) => a + b); return;                   // addu
                case 35: SetFrom(rd, rs, rt, (a, b) => a - b); return;                   // subu
                case 36: SetFrom(rd, rs, rt, (a, b) => a & b); return;                   // and
                case 37: SetFrom(rd, rs, rt, (a, b) => a | b); return;                   // or
                case 42: SetFrom(rd, rs, rt, (a, b) => (int)a < (int)b ? 1u : 0u); return;   // slt
                case 43: SetFrom(rd, rs, rt, (a, b) => a < b ? 1u : 0u); return;         // sltu
                case 16 or 18: Clear(rd); return;                                        // mfhi, mflo
                case 24 or 25 or 26 or 27 or 17 or 19: return;                           // mult, div, mthi, mtlo
                default: if (rd != Zero) Clear(rd); return;
            }
        }

        void ExecuteMemory(uint op, int rs, int rt, uint offset)
        {
            if (Get(rs) is not { } baseAddress)
            {
                if (op < 40) Clear(rt);
                return;
            }
            uint address = baseAddress + offset;
            switch (op)
            {
                case 32: Set(rt, ByteAt(address) is { } b ? (uint)(sbyte)b : null); break;    // lb
                case 36: Set(rt, ByteAt(address)); break;                                     // lbu
                case 33: Set(rt, HalfAt(address) is { } h ? (uint)(short)h : null); break;    // lh
                case 37: Set(rt, HalfAt(address)); break;                                     // lhu
                case 35: Set(rt, WordAt(address)); break;                                     // lw
                case 34: LoadLeft(rt, address); break;                                        // lwl
                case 38: LoadRight(rt, address); break;                                       // lwr
                case 40: StoreKnown(rt, address, 1); break;                                   // sb
                case 41: StoreKnown(rt, address, 2); break;                                   // sh
                case 43: StoreKnown(rt, address, 4); break;                                   // sw
                case 42: StoreLeft(rt, address); break;                                       // swl
                case 46: StoreRight(rt, address); break;                                      // swr
            }
        }

        void StoreKnown(int register, uint address, int bytes)
        {
            if (Get(register) is { } value) Store(address, value, bytes);
            else Forget(address, bytes);
        }

        void Forget(uint address, int bytes)
        {
            for (int i = 0; i < bytes; i++) _written.Remove(address + (uint)i);
        }

        /// <summary>lwl: the bytes from <paramref name="address"/> down to its word start, into the register's high end.</summary>
        void LoadLeft(int register, uint address)
        {
            int count = (int)(address & 3) + 1;
            uint current = Get(register) ?? 0;
            for (int i = 0; i < count; i++)
            {
                if (ByteAt(address - (uint)i) is not { } value)
                {
                    Clear(register);
                    return;
                }
                int shift = 24 - i * 8;
                current = current & ~(0xFFu << shift) | (uint)value << shift;
            }
            Set(register, current);
        }

        /// <summary>lwr: the bytes from <paramref name="address"/> up to its word end, into the register's low end.</summary>
        void LoadRight(int register, uint address)
        {
            int count = 4 - (int)(address & 3);
            uint current = Get(register) ?? 0;
            for (int i = 0; i < count; i++)
            {
                if (ByteAt(address + (uint)i) is not { } value)
                {
                    Clear(register);
                    return;
                }
                int shift = i * 8;
                current = current & ~(0xFFu << shift) | (uint)value << shift;
            }
            Set(register, current);
        }

        void StoreLeft(int register, uint address)
        {
            int count = (int)(address & 3) + 1;
            if (Get(register) is not { } value)
            {
                Forget(address - (uint)(count - 1), count);
                return;
            }
            for (int i = 0; i < count; i++)
                _written[address - (uint)i] = (byte)(value >> (24 - i * 8));
        }

        void StoreRight(int register, uint address)
        {
            int count = 4 - (int)(address & 3);
            if (Get(register) is not { } value)
            {
                Forget(address, count);
                return;
            }
            for (int i = 0; i < count; i++)
                _written[address + (uint)i] = (byte)(value >> (i * 8));
        }
    }
}
