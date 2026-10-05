using System.Reflection;
using RecompOne.Runtime.Context;
using RecompOne.Runtime.Memory;
using RecompOne.Runtime.Modding;

namespace Kf3;

/// <summary>
/// Offer Continue on the title when the memory card is full.
///
///     KF3_TITLECONTINUE=0   the original check, to compare (on by default)
///
/// OPEN.EXE asks the card one question before the title menu, and it is the wrong
/// one. `func_80014174` checks the card and then proves it writable by creating
/// `bu00:BASLUS-00255TEMP` and deleting it again, returning 2 when that create
/// fails. The title (0x80011FDC) takes any non-zero answer as "no card", never
/// reads the directory, and leaves Continue off. A save is 3 of the card's 15
/// blocks, so five saves fill it, and from then on the saves are only reachable
/// through the in-game menu's Load.
///
/// GAME.EXE asks the same question in its start menu (`func_800280D4`, a copy of
/// the routine) and treats 2 as a card it can still read: it goes on to the
/// directory. This makes OPEN.EXE's answer mean the same. The directory read
/// `func_80014264` that follows decides whether there are saves, so a card with
/// none still starts with Continue off. See "Saves and the start menu" in
/// docs/GAME_INTERNALS.md.
/// </summary>
public static class TitleContinue
{
    const uint CardCheck = 0x80014174;   // OPEN.EXE: 0 usable, 1/3 card events, 2 not writable
    const byte NotWritable = 2;

    public static bool Enabled { get; private set; } = true;

    static readonly ModInfo _self = new() { Id = "kf3.titlecontinue", Name = "Title continue", Version = "1.0" };

    public static void Configure(string? spec)
    {
        if (!string.IsNullOrWhiteSpace(spec))
            Enabled = spec.Trim().ToLowerInvariant() is not ("0" or "off" or "false" or "no");
    }

    public static void Install()
    {
        if (!Enabled) return;
        HookAttach.OnOverlayLoad("title continue", Attach,
            "A full card will leave Continue off at the title.");
    }

    static bool Attach()
    {
        SymbolRegistry.Build();
        var target = SymbolRegistry.Resolve("open", null, CardCheck);
        if (target == null) return false;
        var impl = typeof(TitleContinue).GetMethod(nameof(AfterCardCheck), BindingFlags.Public | BindingFlags.Static)!;
        if (!HookManager.AddPost(_self, target, impl)) return false;
        HookManager.Commit();
        bool ok = HookAttach.Installed(target);
        if (!ok) Console.Error.WriteLine("[KF3] title continue: the card check hook did not install");
        return ok;
    }

    public static void AfterCardCheck(CpuContext c, IMemory m)
    {
        if (c.V0 != NotWritable) return;
        c.V0 = 0;
        Console.WriteLine("[KF3] title continue: the card is full; reading its saves anyway");
    }
}
