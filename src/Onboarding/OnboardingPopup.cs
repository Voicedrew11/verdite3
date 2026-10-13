using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;
using ImGuiNET;
using RecompOne.Runtime.Host;
using RecompOne.Runtime.Host.Window;

namespace Verdite.Core;

/// <summary>
/// The setup's pages: a welcome with three presets (Recommended, Original++, Original)
/// and Customise, the display step, the tiers (Basic or Advanced behind one switch),
/// the game's own choice pages (if its spec has any), a summary. A preset goes
/// straight to the summary, where Adjust takes its choices into the display, the
/// tiers and the choice pages; Customise walks them from what the player started with. In the launcher (<see cref="Build"/> set) a strip at the foot follows
/// the game's build, and the popup stays up after Finish until the build is done,
/// which is what holds the game back until both are. In the game it is opened
/// from the settings page and closes on Finish, or on a list of what waits for
/// the next launch.
///
/// Every page can be driven by the mouse, the keyboard or a pad: ImGui's own
/// navigation, switched on only while the popup is open (the runtime's windows
/// never had it) and fed the pad's buttons here, since the runtime's ImGui backend
/// feeds it only the keyboard.
/// </summary>
internal sealed class OnboardingPopup : Popup
{
    public const int TierCount = 3;
    static readonly string[] TierKeys = ["verdite.setup.tier.original", "verdite.setup.tier.originalplus", "verdite.setup.tier.modern"];

    protected override string TitleKey => "verdite.setup.title";
    protected override Vector2 Size => new(720f, 580f);

    /// <summary>Its own Cancel and Back: Escape is Back here, not close.</summary>
    protected override bool Closable => false;

    /// <summary>The launcher's build, as the setup's strip shows it.</summary>
    public readonly record struct BuildState(string Status, int Step, int Steps, string? Error, bool Done);

    /// <summary>Set in the launcher: the build running beside setup.</summary>
    public Func<BuildState>? Build { get; init; }

    /// <summary>True once the choices were applied or setup skipped.</summary>
    public bool Finished { get; private set; }

    enum Page { Welcome, Display, Tiers, Choice, Summary, Waiting, Report }

    readonly OnboardingSpec _spec;
    readonly string _tag;
    readonly bool _inGame;

    Page _page;
    bool _advanced;
    bool _preset;
    bool _focus;
    int[] _display = [];
    int[][] _tiers = [];
    int[] _startDisplay = [];
    int[][] _startTiers = [];
    /// <summary>The choice page on screen, an index into the spec's pages.</summary>
    int _pageAt;
    SetupPageChoice[] _pages = [];
    SetupPageChoice[] _startPages = [];
    /// <summary>Whether the player has changed a choice page; one untouched follows its category.</summary>
    bool[] _pageTouched = [];
    Onboarding.Result? _result;

    public OnboardingPopup(OnboardingSpec spec, string tag, bool inGame)
    {
        _spec = spec;
        _tag = tag;
        _inGame = inGame;
    }

    protected override void OnOpened()
    {
        Go(Page.Welcome);
        _advanced = false;
        _preset = false;
        _result = null;
        Finished = false;
        // A first run starts the display on what suits the monitor; any other run
        // (from the game, after a version bump, forced) on what is set now, so
        // clicking through changes nothing the player chose.
        bool suggest = !_inGame && Onboarding.FirstRun(_spec);
        _display = [.. _spec.Display.Select(f => suggest ? Onboarding.Suggest(_spec, f) : Onboarding.Detect(_spec, f))];
        _tiers = [.. _spec.Categories.Select(c => Onboarding.Detect(_spec, c))];
        // What the player started with, for "Keep as is": a value set by hand is kept
        // when the player does not pick a style for it.
        _startDisplay = [.. _display];
        _startTiers = [.. _tiers.Select(t => (int[])t.Clone())];
        _pages = [.. _spec.Pages.Select(p => Onboarding.Detect(_spec, p))];
        _startPages = [.. _pages.Select(c => c.Clone())];
        _pageTouched = new bool[_spec.Pages.Count];
        Nav(true);
    }

    protected override void OnClosed() => Nav(false);

    protected override void Update()
    {
        if (!IsOpen) return;
        FeedPad();
        if (_page == Page.Waiting && (Build is null || Build().Done || Build().Error is not null)) Close();
    }

    // ---- Pages ----

