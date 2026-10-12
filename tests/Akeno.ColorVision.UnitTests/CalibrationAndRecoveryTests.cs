using System.Text.Json;
using Akeno.ColorVision.ColorScience;
using Akeno.ColorVision.Core.Calibration;
using Akeno.ColorVision.Core.Diagnostics;
using Akeno.ColorVision.Core.Profiles;
using Akeno.ColorVision.Core.Recovery;
using Akeno.ColorVision.Shared;

namespace Akeno.ColorVision.UnitTests;

public class CalibrationAnalyzerTests
{
    /// <summary>Answers a simulated observer would give, using the same rating thresholds as the analyzer.</summary>
    private static Dictionary<string, PairRating> AnswersFor(ObserverModel observer) =>
        TestColorSets.CalibrationPairs.ToDictionary(
            p => p.Id,
            p => (PairRating)CalibrationAnalyzer.PredictRating(
                DiscriminationEvaluator.PerceivedDifference(p.First.Color, p.Second.Color, ColorMatrix.Identity, observer)));

    [Fact]
    public void DeutanLikeAnswersPointToDeutanStartingPoint()
    {
        var insight = CalibrationAnalyzer.Analyze(AnswersFor(new ObserverModel(CvdType.Deutan, 1.0)));
        Assert.NotEqual(CvdType.Protan, insight.CloserModel);
        Assert.NotEmpty(insight.DifficultPairs);
        Assert.False(insight.AnswersLookTypical);
        Assert.Contains("not a medical test", insight.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProtanLikeAnswersPointToProtanStartingPoint()
    {
        var insight = CalibrationAnalyzer.Analyze(AnswersFor(new ObserverModel(CvdType.Protan, 1.0)));
        Assert.NotEqual(CvdType.Deutan, insight.CloserModel);
        Assert.NotEmpty(insight.DifficultPairs);
    }

    [Fact]
    public void TypicalAnswersSuggestGentleCorrection()
    {
        var ratings = TestColorSets.CalibrationPairs.ToDictionary(p => p.Id, _ => PairRating.ClearlyDifferent);
        var insight = CalibrationAnalyzer.Analyze(ratings);
        Assert.Empty(insight.DifficultPairs);
        Assert.True(insight.SuggestedIntensity <= 0.5);
    }

    [Fact]
    public void ControlPairsRatedSimilarAreFlagged()
    {
        var ratings = AnswersFor(new ObserverModel(CvdType.Deutan, 0.8));
        ratings["control-blue-yellow"] = PairRating.Identical;
        var insight = CalibrationAnalyzer.Analyze(ratings);
        Assert.True(insight.ControlPairsLookedSimilar);
        Assert.Contains("screen settings", insight.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TooFewAnswersFallBackToBalancedStart()
    {
        var insight = CalibrationAnalyzer.Analyze(new Dictionary<string, PairRating> { ["red-green"] = PairRating.Identical });
        Assert.Null(insight.CloserModel);
        Assert.Equal(CorrectionMode.RedGreenAssistance, insight.SuggestedMode);
    }

    [Fact]
    public void SummaryNeverClaimsADiagnosis()
    {
        foreach (var observer in new[] { new ObserverModel(CvdType.Protan, 1), new ObserverModel(CvdType.Deutan, 0.6), ObserverModel.Typical })
        {
            var summary = CalibrationAnalyzer.Analyze(AnswersFor(observer)).Summary.ToLowerInvariant();
            Assert.DoesNotContain("you have", summary);
            Assert.DoesNotContain("you are color blind", summary);
            Assert.Contains("not a medical test or diagnosis", summary);
        }
    }

    [Fact]
    public void CannotDistinguishObjectsStartsWithStrongSeparation()
    {
        var insight = CalibrationAnalyzer.Analyze(AnswersFor(new ObserverModel(CvdType.Deutan, 0.8)), SelfDescription.CannotDistinguishObjects);
        Assert.Equal(CorrectionMode.StrongSeparation, insight.SuggestedMode);
    }
}

public class CalibrationCandidateTests
{
    private static CalibrationInsight Insight(CorrectionMode mode) =>
        new(CvdType.Deutan, 0.8, FitConfidence.Medium, mode, 0.8, [], false, false, "summary");

    [Fact]
    public void StrategyRoundPutsSuggestionFirstAndAllAreSafe()
    {
        var candidates = CalibrationCandidates.StrategyRound(Insight(CorrectionMode.DeutanAssistance));
        Assert.Equal(4, candidates.Count);
        Assert.Equal("deutan", candidates[0].Id);
        Assert.All(candidates, c => Assert.True(TransformValidator.Validate(c.BuildMatrix()).IsSafe));
    }

    [Fact]
    public void StrengthAndRefineRoundsVaryIntensityOnly()
    {
        var chosen = CalibrationCandidates.StrategyRound(Insight(CorrectionMode.RedGreenAssistance))[0];
        var strengths = CalibrationCandidates.StrengthRound(chosen);
        Assert.Equal([0.4, 0.7, 1.0], strengths.Select(c => c.Intensity));
        Assert.All(strengths, c => Assert.Equal(chosen.Parameters, c.Parameters));

        var refine = CalibrationCandidates.RefineRound(strengths[1]);
        Assert.Equal([0.55, 0.7, 0.85], refine.Select(c => c.Intensity));
        var top = CalibrationCandidates.RefineRound(strengths[2]);
        Assert.Equal([0.85, 1.0], top.Select(c => c.Intensity));
    }

    [Fact]
    public void CreatedProfileStoresAnswersButNoDiagnosis()
    {
        var chosen = CalibrationCandidates.StrategyRound(Insight(CorrectionMode.DeutanAssistance))[0];
        var record = new CalibrationRecord
        {
            SelfDescription = "Many red and green shades look gray or brown",
            PairRatings = { ["red-green"] = 3 },
            ChosenOption = chosen.Label,
        };
        var profile = CalibrationCandidates.CreateProfile("  Anna's colors  ", chosen, record);
        Assert.Equal("Anna's colors", profile.Name);
        Assert.Equal(ProfileOrigin.Calibration, profile.Origin);
        Assert.Equal(chosen.Intensity, profile.Intensity);

        var json = ProfileSerializer.Serialize(profile).ToLowerInvariant();
        Assert.DoesNotContain("diagnos", json);
        Assert.DoesNotContain("protanopia", json);
        Assert.DoesNotContain("deuteranopia", json);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(3, doc.RootElement.GetProperty("calibration").GetProperty("pairratings").GetProperty("red-green").GetInt32());
    }
}

public class SessionGuardTests
{
    [Fact]
    public void CleanStartIsNotSafeMode()
    {
        using var dir = new TempDir();
        var start = new SessionGuard(dir.Path).Begin(123, safeModeRequested: false);
        Assert.False(start.SafeMode);
        Assert.True(File.Exists(Path.Combine(dir.Path, RecoveryProtocol.SessionLockFile)));
    }

    [Fact]
    public void LeftoverLockMeansUncleanShutdown()
    {
        using var dir = new TempDir();
        new SessionGuard(dir.Path).Begin(1, false);   // previous run never called MarkCleanExit
        var start = new SessionGuard(dir.Path).Begin(2, false);
        Assert.True(start.SafeMode);
        Assert.True(start.Reasons.HasFlag(SafeModeReason.UncleanShutdown));
    }

    [Fact]
    public void CleanExitClearsLock()
    {
        using var dir = new TempDir();
        var guard = new SessionGuard(dir.Path);
        guard.Begin(1, false);
        guard.MarkCleanExit();
        Assert.False(new SessionGuard(dir.Path).Begin(2, false).SafeMode);
    }

    [Fact]
    public void EmergencyFlagIsConsumedOnce()
    {
        using var dir = new TempDir();
        File.WriteAllText(Path.Combine(dir.Path, RecoveryProtocol.SafeModeFlagFile), "x");
        File.WriteAllText(Path.Combine(dir.Path, RecoveryProtocol.LastRecoveryFile), "Emergency reset tool used.");
        var guard = new SessionGuard(dir.Path);
        var first = guard.Begin(1, false);
        Assert.True(first.Reasons.HasFlag(SafeModeReason.EmergencyResetUsed));
        Assert.Equal("Emergency reset tool used.", first.LastRecoveryNote);
        guard.MarkCleanExit();
        Assert.False(new SessionGuard(dir.Path).Begin(2, false).SafeMode);
    }

    [Fact]
    public void RequestedSafeMode()
    {
        using var dir = new TempDir();
        var start = new SessionGuard(dir.Path).Begin(1, safeModeRequested: true);
        Assert.Equal(SafeModeReason.Requested, start.Reasons);
    }

    [Fact]
    public void PortableModeUsesExecutableFolder()
    {
        using var dir = new TempDir();
        File.WriteAllText(Path.Combine(dir.Path, RecoveryProtocol.PortableMarkerFile), string.Empty);
        Assert.Equal(Path.Combine(dir.Path, RecoveryProtocol.PortableDataFolderName), RecoveryProtocol.GetDataDirectory(dir.Path));
    }
}

public class DiagnosticsTests
{
    [Fact]
    public void SanitizerRemovesUserProfilePath()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(profile) || profile.Length <= 3)
        {
            return; // no meaningful profile path on this machine
        }

        var text = PrivacySanitizer.Sanitize($"Failed to open {profile}{Path.DirectorySeparatorChar}secret.txt");
        Assert.DoesNotContain(profile, text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("%USERPROFILE%", text);
    }

    [Fact]
    public void ReportContainsSectionsAndPrivacyNote()
    {
        var report = DiagnosticsReport.Build("0.1.0", [new DiagnosticsSection("Color filter", [new("State", "On")])], ["log line"]);
        Assert.Contains("== Color filter ==", report);
        Assert.Contains("State: On", report);
        Assert.Contains("no screenshots", report);
        Assert.Contains("log line", report);
    }

    [Fact]
    public void FileLoggerWritesAndHonoursToggle()
    {
        using var dir = new TempDir();
        var log = new FileLogger(dir.Path);
        log.Info("hello");
        log.Enabled = false;
        log.Info("hidden");
        var text = string.Concat(Directory.GetFiles(dir.Path).Select(File.ReadAllText));
        Assert.Contains("hello", text);
        Assert.DoesNotContain("hidden", text);
    }

    [Fact]
    public void MemoryLoggerKeepsRecentEntries()
    {
        var log = new MemoryLogger(capacity: 3);
        for (var i = 0; i < 10; i++)
        {
            log.Info($"entry {i}");
        }

        var snapshot = log.Snapshot();
        Assert.Equal(3, snapshot.Count);
        Assert.Contains("entry 9", snapshot[^1]);
    }
}

public class AtomicFileTests
{
    [Fact]
    public void WritesAndReplaces()
    {
        using var dir = new Akeno.ColorVision.UnitTests.TempDir();
        var path = Path.Combine(dir.Path, "sub", "file.json");
        Core.AtomicFile.WriteAllText(path, "one");
        Core.AtomicFile.WriteAllText(path, "two");
        Assert.Equal("two", File.ReadAllText(path));
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(path)!));
    }
}
