using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Akeno.ColorVision.App.Imaging;
using Akeno.ColorVision.App.Infrastructure;
using Akeno.ColorVision.ColorScience;
using Akeno.ColorVision.Core.Calibration;
using Akeno.ColorVision.Core.Profiles;
using Akeno.ColorVision.Windows;
using Microsoft.Win32;

namespace Akeno.ColorVision.App.ViewModels;

public enum CalibrationStep
{
    Intro,
    Describe,
    Pairs,
    Compare,
    Gaming,
    Save,
    Done,
}

public sealed class DescriptionOptionViewModel(SelfDescriptionOption option) : ObservableObject
{
    private bool _isSelected;

    public SelfDescriptionOption Option { get; } = option;

    public string Text => Option.Text;

    public string Detail => Option.Detail;

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}

public sealed class CandidateViewModel(CalibrationCandidate candidate, string letter, BitmapSource preview) : ObservableObject
{
    public CalibrationCandidate Candidate { get; } = candidate;

    public string Letter { get; } = letter;

    public string Title => $"Option {Letter}: {Candidate.Label}";

    public string Description => Candidate.Description;

    public string StrengthText => $"Strength {Candidate.Intensity * 100:0} %";

    public BitmapSource Preview { get; } = preview;
}

/// <summary>The guided calibration wizard. Not a medical test: it collects the user's own judgements.</summary>
public sealed class CalibrationViewModel : PageViewModel
{
    private readonly MainViewModel _main;
    private readonly Dictionary<string, PairRating> _ratings = [];
    private readonly IReadOnlyList<ColorPair> _pairs;
    private readonly DispatcherTimer _previewTimer;
    private CalibrationStep _step = CalibrationStep.Intro;
    private int _pairIndex;
    private CalibrationInsight? _insight;
    private int _round;
    private CalibrationCandidate? _chosen;
    private BitmapSource _pattern;
    private BitmapSource _scene;
    private int _previewSecondsLeft;
    private string _profileName = "My ColorVision Profile";
    private bool _turnOnNow = true;
    private bool _enableOnStartup;
    private bool _useProfileForMinecraft;

    public CalibrationViewModel(AppHost host, MainViewModel main)
        : base(host)
    {
        _main = main;
        _pairs = OrderPairs(TestColorSets.CalibrationPairs);
        _pattern = TestPatterns.CreateComparisonPattern([]);
        _scene = TestPatterns.CreateNetherInspiredScene();
        DescriptionOptions = new ObservableCollection<DescriptionOptionViewModel>(CalibrationQuestionnaire.Options.Select(o => new DescriptionOptionViewModel(o)));

        StartCommand = new RelayCommand(Start);
        CancelCommand = new RelayCommand(Cancel);
        NextFromDescribeCommand = new RelayCommand(() => GoTo(CalibrationStep.Pairs), () => DescriptionOptions.Any(o => o.IsSelected));
        RateCommand = new RelayCommand(p => Rate(p));
        PairBackCommand = new RelayCommand(PairBack);
        ChooseCommand = new RelayCommand(p => Choose(p as CandidateViewModel));
        TryOnScreenCommand = new RelayCommand(p => TryOnScreen(p as CandidateViewModel), _ => _previewSecondsLeft == 0);
        StopPreviewCommand = new RelayCommand(StopPreview);
        ImportScreenshotCommand = new RelayCommand(ImportScreenshot);
        UseDefaultSceneCommand = new RelayCommand(() =>
        {
            _scene = TestPatterns.CreateNetherInspiredScene();
            RefreshScenes();
        });
        ToSaveCommand = new RelayCommand(() => GoTo(CalibrationStep.Save));
        SaveCommand = new RelayCommand(Save, () => !string.IsNullOrWhiteSpace(ProfileName));
        GoHomeCommand = new RelayCommand(() =>
        {
            GoTo(CalibrationStep.Intro);
            _main.Navigate("home");
        });
        RestartCommand = new RelayCommand(() => GoTo(CalibrationStep.Intro));

        _previewTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _previewTimer.Tick += (_, _) =>
        {
            _previewSecondsLeft--;
            OnPropertyChanged(nameof(PreviewCountdownText));
            if (_previewSecondsLeft <= 0)
            {
                StopPreview();
            }
        };
    }

    // ---------------------------------------------------------------- step state

    public CalibrationStep Step
    {
        get => _step;
        private set
        {
            if (SetProperty(ref _step, value))
            {
                OnPropertiesChanged(nameof(IsIntro), nameof(IsDescribe), nameof(IsPairs), nameof(IsCompare), nameof(IsGaming),
                    nameof(IsSave), nameof(IsDone), nameof(StepText), nameof(InProgress));
            }
        }
    }

