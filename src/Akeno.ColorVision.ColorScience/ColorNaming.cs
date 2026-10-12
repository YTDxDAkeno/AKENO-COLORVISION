namespace Akeno.ColorVision.ColorScience;

/// <summary>Broad, everyday color categories shown by the color identifier.</summary>
public enum ColorCategory
{
    Red,
    Orange,
    Brown,
    Yellow,
    Green,
    Cyan,
    Blue,
    Purple,
    Pink,
    Gray,
    Black,
    White,
}

/// <summary>A human-readable description of a color.</summary>
public sealed record ColorDescription(
    Rgb Color,
    string Name,
    ColorCategory Category,
    string Lightness,
    string Saturation,
    string Hex)
{
    /// <summary>E.g. "Dark, muted Red".</summary>
    public string Summary
    {
        get
        {
            var parts = new List<string>(2);
            if (Lightness.Length > 0)
            {
                parts.Add(Lightness);
            }

            if (Saturation.Length > 0)
            {
                parts.Add(Saturation);
            }

            var prefix = parts.Count == 0 ? string.Empty : string.Join(", ", parts) + " ";
            var text = prefix + CategoryName(Category).ToLowerInvariant();
            return char.ToUpperInvariant(text[0]) + text[1..];
        }
    }

    public static string CategoryName(ColorCategory category) => category.ToString();
}

/// <summary>
/// Names colors for the color identifier. The broad <see cref="ColorCategory"/> is computed from
/// OKLCh lightness/chroma/hue rules; the specific name is the nearest entry (in OKLab) of a curated
/// list <i>within the same category</i>, so the name and the category never contradict each other.
/// </summary>
public static class ColorNaming
{
    public static ColorDescription Describe(Rgb color)
    {
        var c = color.Clamp01();
        var lab = OkLab.FromSrgb(c);
        var category = Categorize(lab);

        NamedColor? best = null;
        var bestDistance = double.MaxValue;
        foreach (var entry in Entries)
        {
            if (entry.Category != category)
            {
                continue;
            }

            var d = lab.DistanceTo(entry.Lab);
            if (d < bestDistance)
            {
                bestDistance = d;
                best = entry;
            }
        }

        var name = best?.Name ?? ColorDescription.CategoryName(category);
        return new ColorDescription(c, name, category, LightnessWord(lab, category), SaturationWord(lab, category), c.ToHex());
    }

    /// <summary>Rule-based category from OKLCh coordinates.</summary>
    public static ColorCategory Categorize(Rgb color) => Categorize(OkLab.FromSrgb(color.Clamp01()));

    internal static ColorCategory Categorize(OkLab lab)
    {
        var l = lab.L;
        var c = lab.Chroma;
        var h = lab.HueDegrees;

        var achromaticLimit = l < 0.3 ? 0.035 : 0.02;
        if (c < achromaticLimit || (c < 0.04 && l >= 0.92))
        {
            if (l >= 0.92)
            {
                return ColorCategory.White;
            }

            return l <= 0.25 ? ColorCategory.Black : ColorCategory.Gray;
        }

        if (h >= 345 || h < 38)
        {
            var pinkish = l >= 0.72 || ((h >= 345 || h < 15) && l >= 0.62 && c >= 0.15);
            return pinkish ? ColorCategory.Pink : ColorCategory.Red;
        }

        if (h < 85)
        {
            return l < 0.66 || (c < 0.09 && l < 0.85) ? ColorCategory.Brown : ColorCategory.Orange;
        }

        if (h < 125)
        {
            return l >= 0.70 ? ColorCategory.Yellow : ColorCategory.Green;
        }

        if (h < 175)
        {
            return ColorCategory.Green;
        }

        if (h < 210)
        {
            return ColorCategory.Cyan;
        }

        if (h < 285)
        {
            return ColorCategory.Blue;
        }

        return h >= 330 && l >= 0.75 ? ColorCategory.Pink : ColorCategory.Purple;
    }

    private static string LightnessWord(OkLab lab, ColorCategory category)
    {
        if (category is ColorCategory.Black or ColorCategory.White)
        {
            return string.Empty;
        }

        return lab.L switch
        {
            < 0.30 => "Very dark",
            < 0.45 => "Dark",
            > 0.85 => "Very light",
            > 0.72 => "Light",
            _ => string.Empty,
        };
    }

    private static string SaturationWord(OkLab lab, ColorCategory category)
    {
        if (category is ColorCategory.Black or ColorCategory.White or ColorCategory.Gray)
        {
            return string.Empty;
        }

        return lab.Chroma switch
        {
            < 0.06 => "grayish",
            < 0.10 => "muted",
            > 0.20 => "vivid",
            _ => string.Empty,
        };
    }

    private sealed record NamedColor(string Name, Rgb Color, OkLab Lab, ColorCategory Category);

    private static readonly NamedColor[] Entries = BuildEntries();

