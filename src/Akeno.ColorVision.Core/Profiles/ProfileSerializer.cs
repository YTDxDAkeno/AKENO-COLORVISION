using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Akeno.ColorVision.ColorScience;

namespace Akeno.ColorVision.Core.Profiles;

/// <summary>Result of reading a profile file.</summary>
public sealed record ProfileReadResult(ColorProfile? Profile, IReadOnlyList<string> Warnings, string? Error, bool WasMigrated)
{
    public bool Success => Profile is not null;

    public static ProfileReadResult Fail(string error) => new(null, [], error, false);
}

/// <summary>
/// Versioned JSON format for profiles.
/// <list type="bullet">
/// <item>Schema 2 (current): <c>{"schemaVersion": 2, "id", "name", "mode", "intensity", "parameters": {...}, ...}</c></item>
/// <item>Schema 1 (legacy, flat): <c>{"name", "version": 1, "mode": "custom", "intensity", "contrast", "saturation", "backend"}</c>.
/// Migrated on read.</item>
/// </list>
/// Every value is validated and clamped on read so a hand-edited or corrupted file can never
/// produce an unsafe filter.
/// </summary>
public static class ProfileSerializer
{
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    public static string Serialize(ColorProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var copy = profile.Clone();
        copy.SchemaVersion = ColorProfile.CurrentSchemaVersion;
        return JsonSerializer.Serialize(copy, Options);
    }

