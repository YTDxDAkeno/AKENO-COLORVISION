using System.Text.Json.Serialization;
using Akeno.ColorVision.ColorScience;

namespace Akeno.ColorVision.Core.Profiles;

/// <summary>Which preset family a profile belongs to (used for labels and defaults).</summary>
public enum CorrectionMode
{
    RedGreenAssistance,
    ProtanAssistance,
    DeutanAssistance,
    StrongSeparation,
    BrightnessContrast,
    MinecraftNether,
    Custom,
}

/// <summary>Where a profile came from.</summary>
public enum ProfileOrigin
{
    BuiltIn,
    User,
    Calibration,
    Imported,
}

/// <summary>
/// What the user told the calibration wizard. This intentionally records the user's own answers
/// and choices only — never a diagnosis.
/// </summary>
public sealed class CalibrationRecord
{
    public DateTimeOffset CompletedUtc { get; set; }

    /// <summary>The self-description option the user picked in step 1 (their words, not a diagnosis).</summary>
    public string? SelfDescription { get; set; }

    /// <summary>Pair id → rating (0 = clearly different … 3 = exactly the same).</summary>
    public Dictionary<string, int> PairRatings { get; set; } = [];

    /// <summary>The starting point the wizard suggested from the answers.</summary>
    public CorrectionMode? SuggestedStartingPoint { get; set; }

    /// <summary>The comparison option the user picked as easiest to distinguish.</summary>
    public string? ChosenOption { get; set; }

    /// <summary>Notes about the conditions (e.g. "HDR was on"), to help interpret results later.</summary>
    public List<string> ConditionNotes { get; set; } = [];
}

/// <summary>A saved color correction profile. Serialized as versioned JSON (see <see cref="ProfileSerializer"/>).</summary>
public sealed class ColorProfile
{
    public const int CurrentSchemaVersion = 2;
    public const int MaxNameLength = 60;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = "My ColorVision Profile";

    public string Description { get; set; } = string.Empty;

    public CorrectionMode Mode { get; set; } = CorrectionMode.Custom;

    /// <summary>Overall correction strength 0..1 (blend between original colors and the full correction).</summary>
    public double Intensity { get; set; } = 0.7;

    public CorrectionParameters Parameters { get; set; } = new();

    /// <summary>Requested rendering backend id, or "auto".</summary>
    public string Backend { get; set; } = "auto";

    public ProfileOrigin Origin { get; set; } = ProfileOrigin.User;

    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset ModifiedUtc { get; set; } = DateTimeOffset.UtcNow;

    public CalibrationRecord? Calibration { get; set; }

    [JsonIgnore]
    public bool IsBuiltIn => Origin == ProfileOrigin.BuiltIn;

    /// <summary>Builds the color matrix this profile applies.</summary>
    public ColorMatrix BuildMatrix() => CorrectionPipeline.Build(Parameters, Intensity);

    /// <summary>Same profile with a different intensity (does not modify this instance).</summary>
    public ColorMatrix BuildMatrix(double intensity) => CorrectionPipeline.Build(Parameters, intensity);

    public ColorProfile Clone() => new()
    {
        SchemaVersion = SchemaVersion,
        Id = Id,
        Name = Name,
        Description = Description,
        Mode = Mode,
        Intensity = Intensity,
        Parameters = Parameters,
        Backend = Backend,
        Origin = Origin,
        CreatedUtc = CreatedUtc,
        ModifiedUtc = ModifiedUtc,
        Calibration = Calibration is null ? null : new CalibrationRecord
        {
            CompletedUtc = Calibration.CompletedUtc,
            SelfDescription = Calibration.SelfDescription,
            PairRatings = new Dictionary<string, int>(Calibration.PairRatings),
            SuggestedStartingPoint = Calibration.SuggestedStartingPoint,
            ChosenOption = Calibration.ChosenOption,
            ConditionNotes = [.. Calibration.ConditionNotes],
        },
    };

    /// <summary>Creates an editable user copy with a new id.</summary>
    public ColorProfile CreateUserCopy(string name)
    {
        var copy = Clone();
        copy.Id = Guid.NewGuid();
        copy.Name = name;
        copy.Origin = ProfileOrigin.User;
        copy.CreatedUtc = DateTimeOffset.UtcNow;
        copy.ModifiedUtc = copy.CreatedUtc;
        return copy;
    }

    public override string ToString() => Name;
}
