using System.Diagnostics;
using System.Runtime.InteropServices;
using Akeno.ColorVision.Windows.Native;
using Microsoft.Win32;

namespace Akeno.ColorVision.Windows;

public sealed record MonitorDescription(string DeviceName, int Width, int Height, int Dpi, bool IsPrimary)
{
    public int ScalePercent => (int)Math.Round(Dpi / 96.0 * 100);
}

/// <summary>Facts about the PC that affect how colors look or whether the filter can work.</summary>
public sealed record DisplayEnvironment(
    string OsDescription,
    int OsBuild,
    bool Is64BitProcess,
    bool Is64BitOs,
    IReadOnlyList<MonitorDescription> Monitors,
    bool? AdvancedColorEnabled,
    bool? WindowsColorFiltersActive,
    string? WindowsColorFilterName,
    bool MagnifierRunning)
{
    public bool IsWindows11OrLater => OsBuild >= 22000;
}

/// <summary>
/// Read-only environment checks. Every probe is best-effort: if a probe fails the value is
/// reported as unknown (null) instead of guessing.
/// </summary>
public static class EnvironmentInspector
{
    public static DisplayEnvironment Inspect()
    {
        var (filtersOn, filterName) = ReadWindowsColorFilters();
        return new DisplayEnvironment(
            RuntimeInformation.OSDescription,
            Environment.OSVersion.Version.Build,
            Environment.Is64BitProcess,
            Environment.Is64BitOperatingSystem,
            SafeMonitors(),
            DisplayConfig.AnyAdvancedColorEnabled(),
            filtersOn,
            filterName,
            IsMagnifierRunning());
    }

    public static bool IsMagnifierRunning()
    {
        try
        {
            var processes = Process.GetProcessesByName("Magnify");
            var running = processes.Length > 0;
            foreach (var p in processes)
            {
                p.Dispose();
            }

            return running;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Windows' own color filters (Settings → Accessibility → Color filters) store their state in
    /// HKCU\Software\Microsoft\ColorFiltering. They stack with this app's filter, so we warn when both are on.
    /// </summary>
    public static (bool? Active, string? Name) ReadWindowsColorFilters()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\ColorFiltering");
            if (key is null)
            {
                return (false, null);
            }

            var active = key.GetValue("Active") is int a && a != 0;
            var type = key.GetValue("FilterType") is int t ? t : -1;
            var name = type switch
            {
                0 => "Grayscale",
                1 => "Inverted",
                2 => "Grayscale inverted",
                3 => "Red-green (green weak, deuteranopia)",
                4 => "Red-green (red weak, protanopia)",
                5 => "Blue-yellow (tritanopia)",
                _ => null,
            };
            return (active, name);
        }
        catch (Exception)
        {
            return (null, null);
        }
    }

    private static IReadOnlyList<MonitorDescription> SafeMonitors()
    {
        try
        {
            return EnumerateMonitors();
        }
        catch (Exception)
        {
            return [];
        }
    }

