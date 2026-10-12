using System.Runtime.InteropServices;
using Akeno.ColorVision.Core.Hotkeys;
using Akeno.ColorVision.Windows.Native;

namespace Akeno.ColorVision.Windows;

public sealed record HotkeyRegistrationResult(bool Success, string Message)
{
    public static HotkeyRegistrationResult Ok(HotkeyGesture g) => new(true, $"{g} is active.");
}

/// <summary>
/// Registers system-wide shortcuts with <c>RegisterHotKey</c> (the supported Win32 API — no
/// keyboard hooks, no key injection). Registration happens on a <see cref="MessagePumpThread"/>,
/// and <see cref="Pressed"/> is raised on that thread.
/// </summary>
public sealed class GlobalHotkeyManager : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);
    private readonly MessagePumpThread _pump;
    private readonly Dictionary<int, HotkeyGesture> _registered = [];

    public GlobalHotkeyManager(MessagePumpThread pump)
    {
        _pump = pump ?? throw new ArgumentNullException(nameof(pump));
        _pump.HotkeyMessage += OnHotkey;
    }

    /// <summary>Raised on the pump thread with the hotkey id.</summary>
    public event Action<int>? Pressed;

    public HotkeyRegistrationResult Register(int id, HotkeyGesture gesture)
    {
        ArgumentNullException.ThrowIfNull(gesture);
        var problem = gesture.Validate();
        if (problem is not null)
        {
            return new HotkeyRegistrationResult(false, problem);
        }

        try
        {
            return _pump.Invoke(() =>
            {
                if (_registered.ContainsKey(id))
                {
                    NativeMethods.UnregisterHotKey(IntPtr.Zero, id);
                    _registered.Remove(id);
                }

                var ok = NativeMethods.RegisterHotKey(IntPtr.Zero, id, (uint)gesture.Modifiers | NativeMethods.MOD_NOREPEAT, (uint)gesture.VirtualKey);
                if (!ok)
                {
                    var error = Marshal.GetLastWin32Error();
                    return new HotkeyRegistrationResult(false, error == NativeMethods.ERROR_HOTKEY_ALREADY_REGISTERED
                        ? $"{gesture} is already used by another program. Please choose a different shortcut."
                        : $"{gesture} could not be registered (Windows error {error}).");
                }

                _registered[id] = gesture;
                return HotkeyRegistrationResult.Ok(gesture);
            }, Timeout);
        }
        catch (TimeoutException ex)
        {
            return new HotkeyRegistrationResult(false, ex.Message);
        }
    }

    public void Unregister(int id)
    {
        try
        {
            _pump.Invoke(() =>
            {
                if (_registered.Remove(id))
                {
                    NativeMethods.UnregisterHotKey(IntPtr.Zero, id);
                }
            }, Timeout);
        }
        catch (TimeoutException)
        {
            // The pump is gone; the OS releases hotkeys when the thread exits.
        }
    }

    public void Dispose()
    {
        _pump.HotkeyMessage -= OnHotkey;
        try
        {
            _pump.Invoke(() =>
            {
                foreach (var id in _registered.Keys)
                {
                    NativeMethods.UnregisterHotKey(IntPtr.Zero, id);
                }

                _registered.Clear();
            }, Timeout);
        }
        catch (TimeoutException)
        {
            // Ignore on shutdown.
        }
    }

    private void OnHotkey(int id) => Pressed?.Invoke(id);
}
