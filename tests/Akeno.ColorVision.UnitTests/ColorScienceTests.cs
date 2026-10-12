using Akeno.ColorVision.ColorScience;

namespace Akeno.ColorVision.UnitTests;

public class SrgbTests
{
    [Theory]
    [InlineData(0.0)]
    [InlineData(0.02)]
    [InlineData(0.04045)]
    [InlineData(0.2)]
    [InlineData(0.5)]
    [InlineData(1.0)]
    public void TransferFunction_RoundTrips(double v) =>
        // The published sRGB constants are continuous only to ~1e-8 at the 0.04045 threshold.
        Assert.Equal(v, Srgb.FromLinear(Srgb.ToLinear(v)), 6);

    [Fact]
    public void TransferFunction_KnownValues()
    {
        Assert.Equal(0.21404, Srgb.ToLinear(0.5), 4);
        Assert.Equal(0.0, Srgb.ToLinear(0.0));
        Assert.Equal(1.0, Srgb.ToLinear(1.0), 12);
    }

    [Fact]
    public void TransferFunction_HandlesNegativeValuesSymmetrically() =>
        Assert.Equal(-Srgb.ToLinear(0.3), Srgb.ToLinear(-0.3), 12);

    [Fact]
    public void RelativeLuminance_WhiteIsOneBlackIsZero()
    {
        Assert.Equal(1.0, Srgb.RelativeLuminance(Rgb.White), 4);
        Assert.Equal(0.0, Srgb.RelativeLuminance(Rgb.Black), 12);
    }

    [Fact]
    public void ContrastRatio_BlackOnWhiteIs21() =>
        Assert.Equal(21.0, Srgb.ContrastRatio(Rgb.White, Rgb.Black), 2);
}

public class RgbTests
{
    [Theory]
    [InlineData("#8B2424", 0x8B, 0x24, 0x24)]
    [InlineData("727a32", 0x72, 0x7A, 0x32)]
    [InlineData(" #FFFFFF ", 255, 255, 255)]
    public void ParsesHex(string hex, int r, int g, int b)
    {
        var c = Rgb.FromHex(hex);
        Assert.Equal(((byte)r, (byte)g, (byte)b), c.ToBytes());
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("#12345")]
    [InlineData("#GGGGGG")]
    [InlineData("#1234567")]
    public void RejectsInvalidHex(string? hex) => Assert.False(Rgb.TryParseHex(hex, out _));

    [Fact]
    public void ToHex_ClampsAndRounds()
    {
        Assert.Equal("#FF0000", new Rgb(1.7, -0.2, 0).ToHex());
        Assert.Equal("#808080", Rgb.Gray(0.5).ToHex());
    }

    [Fact]
    public void Clamp_ReplacesNaN() => Assert.Equal(Rgb.Black, new Rgb(double.NaN, double.NaN, double.NaN).Clamp01());
}

public class MachadoModelTests
{
    public static TheoryData<CvdType, int> AllEntries()
    {
        var data = new TheoryData<CvdType, int>();
        foreach (var type in new[] { CvdType.Protan, CvdType.Deutan, CvdType.Tritan })
        {
            for (var i = 0; i <= 10; i++)
            {
                data.Add(type, i);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AllEntries))]
    public void EveryTabulatedRowSumsToOne(CvdType type, int index)
    {
        // White must stay white in the published model; this catches transcription errors.
        var m = MachadoCvdModel.GetTableEntry(type, index);
        for (var row = 0; row < 3; row++)
        {
            Assert.Equal(1.0, m[row * 3] + m[(row * 3) + 1] + m[(row * 3) + 2], 5);
        }
    }

    [Fact]
    public void SeverityZeroIsIdentity()
    {
        Assert.True(MachadoCvdModel.GetSimulationMatrix(CvdType.Protan, 0).IsIdentity(1e-9));
        Assert.True(MachadoCvdModel.GetSimulationMatrix(CvdType.Deutan, 0).IsIdentity(1e-9));
    }

    [Fact]
    public void KnownPublishedValues()
    {
        var protan = MachadoCvdModel.GetSimulationMatrix(CvdType.Protan, 1.0);
        Assert.Equal(0.152286, protan[0, 0], 6);
        Assert.Equal(1.052583, protan[0, 1], 6);
        Assert.Equal(-0.204868, protan[0, 2], 6);
        var deutan = MachadoCvdModel.GetSimulationMatrix(CvdType.Deutan, 0.5);
        Assert.Equal(0.547494, deutan[0, 0], 6);
        Assert.Equal(0.607765, deutan[0, 1], 6);
        Assert.Equal(0.781742, deutan[1, 1], 6);
    }

