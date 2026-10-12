using System.Diagnostics;
using System.IO;
using Akeno.ColorVision.Core.Diagnostics;
using Akeno.ColorVision.Shared;

namespace Akeno.ColorVision.App.Services;

/// <summary>
/// Starts the emergency-reset tool in watchdog mode. If this app ever exits without a clean
/// shutdown, the watchdog resets the screen colors. Optional: the app works without it, but the
/// diagnostics page reports when it is missing.
/// </summary>
public static class WatchdogLauncher
{
    public static string? RecoveryToolPath
    {
        get
        {
            var path = Path.Combine(AppContext.BaseDirectory, RecoveryProtocol.RecoveryExecutableName);
            return File.Exists(path) ? path : null;
        }
    }

    public static Process? Start(IAppLogger log)
    {
        var path = RecoveryToolPath;
        if (path is null)
        {
            log.Warn($"{RecoveryProtocol.RecoveryExecutableName} not found next to the app; crash watchdog not running.");
            return null;
        }

        try
        {
            var process = Process.Start(new ProcessStartInfo(path)
            {
                Arguments = $"--watchdog --pid {Environment.ProcessId}",
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = AppContext.BaseDirectory,
            });
            log.Info("Crash watchdog started.");
            return process;
        }
        catch (Exception ex)
        {
            log.Warn("Could not start the crash watchdog.", ex);
            return null;
        }
    }
}
