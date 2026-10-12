using System.Globalization;
using Akeno.ColorVision.ColorScience;
using Akeno.ColorVision.Core.Profiles;

namespace Akeno.ColorVision.Core.Calibration;

/// <summary>Calibration step 1: how the user describes their experience (not a diagnosis).</summary>
public enum SelfDescription
{
    RedGreenLookSimilar,
    ManyLookGrayOrBrown,
    SomeColorsMuted,
    CannotDistinguishObjects,
    NotSure,
}

/// <summary>Calibration step 2 answers. Values are stored in profiles (0..3).</summary>
public enum PairRating
{
    ClearlyDifferent = 0,
    SlightlyDifferent = 1,
    AlmostIdentical = 2,
    Identical = 3,
}

public enum FitConfidence
{
    Low,
    Medium,
    High,
}

public sealed record SelfDescriptionOption(SelfDescription Value, string Text, string Detail);

public static class CalibrationQuestionnaire
{
    public static IReadOnlyList<SelfDescriptionOption> Options { get; } =
    [
        new(SelfDescription.RedGreenLookSimilar, "Red and green look similar", "Some reds and greens are hard to tell apart."),
        new(SelfDescription.ManyLookGrayOrBrown, "Many red and green shades look gray or brown", "Lots of reds and greens blend into grayish or brownish colors."),
        new(SelfDescription.SomeColorsMuted, "Some colors seem muted", "Colors look washed out or dull, but are mostly distinguishable."),
        new(SelfDescription.CannotDistinguishObjects, "I can't tell certain blocks or objects apart", "For example blocks in a game, map markers, or chart lines."),
        new(SelfDescription.NotSure, "I'm not sure", "That's completely fine — the next steps will figure out what helps."),
    ];

    /// <summary>A gentle starting point before any color pairs have been rated.</summary>
    public static (CorrectionMode Mode, double Intensity) InitialSuggestion(SelfDescription description) => description switch
    {
        SelfDescription.ManyLookGrayOrBrown => (CorrectionMode.RedGreenAssistance, 0.85),
        SelfDescription.SomeColorsMuted => (CorrectionMode.RedGreenAssistance, 0.5),
        SelfDescription.CannotDistinguishObjects => (CorrectionMode.StrongSeparation, 0.8),
        SelfDescription.RedGreenLookSimilar => (CorrectionMode.RedGreenAssistance, 0.7),
        _ => (CorrectionMode.RedGreenAssistance, 0.7),
    };
}

/// <summary>What the wizard concluded from the answers. Worded and used as a starting point only.</summary>
public sealed record CalibrationInsight(
    CvdType? CloserModel,
    double ModelSeverity,
    FitConfidence Confidence,
    CorrectionMode SuggestedMode,
    double SuggestedIntensity,
    IReadOnlyList<ColorPair> DifficultPairs,
    bool ControlPairsLookedSimilar,
    bool AnswersLookTypical,
    string Summary);

/// <summary>
/// Compares the user's pair ratings with what a simplified CVD simulation model predicts for
/// protan-like and deutan-like observers of several severities, and picks the closest model as a
/// <i>starting point</i> for the comparison step. It never claims to know the user's type of color
/// vision deficiency: screens, lighting and individual vision all differ.
/// </summary>
public static class CalibrationAnalyzer
{
    private static readonly double[] Severities = [0.2, 0.4, 0.6, 0.8, 1.0];

