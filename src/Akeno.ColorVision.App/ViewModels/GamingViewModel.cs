using System.Collections.ObjectModel;
using System.Windows.Input;
using Akeno.ColorVision.App.Infrastructure;
using Akeno.ColorVision.Core.Diagnostics;
using Akeno.ColorVision.Core.Gaming;
using Akeno.ColorVision.Core.Profiles;
using Akeno.ColorVision.Windows;

namespace Akeno.ColorVision.App.ViewModels;

public sealed class GameRuleViewModel : ObservableObject
{
    private readonly AppHost _host;
    private readonly Action _changed;

    public GameRuleViewModel(AppHost host, GameRule rule, Action changed)
    {
        _host = host;
        Rule = rule;
        _changed = changed;
        RemoveCommand = new RelayCommand(Remove, () => !Rule.BuiltIn);
    }

    public GameRule Rule { get; }

    public string Name => Rule.Name;

    public string Detail
    {
        get
        {
            var exe = string.Join(" or ", Rule.ExecutableNames);
            var title = Rule.TitleStartsWith is { Length: > 0 } t ? $", window title starts with \"{t}\"" : string.Empty;
            var profile = Rule.ProfileId is { } id ? _host.Profiles.Find(id)?.Name : null;
            return $"{(Rule.BuiltIn ? "Built-in" : "Your rule")}: {exe}{title}. Profile: {profile ?? "the Minecraft profile below"}.";
        }
    }

    public bool IsBuiltIn => Rule.BuiltIn;

    public bool Enabled
    {
        get => Rule.Enabled;
        set
        {
            Rule.Enabled = value;
            if (Rule.BuiltIn)
            {
                var disabled = _host.Settings.Gaming.DisabledBuiltInRules;
                disabled.Remove(Rule.Id);
                if (!value)
                {
                    disabled.Add(Rule.Id);
                }
            }

            _host.SaveSettings();
            OnPropertyChanged();
        }
    }

    public ICommand RemoveCommand { get; }

    private void Remove()
    {
        _host.Settings.Gaming.CustomRules.RemoveAll(r => r.Id == Rule.Id);
        _host.SaveSettings();
        _changed();
    }
}

/// <summary>Gaming page: Minecraft profile, automatic switching and custom game rules.</summary>
public sealed class GamingViewModel : PageViewModel
{
    private ForegroundAppInfo? _selectedApp;

    public GamingViewModel(AppHost host)
        : base(host)
    {
        RefreshAppsCommand = new RelayCommand(RefreshApps);
        AddRuleCommand = new RelayCommand(AddRule, () => _selectedApp is not null);
        UseNowCommand = new RelayCommand(() =>
        {
            Host.Profiles.SetActive(Host.Settings.Gaming.MinecraftProfileId);
            if (!Host.Filter.IsOn)
            {
                Host.ToggleFilterFromUser();
            }
        });
        Host.Games.GameStateChanged += (_, _) => OnPropertiesChanged(nameof(DetectionStatus));
        Host.Profiles.ProfilesChanged += (_, _) => OnPropertiesChanged(nameof(Profiles), nameof(MinecraftProfile));
        BuildRules();
    }

    public bool AutoSwitch
    {
        get => Host.Settings.Gaming.AutoSwitchProfiles;
        set
        {
            Host.Settings.Gaming.AutoSwitchProfiles = value;
            Host.SaveSettings();
            Host.Games.UpdateRunningState();
            OnPropertiesChanged(nameof(AutoSwitch), nameof(DetectionStatus));
        }
    }

    public bool EnableFilterWhileGaming
    {
        get => Host.Settings.Gaming.EnableFilterWhileGaming;
        set
        {
            Host.Settings.Gaming.EnableFilterWhileGaming = value;
            Host.SaveSettings();
            OnPropertyChanged();
        }
    }

