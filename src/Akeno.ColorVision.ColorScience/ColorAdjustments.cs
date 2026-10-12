namespace Akeno.ColorVision.ColorScience;

/// <summary>
/// Building blocks for the correction pipeline. Every function returns a real affine
/// <see cref="ColorMatrix"/> that can be executed by the Magnification API. All of them except
/// brightness/contrast keep grays neutral (rows sum to 1, no offset).
/// </summary>
public static class ColorAdjustments
{
    // Luma weights used for operations on gamma-encoded values (same weights as SVG/CSS filters).
    private const double Wr = 0.2126;
    private const double Wg = 0.7152;
    private const double Wb = 0.0722;

    /// <summary>
    /// Saturation: 0 = grayscale, 1 = unchanged, &gt;1 = more saturated. Luma-preserving
    /// (equivalent to the CSS/SVG <c>saturate()</c> filter).
    /// </summary>
    public static ColorMatrix Saturation(double s) => ColorMatrix.FromRowMajor(
    [
        ((1 - s) * Wr) + s, (1 - s) * Wg, (1 - s) * Wb,
        (1 - s) * Wr, ((1 - s) * Wg) + s, (1 - s) * Wb,
        (1 - s) * Wr, (1 - s) * Wg, ((1 - s) * Wb) + s,
    ]);

    /// <summary>Contrast around a pivot (default mid-gray 0.5): out = c·(in − pivot) + pivot.</summary>
    public static ColorMatrix Contrast(double c, double pivot = 0.5)
    {
        var offset = (1 - c) * pivot;
        return ColorMatrix.FromRowMajor([c, 0, 0, 0, c, 0, 0, 0, c], offset, offset, offset);
    }

    /// <summary>Brightness: adds a constant to every channel.</summary>
    public static ColorMatrix Brightness(double b) => ColorMatrix.Offset(b, b, b);

    /// <summary>
    /// Luma-preserving hue rotation around the gray axis (the CSS/SVG <c>hue-rotate()</c> matrix).
    /// Positive angles move red toward yellow/green; negative angles move red toward magenta.
    /// </summary>
    public static ColorMatrix HueRotation(double degrees)
    {
        var rad = degrees * Math.PI / 180.0;
        var cos = Math.Cos(rad);
        var sin = Math.Sin(rad);
        return ColorMatrix.FromRowMajor(
        [
            0.213 + (cos * 0.787) - (sin * 0.213), 0.715 - (cos * 0.715) - (sin * 0.715), 0.072 - (cos * 0.072) + (sin * 0.928),
            0.213 - (cos * 0.213) + (sin * 0.143), 0.715 + (cos * 0.285) + (sin * 0.140), 0.072 - (cos * 0.072) - (sin * 0.283),
            0.213 - (cos * 0.213) - (sin * 0.787), 0.715 - (cos * 0.715) + (sin * 0.715), 0.072 + (cos * 0.928) + (sin * 0.072),
        ]);
    }

    /// <summary>
    /// Re-maps where the red and green primaries end up while keeping grays neutral:
    /// pure red is hue-rotated by <paramref name="redDegrees"/>, pure green by
    /// <paramref name="greenDegrees"/>, and the blue primary absorbs the difference so that
    /// red + green + blue still maps to white. With equal angles this reduces exactly to
    /// <see cref="HueRotation"/>.
    /// <para>Negative red angles push reds toward pink/magenta; positive green angles push greens toward cyan/teal.</para>
    /// </summary>
    public static ColorMatrix PrimaryHueRotation(double redDegrees, double greenDegrees)
    {
        var red = HueRotation(redDegrees).Transform(new Rgb(1, 0, 0));
        var green = HueRotation(greenDegrees).Transform(new Rgb(0, 1, 0));
        var blue = new Rgb(1 - red.R - green.R, 1 - red.G - green.G, 1 - red.B - green.B);
        return ColorMatrix.FromColumns(red, green, blue);
    }

    /// <summary>
    /// Red-green opponent transfer. The red-minus-green signal, which red-green color vision
    /// deficiency weakens, is copied into channels that remain visible:
    /// <list type="bullet">
    /// <item><paramref name="toBlue"/>: blue += k·(R − G). Reds gain blue (look more purple/pink),
    /// greens lose blue (look more yellow), so the difference moves onto the blue–yellow axis.</item>
    /// <item><paramref name="toLightness"/>: R, G, B += k·(R − G). Positive values make reds lighter
    /// and greens darker; negative values do the opposite.</item>
    /// </list>
    /// Grays (R = G) are unaffected.
    /// </summary>
    public static ColorMatrix RedGreenOpponentTransfer(double toBlue, double toLightness)
    {
        var vr = toLightness;
        var vg = toLightness;
        var vb = toLightness + toBlue;
        return ColorMatrix.FromRowMajor(
        [
            1 + vr, -vr, 0,
            vg, 1 - vg, 0,
            vb, -vb, 1,
        ]);
    }
}
