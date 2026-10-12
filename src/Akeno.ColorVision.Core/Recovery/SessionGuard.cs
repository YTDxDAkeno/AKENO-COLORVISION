using System.Globalization;
using Akeno.ColorVision.Shared;

namespace Akeno.ColorVision.Core.Recovery;

/// <summary>Why the app is starting in safe mode (filter stays off until the user turns it on).</summary>
[Flags]
public enum SafeModeReason
{
    None = 0,

    /// <summary>The previous session did not shut down cleanly (crash, power loss, killed process).</summary>
    UncleanShutdown = 1,

    /// <summary>The emergency reset tool was used.</summary>
    EmergencyResetUsed = 2,

    /// <summary>Started with --safe-mode or with Shift held down.</summary>
    Requested = 4,
}

/// <summary>Result of <see cref="SessionGuard.Begin"/>.</summary>
public sealed record SessionStart(SafeModeReason Reasons, string? LastRecoveryNote)
{
    public bool SafeMode => Reasons != SafeModeReason.None;
}

/// <summary>
/// Detects unclean shutdowns with a session lock file and honours the safe-mode flag written by
/// the emergency reset tool. This is what lets the app avoid re-applying a filter that may have
/// caused a problem last time.
/// </summary>
public sealed class SessionGuard
{
    private readonly string _directory;

    public SessionGuard(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrEmpty(dataDirectory);
        _directory = dataDirectory;
    }

    public string LockPath => Path.Combine(_directory, RecoveryProtocol.SessionLockFile);

    public string SafeModeFlagPath => Path.Combine(_directory, RecoveryProtocol.SafeModeFlagFile);

    public string LastRecoveryPath => Path.Combine(_directory, RecoveryProtocol.LastRecoveryFile);

    /// <summary>Call once at startup (after the single-instance check).</summary>
    public SessionStart Begin(int processId, bool safeModeRequested)
    {
        Directory.CreateDirectory(_directory);
        var reasons = safeModeRequested ? SafeModeReason.Requested : SafeModeReason.None;

        if (File.Exists(LockPath))
        {
            reasons |= SafeModeReason.UncleanShutdown;
        }

        if (File.Exists(SafeModeFlagPath))
        {
            reasons |= SafeModeReason.EmergencyResetUsed;
            TryDelete(SafeModeFlagPath);
        }

        string? note = null;
        if (reasons != SafeModeReason.None && File.Exists(LastRecoveryPath))
        {
            try
            {
                note = File.ReadAllText(LastRecoveryPath).Trim();
            }
            catch (IOException)
            {
                // Optional information only.
            }
        }

        AtomicFile.WriteAllText(LockPath, string.Create(CultureInfo.InvariantCulture, $"pid={processId}\nstarted={DateTimeOffset.UtcNow:O}\n"));
        return new SessionStart(reasons, note);
    }

    /// <summary>Call on a clean exit, after the filter has been turned off.</summary>
    public void MarkCleanExit() => TryDelete(LockPath);

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // Best effort.
        }
        catch (UnauthorizedAccessException)
        {
            // Best effort.
        }
    }
}
