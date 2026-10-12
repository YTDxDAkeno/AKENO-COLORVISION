using Akeno.ColorVision.ColorScience;
using Akeno.ColorVision.Core.Profiles;

namespace Akeno.ColorVision.UnitTests;

public sealed class TempDir : IDisposable
{
    public TempDir() => System.IO.Directory.CreateDirectory(Path);

    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "akeno-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            System.IO.Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}

public class ProfileSerializerTests
{
    [Fact]
    public void RoundTripsAllFields()
    {
        var profile = new ColorProfile
        {
            Name = "Test",
            Mode = CorrectionMode.DeutanAssistance,
            Intensity = 0.65,
            Parameters = new CorrectionParameters { Base = BaseCorrection.DeutanDaltonization, ModelSeverity = 0.7, RedGreenToBlue = 0.4, Saturation = 1.2 },
            Calibration = new CalibrationRecord { SelfDescription = "Red and green look similar", PairRatings = { ["red-green"] = 3 } },
        };
        var json = ProfileSerializer.Serialize(profile);
        Assert.Contains("\"schemaVersion\": 2", json);
        Assert.Contains("\"mode\": \"DeutanAssistance\"", json);

        var back = ProfileSerializer.Deserialize(json);
        Assert.True(back.Success, back.Error);
        Assert.False(back.WasMigrated);
        var p = back.Profile!;
        Assert.Equal(profile.Id, p.Id);
        Assert.Equal("Test", p.Name);
        Assert.Equal(0.65, p.Intensity);
        Assert.Equal(profile.Parameters, p.Parameters);
        Assert.Equal(3, p.Calibration!.PairRatings["red-green"]);
        Assert.True(p.BuildMatrix().ApproximatelyEquals(profile.BuildMatrix(), 1e-12));
    }

    [Fact]
    public void MigratesLegacyFormatFromTheSpecification()
    {
        const string legacy = """
        {
          "name": "My ColorVision Profile",
          "version": 1,
          "mode": "custom",
          "intensity": 0.7,
          "contrast": 1.0,
          "saturation": 1.0,
          "backend": "auto"
        }
        """;
        var result = ProfileSerializer.Deserialize(legacy);
        Assert.True(result.Success, result.Error);
        Assert.True(result.WasMigrated);
        var p = result.Profile!;
        Assert.Equal(ColorProfile.CurrentSchemaVersion, p.SchemaVersion);
        Assert.Equal("My ColorVision Profile", p.Name);
        Assert.Equal(0.7, p.Intensity);
        Assert.Equal(1.0, p.Parameters.Contrast);
        Assert.Equal(1.0, p.Parameters.Saturation);
        Assert.Equal("auto", p.Backend);
        Assert.NotEqual(Guid.Empty, p.Id);
    }

    [Theory]
    [InlineData("protan", CorrectionMode.ProtanAssistance)]
    [InlineData("Deutan", CorrectionMode.DeutanAssistance)]
    [InlineData("strong-separation", CorrectionMode.StrongSeparation)]
    [InlineData("minecraft", CorrectionMode.MinecraftNether)]
    public void LegacyModesMapToPresets(string mode, CorrectionMode expected)
    {
        var result = ProfileSerializer.Deserialize($$"""{"name":"x","version":1,"mode":"{{mode}}","intensity":0.5}""");
        Assert.Equal(expected, result.Profile!.Mode);
        Assert.Equal(BuiltInPresets.ParametersFor(expected), result.Profile.Parameters);
    }

