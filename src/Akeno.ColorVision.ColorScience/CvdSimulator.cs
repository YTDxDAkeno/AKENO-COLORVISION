namespace Akeno.ColorVision.ColorScience;

/// <summary>
/// A simulated observer used to <b>evaluate</b> corrections. Severity 0 represents typical
/// trichromatic vision regardless of <see cref="Type"/>.
/// </summary>
public readonly record struct ObserverModel(CvdType Type, double Severity)
{
    public static ObserverModel Typical => new(CvdType.Deutan, 0);

    public bool IsTypical => Severity <= 0;

    public override string ToString() =>
        IsTypical ? "typical" : FormattableString.Invariant($"{Type.ToString().ToLowerInvariant()} {Severity:0.0#}");
}

/// <summary>
/// Simulates how a gamma-encoded sRGB color may appear to an observer with a CVD, using
/// <see cref="MachadoCvdModel"/> in linear RGB (decode → simulate → clamp → encode).
/// </summary>
public static class CvdSimulator
{
    public static Rgb Simulate(Rgb encoded, ObserverModel observer) =>
        observer.IsTypical ? encoded.Clamp01() : Simulate(encoded, observer.Type, observer.Severity);

    public static Rgb Simulate(Rgb encoded, CvdType type, double severity)
    {
        var matrix = MachadoCvdModel.GetSimulationMatrix(type, severity);
        var linear = Srgb.ToLinear(encoded.Clamp01());
        var simulated = matrix.Transform(linear).Clamp01();
        return Srgb.FromLinear(simulated);
    }
}
