using System.Diagnostics;
using System.Windows.Threading;
using Akeno.ColorVision.Core.Diagnostics;
using Akeno.ColorVision.Core.Hotkeys;
using Akeno.ColorVision.Core.Settings;
using Akeno.ColorVision.Shared;
using Akeno.ColorVision.Windows;

namespace Akeno.ColorVision.App.Services;

public enum HotkeyAction
{
    ToggleFilter = 1,
    EmergencyReset = 2,
    IdentifyColor = 3,
}

/// <summary>
/// Global shortcuts and the emergency reset path.
/// <para>
/// Hotkeys are registered on a dedicated background message-pump thread (not the UI thread).
/// The emergency reset — from its shortcut or from the stand-alone reset tool via a named event —
/// is executed directly on that thread, so it still works if the window is frozen. If even that
/// fails, the process terminates itself: Windows and the watchdog then restore normal colors.
/// </para>
/// </summary>
public sealed class HotkeyService : IDisposable
{
    private readonly MessagePumpThread _pump;
    private readonly GlobalHotkeyManager _manager;
    private readonly FilterService _filter;
    private readonly AppSettings _settings;
    private readonly IAppLogger _log;
    private readonly Dispatcher _ui;
    private readonly EventWaitHandle _emergencyEvent;
    private readonly Dictionary<HotkeyAction, HotkeyRegistrationResult> _results = [];

    public HotkeyService(MessagePumpThread pump, FilterService filter, AppSettings settings, IAppLogger log, Dispatcher ui)
    {
        _pump = pump;
        _filter = filter;
        _settings = settings;
        _log = log;
        _ui = ui;
        _manager = new GlobalHotkeyManager(pump);
        _manager.Pressed += OnPressed;

        _emergencyEvent = new EventWaitHandle(false, EventResetMode.AutoReset, RecoveryProtocol.EmergencyResetEventName);
        _pump.AddWaitHandle(_emergencyEvent, () => PerformEmergencyReset("emergency reset tool"));
    }

    /// <summary>Raised on the UI thread.</summary>
    public event EventHandler? ToggleRequested;

    /// <summary>Raised on the UI thread.</summary>
    public event EventHandler? IdentifyRequested;

    /// <summary>Raised on the UI thread after an emergency reset.</summary>
    public event EventHandler<string>? EmergencyResetPerformed;

    public IReadOnlyDictionary<HotkeyAction, HotkeyRegistrationResult> Results => _results;

    /// <summary>(Re-)registers all shortcuts from settings. Returns results per action.</summary>
    public IReadOnlyDictionary<HotkeyAction, HotkeyRegistrationResult> RegisterAll()
    {
        Register(HotkeyAction.EmergencyReset, _settings.Hotkeys.EmergencyReset);
        Register(HotkeyAction.ToggleFilter, _settings.Hotkeys.ToggleFilter);
        Register(HotkeyAction.IdentifyColor, _settings.Hotkeys.IdentifyColor);
        return _results;
    }

    public HotkeyRegistrationResult Register(HotkeyAction action, string text)
    {
        HotkeyRegistrationResult result;
        if (!HotkeyGesture.TryParse(text, out var gesture, out var error))
        {
            result = new HotkeyRegistrationResult(false, error ?? "Invalid shortcut.");
        }
        else
        {
            result = _manager.Register((int)action, gesture);
        }

        _results[action] = result;
        if (!result.Success)
        {
            _log.Warn($"Shortcut for {action} not registered: {result.Message}");
        }

        return result;
    }

    public void Dispose()
    {
        _manager.Pressed -= OnPressed;
        _manager.Dispose();
        _emergencyEvent.Dispose();
    }

    private void OnPressed(int id)
    {
        switch ((HotkeyAction)id)
        {
            case HotkeyAction.EmergencyReset:
                PerformEmergencyReset("shortcut");
                break;
            case HotkeyAction.ToggleFilter:
                _ui.BeginInvoke(() => ToggleRequested?.Invoke(this, EventArgs.Empty));
                break;
            case HotkeyAction.IdentifyColor:
                _ui.BeginInvoke(() => IdentifyRequested?.Invoke(this, EventArgs.Empty));
                break;
        }
    }

    /// <summary>Runs on the hotkey pump thread — deliberately independent of the UI thread.</summary>
    private void PerformEmergencyReset(string source)
    {
        _log.Warn($"Emergency reset via {source}.");
        var result = _filter.EmergencyReset();
        AcknowledgeResetTool();

        if (!result.Success)
        {
            _log.Error($"Emergency reset failed ({result}); terminating so Windows and the watchdog restore normal colors.");
            try
            {
                Process.GetCurrentProcess().Kill();
            }
            catch (Exception)
            {
                Environment.FailFast("Akeno ColorVision emergency reset failed.");
            }

            return;
        }

        _ui.BeginInvoke(() => EmergencyResetPerformed?.Invoke(this, source));
    }

    private static void AcknowledgeResetTool()
    {
        try
        {
            if (EventWaitHandle.TryOpenExisting(RecoveryProtocol.EmergencyResetAckEventName, out var ack))
            {
                using (ack)
                {
                    ack.Set();
                }
            }
        }
        catch (Exception)
        {
            // The tool only waits a few seconds; nothing else to do.
        }
    }
}
