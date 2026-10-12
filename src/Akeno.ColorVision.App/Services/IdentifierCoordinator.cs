using Akeno.ColorVision.App.Views;

namespace Akeno.ColorVision.App.Services;

/// <summary>Owns the identifier popups so they work whether or not the main window is open.</summary>
public sealed class IdentifierCoordinator
{
    private readonly AppHost _host;
    private IdentifierPopup? _popup;
    private LiveIdentifierOverlay? _live;

    public IdentifierCoordinator(AppHost host) => _host = host;

    public event EventHandler<ColorSampleResult>? SampleTaken;

    public event EventHandler? LiveStateChanged;

    public bool IsLive => _live is not null;

    /// <summary>Shortcut behaviour: stop live mode if running, otherwise identify the color under the pointer.</summary>
    public async void OnShortcut()
    {
        if (IsLive)
        {
            StopLive();
            return;
        }

        await IdentifyAtCursorAsync();
    }

    public async Task<ColorSampleResult?> IdentifyAtCursorAsync()
    {
        if (_host.Identifier.NeedsSamplingCheck)
        {
            await _host.Identifier.RunSamplingCheckAsync();
        }

        var result = _host.Identifier.SampleAtCursor();
        if (result is null)
        {
            return null;
        }

        _popup ??= new IdentifierPopup();
        _popup.ShowResult(result);
        SampleTaken?.Invoke(this, result);
        return result;
    }

    public async void StartLive()
    {
        if (IsLive)
        {
            return;
        }

        if (_host.Identifier.NeedsSamplingCheck)
        {
            await _host.Identifier.RunSamplingCheckAsync();
        }

        _live = new LiveIdentifierOverlay(_host.Identifier, $"Press {_host.Settings.Hotkeys.IdentifyColor} again to stop.");
        _live.Start();
        LiveStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void StopLive()
    {
        if (_live is null)
        {
            return;
        }

        var last = _live.Last;
        _live.Stop();
        _live = null;
        if (last is not null)
        {
            SampleTaken?.Invoke(this, last);
        }

        LiveStateChanged?.Invoke(this, EventArgs.Empty);
    }
}
