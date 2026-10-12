namespace Akeno.ColorVision.ColorScience;

/// <summary>Per-pair result of <see cref="DiscriminationEvaluator"/>.</summary>
public sealed record PairEvaluation(ColorPair Pair, double BaselineDeltaE, double CorrectedDeltaE)
{
    public double Gain => CorrectedDeltaE - BaselineDeltaE;
}

/// <summary>Summary of how a filter changes simulated discriminability.</summary>
public sealed record DiscriminationReport(
    IReadOnlyList<PairEvaluation> Pairs,
    double MeanBaseline,
    double MeanCorrected,
    double MinBaseline,
    double MinCorrected,
    int HardPairsBaseline,
    int HardPairsCorrected,
    double MeanDistortionForTypicalVision)
{
    /// <summary>Pairs with ΔE00 below this value are counted as "hard to tell apart".</summary>
    public const double HardThreshold = 8.0;
}

/// <summary>
/// Model-based evaluation of a correction matrix. For each color pair it simulates what an
/// observer with a given CVD would perceive <i>after</i> the filter (filter applied to encoded
/// values with clamping, exactly like the desktop pipeline; observer simulated with
/// <see cref="MachadoCvdModel"/>), then measures the CIEDE2000 difference.
/// <para>
/// This is a design aid, not proof that a real person can tell colors apart: real vision varies,
/// and the user's own feedback during calibration takes priority.
/// </para>
/// </summary>
public static class DiscriminationEvaluator
{
    public static double PerceivedDifference(Rgb a, Rgb b, ColorMatrix filter, ObserverModel observer)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var fa = filter.TransformClamped(a);
        var fb = filter.TransformClamped(b);
        return ColorDifference.DeltaE2000(CvdSimulator.Simulate(fa, observer), CvdSimulator.Simulate(fb, observer));
    }

    /// <summary>Evaluates <paramref name="filter"/> on <paramref name="pairs"/>, averaging over <paramref name="observers"/>.</summary>
    public static DiscriminationReport Evaluate(
        ColorMatrix filter,
        IReadOnlyList<ColorPair> pairs,
        IReadOnlyList<ObserverModel> observers)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(pairs);
        ArgumentNullException.ThrowIfNull(observers);
        if (pairs.Count == 0 || observers.Count == 0)
        {
            throw new ArgumentException("At least one pair and one observer are required.");
        }

        var results = new List<PairEvaluation>(pairs.Count);
        foreach (var pair in pairs)
        {
            double baseline = 0, corrected = 0;
            foreach (var observer in observers)
            {
                baseline += PerceivedDifference(pair.First.Color, pair.Second.Color, ColorMatrix.Identity, observer);
                corrected += PerceivedDifference(pair.First.Color, pair.Second.Color, filter, observer);
            }

            results.Add(new PairEvaluation(pair, baseline / observers.Count, corrected / observers.Count));
        }

        return new DiscriminationReport(
            results,
            results.Average(r => r.BaselineDeltaE),
            results.Average(r => r.CorrectedDeltaE),
            results.Min(r => r.BaselineDeltaE),
            results.Min(r => r.CorrectedDeltaE),
            results.Count(r => r.BaselineDeltaE < DiscriminationReport.HardThreshold),
            results.Count(r => r.CorrectedDeltaE < DiscriminationReport.HardThreshold),
            MeanDistortion(filter));
    }

    /// <summary>
    /// Average CIEDE2000 between original and filtered colors for typical vision over a coarse
    /// RGB grid: a rough measure of how "unnatural" a filter looks.
    /// </summary>
    public static double MeanDistortion(ColorMatrix filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        const int steps = 6;
        double sum = 0;
        var n = 0;
        for (var r = 0; r < steps; r++)
        {
            for (var g = 0; g < steps; g++)
            {
                for (var b = 0; b < steps; b++)
                {
                    var c = new Rgb(r / (steps - 1.0), g / (steps - 1.0), b / (steps - 1.0));
                    sum += ColorDifference.DeltaE2000(c, filter.TransformClamped(c));
                    n++;
                }
            }
        }

        return sum / n;
    }

    /// <summary>A representative set of red-green observers used when the user's exact vision is unknown.</summary>
    public static IReadOnlyList<ObserverModel> RedGreenObservers { get; } =
    [
        new(CvdType.Protan, 0.6), new(CvdType.Protan, 0.8), new(CvdType.Protan, 1.0),
        new(CvdType.Deutan, 0.6), new(CvdType.Deutan, 0.8), new(CvdType.Deutan, 1.0),
    ];
}
