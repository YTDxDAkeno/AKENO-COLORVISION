using System.IO;
using Akeno.ColorVision.Core.Diagnostics;
using Akeno.ColorVision.Core.Filtering;
using Akeno.ColorVision.Rendering;
using Akeno.ColorVision.Windows;

namespace Akeno.ColorVision.App.Services;

/// <summary>Collects technical facts for the diagnostics page and export. No personal data.</summary>
public static class DiagnosticsBuilder
{
    public static IReadOnlyList<DiagnosticsSection> Build(AppHost host)
    {
        var sections = new List<DiagnosticsSection>();
        var caps = host.Filter.Capabilities;
        var gpu = BackendCatalog.GpuCaptureCapabilities();
        sections.Add(new DiagnosticsSection("Color filter", Items(
            ("Filter state", host.Filter.State.ToString()),
            ("Active backend", caps.DisplayName),
            ("Backend status", $"{BackendCapabilities.Describe(caps.Level)} — {caps.Summary}"),
            ("Last error", host.Filter.LastError ?? "none"),
            ("Other color effect active at start", host.Filter.ForeignEffectDetected ? "yes" : "no"),
            ("Other program changed colors since", host.Filter.ForeignChangeDetected ? "yes" : "no"),
            ("Visually confirmed by user", host.Settings.FilterVisuallyConfirmedUtc is { } t ? t.ToLocalTime().ToString("g") : "not yet"),
            ("GPU capture backend", $"{BackendCapabilities.Describe(gpu.Level)} — {gpu.Summary}"))));

        DisplayEnvironment? env = null;
        try
        {
            env = EnvironmentInspector.Inspect();
        }
        catch (Exception)
        {
            // Reported as unknown below.
        }

        var monitorText = env is null || env.Monitors.Count == 0
            ? "unknown"
            : string.Join("; ", env.Monitors.Select(m => $"{m.Width}×{m.Height} at {m.ScalePercent} %{(m.IsPrimary ? " (primary)" : string.Empty)}"));
        sections.Add(new DiagnosticsSection("System", Items(
            ("Windows", env?.OsDescription ?? Environment.OSVersion.VersionString),
            ("Windows build", env?.OsBuild.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unknown"),
            ("64-bit process / OS", $"{Environment.Is64BitProcess} / {Environment.Is64BitOperatingSystem}"),
            ("Monitors", monitorText),
            ("HDR / advanced color", Describe(env?.AdvancedColorEnabled)),
            ("Windows color filters", env?.WindowsColorFiltersActive switch
            {
                true => "ON (" + (env.WindowsColorFilterName ?? "unknown type") + ")",
                false => "off",
                _ => "unknown",
            }),
            ("Windows Magnifier running", env?.MagnifierRunning == true ? "yes" : "no"))));

        sections.Add(new DiagnosticsSection("App", Items(
            ("Version", AppHost.Version),
            ("Safe mode this session", host.Session.SafeMode ? host.Session.Reasons.ToString() : "no"),
            ("Crash watchdog", host.WatchdogRunning ? "running" : WatchdogLauncher.RecoveryToolPath is null ? "missing (emergency reset tool not found next to the app)" : "not running"),
            ("Emergency reset tool", WatchdogLauncher.RecoveryToolPath is null ? "not found" : "present"),
            ("Shortcuts", string.Join("; ", host.Hotkeys.Results.Select(r => $"{r.Key}: {(r.Value.Success ? "OK" : r.Value.Message)}"))),
            ("Game detection", host.Games.IsRunning ? "running" : "off"),
            ("Profiles", $"{host.Profiles.Profiles.Count} ({host.Profiles.Profiles.Count(p => !p.IsBuiltIn)} custom)"),
            ("Identifier sampling", host.Settings.Identifier.MeasuredSamplingSource.ToString()),
            ("Data folder", host.DataDirectory))));
        return sections;
    }

    public static string BuildReport(AppHost host) =>
        DiagnosticsReport.Build(AppHost.Version, Build(host), host.MemoryLog.Snapshot());

    public static void Export(AppHost host, string path) => File.WriteAllText(path, BuildReport(host));

    private static string Describe(bool? value) => value switch
    {
        true => "ON",
        false => "off",
        _ => "unknown",
    };

    private static List<KeyValuePair<string, string>> Items(params (string Key, string Value)[] items) =>
        items.Select(i => new KeyValuePair<string, string>(i.Key, i.Value)).ToList();
}
