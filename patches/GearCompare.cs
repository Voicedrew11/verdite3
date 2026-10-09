using System.Reflection;
using RecompOne.Runtime.Context;
using RecompOne.Runtime.Events;
using RecompOne.Runtime.Memory;
using RecompOne.Runtime.Modding;
using Rt = RecompOne.Runtime.Runtime;
using Game = Recompiled.KingsField3_game;

namespace Kf3;

/// <summary>
/// Every stat an equip or a purchase would change, beside the equipment list and
/// the shop list, now and after. Ported from Verdite2's GearCompare, on this game's
/// addresses: the panel is drawn by calling this game's own menu routines (the label
/// and number fonts and the window status page 2 uses), not Verdite2's MenuDraw.
///
///     KF3_GEARCOMPARE=1         on (the default); 0, off, false leaves the lists alone
///     KF3_GEARCOMPARE=probe     on, and a line for each item compared
///
/// The switch is a setting under Gameplay (<see cref="GameplaySection"/>), kept in
/// interface.ini; a set variable wins over it.
///
/// The comparison is the game's own stat recompute, <c>func_80029500</c>, run with
/// the item's slot byte written to the candidate's id, and the slot byte, the stat
/// block and <c>0x801C12F0</c> (which the recompute also writes) put back afterwards. The real equip setters are not called, so
/// nothing a comparison computes is kept. The lists are the game's own: the picker
/// (<c>func_8001CBB8</c>), the shop (<c>func_80021298</c>) and the list drawer
/// (<c>func_80025468</c>), whose post hook draws the panel from its callers.
/// See "Comparing gear" in docs/GAME_INTERNALS.md.
/// </summary>
public static class GearCompare
{
    // ---- The routines hooked, in the game module ----
    const uint EquipPicker = 0x8001CBB8;   // func_8001CBB8(row): the equipment page's picker
    const uint BuyPage = 0x80021298;       // func_80021298: the shop
    const uint ListDraw = 0x80025468;      // func_80025468: the scrolling list drawer
    // ---- Their callers, the return addresses the post hook tells apart ----
    const uint EquipListReturn = 0x8001CF84;   // the equipment candidate list
    const uint BuyListReturn = 0x800215B0;     // the shop's buy list
    const uint PromptReturn = 0x80024F34;      // the USE / CANCEL prompt, or the buy prompt
    // ---- The equipment page's frame and the shop's frame, on the caller's SP ----
    const uint EquipListCount = 0x36, EquipListCursor = 0x39, EquipListIds = 0x438;
    const uint BuyListCount = 0x3E, BuyListCursor = 0x41, BuyListIds = 0x1190;
    // ---- GAME.EXE's equipment slots and the stat block ----
    const uint SlotWeapon = 0x801B25AF, SlotHead = 0x801B25D4, SlotBody = 0x801B25D5;
    const uint SlotShield = 0x801B25D6, SlotArms = 0x801B25D7, SlotFeet = 0x801B25D8;
    const uint SlotRing1 = 0x801B25D9, SlotRing2 = 0x801B25DA;
    const uint StatBlock = 0x801B2524, StatBlockLength = 0x38;
    const uint StatWord = 0x801C12F0;    // the recompute copies 0x801B2584 here when non-zero
    const byte NoItem = 0xFF;              // an empty ring slot, and the TAKE OFF row
    // ---- The menu's fonts and the window, as the game draws them ----
    const uint LabelFont = 0x8007E570, NumberFont = 0x8007E564;
    // ---- Guest stack for the records; the frame below c.SP ----
    const uint Frame = 0x100;
    const uint LabelRecord = 0x40, NumberRecord = 0x80;
    // ---- Layout in PS1 320x240. Untested by eye. ----
    const int Right = 293, Top = 24, Bottom = 141, Pad = 6;
    const int FullRowH = 13, FullW = 121;
    const int CompactRowH = 12, CellW = 78;

    /// <summary>interface.ini key, read and written by the Gameplay tab.</summary>
    public const string OnKey = "kf3.gearcompare.enabled";

