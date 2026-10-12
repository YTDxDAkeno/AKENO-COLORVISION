using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Input;
using Akeno.ColorVision.App.Infrastructure;
using Akeno.ColorVision.Core.Recovery;
using Akeno.ColorVision.Windows;

namespace Akeno.ColorVision.App.ViewModels;

/// <summary>Base class for pages shown in the main window.</summary>
public abstract class PageViewModel : ObservableObject
{
    protected PageViewModel(AppHost host) => Host = host;

    protected AppHost Host { get; }

    public virtual void OnNavigatedTo()
    {
    }

    public virtual void OnNavigatedFrom()
    {
    }
}

public sealed record NavItem(string Key, string Title, string Glyph, PageViewModel Page);

/// <summary>A dismissible message shown at the top of the Home page.</summary>
public sealed class NoticeViewModel : ObservableObject
{
    public NoticeViewModel(string title, string message, bool isWarning, string? actionText = null, Action? action = null)
    {
        Title = title;
        Message = message;
        IsWarning = isWarning;
        ActionText = actionText;
        ActionCommand = new RelayCommand(() => action?.Invoke(), () => action is not null);
    }

    public string Title { get; }

    public string Message { get; }

    public bool IsWarning { get; }

    /// <summary>Text label for the icon so the notice type is not conveyed by color alone.</summary>
    public string Kind => IsWarning ? "Warning" : "Note";

    public string Glyph => IsWarning ? "" : "";

    public string? ActionText { get; }

    public ICommand ActionCommand { get; }

    public ICommand? DismissCommand { get; set; }
}

/// <summary>Navigation, global status and notices.</summary>
public sealed class MainViewModel : ObservableObject
{
    private readonly AppHost _host;
    private NavItem _selected;

    public MainViewModel(AppHost host)
    {
        _host = host;
        Home = new HomeViewModel(host, this);
        Filters = new FiltersViewModel(host);
        Calibration = new CalibrationViewModel(host, this);
        Gaming = new GamingViewModel(host);
        Identifier = new IdentifierViewModel(host);
        ProfilesPage = new ProfilesViewModel(host);
        Settings = new SettingsViewModel(host);
        About = new AboutViewModel(host);

        NavItems =
        [
            new("home", "Home", "", Home),
            new("filters", "Color Filters", "", Filters),
            new("calibration", "Calibration", "", Calibration),
            new("gaming", "Gaming", "", Gaming),
            new("identifier", "Color Identifier", "", Identifier),
            new("profiles", "Profiles", "", ProfilesPage),
            new("settings", "Settings", "", Settings),
            new("about", "About", "", About),
        ];
        _selected = NavItems[0];

        ToggleFilterCommand = new RelayCommand(() => _host.ToggleFilterFromUser());
        EmergencyResetCommand = new RelayCommand(() => _host.Filter.EmergencyReset());

        _host.Filter.StatusChanged += (_, _) => RefreshStatus();
        _host.Profiles.ActiveProfileChanged += (_, _) => RefreshStatus();
        _host.Games.GameStateChanged += (_, _) => RefreshStatus();
        BuildStartupNotices();
    }

    public HomeViewModel Home { get; }

    public FiltersViewModel Filters { get; }

    public CalibrationViewModel Calibration { get; }

    public GamingViewModel Gaming { get; }

    public IdentifierViewModel Identifier { get; }

    public ProfilesViewModel ProfilesPage { get; }

    public SettingsViewModel Settings { get; }

    public AboutViewModel About { get; }

    public IReadOnlyList<NavItem> NavItems { get; }

    public ObservableCollection<NoticeViewModel> Notices { get; } = [];

