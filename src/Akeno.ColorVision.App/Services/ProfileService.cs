using System.Collections.ObjectModel;
using System.IO;
using Akeno.ColorVision.ColorScience;
using Akeno.ColorVision.Core.Diagnostics;
using Akeno.ColorVision.Core.Profiles;
using Akeno.ColorVision.Core.Settings;

namespace Akeno.ColorVision.App.Services;

public sealed class ActiveProfileChangedEventArgs(ColorProfile profile, bool automatic) : EventArgs
{
    public ColorProfile Profile { get; } = profile;

    /// <summary>True when changed by game detection rather than by the user.</summary>
    public bool Automatic { get; } = automatic;
}

/// <summary>
/// Owns the list of profiles (built-in presets + user profiles on disk) and which one is active.
/// Must be used from the UI thread.
/// </summary>
public sealed class ProfileService
{
    private readonly ProfileStore _store;
    private readonly AppSettings _settings;
    private readonly IAppLogger _log;
    private readonly Action _saveSettings;
    private ColorProfile _active;

    public ProfileService(ProfileStore store, AppSettings settings, Action saveSettings, IAppLogger log)
    {
        _store = store;
        _settings = settings;
        _saveSettings = saveSettings;
        _log = log;

        foreach (var preset in BuiltInPresets.All)
        {
            if (settings.PresetIntensity.TryGetValue(preset.Id, out var intensity))
            {
                preset.Intensity = intensity;
            }

            Profiles.Add(preset);
        }

        var userProfiles = store.LoadAll(out var issues);
        foreach (var profile in userProfiles.OrderBy(p => p.CreatedUtc))
        {
            Profiles.Add(profile);
        }

        LoadIssues = issues;
        _active = Find(settings.ActiveProfileId) ?? Profiles[0];
        if (!settings.RememberLastProfile)
        {
            _active = Profiles[0];
        }
    }

    public event EventHandler<ActiveProfileChangedEventArgs>? ActiveProfileChanged;

    /// <summary>Raised when the active profile's parameters or intensity changed (the matrix must be rebuilt).</summary>
    public event EventHandler? ActiveProfileEdited;

    public event EventHandler? ProfilesChanged;

    public ObservableCollection<ColorProfile> Profiles { get; } = [];

    public IReadOnlyList<ProfileLoadIssue> LoadIssues { get; }

    public ColorProfile Active => _active;

    public ColorMatrix ActiveMatrix => _active.BuildMatrix();

    public ColorProfile? Find(Guid id) => Profiles.FirstOrDefault(p => p.Id == id);

    public void SetActive(Guid id, bool automatic = false)
    {
        var profile = Find(id);
        if (profile is null || ReferenceEquals(profile, _active))
        {
            return;
        }

        _active = profile;
        if (!automatic)
        {
            _settings.ActiveProfileId = id;
            _saveSettings();
        }

        _log.Info($"Active profile: {profile.Name}{(automatic ? " (automatic)" : string.Empty)}");
        ActiveProfileChanged?.Invoke(this, new ActiveProfileChangedEventArgs(profile, automatic));
    }

    public void SetIntensity(ColorProfile profile, double intensity, bool persist = true)
    {
        ArgumentNullException.ThrowIfNull(profile);
        intensity = Math.Clamp(intensity, 0, 1);
        if (Math.Abs(profile.Intensity - intensity) < 1e-6)
        {
            return;
        }

        profile.Intensity = intensity;
        if (profile.IsBuiltIn)
        {
            _settings.PresetIntensity[profile.Id] = intensity;
        }

        if (persist)
        {
            Persist(profile);
        }

        if (ReferenceEquals(profile, _active))
        {
            ActiveProfileEdited?.Invoke(this, EventArgs.Empty);
        }
    }

    public void UpdateParameters(ColorProfile profile, CorrectionParameters parameters, bool persist = true)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(parameters);
        if (profile.IsBuiltIn)
        {
            throw new InvalidOperationException("Built-in presets cannot be edited; customize a copy instead.");
        }

        profile.Parameters = parameters.Clamped();
        profile.Mode = CorrectionMode.Custom;
        if (persist)
        {
            SaveQuietly(profile);
        }

        if (ReferenceEquals(profile, _active))
        {
            ActiveProfileEdited?.Invoke(this, EventArgs.Empty);
        }
    }

    public ColorProfile CreateCopy(ColorProfile source, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        var copy = source.CreateUserCopy(UniqueName(name ?? $"{source.Name} (custom)"));
        copy.Mode = CorrectionMode.Custom;
        Add(copy);
        return copy;
    }

    public void Add(ColorProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        profile.Name = UniqueName(profile.Name, profile.Id);
        _store.Save(profile);
        Profiles.Add(profile);
        ProfilesChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Rename(ColorProfile profile, string newName)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (profile.IsBuiltIn || string.IsNullOrWhiteSpace(newName))
        {
            return;
        }

        profile.Name = UniqueName(newName.Trim(), profile.Id);
        ProfileSerializer.Normalize(profile);
        SaveQuietly(profile);
        ProfilesChanged?.Invoke(this, EventArgs.Empty);
    }

    public bool Delete(ColorProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (profile.IsBuiltIn)
        {
            return false;
        }

        try
        {
            _store.Delete(profile.Id);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Error("Could not delete profile file.", ex);
            return false;
        }

        Profiles.Remove(profile);
        if (_settings.Gaming.MinecraftProfileId == profile.Id)
        {
            _settings.Gaming.MinecraftProfileId = BuiltInPresets.MinecraftNetherId;
            _saveSettings();
        }

        if (ReferenceEquals(profile, _active))
        {
            SetActive(BuiltInPresets.RedGreenId);
        }

        ProfilesChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public ProfileReadResult Import(string path)
    {
        var result = ProfileStore.Import(path);
        if (result.Profile is { } profile)
        {
            Add(profile);
        }

        return result;
    }

    public static void Export(ColorProfile profile, string path) => ProfileStore.Export(profile, path);

    public void ResetPresetStrengths()
    {
        _settings.PresetIntensity.Clear();
        _saveSettings();
        foreach (var preset in BuiltInPresets.All)
        {
            if (Find(preset.Id) is { } loaded)
            {
                loaded.Intensity = preset.Intensity;
            }
        }

        ActiveProfileEdited?.Invoke(this, EventArgs.Empty);
        ProfilesChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Writes a profile's current values (settings for built-ins, a file for user profiles).</summary>
    public void Persist(ColorProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (profile.IsBuiltIn)
        {
            _saveSettings();
        }
        else
        {
            SaveQuietly(profile);
        }
    }

    private void SaveQuietly(ColorProfile profile)
    {
        try
        {
            _store.Save(profile);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Error($"Could not save profile '{profile.Name}'.", ex);
        }
    }

    private string UniqueName(string name, Guid? self = null)
    {
        var trimmed = string.IsNullOrWhiteSpace(name) ? "My profile" : name.Trim();
        if (trimmed.Length > ColorProfile.MaxNameLength)
        {
            trimmed = trimmed[..ColorProfile.MaxNameLength];
        }

        var candidate = trimmed;
        var n = 2;
        while (Profiles.Any(p => p.Id != self && string.Equals(p.Name, candidate, StringComparison.OrdinalIgnoreCase)))
        {
            candidate = $"{trimmed} {n++}";
        }

        return candidate;
    }
}