    [Fact]
    public void InterpolatesBetweenTabulatedSeverities()
    {
        var a = MachadoCvdModel.GetSimulationMatrix(CvdType.Protan, 0.1);
        var b = MachadoCvdModel.GetSimulationMatrix(CvdType.Protan, 0.2);
        var mid = MachadoCvdModel.GetSimulationMatrix(CvdType.Protan, 0.15);
        Assert.True(mid.ApproximatelyEquals(ColorMatrix.Lerp(a, b, 0.5), 1e-9));
    }

    [Fact]
    public void ClampsSeverity()
    {
        Assert.True(MachadoCvdModel.GetSimulationMatrix(CvdType.Deutan, 5).ApproximatelyEquals(MachadoCvdModel.GetSimulationMatrix(CvdType.Deutan, 1), 1e-12));
        Assert.True(MachadoCvdModel.GetSimulationMatrix(CvdType.Deutan, -1).IsIdentity(1e-12));
        Assert.True(MachadoCvdModel.GetSimulationMatrix(CvdType.Deutan, double.NaN).IsIdentity(1e-12));
    }

    [Fact]
    public void SimulatedRedAndGreenBecomeMoreSimilarWithSeverity()
    {
        var red = Rgb.FromHex("#B5402F");
        var green = Rgb.FromHex("#5E8A2E");
        double Diff(double s) => ColorDifference.DeltaE2000(CvdSimulator.Simulate(red, CvdType.Deutan, s), CvdSimulator.Simulate(green, CvdType.Deutan, s));
        Assert.True(Diff(0.0) > Diff(0.5));
        Assert.True(Diff(0.5) > Diff(1.0));
        Assert.True(Diff(1.0) < 8, "red/green pair should be hard to tell apart for a simulated deuteranope");
    }

    [Fact]
    public void GraysAreUnchangedBySimulation()
    {
        foreach (var v in new[] { 0.0, 0.25, 0.5, 0.75, 1.0 })
        {
            var g = Rgb.Gray(v);
            Assert.True(CvdSimulator.Simulate(g, CvdType.Protan, 1).MaxDifference(g) < 2e-3);
            Assert.True(CvdSimulator.Simulate(g, CvdType.Deutan, 1).MaxDifference(g) < 2e-3);
        }
    }
}

public class ColorDifferenceTests
{
    // Test pairs from Sharma, Wu & Dalal (2005), Table 1.
    [Theory]
    [InlineData(50.0000, 2.6772, -79.7751, 50.0000, 0.0000, -82.7485, 2.0425)]
    [InlineData(50.0000, 3.1571, -77.2803, 50.0000, 0.0000, -82.7485, 2.8615)]
    [InlineData(50.0000, 2.8361, -74.0200, 50.0000, 0.0000, -82.7485, 3.4412)]
    [InlineData(50.0000, -1.3802, -84.2814, 50.0000, 0.0000, -82.7485, 1.0000)]
    [InlineData(50.0000, 0.0000, 0.0000, 50.0000, -1.0000, 2.0000, 2.3669)]
    [InlineData(50.0000, 2.4900, -0.0010, 50.0000, -2.4900, 0.0009, 7.1792)]
    [InlineData(50.0000, 2.5000, 0.0000, 73.0000, 25.0000, -18.0000, 27.1492)]
    [InlineData(50.0000, 2.5000, 0.0000, 61.0000, -5.0000, 29.0000, 22.8977)]
    [InlineData(50.0000, 2.5000, 0.0000, 56.0000, -27.0000, -3.0000, 31.9030)]
    [InlineData(50.0000, 2.5000, 0.0000, 58.0000, 24.0000, 15.0000, 19.4535)]
    [InlineData(60.2574, -34.0099, 36.2677, 60.4626, -34.1751, 39.4387, 1.2644)]
    [InlineData(63.0109, -31.0961, -5.8663, 62.8187, -29.7946, -4.0864, 1.2630)]
    [InlineData(35.0831, -44.1164, 3.7933, 35.0232, -40.0716, 1.5901, 1.8645)]
    [InlineData(22.7233, 20.0904, -46.6940, 23.0331, 14.9730, -42.5619, 2.0373)]
    [InlineData(90.8027, -2.0831, 1.4410, 91.1528, -1.6435, 0.0447, 1.4441)]
    [InlineData(2.0776, 0.0795, -1.1350, 0.9033, -0.0636, -0.5514, 0.9082)]
    public void Ciede2000_MatchesPublishedTestData(double l1, double a1, double b1, double l2, double a2, double b2, double expected) =>
        Assert.Equal(expected, ColorDifference.DeltaE2000(new CieLab(l1, a1, b1), new CieLab(l2, a2, b2)), 4);

