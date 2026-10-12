using System.Collections.Concurrent;
using System.ComponentModel;
using Akeno.ColorVision.Windows.Native;

namespace Akeno.ColorVision.Windows;

/// <summary>
/// A dedicated background STA thread that runs a real Win32 message loop and executes queued work.
/// <para>
/// Used for two things that must keep working even when the WPF UI thread is busy or hung:
/// (1) the Magnification API, which should be called consistently from one thread, and
/// (2) global hotkeys / the emergency-reset event, so that the emergency path is independent
/// of the UI. Messages are pumped with <c>MsgWaitForMultipleObjectsEx</c>, so the thread uses no
/// CPU while idle.
/// </para>
/// </summary>
public sealed class MessagePumpThread : IDisposable
{
    private readonly Thread _thread;
    private readonly ConcurrentQueue<Action> _queue = new();
    private readonly AutoResetEvent _wake = new(false);
    private readonly ManualResetEventSlim _started = new(false);
    private readonly List<(WaitHandle Handle, Action Callback)> _waits = [];
    private volatile bool _stopping;
    private uint _nativeThreadId;

    public MessagePumpThread(string name)
    {
        _thread = new Thread(Run)
        {
            IsBackground = true,
            Name = name,
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        _started.Wait();
    }

    /// <summary>Raised on the pump thread for every WM_HOTKEY (argument: hotkey id).</summary>
    public event Action<int>? HotkeyMessage;

    /// <summary>Raised on the pump thread if a queued action throws.</summary>
    public event Action<Exception>? UnhandledException;

    public bool IsCurrentThread => Environment.CurrentManagedThreadId == _thread.ManagedThreadId;

    public bool IsAlive => _thread.IsAlive && !_stopping;

    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        _queue.Enqueue(action);
        _wake.Set();
    }

    /// <summary>Runs <paramref name="func"/> on the pump thread and waits for the result.</summary>
    /// <exception cref="TimeoutException">The pump thread did not run the work in time.</exception>
    public T Invoke<T>(Func<T> func, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(func);
        if (IsCurrentThread)
        {
            return func();
        }

        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        Post(() =>
        {
            try
            {
                tcs.SetResult(func());
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });

        bool completed;
        try
        {
            completed = tcs.Task.Wait(timeout);
        }
        catch (AggregateException)
        {
            // Faulted: rethrown below with its original type (not wrapped in AggregateException).
            completed = true;
        }

        if (!completed)
        {
            throw new TimeoutException($"Thread '{_thread.Name}' did not respond within {timeout.TotalMilliseconds:0} ms.");
        }

        return tcs.Task.GetAwaiter().GetResult();
    }

    public void Invoke(Action action, TimeSpan timeout) => Invoke(() =>
    {
        action();
        return true;
    }, timeout);

    /// <summary>Calls <paramref name="callback"/> on the pump thread whenever <paramref name="handle"/> is signalled. Use auto-reset events.</summary>
    public void AddWaitHandle(WaitHandle handle, Action callback) => Post(() => _waits.Add((handle, callback)));

    public void Dispose()
    {
        if (_stopping)
        {
            return;
        }

        _stopping = true;
        _wake.Set();
        if (_nativeThreadId != 0)
        {
            NativeMethods.PostThreadMessage(_nativeThreadId, NativeMethods.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        }

        if (!IsCurrentThread)
        {
            _thread.Join(TimeSpan.FromSeconds(2));
        }
    }

    private void Run()
    {
        _nativeThreadId = NativeMethods.GetCurrentThreadId();

        // Force creation of the thread's message queue before anyone posts to it.
        NativeMethods.PeekMessage(out _, IntPtr.Zero, NativeMethods.WM_NULL, NativeMethods.WM_NULL, NativeMethods.PM_NOREMOVE);
        _started.Set();

        while (!_stopping)
        {
            var handles = new IntPtr[1 + _waits.Count];
            handles[0] = _wake.SafeWaitHandle.DangerousGetHandle();
            for (var i = 0; i < _waits.Count; i++)
            {
                handles[i + 1] = _waits[i].Handle.SafeWaitHandle.DangerousGetHandle();
            }

            var result = NativeMethods.MsgWaitForMultipleObjectsEx(
                (uint)handles.Length, handles, NativeMethods.INFINITE, NativeMethods.QS_ALLINPUT, NativeMethods.MWMO_INPUTAVAILABLE);

            if (result == NativeMethods.WAIT_FAILED)
            {
                Report(new Win32Exception());
                Thread.Sleep(50);
            }
            else if (result > NativeMethods.WAIT_OBJECT_0 && result < NativeMethods.WAIT_OBJECT_0 + handles.Length)
            {
                var index = (int)(result - NativeMethods.WAIT_OBJECT_0) - 1;
                if (index >= 0 && index < _waits.Count)
                {
                    SafeRun(_waits[index].Callback);
                }
            }

            PumpMessages();
            DrainQueue();
        }

        DrainQueue();
    }

    private void PumpMessages()
    {
        while (NativeMethods.PeekMessage(out var msg, IntPtr.Zero, 0, 0, NativeMethods.PM_REMOVE))
        {
            if (msg.message == NativeMethods.WM_QUIT)
            {
                _stopping = true;
                return;
            }

            if (msg.message == NativeMethods.WM_HOTKEY && msg.hwnd == IntPtr.Zero)
            {
                var id = (int)msg.wParam;
                SafeRun(() => HotkeyMessage?.Invoke(id));
                continue;
            }

            NativeMethods.TranslateMessage(ref msg);
            NativeMethods.DispatchMessage(ref msg);
        }
    }

    private void DrainQueue()
    {
        while (_queue.TryDequeue(out var action))
        {
            SafeRun(action);
        }
    }

    private void SafeRun(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Report(ex);
        }
    }

    private void Report(Exception ex)
    {
        try
        {
            UnhandledException?.Invoke(ex);
        }
        catch (Exception)
        {
            // Never let the pump die because of a listener.
        }
    }
}
