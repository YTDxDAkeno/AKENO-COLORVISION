using System.Runtime.InteropServices;
using Akeno.ColorVision.ColorScience;
using Akeno.ColorVision.Core.Diagnostics;
using Akeno.ColorVision.Core.Filtering;
using Akeno.ColorVision.Windows;

namespace Akeno.ColorVision.Rendering.Magnification;

/// <summary>
/// System-wide color filter built on the documented Windows Magnification API
/// (<c>MagInitialize</c> / <c>MagSetFullscreenColorEffect</c> / <c>MagGetFullscreenColorEffect</c> /
/// <c>MagUninitialize</c>).
/// <para>What it can do: apply one 5×5 affine color matrix to the whole composed desktop (all
/// monitors), with no magnification and no extra latency of its own. What it cannot do: non-linear
/// transforms or lookup tables, different matrices per monitor, or content that bypasses desktop
/// composition (some exclusive-fullscreen games).</para>
/// <para>All native calls run on one dedicated <see cref="MessagePumpThread"/>, so the filter keeps
/// working even if the UI thread is busy.</para>
/// </summary>
public sealed class MagnificationColorFilterBackend : IColorFilterBackend
{
    public const string BackendId = "magnification";
    private static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(3);

    private readonly IAppLogger _log;
    private readonly MessagePumpThread _thread;
    private ColorMatrix? _applied;
    private ColorMatrix? _preexisting;
    private bool _initialized;
    private bool _failed;
    private bool _disposed;

    public MagnificationColorFilterBackend(IAppLogger? log = null)
    {
        _log = log ?? NullLogger.Instance;
        _thread = new MessagePumpThread("Akeno Magnification");
        _thread.UnhandledException += ex => _log.Error("Unexpected error on the magnification thread.", ex);
    }

    public string Id => BackendId;

    public string DisplayName => "Windows Magnification API (system-wide color matrix)";

    public bool IsInitialized => _initialized;

    /// <summary>Animate matrix changes over ~120 ms instead of switching instantly.</summary>
    public bool SmoothTransitions { get; set; } = true;

    /// <summary>Set by the app once the user has confirmed the effect is visible on this PC.</summary>
    public bool UserConfirmedVisible { get; set; }

    /// <summary>The matrix that was active before this app initialised (null if unknown or identity).</summary>
    public ColorMatrix? PreexistingEffect => _preexisting;

    public BackendCapabilities CheckCapabilities()
    {
        var details = new List<string>
        {
            "Applies one color matrix to the entire desktop, on all monitors at once.",
            "Supports matrix transforms only (no non-linear curves or lookup tables).",
            "Works for normal windows, browsers, videos and borderless/windowed games.",
            "Exclusive-fullscreen games that bypass desktop composition may not be affected.",
        };

        if (!OperatingSystem.IsWindows())
        {
            return Caps(BackendSupportLevel.Unsupported, "Requires Windows.", details);
        }

        if (!Environment.Is64BitProcess && Environment.Is64BitOperatingSystem)
        {
            return Caps(BackendSupportLevel.Unsupported, "The Magnification API does not support 32-bit processes on 64-bit Windows (WOW64).", details);
        }

        if (!OperatingSystem.IsWindowsVersionAtLeast(6, 2))
        {
            return Caps(BackendSupportLevel.Unsupported, "Requires Windows 8 or later.", details);
        }

        if (!MagnificationNative.IsAvailable(out var detail))
        {
            return Caps(BackendSupportLevel.Unsupported, detail, details);
        }

        if (_failed)
        {
            return Caps(BackendSupportLevel.Failed, "The Magnification API reported an error on this PC. See the log for details.", details);
        }

        return UserConfirmedVisible
            ? Caps(BackendSupportLevel.AvailableAndTested, "Available, and you confirmed the filter is visible on this PC.", details)
            : Caps(BackendSupportLevel.AvailableNotVerified, "Available. Use \"Check that the filter works\" to confirm it visibly changes your screen.", details);
    }

