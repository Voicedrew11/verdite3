using System.Diagnostics;
using System.Numerics;
using System.Text;
using ImGuiNET;
using NativeFileDialogNET;
using RecompOne.Runtime;
using RecompOne.Runtime.Cdrom;
using RecompOne.Runtime.Config;
using RecompOne.Runtime.Host.Window;

namespace Verdite3.Launcher;

/// <summary>
/// The first-run language question, asked before the US disc is: English only, or
/// English and Japanese, and for the second, where the Japanese disc is. See "The
/// first-run language step" in docs/JAPANESE.md.
///
/// Asked once, and only when the launcher is about to ask for the disc, so a player
/// who already has a game is left alone; VERDITE3_SETUP=1 asks again.
///
/// The keys are JapaneseDisc's (patches/JapaneseDisc.cs), which is game code and not
/// compiled here. None starts "kf3.", because onboarding.json's "existing" prefix
/// would then take a new player for one who has played.
/// </summary>
static class LanguageStep
{
    internal const string LanguageKey = "verdite.language";
    internal const string JapaneseDiscKey = "verdite.language.jpdisc";
    const string AskedKey = "verdite.language.asked";

    /// <summary>How long to wait for the window to open the picker before asking anyway.</summary>
    static readonly TimeSpan PickerWait = TimeSpan.FromSeconds(10);

    /// <summary>Frames for a closed popup to leave the stack, so the next is not drawn inside it.</summary>
    const int FramesToClose = 2;

    /// <summary>On the launcher's main thread, after the window is made and before the disc is asked for.</summary>
    public static void Run()
    {
        bool discNeeded = !HasValidDisc();
        bool forced = Environment.GetEnvironmentVariable("VERDITE3_SETUP") == "1";
        if (!forced && (!discNeeded || ConfigManager.View.Has(AskedKey))) return;

        Localization.Merge(Strings);

        var picker = discNeeded ? HidePicker() : null;
        var popup = new LanguagePopup();
        PopupManager.Register(popup);
        popup.Open();
        while (popup.IsOpen) Runtime.Pump();

        if (picker is null) return;
        LetPopupsClose();
        picker.Open();
    }

    /// <summary>The window's own test for opening the picker, so the two agree.</summary>
    static bool HasValidDisc()
    {
        var path = Runtime.CdPath;
        return !string.IsNullOrWhiteSpace(path) && File.Exists(path) && Runtime.ValidateDisc(path) is null;
    }

    /// <summary>
    /// The window opens the disc picker as it loads, so it is closed here to be
    /// opened again after this step. Null if it never appeared.
    /// </summary>
    static DiscPickerPopup? HidePicker()
    {
        var clock = Stopwatch.StartNew();
        while (PopupManager.Get<DiscPickerPopup>() is not { IsOpen: true })
        {
            if (clock.Elapsed > PickerWait) return null;
            Runtime.Pump();
        }

        var picker = PopupManager.Get<DiscPickerPopup>()!;
        picker.Close();
        LetPopupsClose();
        return picker;
    }

    static void LetPopupsClose()
    {
        for (int i = 0; i < FramesToClose; i++) Runtime.Pump();
    }

    /// <summary>
    /// The three languages the runtime ships: a key missing one shows as the key.
    /// </summary>
    const string Strings = """
    {
      "strings": {
        "verdite3.language.title": { "en": "Language", "pt-BR": "Idioma", "es-419": "Idioma" },
        "verdite3.language.english": { "en": "English only", "pt-BR": "Somente inglês", "es-419": "Solo inglés" },
        "verdite3.language.both": { "en": "English + Japanese", "pt-BR": "Inglês + japonês", "es-419": "Inglés + japonés" },
        "verdite3.language.note": {
          "en": "Verdite3 optionally supports Japanese language by patching the US disc from the Japanese disc, you can change your language option any time in the settings.",
          "pt-BR": "O Verdite3 oferece suporte opcional ao idioma japonês, aplicando ao disco americano um patch a partir do disco japonês. Você pode mudar a opção de idioma a qualquer momento nas configurações.",
          "es-419": "Verdite3 admite de forma opcional el idioma japonés, parcheando el disco estadounidense a partir del disco japonés. Puedes cambiar la opción de idioma en cualquier momento en la configuración."
        },
        "verdite3.language.next": { "en": "Next", "pt-BR": "Avançar", "es-419": "Siguiente" },
        "verdite3.language.back": { "en": "Back", "pt-BR": "Voltar", "es-419": "Atrás" },
        "verdite3.jpdisc.title": { "en": "Choose your Japanese disc", "pt-BR": "Escolha seu disco japonês", "es-419": "Elige tu disco japonés" },
        "verdite3.jpdisc.description": {
          "en": "Choose the disc image (.cue or .chd) of the Japanese King's Field III (SLPS-00377). You will choose the US disc next.",
          "pt-BR": "Escolha a imagem do disco (.cue ou .chd) do King's Field III japonês (SLPS-00377). Em seguida, você escolherá o disco americano.",
          "es-419": "Elige la imagen del disco (.cue o .chd) del King's Field III japonés (SLPS-00377). A continuación elegirás el disco estadounidense."
        },
        "verdite3.jpdisc.wrong": {
          "en": "This is not the Japanese King's Field III (SLPS-00377).",
          "pt-BR": "Este não é o King's Field III japonês (SLPS-00377).",
          "es-419": "Este no es el King's Field III japonés (SLPS-00377)."
        },
        "verdite3.jpdisc.unreadable": {
          "en": "This file could not be read as a cue/bin pair or a CHD.",
          "pt-BR": "Este arquivo não pôde ser lido como um par cue/bin ou um CHD.",
          "es-419": "Este archivo no se pudo leer como un par cue/bin o un CHD."
        }
      }
    }
    """;
}

