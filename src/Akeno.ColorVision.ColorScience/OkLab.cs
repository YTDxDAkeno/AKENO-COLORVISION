namespace Akeno.ColorVision.ColorScience;

/// <summary>OKLab coordinates (Björn Ottosson, 2020). L in [0, 1].</summary>
public readonly record struct OkLab(double L, double A, double B)
{
    /// <summary>Chroma (distance from the gray axis).</summary>
    public double Chroma => Math.Sqrt((A * A) + (B * B));

    /// <summary>Hue angle in degrees [0, 360).</summary>
    public double HueDegrees
    {
        get
        {
            var h = Math.Atan2(B, A) * 180 / Math.PI;
            return h < 0 ? h + 360 : h;
        }
    }

    /// <summary>Converts a gamma-encoded sRGB color (clamped) to OKLab.</summary>
    public static OkLab FromSrgb(Rgb encoded)
    {
        var c = Srgb.ToLinear(encoded.Clamp01());
        var l = (0.4122214708 * c.R) + (0.5363325363 * c.G) + (0.0514459929 * c.B);
        var m = (0.2119034982 * c.R) + (0.6806995451 * c.G) + (0.1073969566 * c.B);
        var s = (0.0883024619 * c.R) + (0.2817188376 * c.G) + (0.6299787005 * c.B);
        var l_ = Math.Cbrt(l);
        var m_ = Math.Cbrt(m);
        var s_ = Math.Cbrt(s);
        return new OkLab(
            (0.2104542553 * l_) + (0.7936177850 * m_) - (0.0040720468 * s_),
            (1.9779984951 * l_) - (2.4285922050 * m_) + (0.4505937099 * s_),
            (0.0259040371 * l_) + (0.7827717662 * m_) - (0.8086757660 * s_));
    }

    public double DistanceTo(OkLab other)
    {
        var dl = L - other.L;
        var da = A - other.A;
        var db = B - other.B;
        return Math.Sqrt((dl * dl) + (da * da) + (db * db));
    }
}
