using System.Text;
using Akeno.ColorVision.Core.Gaming;
using Akeno.ColorVision.Windows.Native;

namespace Akeno.ColorVision.Windows;

/// <summary>
/// Reports which application is in the foreground, using <c>SetWinEventHook(EVENT_SYSTEM_FOREGROUND)</c>
/// (an out-of-context accessibility event — nothing is injected into other processes) plus a slow
/// poll to notice window-title changes. Only the executable file name and the window title are read;
/// process memory is never accessed, which keeps this compatible with anti-cheat systems.
/// </summary>
public sealed class ForegroundWindowMonitor : IDisposable
{
    private readonly MessagePumpThread _pump;
    private readonly NativeMethods.WinEventDelegate _callback;
    private IntPtr _hook;
    private Timer? _poll;
    private ForegroundAppInfo _last = ForegroundAppInfo.Empty;

    public ForegroundWindowMonitor(MessagePumpThread pump)
    {
        _pump = pump ?? throw new ArgumentNullException(nameof(pump));
        _callback = OnWinEvent;
    }

    /// <summary>Raised on the pump thread when the foreground app or its title changes.</summary>
    public event Action<ForegroundAppInfo>? ForegroundChanged;

    public bool IsRunning => _hook != IntPtr.Zero;

    public void Start()
    {
        _pump.Invoke(() =>
        {
            if (_hook != IntPtr.Zero)
            {
                return;
            }

            _hook = NativeMethods.SetWinEventHook(
                NativeMethods.EVENT_SYSTEM_FOREGROUND, NativeMethods.EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, _callback, 0, 0,
                NativeMethods.WINEVENT_OUTOFCONTEXT | NativeMethods.WINEVENT_SKIPOWNPROCESS);
            Check();
        }, TimeSpan.FromSeconds(3));

        _poll ??= new Timer(_ => _pump.Post(Check), null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));
    }

    public void Stop()
    {
        _poll?.Dispose();
        _poll = null;
        try
        {
            _pump.Invoke(() =>
            {
                if (_hook != IntPtr.Zero)
                {
                    NativeMethods.UnhookWinEvent(_hook);
                    _hook = IntPtr.Zero;
                }

                _last = ForegroundAppInfo.Empty;
            }, TimeSpan.FromSeconds(3));
        }
        catch (TimeoutException)
        {
            // Ignore on shutdown.
        }
    }

    public void Dispose() => Stop();

    /// <summary>Information about the current foreground window (empty if unavailable).</summary>
    public static ForegroundAppInfo GetCurrent() => Describe(NativeMethods.GetForegroundWindow());

    /// <summary>Executable names of visible top-level windows, for the "add an app" picker.</summary>
    public static IReadOnlyList<ForegroundAppInfo> ListVisibleApps()
    {
        var list = new List<ForegroundAppInfo>();
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (NativeMethods.IsWindowVisible(hwnd) && NativeMethods.GetWindowTextLength(hwnd) > 0)
            {
                var info = Describe(hwnd);
                if (!string.IsNullOrEmpty(info.ExecutableName))
                {
                    list.Add(info);
                }
            }

            return true;
        }, IntPtr.Zero);

        return list
            .GroupBy(i => i.ExecutableName, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(i => i.ExecutableName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    internal static ForegroundAppInfo Describe(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return ForegroundAppInfo.Empty;
        }

        NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
        var title = GetTitle(hwnd);
        var exe = GetExecutableName(pid);
        return new ForegroundAppInfo(exe, title, (int)pid);
    }

    private static string GetTitle(IntPtr hwnd)
    {
        var length = NativeMethods.GetWindowTextLength(hwnd);
        if (length <= 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder(Math.Min(length + 1, 1024));
        NativeMethods.GetWindowText(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    private static string GetExecutableName(uint pid)
    {
        if (pid == 0)
        {
            return string.Empty;
        }

        var handle = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (handle == IntPtr.Zero)
        {
            return string.Empty; // e.g. elevated or protected processes
        }

        try
        {
            var size = 1024u;
            var sb = new StringBuilder((int)size);
            return NativeMethods.QueryFullProcessImageName(handle, 0, sb, ref size) ? Path.GetFileName(sb.ToString()) : string.Empty;
        }
        finally
        {
            NativeMethods.CloseHandle(handle);
        }
    }

    private void OnWinEvent(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time) => Check();

    private void Check()
    {
        ForegroundAppInfo info;
        try
        {
            info = GetCurrent();
        }
        catch (Exception)
        {
            return;
        }

        if (info.ProcessId == Environment.ProcessId)
        {
            // Our own window coming to the front should not end game mode instantly; treat as no change.
            return;
        }

        if (info == _last)
        {
            return;
        }

        _last = info;
        ForegroundChanged?.Invoke(info);
    }
}
