using System.Reflection;
using System.Security.Cryptography;
using RecompOne.Runtime.Context;
using RecompOne.Runtime.Memory;
using RecompOne.Runtime.Modding;
using Open = Recompiled.KingsField3_open;

namespace Kf3;

/// <summary>
/// The Japanese title screen, for KF3_LANGUAGE=ja: the images and sounds of the Japanese
/// OP/OP.D, drawn the way the Japanese OPEN.EXE draws them. The US file cannot simply be
/// replaced on the disc: the Japanese one is longer than the US file's sectors and the
/// US buffer, its sound bank is elsewhere, and its logo and text sit in other places. So
/// the US title's loader, its sound setup and its three drawing routines are replaced
/// here, and the rest of OPEN.EXE (the flow, the pad, the card check) stays US.
///
/// The routines are the Japanese program's, in the same shape as the US ones, with the
/// positions and brightness limits of 0x800131AC, 0x80013704 and 0x800138A4 there.
/// See "The title screen" in docs/JAPANESE.md. Everything here runs on the game thread.
/// </summary>
public static class JapaneseTitle
{
    const uint LoaderRoutine = 0x80012494;
    const uint SetupRoutine = 0x800126AC;
    const uint BackgroundRoutine = 0x8001279C;
    const uint CopyrightRoutine = 0x80012B0C;
    const uint MenuRoutine = 0x80012EA0;

    /// <summary>SHA-256 of the OP.D measured; the same on both revisions of the Japanese disc.</summary>
    const string TitleFileSha256 = "746ec85dc78b1a28bbca0664da0981997c64012c180e13333b4cf871243fc351";

    const string TitleFile = "OP/OP.D";
    const uint BufferBytes = 0x3C000;      // the Japanese loader's; the US one takes 0x32000
    const uint SoundBankAt = 0x2A350;      // the sound bank's header in the file
    const uint SoundBodyAt = 0xC20;        // its samples, from the header

    const int GpVabId = 148, GpSoundHeader = 152, GpBuffer = 160, GpPrimitive = 168;
    const uint NoVab = 0xFFFFFFFF;

    const int InitialiseMode = 1, BackgroundResetMode = 2;
    const int MenuResetMode = 3, MenuHoldMode = 4;
    const byte BackgroundBrightness = 0x40, MenuUnselectedBrightness = 0x30;
    const int PulseStep = 4;

    /// <summary>A brightness that rises to Top and falls to Floor, kept in two gp words.</summary>
    readonly record struct Pulse(int ValueGp, int FallingGp, int Start, int Top, int Floor, bool RisesAgainAtFloor);

    static readonly Pulse LogoPulse = new(4, 8, 0x58, 144, 89, false);
    static readonly Pulse CopyrightPulse = new(12, 16, 0x48, 128, 73, false);
    static readonly Pulse MenuPulse = new(20, 24, 0x30, 128, 49, true);

    /// <summary>One textured quad: its texture page, palette, screen rectangle and texture rectangle.</summary>
    readonly record struct Quad(int ColourMode, bool Translucent, int PageX, int PageY, ushort Clut,
        short X0, short Y0, short X1, short Y1, byte U0, byte V0, byte U1, byte V1, int Depth);

    const int DepthBackground = 200, DepthFront = 100;

    static readonly Quad Background = new(1, false, 0x280, 0, 0x7800, 0, 0, 640, 240, 0, 0, 255, 240, DepthBackground);

    static readonly Quad[] Logo =
    [
        new(1, true, 0x280, 0x100, 0x7840, 0, 60, 256, 180, 0, 0, 255, 120, DepthFront),
        new(1, true, 0x300, 0x100, 0x7840, 256, 60, 320, 180, 0, 0, 64, 120, DepthFront),
        new(1, true, 0x280, 0x100, 0x7880, 320, 60, 576, 180, 0, 128, 255, 248, DepthFront),
        new(1, true, 0x300, 0x100, 0x7880, 576, 60, 640, 180, 0, 128, 64, 248, DepthFront),
    ];