    public NavItem SelectedNav
    {
        get => _selected;
        set
        {
            if (value is null || ReferenceEquals(value, _selected))
            {
                return;
            }

            _selected.Page.OnNavigatedFrom();
            _selected = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CurrentPage));
            _selected.Page.OnNavigatedTo();
        }
    }

    public PageViewModel CurrentPage => _selected.Page;

    public bool FilterOn => _host.Filter.IsOn;

    public string FilterStateText => _host.Filter.IsSuspended ? "Paused" : FilterOn ? "ON" : "OFF";

    public string ToggleText => FilterOn ? "Turn filter off" : "Turn filter on";

    public string ActiveProfileName => _host.Profiles.Active.Name;

    public string StatusMessage => _host.Filter.StatusMessage;

    public string? GameText => _host.Games.InGame ? $"Game profile active: {_host.Games.CurrentGameName}" : null;

    public ICommand ToggleFilterCommand { get; }

    public ICommand EmergencyResetCommand { get; }

    public string EmergencyShortcut => _host.Settings.Hotkeys.EmergencyReset;

    public void Navigate(string key)
    {
        var item = NavItems.FirstOrDefault(n => n.Key == key);
        if (item is not null)
        {
            SelectedNav = item;
        }
    }

    public void AddNotice(NoticeViewModel notice)
    {
        notice.DismissCommand = new RelayCommand(() => Notices.Remove(notice));
        Notices.Add(notice);
    }

    private void RefreshStatus()
    {
        OnPropertiesChanged(nameof(FilterOn), nameof(FilterStateText), nameof(ToggleText), nameof(ActiveProfileName), nameof(StatusMessage), nameof(GameText));
        Home.Refresh();
    }

    private void BuildStartupNotices()
    {
        var session = _host.Session;
        if (session.SafeMode)
        {
            var reasons = new List<string>();
            if (session.Reasons.HasFlag(SafeModeReason.UncleanShutdown))
            {
                reasons.Add("the app did not close normally last time");
            }

            if (session.Reasons.HasFlag(SafeModeReason.EmergencyResetUsed))
            {
                reasons.Add("the emergency reset was used");
            }

            if (session.Reasons.HasFlag(SafeModeReason.Requested))
            {
                reasons.Add("safe mode was requested");
            }

            AddNotice(new NoticeViewModel(
                "Safe mode",
                $"The filter was not turned on automatically because {string.Join(" and ", reasons)}. Everything else works normally." +
                (session.LastRecoveryNote is { Length: > 0 } note ? $" ({note})" : string.Empty),
                isWarning: true, "Turn filter on", () => _host.Filter.Enable()));
        }

        foreach (var problem in _host.StartupProblems)
        {
            AddNotice(new NoticeViewModel("Something needs attention", problem, isWarning: true));
        }

        if (_host.Filter.ForeignEffectDetected)
        {
            AddNotice(new NoticeViewModel(
                "Another color effect was active",
                "Another program (for example Windows Magnifier with inverted colors) was already changing the screen colors. Turning on Akeno ColorVision replaces that effect while the filter is on.",
                isWarning: true));
        }

        DisplayEnvironment env;
        try
        {
            env = EnvironmentInspector.Inspect();
        }
        catch (Exception)
        {
            return;
        }

        if (env.WindowsColorFiltersActive == true)
        {
            AddNotice(new NoticeViewModel(
                "Windows color filters are also on",
                $"Windows' own color filter ({env.WindowsColorFilterName ?? "unknown type"}) is active. It stacks with Akeno ColorVision and can make colors confusing. Consider turning one of them off.",
                isWarning: true, "Open Windows color filter settings", () => OpenUri("ms-settings:easeofaccess-colorfilter")));
        }

        if (env.MagnifierRunning)
        {
            AddNotice(new NoticeViewModel(
                "Windows Magnifier is running",
                "Magnifier can use the same screen color feature (for example \"Invert colors\"). If colors look wrong, close Magnifier or turn off its color inversion.",
                isWarning: false));
        }

        if (env.AdvancedColorEnabled == true)
        {
            AddNotice(new NoticeViewModel(
                "HDR / advanced color is on",
                "With HDR or automatic color management turned on, colors and the filter can look different from normal (SDR) mode. If the filter looks too weak, too strong or washed out, try with HDR turned off in Windows display settings.",
                isWarning: false, "Open display settings", () => OpenUri("ms-settings:display")));
        }
    }

    private static void OpenUri(string uri)
    {
        try
        {
            Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
        }
        catch (Exception)
        {
            // Settings app unavailable; nothing else to do.
        }
    }
}
