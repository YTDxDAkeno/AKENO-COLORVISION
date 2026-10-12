// Shared between the main application (via Akeno.ColorVision.Core) and the stand-alone
// emergency reset utility (Akeno.ColorVision.Recovery), which intentionally has no other
// project references. Keep this file dependency-free.

namespace Akeno.ColorVision.Shared;

/// <summary>Names and file locations both processes must agree on.</summary>
internal static class RecoveryProtocol
{
    public const string AppDataFolderName = "AkenoColorVision";

    /// <summary>If this file exists next to the executable, data is stored next to the executable (portable mode).</summary>
    public const string PortableMarkerFile = "portable.txt";

    public const string PortableDataFolderName = "AkenoColorVision-Data";

    /// <summary>Exists while the app runs; left behind after a crash.</summary>
    public const string SessionLockFile = "session.lock";

    /// <summary>Written by the emergency reset tool; makes the next start use safe mode.</summary>
    public const string SafeModeFlagFile = "safe-mode.flag";

    /// <summary>Human-readable note about the last automatic or manual recovery.</summary>
    public const string LastRecoveryFile = "last-recovery.txt";

    public const string EmergencyResetEventName = @"Local\AkenoColorVision.EmergencyReset";
    public const string EmergencyResetAckEventName = @"Local\AkenoColorVision.EmergencyResetAck";
    public const string SingleInstanceMutexName = @"Local\AkenoColorVision.SingleInstance";
    public const string ActivateEventName = @"Local\AkenoColorVision.Activate";

    public const string MainExecutableName = "AkenoColorVision.exe";
    public const string MainProcessName = "AkenoColorVision";
    public const string RecoveryExecutableName = "AkenoColorVision-EmergencyReset.exe";

    /// <summary>Resolves the data directory, honouring portable mode.</summary>
    public static string GetDataDirectory(string? executableDirectory = null)
    {
        var exeDir = executableDirectory ?? AppContext.BaseDirectory;
        try
        {
            if (File.Exists(Path.Combine(exeDir, PortableMarkerFile)))
            {
                return Path.Combine(exeDir, PortableDataFolderName);
            }
        }
        catch (IOException)
        {
            // Fall back to the per-user location.
        }
        catch (UnauthorizedAccessException)
        {
            // Fall back to the per-user location.
        }

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppDataFolderName);
    }
}
