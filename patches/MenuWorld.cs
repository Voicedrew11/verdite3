using System.Diagnostics;
using System.Reflection;
using RecompOne.Runtime;
using RecompOne.Runtime.Context;
using RecompOne.Runtime.Events;
using RecompOne.Runtime.Memory;
using RecompOne.Runtime.Modding;
using Game = Recompiled.KingsField3_game;

namespace Kf3;

/// <summary>
/// Draw the world live behind a menu or a full-screen message (a sign, a line of
/// dialogue), instead of the 320-wide copy of the frozen frame. Verdite2's
/// <c>MenuWorld</c>, on this game's addresses.
///
///     KF3_MENUWORLD=0         the game's frozen copy -- comparison only
///     KF3_MENUWORLD_PROBE=1   world passes a second, primitive bytes used, overflows;
///                             a line per message fade
///     KF3_MENUWORLD_TEST=6:305,...  open those messages (file:entry) from the
///                             player's tick, ten seconds into the first area and
///                             five after each closes
///
/// Every menu runs on one framework: <c>func_80027198</c> shrinks the primitive
/// buffers and stores the displayed frame, <c>func_80026FE4</c> is the frame head
/// and <c>func_800270F8</c> the presenter, which pastes the stored frame back with
/// <c>LoadImage</c> every frame. That paste is 1x, 320 wide and carries no depth, so
/// the margin is never drawn and nothing that needs geometry reaches it. The
/// presenter is replaced: stage 15's drawing half runs into an ordering table of its
/// own in the frozen frame's store, which nothing reads once the paste is gone, that
/// table's end is linked to the head of the menu's, and one <c>DrawOTag</c> draws
/// both.
///
/// A message, <c>func_800441D4</c>, is the same shape with its own loop, the fade
/// <c>func_80043BB8</c>, which pastes a <c>MoveImage</c> copy of the frame dimmed by
/// its brightness; between the fades the game draws nothing at all, so the wide
/// target goes idle and the present falls back to the 1x VRAM frame.
/// <see cref="MessageFade"/> is that loop in C# with the world drawn live, and it
/// waits for the button itself, still drawing.
///
/// See "Menus and messages draw the world live" in docs/WIDESCREEN.md.
/// </summary>
public static class MenuWorld
{
    const uint Presenter = 0x800270F8;
    const uint Enter = 0x80027198;
    const uint Leave = 0x80027310;
    const uint Stage15Routine = 0x800422B8;
    const uint MainLoopReturn = 0x80014FB0;   // the main loop's own jal to stage 15
    const uint Fade = 0x80043BB8;             // a message's fade loop
    const uint PlayerTick = 0x80030FCC;       // stage 4, where KF3_MENUWORLD_TEST opens its messages

    /// <summary>Plays a sound; the model walk calls it for ambient sources, which a
    /// pass must not start.</summary>
    const uint SoundPlay = 0x8001576C;

    const uint BufferIndex = 0x801AEAE8;      // u8
    const uint Descriptor0 = 0x80199158, Descriptor1 = 0x80199164;   // {start, end, cur}
    const uint ActiveDescriptor = 0x80199170, OtPointer = 0x801A9174, FrontPointer = 0x801A91B8;
    const uint DrawEnvs = 0x801A91BC, DrawEnvStride = 0x5C;
    const uint DispEnvs = 0x801A9274, DispEnvStride = 0x14;

    /// <summary>The draw environment's clear: <c>isbg</c> and its colour (DRAWENV
    /// +0x18). The menu's enter turns it off, since its paste covers the frame; the
    /// pass needs it back, or the fog blends over the last menu frame.</summary>
    const uint EnvClear = 0x18;

    const uint MenuBytes = 0x7400;            // each shrunk buffer
    const uint SavedDescriptor1 = 0x8009C410; // the enter's copy of the world's desc1
    const uint FrameStore = 0x8009C388;       // gp+0x174: where the enter stored the frame
    const uint FrameRect = 0x8009C38C;        // gp+0x178: the RECT it stored and pastes

    const uint MessageRect = 0x8009C2D0;      // the texture space a message saves, MoveImages into, restores
    const uint MessageSave = 0x80199168;      // where it saved it: desc1's end, start + 0xE800
    const uint ExamineMask = 0x80081876;      // u16: the examine button
    const uint PadWord = 0x801B265C;          // u16: the player's pad word

    // Zeroed by the frame head func_80035630, which a pass does not call.
    static readonly uint[] FrameCounters = [0x801AEB10, 0x801AEB14, 0x801AEB18];
    static readonly uint[] FogWords = [0x801AEC7C, 0x801AEC80];

