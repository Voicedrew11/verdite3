using System.Text;
using RecompOne.Runtime.Cdrom;

namespace Kf3;

/// <summary>
/// The Japanese King's Field III's text, pictures and videos over the US program,
/// for KF3_LANGUAGE=ja. The US program and all the gameplay data stay; the Japanese
/// disc (the player's own, SLPS-00377) supplies the parts that are words and pictures.
///
///     KF3_LANGUAGE=ja        the layer on; en does nothing at all
///     KF3_LANGUAGE unset     the stored kf3.language decides (Settings ▸ Gameplay ▸ Language)
///     KF3_JP_DISC=path       the Japanese .cue or .chd; else the US disc's folder and
///                            its "Japanese discs" subfolder are searched for one
///     KF3_JP_PROBE=1         log each file's plan, and a served-sector count every 5 s
///                            when it changes
///     KF3_JP_DRYRUN=1        build the plan, log it, and exit 0 before the game starts
///                            (1 when it fails); a self-check, not for play
///
/// Installed before the game starts, it sets <see cref="DiscImage.Decorate"/>. The
/// stored language is not read here: the settings store is loaded by Runtime.Initialize,
/// which Entry.Run calls, so it is not loaded yet when Program.cs installs. It is read
/// at RuntimeReadyEvent, and an earlier US open (the card icon's) passes through. Each
/// US image the runtime opens (the game's own, and the card icon's re-open) is
/// wrapped in <see cref="MixedDiscImage"/>, the plan built once, at the first, from
/// the Japanese disc and the US sectors. The Japanese disc is opened by its format
/// directly, so opening it does not come back through the hook. A disc whose volume
/// id is not SLUS-00255 is passed through unchanged. If no Japanese disc is found, or
/// the mix fails, one [KF3] line says so and the game plays in English.
/// </summary>
public static class JapaneseDisc
{
    const string UsVolumeId = "SLUS-00255";
    const string JapaneseVolumeId = "SLPS-00377";
    const string JapaneseFolder = "Japanese discs";

    /// <summary>The ISO 9660 primary volume descriptor, and where its volume id lies in it.</summary>
    const int PrimaryVolumeSector = 16;
    const int VolumeIdAt = 40;
    const int VolumeIdBytes = 32;
    const int SectorBytes = 2048;

    /// <summary>The settings key the page and the Settings window keep: "ja" or "en".</summary>
    public const string LanguageKey = "kf3.language";

    static readonly object Gate = new();
    static string? _jpDiscPath;
    static bool _dryRun;
    static bool _probe;
    static bool _planTried;
    static Plan? _plan;
    static bool? _japanese;
    static double? _choice;
    static volatile bool _storeReady;
    static volatile bool _active;
    static volatile DiscFs? _usFiles, _jpFiles;

    /// <summary>True once the layer is actually over the US disc.</summary>
    public static bool Active => _active;

    /// <summary>A whole file from the Japanese disc (its root is "GAME.EXE", "CD/COM/FDAT.T"),
    /// or null when no Japanese disc is mixed in or the file is not there.</summary>
    internal static byte[]? ReadJapaneseFile(string path) => ReadWhole(_jpFiles, path);

    /// <summary>A whole file from the US disc, under the same rules as <see cref="ReadJapaneseFile"/>.</summary>
    internal static byte[]? ReadUsFile(string path) => ReadWhole(_usFiles, path);

    static byte[]? ReadWhole(DiscFs? disc, string path)
    {
        if (disc is null) return null;
        try
        {
            return disc.ReadFile(path);
        }
        catch (Exception e) when (e is IOException or InvalidDataException)
        {
            Console.Error.WriteLine($"[KF3] japanese: cannot read {path}: {e.Message}");
            return null;
        }
    }

