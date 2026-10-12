namespace Akeno.ColorVision.ColorScience;

/// <summary>Optional model-based starting correction.</summary>
public enum BaseCorrection
{
    None = 0,

    /// <summary>Daltonization tuned to a protan (L-cone) simulation model.</summary>
    ProtanDaltonization = 1,

    /// <summary>Daltonization tuned to a deutan (M-cone) simulation model.</summary>
    DeutanDaltonization = 2,
}

/// <summary>Valid ranges for <see cref="CorrectionParameters"/>. Values outside are clamped.</summary>
public static class ParameterRanges
{
    public const double SeverityMin = 0.1, SeverityMax = 1.0;
    public const double RedGreenToBlueMin = 0.0, RedGreenToBlueMax = 1.2;
    public const double RedGreenToLightnessMin = -0.5, RedGreenToLightnessMax = 0.5;
    public const double HueShiftMin = -60.0, HueShiftMax = 60.0;
    public const double SaturationMin = 0.0, SaturationMax = 2.0;
    public const double ContrastMin = 0.5, ContrastMax = 1.6;
    public const double BrightnessMin = -0.25, BrightnessMax = 0.25;
    public const double IntensityMin = 0.0, IntensityMax = 1.0;
}

/// <summary>
/// The user-adjustable description of a correction. Every field maps to a real, implemented
/// matrix operation in <see cref="CorrectionPipeline"/>; nothing here is decorative.
/// </summary>
public sealed record CorrectionParameters
{
    public BaseCorrection Base { get; init; } = BaseCorrection.None;

    /// <summary>Severity assumed by the daltonization model (only used when <see cref="Base"/> is not None).</summary>
    public double ModelSeverity { get; init; } = 0.8;

    /// <summary>Strength of the red-green → blue/yellow transfer (see <see cref="ColorAdjustments.RedGreenOpponentTransfer"/>).</summary>
    public double RedGreenToBlue { get; init; }

    /// <summary>Signed red-green → lightness transfer. Positive: reds lighter, greens darker.</summary>
    public double RedGreenToLightness { get; init; }

    /// <summary>Hue rotation of the red primary in degrees. Negative: toward pink/magenta; positive: toward orange.</summary>
    public double RedHueShift { get; init; }

    /// <summary>Hue rotation of the green primary in degrees. Positive: toward cyan/teal; negative: toward yellow.</summary>
    public double GreenHueShift { get; init; }

    public double Saturation { get; init; } = 1.0;

    public double Contrast { get; init; } = 1.0;

    public double Brightness { get; init; }

    public static CorrectionParameters Neutral { get; } = new();

    /// <summary>Returns a copy with every value clamped into its valid range (NaN becomes the neutral value).</summary>
    public CorrectionParameters Clamped() => this with
    {
        Base = Enum.IsDefined(Base) ? Base : BaseCorrection.None,
        ModelSeverity = Clamp(ModelSeverity, ParameterRanges.SeverityMin, ParameterRanges.SeverityMax, 0.8),
        RedGreenToBlue = Clamp(RedGreenToBlue, ParameterRanges.RedGreenToBlueMin, ParameterRanges.RedGreenToBlueMax, 0),
        RedGreenToLightness = Clamp(RedGreenToLightness, ParameterRanges.RedGreenToLightnessMin, ParameterRanges.RedGreenToLightnessMax, 0),
        RedHueShift = Clamp(RedHueShift, ParameterRanges.HueShiftMin, ParameterRanges.HueShiftMax, 0),
        GreenHueShift = Clamp(GreenHueShift, ParameterRanges.HueShiftMin, ParameterRanges.HueShiftMax, 0),
        Saturation = Clamp(Saturation, ParameterRanges.SaturationMin, ParameterRanges.SaturationMax, 1),
        Contrast = Clamp(Contrast, ParameterRanges.ContrastMin, ParameterRanges.ContrastMax, 1),
        Brightness = Clamp(Brightness, ParameterRanges.BrightnessMin, ParameterRanges.BrightnessMax, 0),
    };

    internal static double Clamp(double value, double min, double max, double fallback) =>
        double.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;
}

/// <summary>Turns <see cref="CorrectionParameters"/> plus an overall intensity into a single matrix.</summary>
public static class CorrectionPipeline
{
    /// <summary>
    /// Builds the final matrix:
    /// <code>
    ///   discrimination = PrimaryHue ∘ OpponentTransfer ∘ BaseCorrection
    ///   blended        = lerp(Identity, discrimination, intensity)
    ///   final          = Brightness ∘ Contrast ∘ Saturation ∘ blended
    /// </code>
    /// Intensity only scales the discrimination part; saturation/contrast/brightness are applied
    /// as set so that "strength 0" still honours a pure brightness/contrast profile.
    /// </summary>
    public static ColorMatrix Build(CorrectionParameters parameters, double intensity)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        var p = parameters.Clamped();
        var strength = CorrectionParameters.Clamp(intensity, ParameterRanges.IntensityMin, ParameterRanges.IntensityMax, 0);

        var discrimination = BuildDiscrimination(p);
        var blended = discrimination.WithStrength(strength);

        var tone = ColorAdjustments.Saturation(p.Saturation)
            .Then(ColorAdjustments.Contrast(p.Contrast))
            .Then(ColorAdjustments.Brightness(p.Brightness));

        return blended.Then(tone);
    }

    /// <summary>The discrimination-enhancing part only (no tone adjustments, full strength).</summary>
    public static ColorMatrix BuildDiscrimination(CorrectionParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        var p = parameters.Clamped();
        var m = p.Base switch
        {
            BaseCorrection.ProtanDaltonization => Daltonizer.CreateLinearCorrection(CvdType.Protan, p.ModelSeverity),
            BaseCorrection.DeutanDaltonization => Daltonizer.CreateLinearCorrection(CvdType.Deutan, p.ModelSeverity),
            _ => ColorMatrix.Identity,
        };

        if (p.RedGreenToBlue != 0 || p.RedGreenToLightness != 0)
        {
            m = m.Then(ColorAdjustments.RedGreenOpponentTransfer(p.RedGreenToBlue, p.RedGreenToLightness));
        }

        if (p.RedHueShift != 0 || p.GreenHueShift != 0)
        {
            m = m.Then(ColorAdjustments.PrimaryHueRotation(p.RedHueShift, p.GreenHueShift));
        }

        return m;
    }
}
