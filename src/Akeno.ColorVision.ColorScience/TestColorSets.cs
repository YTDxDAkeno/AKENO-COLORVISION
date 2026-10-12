namespace Akeno.ColorVision.ColorScience;

/// <summary>A labelled color.</summary>
public sealed record NamedSwatch(string Name, Rgb Color)
{
    public NamedSwatch(string name, string hex)
        : this(name, Rgb.FromHex(hex))
    {
    }
}

/// <summary>Two colors that may be hard to tell apart.</summary>
public sealed record ColorPair(string Id, string Label, NamedSwatch First, NamedSwatch Second, bool IsControl = false);

/// <summary>
/// Reference colors used by calibration, evaluation and the visual test board.
/// <para>
/// The "Nether-inspired" swatches are <b>approximate average colors</b> chosen to resemble the kinds
/// of dark reds, browns and muted grays found in that environment. They are original values, not
/// extracted game textures, and are used only to build original test patterns.
/// </para>
/// </summary>
public static class TestColorSets
{
    /// <summary>Pairs shown in calibration step 2 (order matters for the UI).</summary>
    public static IReadOnlyList<ColorPair> CalibrationPairs { get; } =
    [
        new("red-green", "Red vs green", new("Red", "#B5402F"), new("Green", "#5E8A2E")),
        new("darkred-brown", "Dark red vs brown", new("Dark red", "#7A1F1F"), new("Brown", "#6B4A26")),
        new("orange-green", "Orange vs green", new("Orange", "#D2802A"), new("Yellow-green", "#8FA83A")),
        new("mutedgreen-gray", "Muted green vs gray", new("Muted green", "#7A8B6A"), new("Gray", "#838383")),
        new("red-brown", "Red vs brown", new("Red", "#A8322E"), new("Brown", "#7D5A32")),
        new("green-shades", "Two shades of green", new("Green", "#3F8A3F"), new("Olive green", "#7A8A2E")),
        new("red-shades", "Two shades of red", new("Red", "#C23B3B"), new("Red-orange", "#B8652E")),
        new("pink-gray", "Pink vs gray", new("Pink", "#C9939B"), new("Gray", "#A3A3A3")),
        new("purple-blue", "Purple vs blue", new("Purple", "#7840A0"), new("Blue", "#4A55B0")),
        new("teal-gray", "Teal vs gray", new("Teal", "#4E8C86"), new("Gray", "#868686")),
        new("brown-olive", "Brown vs olive", new("Brown", "#7A5530"), new("Olive", "#6B6B2B")),
        new("netherrack-soulsand", "Dark red block vs brown block", new("Dark red", "#6F3030"), new("Brown", "#54402F")),
        new("control-blue-yellow", "Blue vs yellow", new("Blue", "#2E5BD8"), new("Yellow", "#E8C53A"), IsControl: true),
        new("control-gray-levels", "Dark gray vs light gray", new("Dark gray", "#454545"), new("Light gray", "#A5A5A5"), IsControl: true),
    ];

    /// <summary>Approximate colors resembling Nether materials (original values, see class remarks).</summary>
    public static IReadOnlyList<NamedSwatch> NetherInspiredPalette { get; } =
    [
        new("Dark red rock", "#6F3030"),        // netherrack-like
        new("Red moss", "#8A2222"),             // crimson nylium-like
        new("Red wart", "#741010"),             // nether wart block-like
        new("Dark brick", "#2E161A"),           // nether bricks-like
        new("Red brick", "#4A0C0E"),            // red nether bricks-like
        new("Brown sand", "#54402F"),           // soul sand-like
        new("Brown soil", "#4B3A2E"),           // soul soil-like
        new("Gray stone", "#505055"),           // basalt-like
        new("Black stone", "#2B2428"),          // blackstone-like
        new("Teal moss", "#2B7265"),            // warped nylium-like
        new("Teal wart", "#167879"),            // warped wart-like
        new("Orange magma", "#8E4020"),         // magma-like
        new("Glow block", "#AB8354"),           // glowstone-like
        new("Rock with white specks", "#7A4744"), // quartz ore-like
        new("Rock with gold specks", "#7A3A2C"),  // gold ore-like
        new("Old debris", "#5E4239"),           // ancient debris-like
        new("Purple-red stem", "#5E1E2E"),      // crimson stem-like
        new("Gray gravel", "#7F7A78"),
    ];

    /// <summary>Pairs from the Nether-inspired palette that are commonly hard to distinguish.</summary>
    public static IReadOnlyList<ColorPair> NetherConfusionPairs { get; } = BuildNetherPairs();

    /// <summary>General red/green/brown/gray confusion pairs used to evaluate presets.</summary>
    public static IReadOnlyList<ColorPair> GeneralConfusionPairs { get; } =
    [
        .. CalibrationPairs.Where(p => !p.IsControl),
        new("traffic", "Traffic red vs traffic green", new("Red", "#D23C32"), new("Green", "#3CA046")),
        new("leaf-berry", "Berry vs leaf", new("Berry red", "#A0283C"), new("Leaf green", "#507D32")),
        new("rust-moss", "Rust vs moss", new("Rust", "#9A4A28"), new("Moss", "#6E7A3A")),
        new("salmon-khaki", "Salmon vs khaki", new("Salmon", "#D8907A"), new("Khaki", "#B8B07A")),
        new("maroon-darkgreen", "Maroon vs dark green", new("Maroon", "#5A1A1A"), new("Dark green", "#2A4A1E")),
        new("health-bars", "Health bar red vs green", new("Red bar", "#E03030"), new("Green bar", "#30C030")),
    ];

    private static ColorPair[] BuildNetherPairs()
    {
        var p = NetherInspiredPalette.ToDictionary(s => s.Name);
        ColorPair Pair(string a, string b) => new($"nether:{a}|{b}", $"{a} vs {b}", p[a], p[b]);
        return
        [
            // Only pairs that typical vision separates clearly by color (CIEDE2000 > ~7.5); pairs that
            // differ mainly by texture (e.g. ore specks) are not color-vision problems and are excluded.
            Pair("Dark red rock", "Brown sand"),
            Pair("Dark red rock", "Red moss"),
            Pair("Dark red rock", "Red wart"),
            Pair("Dark red rock", "Old debris"),
            Pair("Dark red rock", "Orange magma"),
            Pair("Brown soil", "Gray stone"),
            Pair("Brown sand", "Gray stone"),
            Pair("Dark brick", "Black stone"),
            Pair("Red brick", "Dark brick"),
            Pair("Red moss", "Purple-red stem"),
            Pair("Red moss", "Brown sand"),
            Pair("Teal moss", "Gray stone"),
            Pair("Gray gravel", "Rock with white specks"),
            Pair("Red wart", "Red brick"),
            Pair("Orange magma", "Old debris"),
        ];
    }
}