    public bool QuietWhileGaming
    {
        get => Host.Settings.Gaming.QuietWhileGaming;
        set
        {
            Host.Settings.Gaming.QuietWhileGaming = value;
            Host.SaveSettings();
            OnPropertyChanged();
        }
    }

    public ObservableCollection<ColorProfile> Profiles => Host.Profiles.Profiles;

    public ColorProfile? MinecraftProfile
    {
        get => Host.Profiles.Find(Host.Settings.Gaming.MinecraftProfileId) ?? Host.Profiles.Find(BuiltInPresets.MinecraftNetherId);
        set
        {
            if (value is null)
            {
                return;
            }

            Host.Settings.Gaming.MinecraftProfileId = value.Id;
            Host.SaveSettings();
            OnPropertyChanged();
            foreach (var rule in Rules)
            {
                rule.OnPropertyChangedPublic();
            }
        }
    }

    public string DetectionStatus
    {
        get
        {
            if (!Host.Settings.Gaming.AutoSwitchProfiles)
            {
                return "Automatic switching is off.";
            }

            return Host.Games.InGame
                ? $"Detected: {Host.Games.CurrentGameName}. Using \"{Host.Profiles.Active.Name}\"."
                : "Watching for Minecraft. No game detected right now.";
        }
    }

    public ObservableCollection<GameRuleViewModelProxy> Rules { get; } = [];

    public ObservableCollection<ForegroundAppInfo> RunningApps { get; } = [];

    public ForegroundAppInfo? SelectedApp
    {
        get => _selectedApp;
        set => SetProperty(ref _selectedApp, value);
    }

    public ICommand RefreshAppsCommand { get; }

    public ICommand AddRuleCommand { get; }

    public ICommand UseNowCommand { get; }

    public override void OnNavigatedTo()
    {
        BuildRules();
        OnPropertiesChanged(nameof(DetectionStatus), nameof(MinecraftProfile));
    }

    private void BuildRules()
    {
        Rules.Clear();
        foreach (var rule in Host.Games.Rules)
        {
            Rules.Add(new GameRuleViewModelProxy(new GameRuleViewModel(Host, rule, BuildRules)));
        }
    }

    private void RefreshApps()
    {
        RunningApps.Clear();
        try
        {
            foreach (var app in ForegroundWindowMonitor.ListVisibleApps().Where(a => a.ProcessId != Environment.ProcessId))
            {
                RunningApps.Add(app);
            }
        }
        catch (Exception ex)
        {
            Host.Log.Warn("Could not list running apps: " + ex.Message);
        }
    }

    private void AddRule()
    {
        if (_selectedApp is null)
        {
            return;
        }

        var exe = _selectedApp.ExecutableName;
        if (Host.Settings.Gaming.CustomRules.Any(r => r.ExecutableNames.Contains(exe, StringComparer.OrdinalIgnoreCase)))
        {
            return;
        }

        Host.Settings.Gaming.CustomRules.Add(new GameRule
        {
            Name = System.IO.Path.GetFileNameWithoutExtension(exe),
            ExecutableNames = [exe],
            ProfileId = Host.Settings.Gaming.MinecraftProfileId,
        });
        Host.SaveSettings();
        BuildRules();
    }
}

/// <summary>Exposes <see cref="GameRuleViewModel"/> members for binding and allows refreshing its text.</summary>
public sealed class GameRuleViewModelProxy(GameRuleViewModel inner) : ObservableObject
{
    public GameRuleViewModel Inner { get; } = inner;

    public string Name => Inner.Name;

    public string Detail => Inner.Detail;

    public bool IsBuiltIn => Inner.IsBuiltIn;

    public bool Enabled
    {
        get => Inner.Enabled;
        set
        {
            Inner.Enabled = value;
            OnPropertyChanged();
        }
    }

    public ICommand RemoveCommand => Inner.RemoveCommand;

    public void OnPropertyChangedPublic() => OnPropertiesChanged(nameof(Detail));
}
