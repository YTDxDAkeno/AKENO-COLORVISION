using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Akeno.ColorVision.Shared;

[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]

namespace Akeno.ColorVision.Recovery;

/// <summary>
/// AkenoColorVision-EmergencyReset.exe
/// <para>
/// 1. Double-click (no arguments): asks the running app to reset; if it does not answer, closes it;
///    then resets the full-screen color effect itself and makes the next app start use safe mode.
/// </para>
/// <para>
/// 2. <c>--watchdog --pid N</c>: started by the app. Waits for the app process to exit; if it exits
///    without a clean shutdown (crash, killed in Task Manager), resets the screen colors.
/// </para>
/// Deliberately tiny and independent: no UI framework, no settings parsing, no shared code
/// beyond file/event names, so it still works if the main app or its settings are broken.
/// </summary>
internal static class Program
{
    private const uint MB_OK = 0x0;
    private const uint MB_ICONINFORMATION = 0x40;
    private const uint MB_ICONWARNING = 0x30;
    private const uint MB_SETFOREGROUND = 0x10000;
    private const uint MB_TOPMOST = 0x40000;

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (args.Any(a => a.Equals("--watchdog", StringComparison.OrdinalIgnoreCase)))
            {
                return RunWatchdog(args);
            }

            if (args.Any(a => a is "--help" or "-h" or "/?"))
            {
                Show("Akeno ColorVision Emergency Reset\n\n" +
                     "Run without arguments to restore normal screen colors.\n" +
                     "--quiet    reset without showing a message\n", MB_ICONINFORMATION);
                return 0;
            }

            return RunManualReset(quiet: args.Any(a => a.Equals("--quiet", StringComparison.OrdinalIgnoreCase)));
        }
        catch (Exception ex)
        {
            // As a last resort still try to reset the colors.
            var reset = ResetColorEffect();
            Show($"Akeno ColorVision Emergency Reset ran into a problem:\n{ex.Message}\n\nColor reset: {reset}", MB_ICONWARNING);
            return 1;
        }
    }

    private static int RunManualReset(bool quiet)
    {
        var notes = new List<string>();

        // 1) Ask the running app to reset itself (handled on a background thread, independent of its UI).
        var appAnswered = false;
        var appRunning = IsAppRunning();
        if (appRunning)
        {
            try
            {
                using var ack = new EventWaitHandle(false, EventResetMode.AutoReset, RecoveryProtocol.EmergencyResetAckEventName);
                if (EventWaitHandle.TryOpenExisting(RecoveryProtocol.EmergencyResetEventName, out var request))
                {
                    using (request)
                    {
                        request.Set();
                    }

                    appAnswered = ack.WaitOne(TimeSpan.FromSeconds(2.5));
                }
            }
            catch (Exception ex) when (ex is WaitHandleCannotBeOpenedException or UnauthorizedAccessException or IOException)
            {
                notes.Add("Could not contact the app: " + ex.Message);
            }

            notes.Add(appAnswered ? "The app turned its filter off." : "The app did not respond.");
        }

        // 2) If the app is unresponsive, close it. Windows and this tool then restore normal colors.
        if (appRunning && !appAnswered)
        {
            var killed = KillApp();
            notes.Add(killed > 0 ? "The unresponsive app was closed." : "The app could not be closed.");
        }

        // 3) Reset the full-screen color effect from this process as well.
        notes.Add("Color reset: " + ResetColorEffect());

        // 4) Make the next start use safe mode (filter stays off until turned on).
        var dataDir = RecoveryProtocol.GetDataDirectory();
        TryWrite(Path.Combine(dataDir, RecoveryProtocol.SafeModeFlagFile), DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture));
        TryWrite(Path.Combine(dataDir, RecoveryProtocol.LastRecoveryFile),
            string.Create(CultureInfo.InvariantCulture, $"Emergency reset tool used at {DateTimeOffset.Now:yyyy-MM-dd HH:mm}."));

        if (!quiet)
        {
            Show("Screen colors have been reset to normal.\n\n" + string.Join("\n", notes) +
                 "\n\nThe next time Akeno ColorVision starts, the filter will stay off until you turn it on.", MB_ICONINFORMATION);
        }

        return 0;
    }

    private static int RunWatchdog(string[] args)
    {
        var pidIndex = Array.FindIndex(args, a => a.Equals("--pid", StringComparison.OrdinalIgnoreCase));
        if (pidIndex < 0 || pidIndex + 1 >= args.Length || !int.TryParse(args[pidIndex + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var pid))
        {
            return 2;
        }

        Process process;
        try
        {
            process = Process.GetProcessById(pid);
        }
        catch (ArgumentException)
        {
            return 0; // Already gone.
        }

        using (process)
        {
            process.WaitForExit(); // Blocks without using CPU.
        }

        // Give the app's own shutdown a moment to delete the session lock.
        Thread.Sleep(500);
        var dataDir = RecoveryProtocol.GetDataDirectory();
        var lockFile = Path.Combine(dataDir, RecoveryProtocol.SessionLockFile);
        if (!File.Exists(lockFile))
        {
            return 0; // Clean exit.
        }

        var result = ResetColorEffect();
        TryWrite(Path.Combine(dataDir, RecoveryProtocol.LastRecoveryFile),
            string.Create(CultureInfo.InvariantCulture, $"Akeno ColorVision closed unexpectedly at {DateTimeOffset.Now:yyyy-MM-dd HH:mm}. Screen colors were reset automatically ({result})."));
        return 0;
    }

    private static bool IsAppRunning()
    {
        var processes = Process.GetProcessesByName(RecoveryProtocol.MainProcessName);
        var running = processes.Length > 0;
        foreach (var p in processes)
        {
            p.Dispose();
        }

        return running;
    }

    private static int KillApp()
    {
        var count = 0;
        var session = Process.GetCurrentProcess().SessionId;
        foreach (var p in Process.GetProcessesByName(RecoveryProtocol.MainProcessName))
        {
            using (p)
            {
                try
                {
                    if (p.SessionId != session)
                    {
                        continue;
                    }

                    p.Kill();
                    p.WaitForExit(3000);
                    count++;
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
                {
                    // Could not close this one; continue with the others.
                }
            }
        }

        return count;
    }

    /// <summary>Sets an identity full-screen color effect, then releases the Magnification API.</summary>
    private static string ResetColorEffect()
    {
        try
        {
            if (!MagInitialize())
            {
                return "the Magnification API could not be initialized";
            }

            try
            {
                var identity = new float[25];
                identity[0] = identity[6] = identity[12] = identity[18] = identity[24] = 1f;
                return MagSetFullscreenColorEffect(identity) ? "normal colors set" : "setting normal colors failed";
            }
            finally
            {
                MagUninitialize();
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return "the Magnification API is not available on this system";
        }
    }

    private static void TryWrite(string path, string text)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Optional.
        }
    }

    private static void Show(string text, uint icon) =>
        MessageBoxW(IntPtr.Zero, text, "Akeno ColorVision", MB_OK | icon | MB_SETFOREGROUND | MB_TOPMOST);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

    [DllImport("Magnification.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MagInitialize();

    [DllImport("Magnification.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MagUninitialize();

    // MAGCOLOREFFECT is 25 consecutive floats; a float[25] marshals as a pointer to exactly that layout.
    [DllImport("Magnification.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MagSetFullscreenColorEffect([In] float[] effect);
}
