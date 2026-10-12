using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Media;
using Akeno.ColorVision.App.Imaging;
using Akeno.ColorVision.App.Infrastructure;
using Akeno.ColorVision.App.Services;
using Akeno.ColorVision.Core.Settings;
using Akeno.ColorVision.Windows;

namespace Akeno.ColorVision.App.ViewModels;

/// <summary>Color Identifier page.</summary>
public sealed class IdentifierViewModel : PageViewModel
{
    private ColorSampleResult? _last;
    private string? _countdown;
    private string? _checkMessage;
    private bool _busy;

    public IdentifierViewModel(AppHost host)
        : base(host)
    {
        SampleSoonCommand = new RelayCommand(async () => await SampleSoonAsync(), () => !_busy);
        ToggleLiveCommand = new RelayCommand(ToggleLive);
        RunCheckCommand = new RelayCommand(async () => await RunCheckAsync(), () => !_busy && Host.Filter.IsOn);
        ClearHistoryCommand = new RelayCommand(() =>
        {
            History.Clear();
            OnPropertyChanged(nameof(HasHistory));
        });
        Subscribe();
    }

    public string ShortcutText => Host.Settings.Hotkeys.IdentifyColor;

    public ColorSampleResult? Last
    {
        get => _last;
        private set
        {
            if (SetProperty(ref _last, value))
            {
                OnPropertiesChanged(nameof(HasResult), nameof(SwatchBrush), nameof(ColorName), nameof(Category), nameof(Summary), nameof(Hex), nameof(RgbText),
                    nameof(SourceLabel), nameof(BlockedWarning));
            }
        }
    }

    public bool HasResult => _last is not null;

    // The swatch is shown through the filter, so the original color makes it look like the sampled spot.
    public Brush SwatchBrush => _last?.ReportedColor.ToBrush() ?? Brushes.Transparent;

    public string ColorName => _last?.Description.Name ?? string.Empty;

    public string Category => _last is null ? string.Empty : $"Category: {_last.Description.Category}";

    public string Summary => _last?.Description.Summary ?? string.Empty;

    public string Hex => _last is null ? string.Empty : $"HEX {_last.Description.Hex}";

    public string RgbText => _last?.RgbText ?? string.Empty;

    public string SourceLabel => _last?.SourceLabel ?? string.Empty;

    public string? BlockedWarning => _last?.LooksBlocked == true
        ? "This spot reads as pure black. It may be protected content (for example some streaming video) that cannot be sampled."
        : null;

    public ObservableCollection<ColorSampleResult> History { get; } = [];

    public bool HasHistory => History.Count > 0;

    public string? Countdown
    {
        get => _countdown;
        private set => SetProperty(ref _countdown, value);
    }

    public bool IsLive => Host.IdentifierUi?.IsLive == true;

    public string LiveButtonText => IsLive ? "Stop live identifier" : "Start live identifier";

    public string SamplingStatus
    {
        get
        {
            var checkedAt = Host.Settings.Identifier.SamplingCheckedUtc is { } t ? $" (checked {t.ToLocalTime():d})" : string.Empty;
            return Host.Settings.Identifier.MeasuredSamplingSource switch
            {
                SamplingSource.OriginalColors => "On this PC the identifier reads the original colors, from before the filter" + checkedAt + ". Names always describe the original color.",
                SamplingSource.FilteredColors => "On this PC the identifier reads filtered colors" + checkedAt + ". The app converts them back to estimate the original color and marks those results as estimates.",
                _ => "Not checked yet. With the filter off, the identifier always reads original colors. With the filter on, the app runs a quick check the first time to find out what Windows returns.",
            };
        }
    }

    public string? CheckMessage
    {
        get => _checkMessage;
        private set => SetProperty(ref _checkMessage, value);
    }

    public ICommand SampleSoonCommand { get; }

    public ICommand ToggleLiveCommand { get; }

    public ICommand RunCheckCommand { get; }

    public ICommand ClearHistoryCommand { get; }

    public override void OnNavigatedTo()
    {
        Subscribe();
        OnPropertiesChanged(nameof(ShortcutText), nameof(SamplingStatus), nameof(IsLive), nameof(LiveButtonText));
    }

    private void Subscribe()
    {
        if (Host.IdentifierUi is { } ui)
        {
            ui.SampleTaken -= OnSample;
            ui.SampleTaken += OnSample;
            ui.LiveStateChanged -= OnLiveChanged;
            ui.LiveStateChanged += OnLiveChanged;
        }
    }

    private void OnSample(object? sender, ColorSampleResult result)
    {
        Last = result;
        History.Insert(0, result);
        while (History.Count > 8)
        {
            History.RemoveAt(History.Count - 1);
        }

        OnPropertiesChanged(nameof(SamplingStatus), nameof(HasHistory));
    }

    private void OnLiveChanged(object? sender, EventArgs e) => OnPropertiesChanged(nameof(IsLive), nameof(LiveButtonText));

    private async Task SampleSoonAsync()
    {
        _busy = true;
        try
        {
            for (var i = 3; i > 0; i--)
            {
                Countdown = $"Move the mouse pointer over the color… {i}";
                await Task.Delay(1000);
            }

            Countdown = null;
            await Host.IdentifierUi.IdentifyAtCursorAsync();
        }
        finally
        {
            _busy = false;
            Countdown = null;
        }
    }

    private void ToggleLive()
    {
        if (Host.IdentifierUi.IsLive)
        {
            Host.IdentifierUi.StopLive();
        }
        else
        {
            Host.IdentifierUi.StartLive();
        }
    }

    private async Task RunCheckAsync()
    {
        _busy = true;
        try
        {
            CheckMessage = "Checking… (a small colored square appears for a moment)";
            var verdict = await Host.Identifier.RunSamplingCheckAsync();
            CheckMessage = verdict switch
            {
                SamplingVerdict.ReadsOriginal => "Done: the identifier reads original colors on this PC.",
                SamplingVerdict.ReadsFiltered => "Done: the identifier reads filtered colors on this PC; results will be converted back and marked as estimates.",
                _ => "The check was inconclusive. Try again with a stronger profile, or with the filter off (then colors are always original).",
            };
            OnPropertyChanged(nameof(SamplingStatus));
        }
        finally
        {
            _busy = false;
        }
    }
}
