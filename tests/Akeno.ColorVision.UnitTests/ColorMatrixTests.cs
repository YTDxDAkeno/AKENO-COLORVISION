using Akeno.ColorVision.ColorScience;

namespace Akeno.ColorVision.UnitTests;

public class ColorMatrixTests
{
    private const double Tol = 1e-9;

    [Fact]
    public void Identity_LeavesColorsUnchanged()
    {
        var c = new Rgb(0.2, 0.5, 0.9);
        Assert.Equal(c, ColorMatrix.Identity.Transform(c));
        Assert.True(ColorMatrix.Identity.IsIdentity());
        Assert.Equal(1.0, ColorMatrix.Identity.Determinant, 12);
    }

    [Fact]
    public void Transform_UsesRowsAsOutputChannels()
    {
        // Output red = input green; output green = input blue; output blue = input red.
        var m = ColorMatrix.FromRowMajor([0, 1, 0, 0, 0, 1, 1, 0, 0]);
        var r = m.Transform(new Rgb(0.1, 0.2, 0.3));
        Assert.Equal(0.2, r.R, 12);
        Assert.Equal(0.3, r.G, 12);
        Assert.Equal(0.1, r.B, 12);
    }

    [Fact]
    public void Offset_IsAdded()
    {
        var m = ColorMatrix.Offset(0.1, -0.2, 0.3);
        var r = m.Transform(new Rgb(0.5, 0.5, 0.5));
        Assert.Equal(0.6, r.R, 12);
        Assert.Equal(0.3, r.G, 12);
        Assert.Equal(0.8, r.B, 12);
    }

    [Fact]
    public void Compose_AppliesInnerFirst()
    {
        var scale = ColorMatrix.Scale(2, 2, 2);
        var offset = ColorMatrix.Offset(0.1, 0.1, 0.1);
        var c = new Rgb(0.2, 0.3, 0.4);

        // (scale * offset)(c) = scale(offset(c)) = 2(c + 0.1)
        var a = (scale * offset).Transform(c);
        Assert.Equal(0.6, a.R, 12);
        Assert.Equal(0.8, a.G, 12);

        // offset.Then(scale) is the same thing written in application order.
        Assert.True(offset.Then(scale).ApproximatelyEquals(scale * offset, Tol));

        // And the other order differs: 2c + 0.1.
        var b = (offset * scale).Transform(c);
        Assert.Equal(0.5, b.R, 12);
    }

