using System.Globalization;
using System.Text;

namespace Akeno.ColorVision.ColorScience;

/// <summary>
/// An immutable affine RGB color transform written in <b>column-vector</b> convention:
/// <code>
///   [R']   [m00 m01 m02]   [R]   [t0]
///   [G'] = [m10 m11 m12] · [G] + [t1]
///   [B']   [m20 m21 m22]   [B]   [t2]
/// </code>
/// Row = output channel, column = input channel. This is the form used throughout the color
/// science code. The Windows Magnification API uses the transposed <i>row-vector</i> layout
/// (see <see cref="ToMagColorEffect"/>); the conversion is done in exactly one place so that the
/// rest of the code base never has to think about it.
/// </summary>
public sealed class ColorMatrix : IEquatable<ColorMatrix>
{
    // Row-major 3x4: [m00 m01 m02 t0 | m10 m11 m12 t1 | m20 m21 m22 t2]
    private readonly double[] _v;

    private ColorMatrix(double[] values) => _v = values;

    public static ColorMatrix Identity { get; } = new([1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0]);

    /// <summary>Creates a matrix from a row-major 3x3 linear part and an optional offset.</summary>
    public static ColorMatrix FromRowMajor(ReadOnlySpan<double> m3x3, double t0 = 0, double t1 = 0, double t2 = 0)
    {
        if (m3x3.Length != 9)
        {
            throw new ArgumentException("Expected 9 values (row-major 3x3).", nameof(m3x3));
        }

        return new ColorMatrix(
        [
            m3x3[0], m3x3[1], m3x3[2], t0,
            m3x3[3], m3x3[4], m3x3[5], t1,
            m3x3[6], m3x3[7], m3x3[8], t2,
        ]);
    }

    /// <summary>Creates a matrix from 12 row-major values (3 rows of: three coefficients then an offset).</summary>
    public static ColorMatrix FromAffineRowMajor(ReadOnlySpan<double> m3x4)
    {
        if (m3x4.Length != 12)
        {
            throw new ArgumentException("Expected 12 values (row-major 3x4).", nameof(m3x4));
        }

        return new ColorMatrix(m3x4.ToArray());
    }

    /// <summary>Creates a matrix whose columns are the images of pure red, green and blue.</summary>
    public static ColorMatrix FromColumns(Rgb red, Rgb green, Rgb blue) =>
        FromRowMajor([red.R, green.R, blue.R, red.G, green.G, blue.G, red.B, green.B, blue.B]);

    public static ColorMatrix Offset(double r, double g, double b) => FromRowMajor([1, 0, 0, 0, 1, 0, 0, 0, 1], r, g, b);

    public static ColorMatrix Scale(double r, double g, double b) => FromRowMajor([r, 0, 0, 0, g, 0, 0, 0, b]);

    /// <summary>Linear coefficient: contribution of input channel <paramref name="col"/> to output channel <paramref name="row"/>.</summary>
    public double this[int row, int col]
    {
        get
        {
            if ((uint)row > 2 || (uint)col > 2)
            {
                throw new ArgumentOutOfRangeException(row > 2 ? nameof(row) : nameof(col));
            }

            return _v[(row * 4) + col];
        }
    }

    /// <summary>Constant offset added to output channel <paramref name="row"/>.</summary>
    public double OffsetOf(int row)
    {
        if ((uint)row > 2)
        {
            throw new ArgumentOutOfRangeException(nameof(row));
        }

        return _v[(row * 4) + 3];
    }

    /// <summary>The 12 coefficients in row-major 3x4 order (copy).</summary>
    public double[] ToArray() => (double[])_v.Clone();

    public bool IsFinite => Array.TrueForAll(_v, double.IsFinite);

    public double Determinant =>
        (this[0, 0] * ((this[1, 1] * this[2, 2]) - (this[1, 2] * this[2, 1])))
        - (this[0, 1] * ((this[1, 0] * this[2, 2]) - (this[1, 2] * this[2, 0])))
        + (this[0, 2] * ((this[1, 0] * this[2, 1]) - (this[1, 1] * this[2, 0])));

