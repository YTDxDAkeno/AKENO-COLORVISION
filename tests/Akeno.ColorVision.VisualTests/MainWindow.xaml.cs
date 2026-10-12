using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Akeno.ColorVision.ColorScience;

namespace Akeno.ColorVision.VisualTests;

/// <summary>
/// Manual visual test board. Open it next to Akeno ColorVision and toggle the filter: every swatch,
/// ramp and text sample should visibly change, grays should stay neutral, and confusion pairs should
/// become easier to tell apart. Nothing here is automated — see docs/TESTING.md for the checklist.
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Build();
    }

    private void Build()
    {
        Heading("Akeno ColorVision — Visual Test Board", 26);
        Note("Use this window to check the system-wide filter by eye. Toggle the filter with its shortcut and compare. " +
             "Expected: confusion pairs look more different with the filter on; the gray ramp stays neutral (no tint); black stays black and white stays white.");

        Section("1. Calibration confusion pairs");
        var pairs = Wrap();
        foreach (var pair in TestColorSets.CalibrationPairs)
        {
            pairs.Children.Add(PairTile(pair.Label + (pair.IsControl ? " (control)" : string.Empty), pair.First, pair.Second));
        }

        Section("2. Nether-inspired palette (original approximate colors, not game textures)");
        var nether = Wrap();
        foreach (var swatch in TestColorSets.NetherInspiredPalette)
        {
            nether.Children.Add(SwatchTile(swatch.Name, swatch.Color));
        }

        Section("3. Ramps");
        Ramp("Red → green", Rgb.FromHex("#C8372D"), Rgb.FromHex("#3E9A3A"));
        Ramp("Red → brown → olive", Rgb.FromHex("#B03030"), Rgb.FromHex("#6E6E2A"));
        Ramp("Dark red → gray", Rgb.FromHex("#601818"), Rgb.FromHex("#606060"));
        Ramp("Green → gray", Rgb.FromHex("#3E9A3A"), Rgb.FromHex("#808080"));
        HueStrip();
        Steps("Gray ramp (must stay neutral)", 17, i => Rgb.Gray(i / 16.0));
        Steps("Saturation ramp (red)", 11, i => Rgb.Lerp(Rgb.Gray(0.45), Rgb.FromHex("#D83020"), i / 10.0));

        Section("4. Primaries and secondaries");
        var primaries = Wrap();
        foreach (var (name, hex) in new[] { ("Red", "#FF0000"), ("Green", "#00FF00"), ("Blue", "#0000FF"), ("Cyan", "#00FFFF"), ("Magenta", "#FF00FF"), ("Yellow", "#FFFF00"), ("Black", "#000000"), ("White", "#FFFFFF") })
        {
            primaries.Children.Add(SwatchTile(name, Rgb.FromHex(hex)));
        }

        Section("5. Colored text readability");
        var text = Wrap();
        foreach (var (fg, bg) in new[] { ("#E03030", "#202020"), ("#30C030", "#202020"), ("#E03030", "#30A030"), ("#FFFFFF", "#8A2222"), ("#000000", "#6F8F3F"), ("#C83737", "#7D5A32") })
        {
            var border = new Border { Background = Brush(Rgb.FromHex(bg)), Padding = new Thickness(14), Margin = new Thickness(0, 0, 12, 12), Width = 260 };
            border.Child = new TextBlock { Text = $"Sample text {fg} on {bg}", Foreground = Brush(Rgb.FromHex(fg)), FontSize = 18, TextWrapping = TextWrapping.Wrap };
            text.Children.Add(border);
        }

        Section("6. Color identifier reference patches");
        Note("Point the Color Identifier at these patches. With the filter off it must report the listed HEX value (±1). With the filter on, the result must say whether it is the original color or an estimate.");
        var refs = Wrap();
        foreach (var hex in new[] { "#8B2424", "#727A32", "#C83C32", "#2E5BD8", "#808080" })
        {
            refs.Children.Add(SwatchTile(hex, Rgb.FromHex(hex), 120));
        }
    }

    private void Heading(string text, double size) =>
        Root.Children.Add(new TextBlock { Text = text, FontSize = size, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) });

    private void Section(string text) =>
        Root.Children.Add(new TextBlock { Text = text, FontSize = 20, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 24, 0, 10) });

    private void Note(string text) =>
        Root.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.LightGray, MaxWidth = 1100, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 8) });

    private WrapPanel Wrap()
    {
        var panel = new WrapPanel();
        Root.Children.Add(new Border { Background = Brush(Rgb.Gray(0.35)), Padding = new Thickness(12), CornerRadius = new CornerRadius(8), Child = panel });
        return panel;
    }

    private static FrameworkElement PairTile(string label, NamedSwatch a, NamedSwatch b)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        row.Children.Add(new Border { Width = 80, Height = 80, Background = Brush(a.Color), Margin = new Thickness(0, 0, 8, 0), ToolTip = $"{a.Name} {a.Color.ToHex()}" });
        row.Children.Add(new Border { Width = 80, Height = 80, Background = Brush(b.Color), ToolTip = $"{b.Name} {b.Color.ToHex()}" });
        var panel = new StackPanel { Width = 200, Margin = new Thickness(6, 6, 6, 14) };
        panel.Children.Add(row);
        panel.Children.Add(new TextBlock { Text = label, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) });
        panel.Children.Add(new TextBlock { Text = $"{a.Color.ToHex()} / {b.Color.ToHex()}", TextAlignment = TextAlignment.Center, Foreground = Brushes.LightGray, FontSize = 12 });
        return panel;
    }

    private static FrameworkElement SwatchTile(string label, Rgb color, double size = 90)
    {
        var panel = new StackPanel { Width = size + 40, Margin = new Thickness(4, 4, 4, 12) };
        panel.Children.Add(new Border { Width = size, Height = size, Background = Brush(color), BorderBrush = Brushes.Black, BorderThickness = new Thickness(1) });
        panel.Children.Add(new TextBlock { Text = label, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
        panel.Children.Add(new TextBlock { Text = color.ToHex(), TextAlignment = TextAlignment.Center, Foreground = Brushes.LightGray, FontSize = 12 });
        return panel;
    }

    private void Ramp(string label, Rgb from, Rgb to)
    {
        Root.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 6, 0, 4) });
        var brush = new LinearGradientBrush(Color(from), Color(to), 0);
        Root.Children.Add(new Border { Height = 40, Background = brush, Margin = new Thickness(0, 0, 0, 6) });
    }

    private void HueStrip()
    {
        Root.Children.Add(new TextBlock { Text = "Hue strip", Margin = new Thickness(0, 6, 0, 4) });
        var stops = new GradientStopCollection();
        string[] hexes = ["#FF0000", "#FFFF00", "#00FF00", "#00FFFF", "#0000FF", "#FF00FF", "#FF0000"];
        for (var i = 0; i < hexes.Length; i++)
        {
            stops.Add(new GradientStop(Color(Rgb.FromHex(hexes[i])), i / (double)(hexes.Length - 1)));
        }

        Root.Children.Add(new Border { Height = 40, Background = new LinearGradientBrush(stops, 0), Margin = new Thickness(0, 0, 0, 6) });
    }

    private void Steps(string label, int count, Func<int, Rgb> color)
    {
        Root.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 6, 0, 4) });
        var grid = new UniformGrid { Columns = count, Height = 48, Margin = new Thickness(0, 0, 0, 6) };
        for (var i = 0; i < count; i++)
        {
            grid.Children.Add(new Border { Background = Brush(color(i)), ToolTip = color(i).ToHex() });
        }

        Root.Children.Add(grid);
    }

    private static Color Color(Rgb c)
    {
        var (r, g, b) = c.ToBytes();
        return System.Windows.Media.Color.FromRgb(r, g, b);
    }

    private static SolidColorBrush Brush(Rgb c) => new(Color(c));
}