    public static CalibrationInsight Analyze(IReadOnlyDictionary<string, PairRating> ratings, SelfDescription? selfDescription = null)
    {
        ArgumentNullException.ThrowIfNull(ratings);
        var pairs = TestColorSets.CalibrationPairs;
        var rated = pairs.Where(p => !p.IsControl && ratings.ContainsKey(p.Id)).ToList();
        var controlsProblem = pairs.Where(p => p.IsControl && ratings.TryGetValue(p.Id, out _))
                                   .Any(p => ratings[p.Id] >= PairRating.AlmostIdentical);
        var difficult = rated.Where(p => ratings[p.Id] >= PairRating.AlmostIdentical).ToList();

        var (startMode, startIntensity) = selfDescription is { } sd
            ? CalibrationQuestionnaire.InitialSuggestion(sd)
            : (CorrectionMode.RedGreenAssistance, 0.7);

        if (rated.Count < 3)
        {
            return new CalibrationInsight(null, 0.8, FitConfidence.Low, startMode, startIntensity, difficult, controlsProblem, false,
                "Not enough answers to compare with the model, so we'll start from a balanced red-green correction.");
        }

        var typicalError = Error(rated, ratings, ObserverModel.Typical);
        (double Error, double Severity) bestProtan = (double.MaxValue, 0.8), bestDeutan = (double.MaxValue, 0.8);
        foreach (var severity in Severities)
        {
            var ep = Error(rated, ratings, new ObserverModel(CvdType.Protan, severity));
            var ed = Error(rated, ratings, new ObserverModel(CvdType.Deutan, severity));
            if (ep < bestProtan.Error)
            {
                bestProtan = (ep, severity);
            }

            if (ed < bestDeutan.Error)
            {
                bestDeutan = (ed, severity);
            }
        }

        var bestCvdError = Math.Min(bestProtan.Error, bestDeutan.Error);
        var looksTypical = typicalError + 0.5 < bestCvdError && difficult.Count == 0;
        var gap = Math.Abs(bestProtan.Error - bestDeutan.Error);
        var confidence = gap >= 4 ? FitConfidence.High : gap >= 2 ? FitConfidence.Medium : FitConfidence.Low;
        CvdType? closer = gap < 1 ? null : bestProtan.Error < bestDeutan.Error ? CvdType.Protan : CvdType.Deutan;
        var severityFit = closer switch
        {
            CvdType.Protan => bestProtan.Severity,
            CvdType.Deutan => bestDeutan.Severity,
            _ => (bestProtan.Severity + bestDeutan.Severity) / 2,
        };

        var mode = closer switch
        {
            CvdType.Protan when confidence != FitConfidence.Low => CorrectionMode.ProtanAssistance,
            CvdType.Deutan when confidence != FitConfidence.Low => CorrectionMode.DeutanAssistance,
            _ => CorrectionMode.RedGreenAssistance,
        };

        if (startMode == CorrectionMode.StrongSeparation)
        {
            mode = CorrectionMode.StrongSeparation;
        }

        var intensity = Math.Clamp(0.45 + (0.5 * severityFit), 0.5, 1.0);
        if (selfDescription is SelfDescription.ManyLookGrayOrBrown or SelfDescription.CannotDistinguishObjects)
        {
            intensity = Math.Min(1.0, intensity + 0.1);
        }
        else if (selfDescription is SelfDescription.SomeColorsMuted)
        {
            intensity = Math.Max(0.4, intensity - 0.1);
        }

        if (looksTypical)
        {
            mode = CorrectionMode.RedGreenAssistance;
            intensity = 0.4;
        }

        return new CalibrationInsight(closer, severityFit, confidence, mode, Math.Round(intensity, 2), difficult, controlsProblem, looksTypical,
            BuildSummary(difficult, closer, confidence, controlsProblem, looksTypical));
    }

    /// <summary>Predicted rating for a simulated CIEDE2000 difference.</summary>
    internal static int PredictRating(double deltaE) => deltaE switch
    {
        < 4 => 3,
        < 8 => 2,
        < 15 => 1,
        _ => 0,
    };

    private static double Error(IEnumerable<ColorPair> pairs, IReadOnlyDictionary<string, PairRating> ratings, ObserverModel observer)
    {
        double error = 0;
        foreach (var pair in pairs)
        {
            var dE = DiscriminationEvaluator.PerceivedDifference(pair.First.Color, pair.Second.Color, ColorMatrix.Identity, observer);
            error += Math.Abs(PredictRating(dE) - (int)ratings[pair.Id]);
        }

        return error;
    }

    private static string BuildSummary(IReadOnlyList<ColorPair> difficult, CvdType? closer, FitConfidence confidence, bool controls, bool typical)
    {
        var parts = new List<string>();
        if (difficult.Count > 0)
        {
            var names = string.Join(", ", difficult.Take(4).Select(p => p.Label.ToLowerInvariant()));
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"The pairs that looked most alike to you: {names}{(difficult.Count > 4 ? ", and more" : string.Empty)}."));
        }
        else
        {
            parts.Add("You could tell most pairs apart on this screen.");
        }

        if (typical)
        {
            parts.Add("Your answers are close to what typical color vision would report on this screen, so a gentle correction is suggested.");
        }
        else if (closer is { } type)
        {
            var label = type == CvdType.Protan ? "protan-like (red-sensitive cone)" : "deutan-like (green-sensitive cone)";
            parts.Add($"In our simplified model your answers match a {label} pattern best ({confidence.ToString().ToLowerInvariant()} confidence).");
        }
        else
        {
            parts.Add("Your answers fit the protan-like and deutan-like models about equally, so a balanced red-green correction is suggested.");
        }

        if (controls)
        {
            parts.Add("Some check pairs that should look very different were rated as similar — the screen settings (brightness, HDR, Night light) may be affecting the colors.");
        }

        parts.Add("This is not a medical test or diagnosis. The next step lets you pick whatever actually works best for you.");
        return string.Join(" ", parts);
    }
}