    public bool IsIntro => Step == CalibrationStep.Intro;

    public bool IsDescribe => Step == CalibrationStep.Describe;

    public bool IsPairs => Step == CalibrationStep.Pairs;

    public bool IsCompare => Step == CalibrationStep.Compare;

    public bool IsGaming => Step == CalibrationStep.Gaming;

    public bool IsSave => Step == CalibrationStep.Save;

    public bool IsDone => Step == CalibrationStep.Done;

    public bool InProgress => Step is not (CalibrationStep.Intro or CalibrationStep.Done);

    public string StepText => Step switch
    {
        CalibrationStep.Describe => "Step 1 of 5 — Your experience",
        CalibrationStep.Pairs => "Step 2 of 5 — Compare color pairs",
        CalibrationStep.Compare => "Step 3 of 5 — Choose what works best",
        CalibrationStep.Gaming => "Step 4 of 5 — Game-style colors",
        CalibrationStep.Save => "Step 5 of 5 — Save your profile",
        _ => string.Empty,
    };

    // ---------------------------------------------------------------- intro

    public IReadOnlyList<string> ConditionNotes { get; private set; } = [];

    // ---------------------------------------------------------------- step 1

    public ObservableCollection<DescriptionOptionViewModel> DescriptionOptions { get; }

    // ---------------------------------------------------------------- step 2

    public ColorPair CurrentPair => _pairs[Math.Clamp(_pairIndex, 0, _pairs.Count - 1)];

    public Brush FirstBrush => CurrentPair.First.Color.ToBrush();

    public Brush SecondBrush => CurrentPair.Second.Color.ToBrush();

    public string PairProgressText => $"Pair {_pairIndex + 1} of {_pairs.Count}";

    public double PairProgress => (_pairIndex + 1) / (double)_pairs.Count;

    // ---------------------------------------------------------------- step 3

    public string InsightSummary => _insight?.Summary ?? string.Empty;

    public string RoundTitle => _round switch
    {
        0 => "Which option makes the color pairs easiest to tell apart?",
        1 => "How strong should the correction be?",
        _ => "Fine-tune: which one is easiest?",
    };

    public string RoundHint => _round switch
    {
        0 => "Don't worry about colors looking unnatural — pick the one where the squares in each pair look most different from each other.",
        1 => "Pick the gentlest option that still makes the pairs easy to tell apart.",
        _ => "Last step of the comparison. Choose the one you like best.",
    };

    public string RoundProgress => $"Comparison {_round + 1} of 3";

    public BitmapSource OriginalPattern => _pattern;

    public ObservableCollection<CandidateViewModel> Candidates { get; } = [];

    public string? PreviewCountdownText => _previewSecondsLeft > 0 ? $"Showing on your whole screen… returning in {_previewSecondsLeft} s" : null;

    // ---------------------------------------------------------------- step 4

    public BitmapSource SceneOriginal => _scene;

    public BitmapSource? SceneProfile { get; private set; }

    public BitmapSource? ScenePreset { get; private set; }

    public IReadOnlyList<SceneLegendItem> SceneLegend => TestPatterns.NetherSceneLegend;

    public bool UseProfileForMinecraft
    {
        get => _useProfileForMinecraft;
        set
        {
            if (SetProperty(ref _useProfileForMinecraft, value))
            {
                OnPropertyChanged(nameof(UsePresetForMinecraft));
            }
        }
    }

    public bool UsePresetForMinecraft
    {
        get => !_useProfileForMinecraft;
        set => UseProfileForMinecraft = !value;
    }

    // ---------------------------------------------------------------- step 5

    public string ProfileName
    {
        get => _profileName;
        set => SetProperty(ref _profileName, value);
    }

    public bool TurnOnNow
    {
        get => _turnOnNow;
        set => SetProperty(ref _turnOnNow, value);
    }

    public bool EnableOnStartup
    {
        get => _enableOnStartup;
        set => SetProperty(ref _enableOnStartup, value);
    }

    public string ChoiceSummary => _chosen is null
        ? string.Empty
        : $"You chose \"{_chosen.Label}\" ({DescribeMode(_chosen.Mode)}) at {_chosen.Intensity * 100:0} % strength.";

    public string? SavedMessage { get; private set; }

    // ---------------------------------------------------------------- commands

    public ICommand StartCommand { get; }

    public ICommand CancelCommand { get; }

    public ICommand NextFromDescribeCommand { get; }

