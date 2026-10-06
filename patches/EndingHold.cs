using System.Reflection;
using RecompOne.Runtime.Context;
using RecompOne.Runtime.Dispatch;
using RecompOne.Runtime.Hardware;
using RecompOne.Runtime.Memory;
using RecompOne.Runtime.Modding;
using RecompOne.Runtime.Sdk;

namespace Kf3;

/// <summary>
/// Keep presenting after the ending's last movie, and leave it for the title on a
/// button (Verdite2's <c>EndingHold</c>, on this disc's addresses).
///
///     KF3_ENDINGHOLD=0    the recompiled tail: the spin, and a window that answers nothing
///     KF3_ENDINGEXIT=0    hold the last frame for good, as the console did
///
/// <c>END.EXE</c>'s main <c>func_800119B8</c> plays its movies through
/// <c>func_80011D14(n)</c> (0 and 1, then 60 vblanks, only when the byte at
/// <c>0x800102F8</c> is 2; then 2), clears the boot stub's bytes at
/// <c>0x800102F0</c>/<c>0x800102F8</c>, stops the pad and the CD, and ends in
/// <c>while(1);</c> at <c>0x80011AB8</c> with no <c>VSync</c>. On a console the GPU
/// keeps scanning out the last frame. Here a frame reaches the window only from a
/// <c>VSync</c>, so the same spin leaves a window that is not redrawn, does not
/// close, and spends a core.
///
/// After movie 2 returns this hook does the tail's two writes and never returns:
/// it <c>VSync(0)</c>s instead of spinning. A button seen going down (not one still
/// held from skipping the movie) returns to the title the stub's own way: index 0
/// at <c>[gp]</c> = <c>0x80010260</c>, and the loader loop <c>func_80010038</c>
/// entered with the stub's <c>gp</c>, which is what a returning executable gets.
/// The stack it re-enters on is the ending's, a few words down. See "The ending"
/// in docs/GAME_INTERNALS.md.
/// </summary>
public static class EndingHold
{
    const uint MoviePlayer = 0x80011D14;
    const uint LastMovie = 2;
    const uint StubMain = 0x80010038;
    const uint StubGp = 0x80010260;       // the stub's gp; [gp] is the index it loads
    const uint StubNext = 0x800102F0;
    const uint StubEnding = 0x800102F8;
    const int TitleIndex = 0;             // OPEN.EXE

    static readonly ModInfo _self = new()
    {
        Id = "kf3.endinghold",
        Name = "Ending hold",
        Version = "1.0",
        Description = "Keeps presenting after END.EXE's last frame.",
    };

    public static bool Enabled { get; private set; } = true;
    public static bool ExitToTitle { get; private set; } = true;

    // The movie the current call to the player is playing, from its pre.
    static uint _movie = uint.MaxValue;

    public static void Configure(string? hold, string? exit)
    {
        if (!string.IsNullOrWhiteSpace(hold)) Enabled = !IsOff(hold);
        if (!string.IsNullOrWhiteSpace(exit)) ExitToTitle = !IsOff(exit);
    }

    static bool IsOff(string v) => v.Trim().ToLowerInvariant() is "0" or "off" or "false" or "no";

    /// <summary>Attached in every state, so the movies are logged with the hold off
    /// too: that is the comparison run.</summary>
    public static void Install()
    {
        HookAttach.OnOverlayLoad("ending hold", Attach);
    }

    static bool Attach()
    {
        SymbolRegistry.Build();
        var target = SymbolRegistry.Resolve("end", null, MoviePlayer);
        if (target == null) return false;
        var self = typeof(EndingHold);
        HookManager.AddPre(_self, target, self.GetMethod(nameof(BeforeMovie), BindingFlags.Public | BindingFlags.Static)!);
        HookManager.AddPost(_self, target, self.GetMethod(nameof(AfterMovie), BindingFlags.Public | BindingFlags.Static)!);
        HookManager.Commit();
        bool ok = HookAttach.Installed(target);
        if (!ok) Console.Error.WriteLine("[KF3] ending hold: the movie player's hooks did not install");
        return ok;
    }

    public static void BeforeMovie(CpuContext c, IMemory m)
    {
        _movie = c.A0;
        Console.WriteLine($"[KF3] ending: movie {c.A0}");
    }

    public static void AfterMovie(CpuContext c, IMemory m)
    {
        if (_movie != LastMovie || !Enabled) return;
        _movie = uint.MaxValue;

        // The two writes the tail makes before the pad and the CD are stopped.
        m.WriteU8(StubNext, 0);
        m.WriteU8(StubEnding, 0);

        Console.WriteLine("[KF3] ending: holding the last frame" +
                          (ExitToTitle ? "; any button returns to the title" : ""));
        Console.Out.Flush();

        // Seen going down, not found down: whatever skipped the movie may still be held.
        bool released = !ExitToTitle;
        for (;;)
        {
            c.A0 = 0;
            LibEtc.VSync(c, m);
            if (!ExitToTitle) continue;

            bool down = Controller.State != 0xFFFF || AgentServer.Pressing;
            if (!down) released = true;
            else if (released) break;
        }

        m.WriteU32(StubGp, (uint)TitleIndex);
        m.WriteU8(StubNext, (byte)TitleIndex);
        Console.WriteLine("[KF3] ending: returning to the title");
        Console.Out.Flush();
        c.GP = StubGp;
        Dispatcher.Call(c, m, StubMain);
    }
}
