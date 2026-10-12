namespace Akeno.ColorVision.ColorScience;

/// <summary>
/// sRGB transfer functions (IEC 61966-2-1) and luminance helpers.
/// Negative inputs are handled by odd-symmetric extension so out-of-gamut intermediate values
/// survive a round trip instead of producing NaN.
/// </summary>
public static class Srgb
{
    /// <summary>Rec. 709 / sRGB luminance weights, applied to <b>linear</b> RGB.</summary>
    public const double LumaR = 0.2126;
    public const double LumaG = 0.7152;
    public const double LumaB = 0.0722;

    public static double ToLinear(double encoded)
    {
        var sign = Math.Sign(encoded);
        var v = Math.Abs(encoded);
        var linear = v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        return sign * linear;
    }

    public static double FromLinear(double linear)
    {
        var sign = Math.Sign(linear);
        var v = Math.Abs(linear);
        var encoded = v <= 0.0031308 ? v * 12.92 : (1.055 * Math.Pow(v, 1.0 / 2.4)) - 0.055;
        return sign * encoded;
    }

    public static Rgb ToLinear(Rgb encoded) => new(ToLinear(encoded.R), ToLinear(encoded.G), ToLinear(encoded.B));

    public static Rgb FromLinear(Rgb linear) => new(FromLinear(linear.R), FromLinear(linear.G), FromLinear(linear.B));

    /// <summary>Relative luminance Y (0..1) of a gamma-encoded sRGB color, clamped to the gamut first.</summary>
    public static double RelativeLuminance(Rgb encoded)
    {
        var lin = ToLinear(encoded.Clamp01());
        return (LumaR * lin.R) + (LumaG * lin.G) + (LumaB * lin.B);
    }

    /// <summary>WCAG 2.x contrast ratio between two gamma-encoded colors (1..21).</summary>
    public static double ContrastRatio(Rgb a, Rgb b)
    {
        var la = RelativeLuminance(a);
        var lb = RelativeLuminance(b);
        var hi = Math.Max(la, lb);
        var lo = Math.Min(la, lb);
        return (hi + 0.05) / (lo + 0.05);
    }
}