    /// <summary>The language the settings page shows and keeps: 1 Japanese, 0 English.
    /// It starts as this run started, and a change is kept for the next start; it
    /// does not change the layer in this run.</summary>
    public static double Choice
    {
        get
        {
            lock (Gate)
            {
                _choice ??= StartLanguage() == "ja" ? 1 : 0;
                return _choice.Value;
            }
        }
        set { lock (Gate) _choice = value; }
    }

    /// <summary>Reads KF3_LANGUAGE, and sets the disc hook unless it is en: with no
    /// variable the stored language decides, from RuntimeReadyEvent on (Japanese).</summary>
    public static void Install()
    {
        var language = Verdite.Core.Game.Env("LANGUAGE")?.Trim().ToLowerInvariant();
        if (language == "en") return;
        if (!string.IsNullOrEmpty(language) && language != "ja")
        {
            Console.Error.WriteLine($"[KF3] japanese: KF3_LANGUAGE={language} is not ja or en; the game is in English");
            return;
        }

        _jpDiscPath = Verdite.Core.Game.Env("JP_DISC");
        _dryRun = Verdite.Core.Game.Env("JP_DRYRUN") == "1";
        _probe = Verdite.Core.Game.Env("JP_PROBE") == "1";
        if (language == "ja") _japanese = true;
        RecompOne.Runtime.Events.Event.AddListener<RecompOne.Runtime.Events.RuntimeReadyEvent>(_ => _storeReady = true);
        DiscImage.Decorate = Wrap;
    }

    /// <summary>KF3_LANGUAGE when set, else the stored kf3.language, else en. Only
    /// valid once the settings store is loaded (after RuntimeReadyEvent).</summary>
    static string StartLanguage()
    {
        var env = Verdite.Core.Game.Env("LANGUAGE")?.Trim().ToLowerInvariant();
        if (!string.IsNullOrEmpty(env)) return env;
        return RecompOne.Runtime.Config.ConfigManager.View.Values.TryGetValue(LanguageKey, out var stored)
            ? stored.Trim().ToLowerInvariant()
            : "en";
    }

    /// <summary>
    /// Whether this run is Japanese. KF3_LANGUAGE decides at once. The stored value
    /// waits for RuntimeReadyEvent: Verdite Core's card icon opens the US disc from
    /// Program.cs, before the settings store is loaded, and that open passes through
    /// unmixed without deciding anything.
    /// </summary>
    static bool Japanese()
    {
        lock (Gate)
        {
            if (_japanese is null && _storeReady) _japanese = StartLanguage() == "ja";
            return _japanese ?? false;
        }
    }

    static IDiscImage Wrap(IDiscImage image, string path)
    {
        if (!Japanese()) return image;
        if (VolumeId(image) != UsVolumeId) return image;

        Plan? plan;
        lock (Gate)
        {
            if (!_planTried)
            {
                _planTried = true;
                _plan = BuildPlan(image, path);
            }

            plan = _plan;
        }

        if (plan is null) return image;
        _active = true;
        return new MixedDiscImage(image, plan.Jp, plan.Overrides, plan.Streams, plan.Served);
    }

    static Plan? BuildPlan(IDiscImage us, string usPath)
    {
        var found = FindJapaneseDisc(usPath);
        if (found is null)
        {
            Console.Error.WriteLine($"[KF3] japanese: no Japanese disc ({JapaneseVolumeId}) found beside {usPath}; the game is in English. Set KF3_JP_DISC to its .cue or .chd.");
            return FailedDryRun(1);
        }

        try
        {
            return MixPlan(us, found);
        }
        catch (Exception e) when (e is InvalidDataException or IOException or NotSupportedException)
        {
            found.Image.Dispose();
            Console.Error.WriteLine($"[KF3] japanese: {e.Message}; the game is in English");
            return FailedDryRun(1);
        }
    }

