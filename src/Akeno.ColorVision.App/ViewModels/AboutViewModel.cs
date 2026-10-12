using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Input;
using System.Windows.Threading;
using Akeno.ColorVision.App.Infrastructure;
using Akeno.ColorVision.App.Services;
using Akeno.ColorVision.ColorScience;
using Akeno.ColorVision.Core.Diagnostics;
using Akeno.ColorVision.Core.Filtering;
using Akeno.ColorVision.Rendering;
using Microsoft.Win32;

namespace Akeno.ColorVision.App.ViewModels;

public sealed record BackendStatusItem(string Name, string Level, string Summary, IReadOnlyList<string> Details);

public sealed record CompatibilityItem(string Target, string Expectation, string Status);

public enum VerifyState
{
    Idle,
    Testing,
    Asking,
    Confirmed,
    NotVisible,
}

/// <summary>About &amp; diagnostics page, including the "does the filter work on this PC?" check.</summary>
public sealed class AboutViewModel : PageViewModel
{
    // Swapping red and blue is unmistakable for any color vision, and is a safe, invertible matrix.
    private static readonly ColorMatrix TestMatrix = ColorMatrix.FromRowMajor([0, 0, 1, 0, 1, 0, 1, 0, 0]);
    private readonly DispatcherTimer _timer;
    private VerifyState _verify = VerifyState.Idle;
    private int _secondsLeft;
    private bool _wasOn;
    private string? _message;