    /// <summary>Live: the hooks stay attached and draw nothing when this is off.</summary>
    public static bool Enabled { get; private set; } = true;

    static bool _probe;
    static bool _fromEnv;

    // The picker's row and the shop's flag, set by the pre hooks and cleared by the post ones.
    static int _row = -1;
    static bool _buying;

    // The last comparison: its slot and id, and the rows it drew.
    static bool _have;
    static uint _slot, _id;
    static List<(string Long, string Short, int Now, int New, bool Head)> _rows = [];

    static readonly Dictionary<string, byte[]> _encoded = [];

    // Group 0 has no heading. Groups 2 and 3 end in a TOTAL.
    static readonly (string Long, string Short)[] Heads =
    [
        ("", ""),
        ("MAG", "MAG"),
        ("OFFENSE", "OFF"),
        ("DEFENSE", "DEF"),
    ];

    // The stats in the order they are listed, each with its group.
    static readonly (string Long, string Short, uint Addr, int Group)[] Stats =
    [
        ("PWR", "PWR", 0x801B2524, 0),
        ("HOLY", "HLY", 0x801B252E, 1),
        ("FIRE", "FIR", 0x801B2526, 1),
        ("EARTH", "ERT", 0x801B2528, 1),
        ("WIND", "WND", 0x801B252C, 1),
        ("WATER", "WTR", 0x801B252A, 1),
        ("SLASH", "SLA", 0x801B2538, 2),
        ("BLOW", "BLW", 0x801B253A, 2),
        ("STAB", "STB", 0x801B253C, 2),
        ("HOLY", "HLY", 0x801B253E, 2),
        ("FIRE", "FIR", 0x801B2540, 2),
        ("EARTH", "ERT", 0x801B2542, 2),
        ("WIND", "WND", 0x801B2544, 2),
        ("WATER", "WTR", 0x801B2546, 2),
        ("SLASH", "SLA", 0x801B254A, 3),
        ("BLOW", "BLW", 0x801B254C, 3),
        ("STAB", "STB", 0x801B254E, 3),
        ("POISON", "PSN", 0x801B2550, 3),
        ("DARK", "DRK", 0x801B2552, 3),
        ("FIRE", "FIR", 0x801B2554, 3),
        ("EARTH", "ERT", 0x801B2556, 3),
        ("WIND", "WND", 0x801B2558, 3),
        ("WATER", "WTR", 0x801B255A, 3),
    ];

    static readonly ModInfo _self = new()
    {
        Id = "kf3.gearcompare",
        Name = "Gear compare",
        Version = "1.0",
        Description = "Shows every stat an equip or a purchase would change.",
    };

    /// <summary>KF3_GEARCOMPARE: null or blank leaves the saved setting alone.</summary>
    public static void Configure(string? mode)
    {
        if (string.IsNullOrWhiteSpace(mode)) return;
        string v = mode.Trim().ToLowerInvariant();
        Enabled = v is not ("0" or "off" or "false");
        _probe = v == "probe";
        _fromEnv = true;
    }

    public static void Install()
    {
        Event.AddListener<RuntimeReadyEvent>(_ =>
        {
            if (!_fromEnv) Enabled = Rt.View.GetInt(OnKey, Enabled ? 1 : 0) != 0;
        });

        HookAttach.OnOverlayLoad("gear compare", Attach,
            "Equipping and buying will show no stat comparison. See \"Comparing gear\" in docs/GAME_INTERNALS.md.");
    }

    public static void SetEnabled(bool on) => Enabled = on;

    static MethodInfo M(string name) => typeof(GearCompare).GetMethod(name, BindingFlags.Public | BindingFlags.Static)!;

