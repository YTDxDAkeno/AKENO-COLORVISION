using System.Globalization;
using System.Text;

namespace Akeno.ColorVision.Core.Diagnostics;

/// <summary>A named group of key/value facts for the diagnostics report.</summary>
public sealed record DiagnosticsSection(string Title, IReadOnlyList<KeyValuePair<string, string>> Items);

/// <summary>
/// Builds the plain-text diagnostics report the user can export when asking for help.
/// Contains technical facts only (versions, backend status, display configuration flags) —
/// no screenshots, no window titles, no calibration answers, no user names.
/// </summary>
public static class DiagnosticsReport
{
    public static string Build(string appVersion, IEnumerable<DiagnosticsSection> sections, IEnumerable<string>? recentLog = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Akeno ColorVision — diagnostics report");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Generated: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"App version: {appVersion}");
        sb.AppendLine("This report contains no screenshots, window titles, calibration answers or personal names.");
        sb.AppendLine();

        foreach (var section in sections)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"== {section.Title} ==");
            foreach (var (key, value) in section.Items)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"{key}: {value}");
            }

            sb.AppendLine();
        }

        if (recentLog is not null)
        {
            sb.AppendLine("== Recent log entries ==");
            foreach (var line in recentLog.TakeLast(60))
            {
                sb.AppendLine(line);
            }
        }

        return PrivacySanitizer.Sanitize(sb.ToString());
    }
}