    /// <summary>Applies the transform without clamping (useful for analysis).</summary>
    public Rgb Transform(Rgb c) => new(
        (_v[0] * c.R) + (_v[1] * c.G) + (_v[2] * c.B) + _v[3],
        (_v[4] * c.R) + (_v[5] * c.G) + (_v[6] * c.B) + _v[7],
        (_v[8] * c.R) + (_v[9] * c.G) + (_v[10] * c.B) + _v[11]);

    /// <summary>Applies the transform and clamps to [0, 1], mirroring what a display pipeline does.</summary>
    public Rgb TransformClamped(Rgb c) => Transform(c).Clamp01();

    /// <summary>Returns <c>outer ∘ inner</c>: the transform that applies <paramref name="inner"/> first, then <paramref name="outer"/>.</summary>
    public static ColorMatrix Compose(ColorMatrix outer, ColorMatrix inner)
    {
        ArgumentNullException.ThrowIfNull(outer);
        ArgumentNullException.ThrowIfNull(inner);
        var r = new double[12];
        for (var row = 0; row < 3; row++)
        {
            for (var col = 0; col < 3; col++)
            {
                r[(row * 4) + col] =
                    (outer[row, 0] * inner[0, col]) +
                    (outer[row, 1] * inner[1, col]) +
                    (outer[row, 2] * inner[2, col]);
            }

            r[(row * 4) + 3] =
                (outer[row, 0] * inner.OffsetOf(0)) +
                (outer[row, 1] * inner.OffsetOf(1)) +
                (outer[row, 2] * inner.OffsetOf(2)) +
                outer.OffsetOf(row);
        }

        return new ColorMatrix(r);
    }

    /// <summary>Mathematical composition: <c>(a * b)(x) = a(b(x))</c>.</summary>
    public static ColorMatrix operator *(ColorMatrix outer, ColorMatrix inner) => Compose(outer, inner);

    /// <summary>Returns the transform that applies this matrix first and then <paramref name="next"/>.</summary>
    public ColorMatrix Then(ColorMatrix next) => Compose(next, this);

