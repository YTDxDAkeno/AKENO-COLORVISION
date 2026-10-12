using System.Diagnostics;
using System.IO;
using System.Windows.Input;
using Akeno.ColorVision.App.Infrastructure;
using Akeno.ColorVision.App.Services;
using Akeno.ColorVision.App.Views;
using Akeno.ColorVision.Core.Hotkeys;
using Akeno.ColorVision.Core.Settings;
using Akeno.ColorVision.Windows;
using Microsoft.Win32;

namespace Akeno.ColorVision.App.ViewModels;

/// <summary>Settings page. Every change is saved immediately.</summary>
public sealed class SettingsViewModel : PageViewModel
{
    private string? _message;

    public SettingsViewModel(AppHost host)
        : base(host)
    {
        ResetShortcutsCommand = new RelayCommand(ResetShortcuts);
        OpenLogsCommand = new RelayCommand(() => OpenFolder(Host.FileLog.Directory));
        OpenDataCommand = new RelayCommand(() => OpenFolder(Host.DataDirectory));
        ExportDiagnosticsCommand = new RelayCommand(ExportDiagnostics);
        RestoreDefaultsCommand = new RelayCommand(RestoreDefaults);
        RunResetToolCommand = new RelayCommand(RunResetTool, () => WatchdogLauncher.RecoveryToolPath is not null);
    }

    private AppSettings S => Host.Settings;

    // ---------------------------------------------------------------- general