    private static NamedColor[] BuildEntries()
    {
        (string Name, string Hex)[] raw =
        [
            // Reds
            ("Red", "#FF0000"), ("Bright Red", "#E8222A"), ("Crimson", "#DC143C"), ("Dark Red", "#8B2424"),
            ("Deep Red", "#8B0000"), ("Maroon", "#800000"), ("Burgundy", "#800020"), ("Wine", "#722F37"),
            ("Brick Red", "#B03A2E"), ("Blood Red", "#660000"), ("Raspberry", "#C21E56"), ("Cherry", "#D2042D"),
            ("Dark Brick Red", "#612626"), ("Very Dark Red", "#3B0A0A"), ("Indian Red", "#CD5C5C"),
            ("Tomato Red", "#E5412D"),
            // Pinks
            ("Pink", "#FFC0CB"), ("Light Pink", "#FFB6C1"), ("Hot Pink", "#FF69B4"), ("Deep Pink", "#FF1493"),
            ("Salmon", "#FA8072"), ("Light Coral", "#F08080"), ("Rose Pink", "#F7A1B5"), ("Pale Pink", "#F9D6DC"),
            ("Light Magenta Pink", "#F49AC2"), ("Light Orchid", "#E3A6E3"),
            // Oranges
            ("Orange", "#FFA500"), ("Dark Orange", "#FF8C00"), ("Coral", "#FF7F50"), ("Peach", "#FFCBA4"),
            ("Amber", "#FFBF00"), ("Tangerine", "#F28500"), ("Light Orange", "#FFC07A"), ("Apricot", "#FBCEB1"),
            // Browns
            ("Brown", "#8B4513"), ("Dark Brown", "#5C4033"), ("Chocolate", "#D2691E"), ("Sienna", "#A0522D"),
            ("Rust", "#B7410E"), ("Tan", "#D2B48C"), ("Khaki", "#C3B091"), ("Sand", "#C2B280"),
            ("Coffee", "#6F4E37"), ("Copper", "#B87333"), ("Light Brown", "#B5835A"), ("Gray-Brown", "#7A6A58"),
            ("Dark Olive", "#6B5A2A"), ("Dark Goldenrod", "#B8860B"), ("Taupe Brown", "#8B7D6B"), ("Soil Brown", "#513E32"),
            ("Very Dark Brown", "#3B2A1E"),
            // Yellows
            ("Yellow", "#FFFF00"), ("Gold", "#FFD700"), ("Lemon", "#FFF44F"), ("Mustard", "#E1AD01"),
            ("Light Yellow", "#FFFFA0"), ("Pale Yellow", "#F3EBA0"), ("Butter", "#F6E27F"), ("Straw", "#E4D96F"),
            // Greens
            ("Green", "#00A000"), ("Bright Green", "#00FF00"), ("Lime", "#32CD32"), ("Yellow-Green", "#9ACD32"),
            ("Dark Green", "#006400"), ("Forest Green", "#228B22"), ("Olive", "#808000"), ("Olive Green", "#727A32"),
            ("Dark Olive Green", "#556B2F"), ("Moss Green", "#8A9A5B"), ("Sage", "#9CAF88"), ("Mint", "#98FF98"),
            ("Sea Green", "#2E8B57"), ("Emerald", "#50C878"), ("Grass Green", "#5DA130"), ("Muted Green", "#7A8B6E"),
            ("Pale Green", "#B5D6A7"), ("Very Dark Green", "#0B2E13"), ("Jade", "#00A86B"), ("Gray-Green", "#6F7F66"),
            // Cyans / teals
            ("Cyan", "#00FFFF"), ("Teal", "#008080"), ("Dark Teal", "#014D4E"), ("Turquoise", "#40E0D0"),
            ("Aquamarine", "#7FFFD4"), ("Deep Turquoise", "#0D98BA"), ("Dark Cyan", "#2B7265"), ("Light Cyan", "#B2F5F5"),
            ("Gray-Teal", "#5E8C88"),
            // Blues
            ("Blue", "#0000FF"), ("Royal Blue", "#4169E1"), ("Navy", "#000080"), ("Dark Blue", "#00008B"),
            ("Sky Blue", "#87CEEB"), ("Light Blue", "#ADD8E6"), ("Steel Blue", "#4682B4"), ("Azure", "#007FFF"),
            ("Cobalt", "#0047AB"), ("Denim", "#1560BD"), ("Blue Violet", "#6A5ACD"), ("Periwinkle", "#CCCCFF"),
            ("Slate Gray-Blue", "#36454F"), ("Baby Blue", "#89CFF0"), ("Midnight Blue", "#191970"), ("Gray-Blue", "#6B7B8C"),
            // Purples
            ("Purple", "#800080"), ("Violet", "#8A2BE2"), ("Indigo", "#4B0082"), ("Magenta", "#FF00FF"),
            ("Plum", "#8E4585"), ("Lavender", "#B57EDC"), ("Mauve", "#B784A7"), ("Orchid", "#DA70D6"),
            ("Dark Purple", "#301934"), ("Eggplant", "#614051"), ("Lilac", "#C8A2C8"), ("Grape", "#6F2DA8"),
            ("Dusty Purple", "#8B7090"),
            // Neutrals
            ("White", "#FFFFFF"), ("Off-White", "#F5F5F0"), ("Ivory", "#FFFFF0"), ("Cream", "#FFFDD0"), ("Beige", "#F5F5DC"),
            ("Light Gray", "#D3D3D3"), ("Silver", "#C0C0C0"), ("Gray", "#808080"), ("Dark Gray", "#505050"),
            ("Charcoal", "#333333"), ("Black", "#000000"), ("Near Black", "#1A1A1A"), ("Medium Gray", "#A0A0A0"),
            ("Warm Gray", "#8B8589"), ("Grayish Blue", "#8C92AC"),
        ];

        return raw.Select(r =>
        {
            var color = Rgb.FromHex(r.Hex);
            var lab = OkLab.FromSrgb(color);
            return new NamedColor(r.Name, color, lab, Categorize(lab));
        }).ToArray();
    }
}