/// <summary>An option shown in the comparison steps.</summary>
public sealed record CalibrationCandidate(string Id, string Label, string Description, CorrectionMode Mode, CorrectionParameters Parameters, double Intensity)
{
    public ColorMatrix BuildMatrix() => CorrectionPipeline.Build(Parameters, Intensity);
}

/// <summary>Generates the options for calibration step 3 (strategy, then strength, then fine-tuning).</summary>
public static class CalibrationCandidates
{
    public static IReadOnlyList<CalibrationCandidate> StrategyRound(CalibrationInsight insight)
    {
        ArgumentNullException.ThrowIfNull(insight);
        var severity = Math.Clamp(insight.ModelSeverity, 0.4, 1.0);
        var intensity = insight.SuggestedIntensity;
        var list = new List<CalibrationCandidate>
        {
            new("balanced", "Balanced", "Red/green differences become blue/yellow and light/dark differences.",
                CorrectionMode.RedGreenAssistance, BuiltInPresets.RedGreenParameters, intensity),
            new("protan", "Red-focused", "Daltonization designed with a protan (red-cone) model.",
                CorrectionMode.ProtanAssistance, BuiltInPresets.ProtanParameters with { ModelSeverity = severity }, intensity),
            new("deutan", "Green-focused", "Daltonization designed with a deutan (green-cone) model.",
                CorrectionMode.DeutanAssistance, BuiltInPresets.DeutanParameters with { ModelSeverity = severity }, intensity),
            new("strong", "Strong", "Maximum separation. Looks unnatural, but differences are obvious.",
                CorrectionMode.StrongSeparation, BuiltInPresets.StrongParameters, 1.0),
        };

        // Put the suggested strategy first so it is the first one the user sees.
        var suggestedId = insight.SuggestedMode switch
        {
            CorrectionMode.ProtanAssistance => "protan",
            CorrectionMode.DeutanAssistance => "deutan",
            CorrectionMode.StrongSeparation => "strong",
            _ => "balanced",
        };
        return [.. list.OrderBy(c => c.Id == suggestedId ? 0 : 1)];
    }

    public static IReadOnlyList<CalibrationCandidate> StrengthRound(CalibrationCandidate chosen)
    {
        ArgumentNullException.ThrowIfNull(chosen);
        return
        [
            chosen with { Id = chosen.Id + ":gentle", Label = "Gentle", Intensity = 0.4 },
            chosen with { Id = chosen.Id + ":medium", Label = "Medium", Intensity = 0.7 },
            chosen with { Id = chosen.Id + ":strong", Label = "Strong", Intensity = 1.0 },
        ];
    }

    public static IReadOnlyList<CalibrationCandidate> RefineRound(CalibrationCandidate chosen)
    {
        ArgumentNullException.ThrowIfNull(chosen);
        var baseId = chosen.Id.Split(':')[0];
        var values = new[] { chosen.Intensity - 0.15, chosen.Intensity, chosen.Intensity + 0.15 }
            .Select(v => Math.Round(Math.Clamp(v, 0.2, 1.0), 2))
            .Distinct()
            .ToList();
        return values.Select(v => chosen with
        {
            Id = string.Create(CultureInfo.InvariantCulture, $"{baseId}:{v:0.00}"),
            Label = v < chosen.Intensity ? "A little less" : v > chosen.Intensity ? "A little more" : "As chosen",
            Intensity = v,
        }).ToList();
    }

    public static ColorProfile CreateProfile(string name, CalibrationCandidate chosen, CalibrationRecord record)
    {
        ArgumentNullException.ThrowIfNull(chosen);
        ArgumentNullException.ThrowIfNull(record);
        var profile = new ColorProfile
        {
            Name = string.IsNullOrWhiteSpace(name) ? "My ColorVision Profile" : name.Trim(),
            Mode = CorrectionMode.Custom,
            Intensity = chosen.Intensity,
            Parameters = chosen.Parameters,
            Origin = ProfileOrigin.Calibration,
            Description = $"Created by calibration from the \"{chosen.Label}\" option based on {DescribeMode(chosen.Mode)}.",
            Calibration = record,
        };
        ProfileSerializer.Normalize(profile);
        profile.Origin = ProfileOrigin.Calibration;
        return profile;
    }

    private static string DescribeMode(CorrectionMode mode) => mode switch
    {
        CorrectionMode.ProtanAssistance => "Protan Assistance",
        CorrectionMode.DeutanAssistance => "Deutan Assistance",
        CorrectionMode.StrongSeparation => "Strong Separation",
        _ => "Red-Green Assistance",
    };
}