/// <summary>The step's two pages: the question, then the Japanese disc. Draws on the launcher's main thread.</summary>
sealed class LanguagePopup : Popup
{
    const string JapaneseVolumeId = "SLPS-00377";   // JapaneseDisc's test, so a disc accepted here is one the game takes
    const int PrimaryVolumeSector = 16;
    const int VolumeIdAt = 40;
    const int VolumeIdBytes = 32;
    const int SectorBytes = 2048;
    const float BrowseWidth = 90f;
    const float ButtonWidth = 120f;

    bool _japanese;
    bool _onDiscPage;
    string _path = "";
    string _error = "";

    protected override string TitleKey => _onDiscPage ? "verdite3.jpdisc.title" : "verdite3.language.title";
    protected override Vector2 Size => new(540f, 0f);
    protected override bool Closable => false;

    protected override void DrawContent()
    {
        if (_onDiscPage) DrawDiscPage();
        else DrawQuestion();
    }

    void DrawQuestion()
    {
        if (ImGui.RadioButton(Localization.T("verdite3.language.english"), !_japanese)) _japanese = false;
        if (ImGui.RadioButton(Localization.T("verdite3.language.both"), _japanese)) _japanese = true;
        ImGui.Spacing();
        ImGui.TextWrapped(Localization.T("verdite3.language.note"));
        Rule();

        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (ImGui.GetContentRegionAvail().X - ButtonWidth) * 0.5f);
        if (!ImGui.Button(Localization.T("verdite3.language.next"), new Vector2(ButtonWidth, 0f))) return;
        if (_japanese) _onDiscPage = true;
        else Keep(japaneseDisc: null);
    }

    void DrawDiscPage()
    {
        ImGui.TextWrapped(Localization.T("verdite3.jpdisc.description"));
        ImGui.Spacing();

        float spacing = ImGui.GetStyle().ItemSpacing.X;
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - BrowseWidth - spacing);
        ImGui.InputText("##jp-disc-path", ref _path, 1024);
        ImGui.SameLine();
        if (ImGui.Button(Localization.T("common.browse"), new Vector2(BrowseWidth, 0f))) Browse();

        if (_error.Length > 0)
        {
            ImGui.Spacing();
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0.38f, 0.38f, 1f));
            ImGui.TextWrapped(_error);
            ImGui.PopStyleColor();
        }
        Rule();

        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (ImGui.GetContentRegionAvail().X - (ButtonWidth * 2f + spacing)) * 0.5f);
        if (ImGui.Button(Localization.T("verdite3.language.back"), new Vector2(ButtonWidth, 0f)))
        {
            _onDiscPage = false;
            _error = "";
        }
        ImGui.SameLine();
        if (ImGui.Button(Localization.T("common.confirm"), new Vector2(ButtonWidth, 0f))) Confirm();
    }

    static void Rule()
    {
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
    }

    void Browse()
    {
        try
        {
            using var dialog = new NativeFileDialog().SelectFile().AddFilter(Localization.T("disc.filter"), "cue,chd");
            if (dialog.Open(out string? picked, FolderOf(_path)) != DialogResult.Okay || string.IsNullOrWhiteSpace(picked)) return;
            _path = picked;
            _error = "";
        }
        catch (Exception e)
        {
            _error = Localization.T("disc.error.dialog", e.Message);
        }
    }

    static string? FolderOf(string path) =>
        File.Exists(path) ? Path.GetDirectoryName(Path.GetFullPath(path)) : null;

    void Confirm()
    {
        var path = _path.Trim();
        if (Problem(path) is { } problem)
        {
            _error = Localization.T(problem);
            return;
        }
        Keep(Path.GetFullPath(path));
    }

    /// <summary>The string key saying why this is not the Japanese disc; null when it is.</summary>
    static string? Problem(string path)
    {
        if (!File.Exists(path)) return "disc.error.not_found";
        try
        {
            using var image = DiscImage.Open(path);
            return VolumeId(image) == JapaneseVolumeId ? null : "verdite3.jpdisc.wrong";
        }
        catch (Exception e)
        {
            Console.WriteLine($"[Verdite3] language: cannot read {path}: {e.Message}");
            return "verdite3.jpdisc.unreadable";
        }
    }

    static string VolumeId(IDiscImage image)
    {
        var sector = image.ReadSectorData(PrimaryVolumeSector, SectorBytes);
        return Encoding.ASCII.GetString(sector, VolumeIdAt, VolumeIdBytes).TrimEnd();
    }

    /// <summary>The answer, written: Japanese with its disc, or for English no language key.</summary>
    void Keep(string? japaneseDisc)
    {
        var values = ConfigManager.View.Values;
        values["verdite.language.asked"] = "1";
        if (japaneseDisc is null)
        {
            values.Remove(LanguageStep.LanguageKey);
        }
        else
        {
            values[LanguageStep.LanguageKey] = "ja";
            values[LanguageStep.JapaneseDiscKey] = japaneseDisc;
        }
        Runtime.SaveView();
        Console.WriteLine($"[Verdite3] language: {(japaneseDisc is null ? "English only" : "English + Japanese")}");
        Close();
    }
}