    static readonly Quad Copyright = new(0, true, 0x380, 0, 0x7D40, 192, 170, 447, 182, 0, 0, 255, 12, DepthFront);
    static readonly Quad NewGame = new(0, true, 0x380, 0, 0x7D80, 272, 192, 368, 200, 0, 16, 96, 24, DepthFront);
    static readonly Quad Continue = new(0, true, 0x380, 0, 0x7D80, 272, 204, 368, 212, 96, 16, 192, 24, DepthFront);

    static readonly ModInfo _self = new()
    {
        Id = "kf3.japanesetitle",
        Name = "Japanese title screen",
        Version = "1.0",
        Description = "Draws the Japanese title screen when the Japanese layer is on (KF3_LANGUAGE=ja).",
    };

    const int ReportEveryDraws = 60;
    const int LogoPageX = 640, LogoPageY = 256, LogoPageWords = 160, LogoPageRows = 120, VramWidth = 1024;

    static bool _probe;
    static int _draws;
    static bool _queued, _fileChecked;
    static byte[]? _titleFile;
    static bool _japanese;                 // this run of OPEN.EXE loaded the Japanese file

    public static void Install()
    {
        _probe = Verdite.Core.Game.Env("JP_PROBE") == "1";
        HookAttach.OnOverlayLoad("japanese title", Attach, "The title screen stays US.");
    }

    static bool Attach()
    {
        SymbolRegistry.Build();
        var hooks = new (uint Address, string Impl)[]
        {
            (LoaderRoutine, nameof(LoadTitleFile)),
            (SetupRoutine, nameof(SetUpTitleFile)),
            (BackgroundRoutine, nameof(DrawBackgroundAndLogo)),
            (CopyrightRoutine, nameof(DrawCopyright)),
            (MenuRoutine, nameof(DrawMenu)),
        };
        var targets = hooks.Select(hook => SymbolRegistry.Resolve("open", null, hook.Address)).ToArray();
        if (targets.Any(target => target == null)) return false;

        if (!_queued)
        {
            _queued = true;
            for (int i = 0; i < hooks.Length; i++)
                HookManager.AddReplace(_self, targets[i]!, typeof(JapaneseTitle).GetMethod(hooks[i].Impl, BindingFlags.Public | BindingFlags.Static)!);
        }
        HookManager.Commit();
        return targets.All(target => HookAttach.Installed(target));
    }

    /// <summary>func_80012494: the Japanese file straight into a buffer of its size, instead of US sectors.</summary>
    public static void LoadTitleFile(Action<CpuContext, IMemory> orig, CpuContext c, IMemory m)
    {
        _japanese = false;
        if (MeasuredTitleFile() is not { } file) { orig(c, m); return; }

        c.A0 = BufferBytes;
        Open.malloc_open(c, m);
        uint buffer = c.V0;
        if (buffer == 0)
        {
            Console.WriteLine("[KF3] japanese title: no room for the buffer; the title stays US");
            orig(c, m);
            return;
        }

        for (int i = 0; i < file.Length; i++) m.WriteU8(buffer + (uint)i, file[i]);
        m.WriteU32(c.GP + GpBuffer, buffer);
        _japanese = true;
        Console.WriteLine($"[KF3] japanese title: {file.Length} bytes loaded");
    }

    /// <summary>The file when a Japanese disc is mixed in and it is the one measured; checked once.</summary>
    static byte[]? MeasuredTitleFile()
    {
        if (_fileChecked) return _titleFile;
        if (!JapaneseDisc.Active) return null;
        _fileChecked = true;

        var file = JapaneseDisc.ReadJapaneseFile(TitleFile);
        if (file is null) return null;
        if (Convert.ToHexString(SHA256.HashData(file)).ToLowerInvariant() != TitleFileSha256)
        {
            Console.WriteLine("[KF3] japanese title: OP.D is not the one measured; the title stays US");
            return null;
        }
        return _titleFile = file;
    }

