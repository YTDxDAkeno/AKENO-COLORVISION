using System.Windows.Threading;
using Akeno.ColorVision.ColorScience;
using Akeno.ColorVision.Core.Diagnostics;
using Akeno.ColorVision.Core.Filtering;
using Akeno.ColorVision.Core.Settings;
using Akeno.ColorVision.Rendering.Magnification;
using Microsoft.Win32;

namespace Akeno.ColorVision.App.Services;

/// <summary>
/// UI-facing wrapper around <see cref="FilterController"/>: keeps the active profile's matrix
/// applied, re-applies it after sleep/unlock/display changes, watches for the effect being lost,
/// and exposes simple state for the UI. Events are raised on the UI thread.
/// </summary>
public sealed class FilterService : IDisposable
{
    private readonly FilterController _controller;
    private readonly ProfileService _profiles;
    private readonly AppSettings _settings;
    private readonly IAppLogger _log;
    private readonly Dispatcher _ui;
    private readonly DispatcherTimer _healthTimer;
    private readonly DispatcherTimer _reapplyTimer;
    private ColorMatrix? _overrideMatrix;
    private bool? _suspendedWasOn;
    private bool _foreignChangeReported;
    private bool _holdCompare;
    private bool _disposed;

    public FilterService(FilterController controller, ProfileService profiles, AppSettings settings, IAppLogger log, Dispatcher ui)
    {
        _controller = controller;
        _profiles = profiles;
        _settings = settings;
        _log = log;
        _ui = ui;

        _controller.StateChanged += (_, e) => _ui.BeginInvoke(() =>
        {
            StatusMessage = e.Message;
            StatusChanged?.Invoke(this, EventArgs.Empty);
        });
        _profiles.ActiveProfileChanged += (_, _) => ApplyCurrentMatrix();
        _profiles.ActiveProfileEdited += (_, _) => ApplyCurrentMatrix();

        _healthTimer = new DispatcherTimer(TimeSpan.FromSeconds(5), DispatcherPriority.Background, (_, _) => HealthCheck(), ui);
        _reapplyTimer = new DispatcherTimer(DispatcherPriority.Background, ui) { Interval = TimeSpan.FromSeconds(1.5) };
        _reapplyTimer.Tick += (_, _) =>
        {
            _reapplyTimer.Stop();
            Reapply("system event");
        };

        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.SessionSwitch += OnSessionSwitch;
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
    }

    public event EventHandler? StatusChanged;

    public FilterController Controller => _controller;

    public bool IsOn => _controller.IsOn;

    public FilterState State => _controller.State;

    public string StatusMessage { get; private set; } = "Starting…";

    public string? LastError => _controller.LastError;

    public bool ForeignEffectDetected => _controller.ForeignEffectDetected;

    /// <summary>True while calibration (or another preview) has temporarily turned the filter off.</summary>
    public bool IsSuspended => _suspendedWasOn is not null;

    /// <summary>Set when Windows reported a different active color matrix than ours (another program).</summary>
    public bool ForeignChangeDetected { get; private set; }

    /// <summary>The matrix currently intended for the screen (profile or temporary preview).</summary>
    public ColorMatrix CurrentMatrix => _overrideMatrix ?? _profiles.ActiveMatrix;

    public BackendCapabilities Capabilities => _controller.Backend.CheckCapabilities();

    public BackendResult Initialize()
    {
        if (_controller.Backend is MagnificationColorFilterBackend mag)
        {
            mag.SmoothTransitions = _settings.SmoothTransitions;
            mag.UserConfirmedVisible = _settings.FilterVisuallyConfirmedUtc is not null;
        }

        var result = _controller.Initialize();
        _controller.SetTransformation(CurrentMatrix);
        _healthTimer.Start();
        RaiseStatus(result.Message);
        return result;
    }

    public BackendResult Enable()
    {
        if (IsSuspended)
        {
            _suspendedWasOn = true;
            return BackendResult.Ok("The filter will turn on after calibration.");
        }

        var set = _controller.SetTransformation(CurrentMatrix);
        if (!set.Success)
        {
            RaiseStatus(set.Message);
            return set;
        }

        var result = _controller.Enable();
        _foreignChangeReported = false;
        ForeignChangeDetected = false;
        RaiseStatus(result.Success ? "Filter is on." : result.Message);
        return result;
    }

    public BackendResult Disable()
    {
        if (IsSuspended)
        {
            _suspendedWasOn = false;
            return BackendResult.Ok("The filter will stay off after calibration.");
        }

        var result = _controller.Disable();
        RaiseStatus(result.Success ? "Filter is off." : result.Message);
        return result;
    }

    public BackendResult Toggle() => IsOn || _suspendedWasOn == true ? Disable() : Enable();

