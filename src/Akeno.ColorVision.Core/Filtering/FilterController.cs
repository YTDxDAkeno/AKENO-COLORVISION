using Akeno.ColorVision.ColorScience;
using Akeno.ColorVision.Core.Diagnostics;

namespace Akeno.ColorVision.Core.Filtering;

public enum FilterState
{
    NotInitialized,
    Off,
    On,
    Failed,
    ShutDown,
}

public sealed class FilterStateChangedEventArgs(FilterState previous, FilterState current, string message) : EventArgs
{
    public FilterState Previous { get; } = previous;

    public FilterState Current { get; } = current;

    public string Message { get; } = message;
}

/// <summary>
/// The single owner of the system-wide filter. Enforces a small state machine, validates every
/// matrix with <see cref="TransformValidator"/> before it reaches the screen, and falls back to an
/// identity transform whenever something goes wrong so the desktop is never left distorted by a
/// half-finished operation. Thread-safe.
/// </summary>
public sealed class FilterController : IDisposable
{
    private readonly object _gate = new();
    private readonly IColorFilterBackend _backend;
    private readonly IAppLogger _log;
    private ColorMatrix _target = ColorMatrix.Identity;
    private volatile FilterState _state = FilterState.NotInitialized;

    public FilterController(IColorFilterBackend backend, IAppLogger? log = null)
    {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        _log = log ?? NullLogger.Instance;
    }

    public event EventHandler<FilterStateChangedEventArgs>? StateChanged;

    public FilterState State => _state;

    public bool IsOn => _state == FilterState.On;

    public IColorFilterBackend Backend => _backend;

    /// <summary>The matrix used when the filter is (or will be) on.</summary>
    public ColorMatrix TargetMatrix
    {
        get
        {
            lock (_gate)
            {
                return _target;
            }
        }
    }

    public string? LastError { get; private set; }

    /// <summary>True if another program's color effect was active when the filter initialised.</summary>
    public bool ForeignEffectDetected { get; private set; }

    public BackendResult Initialize()
    {
        FilterStateChangedEventArgs? change;
        BackendResult result;
        lock (_gate)
        {
            result = InitializeCore(out change);
        }

        Raise(change);
        return result;
    }

    /// <summary>Validates and stores a new matrix; applies it immediately if the filter is on.</summary>
    public BackendResult SetTransformation(ColorMatrix matrix)
    {
        ArgumentNullException.ThrowIfNull(matrix);
        var validation = TransformValidator.Validate(matrix);
        if (!validation.IsSafe)
        {
            var message = "Rejected an unsafe color transform: " + string.Join(" ", validation.Errors);
            _log.Warn(message);
            return BackendResult.Fail(message);
        }

        FilterStateChangedEventArgs? change = null;
        BackendResult result;
        lock (_gate)
        {
            _target = matrix;
            if (_state != FilterState.On)
            {
                return BackendResult.Ok("Stored; the filter is off.");
            }

            result = _backend.UpdateTransformation(matrix);
            if (!result.Success)
            {
                _log.Error($"Updating the filter failed: {result}");
                change = FailSafe(result.Message);
            }
        }

        Raise(change);
        return result;
    }

    public BackendResult Enable()
    {
        FilterStateChangedEventArgs? change = null;
        BackendResult result;
        lock (_gate)
        {
            if (_state == FilterState.ShutDown)
            {
                return BackendResult.Fail("The filter has been shut down.");
            }

            if (_state == FilterState.On)
            {
                return BackendResult.Ok("Already on.");
            }

            result = _state is FilterState.NotInitialized or FilterState.Failed
                ? InitializeCore(out change)
                : BackendResult.Ok();

            if (result.Success)
            {
                var validation = TransformValidator.Validate(_target);
                if (!validation.IsSafe)
                {
                    result = BackendResult.Fail("Rejected an unsafe color transform: " + string.Join(" ", validation.Errors));
                }
                else
                {
                    result = _backend.ApplyTransformation(_target);
                    if (result.Success)
                    {
                        LastError = null;
                        change = Transition(FilterState.On, "Filter turned on.") ?? change;
                    }
                    else
                    {
                        _log.Error($"Turning the filter on failed: {result}");
                        change = FailSafe(result.Message);
                    }
                }
            }
        }

        Raise(change);
        return result;
    }

    public BackendResult Disable()
    {
        FilterStateChangedEventArgs? change = null;
        BackendResult result;
        lock (_gate)
        {
            if (_state != FilterState.On)
            {
                return BackendResult.Ok("Already off.");
            }

            result = _backend.Disable();
            if (result.Success)
            {
                change = Transition(FilterState.Off, "Filter turned off.");
            }
            else
            {
                _log.Error($"Turning the filter off failed: {result}");
                change = FailSafe(result.Message);
            }
        }

        Raise(change);
        return result;
    }

    public BackendResult Toggle() => IsOn ? Disable() : Enable();

