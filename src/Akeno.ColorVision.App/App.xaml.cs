using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Akeno.ColorVision.App.Services;
using Akeno.ColorVision.App.ViewModels;
using Akeno.ColorVision.App.Views;
using Akeno.ColorVision.Core.Diagnostics;
using Akeno.ColorVision.Shared;

namespace Akeno.ColorVision.App;

public partial class App : Application
{
    private Mutex? _singleInstance;
    private EventWaitHandle? _activateEvent;
    private AppHost? _host;
    private MainWindow? _window;
    private OsdWindow? _osd;
    private int _unhandledCount;

    public static AppHost Host => ((App)Current)._host ?? throw new InvalidOperationException("The app has not started.");

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var options = StartupOptions.Parse(e.Args);
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            options = options with { SafeModeRequested = true };
        }

        // Single instance: a second start just brings the existing window to the front.
        _singleInstance = new Mutex(true, RecoveryProtocol.SingleInstanceMutexName, out var createdNew);
        if (!createdNew)
        {
            try
            {
                if (EventWaitHandle.TryOpenExisting(RecoveryProtocol.ActivateEventName, out var activate))
                {
                    using (activate)
                    {
                        activate.Set();
                    }
                }
            }
            catch (Exception)
            {
                // Nothing else to do.
            }

            _singleInstance.Dispose();
            _singleInstance = null;
            Shutdown(0);
            return;
        }

        try
        {
            _host = AppHost.Create(options, Dispatcher);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Akeno ColorVision could not start:\n\n" + ex.Message, "Akeno ColorVision", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        RegisterExceptionHandlers();
        ThemeService.Apply(_host.Settings.Theme);
        _host.Start();

        _activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, RecoveryProtocol.ActivateEventName);
        _host.SystemThread.AddWaitHandle(_activateEvent, () => Dispatcher.BeginInvoke(ShowMainWindow));

        var tray = _host.Tray!;
        tray.ShowMainWindowRequested += (_, _) => ShowMainWindow();
        tray.NavigateRequested += (_, page) =>
        {
            ShowMainWindow();
            _window?.ViewModel.Navigate(page);
        };
        tray.ExitRequested += (_, _) => ExitApplication();

        _host.Hotkeys.ToggleRequested += (_, _) =>
        {
            _host.ToggleFilterFromUser();
            ShowOsd(_host.Filter.IsOn ? "Filter ON" : "Filter OFF", _host.Filter.IsOn ? _host.Profiles.Active.Name : "Normal colors");
        };
        _host.Hotkeys.IdentifyRequested += (_, _) => _host.IdentifierUi.OnShortcut();
        _host.Hotkeys.EmergencyResetPerformed += (_, _) => ShowOsd("Colors reset", "The filter is off.", force: true);

        if (!(options.StartInTray || _host.Settings.StartMinimizedToTray))
        {
            ShowMainWindow();
        }
        else
        {
            tray.ShowRunningInTrayHint();
        }
    }

    public void ShowMainWindow()
    {
        if (_host is null)
        {
            return;
        }

        if (_window is null)
        {
            _window = new MainWindow(new MainViewModel(_host));
            _window.Closed += (_, _) => _window = null;
            MainWindow = _window;
        }

        if (!_window.IsVisible)
        {
            _window.Show();
        }

        if (_window.WindowState == WindowState.Minimized)
        {
            _window.WindowState = WindowState.Normal;
        }

        _window.Activate();
    }

    public void ExitApplication()
    {
        _host?.Shutdown();
        _window?.AllowRealClose();
        _window?.Close();
        Shutdown(0);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _host?.Shutdown();
        _activateEvent?.Dispose();
        if (_singleInstance is not null)
        {
            try
            {
                _singleInstance.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // Not owned; ignore.
            }

            _singleInstance.Dispose();
        }

        base.OnExit(e);
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        // Windows is signing out or shutting down: restore normal colors and exit cleanly.
        _host?.Shutdown();
        base.OnSessionEnding(e);
    }

    private void ShowOsd(string title, string detail, bool force = false)
    {
        if (_host is null || (!force && !_host.Settings.ShowShortcutConfirmations))
        {
            return;
        }

        if (!force && _host.Settings.Gaming.QuietWhileGaming && _host.Games.InGame)
        {
            return;
        }

        _osd ??= new OsdWindow();
        _osd.ShowMessage(title, detail);
    }

    private void RegisterExceptionHandlers()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            _host?.Log.Error("Fatal unhandled exception.", args.ExceptionObject as Exception);
            try
            {
                // Make sure the screen is not left filtered if the process is about to die.
                _host?.Filter.Controller.Backend.ForceIdentity();
            }
            catch (Exception)
            {
                // The watchdog / Windows will take over.
            }
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            _host?.Log.Error("Unobserved task exception.", args.Exception);
            args.SetObserved();
        };
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _unhandledCount++;
        _host?.Log.Error("Unhandled UI exception.", e.Exception);
        if (_unhandledCount > 5)
        {
            // Something is badly wrong: reset colors and let the app close.
            try
            {
                _host?.Filter.EmergencyReset();
            }
            catch (Exception)
            {
                // Ignore; shutting down.
            }

            return;
        }

        e.Handled = true;
        MessageBox.Show(
            "Something went wrong, but Akeno ColorVision is still running.\n\n" + e.Exception.Message +
            "\n\nIf the screen colors look wrong, press " + (_host?.Settings.Hotkeys.EmergencyReset ?? "the emergency shortcut") + " or use \"Reset colors\".",
            "Akeno ColorVision", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}
