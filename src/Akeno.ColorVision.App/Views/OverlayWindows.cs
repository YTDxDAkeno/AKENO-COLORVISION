using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Akeno.ColorVision.App.Imaging;
using Akeno.ColorVision.App.Services;
using Akeno.ColorVision.Windows;

namespace Akeno.ColorVision.App.Views;

/// <summary>Base for small topmost popups that never take focus (so games and other apps keep focus).</summary>
public abstract class NonActivatingPopup : Window
{
    protected NonActivatingPopup(bool clickThrough)
    {
        ClickThrough = clickThrough;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        SizeToContent = SizeToContent.WidthAndHeight;
        Focusable = false;
    }

    protected bool ClickThrough { get; }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        if (ClickThrough)
        {
            WindowEffects.MakeClickThroughNoActivate(hwnd);
        }
        else
        {
            WindowEffects.MakeNoActivate(hwnd);
        }

        // Keep these popups out of screen sampling so the identifier never reads its own window.
        WindowEffects.ExcludeFromCapture(hwnd);
    }

    protected static Border CardBorder(UIElement child) => new()
    {
        Background = new SolidColorBrush(Color.FromArgb(0xF2, 0x1F, 0x20, 0x29)),
        BorderBrush = new SolidColorBrush(Color.FromRgb(0x7C, 0x6C, 0xFF)),
        BorderThickness = new Thickness(2),
        CornerRadius = new CornerRadius(12),
        Padding = new Thickness(16, 12, 16, 12),
        Child = child,
    };

    protected static TextBlock Text(string text, double size, FontWeight weight, Brush? brush = null) => new()
    {
        Text = text,
        FontSize = size,
        FontWeight = weight,
        Foreground = brush ?? Brushes.White,
        TextWrapping = TextWrapping.Wrap,
        MaxWidth = 340,
    };

    /// <summary>Positions the window next to a physical-pixel screen point, keeping it on screen.</summary>
    protected void PlaceNear(int physicalX, int physicalY)
    {
        var source = PresentationSource.FromVisual(this);
        var toDip = source?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        var p = toDip.Transform(new Point(physicalX, physicalY));
        var width = ActualWidth > 0 ? ActualWidth : 300;
        var height = ActualHeight > 0 ? ActualHeight : 160;
        var left = p.X + 28;
        var top = p.Y + 28;
        var area = SystemParameters.VirtualScreenWidth > 0
            ? new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight)
            : SystemParameters.WorkArea;
        if (left + width > area.Right)
        {
            left = p.X - width - 28;
        }

        if (top + height > area.Bottom)
        {
            top = p.Y - height - 28;
        }

        Left = Math.Max(area.Left, left);
        Top = Math.Max(area.Top, top);
    }
}

/// <summary>Short on-screen confirmation such as "Filter ON".</summary>
public sealed class OsdWindow : NonActivatingPopup
{
    private readonly DispatcherTimer _timer;
    private readonly TextBlock _title;
    private readonly TextBlock _detail;

    public OsdWindow()
        : base(clickThrough: true)
    {
        _title = Text(string.Empty, 22, FontWeights.SemiBold);
        _detail = Text(string.Empty, 14, FontWeights.Normal, new SolidColorBrush(Color.FromRgb(0xC8, 0xCA, 0xD8)));
        Content = CardBorder(new StackPanel { Children = { _title, _detail } });
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.6) };
        _timer.Tick += (_, _) =>
        {
            _timer.Stop();
            Hide();
        };
    }

    public void ShowMessage(string title, string detail)
    {
        _title.Text = title;
        _detail.Text = detail;
        _detail.Visibility = string.IsNullOrEmpty(detail) ? Visibility.Collapsed : Visibility.Visible;
        if (!IsVisible)
        {
            Show();
        }

        UpdateLayout();
        var area = SystemParameters.WorkArea;
        Left = area.Left + ((area.Width - ActualWidth) / 2);
        Top = area.Top + 40;
        _timer.Stop();
        _timer.Start();
    }
}

