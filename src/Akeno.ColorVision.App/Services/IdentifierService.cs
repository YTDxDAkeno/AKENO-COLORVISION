using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Akeno.ColorVision.App.Imaging;
using Akeno.ColorVision.ColorScience;
using Akeno.ColorVision.Core.Diagnostics;
using Akeno.ColorVision.Core.Settings;
using Akeno.ColorVision.Windows;

namespace Akeno.ColorVision.App.Services;

/// <summary>One color identification result.</summary>
public sealed record ColorSampleResult(
    int X,
    int Y,
    Rgb ScreenColor,
    Rgb ReportedColor,
    ColorDescription Description,
    string SourceLabel,
    bool IsEstimate,
    bool LooksBlocked,
    DateTimeOffset Time)
{
    public string RgbText
    {
        get
        {
            var (r, g, b) = ReportedColor.ToBytes();
            return $"RGB {r}, {g}, {b}";
        }
    }
}

/// <summary>
/// Color identification. Reads screen pixels and explains honestly what was read:
/// the original color (filter off, or this PC's sampling is measured to return pre-filter colors),
/// or a filtered color converted back with the inverse matrix (marked as an estimate).
/// </summary>
public sealed class IdentifierService
{
    private static readonly Rgb[] CheckCandidates =
    [
        Rgb.FromHex("#C83C32"), Rgb.FromHex("#3CA046"), Rgb.FromHex("#2E5BD8"), Rgb.FromHex("#E8C53A"), Rgb.FromHex("#B05AC8"),
    ];

    private readonly FilterService _filter;
    private readonly AppSettings _settings;
    private readonly Action _saveSettings;
    private readonly IAppLogger _log;

    public IdentifierService(FilterService filter, AppSettings settings, Action saveSettings, IAppLogger log)
    {
        _filter = filter;
        _settings = settings;
        _saveSettings = saveSettings;
        _log = log;
    }

    public SamplingSource MeasuredSource => _settings.Identifier.MeasuredSamplingSource;

    /// <summary>True if the filter is on but we have not yet measured what sampling returns.</summary>
    public bool NeedsSamplingCheck => _filter.IsOn && MeasuredSource == SamplingSource.Unknown;

    public ColorSampleResult? SampleAtCursor() =>
        ScreenSampler.TryGetCursorPosition(out var x, out var y) ? SampleAt(x, y) : null;

    public ColorSampleResult? SampleAt(int x, int y)
    {
        var sample = ScreenSampler.TrySample(x, y, 9);
        if (sample is null)
        {
            return null;
        }

        var screen = sample.Average;
        var reported = screen;
        var estimate = false;
        string label;

        if (!_filter.IsOn)
        {
            label = "Original color (the filter is off).";
        }
        else if (MeasuredSource == SamplingSource.OriginalColors)
        {
            label = "Original color (on this PC the identifier reads colors from before the filter).";
        }
        else if (MeasuredSource == SamplingSource.FilteredColors && _filter.CurrentMatrix.TryInvert(out var inverse))
        {
            var back = inverse.Transform(screen);
            if (back.R is > -0.03 and < 1.03 && back.G is > -0.03 and < 1.03 && back.B is > -0.03 and < 1.03)
            {
                reported = back.Clamp01();
                estimate = true;
                label = "Estimated original color (this PC reports filtered colors; converted back using the filter settings).";
            }
            else
            {
                label = "Color as shown with the filter. The original could not be calculated exactly.";
            }
        }
        else
        {
            label = "Color as read from the screen. It is not yet known whether this is before or after the filter — run the check on the Color Identifier page.";
        }

        return new ColorSampleResult(x, y, screen, reported, ColorNaming.Describe(reported), label, estimate, sample.LooksBlocked, DateTimeOffset.Now);
    }

    /// <summary>
    /// Measures whether screen sampling returns original or filtered colors: shows a small patch of a
    /// known color, samples it, and compares with the known color and the filtered known color.
    /// Requires the filter to be on.
    /// </summary>
    public async Task<SamplingVerdict> RunSamplingCheckAsync()
    {
        if (!_filter.IsOn)
        {
            return SamplingVerdict.Inconclusive;
        }

        var matrix = _filter.CurrentMatrix;
        var known = CheckCandidates.OrderByDescending(c => c.MaxDifference(matrix.TransformClamped(c))).First();
        var window = new Window
        {
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            Topmost = true,
            ShowActivated = false,
            Width = 80,
            Height = 80,
            Left = SystemParameters.WorkArea.Left + 24,
            Top = SystemParameters.WorkArea.Top + 24,
            Background = known.ToBrush(),
            Content = new Border(),
            Title = "Akeno ColorVision check",
        };

        try
        {
            window.Show();
            await Task.Delay(350);
            var center = window.PointToScreen(new Point(window.ActualWidth / 2, window.ActualHeight / 2));
            var sample = ScreenSampler.TrySample((int)center.X, (int)center.Y, 5);
            if (sample is null)
            {
                return SamplingVerdict.Inconclusive;
            }

            var verdict = ScreenSampler.ClassifySampling(known, sample.Center, matrix);
            _log.Info($"Sampling check: known {known.ToHex()}, sampled {sample.Center.ToHex()}, filtered {matrix.TransformClamped(known).ToHex()} → {verdict}.");
            if (verdict != SamplingVerdict.Inconclusive)
            {
                _settings.Identifier.MeasuredSamplingSource = verdict == SamplingVerdict.ReadsOriginal ? SamplingSource.OriginalColors : SamplingSource.FilteredColors;
                _settings.Identifier.SamplingCheckedUtc = DateTimeOffset.UtcNow;
                _saveSettings();
            }

            return verdict;
        }
        finally
        {
            window.Close();
        }
    }
}
