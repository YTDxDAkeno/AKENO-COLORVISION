using System.Diagnostics;
using System.IO;
using System.Windows.Threading;
using Akeno.ColorVision.App.Services;
using Akeno.ColorVision.Core.Diagnostics;
using Akeno.ColorVision.Core.Filtering;
using Akeno.ColorVision.Core.Profiles;
using Akeno.ColorVision.Core.Recovery;
using Akeno.ColorVision.Core.Settings;
using Akeno.ColorVision.Rendering;
using Akeno.ColorVision.Shared;
using Akeno.ColorVision.Windows;

namespace Akeno.ColorVision.App;

/// <summary>Command-line options.</summary>
public sealed record StartupOptions(bool StartInTray, bool SafeModeRequested)
{
    public static StartupOptions Parse(IEnumerable<string> args)
    {
        var list = args.Select(a => a.Trim().ToLowerInvariant()).ToList();
        return new StartupOptions(
            list.Contains("--tray") || list.Contains("--minimized"),
            list.Contains("--safe-mode") || list.Contains("--safemode"));
    }
}

/// <summary>
/// Composition root: creates and wires every service, and owns the shutdown sequence
/// (filter off → restore → clean-exit marker) so the screen is always left in a normal state.
/// </summary>
public sealed class AppHost : IDisposable
{
    private readonly MessagePumpThread _systemThread;
    private Process? _watchdog;
    private bool _shutDown;

    private AppHost(StartupOptions options, Dispatcher ui)
    {
        Ui = ui;
        DataDirectory = RecoveryProtocol.GetDataDirectory();
        Directory.CreateDirectory(DataDirectory);

        FileLog = new FileLogger(Path.Combine(DataDirectory, "logs"));
        MemoryLog = new MemoryLogger();
        Log = new CompositeLogger(FileLog, MemoryLog);
        FileLog.Prune();
        Log.Info($"Akeno ColorVision {Version} starting. OS: {Environment.OSVersion.VersionString}, 64-bit process: {Environment.Is64BitProcess}.");

        SettingsStore = new SettingsStore(DataDirectory, Log);
        Settings = SettingsStore.Load(out var settingsProblem);
        FileLog.Enabled = Settings.ErrorLoggingEnabled;
        StartupProblems = settingsProblem is null ? [] : [settingsProblem];

        Session = new SessionGuard(DataDirectory).Begin(Environment.ProcessId, options.SafeModeRequested);
        if (Session.SafeMode)
        {
            Log.Warn($"Starting in safe mode: {Session.Reasons}.");
        }

        Profiles = new ProfileService(new ProfileStore(Path.Combine(DataDirectory, "profiles"), Log), Settings, SaveSettings, Log);
        foreach (var issue in Profiles.LoadIssues)
        {
            StartupProblems.Add($"Profile file '{issue.FileName}': {issue.Message}");
        }

        var backend = BackendCatalog.Create(Profiles.Active.Backend, Log);
        Filter = new FilterService(new FilterController(backend, Log), Profiles, Settings, Log, ui);

        _systemThread = new MessagePumpThread("Akeno Hotkeys");
        _systemThread.UnhandledException += ex => Log.Error("Error on the hotkey thread.", ex);
        Hotkeys = new HotkeyService(_systemThread, Filter, Settings, Log, ui);
        Games = new GameDetectionService(_systemThread, Profiles, Filter, Settings, Log, ui);
        Identifier = new IdentifierService(Filter, Settings, SaveSettings, Log);
        Options = options;
    }

    public static string Version { get; } =
        typeof(AppHost).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion.Split('+')[0] ?? "0.1.0";

    public Dispatcher Ui { get; }

    public StartupOptions Options { get; }

    public string DataDirectory { get; }

    public FileLogger FileLog { get; }

    public MemoryLogger MemoryLog { get; }

    public IAppLogger Log { get; }

    public SettingsStore SettingsStore { get; }

    public AppSettings Settings { get; }

    public SessionStart Session { get; }

    public List<string> StartupProblems { get; }

    public ProfileService Profiles { get; }

    public FilterService Filter { get; }

    public HotkeyService Hotkeys { get; }

    public GameDetectionService Games { get; }

    public IdentifierService Identifier { get; }

    public TrayService? Tray { get; private set; }

    public IdentifierCoordinator IdentifierUi { get; private set; } = null!;

    public MessagePumpThread SystemThread => _systemThread;

    public bool WatchdogRunning => _watchdog is { HasExited: false };

    public static AppHost Create(StartupOptions options, Dispatcher ui) => new(options, ui);

    /// <summary>Second phase of startup, after the WPF application object exists.</summary>
    public void Start()
    {
        _watchdog = WatchdogLauncher.Start(Log);
        IdentifierUi = new IdentifierCoordinator(this);

        var init = Filter.Initialize();
        if (!init.Success)
        {
            StartupProblems.Add("The color filter could not be started: " + init.Message);
        }
        else if (Settings.EnableFilterOnStartup && !Session.SafeMode)
        {
            Filter.Enable();
        }

        Hotkeys.RegisterAll();
        Games.UpdateRunningState();
        Tray = new TrayService(this);
        Filter.StatusChanged += (_, _) => Tray?.UpdateTooltip();
        Profiles.ActiveProfileChanged += (_, _) => Tray?.UpdateTooltip();
        Tray.UpdateTooltip();
    }

    public void SaveSettings() => SettingsStore.Save(Settings);

    /// <summary>Toggle requested directly by the user (button, tray, shortcut).</summary>
    public void ToggleFilterFromUser()
    {
        Games.NotifyManualFilterToggle();
        Filter.Toggle();
    }

    public void Shutdown()
    {
        if (_shutDown)
        {
            return;
        }

        _shutDown = true;
        Log.Info("Shutting down.");
        try
        {
            Games.Dispose();
            Hotkeys.Dispose();
        }
        catch (Exception ex)
        {
            Log.Error("Error while stopping background services.", ex);
        }

        try
        {
            Filter.Dispose();
        }
        catch (Exception ex)
        {
            Log.Error("Error while turning the filter off.", ex);
        }

        Tray?.Dispose();
        _systemThread.Dispose();
        SaveSettings();
        new SessionGuard(DataDirectory).MarkCleanExit();
        Log.Info("Clean exit.");
    }

    public void Dispose() => Shutdown();
}
