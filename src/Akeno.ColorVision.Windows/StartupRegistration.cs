using Microsoft.Win32;

namespace Akeno.ColorVision.Windows;

/// <summary>
/// Opt-in "start with Windows" using the per-user Run key (HKCU — no administrator rights needed).
/// </summary>
public static class StartupRegistration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "AkenoColorVision";

    public static bool IsEnabled(out string? command)
    {
        command = null;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            command = key?.GetValue(ValueName) as string;
            return !string.IsNullOrEmpty(command);
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    /// <summary>Registers (or updates) the startup entry. Returns an error message or null.</summary>
    public static string? Enable(string executablePath, string arguments = "--tray")
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            key.SetValue(ValueName, $"\"{executablePath}\" {arguments}".TrimEnd(), RegistryValueKind.String);
            return null;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return ex.Message;
        }
    }

    public static string? Disable()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            key?.DeleteValue(ValueName, throwOnMissingValue: false);
            return null;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return ex.Message;
        }
    }
}
