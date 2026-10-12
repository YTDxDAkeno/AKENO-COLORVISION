using Akeno.ColorVision.Core.Diagnostics;

namespace Akeno.ColorVision.Core.Profiles;

/// <summary>A problem found while loading profiles from disk.</summary>
public sealed record ProfileLoadIssue(string FileName, string Message);

/// <summary>
/// Stores user profiles as one JSON file per profile in a directory. Built-in presets live in code
/// (<see cref="BuiltInPresets"/>) and are never written to disk.
/// </summary>
public sealed class ProfileStore
{
    public const string FileExtension = ".json";

    private readonly string _directory;
    private readonly IAppLogger _log;

    public ProfileStore(string directory, IAppLogger? log = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);
        _directory = directory;
        _log = log ?? NullLogger.Instance;
    }

    public string Directory => _directory;

    /// <summary>Loads every readable profile. Unreadable files are moved aside and reported, never deleted.</summary>
    public IReadOnlyList<ColorProfile> LoadAll(out IReadOnlyList<ProfileLoadIssue> issues)
    {
        var list = new List<ColorProfile>();
        var problems = new List<ProfileLoadIssue>();
        issues = problems;

        if (!System.IO.Directory.Exists(_directory))
        {
            return list;
        }

        var seen = new HashSet<Guid>();
        foreach (var file in System.IO.Directory.EnumerateFiles(_directory, "*" + FileExtension).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            var name = Path.GetFileName(file);
            string json;
            try
            {
                json = File.ReadAllText(file);
            }
            catch (IOException ex)
            {
                problems.Add(new ProfileLoadIssue(name, $"Could not read the file: {ex.Message}"));
                continue;
            }
            catch (UnauthorizedAccessException ex)
            {
                problems.Add(new ProfileLoadIssue(name, $"Access denied: {ex.Message}"));
                continue;
            }

            var result = ProfileSerializer.Deserialize(json);
            if (!result.Success)
            {
                var moved = AtomicFile.Quarantine(file);
                problems.Add(new ProfileLoadIssue(name, $"{result.Error} The file was set aside{(moved is null ? string.Empty : " as " + Path.GetFileName(moved))}."));
                _log.Warn($"Profile '{name}' could not be loaded: {result.Error}");
                continue;
            }

            var profile = result.Profile!;
            if (BuiltInPresets.IsBuiltInId(profile.Id) || !seen.Add(profile.Id))
            {
                profile.Id = Guid.NewGuid();
                seen.Add(profile.Id);
                problems.Add(new ProfileLoadIssue(name, "Duplicate profile id; a new id was assigned."));
            }

            if (result.WasMigrated)
            {
                // Keep the original next to the converted file.
                try
                {
                    File.Copy(file, file + ".v1-backup", overwrite: true);
                }
                catch (IOException)
                {
                    // Best effort.
                }
            }

            var canonical = PathFor(profile.Id);
            if (result.WasMigrated || !string.Equals(Path.GetFullPath(file), Path.GetFullPath(canonical), StringComparison.OrdinalIgnoreCase))
            {
                // Rewrite in the current format under the canonical "<id>.json" name.
                try
                {
                    Save(profile);
                    if (!string.Equals(Path.GetFullPath(file), Path.GetFullPath(canonical), StringComparison.OrdinalIgnoreCase))
                    {
                        TryDelete(file);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    _log.Warn($"Could not rewrite profile '{name}': {ex.Message}");
                }
            }

            foreach (var warning in result.Warnings)
            {
                _log.Info($"Profile '{profile.Name}': {warning}");
            }

            list.Add(profile);
        }

        return list;
    }

    public void Save(ColorProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (profile.IsBuiltIn)
        {
            throw new InvalidOperationException("Built-in presets cannot be saved; create a copy instead.");
        }

        profile.ModifiedUtc = DateTimeOffset.UtcNow;
        AtomicFile.WriteAllText(PathFor(profile.Id), ProfileSerializer.Serialize(profile));
    }

    public bool Delete(Guid id)
    {
        var path = PathFor(id);
        if (!File.Exists(path))
        {
            return false;
        }

        File.Delete(path);
        return true;
    }

    /// <summary>Writes a profile to an arbitrary path for sharing/backup.</summary>
    public static void Export(ColorProfile profile, string path)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var copy = profile.Clone();
        if (copy.IsBuiltIn)
        {
            copy.Origin = ProfileOrigin.User;
        }

        AtomicFile.WriteAllText(path, ProfileSerializer.Serialize(copy));
    }

    /// <summary>Reads a profile file chosen by the user. The imported profile always gets a fresh id.</summary>
    public static ProfileReadResult Import(string path)
    {
        string json;
        try
        {
            var info = new FileInfo(path);
            if (info.Length > 256 * 1024)
            {
                return ProfileReadResult.Fail("The file is too large to be a profile.");
            }

            json = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            return ProfileReadResult.Fail($"Could not read the file: {ex.Message}");
        }

        var result = ProfileSerializer.Deserialize(json);
        if (result.Profile is { } p)
        {
            p.Id = Guid.NewGuid();
            p.Origin = ProfileOrigin.Imported;
            p.CreatedUtc = DateTimeOffset.UtcNow;
        }

        return result;
    }

    public string PathFor(Guid id) => Path.Combine(_directory, id.ToString("D") + FileExtension);

    private static void TryDelete(string file)
    {
        try
        {
            File.Delete(file);
        }
        catch (IOException)
        {
            // Best effort.
        }
        catch (UnauthorizedAccessException)
        {
            // Best effort.
        }
    }
}