    [Fact]
    public void RejectsNewerSchema()
    {
        var result = ProfileSerializer.Deserialize("""{"schemaVersion": 99, "name": "future"}""");
        Assert.False(result.Success);
        Assert.Contains("newer version", result.Error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[1,2,3]")]
    [InlineData("{\"hello\": 1}")]
    [InlineData("{\"schemaVersion\": \"two\"}")]
    [InlineData("{\"schemaVersion\": 2, \"mode\": \"NoSuchMode\"}")]
    public void InvalidInputFailsGracefully(string json)
    {
        var result = ProfileSerializer.Deserialize(json);
        Assert.False(result.Success);
        Assert.False(string.IsNullOrEmpty(result.Error));
    }

    [Fact]
    public void ClampsOutOfRangeValues()
    {
        const string json = """
        {
          "schemaVersion": 2,
          "id": "00000000-0000-0000-0000-000000000000",
          "name": "   ",
          "intensity": 7,
          "parameters": { "saturation": 50, "contrast": -3, "brightness": 2, "redGreenToBlue": "NaN", "redHueShift": 1000 }
        }
        """;
        var result = ProfileSerializer.Deserialize(json);
        Assert.True(result.Success, result.Error);
        var p = result.Profile!;
        Assert.NotEqual(Guid.Empty, p.Id);
        Assert.Equal("Unnamed profile", p.Name);
        Assert.Equal(1.0, p.Intensity);
        Assert.Equal(ParameterRanges.SaturationMax, p.Parameters.Saturation);
        Assert.Equal(ParameterRanges.ContrastMin, p.Parameters.Contrast);
        Assert.Equal(ParameterRanges.BrightnessMax, p.Parameters.Brightness);
        Assert.Equal(0, p.Parameters.RedGreenToBlue);
        Assert.Equal(ParameterRanges.HueShiftMax, p.Parameters.RedHueShift);
        Assert.NotEmpty(result.Warnings);
        Assert.True(TransformValidator.Validate(p.BuildMatrix()).IsSafe);
    }

    [Fact]
    public void FilesNeverBecomeBuiltIn()
    {
        var json = ProfileSerializer.Serialize(new ColorProfile { Origin = ProfileOrigin.BuiltIn });
        Assert.Equal(ProfileOrigin.User, ProfileSerializer.Deserialize(json).Profile!.Origin);
    }

    [Fact]
    public void LongNamesAreShortened()
    {
        var json = ProfileSerializer.Serialize(new ColorProfile { Name = new string('x', 500) });
        Assert.Equal(ColorProfile.MaxNameLength, ProfileSerializer.Deserialize(json).Profile!.Name.Length);
    }
}

public class ProfileStoreTests
{
    [Fact]
    public void SavesAndLoads()
    {
        using var dir = new TempDir();
        var store = new ProfileStore(dir.Path);
        var p = new ColorProfile { Name = "Mine", Intensity = 0.4 };
        store.Save(p);
        var loaded = store.LoadAll(out var issues);
        Assert.Empty(issues);
        Assert.Single(loaded);
        Assert.Equal("Mine", loaded[0].Name);
        Assert.True(File.Exists(store.PathFor(p.Id)));
    }

    [Fact]
    public void MissingDirectoryLoadsNothing()
    {
        var store = new ProfileStore(Path.Combine(Path.GetTempPath(), "does-not-exist-" + Guid.NewGuid()));
        Assert.Empty(store.LoadAll(out var issues));
        Assert.Empty(issues);
    }

    [Fact]
    public void CorruptFilesAreSetAsideNotDeleted()
    {
        using var dir = new TempDir();
        var store = new ProfileStore(dir.Path);
        store.Save(new ColorProfile { Name = "Good" });
        File.WriteAllText(Path.Combine(dir.Path, "broken.json"), "{ this is not json");

        var loaded = store.LoadAll(out var issues);
        Assert.Single(loaded);
        Assert.Single(issues);
        Assert.False(File.Exists(Path.Combine(dir.Path, "broken.json")));
        Assert.Single(Directory.GetFiles(dir.Path, "broken.json.corrupt-*"));
    }

    [Fact]
    public void MigratesLegacyFileInPlaceKeepingBackup()
    {
        using var dir = new TempDir();
        File.WriteAllText(Path.Combine(dir.Path, "old.json"), """{"name":"Old","version":1,"mode":"custom","intensity":0.5}""");
        var store = new ProfileStore(dir.Path);
        var loaded = store.LoadAll(out _);
        Assert.Single(loaded);
        Assert.True(File.Exists(store.PathFor(loaded[0].Id)));
        Assert.True(File.Exists(Path.Combine(dir.Path, "old.json.v1-backup")));
        Assert.False(File.Exists(Path.Combine(dir.Path, "old.json")));

        // Loading again does not duplicate anything.
        Assert.Single(store.LoadAll(out _));
    }

    [Fact]
    public void DuplicateIdsAreRepaired()
    {
        using var dir = new TempDir();
        var p = new ColorProfile { Name = "A" };
        var json = ProfileSerializer.Serialize(p);
        File.WriteAllText(Path.Combine(dir.Path, "a.json"), json);
        File.WriteAllText(Path.Combine(dir.Path, "b.json"), json);
        var store = new ProfileStore(dir.Path);
        var loaded = store.LoadAll(out var issues);
        Assert.Equal(2, loaded.Count);
        Assert.NotEqual(loaded[0].Id, loaded[1].Id);
        Assert.Contains(issues, i => i.Message.Contains("Duplicate", StringComparison.Ordinal));
        Assert.Equal(2, store.LoadAll(out _).Count);
    }

    [Fact]
    public void BuiltInProfilesCannotBeSaved()
    {
        using var dir = new TempDir();
        Assert.Throws<InvalidOperationException>(() => new ProfileStore(dir.Path).Save(BuiltInPresets.RedGreen));
    }

    [Fact]
    public void ImportAssignsFreshIdAndOrigin()
    {
        using var dir = new TempDir();
        var original = new ColorProfile { Name = "Shared" };
        var file = Path.Combine(dir.Path, "shared.json");
        ProfileStore.Export(original, file);
        var imported = ProfileStore.Import(file);
        Assert.True(imported.Success);
        Assert.NotEqual(original.Id, imported.Profile!.Id);
        Assert.Equal(ProfileOrigin.Imported, imported.Profile.Origin);
    }

    [Fact]
    public void ImportRejectsHugeFiles()
    {
        using var dir = new TempDir();
        var file = Path.Combine(dir.Path, "huge.json");
        File.WriteAllText(file, new string(' ', 300 * 1024));
        Assert.False(ProfileStore.Import(file).Success);
    }

    [Fact]
    public void DeleteRemovesFile()
    {
        using var dir = new TempDir();
        var store = new ProfileStore(dir.Path);
        var p = new ColorProfile();
        store.Save(p);
        Assert.True(store.Delete(p.Id));
        Assert.False(store.Delete(p.Id));
    }
}

public class BuiltInPresetTests
{
    [Fact]
    public void IdsAreUniqueAndStable()
    {
        var ids = BuiltInPresets.All.Select(p => p.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.Equal(new Guid("6a1f0c52-8d1e-4b7a-9c35-0d6f7e1a2b01"), BuiltInPresets.RedGreenId);
    }

    [Fact]
    public void AllPresetsAreSafe()
    {
        foreach (var preset in BuiltInPresets.All)
        {
            var v = TransformValidator.Validate(preset.BuildMatrix());
            Assert.True(v.IsSafe, $"{preset.Name}: {string.Join(" ", v.Errors)}");
            Assert.True(TransformValidator.Validate(preset.BuildMatrix(1.0)).IsSafe, $"{preset.Name} at full strength");
        }
    }

    [Fact]
    public void AllPresetsKeepGraysNeutral()
    {
        foreach (var preset in BuiltInPresets.All)
        {
            var m = preset.BuildMatrix();
            foreach (var v in new[] { 0.3, 0.5, 0.7 })
            {
                var c = m.TransformClamped(Rgb.Gray(v));
                Assert.True(Math.Abs(c.R - c.G) < 1e-5 && Math.Abs(c.G - c.B) < 1e-5, $"{preset.Name} tints gray {v}");
            }
        }
    }

    [Theory]
    [InlineData(CorrectionMode.RedGreenAssistance)]
    [InlineData(CorrectionMode.ProtanAssistance)]
    [InlineData(CorrectionMode.DeutanAssistance)]
    [InlineData(CorrectionMode.StrongSeparation)]
    [InlineData(CorrectionMode.MinecraftNether)]
    public void DiscriminationPresetsIncreaseSimulatedDifferences(CorrectionMode mode)
    {
        var preset = BuiltInPresets.All.Single(p => p.Mode == mode);
        var report = DiscriminationEvaluator.Evaluate(preset.BuildMatrix(), TestColorSets.GeneralConfusionPairs, DiscriminationEvaluator.RedGreenObservers);
        Assert.True(report.MeanCorrected > report.MeanBaseline * 1.3, $"{preset.Name}: {report.MeanBaseline:0.0} -> {report.MeanCorrected:0.0}");
        Assert.True(report.HardPairsCorrected < report.HardPairsBaseline, $"{preset.Name}: hard pairs {report.HardPairsBaseline} -> {report.HardPairsCorrected}");
    }

    [Fact]
    public void StrongSeparationLeavesNoHardGeneralPairs()
    {
        var report = DiscriminationEvaluator.Evaluate(BuiltInPresets.Strong.BuildMatrix(), TestColorSets.GeneralConfusionPairs, DiscriminationEvaluator.RedGreenObservers);
        Assert.Equal(0, report.HardPairsCorrected);
    }

    [Fact]
    public void MinecraftPresetImprovesNetherPairsMoreThanEverydayPreset()
    {
        var observers = DiscriminationEvaluator.RedGreenObservers;
        var nether = DiscriminationEvaluator.Evaluate(BuiltInPresets.MinecraftNether.BuildMatrix(), TestColorSets.NetherConfusionPairs, observers);
        var everyday = DiscriminationEvaluator.Evaluate(BuiltInPresets.RedGreen.BuildMatrix(), TestColorSets.NetherConfusionPairs, observers);
        Assert.True(nether.MeanCorrected > nether.MeanBaseline * 1.4);
        Assert.True(nether.MeanCorrected > everyday.MeanCorrected);
        Assert.True(nether.HardPairsCorrected < nether.HardPairsBaseline);
    }

    [Fact]
    public void EverydayPresetStaysReasonablyNatural()
    {
        var distortion = DiscriminationEvaluator.MeanDistortion(BuiltInPresets.RedGreen.BuildMatrix());
        Assert.InRange(distortion, 1, 12);
    }

    [Fact]
    public void ZeroIntensityRemovesColorRemappingButKeepsToneSettings()
    {
        // Intensity scales only the discrimination part; saturation/contrast/brightness stay as set.
        var preset = BuiltInPresets.RedGreen;
        var expected = ColorAdjustments.Saturation(preset.Parameters.Saturation);
        Assert.True(preset.BuildMatrix(0).ApproximatelyEquals(expected, 1e-9));
    }
}

public class TransformValidatorTests
{
    [Fact]
    public void IdentityIsSafe() => Assert.True(TransformValidator.Validate(ColorMatrix.Identity).IsSafe);

    [Fact]
    public void AllBlackIsRejected() => Assert.False(TransformValidator.Validate(ColorMatrix.Scale(0, 0, 0)).IsSafe);

    [Fact]
    public void WashedOutIsRejected() =>
        Assert.False(TransformValidator.Validate(ColorAdjustments.Contrast(0.1)).IsSafe);

    [Fact]
    public void NonFiniteIsRejected() =>
        Assert.False(TransformValidator.Validate(ColorMatrix.FromRowMajor([double.PositiveInfinity, 0, 0, 0, 1, 0, 0, 0, 1])).IsSafe);

    [Fact]
    public void HugeCoefficientIsRejected() =>
        Assert.False(TransformValidator.Validate(ColorMatrix.FromRowMajor([9, -8, 0, 0, 1, 0, 0, 0, 1])).IsSafe);

    [Fact]
    public void LargeOffsetIsRejected() =>
        Assert.False(TransformValidator.Validate(ColorMatrix.Offset(0.9, 0.9, 0.9)).IsSafe);

    [Fact]
    public void GrayscaleIsRejectedBecauseItMergesColors() =>
        Assert.False(TransformValidator.Validate(ColorAdjustments.Saturation(0)).IsSafe);

    [Fact]
    public void ChannelSwapIsAllowed() =>
        Assert.True(TransformValidator.Validate(ColorMatrix.FromRowMajor([0, 0, 1, 0, 1, 0, 1, 0, 0])).IsSafe);

    [Fact]
    public void TintProducesWarning()
    {
        var v = TransformValidator.Validate(ColorMatrix.Offset(0.2, 0, 0));
        Assert.True(v.IsSafe);
        Assert.NotEmpty(v.Warnings);
    }
}
