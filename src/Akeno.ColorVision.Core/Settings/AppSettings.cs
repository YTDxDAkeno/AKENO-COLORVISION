using System.Text.Json;
using Akeno.ColorVision.Core.Diagnostics;
using Akeno.ColorVision.Core.Gaming;
using Akeno.ColorVision.Core.Hotkeys;
using Akeno.ColorVision.Core.Profiles;

namespace Akeno.ColorVision.Core.Settings;

public enum AppTheme
{
    Dark,
    Light,
}

public sealed class GamingSettings
{
    /// <summary>Switch to the game profile automatically when a known game is in front.</summary>
    public bool AutoSwitchProfiles { get; set; } = true;

    /// <summary>Also turn the filter on while the game is in front (off = only switch profile if the filter is already on).</summary>
    public bool EnableFilterWhileGaming { get; set; }

    /// <summary>Profile used for Minecraft (and for user rules without their own profile).</summary>
    public Guid MinecraftProfileId { get; set; } = BuiltInPresets.MinecraftNetherId;

    public double RestoreDelaySeconds { get; set; } = 1.5;

    /// <summary>Suppress on-screen confirmations while a detected game is in front.</summary>
    public bool QuietWhileGaming { get; set; } = true;

    /// <summary>User-defined rules (built-in Minecraft rules are added in code).</summary>
    public List<GameRule> CustomRules { get; set; } = [];

    /// <summary>Ids of built-in rules the user switched off.</summary>
    public List<Guid> DisabledBuiltInRules { get; set; } = [];
}

/// <summary>Records how the color identifier's screen sampling behaves on this PC (measured by its self-check).</summary>
public enum SamplingSource
{
    Unknown,
    OriginalColors,
    FilteredColors,
}

public sealed class IdentifierSettings
{
    public bool ShowHex { get; set; } = true;

    public bool ShowRgb { get; set; } = true;

    public SamplingSource MeasuredSamplingSource { get; set; } = SamplingSource.Unknown;

    public DateTimeOffset? SamplingCheckedUtc { get; set; }
}

/// <summary>All persisted application settings. Everything is stored locally; nothing is uploaded.</summary>
public sealed class AppSettings
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public Guid ActiveProfileId { get; set; } = BuiltInPresets.RedGreenId;

    /// <summary>Turn the filter on when the app starts (opt-in; skipped in safe mode).</summary>
    public bool EnableFilterOnStartup { get; set; }

    public bool StartWithWindows { get; set; }

    public bool StartMinimizedToTray { get; set; }

    /// <summary>Closing the window keeps the app running in the tray.</summary>
    public bool CloseToTray { get; set; } = true;

    public bool RememberLastProfile { get; set; } = true;

    /// <summary>Animate profile changes (short cross-fade between matrices).</summary>
    public bool SmoothTransitions { get; set; } = true;

    /// <summary>Show a small on-screen confirmation when a shortcut toggles the filter.</summary>
    public bool ShowShortcutConfirmations { get; set; } = true;

    public AppTheme Theme { get; set; } = AppTheme.Dark;

    public bool ErrorLoggingEnabled { get; set; } = true;

    public HotkeySettings Hotkeys { get; set; } = new();

    public GamingSettings Gaming { get; set; } = new();

    public IdentifierSettings Identifier { get; set; } = new();

    /// <summary>Strength overrides for built-in presets (preset id → intensity 0..1).</summary>
    public Dictionary<Guid, double> PresetIntensity { get; set; } = [];

    /// <summary>Set when the user confirmed in the app that the filter visibly changes their screen.</summary>
    public DateTimeOffset? FilterVisuallyConfirmedUtc { get; set; }

    public bool HasCompletedFirstRun { get; set; }

    public IReadOnlyList<string> Normalize()
    {
        var notes = new List<string>();
        Hotkeys ??= new HotkeySettings();
        Gaming ??= new GamingSettings();
        Identifier ??= new IdentifierSettings();
        PresetIntensity ??= [];
        Gaming.CustomRules ??= [];
        Gaming.DisabledBuiltInRules ??= [];
        notes.AddRange(Hotkeys.Normalize());

        if (!Enum.IsDefined(Theme))
        {
            Theme = AppTheme.Dark;
        }

        if (!double.IsFinite(Gaming.RestoreDelaySeconds) || Gaming.RestoreDelaySeconds < 0 || Gaming.RestoreDelaySeconds > 30)
        {
            Gaming.RestoreDelaySeconds = 1.5;
            notes.Add("Game restore delay was out of range; reset to 1.5 s.");
        }

        foreach (var key in PresetIntensity.Keys.ToList())
        {
            var v = PresetIntensity[key];
            if (!BuiltInPresets.IsBuiltInId(key) || !double.IsFinite(v))
            {
                PresetIntensity.Remove(key);
            }
            else
            {
                PresetIntensity[key] = Math.Clamp(v, 0, 1);
            }
        }

        Gaming.CustomRules.RemoveAll(r => r is null || r.ExecutableNames is null || r.ExecutableNames.Count == 0);
        foreach (var rule in Gaming.CustomRules)
        {
            rule.BuiltIn = false;
            rule.TitleExcludes ??= [];
            if (string.IsNullOrWhiteSpace(rule.Name))
            {
                rule.Name = rule.ExecutableNames[0];
            }
        }

        SchemaVersion = CurrentSchemaVersion;
        return notes;
    }
}

/// <summary>Loads and saves <see cref="AppSettings"/> as JSON with crash-safe writes.</summary>
public sealed class SettingsStore
{
    public const string FileName = "settings.json";

    private readonly string _path;
    private readonly IAppLogger _log;

    public SettingsStore(string directory, IAppLogger? log = null)
    {
        _path = Path.Combine(directory, FileName);
        _log = log ?? NullLogger.Instance;
    }

    public string FilePath => _path;

    /// <summary>Loads settings. Missing → defaults. Corrupt → file set aside and defaults used (reported via <paramref name="problem"/>).</summary>
    public AppSettings Load(out string? problem)
    {
        problem = null;
        if (!File.Exists(_path))
        {
            return new AppSettings();
        }

        try
        {
            var json = File.ReadAllText(_path);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, ProfileSerializer.Options);
            if (settings is null)
            {
                throw new JsonException("Settings file is empty.");
            }

            if (settings.SchemaVersion > AppSettings.CurrentSchemaVersion)
            {
                _log.Warn($"Settings were written by a newer version (schema {settings.SchemaVersion}); unknown values are ignored.");
            }

            foreach (var note in settings.Normalize())
            {
                _log.Info("Settings: " + note);
            }

            return settings;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            var moved = AtomicFile.Quarantine(_path);
            problem = "Your settings file could not be read, so default settings are being used." +
                      (moved is null ? string.Empty : $" The old file was kept as {Path.GetFileName(moved)}.");
            _log.Error("Settings could not be loaded; using defaults.", ex);
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        try
        {
            AtomicFile.WriteAllText(_path, JsonSerializer.Serialize(settings, ProfileSerializer.Options));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Error("Settings could not be saved.", ex);
        }
    }
}