    static bool Attach()
    {
        SymbolRegistry.Build();
        var picker = SymbolRegistry.Resolve("game", null, EquipPicker);
        var buy = SymbolRegistry.Resolve("game", null, BuyPage);
        var list = SymbolRegistry.Resolve("game", null, ListDraw);
        if (picker == null || buy == null || list == null) return false;

        // All five or none: a panel without its clearing hooks would hang on the screen.
        if (!HookManager.AddPre(_self, picker, M(nameof(BeforePicker)))
            || !HookManager.AddPost(_self, picker, M(nameof(AfterPicker)))
            || !HookManager.AddPre(_self, buy, M(nameof(BeforeBuy)))
            || !HookManager.AddPost(_self, buy, M(nameof(AfterBuy)))
            || !HookManager.AddPost(_self, list, M(nameof(AfterList))))
        {
            HookManager.RemoveMod(_self);
            return false;
        }
        HookManager.Commit();

        bool ok = HookAttach.Installed(picker) && HookAttach.Installed(buy) && HookAttach.Installed(list);
        Console.WriteLine(ok
            ? $"[KF3] gear compare: {(Enabled ? "on" : "off")}{(_probe ? ", probe" : "")}, 3 routines hooked"
            : "[KF3] gear compare: the hooks did not install");
        return ok;
    }

    /// <summary>The equipment picker is about to run for a row.</summary>
    public static bool BeforePicker(CpuContext c, IMemory m)
    {
        _row = (int)c.A0;
        _have = false;
        return true;
    }

    /// <summary>The equipment picker has returned: no row is shown.</summary>
    public static void AfterPicker(CpuContext c, IMemory m)
    {
        _row = -1;
        _have = false;
    }

    /// <summary>The shop is about to run.</summary>
    public static bool BeforeBuy(CpuContext c, IMemory m)
    {
        _buying = true;
        _have = false;
        return true;
    }

    /// <summary>The shop has returned.</summary>
    public static void AfterBuy(CpuContext c, IMemory m)
    {
        _buying = false;
        _have = false;
    }