/// <summary>Shows one identification result next to the mouse pointer for a few seconds.</summary>
public sealed class IdentifierPopup : NonActivatingPopup
{
    private readonly DispatcherTimer _timer;
    private readonly StackPanel _panel = new();

    public IdentifierPopup()
        : base(clickThrough: true)
    {
        Content = CardBorder(_panel);
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
        _timer.Tick += (_, _) =>
        {
            _timer.Stop();
            Hide();
        };
    }

    public void ShowResult(ColorSampleResult result)
    {
        Fill(_panel, result, compact: false);
        if (!IsVisible)
        {
            Show();
        }

        UpdateLayout();
        PlaceNear(result.X, result.Y);
        _timer.Stop();
        _timer.Start();
    }

    public static void Fill(StackPanel panel, ColorSampleResult result, bool compact)
    {
        panel.Children.Clear();
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new Border
        {
            Width = compact ? 40 : 56,
            Height = compact ? 40 : 56,
            CornerRadius = new CornerRadius(8),
            BorderBrush = Brushes.White,
            BorderThickness = new Thickness(2),
            // The swatch is displayed through the filter, so use the original (or estimated original) color:
            // the user then sees it exactly as the sampled spot appears on screen.
            Background = result.ReportedColor.ToBrush(),
            Margin = new Thickness(0, 0, 14, 0),
        });
        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(Text(result.Description.Name, compact ? 20 : 24, FontWeights.SemiBold));
        texts.Children.Add(Text($"Category: {result.Description.Category}", 16, FontWeights.Normal));
        row.Children.Add(texts);
        panel.Children.Add(row);
        panel.Children.Add(Text($"{result.Description.Summary} · HEX {result.Description.Hex} · {result.RgbText}", 13, FontWeights.Normal,
            new SolidColorBrush(Color.FromRgb(0xC8, 0xCA, 0xD8))));
        if (!compact)
        {
            panel.Children.Add(Text(result.SourceLabel, 12, FontWeights.Normal, new SolidColorBrush(Color.FromRgb(0xA9, 0xAC, 0xBD))));
        }

        if (result.LooksBlocked)
        {
            panel.Children.Add(Text("This area reads as pure black. It may be protected content (e.g. some streaming video) that cannot be sampled.", 12, FontWeights.Normal,
                new SolidColorBrush(Color.FromRgb(0xF5, 0xB0, 0x1E))));
        }
    }
}

/// <summary>Live mode: follows the mouse pointer and names the color underneath it.</summary>
public sealed class LiveIdentifierOverlay : NonActivatingPopup
{
    private readonly IdentifierService _identifier;
    private readonly DispatcherTimer _timer;
    private readonly StackPanel _panel = new();
    private readonly TextBlock _hint;

    public LiveIdentifierOverlay(IdentifierService identifier, string stopHint)
        : base(clickThrough: true)
    {
        _identifier = identifier;
        _hint = Text(stopHint, 12, FontWeights.Normal, new SolidColorBrush(Color.FromRgb(0xA9, 0xAC, 0xBD)));
        var root = new StackPanel();
        root.Children.Add(_panel);
        root.Children.Add(_hint);
        Content = CardBorder(root);
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        _timer.Tick += (_, _) => UpdateSample();
    }

    public ColorSampleResult? Last { get; private set; }

    public void Start()
    {
        Show();
        UpdateSample();
        _timer.Start();
    }

    public void Stop()
    {
        _timer.Stop();
        Close();
    }

    private void UpdateSample()
    {
        var result = _identifier.SampleAtCursor();
        if (result is null)
        {
            return;
        }

        Last = result;
        IdentifierPopup.Fill(_panel, result, compact: true);
        UpdateLayout();
        PlaceNear(result.X, result.Y);
    }
}