    /// <summary>Coefficient-wise linear interpolation. Valid for affine maps: the result is the affine blend of both transforms.</summary>
    public static ColorMatrix Lerp(ColorMatrix from, ColorMatrix to, double t)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);
        var r = new double[12];
        for (var i = 0; i < 12; i++)
        {
            r[i] = from._v[i] + ((to._v[i] - from._v[i]) * t);
        }

        return new ColorMatrix(r);
    }

    /// <summary>Blends between identity (strength 0) and this matrix (strength 1). Strength is clamped to [0, 1].</summary>
    public ColorMatrix WithStrength(double strength) =>
        Lerp(Identity, this, double.IsNaN(strength) ? 0 : Math.Clamp(strength, 0.0, 1.0));

    public bool TryInvert(out ColorMatrix inverse)
    {
        inverse = Identity;
        var det = Determinant;
        if (!double.IsFinite(det) || Math.Abs(det) < 1e-12)
        {
            return false;
        }

        var a = this;
        var inv = new double[9];
        inv[0] = ((a[1, 1] * a[2, 2]) - (a[1, 2] * a[2, 1])) / det;
        inv[1] = ((a[0, 2] * a[2, 1]) - (a[0, 1] * a[2, 2])) / det;
        inv[2] = ((a[0, 1] * a[1, 2]) - (a[0, 2] * a[1, 1])) / det;
        inv[3] = ((a[1, 2] * a[2, 0]) - (a[1, 0] * a[2, 2])) / det;
        inv[4] = ((a[0, 0] * a[2, 2]) - (a[0, 2] * a[2, 0])) / det;
        inv[5] = ((a[0, 2] * a[1, 0]) - (a[0, 0] * a[1, 2])) / det;
        inv[6] = ((a[1, 0] * a[2, 1]) - (a[1, 1] * a[2, 0])) / det;
        inv[7] = ((a[0, 1] * a[2, 0]) - (a[0, 0] * a[2, 1])) / det;
        inv[8] = ((a[0, 0] * a[1, 1]) - (a[0, 1] * a[1, 0])) / det;

        // Inverse offset: -A^-1 · t
        var t0 = -((inv[0] * OffsetOf(0)) + (inv[1] * OffsetOf(1)) + (inv[2] * OffsetOf(2)));
        var t1 = -((inv[3] * OffsetOf(0)) + (inv[4] * OffsetOf(1)) + (inv[5] * OffsetOf(2)));
        var t2 = -((inv[6] * OffsetOf(0)) + (inv[7] * OffsetOf(1)) + (inv[8] * OffsetOf(2)));
        inverse = FromRowMajor(inv, t0, t1, t2);
        return inverse.IsFinite;
    }

    public double MaxAbsDifference(ColorMatrix other)
    {
        ArgumentNullException.ThrowIfNull(other);
        var max = 0.0;
        for (var i = 0; i < 12; i++)
        {
            max = Math.Max(max, Math.Abs(_v[i] - other._v[i]));
        }

        return max;
    }

    public bool IsIdentity(double tolerance = 1e-6) => MaxAbsDifference(Identity) <= tolerance;

    public bool ApproximatelyEquals(ColorMatrix other, double tolerance = 1e-6) => MaxAbsDifference(other) <= tolerance;

    // ------------------------------------------------------------------------------------------
    // Windows Magnification API interop (MAGCOLOREFFECT = float transform[5][5])
    // ------------------------------------------------------------------------------------------

    /// <summary>Number of floats in a MAGCOLOREFFECT structure.</summary>
    public const int MagColorEffectLength = 25;

    /// <summary>
    /// Converts to the 25-float MAGCOLOREFFECT layout used by <c>MagSetFullscreenColorEffect</c>.
    /// <para>
    /// MAGCOLOREFFECT uses the row-vector convention (as GDI+ ColorMatrix does):
    /// <c>[R' G' B' A' 1] = [R G B A 1] · T</c>, so <c>T[input][output]</c> holds the contribution
    /// of an input channel to an output channel and row 4 holds the constant offsets.
    /// Microsoft's documented examples confirm this: the inversion effect stores <c>-1</c> on the
    /// diagonal and <c>1, 1, 1</c> in row 4.
    /// </para>
    /// Alpha is passed through unchanged.
    /// </summary>
    public float[] ToMagColorEffect()
    {
        var t = new float[MagColorEffectLength];
        for (var input = 0; input < 3; input++)
        {
            for (var output = 0; output < 3; output++)
            {
                t[(input * 5) + output] = (float)this[output, input];
            }
        }

        t[(3 * 5) + 3] = 1f;
        for (var output = 0; output < 3; output++)
        {
            t[(4 * 5) + output] = (float)OffsetOf(output);
        }

        t[(4 * 5) + 4] = 1f;
        return t;
    }

    /// <summary>Inverse of <see cref="ToMagColorEffect"/>. Alpha terms are ignored.</summary>
    public static ColorMatrix FromMagColorEffect(ReadOnlySpan<float> t)
    {
        if (t.Length != MagColorEffectLength)
        {
            throw new ArgumentException($"Expected {MagColorEffectLength} values.", nameof(t));
        }

        var m = new double[9];
        for (var input = 0; input < 3; input++)
        {
            for (var output = 0; output < 3; output++)
            {
                m[(output * 3) + input] = t[(input * 5) + output];
            }
        }

        return FromRowMajor(m, t[20], t[21], t[22]);
    }

    public bool Equals(ColorMatrix? other) =>
        other is not null && (ReferenceEquals(this, other) || _v.AsSpan().SequenceEqual(other._v));

    public override bool Equals(object? obj) => obj is ColorMatrix m && Equals(m);

    public override int GetHashCode()
    {
        var hash = default(HashCode);
        foreach (var v in _v)
        {
            hash.Add(v);
        }

        return hash.ToHashCode();
    }

    public override string ToString()
    {
        var sb = new StringBuilder();
        for (var row = 0; row < 3; row++)
        {
            sb.Append(CultureInfo.InvariantCulture,
                $"[{this[row, 0],8:0.0000} {this[row, 1],8:0.0000} {this[row, 2],8:0.0000} | {OffsetOf(row),8:0.0000}]");
            if (row < 2)
            {
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }
}