    /// <summary>After the list drawer: the candidate under the cursor is compared and
    /// drawn beside the list. In a post hook c.SP and c.RA are the caller's.</summary>
    public static void AfterList(CpuContext c, IMemory m)
    {
        if (!Enabled) return;
        try
        {
            uint sp = c.SP;
            if (c.RA == EquipListReturn && _row >= 0)
            {
                if (m.ReadU8(sp + EquipListCount) == 0) return;
                uint id = m.ReadU8(sp + EquipListIds + m.ReadU8(sp + EquipListCursor));   // 0xFF is TAKE OFF
                if (EquipSlot(_row) is not { } slot) return;
                Update(c, m, slot, id);
                Draw(c, m);
            }
            else if (c.RA == BuyListReturn && _buying)
            {
                if (m.ReadU8(sp + BuyListCount) == 0) return;
                uint id = m.ReadU8(sp + BuyListIds + m.ReadU8(sp + BuyListCursor));
                if (ShopSlot(m, id) is not { } slot)
                {
                    _have = false;
                    return;
                }
                Update(c, m, slot, id);
                Draw(c, m);
            }
            else if (c.RA == PromptReturn && (_row >= 0 || _buying) && _have)
            {
                Draw(c, m);
            }
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"[KF3] gear compare: {e.Message}");
            _have = false;
        }
    }

    /// <summary>The slot byte an equipment row writes, or null for a row with none.</summary>
    static uint? EquipSlot(int row) => row switch
    {
        0 => SlotWeapon,
        2 => SlotShield,
        3 => SlotHead,
        4 => SlotBody,
        5 => SlotArms,
        6 => SlotFeet,
        7 => SlotRing1,
        8 => SlotRing2,
        _ => null,
    };

    /// <summary>The slot byte a shop item goes to, by its id's range. A ring takes the
    /// first empty ring slot, else the first.</summary>
    static uint? ShopSlot(IMemory m, uint id)
    {
        if (id <= 0x21) return SlotWeapon;
        if (id <= 0x29) return SlotHead;
        if (id <= 0x32) return SlotBody;
        if (id <= 0x3E) return SlotShield;
        if (id <= 0x47) return SlotArms;
        if (id <= 0x50) return SlotFeet;
        if (id <= 0x5E)
        {
            if (m.ReadU8(SlotRing1) == NoItem) return SlotRing1;
            if (m.ReadU8(SlotRing2) == NoItem) return SlotRing2;
            return SlotRing1;
        }
        return null;
    }

    /// <summary>Compares <paramref name="id"/> in <paramref name="slot"/> with what is
    /// there now. The recompute rewrites the stat block and <c>0x801C12F0</c>; both are
    /// put back with the slot byte, so the comparison changes nothing.</summary>
    static void Update(CpuContext c, IMemory m, uint slot, uint id)
    {
        if (_have && slot == _slot && id == _id) return;

        var now = new int[Stats.Length];
        for (int i = 0; i < Stats.Length; i++) now[i] = m.ReadU16(Stats[i].Addr);

        byte old = m.ReadU8(slot);
        var block = new byte[StatBlockLength];
        for (int i = 0; i < block.Length; i++) block[i] = m.ReadU8(StatBlock + (uint)i);
        uint word = m.ReadU32(StatWord);

        var after = new int[Stats.Length];
        var saved = c.Snapshot();
        try
        {
            m.WriteU8(slot, (byte)id);
            c.SP -= 0x100u;
            Game.func_80029500(c, m);
            for (int i = 0; i < Stats.Length; i++) after[i] = m.ReadU16(Stats[i].Addr);
        }
        finally
        {
            m.WriteU8(slot, old);
            for (int i = 0; i < block.Length; i++) m.WriteU8(StatBlock + (uint)i, block[i]);
            m.WriteU32(StatWord, word);
            c.Restore(saved);
        }

        var rows = new List<(string Long, string Short, int Now, int New, bool Head)>();
        for (int g = 0; g < Heads.Length; g++)
        {
            bool any = false;
            int sumNow = 0, sumNew = 0;
            for (int i = 0; i < Stats.Length; i++)
            {
                if (Stats[i].Group != g) continue;
                sumNow += now[i];
                sumNew += after[i];
                if (now[i] == after[i]) continue;
                if (!any && g != 0) rows.Add((Heads[g].Long, Heads[g].Short, 0, 0, true));
                any = true;
                rows.Add((Stats[i].Long, Stats[i].Short, now[i], after[i], false));
            }
            if (any && (g == 2 || g == 3)) rows.Add(("TOTAL", "TOT", sumNow, sumNew, false));
        }

        _rows = rows;
        _slot = slot;
        _id = id;
        _have = true;

        if (_probe)
        {
            string text = rows.Count == 0
                ? "no change"
                : string.Join(", ", rows.Where(r => !r.Head).Select(r => $"{r.Long} {r.Now}>{r.New}"));
            Console.WriteLine($"[KF3] gear compare: slot 0x{slot:X8} id 0x{id:X2}: {text}");
        }
    }

    /// <summary>The panel: the full layout when it fits beside the page, else the compact one.</summary>
    static void Draw(CpuContext c, IMemory m)
    {
        var saved = c.Snapshot();
        try
        {
            c.SP -= Frame;
            bool headFirst = _rows.Count > 0 && _rows[0].Head;
            int lines = 1 + (_rows.Count == 0 ? 1 : _rows.Count - (headFirst ? 1 : 0));
            int height = Pad + lines * FullRowH + Pad;
            if (Top + height <= Bottom) DrawFull(c, m, headFirst, height);
            else DrawCompact(c, m);
        }
        finally
        {
            c.Restore(saved);
        }
    }

    /// <summary>One column with the long labels: NOW and NEW over the numbers, and the
    /// first heading on the first line with them.</summary>
    static void DrawFull(CpuContext c, IMemory m, bool headFirst, int height)
    {
        int x = Right - FullW;
        int y0 = Top + Pad;
        Text(c, m, x + 62, y0, "NOW");
        Text(c, m, x + 92, y0, "NEW");
        int first = 0;
        if (headFirst)
        {
            Text(c, m, x + 8, y0, _rows[0].Long);
            first = 1;
        }
        if (_rows.Count == 0) Text(c, m, x + 8, y0 + FullRowH, "NO CHANGE");
        for (int j = first, k = 1; j < _rows.Count; j++, k++)
        {
            var r = _rows[j];
            int y = y0 + k * FullRowH;
            if (r.Head)
            {
                Text(c, m, x + 8, y, r.Long);
                continue;
            }
            Text(c, m, x + 14, y, r.Long);
            Number(c, m, x + 62, y, r.Now);
            Number(c, m, x + 92, y, r.New);
        }
        Window(c, m, x, Top, FullW, height);
    }

    /// <summary>Two columns of short labels when the long layout would cover the list.
    /// A single heading is dropped: its items say what they are.</summary>
    static void DrawCompact(CpuContext c, IMemory m)
    {
        int headings = _rows.Count(r => r.Head);
        var rows = _rows.Where(r => !(r.Head && headings == 1)).ToList();
        int n = rows.Count;
        int perCol = Math.Max(1, (n + 1) / 2);
        int cols = n > perCol ? 2 : 1;
        int w = 8 + cols * CellW + 2;
        int x = Right - w;
        int height = Pad * 2 + (1 + perCol) * CompactRowH;
        int top = Top;
        if (top + height > Bottom) top = Math.Max(8, Bottom - height);
        int y0 = top + Pad;

        for (int col = 0; col < cols; col++)
        {
            int cx = x + 8 + col * CellW;
            Text(c, m, cx + 25, y0, "NOW");
            Text(c, m, cx + 50, y0, "NEW");
        }
        if (n == 0) Text(c, m, x + 8, y0 + CompactRowH, "NO CHANGE");
        for (int k = 0; k < n; k++)
        {
            var r = rows[k];
            int cx = x + 8 + (k / perCol) * CellW;
            int y = y0 + (k % perCol + 1) * CompactRowH;
            Text(c, m, cx, y, r.Short);
            if (r.Head) continue;
            Number(c, m, cx + 25, y, r.Now);
            Number(c, m, cx + 50, y, r.New);
        }
        Window(c, m, x, top, w, height);
    }

    /// <summary>A label record at c.SP + 0x40, its string ending 0xFF, drawn with the label font.</summary>
    static void Text(CpuContext c, IMemory m, int x, int y, string s)
    {
        if (!_encoded.TryGetValue(s, out var bytes)) _encoded[s] = bytes = MenuFont.Encode(s);
        uint rec = c.SP + LabelRecord;
        m.WriteU16(rec, (ushort)x);
        m.WriteU16(rec + 2u, (ushort)y);
        for (int i = 0; i < bytes.Length; i++) m.WriteU8(rec + 4u + (uint)i, bytes[i]);
        c.A0 = LabelFont;
        c.A1 = rec;
        Game.func_800261DC(c, m);
    }

    /// <summary>A number record at c.SP + 0x80: the game's formatter writes the digits
    /// into the record's string, which the number font then draws.</summary>
    static void Number(CpuContext c, IMemory m, int x, int y, int v)
    {
        v = Math.Clamp(v, 0, 999);
        uint rec = c.SP + NumberRecord;
        m.WriteU16(rec, (ushort)x);
        m.WriteU16(rec + 2u, (ushort)y);
        m.WriteU32(c.SP + 0x10u, rec + 4u);
        c.A0 = (uint)v;
        c.A1 = 3;
        c.A2 = 0;
        c.A3 = 0;
        Game.func_800277C0(c, m);
        c.A0 = NumberFont;
        c.A1 = rec;
        Game.func_8002636C(c, m);
    }

    /// <summary>The menu's own box, drawn last so it lies under the text.</summary>
    static void Window(CpuContext c, IMemory m, int x, int y, int w, int h)
    {
        c.A0 = (uint)x;
        c.A1 = (uint)y;
        c.A2 = (uint)w;
        c.A3 = (uint)h;
        Game.func_80026ACC(c, m);
    }
}