    protected override void DrawContent()
    {
        var style = ImGui.GetStyle();
        float footer = ImGui.GetFrameHeightWithSpacing() + style.ItemSpacing.Y * 2f;
        if (Build is not null) footer += ImGui.GetTextLineHeightWithSpacing() + ImGui.GetFrameHeightWithSpacing();

        if (ImGui.BeginChild("##page", new Vector2(0f, -footer)))
        {
            switch (_page)
            {
                case Page.Welcome: Welcome(); break;
                case Page.Display: Display(); break;
                case Page.Tiers: if (_advanced) Advanced(); else Basic(); break;
                case Page.Choice: Choice(); break;
                case Page.Summary: Summary(); break;
                case Page.Waiting: Waiting(); break;
                case Page.Report: Report(); break;
            }
        }
        ImGui.EndChild();

        ImGui.Separator();
        if (Build is not null) Strip();
        Footer();

        // Escape and the pad's Circle/B step back, but never off the welcome, where
        // Back is skip, and never while a combo's list is open, which they close.
        if (_page != Page.Welcome && ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows)
            && (ImGui.IsKeyPressed(ImGuiKey.Escape, false) || ImGui.IsKeyPressed(ImGuiKey.GamepadFaceRight, false)))
            Back();
    }

    void Welcome()
    {
        Heading("verdite.setup.welcome.title");
        Wrapped(_spec.Welcome ?? "verdite.setup.welcome");
        if (Build is not null)
        {
            ImGui.Spacing();
            Wrapped("verdite.setup.welcome.building");
        }
        ImGui.Spacing();

        float h = ImGui.GetTextLineHeightWithSpacing() * 3f;
        ImGui.PushStyleVar(ImGuiStyleVar.ButtonTextAlign, new Vector2(0f, 0.5f));
        if (_focus) { ImGui.SetKeyboardFocusHere(); _focus = false; }
        if (ImGui.Button($"{Localization.T("verdite.setup.recommended")}\n{Localization.T("verdite.setup.recommended.desc")}", new Vector2(-1f, h)))
            Pick(2);
        ImGui.Spacing();
        if (ImGui.Button($"{Localization.T("verdite.setup.originalplus")}\n{Localization.T("verdite.setup.originalplus.desc")}", new Vector2(-1f, h)))
            Pick(1);
        ImGui.Spacing();
        var list = string.Join(", ", _spec.Display
            .Where(f => f.Original is { } o && o >= 0 && o < f.Options.Count)
            .Select(f => OptionLabel(f.Options[f.Original!.Value])));
        if (ImGui.Button($"{Localization.T("verdite.setup.original")}\n{Localization.T("verdite.setup.original.desc", list)}", new Vector2(-1f, h)))
            Pick(0);
        ImGui.Spacing();
        if (ImGui.Button($"{Localization.T("verdite.setup.customise")}\n{Localization.T("verdite.setup.customise.desc")}", new Vector2(-1f, h)))
        {
            _display = [.. _startDisplay];
            _tiers = [.. _startTiers.Select(t => (int[])t.Clone())];
            _pages = [.. _startPages.Select(c => c.Clone())];
            _pageTouched = new bool[_spec.Pages.Count];
            _preset = false;
            Go(Page.Display);
        }
        ImGui.PopStyleVar();
    }

    /// <summary>A preset's choices (tier 0, 1 or 2), then the summary.</summary>
    void Pick(int tier)
    {
        (_display, _tiers, _pages) = Onboarding.Preset(_spec, tier);
        _preset = true;
        Go(Page.Summary);
    }

    void Display()
    {
        Heading("verdite.setup.display");
        Dim("verdite.setup.display.intro");
        if (Onboarding.Monitor() is { } m)
            Dim(Localization.T("verdite.setup.monitor", m.W, m.H, Onboarding.Refresh()), key: false);
        ImGui.Spacing();

        float labelW = ImGui.GetContentRegionAvail().X * 0.35f;
        for (int i = 0; i < _spec.Display.Count; i++)
        {
            var field = _spec.Display[i];
            bool keep = _startDisplay[i] < 0;
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(Localization.T(field.Label));
            if (field.Tip is { } tip && ImGui.IsItemHovered()) ImGui.SetTooltip(Localization.T(tip));
            ImGui.SameLine(labelW);
            ImGui.SetNextItemWidth(-1f);
            // Only the first field takes the focus: _focus is spent here.
            if (_focus) { ImGui.SetKeyboardFocusHere(); _focus = false; }
            if (field.Slider)
            {
                Slider(field, ref _display[i], keep);
                continue;
            }
            string preview = _display[i] >= 0 ? OptionLabel(field.Options[_display[i]]) : KeepValue(field);
            if (ImGui.BeginCombo($"##{field.Id}", preview))
            {
                if (keep)
                {
                    bool kept = _display[i] < 0;
                    if (ImGui.Selectable(KeepValue(field), kept)) _display[i] = -1;
                    if (kept) ImGui.SetItemDefaultFocus();
                }
                for (int o = 0; o < field.Options.Count; o++)
                {
                    bool selected = o == _display[i];
                    if (ImGui.Selectable(OptionLabel(field.Options[o]), selected)) _display[i] = o;
                    if (selected) ImGui.SetItemDefaultFocus();
                }
                ImGui.EndCombo();
            }
        }
    }

    /// <summary>
    /// The tiers page's top: Basic or Advanced as a switch, a line on what the chosen
    /// one means, and the intro. Both pages start with it.
    /// </summary>
    void Switch()
    {
        float w = (ImGui.GetContentRegionAvail().X - ImGui.GetStyle().ItemSpacing.X) / 2f;
        if (LitButton(Localization.T("verdite.setup.basic"), "switchBasic", !_advanced, new Vector2(w, 0f))) _advanced = false;
        ImGui.SameLine();
        if (LitButton(Localization.T("verdite.setup.advanced"), "switchAdvanced", _advanced, new Vector2(-1f, 0f))) _advanced = true;
        ImGui.Spacing();
        var names = string.Join(", ", _spec.Categories.Select(c => Localization.T(c.Label)));
        Dim(Localization.T(_advanced ? "verdite.setup.advanced.desc" : "verdite.setup.basic.desc", names), key: false);
        Dim("verdite.setup.tiers.intro");
    }

    void Basic()
    {
        Switch();
        for (int c = 0; c < _spec.Categories.Count; c++)
        {
            var cat = _spec.Categories[c];
            ImGui.Spacing();
            ImGui.SeparatorText(Localization.T(cat.Label));
            int now = _tiers[c].Distinct().Count() == 1 ? _tiers[c][0] : -1;
            // Keep restores what the groups started at, so a category set by hand is kept.
            bool started = _startTiers[c].Distinct().Count() != 1 || _startTiers[c][0] < 0;
            var pick = TierRow(cat.Id, now, cat.Tiers, Enumerable.Range(0, TierCount).ToArray(), started, first: c == 0);
            if (pick is { } p)
            {
                if (p < 0) Array.Copy(_startTiers[c], _tiers[c], _tiers[c].Length);
                else Array.Fill(_tiers[c], p);
            }
        }
    }

    void Advanced()
    {
        Switch();
        for (int c = 0; c < _spec.Categories.Count; c++)
        {
            var cat = _spec.Categories[c];
            ImGui.Spacing();
            ImGui.SeparatorText(Localization.T(cat.Label));
            for (int g = 0; g < cat.Groups.Count; g++)
            {
                var group = cat.Groups[g];
                ImGui.TextUnformatted(Localization.T(group.Label));
                var pick = TierRow($"{cat.Id}.{group.Id}", _tiers[c][g], group.Tiers.Select(t => t.Desc).ToList(),
                                   Spans(group), _startTiers[c][g] < 0, first: c == 0 && g == 0);
                if (pick is { } p) _tiers[c][g] = p < 0 ? _startTiers[c][g] : p;
                ImGui.Spacing();
            }
        }
    }

    /// <summary>
    /// One of the spec's choice pages: its heading and paragraphs, its label, an option
    /// button each (the chosen one lit, its description under it), then its slider.
    /// </summary>
    void Choice()
    {
        var page = _spec.Pages[_pageAt];
        var choice = _pages[_pageAt];
        Heading(page.Title);
        foreach (var body in page.Body)
        {
            Wrapped(body);
            ImGui.Spacing();
        }
        ImGui.SeparatorText(Localization.T(page.Label));
        var style = ImGui.GetStyle();
        for (int o = 0; o < page.Options.Count; o++)
        {
            var option = page.Options[o];
            bool lit = choice.Option == o;
            // The page's first button takes the focus, once; a page with no options gives it to the slider.
            if (o == 0 && _focus) { ImGui.SetKeyboardFocusHere(); _focus = false; }
            if (LitButton(OptionLabel(option), $"{page.Id}{o}", lit, new Vector2(-1f, 0f)))
            {
                choice.Option = o;
                _pageTouched[_pageAt] = true;
                if (option.Value is { } v) choice.Value = v;
            }
            if (option.Desc is { } desc)
            {
                if (!lit) ImGui.PushStyleColor(ImGuiCol.Text, style.Colors[(int)ImGuiCol.TextDisabled]);
                ImGui.TextWrapped(Localization.T(desc));
                if (!lit) ImGui.PopStyleColor();
            }
            ImGui.Spacing();
        }

        if (page.Slider is { } s)
        {
            ImGui.Spacing();
            ImGui.TextUnformatted(Localization.T(s.Label));
            float v = (float)ValueOf(s, choice);
            // At the minimum the slider shows its label, not the number.
            string format = s.MinLabel is { } low && v <= s.Min + 1e-6 ? Localization.T(low).Replace("%", "%%") : s.Format;
            if (_focus) { ImGui.SetKeyboardFocusHere(); _focus = false; }
            if (ImGui.SliderFloat($"##{page.Id}.slider", ref v, (float)s.Min, (float)s.Max, format,
                                  ImGuiSliderFlags.AlwaysClamp | ImGuiSliderFlags.NoInput))
            {
                double d = s.Step > 0 ? Math.Round(v / s.Step) * s.Step : v;
                choice.Value = Math.Clamp(d, s.Min, s.Max);
                _pageTouched[_pageAt] = true;
            }
            if (s.Tip is { } tip && ImGui.IsItemHovered()) ImGui.SetTooltip(Localization.T(tip));
        }
    }

    /// <summary>A choice page's slider value now: its own, else the key's, else its minimum.</summary>
    double ValueOf(SetupSlider slider, SetupPageChoice choice) =>
        choice.Value ?? Onboarding.Level(_spec, slider.Key) ?? slider.Min;

    /// <summary>A slider's value as the summary shows it: its minimum label at the minimum, else its format.</summary>
    string SliderText(SetupSlider slider, double v) =>
        slider.MinLabel is { } low && v <= slider.Min + 1e-6 ? Localization.T(low) : Printf(slider.Format, v);

    /// <summary>A number in an ImGui printf format, the one forms the setup uses: <c>%.2f</c>, <c>%d</c>.</summary>
    static string Printf(string format, double v)
    {
        var m = Regex.Match(format, @"%(?:\.(\d+))?([fd])");
        if (!m.Success) return format;
        string number = m.Groups[2].Value == "d"
            ? Math.Round(v).ToString(CultureInfo.InvariantCulture)
            : v.ToString("F" + (m.Groups[1].Success ? m.Groups[1].Value : "6"), CultureInfo.InvariantCulture);
        return format[..m.Index] + number + format[(m.Index + m.Length)..];
    }

    void Summary()
    {
        if (_preset)
        {
            // A preset's choices stay as they are; Adjust only opens them for changing.
            if (_focus) { ImGui.SetKeyboardFocusHere(); _focus = false; }
            if (ImGui.Button(Localization.T("verdite.setup.adjust"), new Vector2(-1f, 0f)))
            {
                _preset = false;
                Go(Page.Display);
            }
            ImGui.Spacing();
        }
        Heading("verdite.setup.summary");
        Dim("verdite.setup.summary.intro");
        ImGui.Spacing();

        ImGui.SeparatorText(Localization.T("verdite.setup.display"));
        for (int i = 0; i < _spec.Display.Count; i++)
            Line(_spec.Display[i].Label, _display[i] >= 0 ? OptionLabel(_spec.Display[i].Options[_display[i]]) : KeepValue(_spec.Display[i]));

        for (int c = 0; c < _spec.Categories.Count; c++)
        {
            var cat = _spec.Categories[c];
            ImGui.SeparatorText(Localization.T(cat.Label));
            bool one = _tiers[c].Distinct().Count() == 1;
            if (one && !_advanced)
            {
                Line(cat.Label, TierName(_tiers[c][0]));
                continue;
            }
            for (int g = 0; g < cat.Groups.Count; g++)
                Line(cat.Groups[g].Label, GroupName(cat.Groups[g], _tiers[c][g]));
        }

        for (int p = 0; p < _spec.Pages.Count; p++)
        {
            var page = _spec.Pages[p];
            var choice = _pages[p];
            ImGui.SeparatorText(Localization.T(page.Title));
            Line(page.Label, choice.Option >= 0 && choice.Option < page.Options.Count
                ? OptionLabel(page.Options[choice.Option])
                : Localization.T("verdite.setup.keep"));
            if (page.Slider is { } s) Line(s.Label, SliderText(s, ValueOf(s, choice)));
        }
    }

    void Waiting()
    {
        Heading("verdite.setup.waiting.title");
        Wrapped("verdite.setup.waiting");
    }

    void Report()
    {
        Heading("verdite.setup.report.title");
        Wrapped("verdite.setup.report");
        ImGui.Spacing();
        foreach (var name in LaterNames()) ImGui.BulletText(name);
    }

    // ---- The foot of every page ----

    void Strip()
    {
        var state = Build!();
        if (state.Error is not null)
        {
            // A failed build keeps the setup open, so the choices are not lost; the
            // failure popup after it says why.
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0.38f, 0.38f, 1f));
            ImGui.TextWrapped(Localization.T("verdite.setup.buildfailed"));
            ImGui.PopStyleColor();
            return;
        }
        if (state.Done)
            ImGui.TextUnformatted(Localization.T("verdite.setup.built"));
        else
            ImGui.TextUnformatted($"{Localization.T(state.Status)}{new string('.', 1 + (int)(ImGui.GetTime() * 2.0) % 3)}");
        float fraction = state.Done ? 1f : state.Steps <= 0 ? 0f : Math.Clamp(state.Step / (float)state.Steps, 0f, 1f);
        ImGui.ProgressBar(fraction, new Vector2(-1f, ImGui.GetFrameHeight() * 0.5f), "");
    }

    void Footer()
    {
        var style = ImGui.GetStyle();
        float avail = ImGui.GetContentRegionAvail().X;
        float half = (avail - style.ItemSpacing.X) / 2f;

        (string? left, string? right) = _page switch
        {
            Page.Welcome => (_inGame ? "verdite.setup.cancel" : "verdite.setup.skip", (string?)null),
            Page.Display => ("verdite.setup.back", "verdite.setup.next"),
            Page.Tiers => ("verdite.setup.back", "verdite.setup.next"),
            Page.Choice => ("verdite.setup.back", "verdite.setup.next"),
            Page.Summary => ("verdite.setup.back", "verdite.setup.finish"),
            Page.Report => (null, "verdite.setup.close"),
            _ => (null, null),
        };

        if (left is not null)
        {
            if (ImGui.Button(Localization.T(left), new Vector2(half, 0f))) Back();
            ImGui.SameLine();
        }
        else ImGui.Dummy(new Vector2(half, 0f));
        if (right is null) return;
        ImGui.SameLine(avail - half + style.WindowPadding.X);
        // Finish takes the focus on a summary with no Adjust above it, and Close on the report.
        if (_focus && (_page == Page.Report || _page == Page.Summary && !_preset)) { ImGui.SetKeyboardFocusHere(); _focus = false; }
        if (ImGui.Button(Localization.T(right), new Vector2(half, 0f))) Forward();
    }

    void Back()
    {
        switch (_page)
        {
            case Page.Welcome:
                if (_inGame) Close();
                else
                {
                    Onboarding.Skip(_spec, _tag);
                    Finished = true;
                    Done();
                }
                break;
            case Page.Display: ToWelcome(); break;
            case Page.Tiers: Go(Page.Display); break;
            case Page.Choice:
                if (_pageAt > 0) { _pageAt--; Go(Page.Choice); }
                else Go(Page.Tiers);
                break;
            case Page.Summary:
                if (_preset) ToWelcome();
                else if (_spec.Pages.Count > 0) { _pageAt = _spec.Pages.Count - 1; Go(Page.Choice); }
                else Go(Page.Tiers);
                break;
            case Page.Report: Close(); break;
        }
    }

    void ToWelcome() => Go(Page.Welcome);

    /// <summary>
    /// Enter a choice page going forward. Untouched by the player, a page with a
    /// <see cref="SetupPage.Follows"/> category takes the preset of the tier that
    /// category is at, when every one of its groups is at the same tier.
    /// </summary>
    void Enter(int at)
    {
        _pageAt = at;
        var page = _spec.Pages[at];
        int c = page.Follows is null || _pageTouched[at] ? -1 : _spec.Categories.FindIndex(k => k.Id == page.Follows);
        if (c >= 0 && _tiers[c].Distinct().Count() == 1 && _tiers[c][0] is var t && t >= 0 && t < page.Presets.Count)
            _pages[at] = page.Presets[t].Clone();
        Go(Page.Choice);
    }

    /// <summary>Change page, and put the keyboard focus on the page's first widget.</summary>
    void Go(Page page)
    {
        _page = page;
        _focus = true;
    }

    void Forward()
    {
        switch (_page)
        {
            case Page.Display: Go(Page.Tiers); break;
            case Page.Tiers:
                if (_spec.Pages.Count > 0) Enter(0);
                else Go(Page.Summary);
                break;
            case Page.Choice:
                if (_pageAt + 1 < _spec.Pages.Count) Enter(_pageAt + 1);
                else Go(Page.Summary);
                break;
            case Page.Summary: Finish(); break;
            case Page.Report: Close(); break;
        }
    }

    void Finish()
    {
        _result = Onboarding.Apply(_spec, Onboarding.Sets(_spec, _display, _tiers, _pages), _tag);
        Finished = true;
        if (_inGame && _result.NextLaunch.Count > 0) Go(Page.Report);
        else Done();
    }

    /// <summary>
    /// Close, or in the launcher hold on the waiting page until the build is done. A
    /// build that failed closes too: the choices are applied, and the strip said why.
    /// </summary>
    void Done()
    {
        if (Build is { } build && build() is { Done: false, Error: null }) Go(Page.Waiting);
        else Close();
    }

    // ---- Pieces ----

    /// <summary>
    /// A field as a slider whose positions are its options, as the Settings window's
    /// frame rate is: the format is the option's label, a literal, so no index ever
    /// shows and typed entry is off. When <paramref name="keep"/>, the slider's first
    /// position, one below the options, is keep current, labelled with the value it
    /// keeps.
    /// </summary>
    void Slider(SetupField field, ref int index, bool keep)
    {
        int last = field.Options.Count - 1;
        int lo = keep ? -1 : 0;
        index = Math.Clamp(index, lo, last);
        string label = index >= 0 ? OptionLabel(field.Options[index]) : KeepValue(field);
        int at = index;
        if (ImGui.SliderInt($"##{field.Id}", ref at, lo, last, label.Replace("%", "%%"),
                            ImGuiSliderFlags.AlwaysClamp | ImGuiSliderFlags.NoInput))
            index = at;
    }

    /// <summary>
    /// The tiers as buttons, one per run of identical tiers (<paramref name="span"/>, see
    /// <see cref="Spans"/>), each run's names joined and the lit one marked; the run's
    /// description wrapped under its button. A keep button comes first when
    /// <paramref name="keep"/>; <paramref name="first"/> gives the row the page's focus.
    /// Returns the run's first tier when one is clicked, -1
    /// for keep, or null when nothing was.
    /// </summary>
    int? TierRow(string id, int current, IReadOnlyList<string> descs, int[] span, bool keep, bool first = false)
    {
        // The row's first button takes the focus, once; the switch above never does.
        if (first && _focus) { ImGui.SetKeyboardFocusHere(); _focus = false; }
        var style = ImGui.GetStyle();
        float space = style.ItemSpacing.X;
        var starts = Enumerable.Range(0, span.Length).Where(t => span[t] == t).ToList();
        int buttons = starts.Count + (keep ? 1 : 0);
        float unit = (ImGui.GetContentRegionAvail().X - space * (buttons - 1)) / (span.Length + (keep ? 2f / 3f : 0f));
        int? pick = null;
        var columns = new List<(float X, float W, int Start, bool Lit)>();

        if (keep)
        {
            if (LitButton(Localization.T("verdite.setup.keep"), $"{id}keep", current < 0, new Vector2(unit * 2f / 3f, 0f))) pick = -1;
            ImGui.SameLine();
        }
        foreach (int s in starts)
        {
            int count = span.Count(x => x == s);
            float w = unit * count + space * (count - 1);
            bool lit = current >= 0 && current < span.Length && span[current] == s;
            if (columns.Count > 0) ImGui.SameLine();
            columns.Add((ImGui.GetCursorPosX(), w, s, lit));
            var names = string.Join(" / ", Enumerable.Range(s, count).Select(t => Localization.T(TierKeys[t])));
            if (LitButton(names, $"{id}{s}", lit, new Vector2(w, 0f))) pick = s;
        }

        for (int k = 0; k < columns.Count; k++)
        {
            var (x, w, s, lit) = columns[k];
            if (k > 0) ImGui.SameLine(x);
            else ImGui.SetCursorPosX(x);
            ImGui.BeginGroup();
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + w);
            if (!lit) ImGui.PushStyleColor(ImGuiCol.Text, style.Colors[(int)ImGuiCol.TextDisabled]);
            ImGui.TextWrapped(s < descs.Count ? Localization.T(descs[s]) : "");
            if (!lit) ImGui.PopStyleColor();
            ImGui.PopTextWrapPos();
            ImGui.EndGroup();
        }

        if (current < 0) Dim("verdite.setup.custom");
        return pick;
    }

    /// <summary>
    /// For each tier of a group, the index of the first tier of its run of identical
    /// tiers (the same keys, every value the same), so a run shows as one button.
    /// </summary>
    static int[] Spans(SetupGroup group)
    {
        var span = new int[group.Tiers.Count];
        for (int t = 0; t < span.Length; t++)
            span[t] = t > 0 && SameSet(group.Tiers[t - 1], group.Tiers[t]) ? span[t - 1] : t;
        return span;
    }

    static bool SameSet(SetupTier a, SetupTier b) =>
        a.Set.Count == b.Set.Count
        && a.Set.All(kv => b.Set.TryGetValue(kv.Key, out var v) && Onboarding.SameText(kv.Value, v));

    /// <summary>A button, lit in the active colour when <paramref name="lit"/> and marked "> " so the state is not colour alone; true when clicked.</summary>
    static bool LitButton(string label, string id, bool lit, Vector2 size)
    {
        var style = ImGui.GetStyle();
        if (lit)
        {
            ImGui.PushStyleColor(ImGuiCol.Button, style.Colors[(int)ImGuiCol.ButtonActive]);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, style.Colors[(int)ImGuiCol.ButtonActive]);
        }
        bool clicked = ImGui.Button($"{(lit ? "> " : "")}{label}##{id}", size);
        if (lit) ImGui.PopStyleColor(2);
        return clicked;
    }

    /// <summary>The text a display field shows when it keeps its current value.</summary>
    string KeepValue(SetupField field) => Localization.T("verdite.setup.keepvalue", Onboarding.Current(_spec, field) ?? "?");

    static string TierName(int t) => t >= 0 && t < TierCount ? Localization.T(TierKeys[t]) : Localization.T("verdite.setup.keep");

    /// <summary>A group's tier by name: the names of its run when the tier is in one, "Keep as is" when it is set by hand.</summary>
    static string GroupName(SetupGroup group, int t)
    {
        if (t < 0 || t >= group.Tiers.Count) return Localization.T("verdite.setup.keep");
        var span = Spans(group);
        return string.Join(" / ", Enumerable.Range(0, span.Length).Where(x => span[x] == span[t])
            .Select(x => Localization.T(TierKeys[x])));
    }

    /// <summary>An option's label: a localisation key (they all have a dot), or a
    /// plain one ("60", "16:9") shown as it is, which the runtime would warn about.</summary>
    static string OptionLabel(SetupOption o) => OptionLabel(o.Label, o.Set);

    static string OptionLabel(SetupPageOption o) => OptionLabel(o.Label, o.Set);

    static string OptionLabel(string label, Dictionary<string, string?> set)
    {
        if (!label.Contains('.')) return label;
        return set.Values.Any(v => v == "$refresh") ? Localization.T(label, Onboarding.Refresh()) : Localization.T(label);
    }

    /// <summary>The groups and display fields owning a key that waits for the next launch.</summary>
    IEnumerable<string> LaterNames()
    {
        if (_result is null) yield break;
        var seen = new HashSet<string>();
        foreach (var key in _result.NextLaunch)
        {
            string? name = _spec.Display.FirstOrDefault(f => f.Options.Any(o => o.Set.ContainsKey(key)))?.Label
                ?? _spec.Categories.SelectMany(c => c.Groups).FirstOrDefault(g => g.Tiers.Any(t => t.Set.ContainsKey(key)))?.Label;
            var text = Localization.T(name ?? key);
            if (seen.Add(text)) yield return text;
        }
    }

    static void Heading(string key)
    {
        ImGui.TextUnformatted(Localization.T(key));
        ImGui.Separator();
        ImGui.Spacing();
    }

    static void Wrapped(string key) => ImGui.TextWrapped(Localization.T(key));

    static void Dim(string text, bool key = true)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]);
        ImGui.TextWrapped(key ? Localization.T(text) : text);
        ImGui.PopStyleColor();
    }

    static void Line(string labelKey, string value)
    {
        ImGui.TextUnformatted(Localization.T(labelKey));
        ImGui.SameLine(ImGui.GetContentRegionAvail().X * 0.45f);
        ImGui.TextUnformatted(value);
    }

    // ---- The pad, for ImGui's navigation ----

    /// <summary>SDL game-controller buttons, as the runtime numbers them (the stick
    /// directions are its own 102-105), to ImGui's gamepad keys.</summary>
    static readonly (int Button, ImGuiKey Key)[] PadKeys =
    [
        (0, ImGuiKey.GamepadFaceDown), (1, ImGuiKey.GamepadFaceRight),
        (2, ImGuiKey.GamepadFaceLeft), (3, ImGuiKey.GamepadFaceUp),
        (9, ImGuiKey.GamepadL1), (10, ImGuiKey.GamepadR1),
        (11, ImGuiKey.GamepadDpadUp), (12, ImGuiKey.GamepadDpadDown),
        (13, ImGuiKey.GamepadDpadLeft), (14, ImGuiKey.GamepadDpadRight),
        (102, ImGuiKey.GamepadLStickLeft), (103, ImGuiKey.GamepadLStickRight),
        (104, ImGuiKey.GamepadLStickUp), (105, ImGuiKey.GamepadLStickDown),
    ];

    readonly bool[] _held = new bool[PadKeys.Length];
    ImGuiConfigFlags _flags;
    ImGuiBackendFlags _backend;
    bool _nav;

    void Nav(bool on)
    {
        var io = ImGui.GetIO();
        if (on && !_nav)
        {
            _flags = io.ConfigFlags;
            _backend = io.BackendFlags;
            io.ConfigFlags |= ImGuiConfigFlags.NavEnableKeyboard | ImGuiConfigFlags.NavEnableGamepad;
            io.BackendFlags |= ImGuiBackendFlags.HasGamepad;
            _nav = true;
        }
        else if (!on && _nav)
        {
            for (int i = 0; i < PadKeys.Length; i++)
                if (_held[i]) { io.AddKeyEvent(PadKeys[i].Key, false); _held[i] = false; }
            io.ConfigFlags = _flags;
            io.BackendFlags = _backend;
            _nav = false;
        }
    }

    void FeedPad()
    {
        if (!_nav) return;
        var io = ImGui.GetIO();
        for (int i = 0; i < PadKeys.Length; i++)
        {
            bool down = HostWindow.IsPadButtonDown(PadKeys[i].Button);
            if (down == _held[i]) continue;
            _held[i] = down;
            io.AddKeyEvent(PadKeys[i].Key, down);
        }
    }

    /// <summary>The setup's own words, every key in the runtime's three languages.
    /// The game's groups and options are the game's, in its onboarding.json.</summary>
    public const string Strings = """
    {
      "strings": {
        "verdite.setup.title": { "en": "Setup", "pt-BR": "Configuração", "es-419": "Configuración" },
        "verdite.setup.welcome.title": { "en": "Welcome", "pt-BR": "Bem-vindo", "es-419": "Bienvenido" },
        "verdite.setup.welcome": {
          "en": "Before you play, choose how the game should look, sound and control. Everything here can be changed later in the settings.",
          "pt-BR": "Antes de jogar, escolha como o jogo deve parecer, soar e ser controlado. Tudo aqui pode ser alterado depois nas configurações.",
          "es-419": "Antes de jugar, elige cómo debe verse, sonar y controlarse el juego. Todo esto se puede cambiar después en la configuración."
        },
        "verdite.setup.welcome.building": {
          "en": "The game is being built from your disc in the meantime. It starts once both are done.",
          "pt-BR": "Enquanto isso, o jogo está sendo construído a partir do seu disco. Ele inicia quando as duas coisas terminarem.",
          "es-419": "Mientras tanto, el juego se está construyendo desde tu disco. Empieza cuando ambas cosas terminen."
        },
        "verdite.setup.recommended": { "en": "Recommended", "pt-BR": "Recomendado", "es-419": "Recomendado" },
        "verdite.setup.recommended.desc": {
          "en": "Everything the port adds, with the display suited to your monitor.",
          "pt-BR": "Tudo o que o port acrescenta, com a tela adequada ao seu monitor.",
          "es-419": "Todo lo que añade el port, con la pantalla adaptada a tu monitor."
        },
        "verdite.setup.originalplus": { "en": "Original++", "pt-BR": "Original++", "es-419": "Original++" },
        "verdite.setup.originalplus.desc": {
          "en": "The PlayStation's look and feel, with what its hardware got wrong fixed, and the display suited to your monitor.",
          "pt-BR": "A aparência e a sensação do PlayStation, com o que o hardware fazia errado corrigido, e a tela adequada ao seu monitor.",
          "es-419": "El aspecto y la sensación de la PlayStation, con lo que el hardware hacía mal corregido, y la pantalla adaptada a tu monitor."
        },
        "verdite.setup.original": { "en": "Original", "pt-BR": "Original", "es-419": "Original" },
        "verdite.setup.original.desc": {
          "en": "Every style as the PlayStation ran it, at {0}.",
          "pt-BR": "Todos os estilos como o PlayStation os rodava, em {0}.",
          "es-419": "Todos los estilos como los ejecutaba la PlayStation, a {0}."
        },
        "verdite.setup.customise": { "en": "Customise…", "pt-BR": "Personalizar…", "es-419": "Personalizar…" },
        "verdite.setup.customise.desc": {
          "en": "Choose the display and a style for each part yourself.",
          "pt-BR": "Escolha a tela e um estilo para cada parte, por conta própria.",
          "es-419": "Elige tú mismo la pantalla y un estilo para cada parte."
        },
        "verdite.setup.adjust": { "en": "Adjust these choices…", "pt-BR": "Ajustar estas escolhas…", "es-419": "Ajustar estas opciones…" },
        "verdite.setup.buildfailed": {
          "en": "The build failed. Finish setup to keep your choices; the reason follows.",
          "pt-BR": "A construção falhou. Conclua a configuração para manter suas escolhas; o motivo vem em seguida.",
          "es-419": "La construcción falló. Finaliza la configuración para conservar tus opciones; el motivo viene a continuación."
        },
        "verdite.setup.tiers.intro": {
          "en": "Original is the game as the PlayStation ran it. Original++ keeps that look and feel and fixes what the hardware got wrong. Modern adds the port's enhancements.",
          "pt-BR": "Original é o jogo como o PlayStation o rodava. Original++ mantém essa aparência e sensação e corrige o que o hardware fazia errado. Moderno acrescenta as melhorias do port.",
          "es-419": "Original es el juego tal como lo ejecutaba la PlayStation. Original++ conserva ese aspecto y sensación y corrige lo que el hardware hacía mal. Moderno agrega las mejoras del port."
        },
        "verdite.setup.tier.original": { "en": "Original", "pt-BR": "Original", "es-419": "Original" },
        "verdite.setup.tier.originalplus": { "en": "Original++", "pt-BR": "Original++", "es-419": "Original++" },
        "verdite.setup.tier.modern": { "en": "Modern", "pt-BR": "Moderno", "es-419": "Moderno" },
        "verdite.setup.display": { "en": "Display", "pt-BR": "Tela", "es-419": "Pantalla" },
        "verdite.setup.display.intro": {
          "en": "How the picture is shown. These are asked on their own, apart from the styles that follow.",
          "pt-BR": "Como a imagem é exibida. Estas opções são perguntadas à parte dos estilos a seguir.",
          "es-419": "Cómo se muestra la imagen. Estas opciones se preguntan aparte de los estilos que siguen."
        },
        "verdite.setup.monitor": {
          "en": "Your display: {0} × {1} at {2} Hz",
          "pt-BR": "Sua tela: {0} × {1} a {2} Hz",
          "es-419": "Tu pantalla: {0} × {1} a {2} Hz"
        },
        "verdite.setup.keepvalue": {
          "en": "Keep current ({0})",
          "pt-BR": "Manter atual ({0})",
          "es-419": "Mantener actual ({0})"
        },
        "verdite.setup.keep": { "en": "Keep as is", "pt-BR": "Manter como está", "es-419": "Dejar como está" },
        "verdite.setup.basic": { "en": "Basic", "pt-BR": "Básico", "es-419": "Básico" },
        "verdite.setup.basic.desc": {
          "en": "One style each for {0}.",
          "pt-BR": "Um estilo para cada: {0}.",
          "es-419": "Un estilo para cada uno: {0}."
        },
        "verdite.setup.advanced": { "en": "Advanced", "pt-BR": "Avançado", "es-419": "Avanzado" },
        "verdite.setup.advanced.desc": {
          "en": "A style for each part of {0}, one by one.",
          "pt-BR": "Um estilo para cada parte de {0}, uma por uma.",
          "es-419": "Un estilo para cada parte de {0}, una por una."
        },
        "verdite.setup.custom": {
          "en": "Custom: set by hand. Choose a style to replace it, or keep it as it is.",
          "pt-BR": "Personalizado: definido à mão. Escolha um estilo para substituí-lo, ou mantenha como está.",
          "es-419": "Personalizado: definido a mano. Elige un estilo para reemplazarlo, o déjalo como está."
        },
        "verdite.setup.summary": { "en": "Ready", "pt-BR": "Pronto", "es-419": "Listo" },
        "verdite.setup.summary.intro": {
          "en": "These choices will be applied:",
          "pt-BR": "Estas escolhas serão aplicadas:",
          "es-419": "Se aplicarán estas opciones:"
        },
        "verdite.setup.waiting.title": { "en": "Almost there", "pt-BR": "Quase lá", "es-419": "Casi listo" },
        "verdite.setup.waiting": {
          "en": "Your settings are saved. The game starts as soon as it is built.",
          "pt-BR": "Suas configurações foram salvas. O jogo inicia assim que for construído.",
          "es-419": "Tu configuración se guardó. El juego empieza en cuanto esté construido."
        },
        "verdite.setup.built": { "en": "The game is built.", "pt-BR": "O jogo foi construído.", "es-419": "El juego está construido." },
        "verdite.setup.report.title": { "en": "Done", "pt-BR": "Concluído", "es-419": "Listo" },
        "verdite.setup.report": {
          "en": "Everything else has changed already. These take effect the next time the game starts:",
          "pt-BR": "Todo o resto já mudou. Estas entram em vigor na próxima vez que o jogo iniciar:",
          "es-419": "Todo lo demás ya cambió. Estas se aplican la próxima vez que inicie el juego:"
        },
        "verdite.setup.skip": { "en": "Skip setup", "pt-BR": "Pular configuração", "es-419": "Omitir configuración" },
        "verdite.setup.cancel": { "en": "Cancel", "pt-BR": "Cancelar", "es-419": "Cancelar" },
        "verdite.setup.back": { "en": "Back", "pt-BR": "Voltar", "es-419": "Atrás" },
        "verdite.setup.next": { "en": "Next", "pt-BR": "Avançar", "es-419": "Siguiente" },
        "verdite.setup.finish": { "en": "Finish", "pt-BR": "Concluir", "es-419": "Finalizar" },
        "verdite.setup.close": { "en": "Close", "pt-BR": "Fechar", "es-419": "Cerrar" }
      }
    }
    """;
}