    public ICommand RateCommand { get; }

    public ICommand PairBackCommand { get; }

    public ICommand ChooseCommand { get; }

    public ICommand TryOnScreenCommand { get; }

    public ICommand StopPreviewCommand { get; }

    public ICommand ImportScreenshotCommand { get; }

    public ICommand UseDefaultSceneCommand { get; }

    public ICommand ToSaveCommand { get; }

    public ICommand SaveCommand { get; }

    public ICommand GoHomeCommand { get; }

    public ICommand RestartCommand { get; }

    public override void OnNavigatedTo()
    {
        ConditionNotes = BuildConditionNotes();
        OnPropertyChanged(nameof(ConditionNotes));
        if (InProgress)
        {
            Host.Filter.Suspend();
        }
    }

    public override void OnNavigatedFrom()
    {
        StopPreview();
        if (InProgress)
        {
            // Leaving mid-way: give the user their normal filter back; progress is kept.
            Host.Filter.Resume();
        }
    }

    // ---------------------------------------------------------------- logic

    private void Start()
    {
        _ratings.Clear();
        _pairIndex = 0;
        _round = 0;
        _chosen = null;
        _insight = null;
        SavedMessage = null;
        foreach (var o in DescriptionOptions)
        {
            o.IsSelected = false;
        }

        Host.Filter.Suspend();
        GoTo(CalibrationStep.Describe);
    }

    private void Cancel()
    {
        StopPreview();
        Host.Filter.Resume();
        GoTo(CalibrationStep.Intro);
    }

    private void GoTo(CalibrationStep step)
    {
        if (step == CalibrationStep.Pairs)
        {
            _pairIndex = 0;
            RefreshPair();
        }

        Step = step;
    }

    private void Rate(object? parameter)
    {
        if (!int.TryParse(parameter?.ToString(), out var value))
        {
            return;
        }

        _ratings[CurrentPair.Id] = (PairRating)Math.Clamp(value, 0, 3);
        if (_pairIndex < _pairs.Count - 1)
        {
            _pairIndex++;
            RefreshPair();
            return;
        }

        var description = DescriptionOptions.FirstOrDefault(o => o.IsSelected)?.Option.Value;
        _insight = CalibrationAnalyzer.Analyze(_ratings, description);
        _pattern = TestPatterns.CreateComparisonPattern(_insight.DifficultPairs.Count >= 3
            ? _insight.DifficultPairs
            : [.. _insight.DifficultPairs, .. TestColorSets.CalibrationPairs.Where(p => !p.IsControl && !_insight.DifficultPairs.Contains(p))]);
        _round = 0;
        ShowCandidates(CalibrationCandidates.StrategyRound(_insight));
        OnPropertiesChanged(nameof(InsightSummary), nameof(OriginalPattern));
        GoTo(CalibrationStep.Compare);
    }

    private void PairBack()
    {
        if (_pairIndex > 0)
        {
            _pairIndex--;
            RefreshPair();
        }
        else
        {
            GoTo(CalibrationStep.Describe);
        }
    }

    private void RefreshPair() =>
        OnPropertiesChanged(nameof(CurrentPair), nameof(FirstBrush), nameof(SecondBrush), nameof(PairProgressText), nameof(PairProgress));

    private void ShowCandidates(IReadOnlyList<CalibrationCandidate> candidates)
    {
        Candidates.Clear();
        var letters = "ABCD";
        for (var i = 0; i < candidates.Count; i++)
        {
            Candidates.Add(new CandidateViewModel(candidates[i], letters[i].ToString(), ImageTransform.Apply(_pattern, candidates[i].BuildMatrix())));
        }

        OnPropertiesChanged(nameof(RoundTitle), nameof(RoundHint), nameof(RoundProgress));
    }

    private void Choose(CandidateViewModel? candidate)
    {
        if (candidate is null)
        {
            return;
        }

        StopPreview();
        _chosen = candidate.Candidate;
        _round++;
        if (_round == 1)
        {
            ShowCandidates(CalibrationCandidates.StrengthRound(_chosen));
        }
        else if (_round == 2)
        {
            ShowCandidates(CalibrationCandidates.RefineRound(_chosen));
        }
        else
        {
            UseProfileForMinecraft = _chosen.Mode == CorrectionMode.StrongSeparation;
            RefreshScenes();
            GoTo(CalibrationStep.Gaming);
        }
    }