    /// <summary>func_800126AC with the Japanese file's sound bank: the images, then the bank.</summary>
    public static void SetUpTitleFile(Action<CpuContext, IMemory> orig, CpuContext c, IMemory m)
    {
        if (!_japanese) { orig(c, m); return; }

        uint buffer = m.ReadU32(c.GP + GpBuffer);
        c.A0 = buffer;
        Open.func_800135BC(c, m);

        uint header = buffer + SoundBankAt;
        m.WriteU32(c.GP + GpSoundHeader, header);
        c.A0 = header;
        c.A1 = NoVab;
        Open.SsVabOpenHead_open(c, m);
        short vab = (short)c.V0;
        m.WriteU16(c.GP + GpVabId, (ushort)vab);
        if (vab != -1) TransferSamples(c, m, header + SoundBodyAt, vab);

        c.A0 = 0;
        Open.func_80016168_open(c, m);
    }

    static void TransferSamples(CpuContext c, IMemory m, uint body, short vab)
    {
        c.A0 = body;
        Open.func_80025FB4(c, m);
        if ((short)c.V0 != vab) return;
        c.A0 = 1;
        Open.SsVabTransCompleted_open(c, m);
    }

    /// <summary>func_8001279C: the background, then the logo in four pieces. V0 is whether the logo's fade ended.</summary>
    public static void DrawBackgroundAndLogo(Action<CpuContext, IMemory> orig, CpuContext c, IMemory m)
    {
        if (!_japanese) { orig(c, m); return; }
        int mode = (int)c.A0;
        if (mode == BackgroundResetMode) { Reset(c, m, LogoPulse); return; }
        if (mode == InitialiseMode) Initialise(c, m, LogoPulse);

        Draw(c, m, Background, BackgroundBrightness);
        bool ended = Step(c, m, LogoPulse);
        byte brightness = Brightness(c, m, LogoPulse);
        foreach (var piece in Logo) Draw(c, m, piece, brightness);
        c.V0 = ended ? 1u : 0u;
        ReportDraw(c, m, brightness);
    }

    /// <summary>Under KF3_JP_PROBE: the logo's brightness, the sound bank and the logo's page in VRAM.</summary>
    static void ReportDraw(CpuContext c, IMemory m, byte brightness)
    {
        if (!_probe || _draws++ % ReportEveryDraws != 0) return;
        int filled = 0;
        if (RecompOne.Runtime.Runtime.Gpu is { } gpu)
            for (int row = 0; row < LogoPageRows; row++)
                for (int word = 0; word < LogoPageWords; word++)
                    if (gpu.Vram[(LogoPageY + row) * VramWidth + LogoPageX + word] != 0) filled++;
        Console.WriteLine($"[KF3] japanese title: draw {_draws}, logo brightness {brightness}, sound bank {(short)m.ReadU16(c.GP + GpVabId)}, logo page {filled} of {LogoPageRows * LogoPageWords} words filled");
    }

    /// <summary>func_80012B0C: the copyright line. V0 is whether its fade ended.</summary>
    public static void DrawCopyright(Action<CpuContext, IMemory> orig, CpuContext c, IMemory m)
    {
        if (!_japanese) { orig(c, m); return; }
        int mode = (int)c.A0;
        if (mode == BackgroundResetMode) { Reset(c, m, CopyrightPulse); return; }
        if (mode == InitialiseMode) Initialise(c, m, CopyrightPulse);

        bool ended = Step(c, m, CopyrightPulse);
        Draw(c, m, Copyright, Brightness(c, m, CopyrightPulse));
        c.V0 = ended ? 1u : 0u;
    }

