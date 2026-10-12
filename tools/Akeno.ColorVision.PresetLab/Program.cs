using System.Globalization;
using Akeno.ColorVision.ColorScience;
using Akeno.ColorVision.Core.Profiles;

// PresetLab: developer tool that evaluates correction presets with the CVD simulation model.
//
//   dotnet run --project tools/Akeno.ColorVision.PresetLab -- evaluate
//   dotnet run --project tools/Akeno.ColorVision.PresetLab -- search [general|nether]
//   dotnet run --project tools/Akeno.ColorVision.PresetLab -- pairs
//   dotnet run --project tools/Akeno.ColorVision.PresetLab -- try blue=0.7 light=0.15 red=0 green=0 sat=1.1 contrast=1 bright=0 base=None sev=0.8 intensity=1
//
// Results are model-based estimates (see docs/COLOR-SCIENCE.md), not measurements of a real person.

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

var command = args.Length > 0 ? args[0] : "evaluate";
switch (command)
{
    case "evaluate":
        Evaluate();
        break;
    case "search":
        Search(args.Length > 1 ? args[1] : "general");
        break;
    case "pairs":
        Pairs();
        break;
    case "markdown":
        Markdown();
        break;
    case "try":
        Try(args.Skip(1).ToArray());
        break;
    default:
        Console.WriteLine("Unknown command. Use: evaluate | search [general|nether] | pairs | markdown");
        break;
}

static void Evaluate()
{
    var observers = DiscriminationEvaluator.RedGreenObservers;
    Console.WriteLine("Observers: " + string.Join(", ", observers));
    Console.WriteLine();
    Console.WriteLine($"{"Preset",-34} {"General mean",13} {"hard",5} {"Nether mean",12} {"hard",5} {"Distortion",11}");
    foreach (var preset in BuiltInPresets.All)
    {
        var m = preset.BuildMatrix();
        var g = DiscriminationEvaluator.Evaluate(m, TestColorSets.GeneralConfusionPairs, observers);
        var n = DiscriminationEvaluator.Evaluate(m, TestColorSets.NetherConfusionPairs, observers);
        var safe = TransformValidator.Validate(m);
        Console.WriteLine($"{preset.Name,-34} {g.MeanBaseline,6:0.0}→{g.MeanCorrected,5:0.0} {g.HardPairsBaseline,2}→{g.HardPairsCorrected,-2} {n.MeanBaseline,5:0.0}→{n.MeanCorrected,5:0.0} {n.HardPairsBaseline,2}→{n.HardPairsCorrected,-2} {g.MeanDistortionForTypicalVision,8:0.0}  {(safe.IsSafe ? "safe" : "UNSAFE")}");
    }

    Console.WriteLine();
    Console.WriteLine("Daltonization domain comparison (deutan 0.8 / protan 0.8, general pairs):");
    foreach (var type in new[] { CvdType.Deutan, CvdType.Protan })
    {
        var exact = Daltonizer.CreateLinearCorrection(type, 0.8);
        var fitted = Daltonizer.CreateEncodedApproximation(type, 0.8);
        var obs = new[] { new ObserverModel(type, 0.8) };
        var naive = DiscriminationEvaluator.Evaluate(exact, TestColorSets.GeneralConfusionPairs, obs);
        var fit = DiscriminationEvaluator.Evaluate(fitted, TestColorSets.GeneralConfusionPairs, obs);
        var rms = MatrixFitting.RmsError(fitted, c => Daltonizer.CorrectExact(c, type, 0.8));
        var rmsNaive = MatrixFitting.RmsError(exact, c => Daltonizer.CorrectExact(c, type, 0.8));
        Console.WriteLine($"  {type}: naive-in-encoded mean {naive.MeanCorrected:0.0} (rms vs exact {rmsNaive:0.000}); fitted mean {fit.MeanCorrected:0.0} (rms vs exact {rms:0.000}); baseline {fit.MeanBaseline:0.0}");
    }
}

static void Pairs()
{
    var observers = DiscriminationEvaluator.RedGreenObservers;
    foreach (var set in new[] { TestColorSets.CalibrationPairs, TestColorSets.NetherConfusionPairs })
    {
        foreach (var pair in set)
        {
            var typical = DiscriminationEvaluator.PerceivedDifference(pair.First.Color, pair.Second.Color, ColorMatrix.Identity, ObserverModel.Typical);
            var per = observers.Select(o => DiscriminationEvaluator.PerceivedDifference(pair.First.Color, pair.Second.Color, ColorMatrix.Identity, o));
            Console.WriteLine($"{pair.Label,-44} typical {typical,5:0.0} | " + string.Join(" ", per.Select(v => $"{v,5:0.0}")));
        }

        Console.WriteLine();
    }
}

