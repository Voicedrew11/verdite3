using System.Reflection;
using RecompOne.Runtime.Context;
using RecompOne.Runtime.Memory;
using RecompOne.Runtime.Modding;

namespace Kf3;

/// <summary>
/// The scrolling textures held to the tick. Stage 15's call #2, func_800351FC, steps
/// two records of 0x18 bytes at 0x801AEB1C (a countdown, a scroll phase) and copies
/// each one's source image into VRAM at that phase with two MoveImages, on every
/// call; under pacing that is every drawn frame, so the textures scrolled at the
/// drawn rate. The routine now runs on the first stage 15 of a tick only, and VRAM
/// keeps its picture between.
///
///     KF3_TEXSCROLL=0       the routine on every frame -- comparison only
///     KF3_TEXSCROLL=carry   also redraw each frame at the phase interpolated
///                           between the last two ticks (not judged)
///
/// See "3e" in docs/SMOOTHING.md.
/// </summary>
public static class TextureScroll
{
    const uint Routine = 0x800351FC;
    const uint MoveImage = 0x80079E90;
    const uint Table = 0x801AEB1C;
    const int Stride = 0x18, Count = 2;

    enum Mode { Off, Hold, Carry }
    static Mode _mode = Mode.Hold;
    static bool _queued;
    static long _seen = -1;
    static Action<CpuContext, IMemory>? _moveImage;

    // Per record: the phase at the last two ticks, the tick of the last sample,
    // and the phase last put in VRAM.
    static readonly short[] _prev = new short[Count], _cur = new short[Count], _shown = new short[Count];
    static readonly short[] _height = new short[Count];
    static readonly long[] _tick = [-1, -1];

    static bool _probe;
    static double _probeAt = -1.0;
    static long _calls, _ran, _carried;

    static readonly ModInfo _self = new()
    {
        Id = "kf3.texturescroll",
        Name = "Texture scroll pacing",
        Version = "1.0",
        Description = "Scrolls the animated textures at the world's rate, not the frame rate.",
    };

    public static void Configure(string? mode)
    {
        _mode = mode?.Trim().ToLowerInvariant() switch
        {
            "0" or "off" => Mode.Off,
            "carry" => Mode.Carry,
            _ => Mode.Hold,
        };
        _probe = Environment.GetEnvironmentVariable("KF3_FPS_PROBE") == "1";
    }

    public static void Install()
    {
        if (_mode == Mode.Off || !FramePacing.Enabled) return;
        HookAttach.OnOverlayLoad("texture scroll", Attach);
    }

    static bool Attach()
    {
        SymbolRegistry.Build();
        var target = SymbolRegistry.Resolve("game", null, Routine);
        var move = SymbolRegistry.Resolve("game", null, MoveImage);
        if (target == null || move == null) return false;
        _moveImage = move.CreateDelegate<Action<CpuContext, IMemory>>();
        if (!_queued)
        {
            var impl = typeof(TextureScroll).GetMethod(nameof(Replace), BindingFlags.NonPublic | BindingFlags.Static)!;
            _queued = HookManager.AddReplace(_self, target, impl);
            if (!_queued) return false;
        }
        HookManager.Commit();
        bool ok = HookAttach.Installed(target);
        Console.WriteLine(ok ? $"[KF3] texture scroll: {_mode.ToString().ToLowerInvariant()}"
                             : "[KF3] texture scroll: not installed");
        return ok;
    }

    static void Replace(Action<CpuContext, IMemory> orig, CpuContext c, IMemory m)
    {
        _calls++;
        if (FramePacing.FirstWalkOfTick(ref _seen))
        {
            orig(c, m);
            _ran++;
            if (_mode == Mode.Carry) Sample(m);
        }
        if (_mode == Mode.Carry) Draw(c, m);
        if (_probe) Probe();
    }

    static void Sample(IMemory m)
    {
        long tick = FramePacing.Ticks;
        for (int i = 0; i < Count; i++)
        {
            uint rec = Table + (uint)(i * Stride);
            short phase = (short)m.ReadU16(rec + 4), h = (short)m.ReadU16(rec + 0x16);
            bool fresh = m.ReadU8(rec) != 1 || h <= 0 || h != _height[i] || _tick[i] != tick - 1;
            _prev[i] = fresh ? phase : _cur[i];
            _cur[i] = _shown[i] = phase;
            _height[i] = h;
            _tick[i] = tick;
        }
    }

    /// <summary>Put each moving record's image in VRAM at the interpolated phase,
    /// with the routine's own two copies.</summary>
    static void Draw(CpuContext c, IMemory m)
    {
        double f = FramePacing.TickFraction;
        for (int i = 0; i < Count; i++)
        {
            uint rec = Table + (uint)(i * Stride);
            int h = _height[i];
            if (_tick[i] < 0 || m.ReadU8(rec) != 1 || h <= 0 || _prev[i] == _cur[i]) continue;
            int step = ((_cur[i] - _prev[i]) % h + h) % h;
            short phase = (short)((_prev[i] + (int)Math.Round(step * f)) % h);
            if (phase == _shown[i]) continue;
            Upload(c, m, rec, phase);
            _shown[i] = phase;
            _carried++;
        }
    }

    static void Upload(CpuContext c, IMemory m, uint rec, short phase)
    {
        uint sp = c.SP - 0x20u, ra = c.RA, rect = sp + 0x10u;
        c.SP = sp;
        short srcX = (short)m.ReadU16(rec + 0x10), srcY = (short)m.ReadU16(rec + 0x12);
        short w = (short)m.ReadU16(rec + 0x14), h = (short)m.ReadU16(rec + 0x16);
        short dstX = (short)m.ReadU16(rec + 8), dstY = (short)m.ReadU16(rec + 0xA);

        Move(c, m, rect, srcX, srcY, w, (short)(h - phase), dstX, (short)(dstY + phase));
        if (phase != 0)
            Move(c, m, rect, srcX, (short)(srcY + h - phase), w, phase, dstX, dstY);

        c.SP = sp + 0x20u;
        c.RA = ra;
    }

    static void Move(CpuContext c, IMemory m, uint rect, short x, short y, short w, short h, short dx, short dy)
    {
        m.WriteU16(rect, (ushort)x);
        m.WriteU16(rect + 2, (ushort)y);
        m.WriteU16(rect + 4, (ushort)w);
        m.WriteU16(rect + 6, (ushort)h);
        c.A0 = rect;
        c.A1 = (uint)dx;
        c.A2 = (uint)dy;
        c.RA = 0x800352D4u;
        _moveImage!(c, m);
    }

    static void Probe()
    {
        double now = Environment.TickCount64 / 1000.0;
        if (_probeAt < 0.0) _probeAt = now;
        double dt = now - _probeAt;
        if (dt < 1.0) return;
        Console.WriteLine($"[KF3] texture scroll: {_calls / dt:0.0} call(s)/s, {_ran / dt:0.0} run, {_carried / dt:0.0} carried upload(s)/s");
        _probeAt = now;
        _calls = _ran = _carried = 0;
    }
}