    /// <summary>func_80012EA0(selected): NEW and CONTINUE, the selected one pulsing. V0 is whether the pulse ended.</summary>
    public static void DrawMenu(Action<CpuContext, IMemory> orig, CpuContext c, IMemory m)
    {
        if (!_japanese) { orig(c, m); return; }
        int selected = (int)c.A0;
        if (selected == MenuResetMode) { Reset(c, m, MenuPulse); return; }

        bool ended = false;
        if (selected == MenuHoldMode) m.WriteU32(c.GP + (uint)MenuPulse.ValueGp, (uint)MenuPulse.Start);
        else ended = Step(c, m, MenuPulse);

        byte pulse = Brightness(c, m, MenuPulse);
        Draw(c, m, NewGame, selected == 1 ? MenuUnselectedBrightness : pulse);
        Draw(c, m, Continue, selected == 0 ? MenuUnselectedBrightness : pulse);
        c.V0 = ended ? 1u : 0u;
    }

    static void Reset(CpuContext c, IMemory m, Pulse pulse)
    {
        m.WriteU32(c.GP + (uint)pulse.ValueGp, 0);
        m.WriteU32(c.GP + (uint)pulse.FallingGp, 0);
        c.V0 = 0;
    }

    static void Initialise(CpuContext c, IMemory m, Pulse pulse)
    {
        m.WriteU32(c.GP + (uint)pulse.FallingGp, 1);
        m.WriteU32(c.GP + (uint)pulse.ValueGp, (uint)pulse.Start);
    }

    /// <summary>One step of the brightness. True when it has fallen below its floor.</summary>
    static bool Step(CpuContext c, IMemory m, Pulse pulse)
    {
        uint valueAt = c.GP + (uint)pulse.ValueGp, fallingAt = c.GP + (uint)pulse.FallingGp;
        int value = (int)m.ReadU32(valueAt);
        if (m.ReadU32(fallingAt) == 0)
        {
            value += PulseStep;
            m.WriteU32(valueAt, (uint)value);
            if (value >= pulse.Top) m.WriteU32(fallingAt, 1);
            return false;
        }

        if (value < pulse.Floor)
        {
            if (pulse.RisesAgainAtFloor) m.WriteU32(fallingAt, 0);
            return true;
        }
        m.WriteU32(valueAt, (uint)(value - PulseStep));
        return false;
    }

    static byte Brightness(CpuContext c, IMemory m, Pulse pulse) => m.ReadU8(c.GP + (uint)pulse.ValueGp);

    /// <summary>One textured quad through the title's own primitive setup and submit.</summary>
    static void Draw(CpuContext c, IMemory m, Quad quad, byte brightness)
    {
        Open.func_800132F4(c, m);

        c.A0 = (uint)quad.ColourMode;
        c.A1 = quad.Translucent ? 1u : 0u;
        c.A2 = (uint)quad.PageX;
        c.A3 = (uint)quad.PageY;
        Open.GetTPage_open(c, m);
        ushort page = (ushort)c.V0;

        uint prim = m.ReadU32(c.GP + GpPrimitive);
        m.WriteU16(prim + 22, page);
        m.WriteU16(prim + 14, quad.Clut);
        if (quad.Translucent)
        {
            c.A0 = prim;
            c.A1 = 1;
            Open.SetSemiTrans_open(c, m);
        }

        for (uint channel = 4; channel <= 6; channel++) m.WriteU8(prim + channel, brightness);
        WriteCorner(m, prim + 8, quad.X0, quad.Y0, quad.U0, quad.V0);
        WriteCorner(m, prim + 16, quad.X1, quad.Y0, quad.U1, quad.V0);
        WriteCorner(m, prim + 24, quad.X0, quad.Y1, quad.U0, quad.V1);
        WriteCorner(m, prim + 32, quad.X1, quad.Y1, quad.U1, quad.V1);

        c.A0 = (uint)quad.Depth;
        Open.func_8001333C(c, m);
    }

    /// <summary>A corner of the primitive: X and Y, then U and V after them.</summary>
    static void WriteCorner(IMemory m, uint at, short x, short y, byte u, byte v)
    {
        m.WriteU16(at, unchecked((ushort)x));
        m.WriteU16(at + 2, unchecked((ushort)y));
        m.WriteU8(at + 4, u);
        m.WriteU8(at + 5, v);
    }
}