static void Search(string set)
{
    var pairs = set == "nether" ? TestColorSets.NetherConfusionPairs : TestColorSets.GeneralConfusionPairs;
    var observers = DiscriminationEvaluator.RedGreenObservers;
    var results = new List<(double Score, CorrectionParameters P, DiscriminationReport R)>();
    foreach (var baseModel in new[] { BaseCorrection.None, BaseCorrection.DeutanDaltonization, BaseCorrection.ProtanDaltonization })
    {
        foreach (var toBlue in new[] { 0.0, 0.3, 0.5, 0.7, 0.9, 1.1 })
        {
            foreach (var toLight in new[] { -0.3, -0.15, 0.0, 0.15, 0.3, 0.45 })
            {
                foreach (var redHue in new[] { -40.0, -20.0, 0.0, 20.0 })
                {
                    foreach (var greenHue in new[] { -20.0, 0.0, 20.0, 40.0 })
                    {
                        foreach (var sat in new[] { 1.0, 1.25 })
                        {
                            var p = new CorrectionParameters
                            {
                                Base = baseModel,
                                ModelSeverity = 0.8,
                                RedGreenToBlue = toBlue,
                                RedGreenToLightness = toLight,
                                RedHueShift = redHue,
                                GreenHueShift = greenHue,
                                Saturation = sat,
                                Contrast = set == "nether" ? 1.1 : 1.0,
                                Brightness = set == "nether" ? 0.04 : 0.0,
                            };
                            var m = CorrectionPipeline.Build(p, 1.0);
                            if (!TransformValidator.Validate(m).IsSafe)
                            {
                                continue;
                            }

                            var r = DiscriminationEvaluator.Evaluate(m, pairs, observers);
                            // Score: saturating mean (differences above 20 add little) minus a mild naturalness penalty.
                            var score = r.Pairs.Average(x => Math.Min(x.CorrectedDeltaE, 20.0)) - (0.15 * r.MeanDistortionForTypicalVision);
                            results.Add((score, p, r));
                        }
                    }
                }
            }
        }
    }

    foreach (var (score, p, r) in results.OrderByDescending(x => x.Score).Take(15))
    {
        Console.WriteLine($"score {score,6:0.00} mean {r.MeanBaseline:0.0}→{r.MeanCorrected:0.0} min {r.MinBaseline:0.0}→{r.MinCorrected:0.0} hard {r.HardPairsBaseline}→{r.HardPairsCorrected} dist {r.MeanDistortionForTypicalVision:0.0} | {p.Base} blue {p.RedGreenToBlue} light {p.RedGreenToLightness} redHue {p.RedHueShift} greenHue {p.GreenHueShift} sat {p.Saturation}");
    }
}

static void Markdown()
{
    var observers = DiscriminationEvaluator.RedGreenObservers;
    Console.WriteLine("| Preset | General pairs: mean ΔE00 (before → after) | Hard pairs (<8) | Nether-inspired pairs: mean ΔE00 | Hard pairs (<8) | Distortion for typical vision |");
    Console.WriteLine("|---|---|---|---|---|---|");
    foreach (var preset in BuiltInPresets.All)
    {
        var m = preset.BuildMatrix();
        var g = DiscriminationEvaluator.Evaluate(m, TestColorSets.GeneralConfusionPairs, observers);
        var n = DiscriminationEvaluator.Evaluate(m, TestColorSets.NetherConfusionPairs, observers);
        Console.WriteLine($"| {preset.Name} | {g.MeanBaseline:0.0} → {g.MeanCorrected:0.0} | {g.HardPairsBaseline} → {g.HardPairsCorrected} of {g.Pairs.Count} | {n.MeanBaseline:0.0} → {n.MeanCorrected:0.0} | {n.HardPairsBaseline} → {n.HardPairsCorrected} of {n.Pairs.Count} | {g.MeanDistortionForTypicalVision:0.0} |");
    }
}

static void Try(string[] options)
{
    var kv = options.Select(o => o.Split('=', 2)).Where(p => p.Length == 2).ToDictionary(p => p[0], p => p[1]);
    double D(string key, double fallback) => kv.TryGetValue(key, out var v) ? double.Parse(v, CultureInfo.InvariantCulture) : fallback;
    var p = new CorrectionParameters
    {
        Base = kv.TryGetValue("base", out var b) ? Enum.Parse<BaseCorrection>(b, ignoreCase: true) : BaseCorrection.None,
        ModelSeverity = D("sev", 0.8),
        RedGreenToBlue = D("blue", 0),
        RedGreenToLightness = D("light", 0),
        RedHueShift = D("red", 0),
        GreenHueShift = D("green", 0),
        Saturation = D("sat", 1),
        Contrast = D("contrast", 1),
        Brightness = D("bright", 0),
    };
    var m = CorrectionPipeline.Build(p, D("intensity", 1));
    var observers = DiscriminationEvaluator.RedGreenObservers;
    var g = DiscriminationEvaluator.Evaluate(m, TestColorSets.GeneralConfusionPairs, observers);
    var n = DiscriminationEvaluator.Evaluate(m, TestColorSets.NetherConfusionPairs, observers);
    Console.WriteLine($"general {g.MeanBaseline:0.0}->{g.MeanCorrected:0.0} min {g.MinCorrected:0.0} hard {g.HardPairsBaseline}->{g.HardPairsCorrected} | nether {n.MeanBaseline:0.0}->{n.MeanCorrected:0.0} min {n.MinCorrected:0.0} hard {n.HardPairsBaseline}->{n.HardPairsCorrected} | distortion {g.MeanDistortionForTypicalVision:0.0} | {(TransformValidator.Validate(m).IsSafe ? "safe" : "UNSAFE")}");
    if (kv.ContainsKey("verbose"))
    {
        foreach (var r in n.Pairs.Concat(g.Pairs))
        {
            Console.WriteLine($"   {r.Pair.Label,-44} {r.BaselineDeltaE,5:0.0} -> {r.CorrectedDeltaE,5:0.0}");
        }
    }
}