    public static IReadOnlyList<MonitorDescription> EnumerateMonitors()
    {
        var list = new List<MonitorDescription>();
        NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr monitor, IntPtr hdc, ref NativeMethods.RECT rect, IntPtr data) =>
        {
            var info = new NativeMethods.MONITORINFOEX { cbSize = Marshal.SizeOf<NativeMethods.MONITORINFOEX>(), szDevice = string.Empty };
            if (NativeMethods.GetMonitorInfo(monitor, ref info))
            {
                var dpi = 96u;
                try
                {
                    if (NativeMethods.GetDpiForMonitor(monitor, 0 /* MDT_EFFECTIVE_DPI */, out var dx, out _) == 0)
                    {
                        dpi = dx;
                    }
                }
                catch (EntryPointNotFoundException)
                {
                    // Older Windows; keep 96.
                }
                catch (DllNotFoundException)
                {
                    // Older Windows; keep 96.
                }

                list.Add(new MonitorDescription(
                    info.szDevice.TrimEnd('\0'),
                    info.rcMonitor.Right - info.rcMonitor.Left,
                    info.rcMonitor.Bottom - info.rcMonitor.Top,
                    (int)dpi,
                    (info.dwFlags & 1) != 0));
            }

            return true;
        }, IntPtr.Zero);
        return list;
    }

    /// <summary>Queries "advanced color" (HDR / auto color management) state via the DisplayConfig API.</summary>
    private static class DisplayConfig
    {
        private const uint QDC_ONLY_ACTIVE_PATHS = 0x00000002;
        private const uint DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO = 9;

        [StructLayout(LayoutKind.Sequential)]
        private struct LUID
        {
            public uint LowPart;
            public int HighPart;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DISPLAYCONFIG_PATH_SOURCE_INFO
        {
            public LUID adapterId;
            public uint id;
            public uint modeInfoIdx;
            public uint statusFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DISPLAYCONFIG_RATIONAL
        {
            public uint Numerator;
            public uint Denominator;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DISPLAYCONFIG_PATH_TARGET_INFO
        {
            public LUID adapterId;
            public uint id;
            public uint modeInfoIdx;
            public uint outputTechnology;
            public uint rotation;
            public uint scaling;
            public DISPLAYCONFIG_RATIONAL refreshRate;
            public uint scanLineOrdering;
            public int targetAvailable;
            public uint statusFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DISPLAYCONFIG_PATH_INFO
        {
            public DISPLAYCONFIG_PATH_SOURCE_INFO sourceInfo;
            public DISPLAYCONFIG_PATH_TARGET_INFO targetInfo;
            public uint flags;
        }

        [StructLayout(LayoutKind.Sequential, Size = 64)]
        private struct DISPLAYCONFIG_MODE_INFO
        {
            public uint infoType;
            public uint id;
            public LUID adapterId;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DISPLAYCONFIG_DEVICE_INFO_HEADER
        {
            public uint type;
            public uint size;
            public LUID adapterId;
            public uint id;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO
        {
            public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
            public uint value;
            public uint colorEncoding;
            public uint bitsPerColorChannel;
        }

        [DllImport("user32.dll")]
        private static extern int GetDisplayConfigBufferSizes(uint flags, out uint numPathArrayElements, out uint numModeInfoArrayElements);

        [DllImport("user32.dll")]
        private static extern int QueryDisplayConfig(uint flags, ref uint numPathArrayElements, [Out] DISPLAYCONFIG_PATH_INFO[] pathArray, ref uint numModeInfoArrayElements, [Out] DISPLAYCONFIG_MODE_INFO[] modeInfoArray, IntPtr currentTopologyId);

        [DllImport("user32.dll")]
        private static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO requestPacket);

        public static bool? AnyAdvancedColorEnabled()
        {
            try
            {
                if (Marshal.SizeOf<DISPLAYCONFIG_PATH_INFO>() != 72 || Marshal.SizeOf<DISPLAYCONFIG_MODE_INFO>() != 64)
                {
                    return null; // layout sanity check failed; do not guess
                }

                if (GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out var pathCount, out var modeCount) != 0)
                {
                    return null;
                }

                var paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
                var modes = new DISPLAYCONFIG_MODE_INFO[modeCount];
                if (QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero) != 0)
                {
                    return null;
                }

                var any = false;
                var queried = false;
                for (var i = 0; i < pathCount; i++)
                {
                    var info = new DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO
                    {
                        header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
                        {
                            type = DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO,
                            size = (uint)Marshal.SizeOf<DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO>(),
                            adapterId = paths[i].targetInfo.adapterId,
                            id = paths[i].targetInfo.id,
                        },
                    };
                    if (DisplayConfigGetDeviceInfo(ref info) == 0)
                    {
                        queried = true;
                        any |= (info.value & 0x2) != 0; // advancedColorEnabled
                    }
                }

                return queried ? any : null;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