    const uint Scratchpad = 0x1F800000, ScratchpadWords = 0x100;
    const uint OtEntries = 0x2000, OtBytes = OtEntries * 4u, FrontEntries = 8;
    const uint TableOffset = 0x10;            // the pass's descriptor, then its table, its front table, its primitives
    const uint ArenaOffset = TableOffset + OtBytes + FrontEntries * 4u;
    const uint MinArena = 0x10000;

    public static bool Enabled { get; set; } = true;

    /// <summary>How many vblanks each step of a message's fade is shown for: 1 is the
    /// game's own (one step a <c>VSync(0)</c>, ten steps in, seven out), more holds each
    /// brightness that many times longer. The wait for the button is not touched.
    /// <c>KF3_MESSAGE_FADE</c>, else Gameplay's <see cref="FadeKey"/>.</summary>
    public static int FadeVBlanks { get; private set; } = 1;
    public const string FadeKey = "kf3.messagefade";
    public const int MaxFadeVBlanks = 4;
    public static void SetFadeVBlanks(int vblanks) => FadeVBlanks = Math.Clamp(vblanks, 1, MaxFadeVBlanks);
    static bool _probe, _diff;

    static bool _hooked, _queued, _worldSeen, _session;
    static int _depth;
    static bool _drawing;

    // A menu session's pass region: the frozen frame's store.
    static uint _store, _storeEnd;

    // The world as the last stage 15 left it, put back for a pass.
    static readonly Gte.State _worldGte = new(), _menuGte = new();
    static readonly uint[] _worldPad = new uint[ScratchpadWords], _menuPad = new uint[ScratchpadWords];
    static readonly uint[] _worldFog = new uint[2], _worldClear = new uint[2];
    static Camera? _worldView;
    static double _worldFraction = 1.0;
    static readonly byte[] _camera = new byte[CameraBlock.Bytes];

    static readonly Stopwatch _clock = Stopwatch.StartNew();
    static double _windowMs;
    static int _passes, _overflows, _refused, _messages, _messagesRefused;
    static uint _peak, _arena;

    // KF3_MENUWORLD_TEST: messages to open, and when.
    static readonly Queue<(uint File, uint Entry)> _tests = new();
    static readonly Stopwatch _testClock = new();
    static double _testAt = 10.0;

    static readonly ModInfo _self = new()
    {
        Id = "kf3.menuworld",
        Name = "Menu world",
        Version = "1.0",
        Description = "Draws the world live behind menus and messages instead of a frozen 320-wide copy.",
    };