    [Fact]
    public void Compose_IsAssociative()
    {
        var a = ColorAdjustments.Saturation(1.3);
        var b = ColorAdjustments.Contrast(1.2);
        var c = ColorAdjustments.HueRotation(25);
        Assert.True(((a * b) * c).ApproximatelyEquals(a * (b * c), 1e-12));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.3)]
    [InlineData(0.7)]
    [InlineData(1.0)]
    public void WithStrength_InterpolatesBetweenIdentityAndTarget(double s)
    {
        var target = ColorAdjustments.RedGreenOpponentTransfer(0.8, 0.2);
        var blended = target.WithStrength(s);
        var c = new Rgb(0.8, 0.2, 0.1);
        var expected = Rgb.Lerp(c, target.Transform(c), s);
        var actual = blended.Transform(c);
        Assert.Equal(expected.R, actual.R, 12);
        Assert.Equal(expected.G, actual.G, 12);
        Assert.Equal(expected.B, actual.B, 12);
    }

    [Theory]
    [InlineData(-1.0, 0.0)]
    [InlineData(2.0, 1.0)]
    [InlineData(double.NaN, 0.0)]
    public void WithStrength_ClampsInvalidValues(double input, double effective)
    {
        var target = ColorAdjustments.Saturation(1.8);
        Assert.True(target.WithStrength(input).ApproximatelyEquals(target.WithStrength(effective), Tol));
    }

    [Fact]
    public void TryInvert_ProducesInverse()
    {
        var m = ColorAdjustments.Contrast(1.3) * ColorAdjustments.HueRotation(40) * ColorMatrix.Offset(0.05, 0, -0.02);
        Assert.True(m.TryInvert(out var inv));
        Assert.True((inv * m).IsIdentity(1e-9));
        Assert.True((m * inv).IsIdentity(1e-9));
    }

    [Fact]
    public void TryInvert_FailsForSingularMatrix()
    {
        var gray = ColorAdjustments.Saturation(0);
        Assert.False(gray.TryInvert(out _));
    }

    [Fact]
    public void MagColorEffect_UsesRowVectorLayoutWithOffsetsInRow4()
    {
        // Same convention as Microsoft's documented examples: T[input][output], offsets in row 4.
        var m = ColorMatrix.FromRowMajor([0.1, 0.2, 0.3, 0.4, 0.5, 0.6, 0.7, 0.8, 0.9], 0.01, 0.02, 0.03);
        var t = m.ToMagColorEffect();
        Assert.Equal(25, t.Length);
        // Contribution of input green (row 1) to output red (column 0) = m[0,1] = 0.2
        Assert.Equal(0.2f, t[(1 * 5) + 0], 6);
        // Contribution of input red (row 0) to output blue (column 2) = m[2,0] = 0.7
        Assert.Equal(0.7f, t[(0 * 5) + 2], 6);
        Assert.Equal(0.01f, t[20], 6);
        Assert.Equal(0.02f, t[21], 6);
        Assert.Equal(0.03f, t[22], 6);
        // Alpha passes through and the homogeneous coordinate is 1.
        Assert.Equal(1f, t[18]);
        Assert.Equal(1f, t[24]);
        Assert.Equal(0f, t[3]);
        Assert.Equal(0f, t[15]);
    }

    [Fact]
    public void MagColorEffect_MatchesMicrosoftInvertExample()
    {
        // From the Magnification API documentation: color inversion.
        float[] documented =
        [
            -1, 0, 0, 0, 0,
            0, -1, 0, 0, 0,
            0, 0, -1, 0, 0,
            0, 0, 0, 1, 0,
            1, 1, 1, 0, 1,
        ];
        var m = ColorMatrix.FromMagColorEffect(documented);
        var inverted = m.Transform(new Rgb(0.2, 0.6, 1.0));
        Assert.Equal(0.8, inverted.R, 6);
        Assert.Equal(0.4, inverted.G, 6);
        Assert.Equal(0.0, inverted.B, 6);
        Assert.Equal(documented, m.ToMagColorEffect());
    }

    [Fact]
    public void MagColorEffect_MatchesMicrosoftGrayscaleExample()
    {
        float[] documented =
        [
            0.3f, 0.3f, 0.3f, 0, 0,
            0.6f, 0.6f, 0.6f, 0, 0,
            0.1f, 0.1f, 0.1f, 0, 0,
            0, 0, 0, 1, 0,
            0, 0, 0, 0, 1,
        ];
        var m = ColorMatrix.FromMagColorEffect(documented);
        var gray = m.Transform(new Rgb(1, 0, 0));
        Assert.Equal(0.3, gray.R, 6);
        Assert.Equal(0.3, gray.G, 6);
        Assert.Equal(0.3, gray.B, 6);
    }

    [Fact]
    public void MagColorEffect_RoundTrips()
    {
        var m = BuiltInPresetsMatrix();
        var back = ColorMatrix.FromMagColorEffect(m.ToMagColorEffect());
        Assert.True(back.ApproximatelyEquals(m, 1e-6));
    }

    [Fact]
    public void FromMagColorEffect_RejectsWrongLength() =>
        Assert.Throws<ArgumentException>(() => ColorMatrix.FromMagColorEffect(new float[24]));

    [Fact]
    public void TransformClamped_LimitsToDisplayRange()
    {
        var m = ColorMatrix.Scale(3, -1, 1);
        var r = m.TransformClamped(new Rgb(0.5, 0.5, 0.5));
        Assert.Equal(1, r.R);
        Assert.Equal(0, r.G);
        Assert.Equal(0.5, r.B, 12);
    }

    [Fact]
    public void IsFinite_DetectsNaN()
    {
        var m = ColorMatrix.FromRowMajor([1, 0, 0, 0, double.NaN, 0, 0, 0, 1]);
        Assert.False(m.IsFinite);
    }

    private static ColorMatrix BuiltInPresetsMatrix() =>
        CorrectionPipeline.Build(new CorrectionParameters { RedGreenToBlue = 0.8, RedGreenToLightness = 0.15, Saturation = 1.1, Contrast = 1.1, Brightness = 0.03 }, 0.7);
}