    public bool StartWithWindows
    {
        get => S.StartWithWindows;
        set
        {
            var error = value
                ? StartupRegistration.Enable(Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "AkenoColorVision.exe"))
                : StartupRegistration.Disable();
            if (error is not null)
            {
                Message = "Could not change the startup setting: " + error;
                OnPropertyChanged();
                return;
            }

            S.StartWithWindows = value;
            Save();
        }
    }

    public bool StartMinimizedToTray
    {
        get => S.StartMinimizedToTray;
        set
        {
            S.StartMinimizedToTray = value;
            Save();
        }
    }

    public bool CloseToTray
    {
        get => S.CloseToTray;
        set
        {
            S.CloseToTray = value;
            Save();
        }
    }

    public bool EnableFilterOnStartup
    {
        get => S.EnableFilterOnStartup;
        set
        {
            S.EnableFilterOnStartup = value;
            Save();
        }
    }

    public bool RememberLastProfile
    {
        get => S.RememberLastProfile;
        set
        {
            S.RememberLastProfile = value;
            Save();
        }
    }

    public bool SmoothTransitions
    {
        get => S.SmoothTransitions;
        set
        {
            S.SmoothTransitions = value;
            Host.Filter.SetSmoothTransitions(value);
            Save();
        }
    }

    public bool ShowShortcutConfirmations
    {
        get => S.ShowShortcutConfirmations;
        set
        {
            S.ShowShortcutConfirmations = value;
            Save();
        }
    }

    // ---------------------------------------------------------------- shortcuts

    public string ToggleShortcut
    {
        get => S.Hotkeys.ToggleFilter;
        set => ApplyShortcut(HotkeyAction.ToggleFilter, value);
    }

    public string EmergencyShortcut
    {
        get => S.Hotkeys.EmergencyReset;
        set => ApplyShortcut(HotkeyAction.EmergencyReset, value);
    }

    public string IdentifyShortcut
    {
        get => S.Hotkeys.IdentifyColor;
        set => ApplyShortcut(HotkeyAction.IdentifyColor, value);
    }

    public string ToggleStatus => StatusFor(HotkeyAction.ToggleFilter);

    public string EmergencyStatus => StatusFor(HotkeyAction.EmergencyReset);

    public string IdentifyStatus => StatusFor(HotkeyAction.IdentifyColor);

    // ---------------------------------------------------------------- appearance

    public bool DarkTheme
    {
        get => S.Theme == AppTheme.Dark;
        set
        {
            if (value)
            {
                SetTheme(AppTheme.Dark);
            }
        }
    }

    public bool LightTheme
    {
        get => S.Theme == AppTheme.Light;
        set
        {
            if (value)
            {
                SetTheme(AppTheme.Light);
            }
        }
    }

    // ---------------------------------------------------------------- privacy

    public bool ErrorLoggingEnabled
    {
        get => S.ErrorLoggingEnabled;
        set
        {
            S.ErrorLoggingEnabled = value;
            Host.FileLog.Enabled = value;
            Save();
        }
    }

    public string DataDirectory => Host.DataDirectory;

    public string ResetToolPath => WatchdogLauncher.RecoveryToolPath ?? "Not found next to the app. Keep AkenoColorVision-EmergencyReset.exe in the same folder as AkenoColorVision.exe.";

    public string? Message
    {
        get => _message;
        private set => SetProperty(ref _message, value);
    }

    public ICommand ResetShortcutsCommand { get; }

    public ICommand OpenLogsCommand { get; }

    public ICommand OpenDataCommand { get; }

    public ICommand ExportDiagnosticsCommand { get; }

    public ICommand RestoreDefaultsCommand { get; }

    public ICommand RunResetToolCommand { get; }

    public override void OnNavigatedTo() => RefreshAll();

    private void ApplyShortcut(HotkeyAction action, string text)
    {
        if (!HotkeyGesture.TryParse(text, out var gesture, out var error))
        {
            Message = error;
            RefreshShortcuts();
            return;
        }

        var problem = gesture.Validate();
        if (problem is not null)
        {
            Message = problem;
            RefreshShortcuts();
            return;
        }

        var others = new[] { S.Hotkeys.ToggleFilter, S.Hotkeys.EmergencyReset, S.Hotkeys.IdentifyColor }
            .Where((_, i) => i != (int)action - 1);
        if (others.Any(o => HotkeyGesture.TryParse(o, out var g, out _) && g == gesture))
        {
            Message = $"{gesture} is already used by another Akeno ColorVision shortcut.";
            RefreshShortcuts();
            return;
        }

        var previous = action switch
        {
            HotkeyAction.ToggleFilter => S.Hotkeys.ToggleFilter,
            HotkeyAction.EmergencyReset => S.Hotkeys.EmergencyReset,
            _ => S.Hotkeys.IdentifyColor,
        };

        var result = Host.Hotkeys.Register(action, gesture.ToString());
        if (!result.Success)
        {
            // Keep the previous working shortcut.
            Host.Hotkeys.Register(action, previous);
            Message = result.Message;
            RefreshShortcuts();
            return;
        }

        switch (action)
        {
            case HotkeyAction.ToggleFilter:
                S.Hotkeys.ToggleFilter = gesture.ToString();
                break;
            case HotkeyAction.EmergencyReset:
                S.Hotkeys.EmergencyReset = gesture.ToString();
                break;
            default:
                S.Hotkeys.IdentifyColor = gesture.ToString();
                break;
        }

        Message = $"Saved: {gesture}.";
        Save();
        RefreshShortcuts();
    }

    private string StatusFor(HotkeyAction action) =>
        Host.Hotkeys.Results.TryGetValue(action, out var r) ? (r.Success ? "Working" : "Not working: " + r.Message) : "Not registered";

    private void ResetShortcuts()
    {
        S.Hotkeys = new HotkeySettings();
        Save();
        Host.Hotkeys.RegisterAll();
        Message = "Shortcuts restored to their defaults.";
        RefreshShortcuts();
    }

    private void SetTheme(AppTheme theme)
    {
        S.Theme = theme;
        ThemeService.Apply(theme);
        Save();
        OnPropertiesChanged(nameof(DarkTheme), nameof(LightTheme));
    }

    private void ExportDiagnostics()
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export diagnostics",
            FileName = $"akeno-colorvision-diagnostics-{DateTime.Now:yyyyMMdd-HHmm}.txt",
            Filter = "Text file|*.txt",
        };
        if (dialog.ShowDialog() == true)
        {
            try
            {
                DiagnosticsBuilder.Export(Host, dialog.FileName);
                Message = "Diagnostics saved. They contain no screenshots or personal information.";
            }
            catch (Exception ex)
            {
                Message = "Could not save diagnostics: " + ex.Message;
            }
        }
    }

    private void RestoreDefaults()
    {
        if (!Dialogs.Confirm("Restore default settings",
                "Restore all settings to their defaults? Your profiles are kept. The filter and shortcuts are reset to default behaviour.", "Restore defaults"))
        {
            return;
        }

        var fresh = new AppSettings
        {
            FilterVisuallyConfirmedUtc = S.FilterVisuallyConfirmedUtc,
            HasCompletedFirstRun = S.HasCompletedFirstRun,
            Identifier = S.Identifier,
        };
        S.ActiveProfileId = fresh.ActiveProfileId;
        S.EnableFilterOnStartup = fresh.EnableFilterOnStartup;
        S.StartMinimizedToTray = fresh.StartMinimizedToTray;
        S.CloseToTray = fresh.CloseToTray;
        S.RememberLastProfile = fresh.RememberLastProfile;
        S.SmoothTransitions = fresh.SmoothTransitions;
        S.ShowShortcutConfirmations = fresh.ShowShortcutConfirmations;
        S.ErrorLoggingEnabled = fresh.ErrorLoggingEnabled;
        S.Hotkeys = fresh.Hotkeys;
        S.Gaming = fresh.Gaming;
        S.PresetIntensity.Clear();
        if (S.StartWithWindows)
        {
            StartupRegistration.Disable();
            S.StartWithWindows = false;
        }

        Save();
        Host.Hotkeys.RegisterAll();
        Host.Games.UpdateRunningState();
        Host.FileLog.Enabled = S.ErrorLoggingEnabled;
        Host.Filter.SetSmoothTransitions(S.SmoothTransitions);
        Host.Profiles.ResetPresetStrengths();
        Host.Profiles.SetActive(fresh.ActiveProfileId);
        SetTheme(AppTheme.Dark);
        Message = "Default settings restored.";
        RefreshAll();
    }

    private void RunResetTool()
    {
        try
        {
            Process.Start(new ProcessStartInfo(WatchdogLauncher.RecoveryToolPath!) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Message = "Could not start the reset tool: " + ex.Message;
        }
    }

    private void Save() => Host.SaveSettings();

    private void RefreshShortcuts() => OnPropertiesChanged(nameof(ToggleShortcut), nameof(EmergencyShortcut), nameof(IdentifyShortcut),
        nameof(ToggleStatus), nameof(EmergencyStatus), nameof(IdentifyStatus));

    private void RefreshAll()
    {
        OnPropertiesChanged(nameof(StartWithWindows), nameof(StartMinimizedToTray), nameof(CloseToTray), nameof(EnableFilterOnStartup),
            nameof(RememberLastProfile), nameof(SmoothTransitions), nameof(ShowShortcutConfirmations), nameof(DarkTheme), nameof(LightTheme),
            nameof(ErrorLoggingEnabled), nameof(DataDirectory), nameof(ResetToolPath));
        RefreshShortcuts();
    }

    private static void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = false });
        }
        catch (Exception)
        {
            // Explorer unavailable; ignore.
        }
    }
}
