using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Threading;
using Akeno.ColorVision.App.Infrastructure;
using Akeno.ColorVision.Core.Profiles;

namespace Akeno.ColorVision.App.ViewModels;

/// <summary>A selectable profile card.</summary>
public sealed class ProfileCardViewModel : ObservableObject
{
    private readonly AppHost _host;

    public ProfileCardViewModel(AppHost host, ColorProfile profile)
    {
        _host = host;
        Profile = profile;
        ActivateCommand = new RelayCommand(() => _host.Profiles.SetActive(Profile.Id));
    }

    public ColorProfile Profile { get; }

    public string Name => Profile.Name;

    public string Description => string.IsNullOrWhiteSpace(Profile.Description) ? OriginText : Profile.Description;

    public string OriginText => Profile.Origin switch
    {
        ProfileOrigin.BuiltIn => "Built-in preset",
        ProfileOrigin.Calibration => "Your calibrated profile",
        ProfileOrigin.Imported => "Imported profile",
        _ => "Your custom profile",
    };

    public bool IsActive
    {
        get => _host.Profiles.Active.Id == Profile.Id;
        set
        {
            if (value)
            {
                _host.Profiles.SetActive(Profile.Id);
            }
        }
    }

    public ICommand ActivateCommand { get; }

    public void Refresh() => OnPropertiesChanged(nameof(IsActive), nameof(Name), nameof(Description));
}

public sealed class HomeViewModel : PageViewModel
{
    private readonly MainViewModel _main;
    private readonly DispatcherTimer _persistTimer;

    public HomeViewModel(AppHost host, MainViewModel main)
        : base(host)
    {
        _main = main;
        ToggleCommand = new RelayCommand(() => Host.ToggleFilterFromUser());
        CalibrateCommand = new RelayCommand(() => _main.Navigate("calibration"));
        IdentifyCommand = new RelayCommand(() => _main.Navigate("identifier"));
        FiltersCommand = new RelayCommand(() => _main.Navigate("filters"));
        ResetCommand = new RelayCommand(() => Host.Filter.EmergencyReset());
        _persistTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        _persistTimer.Tick += (_, _) =>
        {
            _persistTimer.Stop();
            Host.Profiles.Persist(Host.Profiles.Active);
        };

        RebuildCards();
        Host.Profiles.ProfilesChanged += (_, _) => RebuildCards();
        Host.Profiles.ActiveProfileEdited += (_, _) => OnPropertiesChanged(nameof(Intensity), nameof(IntensityText));
    }

    public ObservableCollection<NoticeViewModel> Notices => _main.Notices;

    public ObservableCollection<ProfileCardViewModel> Cards { get; } = [];

    public bool FilterOn => Host.Filter.IsOn;

    public string Headline => Host.Filter.IsSuspended
        ? "The filter is paused (calibration)"
        : FilterOn ? "The filter is ON" : "The filter is OFF";

    public string StatusGlyph => FilterOn ? "" : "";

    public string ToggleText => FilterOn ? "Turn filter off" : "Turn filter on";

    public string ShortcutHint => $"Shortcut: {Host.Settings.Hotkeys.ToggleFilter}";

    public string StatusDetail => Host.Filter.State switch
    {
        Core.Filtering.FilterState.Failed => "The filter could not be applied: " + (Host.Filter.LastError ?? "unknown error") + " See About → Diagnostics.",
        _ => FilterOn
            ? "Colors on your whole screen are being adjusted to make confusing colors easier to tell apart."
            : "Your screen shows normal colors. Turn the filter on to use your profile.",
    };

    public string ActiveProfileName => Host.Profiles.Active.Name;

    public string? GameText => Host.Games.InGame ? $"Game profile active because {Host.Games.CurrentGameName} is in front." : null;

    public double Intensity
    {
        get => Host.Profiles.Active.Intensity;
        set
        {
            Host.Profiles.SetIntensity(Host.Profiles.Active, value, persist: false);
            _persistTimer.Stop();
            _persistTimer.Start();
            OnPropertyChanged(nameof(IntensityText));
        }
    }

    public string IntensityText => $"{Intensity * 100:0} %";

    public ICommand ToggleCommand { get; }

    public ICommand CalibrateCommand { get; }

    public ICommand IdentifyCommand { get; }

    public ICommand FiltersCommand { get; }

    public ICommand ResetCommand { get; }

    public string EmergencyShortcut => Host.Settings.Hotkeys.EmergencyReset;

    public bool ShowFirstRunHint => !Host.Settings.HasCompletedFirstRun;

    public void Refresh()
    {
        OnPropertiesChanged(nameof(FilterOn), nameof(Headline), nameof(StatusGlyph), nameof(ToggleText), nameof(StatusDetail),
            nameof(ActiveProfileName), nameof(Intensity), nameof(IntensityText), nameof(GameText), nameof(ShortcutHint), nameof(EmergencyShortcut));
        foreach (var card in Cards)
        {
            card.Refresh();
        }
    }

    public override void OnNavigatedTo() => Refresh();

    private void RebuildCards()
    {
        Cards.Clear();
        foreach (var profile in Host.Profiles.Profiles)
        {
            Cards.Add(new ProfileCardViewModel(Host, profile));
        }
    }
}
