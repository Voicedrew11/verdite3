using System.Reflection;
using RecompOne.Runtime.Context;
using RecompOne.Runtime.Memory;
using RecompOne.Runtime.Modding;

namespace Kf3;

/// <summary>
/// Let a full memory card be read and saved over.
///
///     KF3_FULLCARD=0   the original checks, to compare (on by default)
///
/// Both executables ask the card the same wrong question. The card check
/// (OPEN.EXE `func_80014174`, GAME.EXE `func_800280D4`, copies of one routine)
/// waits for the card, then proves it writable by creating
/// `bu00:BASLUS-00255TEMP` and deleting it again, returning 2 when that create
/// fails. A save is 3 of the card's 15 blocks, so five saves fill it and the
/// create always fails from then on.
///
/// - The title (0x80011FDC) takes any non-zero answer as "no card", never reads
///   the directory, and leaves Continue off.
/// - The in-game Save (`func_800203C8`) takes 2 as an unformatted card and asks
///   to format it, so nothing can be saved over an existing slot, and the format
///   it offers would erase every save.
/// - The in-game Load (`func_8002008C`) retries the check ten times, then reads
///   the directory anyway, which is why Load still works.
///
/// This turns 2 into 0 after both checks, so the directory decides. The saver
/// (`func_80028750`) only creates a file for a slot that has none, so saving
/// over a slot needs no free block; a new slot on a full card fails its own
/// create and the game reports the save as failed. See "Saves and the start
/// menu" in docs/GAME_INTERNALS.md.
/// </summary>
public static class FullCard
{
    const uint TitleCheck = 0x80014174;   // OPEN.EXE: 0 usable, 1/3 card events, 2 not writable
    const uint GameCheck = 0x800280D4;    // GAME.EXE: the same
    const byte NotWritable = 2;

    public static bool Enabled { get; private set; } = true;

    static readonly ModInfo _self = new() { Id = "kf3.fullcard", Name = "Full card", Version = "1.1" };

    public static void Configure(string? spec)
    {
        if (!string.IsNullOrWhiteSpace(spec))
            Enabled = spec.Trim().ToLowerInvariant() is not ("0" or "off" or "false" or "no");
    }

    public static void Install()
    {
        if (!Enabled) return;
        HookAttach.OnOverlayLoad("full card (title)", () => Attach("open", TitleCheck),
            "A full card will leave Continue off at the title.");
        HookAttach.OnOverlayLoad("full card (game)", () => Attach("game", GameCheck),
            "A full card will ask to be formatted instead of saving.");
    }

    static bool Attach(string overlay, uint address)
    {
        SymbolRegistry.Build();
        var target = SymbolRegistry.Resolve(overlay, null, address);
        if (target == null) return false;
        var impl = typeof(FullCard).GetMethod(nameof(AfterCardCheck), BindingFlags.Public | BindingFlags.Static)!;
        if (!HookManager.AddPost(_self, target, impl)) return false;
        HookManager.Commit();
        bool ok = HookAttach.Installed(target);
        if (!ok) Console.Error.WriteLine($"[KF3] full card: the {overlay} card check hook did not install");
        return ok;
    }

    public static void AfterCardCheck(CpuContext c, IMemory m)
    {
        if (c.V0 != NotWritable) return;
        c.V0 = 0;
        Console.WriteLine("[KF3] full card: no free block for the scratch file; using the card anyway");
    }
}
