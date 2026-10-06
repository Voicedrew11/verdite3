using System.Reflection;
using RecompOne.Runtime.Context;
using RecompOne.Runtime.Memory;
using RecompOne.Runtime.Modding;

namespace Kf3;

/// <summary>
/// Boot straight into the ending: <c>KF3_BOOTEXE=end</c> (Verdite2's <c>BootExe</c>).
///
///     KF3_BOOTEXE=end     END.EXE as GAME.EXE's ending hands over to it: all three movies
///     KF3_BOOTEXE=end3    END.EXE as GAME.EXE's other hand-over (exit word 4): the last movie only
///
/// The ending is otherwise reachable only by finishing the game, which leaves
/// anything wrong with <c>END.EXE</c> (see <see cref="EndingHold"/>) unreproducible
/// in a diagnostic session.
///
/// The way in is the boot stub's own. <c>SLUS_002.55</c>'s loader loop
/// <c>func_80010038</c> (its <c>gp</c> is <c>0x80010260</c>) loads the file named by
/// the index at <c>[gp]</c> from the table at <c>0x8001024C</c> (<c>0</c> =
/// <c>OPEN.EXE</c>, <c>1</c> = <c>GAME.EXE</c>, <c>2</c> = <c>END.EXE</c>), <c>Exec</c>s it
/// as a call, and on return takes the next index from the byte at
/// <c>0x800102F0</c>. GAME.EXE's main loop hands over by writing that byte and the
/// one at <c>0x800102F8</c> and returning. So the first <c>OPEN.EXE</c> is skipped
/// at its entry with the same two bytes written, once: the title reached later
/// through <see cref="EndingHold"/> must run.
///
/// <c>END.EXE</c> booted this way gets none of GAME.EXE's state, which is honest for
/// a movie player that initialises everything it uses; it is a diagnostic, not a
/// way to play. See "The ending" in docs/GAME_INTERNALS.md.
/// </summary>
public static class BootExe
{
    const uint OpenEntry = 0x800136C8;
    const uint StubNext = 0x800102F0;     // u8, the next executable's index
    const uint StubEnding = 0x800102F8;   // u8, END.EXE plays its first two movies when 2
    const int EndIndex = 2;

    static readonly ModInfo _self = new()
    {
        Id = "kf3.bootexe",
        Name = "Boot executable",
        Version = "1.0",
        Description = "Boots straight into END.EXE.",
    };

    static int _ending = -1;
    static bool _spent;

    public static void Configure(string? which)
    {
        if (string.IsNullOrWhiteSpace(which)) return;
        _ending = which.Trim().ToLowerInvariant() switch
        {
            "end" => 2,
            "end3" => 3,
            _ => -1,
        };
        if (_ending < 0)
            Console.Error.WriteLine($"[KF3] boot exe: '{which}' is not end or end3 -- ignored");
    }

    public static void Install()
    {
        if (_ending < 0) return;
        HookAttach.OnOverlayLoad("boot exe", Attach);
    }

    static bool Attach()
    {
        SymbolRegistry.Build();
        var target = SymbolRegistry.Resolve("open", null, OpenEntry);
        if (target == null) return false;
        var impl = typeof(BootExe).GetMethod(nameof(BeforeOpen), BindingFlags.Public | BindingFlags.Static)!;
        HookManager.AddPre(_self, target, impl);
        HookManager.Commit();
        bool ok = HookAttach.Installed(target);
        if (!ok) Console.Error.WriteLine("[KF3] boot exe: OPEN.EXE's entry hook did not install");
        return ok;
    }

    /// <summary>OPEN.EXE's entry, the first time only: return to the stub at once
    /// with END.EXE asked for, as GAME.EXE's hand-over asks for it.</summary>
    public static bool BeforeOpen(CpuContext c, IMemory m)
    {
        if (_spent) return true;
        _spent = true;
        m.WriteU8(StubNext, EndIndex);
        m.WriteU8(StubEnding, (byte)_ending);
        Console.WriteLine($"[KF3] boot exe: OPEN.EXE skipped, END.EXE next (0x800102F8 = {_ending})");
        return false;
    }
}
