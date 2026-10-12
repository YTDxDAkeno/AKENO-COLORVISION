using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Akeno.ColorVision.ColorScience;

namespace Akeno.ColorVision.App.Imaging;

/// <summary>A legend entry for a generated scene.</summary>
public sealed record SceneLegendItem(char Key, string Name, Brush Swatch);

/// <summary>
/// Procedurally generated, original test images. No game textures are used: the "Nether-inspired"
/// scene is built from approximate average colors (see <see cref="TestColorSets.NetherInspiredPalette"/>)
/// with a simple blocky noise pattern.
/// </summary>
public static class TestPatterns
{
    private const int Tile = 40;
    private const int SubPixels = 8;

    private static readonly string[] SceneMap =
    [
        "DDDDDDDDDDDDDDDD",
        "DDDGDDDDDWDDDDBB",
        "DDDDDDDDDDDDDBXB",
        "RRDDDDOODDDDDBBB",
        "RRRSDDDDDTTTDDDD",
        "SSSSSDEDDTTTKKKK",
        "PPSSSSDDDDTTKFFK",
        "PPPSSSDDDHTTKKKK",
    ];

    private static readonly Dictionary<char, (string Name, string Hex, string? SpeckHex)> SceneKey = new()
    {
        ['D'] = ("Dark red rock", "#6F3030", null),
        ['G'] = ("Rock with gold specks", "#6F3030", "#E2B33C"),
        ['W'] = ("Rock with white specks", "#6F3030", "#E6DED6"),
        ['R'] = ("Red moss", "#8A2222", null),
        ['O'] = ("Orange magma", "#8E4020", "#D9792B"),
        ['S'] = ("Brown sand", "#54402F", null),
        ['T'] = ("Teal moss", "#2B7265", null),
        ['E'] = ("Old debris", "#5E4239", null),
        ['B'] = ("Dark brick", "#2E161A", null),
        ['X'] = ("Red brick", "#4A0C0E", null),
        ['K'] = ("Black stone", "#2B2428", null),
        ['F'] = ("Red wart", "#741010", null),
        ['P'] = ("Gray stone", "#505055", null),
        ['H'] = ("Glow block", "#AB8354", "#E8C87A"),
    };

    public static IReadOnlyList<SceneLegendItem> NetherSceneLegend { get; } = SceneKey
        .Select(kv => new SceneLegendItem(kv.Key, kv.Value.Name, Rgb.FromHex(kv.Value.Hex).ToBrush()))
        .ToList();

    /// <summary>A 640×320 blocky cave scene in dark reds, browns and grays.</summary>
    public static BitmapSource CreateNetherInspiredScene()
    {
        var width = SceneMap[0].Length * Tile;
        var height = SceneMap.Length * Tile;
        var stride = width * 4;
        var pixels = new byte[height * stride];
        var sub = Tile / SubPixels;

        for (var ty = 0; ty < SceneMap.Length; ty++)
        {
            for (var tx = 0; tx < SceneMap[ty].Length; tx++)
            {
                var (_, hex, speck) = SceneKey[SceneMap[ty][tx]];
                var baseColor = Rgb.FromHex(hex);
                var speckColor = speck is null ? (Rgb?)null : Rgb.FromHex(speck);
                for (var sy = 0; sy < SubPixels; sy++)
                {
                    for (var sx = 0; sx < SubPixels; sx++)
                    {
                        var h = Hash(tx, ty, sx, sy);
                        Rgb c;
                        if (speckColor is { } s && (h & 0xFF) < 30)
                        {
                            c = s;
                        }
                        else
                        {
                            var factor = 0.84 + (((h >> 8) & 0xFF) / 255.0 * 0.32);
                            c = new Rgb(baseColor.R * factor, baseColor.G * factor, baseColor.B * factor);
                        }

                        var (r, g, b) = c.ToBytes();
                        for (var py = 0; py < sub; py++)
                        {
                            var y = (ty * Tile) + (sy * sub) + py;
                            for (var px = 0; px < sub; px++)
                            {
                                var x = (tx * Tile) + (sx * sub) + px;
                                var i = (y * stride) + (x * 4);
                                pixels[i] = b;
                                pixels[i + 1] = g;
                                pixels[i + 2] = r;
                                pixels[i + 3] = 255;
                            }
                        }
                    }
                }
            }
        }

        return Freeze(width, height, pixels);
    }

    /// <summary>Side-by-side squares for the given pairs, plus a red→green ramp, for comparing correction options.</summary>
    public static BitmapSource CreateComparisonPattern(IReadOnlyList<ColorPair> pairs)
    {
        var chosen = pairs.Count > 0 ? pairs.Take(6).ToList() : TestColorSets.CalibrationPairs.Where(p => !p.IsControl).Take(6).ToList();
        const int cell = 56, gap = 6, pairGap = 22, margin = 12, cols = 3;
        var rows = (int)Math.Ceiling(chosen.Count / (double)cols);
        var pairWidth = (cell * 2) + gap;
        var width = (margin * 2) + (cols * pairWidth) + ((cols - 1) * pairGap);
        var rampHeight = 28;
        var height = (margin * 3) + (rows * cell) + ((rows - 1) * pairGap) + rampHeight;
        var stride = width * 4;
        var pixels = new byte[height * stride];
        Fill(pixels, stride, 0, 0, width, height, Rgb.Gray(0.35));

        for (var i = 0; i < chosen.Count; i++)
        {
            var col = i % cols;
            var row = i / cols;
            var x = margin + (col * (pairWidth + pairGap));
            var y = margin + (row * (cell + pairGap));
            Fill(pixels, stride, x, y, cell, cell, chosen[i].First.Color);
            Fill(pixels, stride, x + cell + gap, y, cell, cell, chosen[i].Second.Color);
        }

        var rampY = height - margin - rampHeight;
        var rampW = width - (2 * margin);
        var red = Rgb.FromHex("#C8372D");
        var brown = Rgb.FromHex("#7A5A2E");
        var green = Rgb.FromHex("#3E9A3A");
        for (var x = 0; x < rampW; x++)
        {
            var t = x / (double)(rampW - 1);
            var c = t < 0.5 ? Rgb.Lerp(red, brown, t * 2) : Rgb.Lerp(brown, green, (t - 0.5) * 2);
            Fill(pixels, stride, margin + x, rampY, 1, rampHeight, c);
        }

        return Freeze(width, height, pixels);
    }

    private static void Fill(byte[] pixels, int stride, int x0, int y0, int w, int h, Rgb color)
    {
        var (r, g, b) = color.ToBytes();
        for (var y = y0; y < y0 + h; y++)
        {
            for (var x = x0; x < x0 + w; x++)
            {
                var i = (y * stride) + (x * 4);
                pixels[i] = b;
                pixels[i + 1] = g;
                pixels[i + 2] = r;
                pixels[i + 3] = 255;
            }
        }
    }

    private static BitmapSource Freeze(int width, int height, byte[] pixels)
    {
        var bmp = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
        bmp.WritePixels(new Int32Rect(0, 0, width, height), pixels, width * 4, 0);
        bmp.Freeze();
        return bmp;
    }

    private static uint Hash(int a, int b, int c, int d)
    {
        unchecked
        {
            var h = (uint)((a * 73856093) ^ (b * 19349663) ^ (c * 83492791) ^ (d * 2654435761u));
            h ^= h >> 13;
            h *= 0x5bd1e995;
            h ^= h >> 15;
            return h;
        }
    }
}