    /// <summary>Re-applies the current matrix (after sleep/resume, unlock or display changes).</summary>
    public BackendResult Reapply()
    {
        FilterStateChangedEventArgs? change = null;
        BackendResult result;
        lock (_gate)
        {
            if (_state != FilterState.On)
            {
                return BackendResult.Ok("Filter is off; nothing to re-apply.");
            }

            result = _backend.ApplyTransformation(_target);
            if (!result.Success)
            {
                _log.Error($"Re-applying the filter failed: {result}");
                change = FailSafe(result.Message);
            }
        }

        Raise(change);
        return result;
    }

    /// <summary>
    /// Emergency path: returns the screen to normal colors from any state. If another thread holds
    /// the controller (for example a hung operation), it bypasses the lock and forces identity.
    /// </summary>
    public BackendResult EmergencyReset(TimeSpan lockTimeout)
    {
        _log.Warn("Emergency reset requested.");
        FilterStateChangedEventArgs? change = null;
        BackendResult result;
        var taken = false;
        try
        {
            Monitor.TryEnter(_gate, lockTimeout, ref taken);
            if (taken)
            {
                result = _backend.IsInitialized ? _backend.Disable() : BackendResult.Ok("Filter was not active.");
                if (!result.Success)
                {
                    result = _backend.ForceIdentity();
                }

                if (_state is FilterState.On or FilterState.Failed)
                {
                    change = result.Success
                        ? Transition(FilterState.Off, "Emergency reset: normal colors restored.")
                        : Transition(FilterState.Failed, "Emergency reset failed: " + result.Message);
                }
            }
            else
            {
                _log.Warn("Emergency reset: controller busy; forcing identity directly.");
                result = _backend.ForceIdentity();
                if (result.Success)
                {
                    var previous = _state;
                    _state = FilterState.Off;
                    change = new FilterStateChangedEventArgs(previous, FilterState.Off, "Emergency reset: normal colors restored.");
                }
            }
        }
        finally
        {
            if (taken)
            {
                Monitor.Exit(_gate);
            }
        }

        Raise(change);
        return result;
    }

    /// <summary>Turns the filter off, restores the pre-existing state and releases the backend.</summary>
    public void Shutdown()
    {
        FilterStateChangedEventArgs? change = null;
        lock (_gate)
        {
            if (_state == FilterState.ShutDown)
            {
                return;
            }

            try
            {
                if (_state == FilterState.On)
                {
                    var r = _backend.Disable();
                    if (!r.Success)
                    {
                        _backend.ForceIdentity();
                    }
                }

                if (_backend.IsInitialized)
                {
                    var restore = _backend.Restore();
                    if (!restore.Success)
                    {
                        _log.Warn($"Restore on shutdown reported: {restore}");
                    }
                }
            }
            catch (Exception ex)
            {
                _log.Error("Error while shutting down the filter.", ex);
                try
                {
                    _backend.ForceIdentity();
                }
                catch (Exception)
                {
                    // Nothing more we can do; the OS / watchdog takes over.
                }
            }

            change = Transition(FilterState.ShutDown, "Filter shut down.");
        }

        Raise(change);
    }

    public void Dispose()
    {
        Shutdown();
        _backend.Dispose();
    }

    private BackendResult InitializeCore(out FilterStateChangedEventArgs? change)
    {
        change = null;
        if (_state is FilterState.Off or FilterState.On)
        {
            return BackendResult.Ok("Already initialized.");
        }

        if (_state == FilterState.ShutDown)
        {
            return BackendResult.Fail("The filter has been shut down.");
        }

        var result = _backend.Initialize();
        if (!result.Success)
        {
            LastError = result.Message;
            _log.Error($"Filter backend '{_backend.Id}' failed to initialise: {result}");
            change = Transition(FilterState.Failed, result.Message);
            return result;
        }

        var current = _backend.ReadCurrentTransformation();
        ForeignEffectDetected = current is not null && !current.IsIdentity(1e-4);
        if (ForeignEffectDetected)
        {
            _log.Warn("Another program's full-screen color effect was active when the filter started.");
        }

        LastError = null;
        change = Transition(FilterState.Off, "Ready.");
        return result;
    }

    private FilterStateChangedEventArgs? FailSafe(string message)
    {
        // Never leave a half-applied transform on screen.
        var reset = _backend.ForceIdentity();
        if (!reset.Success)
        {
            _log.Error($"Fail-safe identity reset also failed: {reset}");
        }

        LastError = message;
        return Transition(FilterState.Failed, message);
    }

    private FilterStateChangedEventArgs? Transition(FilterState next, string message)
    {
        var previous = _state;
        _state = next;
        if (previous == next)
        {
            return null;
        }

        _log.Info($"Filter state: {previous} -> {next} ({message})");
        return new FilterStateChangedEventArgs(previous, next, message);
    }

    private void Raise(FilterStateChangedEventArgs? change)
    {
        if (change is null)
        {
            return;
        }

        try
        {
            StateChanged?.Invoke(this, change);
        }
        catch (Exception ex)
        {
            _log.Error("A filter state listener threw an exception.", ex);
        }
    }
}
