using System.Globalization;
using System.Text;

namespace Akeno.ColorVision.Core.Diagnostics;

public enum LogLevel
{
    Info,
    Warning,
    Error,
}

/// <summary>Minimal logging abstraction (no third-party dependency).</summary>
public interface IAppLogger
{
    void Log(LogLevel level, string message, Exception? exception = null);
}

public static class AppLoggerExtensions
{
    public static void Info(this IAppLogger log, string message) => log.Log(LogLevel.Info, message);

    public static void Warn(this IAppLogger log, string message, Exception? ex = null) => log.Log(LogLevel.Warning, message, ex);

    public static void Error(this IAppLogger log, string message, Exception? ex = null) => log.Log(LogLevel.Error, message, ex);
}

public sealed class NullLogger : IAppLogger
{
    public static NullLogger Instance { get; } = new();

    public void Log(LogLevel level, string message, Exception? exception = null)
    {
    }
}

/// <summary>Keeps the most recent entries in memory (used by the diagnostics page and by tests).</summary>
public sealed class MemoryLogger : IAppLogger
{
    private readonly object _gate = new();
    private readonly Queue<string> _entries = new();
    private readonly int _capacity;

    public MemoryLogger(int capacity = 200) => _capacity = capacity;

    public void Log(LogLevel level, string message, Exception? exception = null)
    {
        var line = FileLogger.Format(DateTimeOffset.Now, level, message, exception);
        lock (_gate)
        {
            _entries.Enqueue(line);
            while (_entries.Count > _capacity)
            {
                _entries.Dequeue();
            }
        }
    }

    public IReadOnlyList<string> Snapshot()
    {
        lock (_gate)
        {
            return [.. _entries];
        }
    }
}

/// <summary>Sends every entry to several loggers.</summary>
public sealed class CompositeLogger(params IAppLogger[] loggers) : IAppLogger
{
    public void Log(LogLevel level, string message, Exception? exception = null)
    {
        foreach (var logger in loggers)
        {
            try
            {
                logger.Log(level, message, exception);
            }
            catch (Exception)
            {
                // Logging must never break the application.
            }
        }
    }
}

/// <summary>
/// Local, daily-rotated text log. Never uploads anything. Messages are passed through
/// <see cref="PrivacySanitizer"/> so user names in paths do not end up in log files.
/// </summary>
public sealed class FileLogger : IAppLogger
{
    private readonly object _gate = new();
    private readonly string _directory;
    private readonly int _retainDays;

    public FileLogger(string directory, int retainDays = 7)
    {
        _directory = directory;
        _retainDays = retainDays;
    }

    /// <summary>When false nothing is written (user setting "Error logging").</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Lowest level written to disk.</summary>
    public LogLevel MinimumLevel { get; set; } = LogLevel.Info;

    public string Directory => _directory;

    public void Log(LogLevel level, string message, Exception? exception = null)
    {
        if (!Enabled || level < MinimumLevel)
        {
            return;
        }

        try
        {
            var line = Format(DateTimeOffset.Now, level, message, exception);
            lock (_gate)
            {
                System.IO.Directory.CreateDirectory(_directory);
                var file = Path.Combine(_directory, $"akeno-colorvision-{DateTime.Now:yyyyMMdd}.log");
                File.AppendAllText(file, line + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch (Exception)
        {
            // Never let logging failures affect the app.
        }
    }

    /// <summary>Deletes log files older than the retention period.</summary>
    public void Prune()
    {
        try
        {
            if (!System.IO.Directory.Exists(_directory))
            {
                return;
            }

            foreach (var file in System.IO.Directory.EnumerateFiles(_directory, "akeno-colorvision-*.log"))
            {
                if (File.GetLastWriteTimeUtc(file) < DateTime.UtcNow.AddDays(-_retainDays))
                {
                    File.Delete(file);
                }
            }
        }
        catch (Exception)
        {
            // Best effort.
        }
    }

    internal static string Format(DateTimeOffset time, LogLevel level, string message, Exception? exception)
    {
        var text = string.Create(CultureInfo.InvariantCulture, $"{time:yyyy-MM-dd HH:mm:ss.fff} [{level.ToString().ToUpperInvariant()}] {message}");
        if (exception is not null)
        {
            text += $" | {exception.GetType().Name}: {exception.Message}";
        }

        return PrivacySanitizer.Sanitize(text);
    }
}

/// <summary>Removes personal information (user names in paths) from text before it is logged or exported.</summary>
public static class PrivacySanitizer
{
    public static string Sanitize(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        var result = text;
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(profile) && profile.Length > 3)
        {
            result = result.Replace(profile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
        }

        var user = Environment.UserName;
        if (!string.IsNullOrEmpty(user) && user.Length >= 3)
        {
            result = result.Replace(@"\" + user + @"\", @"\<user>\", StringComparison.OrdinalIgnoreCase)
                           .Replace("/" + user + "/", "/<user>/", StringComparison.OrdinalIgnoreCase);
        }

        var machine = Environment.MachineName;
        if (!string.IsNullOrEmpty(machine) && machine.Length >= 3)
        {
            result = result.Replace(machine, "<computer>", StringComparison.OrdinalIgnoreCase);
        }

        return result;
    }
}
