namespace Akeno.ColorVision.ColorScience;

/// <summary>CIE 1976 L*a*b* coordinates (D65 white point).</summary>
public readonly record struct CieLab(double L, double A, double B);

/// <summary>Perceptual color spaces and color-difference formulas.</summary>
public static class ColorDifference
{
    // D65 reference white (CIE 1931 2°), Y normalised to 1.
    private const double Xn = 0.95047;
    private const double Yn = 1.00000;
    private const double Zn = 1.08883;

    /// <summary>Converts a gamma-encoded sRGB color (clamped to gamut) to CIELAB (D65).</summary>
    public static CieLab ToLab(Rgb encoded)
    {
        var lin = Srgb.ToLinear(encoded.Clamp01());
        var x = (0.4124564 * lin.R) + (0.3575761 * lin.G) + (0.1804375 * lin.B);
        var y = (0.2126729 * lin.R) + (0.7151522 * lin.G) + (0.0721750 * lin.B);
        var z = (0.0193339 * lin.R) + (0.1191920 * lin.G) + (0.9503041 * lin.B);

        var fx = F(x / Xn);
        var fy = F(y / Yn);
        var fz = F(z / Zn);
        return new CieLab((116 * fy) - 16, 500 * (fx - fy), 200 * (fy - fz));

        static double F(double t)
        {
            const double delta = 6.0 / 29.0;
            return t > delta * delta * delta ? Math.Cbrt(t) : (t / (3 * delta * delta)) + (4.0 / 29.0);
        }
    }

    /// <summary>CIEDE2000 color difference between two sRGB colors.</summary>
    public static double DeltaE2000(Rgb a, Rgb b) => DeltaE2000(ToLab(a), ToLab(b));

    /// <summary>
    /// CIEDE2000 (kL = kC = kH = 1), implemented after Sharma, Wu and Dalal (2005),
    /// "The CIEDE2000 color-difference formula: implementation notes, supplementary test data,
    /// and mathematical observations", Color Research &amp; Application 30(1).
    /// Roughly: ~1 is a just-noticeable difference, &lt;5 is easy to confuse at a glance.
    /// </summary>
    public static double DeltaE2000(CieLab lab1, CieLab lab2)
    {
        const double pow25To7 = 6103515625.0; // 25^7
        var c1 = Math.Sqrt((lab1.A * lab1.A) + (lab1.B * lab1.B));
        var c2 = Math.Sqrt((lab2.A * lab2.A) + (lab2.B * lab2.B));
        var cBar = (c1 + c2) / 2;
        var cBar7 = Math.Pow(cBar, 7);
        var g = 0.5 * (1 - Math.Sqrt(cBar7 / (cBar7 + pow25To7)));

        var a1p = (1 + g) * lab1.A;
        var a2p = (1 + g) * lab2.A;
        var c1p = Math.Sqrt((a1p * a1p) + (lab1.B * lab1.B));
        var c2p = Math.Sqrt((a2p * a2p) + (lab2.B * lab2.B));
        var h1p = HueDegrees(lab1.B, a1p);
        var h2p = HueDegrees(lab2.B, a2p);

        var dLp = lab2.L - lab1.L;
        var dCp = c2p - c1p;
        double dhp;
        if (c1p * c2p == 0)
        {
            dhp = 0;
        }
        else
        {
            dhp = h2p - h1p;
            if (dhp > 180)
            {
                dhp -= 360;
            }
            else if (dhp < -180)
            {
                dhp += 360;
            }
        }

        var dHp = 2 * Math.Sqrt(c1p * c2p) * Math.Sin(ToRad(dhp / 2));

        var lBarP = (lab1.L + lab2.L) / 2;
        var cBarP = (c1p + c2p) / 2;
        double hBarP;
        if (c1p * c2p == 0)
        {
            hBarP = h1p + h2p;
        }
        else if (Math.Abs(h1p - h2p) <= 180)
        {
            hBarP = (h1p + h2p) / 2;
        }
        else if (h1p + h2p < 360)
        {
            hBarP = (h1p + h2p + 360) / 2;
        }
        else
        {
            hBarP = (h1p + h2p - 360) / 2;
        }

        var t = 1
            - (0.17 * Math.Cos(ToRad(hBarP - 30)))
            + (0.24 * Math.Cos(ToRad(2 * hBarP)))
            + (0.32 * Math.Cos(ToRad((3 * hBarP) + 6)))
            - (0.20 * Math.Cos(ToRad((4 * hBarP) - 63)));
        var dTheta = 30 * Math.Exp(-Math.Pow((hBarP - 275) / 25, 2));
        var cBarP7 = Math.Pow(cBarP, 7);
        var rc = 2 * Math.Sqrt(cBarP7 / (cBarP7 + pow25To7));
        var lMinus50Sq = (lBarP - 50) * (lBarP - 50);
        var sl = 1 + (0.015 * lMinus50Sq / Math.Sqrt(20 + lMinus50Sq));
        var sc = 1 + (0.045 * cBarP);
        var sh = 1 + (0.015 * cBarP * t);
        var rt = -Math.Sin(ToRad(2 * dTheta)) * rc;

        var lTerm = dLp / sl;
        var cTerm = dCp / sc;
        var hTerm = dHp / sh;
        return Math.Sqrt((lTerm * lTerm) + (cTerm * cTerm) + (hTerm * hTerm) + (rt * cTerm * hTerm));

        static double HueDegrees(double b, double ap)
        {
            if (b == 0 && ap == 0)
            {
                return 0;
            }

            var h = Math.Atan2(b, ap) * 180 / Math.PI;
            return h < 0 ? h + 360 : h;
        }

        static double ToRad(double deg) => deg * Math.PI / 180;
    }
}
