using Akeno.ColorVision.ColorScience;

namespace Akeno.ColorVision.Core.Filtering;

/// <summary>
/// How much a backend can be trusted on this PC. Shown verbatim in the diagnostics page so that
/// nothing is presented as working when it has not been checked.
/// </summary>
public enum BackendSupportLevel
{
    /// <summary>The API responds on this PC and the user has confirmed the effect is visible.</summary>
    AvailableAndTested,

    /// <summary>The API is present and responds, but nobody has confirmed the visible effect on this PC yet.</summary>
    AvailableNotVerified,

    /// <summary>Implemented but known to have significant limitations; never selected automatically.</summary>
    Experimental,

    /// <summary>Not available on this PC or not included in this version.</summary>
    Unsupported,

    /// <summary>Tried and failed on this PC.</summary>
    Failed,
}

/// <summary>Static description of what a backend can do here.</summary>
public sealed record BackendCapabilities(
    string BackendId,
    string DisplayName,
    BackendSupportLevel Level,
    string Summary,
    IReadOnlyList<string> Details,
    bool SupportsColorMatrix,
    bool SupportsNonLinearTransforms,
    bool SupportsPerMonitorTransforms)
{
    public static string Describe(BackendSupportLevel level) => level switch
    {
        BackendSupportLevel.AvailableAndTested => "Available and tested",
        BackendSupportLevel.AvailableNotVerified => "Available, not yet verified on this PC",
        BackendSupportLevel.Experimental => "Experimental",
        BackendSupportLevel.Unsupported => "Unsupported",
        BackendSupportLevel.Failed => "Failed",
        _ => level.ToString(),
    };
}

/// <summary>Outcome of a backend operation.</summary>
public sealed record BackendResult(bool Success, string Message, int? NativeErrorCode = null)
{
    public static BackendResult Ok(string message = "OK") => new(true, message);

    public static BackendResult Fail(string message, int? nativeErrorCode = null) => new(false, message, nativeErrorCode);

    public override string ToString() => Success ? Message : $"{Message}{(NativeErrorCode is { } c ? $" (error {c})" : string.Empty)}";
}

/// <summary>
/// A system-wide color filter implementation. Implementations must be safe to call from any
/// thread (they marshal internally if the underlying API has thread affinity) and must never
/// throw for expected failures — they return <see cref="BackendResult.Fail"/> instead.
/// </summary>
public interface IColorFilterBackend : IDisposable
{
    string Id { get; }

    string DisplayName { get; }

    bool IsInitialized { get; }

    /// <summary>Checks availability without changing anything on screen.</summary>
    BackendCapabilities CheckCapabilities();

    /// <summary>Prepares the API and records the pre-existing screen color state.</summary>
    BackendResult Initialize();

    /// <summary>Turns the filter on with the given matrix.</summary>
    BackendResult ApplyTransformation(ColorMatrix matrix);

    /// <summary>Changes the matrix while the filter is on (may animate smoothly).</summary>
    BackendResult UpdateTransformation(ColorMatrix matrix);

    /// <summary>Turns the filter off (identity transform).</summary>
    BackendResult Disable();

    /// <summary>
    /// Returns the screen to the state recorded by <see cref="Initialize"/> where that is safe,
    /// and releases system resources. Called on shutdown.
    /// </summary>
    BackendResult Restore();

    /// <summary>Reads the transform currently active on screen, if the API allows it.</summary>
    ColorMatrix? ReadCurrentTransformation();

    /// <summary>Last-resort reset that bypasses normal sequencing (used by the emergency path).</summary>
    BackendResult ForceIdentity();
}