    /// <summary>Re-applies the matrix of the active profile (call after profile edits).</summary>
    public void ApplyCurrentMatrix()
    {
        if (_disposed)
        {
            return;
        }

        var result = _controller.SetTransformation(CurrentMatrix);
        if (!result.Success)
        {
            RaiseStatus(result.Message);
        }
        else
        {
            StatusChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Temporarily shows a matrix other than the active profile (e.g. calibration preview).</summary>
    public BackendResult ShowTemporary(ColorMatrix matrix)
    {
        _overrideMatrix = matrix;
        var set = _controller.SetTransformation(matrix);
        if (!set.Success)
        {
            _overrideMatrix = null;
            return set;
        }

        return _controller.IsOn ? set : _controller.Enable();
    }

    /// <summary>Ends <see cref="ShowTemporary"/>, returning to the active profile and the given on/off state.</summary>
    public void EndTemporary(bool filterOn)
    {
        _overrideMatrix = null;
        _controller.SetTransformation(CurrentMatrix);
        if (!filterOn && _controller.IsOn)
        {
            _controller.Disable();
        }
        else if (filterOn && !_controller.IsOn)
        {
            _controller.Enable();
        }

        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>While a "hold to compare" button is pressed, temporarily show original colors.</summary>
    public void BeginHoldCompare()
    {
        if (_controller.IsOn && !_holdCompare)
        {
            _holdCompare = true;
            _controller.Disable();
        }
    }

    public void EndHoldCompare()
    {
        if (_holdCompare)
        {
            _holdCompare = false;
            _controller.Enable();
        }
    }

    /// <summary>Turns the filter off for calibration (so swatches are shown unfiltered) and remembers the state.</summary>
    public void Suspend()
    {
        if (IsSuspended)
        {
            return;
        }

        _suspendedWasOn = _controller.IsOn;
        if (_controller.IsOn)
        {
            _controller.Disable();
        }

        RaiseStatus("Filter paused during calibration.");
    }

    public void Resume(bool? forceOn = null)
    {
        if (!IsSuspended)
        {
            return;
        }

        var turnOn = forceOn ?? _suspendedWasOn == true;
        _suspendedWasOn = null;
        _overrideMatrix = null;
        _controller.SetTransformation(CurrentMatrix);
        if (turnOn)
        {
            Enable();
        }
        else
        {
            RaiseStatus("Filter is off.");
        }
    }

    /// <summary>Emergency reset from any thread.</summary>
    public BackendResult EmergencyReset()
    {
        _overrideMatrix = null;
        var result = _controller.EmergencyReset(TimeSpan.FromSeconds(2));
        _ui.BeginInvoke(() =>
        {
            _suspendedWasOn = _suspendedWasOn is null ? null : false;
            RaiseStatus(result.Success ? "Emergency reset: normal colors restored." : "Emergency reset problem: " + result.Message);
        });
        return result;
    }

    public void SetSmoothTransitions(bool enabled)
    {
        if (_controller.Backend is MagnificationColorFilterBackend mag)
        {
            mag.SmoothTransitions = enabled;
        }
    }

    public void MarkVisuallyConfirmed(bool confirmed)
    {
        if (_controller.Backend is MagnificationColorFilterBackend mag)
        {
            mag.UserConfirmedVisible = confirmed;
        }

        RaiseStatus(confirmed ? "Thanks — the filter is confirmed to work on this PC." : StatusMessage);
    }

    public void Shutdown()
    {
        if (_disposed)
        {
            return;
        }

        _healthTimer.Stop();
        _reapplyTimer.Stop();
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        _controller.Shutdown();
        _disposed = true;
    }

    public void Dispose()
    {
        Shutdown();
        _controller.Dispose();
    }

    private void Reapply(string reason)
    {
        if (!_controller.IsOn)
        {
            return;
        }

        var result = _controller.Reapply();
        _log.Info($"Re-applied filter after {reason}: {result}");
        if (!result.Success)
        {
            RaiseStatus(result.Message);
        }
    }

    private void HealthCheck()
    {
        if (!_controller.IsOn)
        {
            return;
        }

        var current = _controller.Backend.ReadCurrentTransformation();
        if (current is null)
        {
            return;
        }

        var expected = _controller.TargetMatrix;
        if (current.MaxAbsDifference(expected) < 1e-3)
        {
            return;
        }

        if (current.IsIdentity(1e-4))
        {
            // The effect disappeared (e.g. display driver reset or DWM restart): put it back.
            _log.Warn("The color effect was lost; re-applying.");
            Reapply("effect lost");
        }
        else if (!_foreignChangeReported)
        {
            // Another program changed the screen colors: do not fight it, just tell the user.
            _foreignChangeReported = true;
            ForeignChangeDetected = true;
            _log.Warn("Another program changed the full-screen color effect.");
            RaiseStatus("Another program changed the screen colors. Turn the filter off and on to re-apply yours.");
        }
    }

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume)
        {
            _ui.BeginInvoke(() => _reapplyTimer.Start());
        }
    }

    private void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
    {
        if (e.Reason is SessionSwitchReason.SessionUnlock or SessionSwitchReason.ConsoleConnect or SessionSwitchReason.RemoteConnect or SessionSwitchReason.SessionLogon)
        {
            _ui.BeginInvoke(() => _reapplyTimer.Start());
        }
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) => _ui.BeginInvoke(() => _reapplyTimer.Start());

    private void RaiseStatus(string message)
    {
        StatusMessage = message;
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }
}
