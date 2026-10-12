using System.Windows.Threading;
using Akeno.ColorVision.Core.Diagnostics;
using Akeno.ColorVision.Core.Gaming;
using Akeno.ColorVision.Core.Profiles;
using Akeno.ColorVision.Core.Settings;
using Akeno.ColorVision.Windows;

namespace Akeno.ColorVision.App.Services;

/// <summary>
/// Optional automatic game profiles. Watches the foreground window (exe name + title only) and
/// applies <see cref="AutoProfileSwitcher"/> decisions on the UI thread.
/// </summary>
public sealed class GameDetectionService : IDisposable
{
    private readonly ForegroundWindowMonitor _monitor;
    private readonly AutoProfileSwitcher _switcher = new();
    private readonly ProfileService _profiles;
    private readonly FilterService _filter;
    private readonly AppSettings _settings;
    private readonly IAppLogger _log;
    private readonly Dispatcher _ui;
    private readonly DispatcherTimer _tick;
    private bool _applying;

    public GameDetectionService(MessagePumpThread pump, ProfileService profiles, FilterService filter, AppSettings settings, IAppLogger log, Dispatcher ui)
    {
        _monitor = new ForegroundWindowMonitor(pump);
        _profiles = profiles;
        _filter = filter;
        _settings = settings;
        _log = log;
        _ui = ui;
        _monitor.ForegroundChanged += info => _ui.BeginInvoke(() => OnForeground(info));
        _profiles.ActiveProfileChanged += (_, e) =>
        {
            if (!e.Automatic && !_applying && _switcher.InGameMode)
            {
                _switcher.OnUserOverride();
                GameStateChanged?.Invoke(this, EventArgs.Empty);
            }
        };
        _tick = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Background, (_, _) => Apply(_switcher.Tick()), ui);
    }

    public event EventHandler? GameStateChanged;

    public bool IsRunning => _monitor.IsRunning;

    public bool InGame => _switcher.InGameMode;

    public string? CurrentGameName => _switcher.ActiveRule?.Name;

    public IReadOnlyList<GameRule> Rules =>
    [
        .. _settings.Gaming.CustomRules,
        .. BuiltInGameRules.All.Select(r =>
        {
            r.Enabled = !_settings.Gaming.DisabledBuiltInRules.Contains(r.Id);
            return r;
        }),
    ];

    public void UpdateRunningState()
    {
        if (_settings.Gaming.AutoSwitchProfiles)
        {
            Start();
        }
        else
        {
            Stop();
        }
    }

    public void Start()
    {
        _switcher.RestoreDelay = TimeSpan.FromSeconds(_settings.Gaming.RestoreDelaySeconds);
        if (!_monitor.IsRunning)
        {
            try
            {
                _monitor.Start();
                _tick.Start();
                _log.Info("Game detection started.");
            }
            catch (Exception ex)
            {
                _log.Error("Game detection could not start.", ex);
            }
        }
    }

    public void Stop()
    {
        if (_monitor.IsRunning)
        {
            _monitor.Stop();
            _tick.Stop();
            Apply(_switcher.Reset());
            _log.Info("Game detection stopped.");
        }
    }

    /// <summary>The user toggled the filter by hand: keep their choice instead of restoring later.</summary>
    public void NotifyManualFilterToggle()
    {
        if (_switcher.InGameMode)
        {
            _switcher.OnUserOverride();
            GameStateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Dispose()
    {
        _tick.Stop();
        _monitor.Dispose();
    }

    private void OnForeground(ForegroundAppInfo info)
    {
        var rule = GameRuleMatcher.Match(info, Rules);
        var profileId = rule?.ProfileId ?? _settings.Gaming.MinecraftProfileId;
        if (_profiles.Find(profileId) is null)
        {
            profileId = BuiltInPresets.MinecraftNetherId;
        }

        Apply(_switcher.OnForegroundChanged(rule, profileId, _profiles.Active.Id, _filter.IsOn, _settings.Gaming.EnableFilterWhileGaming));
    }

    private void Apply(SwitchAction action)
    {
        if (action.Kind == SwitchActionKind.None)
        {
            return;
        }

        _applying = true;
        try
        {
            if (action.Kind == SwitchActionKind.ActivateGameProfile)
            {
                _log.Info($"Game detected: {action.Rule?.Name}.");
                if (action.ProfileId is { } id)
                {
                    _profiles.SetActive(id, automatic: true);
                }

                if (action.FilterOn == true && !_filter.IsOn)
                {
                    _filter.Enable();
                }
            }
            else if (action.Kind == SwitchActionKind.RestorePrevious)
            {
                _log.Info($"Game no longer in front ({action.Rule?.Name}); restoring previous profile.");
                if (action.ProfileId is { } id)
                {
                    _profiles.SetActive(id, automatic: true);
                }

                if (action.FilterOn == false && _filter.IsOn)
                {
                    _filter.Disable();
                }
                else if (action.FilterOn == true && !_filter.IsOn)
                {
                    _filter.Enable();
                }
            }
        }
        finally
        {
            _applying = false;
        }

        GameStateChanged?.Invoke(this, EventArgs.Empty);
    }
}
