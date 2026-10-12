using System.Runtime.InteropServices;
using Akeno.ColorVision.Core.Diagnostics;
using Akeno.ColorVision.Core.Filtering;
using Akeno.ColorVision.Rendering.Magnification;

[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]

namespace Akeno.ColorVision.Rendering;

/// <summary>
/// Lists the rendering backends known to this version and creates the active one.
/// <para>
/// Only the Magnification API backend is implemented in v0.1.0. The GPU capture + shader approach
/// was evaluated (see docs/TECHNICAL-ARCHITECTURE.md) but is not shipped, and it is reported as
/// such rather than being silently substituted.
/// </para>
/// </summary>
public static class BackendCatalog
{
    public const string GpuCaptureId = "gpu-capture";

    /// <summary>Creates the backend for "auto" or an explicit id. Unknown ids fall back to the Magnification backend (logged).</summary>
    public static IColorFilterBackend Create(string? requestedId, IAppLogger log)
    {
        if (!string.IsNullOrEmpty(requestedId) &&
            !string.Equals(requestedId, "auto", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(requestedId, MagnificationColorFilterBackend.BackendId, StringComparison.OrdinalIgnoreCase))
        {
            log.Warn($"Requested backend '{requestedId}' is not available in this version; using the Magnification API backend.");
        }

        return new MagnificationColorFilterBackend(log);
    }

    /// <summary>Static description of the evaluated-but-not-included GPU backend, for the diagnostics page.</summary>
    public static BackendCapabilities GpuCaptureCapabilities() => new(
        GpuCaptureId,
        "GPU capture + shader (Windows Graphics Capture + Direct3D 11)",
        BackendSupportLevel.Unsupported,
        "Not included in this version. It was evaluated and postponed: it adds at least one frame of latency, needs a click-through overlay, cannot affect exclusive-fullscreen games, and cannot be verified without real Windows hardware testing.",
        [
            "Would allow non-linear corrections (lookup tables) and per-monitor settings.",
            "Planned only after the Magnification backend has been verified on real hardware.",
        ],
        SupportsColorMatrix: true,
        SupportsNonLinearTransforms: true,
        SupportsPerMonitorTransforms: true);
}