    public BackendResult Initialize()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return Run(() =>
        {
            if (_initialized)
            {
                return BackendResult.Ok("Already initialized.");
            }

            if (!MagnificationNative.MagInitialize())
            {
                _failed = true;
                return BackendResult.Fail("MagInitialize failed.", Marshal.GetLastPInvokeError());
            }

            _initialized = true;
            _failed = false;

            if (MagnificationNative.MagGetFullscreenColorEffect(out var current))
            {
                var matrix = ColorMatrix.FromMagColorEffect(current.ToArray());
                _preexisting = matrix.IsIdentity(1e-4) ? null : matrix;
                if (_preexisting is not null)
                {
                    _log.Warn("A full-screen color effect from another program was already active.");
                }
            }

            _log.Info("Magnification API initialized.");
            return BackendResult.Ok("Magnification API initialized.");
        });
    }

    public BackendResult ApplyTransformation(ColorMatrix matrix) => SetMatrix(matrix, animate: false);

    public BackendResult UpdateTransformation(ColorMatrix matrix) => SetMatrix(matrix, animate: SmoothTransitions);

    public BackendResult Disable() => SetMatrix(ColorMatrix.Identity, animate: false, isDisable: true);

    public BackendResult Restore()
    {
        if (!_initialized)
        {
            return BackendResult.Ok("Nothing to restore.");
        }

        return Run(() =>
        {
            var identity = MAGCOLOREFFECT.FromArray(ColorMatrix.Identity.ToMagColorEffect());
            MagnificationNative.MagSetFullscreenColorEffect(ref identity);
            _applied = null;

            // A pre-existing effect from another program is deliberately NOT re-applied: its owner may
            // have exited, and re-applying it from this process could leave the screen distorted with
            // nothing left to turn it off. The owning program (e.g. Windows Magnifier) re-applies its
            // own effect when needed.
            var note = _preexisting is null
                ? "Normal colors restored."
                : "Normal colors restored. Another program's color effect was active before; that program may need to re-enable it.";

            var ok = MagnificationNative.MagUninitialize();
            _initialized = false;
            _log.Info("Magnification API released.");
            return ok ? BackendResult.Ok(note) : BackendResult.Fail("MagUninitialize failed. " + note, Marshal.GetLastPInvokeError());
        });
    }

    public ColorMatrix? ReadCurrentTransformation()
    {
        if (!_initialized)
        {
            return null;
        }

        try
        {
            return _thread.Invoke(() =>
                MagnificationNative.MagGetFullscreenColorEffect(out var effect)
                    ? ColorMatrix.FromMagColorEffect(effect.ToArray())
                    : null, CallTimeout);
        }
        catch (Exception ex) when (ex is TimeoutException or DllNotFoundException or EntryPointNotFoundException)
        {
            _log.Warn("Could not read the current color effect.", ex);
            return null;
        }
    }

    public BackendResult ForceIdentity()
    {
        // Preferred: on the owning thread with a short timeout.
        try
        {
            return _thread.Invoke(ForceIdentityCore, TimeSpan.FromSeconds(1));
        }
        catch (TimeoutException)
        {
            _log.Error("Magnification thread unresponsive; forcing identity from the calling thread.");
        }
        catch (Exception ex)
        {
            _log.Error("ForceIdentity failed on the magnification thread.", ex);
        }

        // Last resort from the calling thread.
        try
        {
            return ForceIdentityCore();
        }
        catch (Exception ex)
        {
            return BackendResult.Fail("Could not reset the screen colors: " + ex.Message);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            if (_initialized)
            {
                Restore();
            }
        }
        catch (Exception ex)
        {
            _log.Error("Error while releasing the Magnification API.", ex);
        }

        _disposed = true;
        _thread.Dispose();
    }

    private BackendResult ForceIdentityCore()
    {
        if (!_initialized && !MagnificationNative.MagInitialize())
        {
            return BackendResult.Fail("MagInitialize failed during reset.", Marshal.GetLastPInvokeError());
        }

        _initialized = true;
        var identity = MAGCOLOREFFECT.FromArray(ColorMatrix.Identity.ToMagColorEffect());
        if (!MagnificationNative.MagSetFullscreenColorEffect(ref identity))
        {
            return BackendResult.Fail("MagSetFullscreenColorEffect(identity) failed.", Marshal.GetLastPInvokeError());
        }

        _applied = null;
        return BackendResult.Ok("Normal colors restored.");
    }

    private BackendResult SetMatrix(ColorMatrix target, bool animate, bool isDisable = false)
    {
        ArgumentNullException.ThrowIfNull(target);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!target.IsFinite)
        {
            return BackendResult.Fail("The transform contains invalid numbers.");
        }

        return Run(() =>
        {
            if (!_initialized)
            {
                return BackendResult.Fail("The Magnification API is not initialized.");
            }

            var from = _applied ?? ColorMatrix.Identity;
            if (animate && from.MaxAbsDifference(target) > 0.02)
            {
                const int steps = 6;
                for (var i = 1; i < steps; i++)
                {
                    var t = (double)i / steps;
                    var eased = t * t * (3 - (2 * t)); // smoothstep
                    if (!Set(ColorMatrix.Lerp(from, target, eased), out _))
                    {
                        break;
                    }

                    Thread.Sleep(20);
                }
            }

            if (!Set(target, out var error))
            {
                _failed = true;
                return BackendResult.Fail(isDisable ? "Could not turn the filter off." : "Could not apply the color filter.", error);
            }

            _applied = target.IsIdentity() ? null : target;

            // Read back to verify the API accepted the exact matrix (catches layout/order mistakes).
            if (MagnificationNative.MagGetFullscreenColorEffect(out var readBack))
            {
                var actual = ColorMatrix.FromMagColorEffect(readBack.ToArray());
                if (actual.MaxAbsDifference(target) > 1e-3)
                {
                    _log.Warn("The color effect read back from Windows differs from the one that was set.");
                    return BackendResult.Ok("Applied, but Windows reports a different active matrix (another program may also be changing screen colors).");
                }
            }

            return BackendResult.Ok(isDisable ? "Filter turned off." : "Filter applied.");
        });
    }

    private static bool Set(ColorMatrix matrix, out int error)
    {
        var effect = MAGCOLOREFFECT.FromArray(matrix.ToMagColorEffect());
        var ok = MagnificationNative.MagSetFullscreenColorEffect(ref effect);
        error = ok ? 0 : Marshal.GetLastPInvokeError();
        return ok;
    }

    private BackendResult Run(Func<BackendResult> action)
    {
        try
        {
            return _thread.Invoke(action, CallTimeout);
        }
        catch (TimeoutException ex)
        {
            return BackendResult.Fail(ex.Message);
        }
        catch (DllNotFoundException ex)
        {
            _failed = true;
            return BackendResult.Fail("Magnification.dll is not available: " + ex.Message);
        }
        catch (EntryPointNotFoundException ex)
        {
            _failed = true;
            return BackendResult.Fail("This version of Windows does not support full-screen color effects: " + ex.Message);
        }
        catch (Exception ex)
        {
            _log.Error("Unexpected magnification error.", ex);
            return BackendResult.Fail("Unexpected error: " + ex.Message);
        }
    }

    private BackendCapabilities Caps(BackendSupportLevel level, string summary, IReadOnlyList<string> details) =>
        new(BackendId, DisplayName, level, summary, details, SupportsColorMatrix: true, SupportsNonLinearTransforms: false, SupportsPerMonitorTransforms: false);
}
