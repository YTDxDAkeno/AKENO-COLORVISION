using Akeno.ColorVision.ColorScience;

namespace Akeno.ColorVision.Core.Profiles;

/// <summary>
/// The built-in presets. Parameter values were chosen with the PresetLab tool
/// (tools/Akeno.ColorVision.PresetLab), which scores candidates by simulated color differences for
/// protan and deutan observers of several severities; see docs/COLOR-SCIENCE.md for the numbers.
/// They are starting points: calibration and the strength slider personalise them.
/// </summary>
public static class BuiltInPresets
{
    public static readonly Guid RedGreenId = new("6a1f0c52-8d1e-4b7a-9c35-0d6f7e1a2b01");
    public static readonly Guid ProtanId = new("6a1f0c52-8d1e-4b7a-9c35-0d6f7e1a2b02");
    public static readonly Guid DeutanId = new("6a1f0c52-8d1e-4b7a-9c35-0d6f7e1a2b03");
    public static readonly Guid StrongId = new("6a1f0c52-8d1e-4b7a-9c35-0d6f7e1a2b04");
    public static readonly Guid BrightnessContrastId = new("6a1f0c52-8d1e-4b7a-9c35-0d6f7e1a2b05");
    public static readonly Guid MinecraftNetherId = new("6a1f0c52-8d1e-4b7a-9c35-0d6f7e1a2b06");

    private static readonly DateTimeOffset Epoch = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);

    public static CorrectionParameters RedGreenParameters { get; } = new()
    {
        RedGreenToBlue = 0.8,
        RedGreenToLightness = 0.15,
        Saturation = 1.1,
    };

    public static CorrectionParameters ProtanParameters { get; } = new()
    {
        Base = BaseCorrection.ProtanDaltonization,
        ModelSeverity = 0.8,
        RedGreenToBlue = 0.3,
        RedGreenToLightness = 0.15,
        Saturation = 1.05,
    };

    public static CorrectionParameters DeutanParameters { get; } = new()
    {
        Base = BaseCorrection.DeutanDaltonization,
        ModelSeverity = 0.8,
        RedGreenToBlue = 0.4,
        Saturation = 1.05,
    };

    public static CorrectionParameters StrongParameters { get; } = new()
    {
        RedGreenToBlue = 1.1,
        RedGreenToLightness = 0.3,
        Saturation = 1.25,
        Contrast = 1.05,
    };

    public static CorrectionParameters BrightnessContrastParameters { get; } = new()
    {
        Saturation = 1.35,
        Contrast = 1.2,
        Brightness = 0.02,
    };

    public static CorrectionParameters MinecraftNetherParameters { get; } = new()
    {
        RedGreenToBlue = 0.6,
        RedGreenToLightness = -0.25,
        RedHueShift = 10,
        Saturation = 1.45,
        Contrast = 1.25,
        Brightness = 0.07,
    };

    public static ColorProfile RedGreen => Create(RedGreenId, "Red-Green Assistance", CorrectionMode.RedGreenAssistance, 0.7, RedGreenParameters,
        "Balanced everyday correction. Moves the red/green difference onto blue/yellow and lightness, which most people with red-green color vision differences can still see.");

    public static ColorProfile Protan => Create(ProtanId, "Protan Assistance", CorrectionMode.ProtanAssistance, 0.8, ProtanParameters,
        "Daltonization designed with a protan (red-cone) vision model. Try it if reds tend to look dark or muddy.");

    public static ColorProfile Deutan => Create(DeutanId, "Deutan Assistance", CorrectionMode.DeutanAssistance, 0.8, DeutanParameters,
        "Daltonization designed with a deutan (green-cone) vision model. Try it if reds and greens both look brownish or yellowish.");

    public static ColorProfile Strong => Create(StrongId, "Strong Separation", CorrectionMode.StrongSeparation, 1.0, StrongParameters,
        "Aggressive mode for games and charts. Colors look unnatural on purpose so that differences are easy to spot.");

    public static ColorProfile BrightnessContrast => Create(BrightnessContrastId, "Brightness & Contrast", CorrectionMode.BrightnessContrast, 0.0, BrightnessContrastParameters,
        "No color remapping; only more saturation and contrast. Helps when colors look muted, but does not separate red from green by itself.");

    public static ColorProfile MinecraftNether => Create(MinecraftNetherId, "Minecraft — Nether Visibility", CorrectionMode.MinecraftNether, 1.0, MinecraftNetherParameters,
        "Tuned for dark reds, browns and grays like those in Minecraft's Nether. Brighter and strongly separated; not meant to look natural.");

    /// <summary>All built-in presets in display order.</summary>
    public static IReadOnlyList<ColorProfile> All => [RedGreen, Protan, Deutan, Strong, BrightnessContrast, MinecraftNether];

    public static bool IsBuiltInId(Guid id) => All.Any(p => p.Id == id);

    public static ColorProfile? Find(Guid id) => All.FirstOrDefault(p => p.Id == id);

    /// <summary>Default parameters for a mode (used by legacy-profile migration and "reset to preset").</summary>
    public static CorrectionParameters ParametersFor(CorrectionMode mode) => mode switch
    {
        CorrectionMode.RedGreenAssistance => RedGreenParameters,
        CorrectionMode.ProtanAssistance => ProtanParameters,
        CorrectionMode.DeutanAssistance => DeutanParameters,
        CorrectionMode.StrongSeparation => StrongParameters,
        CorrectionMode.BrightnessContrast => BrightnessContrastParameters,
        CorrectionMode.MinecraftNether => MinecraftNetherParameters,
        _ => RedGreenParameters,
    };

    private static ColorProfile Create(Guid id, string name, CorrectionMode mode, double intensity, CorrectionParameters parameters, string description) => new()
    {
        Id = id,
        Name = name,
        Mode = mode,
        Intensity = intensity,
        Parameters = parameters,
        Description = description,
        Origin = ProfileOrigin.BuiltIn,
        CreatedUtc = Epoch,
        ModifiedUtc = Epoch,
    };
}
