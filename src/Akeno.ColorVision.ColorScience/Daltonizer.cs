namespace Akeno.ColorVision.ColorScience;

/// <summary>
/// Daltonization: redistributes the color information that a simulated observer cannot see into
/// channels they can see. Based on Fidaner, Lin and Ozguven (2005), "Analysis of Color Blindness"
/// (Stanford EE368 project), which is the basis of the widely used daltonize.org algorithm:
/// <code>
///   simulated = S · c          (S = CVD simulation matrix, linear RGB)
///   error     = c − simulated  (information lost for that observer)
///   corrected = c + E · error  (E shifts the lost information into visible channels)
///   ⇒ D = I + E · (I − S)
/// </code>
/// Because S maps white to white, (I − S) maps every gray to 0 and D preserves grays.
/// <para>
/// The system-wide filter applies its matrix to <b>gamma-encoded</b> desktop values, but D is
/// defined for linear light. <see cref="CreateEncodedApproximation"/> therefore fits the best
/// gray-preserving encoded-domain matrix to the true linear-light correction (see
/// <see cref="MatrixFitting"/>); this is what the presets use.
/// </para>
/// </summary>
public static class Daltonizer
{
    /// <summary>Fidaner et al. error-modification matrix for red-green deficiencies.</summary>
    public static ColorMatrix FidanerErrorMatrix { get; } = ColorMatrix.FromRowMajor(
    [
        0.0, 0.0, 0.0,
        0.7, 1.0, 0.0,
        0.7, 0.0, 1.0,
    ]);

    /// <summary>The linear-light correction matrix D = I + E·(I − S).</summary>
    public static ColorMatrix CreateLinearCorrection(CvdType type, double severity)
    {
        if (type == CvdType.Tritan)
        {
            throw new NotSupportedException("Daltonization presets are only defined for red-green (protan/deutan) deficiencies.");
        }

        var s = MachadoCvdModel.GetSimulationMatrix(type, severity);
        var e = FidanerErrorMatrix;
        var m = new double[9];
        for (var row = 0; row < 3; row++)
        {
            for (var col = 0; col < 3; col++)
            {
                // (I - S)[k, col]
                double sum = 0;
                for (var k = 0; k < 3; k++)
                {
                    var iMinusS = (k == col ? 1.0 : 0.0) - s[k, col];
                    sum += e[row, k] * iMinusS;
                }

                m[(row * 3) + col] = (row == col ? 1.0 : 0.0) + sum;
            }
        }

        return ColorMatrix.FromRowMajor(m);
    }

    /// <summary>Applies the exact linear-light daltonization to a gamma-encoded color (reference implementation).</summary>
    public static Rgb CorrectExact(Rgb encoded, CvdType type, double severity)
    {
        var d = CreateLinearCorrection(type, severity);
        var linear = Srgb.ToLinear(encoded.Clamp01());
        return Srgb.FromLinear(d.Transform(linear).Clamp01());
    }

    /// <summary>
    /// Best gray-preserving matrix for the gamma-encoded domain approximating <see cref="CorrectExact"/>.
    /// Results are cached because fitting samples the RGB cube.
    /// </summary>
    public static ColorMatrix CreateEncodedApproximation(CvdType type, double severity)
    {
        var key = (type, Math.Round(Math.Clamp(severity, 0.0, 1.0), 3));
        lock (Cache)
        {
            if (Cache.TryGetValue(key, out var cached))
            {
                return cached;
            }
        }

        var d = CreateLinearCorrection(type, key.Item2);
        var fitted = MatrixFitting.FitGrayPreserving(c => Srgb.FromLinear(d.Transform(Srgb.ToLinear(c)).Clamp01()), 9);
        lock (Cache)
        {
            Cache[key] = fitted;
        }

        return fitted;
    }

    private static readonly Dictionary<(CvdType, double), ColorMatrix> Cache = [];
}