    public AboutViewModel(AppHost host)
        : base(host)
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) =>
        {
            _secondsLeft--;
            OnPropertyChanged(nameof(VerifyText));
            if (_secondsLeft <= 0)
            {
                EndTest();
            }
        };
        StartTestCommand = new RelayCommand(StartTest, () => _verify is not VerifyState.Testing);
        ConfirmYesCommand = new RelayCommand(() => Answer(true));
        ConfirmNoCommand = new RelayCommand(() => Answer(false));
        ExportCommand = new RelayCommand(Export);
        RefreshCommand = new RelayCommand(Refresh);
        OpenProjectCommand = new RelayCommand(() => Open("https://github.com/YTDxDAkeno/AKENO-COLORVISION"));
        Refresh();
    }

    public string Version => $"Version {AppHost.Version}";

    public ObservableCollection<BackendStatusItem> Backends { get; } = [];

    public ObservableCollection<DiagnosticsSection> Sections { get; } = [];

    public IReadOnlyList<CompatibilityItem> Compatibility { get; } =
    [
        new("Windows desktop, File Explorer, Settings", "Works through desktop composition (verified on a Windows CI session)", "Not yet verified on a physical monitor"),
        new("Google Chrome, Microsoft Edge, Firefox", "Expected to work", "Not yet verified"),
        new("YouTube and other web video", "Expected to work; some protected video may behave differently", "Not yet verified"),
        new("Discord", "Expected to work", "Not yet verified"),
        new("Minecraft: Java Edition (windowed / borderless)", "Expected to work", "Not yet verified in-game"),
        new("Minecraft: Java Edition (F11 fullscreen)", "Usually composed by Windows; may not be affected on some systems", "Not yet verified in-game"),
        new("Minecraft for Windows (Bedrock)", "Expected to work in windowed / fullscreen-window mode", "Not yet verified in-game"),
        new("Games in exclusive fullscreen", "May bypass the filter — use borderless/windowed mode", "Known limitation"),
        new("Screenshots and screen recordings", "Usually include the filtered colors", "Verified for GDI captures in CI"),
        new("Multiple monitors", "One filter for all monitors (no per-monitor settings)", "By design"),
        new("HDR displays", "Works, but colors may look different than in SDR", "Not yet verified"),
        new("Remote Desktop sessions", "Unknown", "Not tested"),
    ];

    public VerifyState Verify
    {
        get => _verify;
        private set
        {
            if (SetProperty(ref _verify, value))
            {
                OnPropertiesChanged(nameof(VerifyText), nameof(IsAsking), nameof(IsTesting));
            }
        }
    }

    public bool IsAsking => Verify == VerifyState.Asking;

    public bool IsTesting => Verify == VerifyState.Testing;

    public string VerifyText => Verify switch
    {
        VerifyState.Testing => $"Test running: red and blue are swapped on your whole screen. Back to normal in {_secondsLeft} s…",
        VerifyState.Asking => "Did the colors on your screen change during the test (for example, blue things turned red-ish)?",
        VerifyState.Confirmed => "Confirmed: the filter visibly changes your screen on this PC.",
        VerifyState.NotVisible => "The screen did not change. Common causes: an exclusive-fullscreen app was in front, a Remote Desktop session, or HDR settings. Please export diagnostics and report the problem.",
        _ => Host.Settings.FilterVisuallyConfirmedUtc is { } t
            ? $"You confirmed the filter works on this PC on {t.ToLocalTime():d}."
            : "Not confirmed yet. The test swaps red and blue on your whole screen for 5 seconds.",
    };

    public string? Message
    {
        get => _message;
        private set => SetProperty(ref _message, value);
    }

    public ICommand StartTestCommand { get; }

    public ICommand ConfirmYesCommand { get; }

    public ICommand ConfirmNoCommand { get; }

    public ICommand ExportCommand { get; }

    public ICommand RefreshCommand { get; }

    public ICommand OpenProjectCommand { get; }

    public override void OnNavigatedTo() => Refresh();

    public override void OnNavigatedFrom()
    {
        if (Verify == VerifyState.Testing)
        {
            EndTest();
        }
    }

    private void Refresh()
    {
        Backends.Clear();
        foreach (var caps in new[] { Host.Filter.Capabilities, BackendCatalog.GpuCaptureCapabilities() })
        {
            Backends.Add(new BackendStatusItem(caps.DisplayName, BackendCapabilities.Describe(caps.Level), caps.Summary, caps.Details));
        }

        Sections.Clear();
        foreach (var section in DiagnosticsBuilder.Build(Host))
        {
            Sections.Add(section);
        }

        OnPropertyChanged(nameof(VerifyText));
    }

    private void StartTest()
    {
        _wasOn = Host.Filter.IsOn;
        var result = Host.Filter.ShowTemporary(TestMatrix);
        if (!result.Success)
        {
            Message = "The test could not start: " + result.Message;
            Verify = VerifyState.NotVisible;
            return;
        }

        _secondsLeft = 5;
        Verify = VerifyState.Testing;
        _timer.Start();
    }

    private void EndTest()
    {
        _timer.Stop();
        Host.Filter.EndTemporary(_wasOn);
        Verify = VerifyState.Asking;
    }

    private void Answer(bool visible)
    {
        if (visible)
        {
            Host.Settings.FilterVisuallyConfirmedUtc = DateTimeOffset.UtcNow;
            Host.SaveSettings();
            Host.Filter.MarkVisuallyConfirmed(true);
            Host.Log.Info("User confirmed the filter is visible.");
            Verify = VerifyState.Confirmed;
        }
        else
        {
            Host.Log.Warn("User reported that the filter test was NOT visible.");
            Verify = VerifyState.NotVisible;
        }

        Refresh();
    }

    private void Export()
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export diagnostics",
            FileName = $"akeno-colorvision-diagnostics-{DateTime.Now:yyyyMMdd-HHmm}.txt",
            Filter = "Text file|*.txt",
        };
        if (dialog.ShowDialog() == true)
        {
            try
            {
                DiagnosticsBuilder.Export(Host, dialog.FileName);
                Message = "Diagnostics saved.";
            }
            catch (Exception ex)
            {
                Message = "Could not save: " + ex.Message;
            }
        }
    }

    private static void Open(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception)
        {
            // No browser available.
        }
    }
}