    [Fact]
    public void Ciede2000_IsSymmetricAndZeroForIdenticalColors()
    {
        var a = Rgb.FromHex("#6F3030");
        var b = Rgb.FromHex("#54402F");
        Assert.Equal(0, ColorDifference.DeltaE2000(a, a), 10);
        Assert.Equal(ColorDifference.DeltaE2000(a, b), ColorDifference.DeltaE2000(b, a), 10);
    }

    [Fact]
    public void Lab_WhiteAndBlack()
    {
        var white = ColorDifference.ToLab(Rgb.White);
        Assert.Equal(100, white.L, 2);
        Assert.Equal(0, white.A, 2);
        Assert.Equal(0, white.B, 2);
        Assert.Equal(0, ColorDifference.ToLab(Rgb.Black).L, 6);
    }

    [Fact]
    public void OkLab_WhiteIsNeutral()
    {
        var w = OkLab.FromSrgb(Rgb.White);
        Assert.Equal(1.0, w.L, 3);
        Assert.True(w.Chroma < 1e-3);
    }
}

public class AdjustmentTests
{
    private static readonly Rgb[] Grays = [Rgb.Gray(0), Rgb.Gray(0.25), Rgb.Gray(0.5), Rgb.Gray(1)];

    public static TheoryData<string> GrayPreservingNames() => new() { "saturation", "hue", "primary", "opponent", "deutan", "protan" };

    private static ColorMatrix Get(string name) => name switch
    {
        "saturation" => ColorAdjustments.Saturation(1.6),
        "hue" => ColorAdjustments.HueRotation(35),
        "primary" => ColorAdjustments.PrimaryHueRotation(-25, 30),
        "opponent" => ColorAdjustments.RedGreenOpponentTransfer(0.9, 0.3),
        "deutan" => Daltonizer.CreateLinearCorrection(CvdType.Deutan, 0.8),
        "protan" => Daltonizer.CreateLinearCorrection(CvdType.Protan, 0.8),
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };

    [Theory]
    [MemberData(nameof(GrayPreservingNames))]
    public void PreservesGrays(string name)
    {
        // Daltonization inherits the 6-decimal rounding of the published Machado tables.
        var m = Get(name);
        foreach (var g in Grays)
        {
            Assert.True(m.Transform(g).MaxDifference(g) < 1e-5, $"{name} changed gray {g}");
        }
    }

    [Fact]
    public void SaturationZeroIsGrayscale()
    {
        var c = ColorAdjustments.Saturation(0).Transform(new Rgb(0.9, 0.2, 0.1));
        Assert.Equal(c.R, c.G, 12);
        Assert.Equal(c.G, c.B, 12);
    }

    [Fact]
    public void SaturationOneIsIdentity() => Assert.True(ColorAdjustments.Saturation(1).IsIdentity(1e-12));

    [Fact]
    public void ContrastPivotsAroundMidGray()
    {
        var m = ColorAdjustments.Contrast(1.5);
        Assert.Equal(0.5, m.Transform(Rgb.Gray(0.5)).R, 12);
        Assert.Equal(0.8, m.Transform(Rgb.Gray(0.7)).R, 12);
    }

    [Fact]
    public void HueRotationZeroIsIdentityAnd360IsIdentity()
    {
        Assert.True(ColorAdjustments.HueRotation(0).IsIdentity(1e-12));
        Assert.True(ColorAdjustments.HueRotation(360).IsIdentity(1e-9));
    }

    [Fact]
    public void PrimaryHueRotationWithEqualAnglesEqualsGlobalHueRotation() =>
        Assert.True(ColorAdjustments.PrimaryHueRotation(20, 20).ApproximatelyEquals(ColorAdjustments.HueRotation(20), 1e-9));

    [Fact]
    public void NegativeRedHueShiftAddsBlueToRed()
    {
        var red = ColorAdjustments.PrimaryHueRotation(-30, 0).Transform(new Rgb(1, 0, 0));
        Assert.True(red.B > 0.1, "red should move toward magenta");
    }

    [Fact]
    public void OpponentTransfer_MovesRedTowardBlueAndGreenAway()
    {
        var m = ColorAdjustments.RedGreenOpponentTransfer(0.8, 0);
        var red = m.Transform(new Rgb(0.8, 0.2, 0.2));
        var green = m.Transform(new Rgb(0.2, 0.8, 0.2));
        Assert.True(red.B > 0.6);
        Assert.True(green.B < 0);
    }

    [Fact]
    public void OpponentTransfer_LightnessMakesRedsLighter()
    {
        var m = ColorAdjustments.RedGreenOpponentTransfer(0, 0.3);
        var red = new Rgb(0.6, 0.2, 0.2);
        Assert.True(Srgb.RelativeLuminance(m.Transform(red)) > Srgb.RelativeLuminance(red));
    }

