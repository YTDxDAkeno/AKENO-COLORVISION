using System.Globalization;

namespace Akeno.ColorVision.ColorScience;

/// <summary>
/// An RGB triple. Unless stated otherwise the components are <b>gamma-encoded sRGB</b> values
/// in the nominal range [0, 1] (the same values a display receives). Intermediate results may
/// fall outside [0, 1]; call <see cref="Clamp01"/> before displaying them.
/// </summary>
public readonly record struct Rgb(double R, double G, double B)
{
    public static Rgb Black => new(0, 0, 0);
    public static Rgb White => new(1, 1, 1);

    public static Rgb Gray(double value) => new(value, value, value);

    public static Rgb FromBytes(byte r, byte g, byte b) => new(r / 255.0, g / 255.0, b / 255.0);

    /// <summary>Parses "#RRGGBB" or "RRGGBB" (case-insensitive).</summary>
    public static Rgb FromHex(string hex)
    {
        if (!TryParseHex(hex, out var color))
        {
            throw new FormatException($"'{hex}' is not a valid #RRGGBB color.");
        }

        return color;
    }

    public static bool TryParseHex(string? hex, out Rgb color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(hex))
        {
            return false;
        }

        var span = hex.Trim().AsSpan();
        if (span.StartsWith("#"))
        {
            span = span[1..];
        }

        if (span.Length != 6 || !uint.TryParse(span, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
        {
            return false;
        }

        color = FromBytes((byte)(value >> 16), (byte)(value >> 8), (byte)value);
        return true;
    }

    public double this[int channel] => channel switch
    {
        0 => R,
        1 => G,
        2 => B,
        _ => throw new ArgumentOutOfRangeException(nameof(channel)),
    };

    public bool IsFinite => double.IsFinite(R) && double.IsFinite(G) && double.IsFinite(B);

    public Rgb Clamp01() => new(Clamp(R), Clamp(G), Clamp(B));

    public (byte R, byte G, byte B) ToBytes() => (ToByte(R), ToByte(G), ToByte(B));

    public string ToHex()
    {
        var (r, g, b) = ToBytes();
        return string.Create(CultureInfo.InvariantCulture, $"#{r:X2}{g:X2}{b:X2}");
    }

    public static Rgb Lerp(Rgb a, Rgb b, double t) =>
        new(a.R + ((b.R - a.R) * t), a.G + ((b.G - a.G) * t), a.B + ((b.B - a.B) * t));

    /// <summary>Largest per-channel absolute difference.</summary>
    public double MaxDifference(Rgb other) =>
        Math.Max(Math.Abs(R - other.R), Math.Max(Math.Abs(G - other.G), Math.Abs(B - other.B)));

    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"({R:0.####}, {G:0.####}, {B:0.####})");

    private static double Clamp(double v) => double.IsNaN(v) ? 0 : Math.Clamp(v, 0.0, 1.0);

    private static byte ToByte(double v) => (byte)Math.Round(Clamp(v) * 255.0, MidpointRounding.AwayFromZero);
}
