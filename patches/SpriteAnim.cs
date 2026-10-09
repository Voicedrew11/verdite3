using System.Diagnostics;
using System.Reflection;
using RecompOne.Runtime.Context;
using RecompOne.Runtime.Memory;
using RecompOne.Runtime.Modding;

namespace Kf3;

/// <summary>
/// Step the billboard sprites' cels once a world tick instead of once a drawn
/// frame. Stage 15's model walk func_80040AE4 bumps the clock word at 0x80182964
/// and, for every live record of 0x18 bytes from 0x80182968, steps the cel byte
/// at +0x5 when the clock divides the record's interval (+0x4). With frame pacing
/// on the walk runs on every drawn frame, so the sprites animate at the render
/// rate. A pre hook on the walk snapshots the clock and cels when the frame is
/// not the tick's first walk and a post hook puts them back; the walk draws the
/// cel it read before stepping, so a held frame draws the tick's cel unchanged.
/// See "What still runs at the render rate" in docs/GAME_INTERNALS.md.
///
///     KF3_SPRITEANIM=0        leave the animation on the render rate -- comparison only
///
/// With KF3_FPS_PROBE=1 the walk counts are printed once a second.
/// </summary>
public static class SpriteAnim
{
    /// <summary>Stage 15's model, object, effect and billboard walk (0x800428A8's call).</summary>
    const uint Walk = 0x80040AE4;

    /// <summary>The `u32` bumped once per walk and divided by each record's interval.</summary>
    const uint Clock = 0x80182964;

    /// <summary>The billboard table: 128 records of 0x18; a `u16[+0]` of 0xFFFF is free.</summary>
    const uint Table = 0x80182968;
    const int Stride = 0x18, Count = 0x80, IdOff = 0x0, CelOff = 0x5;

    public static bool Enabled { get; set; } = true;

    internal static ModInfo Mod => _self;
    static readonly ModInfo _self = new()
    {
        Id = "kf3.spriteanim",
        Name = "Sprite animation pacing",
        Version = "1.0",
        Description = "Animates billboard sprites at the world's rate, not the frame rate.",
    };

    static bool _probe;
    static bool _paired;

    /// <summary>Decided by the pre so the post knows whether it saved anything.</summary>
    static bool _held;

    /// <summary>The tick of the last stepping walk; later walks in it are held.</summary>
    static long _seen = -1;

    static uint _savedClock;
    static readonly ushort[] _savedId = new ushort[Count];
    static readonly byte[] _savedCel = new byte[Count];

    // The probe's window, a second of walks and how many of them stepped.
    static readonly Stopwatch _clock = Stopwatch.StartNew();
    static double _windowMs;
    static long _walks, _stepped;

    public static void Configure(string? mode)
    {
        _probe = Environment.GetEnvironmentVariable("KF3_FPS_PROBE") == "1";
        if (string.IsNullOrWhiteSpace(mode)) return;
        Enabled = mode.Trim().ToLowerInvariant() is not ("0" or "off");
    }

    /// <summary>Attach on the first overlay load, as FramePacing.Install does; the
    /// world's rate is the whole point, so pacing off means nothing to hold.</summary>
    public static void Install()
    {
        // Attached whether or not pacing is on, so the Testing tab can switch both live.
        HookAttach.OnOverlayLoad("sprite anim", Attach);
    }

    static bool Attach()
    {
        SymbolRegistry.Build();
        var target = SymbolRegistry.Resolve("game", null, Walk);
        if (target == null)
        {
            Console.Error.WriteLine($"[KF3] sprite anim: not installed (no game function at 0x{Walk:X8})");
            return false;
        }

        var self = typeof(SpriteAnim);
        int n = 0;
        if (HookManager.AddPre(_self, target,
                self.GetMethod(nameof(Before), BindingFlags.Public | BindingFlags.Static)!)) n++;
        if (HookManager.AddPost(_self, target,
                self.GetMethod(nameof(After), BindingFlags.Public | BindingFlags.Static)!)) n++;

        HookManager.Commit();

        // Half a pair is worse than neither: a post with no pre would write
        // whatever the saved arrays happen to hold into the game's own table.
        _paired = n == 2 && HookAttach.Installed(target);
        if (!_paired)
        {
            Console.Error.WriteLine("[KF3] sprite anim: not installed (the pair did not attach)");
            return false;
        }

        Console.WriteLine("[KF3] sprite anim: held to the tick");
        return true;
    }

    /// <summary>On a held walk, save the clock and every record's cel for the
    /// post; on the tick's first walk the game steps them itself.</summary>
    public static void Before(CpuContext c, IMemory m)
    {
        // The C# walk holds the cels itself. A frozen pass (MenuWorld) holds them
        // whatever the switches say: it draws the world as it stood.
        if (ModelWalk.InCSharp || ((!Enabled || !FramePacing.Enabled) && !FramePacing.Frozen))
        { _held = false; return; }
        _walks++;
        if (FramePacing.FirstWalkOfTick(ref _seen))
        {
            _held = false;
            _stepped++;
            return;
        }

        _held = true;
        _savedClock = m.ReadU32(Clock);
        for (int i = 0; i < Count; i++)
        {
            uint rec = (uint)(Table + i * Stride);
            _savedId[i] = m.ReadU16(rec + IdOff);
            _savedCel[i] = m.ReadU8(rec + CelOff);
        }
    }

    /// <summary>Put the clock and the cels back on a held walk. The walk drew the
    /// cel it read before stepping, so the picture is the tick's cel unchanged.</summary>
    public static void After(CpuContext c, IMemory m)
    {
        if (_held)
        {
            m.WriteU32(Clock, _savedClock);
            for (int i = 0; i < Count; i++)
            {
                if (_savedId[i] == 0xFFFF) continue;
                uint rec = (uint)(Table + i * Stride);

                // An area load rewrites the whole table; a stale cel must not go
                // into a slot that now holds a different sprite.
                if (m.ReadU16(rec + IdOff) != _savedId[i]) continue;
                m.WriteU8(rec + CelOff, _savedCel[i]);
            }
        }

        if (_probe && !ModelWalk.InCSharp) PrintProbe();
    }

    static void PrintProbe()
    {
        double now = _clock.Elapsed.TotalMilliseconds;
        if (_windowMs <= 0.0) { _windowMs = now; return; }
        double elapsed = now - _windowMs;
        if (elapsed < 1000.0) return;
        Console.WriteLine($"[KF3] sprite anim: {_stepped} walk(s) stepped, {_walks - _stepped} held");
        _windowMs = now;
        _walks = _stepped = 0;
    }
}