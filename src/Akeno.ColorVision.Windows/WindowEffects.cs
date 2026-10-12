using Akeno.ColorVision.Windows.Native;

namespace Akeno.ColorVision.Windows;

/// <summary>Small window-level helpers used by the UI.</summary>
public static class WindowEffects
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_TRANSPARENT = 0x00000020;
    private const long WS_EX_TOOLWINDOW = 0x00000080;
    private const long WS_EX_NOACTIVATE = 0x08000000;
    private const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;

    /// <summary>Uses a dark title bar on Windows 10 20H1+ / Windows 11. Ignored where unsupported.</summary>
    public static void SetDarkTitleBar(IntPtr hwnd, bool dark)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        try
        {
            var value = dark ? 1 : 0;
            NativeMethods.DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref value, sizeof(int));
        }
        catch (Exception)
        {
            // Cosmetic only.
        }
    }

    /// <summary>Makes a popup ignore the mouse and never take focus (so games keep focus).</summary>
    public static void MakeClickThroughNoActivate(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var style = NativeMethods.GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
        style |= WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
        NativeMethods.SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(style));
    }

    /// <summary>Makes a popup that should never take focus (but still receives clicks).</summary>
    public static void MakeNoActivate(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var style = NativeMethods.GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
        style |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
        NativeMethods.SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(style));
    }

    /// <summary>Hides a window from screenshots/screen sampling (Windows 10 2004+). Returns false where unsupported.</summary>
    public static bool ExcludeFromCapture(IntPtr hwnd) =>
        hwnd != IntPtr.Zero && NativeMethods.SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE);
}
