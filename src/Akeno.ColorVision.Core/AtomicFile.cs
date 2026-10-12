using System.Text;

namespace Akeno.ColorVision.Core;

/// <summary>Crash-safe file writes: write to a temporary file, then atomically replace the target.</summary>
public static class AtomicFile
{
    public static void WriteAllText(string path, string contents)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temp = path + ".tmp-" + Guid.NewGuid().ToString("N")[..8];
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                writer.Write(contents);
            }

            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
            {
                try
                {
                    File.Delete(temp);
                }
                catch (IOException)
                {
                    // Best effort cleanup.
                }
            }
        }
    }

    /// <summary>Moves a damaged file aside (keeps it for inspection) and returns the new path.</summary>
    public static string? Quarantine(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var target = $"{path}.corrupt-{DateTime.UtcNow:yyyyMMddHHmmss}";
            File.Move(path, target, overwrite: true);
            return target;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}