    public static void Configure(string? enabled, string? probe, string? test)
    {
        if (!string.IsNullOrWhiteSpace(enabled)) Enabled = enabled.Trim() is not ("0" or "off");
        _probe = probe?.Trim() is "1" or "2";
        _diff = probe?.Trim() == "2";
        foreach (var item in (test ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var f = item.Split(':');
            if (f.Length != 2 || !uint.TryParse(f[0], out uint file) || !uint.TryParse(f[1], out uint entry))
                throw new ArgumentException($"KF3_MENUWORLD_TEST: bad message '{item}' (file:entry)");
            _tests.Enqueue((file, entry));
        }
    }

    public static void Install()
    {
        Event.AddListener<OverlayLoadedEvent>(e =>
        {
            _worldSeen = false;
            _session = false;
            _depth = 0;
            Mouse.Suspend(false);
            if (_tests.Count > 0 && !_testClock.IsRunning && e.Name.StartsWith("fdat", StringComparison.Ordinal))
                _testClock.Start();
        });
        HookAttach.OnOverlayLoad("menu world", Attach);
    }

    static bool Attach()
    {
        SymbolRegistry.Build();
        uint[] addresses = [Presenter, Enter, Leave, Stage15Routine, Fade, SoundPlay, PlayerTick];
        var fns = addresses.Select(a => SymbolRegistry.Resolve("game", null, a)).ToArray();
        if (fns.Any(f => f == null))
        {
            Console.Error.WriteLine("[KF3] menu world: game functions not found; menus keep the frozen frame.");
            return false;
        }

        MethodInfo M(string name) => typeof(MenuWorld).GetMethod(name, BindingFlags.Public | BindingFlags.Static)!;
        // Queued once: Installed() is true for a function any patch has hooked, so it
        // cannot say whether *these* delegates are on it. A retry only re-commits.
        if (!_queued)
        {
            _queued = true;
            HookManager.AddReplace(_self, fns[0]!, M(nameof(Present)));
            HookManager.AddPost(_self, fns[1]!, M(nameof(AfterEnter)));
            HookManager.AddPost(_self, fns[2]!, M(nameof(AfterLeave)));
            HookManager.AddPre(_self, fns[3]!, M(nameof(BeforeFrame)));
            HookManager.AddPost(_self, fns[3]!, M(nameof(AfterFrame)));
            HookManager.AddReplace(_self, fns[4]!, M(nameof(MessageFade)));
            if (_diff && SymbolRegistry.Resolve("game", null, 0x80035700) is { } swap)
                HookManager.AddPre(_self, swap, M(nameof(BeforeSwap)));
            HookManager.AddPre(_self, fns[5]!, M(nameof(Quiet)));
            if (_tests.Count > 0) HookManager.AddPre(_self, fns[6]!, M(nameof(Test)));
        }
        HookManager.Commit();

        _hooked = fns.Take(6).All(f => HookAttach.Installed(f!));
        Console.WriteLine($"[KF3] menu world: {(Enabled && _hooked ? "on" : "off")}" +
                          (_hooked ? "" : " (hooks incomplete)"));
        return _hooked;
    }

    /// <summary>The ambient sound the model walk would start: not from a pass.</summary>
    public static bool Quiet(CpuContext c, IMemory m) => !_drawing;

    /// <summary>Stage 15's pre: the main loop drawing means no menu is open, and the
    /// fraction its smoothers will draw at is the one a pass draws at.</summary>
    public static void BeforeFrame(CpuContext c, IMemory m)
    {
        if (_drawing) return;
        if (c.RA == MainLoopReturn)
        {
            // A session left some other way ends here.
            _session = false;
            _depth = 0;
            Mouse.Suspend(false);
        }
        _worldFraction = FramePacing.TickFraction;
        _inStage15Frame = true;
    }

    /// <summary>Stage 15's post: record what the world was drawn with.</summary>
    public static void AfterFrame(CpuContext c, IMemory m)
    {
        if (_drawing) return;
        _inStage15Frame = false;
        _worldSeen = true;
        Gte.Save(_worldGte);
        for (uint i = 0; i < ScratchpadWords; i++) _worldPad[i] = m.ReadU32(Scratchpad + i * 4u);
        for (int i = 0; i < FogWords.Length; i++) _worldFog[i] = m.ReadU32(FogWords[i]);
        for (uint i = 0; i < 2; i++) _worldClear[i] = m.ReadU32(DrawEnvs + i * DrawEnvStride + EnvClear);
        // The block holds the tick's camera; a carried frame was drawn from the override.
        _worldView = Stage15.InCSharp ? Stage15.ViewOverride : null;
    }

    public static void AfterEnter(CpuContext c, IMemory m)
    {
        if (_depth++ > 0) return;

        // A menu wants a pointer; the mouse gets it back when the menu closes.
        Mouse.Suspend(true);
        bool can = Enabled && _hooked && _worldSeen && Stage15.CanDraw;
        _session = can && Layout(m);
        if (can && !_session) _refused++;
        if (_probe)
            Console.WriteLine($"[KF3] menu world: menu entered from 0x{c.RA:X8}, " +
                              (_session ? $"live, pass at 0x{_store:X8}" : "the game's paste"));
    }

    public static void AfterLeave(CpuContext c, IMemory m)
    {
        if (_depth > 0 && --_depth == 0)
        {
            _session = false;
            Mouse.Suspend(false);
        }
    }

    /// <summary>The shrunk buffers the enter leaves: <c>start .. start + 0x7400</c> and
    /// on to <c>+ 0xE800</c>, and the 0x25800-byte frame store after them.</summary>
    static bool Shrunk(IMemory m, out uint start)
    {
        start = m.ReadU32(Descriptor0);
        return m.ReadU32(Descriptor0 + 4u) == start + MenuBytes &&
               m.ReadU32(Descriptor1) == start + MenuBytes &&
               m.ReadU32(Descriptor1 + 4u) == start + 2u * MenuBytes;
    }

    /// <summary>Check the shrunk layout is the one expected; the pass goes in the
    /// frame store, which ends where the world's second buffer did.</summary>
    static bool Layout(IMemory m)
    {
        if (!Shrunk(m, out uint start)) return false;
        uint store = m.ReadU32(FrameStore);
        uint bytes = m.ReadU16(FrameRect + 4u) * (uint)m.ReadU16(FrameRect + 6u) * 2u;
        uint end = store + bytes;
        if (store != start + 2u * MenuBytes || bytes < ArenaOffset + MinArena) return false;
        if (end > m.ReadU32(SavedDescriptor1 + 4u) || end > 0x80000000u + Runtime.RamSize) return false;
        _store = store;
        _storeEnd = end;
        return true;
    }

    /// <summary>
    /// <c>func_800270F8</c>: DrawSync, VSync, PutDispEnv, PutDrawEnv, the paste,
    /// DrawOTag -- with the world in place of the paste.
    /// </summary>
    public static void Present(Action<CpuContext, IMemory> orig, CpuContext c, IMemory m)
    {
        if (!_session || !Enabled || m is not PSMemory mem)
        {
            orig(c, m);
            return;
        }

        var saved = c.Snapshot();
        c.A0 = 0u;
        Game.func_80079BA0(c, mem);                 // DrawSync(0)

        uint menuOt = mem.ReadU32(OtPointer);
        uint head = DrawWorld(c, mem, _store, _storeEnd, menuOt + (OtEntries - 1u) * 4u);

        c.A0 = 0u;
        Game.func_8007910C(c, mem);                 // VSync(0)

        uint idx = mem.ReadU8(BufferIndex);
        c.A0 = DispEnvs + idx * DispEnvStride;
        Game.func_8007A274(c, mem);                 // PutDispEnv
        var clear = WorldClear(mem);
        c.A0 = DrawEnvs + idx * DrawEnvStride;
        Game.func_8007A178(c, mem);                 // PutDrawEnv, with the world's clear
        RestoreClear(mem, clear);

        c.A0 = head;
        Game.func_8007A104(c, mem);                 // DrawOTag

        c.Restore(saved);
        if (_probe) Report();
    }

    /// <summary>
    /// Stage 15's drawing half into a table of its own at <paramref name="region"/>,
    /// its primitives after it up to <paramref name="regionEnd"/>, the end of that
    /// table linked to <paramref name="next"/>. Returns the entry a walk starts at.
    /// Everything the pass moves is put back, and the world's own state is put in
    /// for it: the GTE, the scratchpad, the fog, the camera it was drawn from.
    /// </summary>
    static uint DrawWorld(CpuContext c, PSMemory mem, uint region, uint regionEnd, uint next)
    {
        uint desc = region, table = region + TableOffset, front = table + OtBytes, arena = region + ArenaOffset;

        var regs = c.Snapshot();
        Gte.Save(_menuGte);
        for (uint i = 0; i < ScratchpadWords; i++) _menuPad[i] = mem.ReadU32(Scratchpad + i * 4u);
        uint activeDesc = mem.ReadU32(ActiveDescriptor), ot = mem.ReadU32(OtPointer), frontPtr = mem.ReadU32(FrontPointer);
        for (uint i = 0; i < CameraBlock.Bytes; i++) _camera[i] = mem.ReadU8(CameraBlock.Start + i);
        uint fog0 = mem.ReadU32(FogWords[0]), fog1 = mem.ReadU32(FogWords[1]);
        uint n0 = mem.ReadU32(FrameCounters[0]), n1 = mem.ReadU32(FrameCounters[1]), n2 = mem.ReadU32(FrameCounters[2]);

        ClearTable(mem, table, OtEntries);
        ClearTable(mem, front, FrontEntries);
        mem.WriteU32(desc, arena);
        mem.WriteU32(desc + 4u, regionEnd);
        mem.WriteU32(desc + 8u, arena);
        mem.WriteU32(ActiveDescriptor, desc);
        mem.WriteU32(OtPointer, table);
        mem.WriteU32(FrontPointer, front);
        foreach (uint a in FrameCounters) mem.WriteU32(a, 0u);
        for (uint i = 0; i < ScratchpadWords; i++) mem.WriteU32(Scratchpad + i * 4u, _worldPad[i]);
        for (int i = 0; i < FogWords.Length; i++) mem.WriteU32(FogWords[i], _worldFog[i]);
        Gte.Load(_worldGte);
        if (_worldView is { } view) CameraBlock.Store(mem, view);

        _drawing = true;
        FramePacing.FrozenFraction = _worldFraction;
        FramePacing.Frozen = true;
        // The pass is a scene to the retained renderer, as stage 15's frame is: without
        // it the world behind a menu fell back to the packet path, a different picture
        // from play's whenever "Retained GPU" is chosen.
        GpuWorld.SceneIn(c, mem);
        try
        {
            Stage15.DrawScene(c, mem, () => GpuWorld.Begin(c, mem));
        }
        finally
        {
            GpuWorld.SceneOut(c, mem);
            FramePacing.Frozen = false;
            _drawing = false;

            if (_diff) Compare(mem, table + (OtEntries - 1u) * 4u, front);

            uint cur = mem.ReadU32(desc + 8u);
            uint used = cur - arena;
            if (used > _peak) _peak = used;
            if (cur > regionEnd) _overflows++;
            _arena = regionEnd - arena;
            _passes++;

            // The front table spliced in after entry 0x1FFE, as stage 15's swap
            // func_80035700 does, then the table's end linked to the next one.
            uint e = table + (OtEntries - 2u) * 4u;
            uint link = mem.ReadU32(e);
            mem.WriteU32(front, (mem.ReadU32(front) & 0xFF000000u) | (link & 0x00FFFFFFu));
            mem.WriteU32(e, (link & 0xFF000000u) | ((front + (FrontEntries - 1u) * 4u) & 0x00FFFFFFu));
            Terminate(mem, table, next);

            mem.WriteU32(ActiveDescriptor, activeDesc);
            mem.WriteU32(OtPointer, ot);
            mem.WriteU32(FrontPointer, frontPtr);
            for (uint i = 0; i < CameraBlock.Bytes; i++) mem.WriteU8(CameraBlock.Start + i, _camera[i]);
            mem.WriteU32(FogWords[0], fog0);
            mem.WriteU32(FogWords[1], fog1);
            mem.WriteU32(FrameCounters[0], n0);
            mem.WriteU32(FrameCounters[1], n1);
            mem.WriteU32(FrameCounters[2], n2);
            for (uint i = 0; i < ScratchpadWords; i++) mem.WriteU32(Scratchpad + i * 4u, _menuPad[i]);
            Gte.Load(_menuGte);
            c.Restore(regs);
        }
        return table + (OtEntries - 1u) * 4u;
    }

    /// <summary><c>ClearOTagR</c>: each entry links to the one before it, and entry 0
    /// ends the walk.</summary>
    static void ClearTable(PSMemory mem, uint table, uint entries)
    {
        mem.WriteU32(table, 0x00FFFFFFu);
        for (uint i = 1; i < entries; i++)
            mem.WriteU32(table + i * 4u, (table + (i - 1u) * 4u) & 0x00FFFFFFu);
    }

    /// <summary>Point the end of the walk that starts at entry 0 -- entry 0 itself, or
    /// the last primitive added there -- at <paramref name="next"/>.</summary>
    static void Terminate(PSMemory mem, uint table, uint next)
    {
        uint at = table;
        for (int guard = 0; guard < 0x10000; guard++)
        {
            uint tag = mem.ReadU32(at);
            if ((tag & 0x00FFFFFFu) == 0x00FFFFFFu)
            {
                mem.WriteU32(at, (tag & 0xFF000000u) | (next & 0x00FFFFFFu));
                return;
            }
            at = 0x80000000u | (tag & 0x00FFFFFFu);
        }
    }

    /// <summary>Put the world's clear on both draw environments; returns what was there.</summary>
    static (uint, uint) WorldClear(IMemory m)
    {
        uint e0 = DrawEnvs + EnvClear, e1 = e0 + DrawEnvStride;
        var was = (m.ReadU32(e0), m.ReadU32(e1));
        m.WriteU32(e0, _worldClear[0]);
        m.WriteU32(e1, _worldClear[1]);
        return was;
    }

    static void RestoreClear(IMemory m, (uint, uint) was)
    {
        m.WriteU32(DrawEnvs + EnvClear, was.Item1);
        m.WriteU32(DrawEnvs + DrawEnvStride + EnvClear, was.Item2);
    }

    // ---- messages ----------------------------------------------------------------

    /// <summary>
    /// Where a message's pass can go. <c>func_800441D4</c> shrinks the buffers as a
    /// menu does and saves the texture space it is about to <c>MoveImage</c> the frame
    /// into after them, at <c>start + 0xE800</c>; that save is the only room. The pass
    /// borrows it: the saved texels go back into VRAM first (the walls sample them, and
    /// nothing drawn here samples the copy), the bytes are kept aside while the fade
    /// runs and copied back before it returns, for the game's own restore at close.
    /// </summary>
    static bool MessageLayout(IMemory m, out uint save, out uint end)
    {
        save = end = 0;
        if (!Shrunk(m, out uint start)) return false;
        save = m.ReadU32(MessageSave);
        uint bytes = m.ReadU16(MessageRect + 4u) * (uint)m.ReadU16(MessageRect + 6u) * 2u;
        end = save + bytes;
        // The world's two buffers are 0x1A000 each; the save must lie inside them.
        return save == start + 2u * MenuBytes && bytes >= ArenaOffset + MinArena &&
               end <= start + 0x34000u && end <= 0x80000000u + Runtime.RamSize;
    }

    /// <summary>
    /// <c>func_80043BB8(brightness, step)</c>: one step of the fade a frame until the
    /// brightness leaves 1..0x77, or until a button once all were up. Returns the
    /// button state (-1, or -2 once all were up) when the fade ran out and the
    /// brightness when a press cut it short, and on a press writes the examine bit of
    /// the pad word as the press had it -- <c>func_800441D4</c> reads exactly that.
    ///
    /// The game's step is the frame head, the two text quads, the frame copy in two
    /// halves dimmed to <c>0x80 - b/2</c>, <c>DrawSync</c> and stage 15's swap. Here the
    /// world pass runs into its own table ahead of the message's, in place of the
    /// copy. A fade-in that runs out also waits for the button here, still drawing,
    /// and returns the 0x50 the caller's own wait would have.
    /// </summary>
    public static void MessageFade(Action<CpuContext, IMemory> orig, CpuContext c, IMemory m)
    {
        if (!(Enabled && _hooked && _worldSeen && Stage15.CanDraw && m is PSMemory mem &&
              MessageLayout(m, out uint save, out uint end)))
        {
            if (Enabled && _hooked) _messagesRefused++;
            orig(c, m);
            return;
        }

        var saved = c.Snapshot();
        int b = (int)c.A0, step = (int)c.A1, state = -1, frames = 0;
        uint pad = c.FP;   // the game's step reads the pad into fp; the first test sees the caller's
        uint result;
        bool ranOut = false, waited = false;
        int fadeFrames = 0;
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        double fadeMs = 0;
        _messages++;

        // The saved texels back where the MoveImage put the frame, then the save's
        // bytes aside so the pass can use them.
        c.A0 = MessageRect;
        c.A1 = save;
        Game.func_80079DC8(c, mem);                 // LoadImage
        c.A0 = 0u;
        Game.func_80079BA0(c, mem);                 // DrawSync(0)
        var kept = new uint[(end - save) / 4u];
        for (int i = 0; i < kept.Length; i++) kept[i] = mem.ReadU32(save + (uint)i * 4u);

        try
        {
            while (true)
            {
                // Each extra draw ends in the swap's VSync(0): one more vblank at this brightness.
                for (int hold = 0; hold < FadeVBlanks; hold++) { Step(c, mem, b, save, end); frames++; }
                b += step;
                if ((uint)(b - 1) >= 0x77u)
                {
                    fadeFrames = frames;
                    fadeMs = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                    Examine(mem, pad, ranOut: true);
                    result = (uint)state;
                    ranOut = true;
                    break;
                }
                pad = Pad(c, mem);
                if (state == -1) { if (pad == 0) state = -2; continue; }
                if (pad == 0) continue;
                fadeFrames = frames;
                fadeMs = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                Examine(mem, pad, ranOut: false);
                result = (uint)b;
                break;
            }

            // Faded in: the caller waits for a button drawing nothing, which leaves the
            // wide target idle. Wait here instead, still drawing, as its wait would.
            if (step > 0 && ranOut)
            {
                int held = b - step;
                while (true)
                {
                    Step(c, mem, held, save, end);
                    frames++;
                    pad = Pad(c, mem);
                    if (state == -1) { if (pad == 0) state = -2; continue; }
                    if (pad == 0) continue;
                    Examine(mem, pad, ranOut: false);
                    break;
                }
                result = 0x50u;
                waited = true;
            }
        }
        finally
        {
            for (int i = 0; i < kept.Length; i++) mem.WriteU32(save + (uint)i * 4u, kept[i]);
        }

        if (_probe)
            Console.WriteLine($"[KF3] menu world: message fade {step:+0;-0} over the live world, {fadeFrames} step(s) in {fadeMs:0} ms, " +
                              $"{frames} frame(s) in all" +
                              (waited ? ", held for the button" : "") + $", returns {(int)result}");
        c.Restore(saved);
        c.V0 = result;
    }

    /// <summary>One frame of the fade: the frame head, the message's quads, the world
    /// pass ahead of the message's table, DrawSync, stage 15's swap.</summary>
    static void Step(CpuContext c, PSMemory mem, int b, uint region, uint regionEnd)
    {
        Game.func_80035630(c, mem);                 // the frame head: flip, tables, descriptor
        uint ot = mem.ReadU32(OtPointer);
        MessageQuads(mem, ot, b);

        uint head = DrawWorld(c, mem, region, regionEnd, ot + (OtEntries - 1u) * 4u);

        c.A0 = 0u;
        Game.func_80079BA0(c, mem);                 // DrawSync(0)
        // The swap draws from OtPointer + 0x7FFC and splices the front table in
        // there: point it at the pass's table, whose end leads into the message's.
        mem.WriteU32(OtPointer, head - (OtEntries - 1u) * 4u);
        var clear = WorldClear(mem);
        Game.func_80035700(c, mem);                 // stage 15's swap
        RestoreClear(mem, clear);
        mem.WriteU32(OtPointer, ot);
        if (_probe) Report();
    }

    static uint Pad(CpuContext c, PSMemory mem)
    {
        c.A0 = 1u;
        Game.PadRead_game(c, mem);
        return c.V0;
    }

    /// <summary>A press of the examine button sets its bit in the pad word and any
    /// other press clears it; a fade that ran out does the same with the last read,
    /// leaving the word alone when nothing was held.</summary>
    static void Examine(PSMemory mem, uint pad, bool ranOut)
    {
        ushort mask = mem.ReadU16(ExamineMask), word = mem.ReadU16(PadWord);
        if ((pad & mask) != 0u) mem.WriteU16(PadWord, (ushort)(word | mask));
        else if (!ranOut || pad != 0u) mem.WriteU16(PadWord, (ushort)(word & ~mask));
    }

    /// <summary>
    /// What is left of the game's quads without the copy: the message picture
    /// subtractive one pixel down and right, then additive, at the brightness, into
    /// entry 0 as the game adds them; under them a black quad at 50% once the fade is
    /// half up, in entry 1. The console has no multiply to match the copy's
    /// <c>0x80 - b/2</c> exactly, so 50% stands in for the 58% it reaches.
    /// </summary>
    static void MessageQuads(PSMemory mem, uint ot, int b)
    {
        uint desc = mem.ReadU32(ActiveDescriptor);
        uint cur = mem.ReadU32(desc + 8u), end = mem.ReadU32(desc + 4u);
        if (cur + 0x28u * 2u + 0x18u + 0x0Cu > end) return;

        uint clut = (0x1E0u << 6) | (0x60u >> 4);
        Ft4(mem, cur, (uint)b, clut, TPage(0, 1, 0x3C0, 0x100), 0x20, 0x20); Link(mem, ot, cur); cur += 0x28u;
        Ft4(mem, cur, (uint)b, clut, TPage(0, 2, 0x3C0, 0x100), 0x21, 0x21); Link(mem, ot, cur); cur += 0x28u;
        mem.WriteU32(FrameCounters[2], mem.ReadU32(FrameCounters[2]) + 2u);

        if (b >= 0x36)
        {
            // POLY_F4, semi-transparent, black: B/2 + 0 under mode 0, which the
            // texpage set after it (so walked before it) selects. It spans the margin.
            mem.WriteU32(cur, 0x05000000u);
            mem.WriteU32(cur + 4u, 0x2A000000u);
            mem.WriteU32(cur + 8u, Xy(-512, 0));
            mem.WriteU32(cur + 12u, Xy(832, 0));
            mem.WriteU32(cur + 16u, Xy(-512, 240));
            mem.WriteU32(cur + 20u, Xy(832, 240));
            Link(mem, ot + 4u, cur); cur += 0x18u;

            mem.WriteU32(cur, 0x02000000u);
            mem.WriteU32(cur + 4u, 0xE1000000u | 0x200u);   // texpage 0, mode 0, dither on
            mem.WriteU32(cur + 8u, 0xE2000000u);             // no texture window
            Link(mem, ot + 4u, cur); cur += 0x0Cu;
        }

        mem.WriteU32(desc + 8u, cur);
    }

    static uint TPage(uint tp, uint abr, uint x, uint y) =>
        ((tp & 3u) << 7) | ((abr & 3u) << 5) | ((y & 0x100u) >> 4) | ((x & 0x3FFu) >> 6) | ((y & 0x200u) << 2);

    static uint Xy(int x, int y) => ((uint)(ushort)(short)y << 16) | (ushort)(short)x;

    /// <summary>The message picture as <c>func_80043BB8</c> builds it: 256x208 texels
    /// from (0, 0) of the page, at (<paramref name="x"/>, <paramref name="y"/>).</summary>
    static void Ft4(PSMemory mem, uint p, uint rgb, uint clut, uint tpage, int x, int y)
    {
        mem.WriteU32(p, 0x09000000u);
        mem.WriteU32(p + 4u, 0x2E000000u | (rgb << 16) | (rgb << 8) | rgb);   // textured, semi-transparent
        mem.WriteU32(p + 8u, Xy(x, y));
        mem.WriteU32(p + 12u, clut << 16);
        mem.WriteU32(p + 16u, Xy(x + 0x100, y));
        mem.WriteU32(p + 20u, (tpage << 16) | 0x00FFu);
        mem.WriteU32(p + 24u, Xy(x, y + 0xD0));
        mem.WriteU32(p + 28u, 0xD000u);
        mem.WriteU32(p + 32u, Xy(x + 0x100, y + 0xD0));
        mem.WriteU32(p + 36u, 0xD0FFu);
    }

    /// <summary>AddPrim.</summary>
    static void Link(PSMemory mem, uint entry, uint prim)
    {
        uint e = mem.ReadU32(entry);
        mem.WriteU32(prim, (mem.ReadU32(prim) & 0xFF000000u) | (e & 0x00FFFFFFu));
        mem.WriteU32(entry, (e & 0xFF000000u) | (prim & 0x00FFFFFFu));
    }

    // ---- KF3_MENUWORLD_TEST ------------------------------------------------------

    /// <summary>Open the next test message from the player's tick once its time
    /// comes: the command channel cannot reach a sign.</summary>
    public static void Test(CpuContext c, IMemory m)
    {
        if (_tests.Count == 0 || !_testClock.IsRunning || _testClock.Elapsed.TotalSeconds < _testAt) return;
        if (m is not PSMemory mem) return;
        var (file, entry) = _tests.Dequeue();
        Console.WriteLine($"[KF3] menu world: test message ({file}, {entry})");
        Console.Out.Flush();
        var saved = c.Snapshot();
        c.A0 = file;
        c.A1 = entry;
        Game.func_800441D4(c, mem);
        c.Restore(saved);
        Console.WriteLine($"[KF3] menu world: test message ({file}, {entry}) closed");
        Console.Out.Flush();
        _testAt = _testClock.Elapsed.TotalSeconds + 5.0;
    }

    // ---- KF3_MENUWORLD_PROBE=2: a pass against the last frame ------------------

    static List<uint[]> _lastFrame = new();
    static bool _inStage15Frame;

    /// <summary>The swap's pre inside a main-loop stage 15: keep that frame's packets.</summary>
    public static void BeforeSwap(CpuContext c, IMemory m)
    {
        if (_drawing || !_inStage15Frame) return;
        uint ot = m.ReadU32(OtPointer);
        // The swap splices the front table in after this walk's entry 0x1FFE; walk both.
        _lastFrame = Walk(m, ot + (OtEntries - 1u) * 4u, m.ReadU32(FrontPointer));
    }

    static List<uint[]> Walk(IMemory m, uint head, uint front)
    {
        var list = new List<uint[]>();
        void Run(uint at, uint stop)
        {
            for (int guard = 0; guard < 200000; guard++)
            {
                uint tag = m.ReadU32(at);
                uint n = tag >> 24;
                if (n > 0)
                {
                    var w = new uint[n];
                    for (uint i = 0; i < n; i++) w[i] = m.ReadU32(at + 4u + i * 4u);
                    MaskPadding(w);
                    list.Add(w);
                }
                uint link = tag & 0x00FFFFFFu;
                if (link == 0x00FFFFFFu || (0x80000000u | link) == stop) return;
                at = 0x80000000u | link;
            }
        }
        Run(front + (FrontEntries - 1u) * 4u, 0);
        Run(head, 0);
        return list;
    }

    /// <summary>A textured polygon's third and fourth texture words carry no clut or
    /// texpage: their high halves are padding, whatever the buffer held.</summary>
    static void MaskPadding(uint[] w)
    {
        uint cmd = w[0] >> 24;
        if ((cmd & 0xE0u) != 0x20u || (cmd & 0x04u) == 0) return;
        int verts = (cmd & 0x08u) != 0 ? 4 : 3;
        bool shaded = (cmd & 0x10u) != 0;
        int at = 1;
        for (int v = 0; v < verts; v++)
        {
            if (v > 0 && shaded) at++;
            at++;                                   // xy
            if (v >= 2 && at < w.Length) w[at] &= 0xFFFFu;
            at++;                                   // uv
        }
    }

    static int _compared;

    static void Compare(IMemory m, uint head, uint front)
    {
        if (_compared >= 3) return;
        _compared++;
        var pass = Walk(m, head, front);
        var a = _lastFrame;
        int same = 0, diff = 0, shown = 0;
        var byKey = new Dictionary<string, int>();
        foreach (var w in a) { string k = string.Join(",", w); byKey[k] = byKey.GetValueOrDefault(k) + 1; }
        foreach (var w in pass)
        {
            string k = string.Join(",", w);
            if (byKey.TryGetValue(k, out int n) && n > 0) { byKey[k] = n - 1; same++; continue; }
            diff++;
            if (shown++ < 12) Console.WriteLine($"[KF3] menu world diff: pass only {string.Join(" ", w.Select(x => x.ToString("X8")))}");
        }
        int missing = byKey.Values.Sum();
        shown = 0;
        foreach (var (k, n) in byKey)
            if (n > 0 && shown++ < 12) Console.WriteLine($"[KF3] menu world diff: frame only x{n} {string.Join(" ", k.Split(',').Select(x => uint.Parse(x).ToString("X8")))}");
        Console.WriteLine($"[KF3] menu world diff: frame {a.Count} packets, pass {pass.Count}; {same} identical, {diff} only in the pass, {missing} only in the frame");
        Console.Out.Flush();
    }

    static void Report()
    {
        double now = _clock.Elapsed.TotalMilliseconds;
        double elapsed = now - _windowMs;
        if (elapsed < 1000.0) return;
        Console.WriteLine($"[KF3] menu world: {_passes * 1000.0 / elapsed:0.#} passes/s, " +
                          $"peak {_peak}/{_arena} bytes, {_overflows} overflow(s), " +
                          $"{_refused} session(s) refused, {_messages} message fade(s) live, {_messagesRefused} left to the game");
        Console.Out.Flush();
        _windowMs = now;
        _passes = _overflows = 0;
        _peak = 0;
    }
}