    private void TryOnScreen(CandidateViewModel? candidate)
    {
        if (candidate is null)
        {
            return;
        }

        var result = Host.Filter.ShowTemporary(candidate.Candidate.BuildMatrix());
        if (!result.Success)
        {
            return;
        }

        _previewSecondsLeft = 10;
        _previewTimer.Start();
        OnPropertyChanged(nameof(PreviewCountdownText));
    }

    private void StopPreview()
    {
        if (_previewSecondsLeft <= 0 && !_previewTimer.IsEnabled)
        {
            return;
        }

        _previewTimer.Stop();
        _previewSecondsLeft = 0;
        Host.Filter.EndTemporary(filterOn: false);
        OnPropertyChanged(nameof(PreviewCountdownText));
    }

    private void RefreshScenes()
    {
        SceneProfile = _chosen is null ? null : ImageTransform.Apply(_scene, _chosen.BuildMatrix());
        ScenePreset = ImageTransform.Apply(_scene, BuiltInPresets.MinecraftNether.BuildMatrix());
        OnPropertiesChanged(nameof(SceneOriginal), nameof(SceneProfile), nameof(ScenePreset));
    }

    private void ImportScreenshot()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose a screenshot (it stays on this PC)",
            Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp|All files|*.*",
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            _scene = ImageTransform.LoadForPreview(dialog.FileName, 900);
            RefreshScenes();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show("That image could not be opened: " + ex.Message, "Akeno ColorVision");
        }
    }

    private void Save()
    {
        if (_chosen is null)
        {
            return;
        }

        var description = DescriptionOptions.FirstOrDefault(o => o.IsSelected)?.Option;
        var record = new CalibrationRecord
        {
            CompletedUtc = DateTimeOffset.UtcNow,
            SelfDescription = description?.Text,
            PairRatings = _ratings.ToDictionary(kv => kv.Key, kv => (int)kv.Value),
            SuggestedStartingPoint = _insight?.SuggestedMode,
            ChosenOption = _chosen.Label,
            ConditionNotes = [.. ConditionNotes],
        };

        var profile = CalibrationCandidates.CreateProfile(ProfileName, _chosen, record);
        Host.Profiles.Add(profile);
        Host.Profiles.SetActive(profile.Id);
        if (UseProfileForMinecraft)
        {
            Host.Settings.Gaming.MinecraftProfileId = profile.Id;
        }

        Host.Settings.EnableFilterOnStartup = EnableOnStartup || Host.Settings.EnableFilterOnStartup;
        Host.Settings.HasCompletedFirstRun = true;
        Host.SaveSettings();
        Host.Filter.Resume(forceOn: TurnOnNow);

        SavedMessage = $"Saved \"{profile.Name}\" and made it your active profile." +
                       (TurnOnNow ? " The filter is on." : " Turn the filter on from Home whenever you like.");
        OnPropertiesChanged(nameof(SavedMessage), nameof(ChoiceSummary));
        GoTo(CalibrationStep.Done);
    }

    private List<string> BuildConditionNotes()
    {
        var notes = new List<string>
        {
            "This is not a medical test. It only finds settings that work for you on this screen.",
            "If Night light is on (Windows Settings → Display), turn it off during calibration — it tints the screen.",
        };
        try
        {
            var env = EnvironmentInspector.Inspect();
            if (env.AdvancedColorEnabled == true)
            {
                notes.Add("HDR / advanced color is on. Colors can look different; results are most reliable with HDR off.");
            }

            if (env.WindowsColorFiltersActive == true)
            {
                notes.Add("Windows' own color filter is on. Please turn it off during calibration.");
            }
        }
        catch (Exception)
        {
            // Environment checks are optional.
        }

        notes.Add(Host.Filter.IsOn
            ? "Your filter will be paused while you calibrate, so you see the original colors."
            : "The filter stays off while you calibrate.");
        return notes;
    }

    private static List<ColorPair> OrderPairs(IReadOnlyList<ColorPair> pairs)
    {
        // Spread the two control pairs through the sequence.
        var normal = pairs.Where(p => !p.IsControl).ToList();
        var controls = pairs.Where(p => p.IsControl).ToList();
        if (controls.Count > 0 && normal.Count > 4)
        {
            normal.Insert(4, controls[0]);
        }

        if (controls.Count > 1)
        {
            normal.Insert(Math.Min(10, normal.Count), controls[1]);
        }

        return normal;
    }

    private static string DescribeMode(CorrectionMode mode) => mode switch
    {
        CorrectionMode.ProtanAssistance => "red-focused daltonization",
        CorrectionMode.DeutanAssistance => "green-focused daltonization",
        CorrectionMode.StrongSeparation => "strong separation",
        _ => "balanced red-green assistance",
    };
}