    public static ProfileReadResult Deserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return ProfileReadResult.Fail("The file is empty.");
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        }
        catch (JsonException ex)
        {
            return ProfileReadResult.Fail($"The file is not valid JSON ({ex.Message}).");
        }

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return ProfileReadResult.Fail("The file does not contain a profile object.");
            }

            var root = doc.RootElement;
            if (TryGetPropertyIgnoreCase(root, "schemaVersion", out var schemaElement))
            {
                if (schemaElement.ValueKind != JsonValueKind.Number || !schemaElement.TryGetInt32(out var schema))
                {
                    return ProfileReadResult.Fail("'schemaVersion' must be a number.");
                }

                if (schema > ColorProfile.CurrentSchemaVersion)
                {
                    return ProfileReadResult.Fail($"This profile was created by a newer version of Akeno ColorVision (schema {schema}). Please update the app.");
                }

                if (schema == ColorProfile.CurrentSchemaVersion)
                {
                    return ReadCurrent(json);
                }

                if (schema == 1)
                {
                    return MigrateV1(root);
                }

                return ProfileReadResult.Fail($"Unsupported profile schema version {schema}.");
            }

            if (TryGetPropertyIgnoreCase(root, "version", out _))
            {
                return MigrateV1(root);
            }

            return ProfileReadResult.Fail("The file is missing 'schemaVersion'; it does not look like an Akeno ColorVision profile.");
        }
    }

    /// <summary>
    /// Clamps every value into its valid range and fixes structural problems.
    /// Returns human-readable notes about what was changed.
    /// </summary>
    public static IReadOnlyList<string> Normalize(ColorProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var warnings = new List<string>();

        if (profile.Id == Guid.Empty)
        {
            profile.Id = Guid.NewGuid();
            warnings.Add("Profile had no id; a new one was assigned.");
        }

        var name = (profile.Name ?? string.Empty).Trim();
        name = new string(name.Where(c => !char.IsControl(c)).ToArray());
        if (name.Length == 0)
        {
            name = "Unnamed profile";
            warnings.Add("Profile had no name.");
        }

        if (name.Length > ColorProfile.MaxNameLength)
        {
            name = name[..ColorProfile.MaxNameLength];
            warnings.Add("Profile name was shortened.");
        }

        profile.Name = name;
        profile.Description ??= string.Empty;

        if (!Enum.IsDefined(profile.Mode))
        {
            profile.Mode = CorrectionMode.Custom;
            warnings.Add("Unknown mode; using Custom.");
        }

        if (!double.IsFinite(profile.Intensity) || profile.Intensity < 0 || profile.Intensity > 1)
        {
            var fixedValue = double.IsFinite(profile.Intensity) ? Math.Clamp(profile.Intensity, 0, 1) : 0.7;
            warnings.Add(string.Create(CultureInfo.InvariantCulture, $"Intensity {profile.Intensity} was out of range; using {fixedValue:0.##}."));
            profile.Intensity = fixedValue;
        }

        var original = profile.Parameters ?? new CorrectionParameters();
        var clamped = original.Clamped();
        if (clamped != original)
        {
            warnings.Add("Some correction values were out of range and have been limited to safe values.");
        }

        profile.Parameters = clamped;

        if (string.IsNullOrWhiteSpace(profile.Backend))
        {
            profile.Backend = "auto";
        }

        if (!Enum.IsDefined(profile.Origin) || profile.Origin == ProfileOrigin.BuiltIn)
        {
            // Files on disk are never built-in presets; those live in code.
            profile.Origin = ProfileOrigin.User;
        }

        if (profile.Calibration is { } cal)
        {
            cal.PairRatings ??= [];
            cal.ConditionNotes ??= [];
            foreach (var key in cal.PairRatings.Keys.ToList())
            {
                cal.PairRatings[key] = Math.Clamp(cal.PairRatings[key], 0, 3);
            }
        }

        profile.SchemaVersion = ColorProfile.CurrentSchemaVersion;
        return warnings;
    }

    private static ProfileReadResult ReadCurrent(string json)
    {
        ColorProfile? profile;
        try
        {
            profile = JsonSerializer.Deserialize<ColorProfile>(json, Options);
        }
        catch (JsonException ex)
        {
            return ProfileReadResult.Fail($"The profile could not be read ({ex.Message}).");
        }
        catch (NotSupportedException ex)
        {
            return ProfileReadResult.Fail($"The profile could not be read ({ex.Message}).");
        }

        if (profile is null)
        {
            return ProfileReadResult.Fail("The profile is empty.");
        }

        var warnings = Normalize(profile);
        return new ProfileReadResult(profile, warnings, null, false);
    }

    private static ProfileReadResult MigrateV1(JsonElement root)
    {
        var warnings = new List<string> { "Profile was converted from the older format (schema 1)." };
        var modeText = GetString(root, "mode") ?? "custom";
        var mode = ParseLegacyMode(modeText, out var known);
        if (!known)
        {
            warnings.Add($"Unknown legacy mode '{modeText}'; using Red-Green Assistance settings.");
        }

        var parameters = BuiltInPresets.ParametersFor(mode == CorrectionMode.Custom ? CorrectionMode.RedGreenAssistance : mode);
        if (TryGetDouble(root, "contrast", out var contrast))
        {
            parameters = parameters with { Contrast = contrast };
        }

        if (TryGetDouble(root, "saturation", out var saturation))
        {
            parameters = parameters with { Saturation = saturation };
        }

        if (TryGetDouble(root, "brightness", out var brightness))
        {
            parameters = parameters with { Brightness = brightness };
        }

        var profile = new ColorProfile
        {
            Name = GetString(root, "name") ?? "Imported profile",
            Mode = mode,
            Intensity = TryGetDouble(root, "intensity", out var intensity) ? intensity : 0.7,
            Parameters = parameters,
            Backend = GetString(root, "backend") ?? "auto",
            Origin = ProfileOrigin.User,
        };

        warnings.AddRange(Normalize(profile));
        return new ProfileReadResult(profile, warnings, null, true);
    }

    private static CorrectionMode ParseLegacyMode(string text, out bool known)
    {
        known = true;
        var key = new string(text.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        switch (key)
        {
            case "custom":
                return CorrectionMode.Custom;
            case "redgreen":
            case "redgreenassistance":
            case "redgreendistinction":
                return CorrectionMode.RedGreenAssistance;
            case "protan":
            case "protanassistance":
                return CorrectionMode.ProtanAssistance;
            case "deutan":
            case "deutanassistance":
                return CorrectionMode.DeutanAssistance;
            case "strong":
            case "strongseparation":
                return CorrectionMode.StrongSeparation;
            case "brightnesscontrast":
            case "contrast":
                return CorrectionMode.BrightnessContrast;
            case "minecraft":
            case "minecraftnether":
            case "nether":
                return CorrectionMode.MinecraftNether;
            default:
                known = false;
                return CorrectionMode.Custom;
        }
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement obj, string name, out JsonElement value)
    {
        foreach (var p in obj.EnumerateObject())
        {
            if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = p.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static string? GetString(JsonElement obj, string name) =>
        TryGetPropertyIgnoreCase(obj, name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool TryGetDouble(JsonElement obj, string name, out double value)
    {
        value = 0;
        return TryGetPropertyIgnoreCase(obj, name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out value);
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