    static Plan MixPlan(IDiscImage us, Found found)
    {
        var usFs = DiscFs.FromImage(us);
        var jpFs = DiscFs.FromImage(found.Image);
        _usFiles = usFs;
        _jpFiles = jpFs;
        if (_probe || _dryRun)
            foreach (var line in JapaneseMix.Describe(usFs, jpFs))
                Console.WriteLine($"[KF3] japanese: {line}");

        var overrides = JapaneseMix.BuildSectorOverrides(usFs, jpFs);
        var streams = JapaneseMix.BuildStreamRanges(usFs, jpFs);
        Console.WriteLine($"[KF3] japanese: {JapaneseVolumeId} from {Path.GetFileName(found.Path)} over {UsVolumeId}: {overrides.Count} sector(s) of text and data replaced, {streams.Count} video(s) served from it");
        if (_dryRun)
        {
            Console.WriteLine("[KF3] japanese: KF3_JP_DRYRUN=1, exiting before the game starts");
            Environment.Exit(0);
        }

        var served = _probe ? new MixedDiscImage.ServedCount() : null;
        return new Plan(found.Image, overrides, streams, served);
    }

    /// <summary>In a dry run a failure exits 1 rather than letting the game start in English; otherwise it is null.</summary>
    static Plan? FailedDryRun(int code)
    {
        if (_dryRun) Environment.Exit(code);
        return null;
    }

    static Found? FindJapaneseDisc(string usPath)
    {
        foreach (var candidate in Candidates(usPath))
            if (OpenIfJapanese(candidate) is { } found)
                return found;
        return null;
    }

    /// <summary>KF3_JP_DISC alone when it is set; otherwise every disc file in the US disc's folder and in its Japanese subfolder.</summary>
    static IEnumerable<string> Candidates(string usPath)
    {
        if (!string.IsNullOrWhiteSpace(_jpDiscPath))
        {
            yield return _jpDiscPath;
            yield break;
        }

        var folder = Path.GetDirectoryName(Path.GetFullPath(usPath)) ?? ".";
        foreach (var dir in new[] { folder, Path.Combine(folder, JapaneseFolder) })
        {
            if (!Directory.Exists(dir)) continue;
            foreach (var file in Directory.GetFiles(dir).Where(IsDiscFile).Order(StringComparer.Ordinal))
                yield return file;
        }
    }

    static bool IsDiscFile(string path)
    {
        return path.EndsWith(".cue", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".chd", StringComparison.OrdinalIgnoreCase);
    }

    static Found? OpenIfJapanese(string path)
    {
        IDiscImage image;
        try
        {
            image = OpenByFormat(path);
        }
        catch (Exception e) when (e is IOException or NotSupportedException or InvalidDataException)
        {
            Console.Error.WriteLine($"[KF3] japanese: cannot open {path}: {e.Message}");
            return null;
        }

        if (VolumeId(image) == JapaneseVolumeId)
            return new Found(image, path);
        image.Dispose();
        return null;
    }

    /// <summary>Opens a disc by its format, not through DiscImage.Open, so the hook does not see it.</summary>
    static IDiscImage OpenByFormat(string path)
    {
        IDiscImage image = DiscImage.Detect(path) switch
        {
            DiscFormat.Chd => ChdImage.Open(path),
            DiscFormat.CueBin => CueBinImage.Open(path),
            _ => throw new NotSupportedException($"not a disc image: {path}"),
        };
        return image;
    }

    /// <summary>The volume id in the primary volume descriptor, sector 16, without its padding.</summary>
    static string VolumeId(IDiscImage image)
    {
        var sector = image.ReadSectorData(PrimaryVolumeSector, SectorBytes);
        return Encoding.ASCII.GetString(sector, VolumeIdAt, VolumeIdBytes).TrimEnd();
    }

    /// <summary>A Japanese disc found and identified, still open.</summary>
    private sealed record Found(IDiscImage Image, string Path);

    /// <summary>What the hook serves: the Japanese disc, the replaced sectors and the video ranges.</summary>
    private sealed record Plan(IDiscImage Jp, IReadOnlyDictionary<int, byte[]> Overrides, IReadOnlyList<StreamRange> Streams, MixedDiscImage.ServedCount? Served);
}
