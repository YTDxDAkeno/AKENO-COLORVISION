namespace Akeno.ColorVision.ColorScience;

/// <summary>Outcome of <see cref="TransformValidator.Validate"/>.</summary>
public sealed record TransformValidation(bool IsSafe, IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings)
{
    public static TransformValidation Safe { get; } = new(true, [], []);
}

/// <summary>
/// Safety gate in front of every system-wide filter change. A full-screen color matrix can make
/// the desktop unreadable (for example by mapping everything to black), so matrices are rejected
/// unless they are finite, bounded, and keep a usable difference between black and white.
/// </summary>
public static class TransformValidator
{
    /// <summary>Largest absolute coefficient accepted.</summary>
    public const double MaxCoefficient = 4.0;

    /// <summary>Largest absolute offset accepted.</summary>
    public const double MaxOffset = 0.6;

    /// <summary>Minimum luminance difference between transformed white and transformed black.</summary>
    public const double MinBlackWhiteLuminanceDelta = 0.30;

    /// <summary>Minimum |determinant|; below this the transform collapses colors into a plane.</summary>
    public const double MinDeterminant = 0.02;

    public static TransformValidation Validate(ColorMatrix matrix)
    {
        ArgumentNullException.ThrowIfNull(matrix);
        var errors = new List<string>();
        var warnings = new List<string>();

        if (!matrix.IsFinite)
        {
            errors.Add("The transform contains invalid (non-finite) numbers.");
            return new TransformValidation(false, errors, warnings);
        }

        for (var row = 0; row < 3; row++)
        {
            for (var col = 0; col < 3; col++)
            {
                if (Math.Abs(matrix[row, col]) > MaxCoefficient)
                {
                    errors.Add($"Coefficient [{row},{col}] = {matrix[row, col]:0.###} exceeds ±{MaxCoefficient}.");
                }
            }

            if (Math.Abs(matrix.OffsetOf(row)) > MaxOffset)
            {
                errors.Add($"Offset {row} = {matrix.OffsetOf(row):0.###} exceeds ±{MaxOffset}.");
            }
        }

        var white = matrix.TransformClamped(Rgb.White);
        var black = matrix.TransformClamped(Rgb.Black);
        var delta = Srgb.RelativeLuminance(white) - Srgb.RelativeLuminance(black);
        if (delta < MinBlackWhiteLuminanceDelta)
        {
            errors.Add($"White and black would become too similar (luminance difference {delta:0.00}); the screen could become unreadable.");
        }

        if (Math.Abs(matrix.Determinant) < MinDeterminant)
        {
            errors.Add("The transform would merge many different colors into the same color.");
        }

        var midGray = matrix.TransformClamped(Rgb.Gray(0.5));
        if (midGray.MaxDifference(Rgb.Gray((midGray.R + midGray.G + midGray.B) / 3)) > 0.08)
        {
            warnings.Add("Grays will be tinted noticeably.");
        }

        if (Srgb.RelativeLuminance(black) > 0.1)
        {
            warnings.Add("Black will look noticeably lighter (washed out).");
        }

        if (Srgb.RelativeLuminance(white) < 0.6)
        {
            warnings.Add("White will look noticeably darker.");
        }

        return new TransformValidation(errors.Count == 0, errors, warnings);
    }
}