    [Fact]
    public void Daltonization_SeverityZeroIsIdentity() =>
        Assert.True(Daltonizer.CreateLinearCorrection(CvdType.Deutan, 0).IsIdentity(1e-12));

    [Fact]
    public void Daltonization_RejectsTritan() =>
        Assert.Throws<NotSupportedException>(() => Daltonizer.CreateLinearCorrection(CvdType.Tritan, 1));

    [Fact]
    public void Daltonization_IncreasesSimulatedDifferenceForTargetObserver()
    {
        var red = Rgb.FromHex("#B5402F");
        var green = Rgb.FromHex("#5E8A2E");
        var observer = new ObserverModel(CvdType.Deutan, 0.9);
        var before = DiscriminationEvaluator.PerceivedDifference(red, green, ColorMatrix.Identity, observer);
        var after = DiscriminationEvaluator.PerceivedDifference(red, green, Daltonizer.CreateLinearCorrection(CvdType.Deutan, 0.9), observer);
        Assert.True(after > before);
    }

    [Fact]
    public void EncodedApproximation_IsGrayPreservingAndCloseToExact()
    {
        var fitted = Daltonizer.CreateEncodedApproximation(CvdType.Protan, 0.8);
        foreach (var g in Grays)
        {
            Assert.True(fitted.Transform(g).MaxDifference(g) < 1e-9);
        }

        var rms = MatrixFitting.RmsError(fitted, c => Daltonizer.CorrectExact(c, CvdType.Protan, 0.8));
        Assert.True(rms < 0.15, $"rms {rms}");
    }
}

public class ColorNamingTests
{
    [Theory]
    [InlineData("#FF0000", ColorCategory.Red)]
    [InlineData("#8B2424", ColorCategory.Red)]
    [InlineData("#00FF00", ColorCategory.Green)]
    [InlineData("#727A32", ColorCategory.Green)]
    [InlineData("#228B22", ColorCategory.Green)]
    [InlineData("#0000FF", ColorCategory.Blue)]
    [InlineData("#87CEEB", ColorCategory.Blue)]
    [InlineData("#FFFF00", ColorCategory.Yellow)]
    [InlineData("#FFD700", ColorCategory.Yellow)]
    [InlineData("#FFA500", ColorCategory.Orange)]
    [InlineData("#8B4513", ColorCategory.Brown)]
    [InlineData("#54402F", ColorCategory.Brown)]
    [InlineData("#808080", ColorCategory.Gray)]
    [InlineData("#000000", ColorCategory.Black)]
    [InlineData("#FFFFFF", ColorCategory.White)]
    [InlineData("#800080", ColorCategory.Purple)]
    [InlineData("#FFC0CB", ColorCategory.Pink)]
    [InlineData("#00FFFF", ColorCategory.Cyan)]
    [InlineData("#008080", ColorCategory.Cyan)]
    public void CategorizesCommonColors(string hex, ColorCategory expected) =>
        Assert.Equal(expected, ColorNaming.Categorize(Rgb.FromHex(hex)));

    [Theory]
    [InlineData("#8B2424", "Dark Red")]
    [InlineData("#727A32", "Olive Green")]
    [InlineData("#FF0000", "Red")]
    [InlineData("#000000", "Black")]
    public void NamesExactEntries(string hex, string name) =>
        Assert.Equal(name, ColorNaming.Describe(Rgb.FromHex(hex)).Name);

    [Fact]
    public void NameNeverContradictsCategory()
    {
        // Sample the RGB cube; whenever the name ends in a category word, it must match the category.
        var words = Enum.GetNames<ColorCategory>();
        var problems = new SortedSet<string>();
        for (var r = 0; r <= 255; r += 17)
        {
            for (var g = 0; g <= 255; g += 17)
            {
                for (var b = 0; b <= 255; b += 17)
                {
                    var d = ColorNaming.Describe(Rgb.FromBytes((byte)r, (byte)g, (byte)b));
                    var last = d.Name.Split(' ', '-')[^1];
                    if (words.Contains(last) && last != d.Category.ToString())
                    {
                        problems.Add($"'{d.Name}' vs {d.Category}");
                    }
                }
            }
        }

        Assert.True(problems.Count == 0, string.Join("; ", problems));
    }

    [Fact]
    public void SummaryIsReadable()
    {
        var d = ColorNaming.Describe(Rgb.FromHex("#3B0A0A"));
        Assert.Equal(ColorCategory.Red, d.Category);
        Assert.Contains("dark", d.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("#3B0A0A", d.Hex);
    }
}
