namespace Akeno.ColorVision.Core.Gaming;

/// <summary>
/// What we know about the foreground window: only the executable file name and the window title.
/// Obtained with documented, non-invasive Windows APIs (no process memory access, no injection).
/// </summary>
public sealed record ForegroundAppInfo(string ExecutableName, string WindowTitle, int ProcessId)
{
    public static ForegroundAppInfo Empty { get; } = new(string.Empty, string.Empty, 0);
}

/// <summary>A rule that recognises a game (or any app) and the profile to use while it is in front.</summary>
public sealed class GameRule
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    /// <summary>Executable file names, e.g. "javaw.exe" (case-insensitive).</summary>
    public List<string> ExecutableNames { get; set; } = [];

    /// <summary>Optional: the window title must start with this text.</summary>
    public string? TitleStartsWith { get; set; }

    /// <summary>Optional: the window title must contain this text.</summary>
    public string? TitleContains { get; set; }

    /// <summary>Window titles containing any of these are ignored (e.g. launchers).</summary>
    public List<string> TitleExcludes { get; set; } = [];

    /// <summary>Profile to activate; null uses the Minecraft preset.</summary>
    public Guid? ProfileId { get; set; }

    public bool Enabled { get; set; } = true;

    public bool BuiltIn { get; set; }

    public bool Matches(ForegroundAppInfo app)
    {
        if (!Enabled || string.IsNullOrEmpty(app.ExecutableName))
        {
            return false;
        }

        if (!ExecutableNames.Any(e => string.Equals(e, app.ExecutableName, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        var title = app.WindowTitle ?? string.Empty;
        if (!string.IsNullOrEmpty(TitleStartsWith) && !title.StartsWith(TitleStartsWith, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrEmpty(TitleContains) && !title.Contains(TitleContains, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !TitleExcludes.Any(x => !string.IsNullOrEmpty(x) && title.Contains(x, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>Rules shipped with the app. Minecraft detection only uses the executable name and window title.</summary>
public static class BuiltInGameRules
{
    public static readonly Guid MinecraftJavaId = new("3c9e1f60-1b2a-4c55-8e0d-6a7b8c9d0e01");
    public static readonly Guid MinecraftBedrockId = new("3c9e1f60-1b2a-4c55-8e0d-6a7b8c9d0e02");
    public static readonly Guid MinecraftBedrockUwpId = new("3c9e1f60-1b2a-4c55-8e0d-6a7b8c9d0e03");

    /// <summary>
    /// Minecraft: Java Edition runs inside a Java process (javaw.exe / java.exe) whose game window title
    /// starts with "Minecraft" (e.g. "Minecraft 1.21.4", "Minecraft* 1.21 - Singleplayer").
    /// The official launcher window ("Minecraft Launcher") is excluded.
    /// </summary>
    public static GameRule MinecraftJava => new()
    {
        Id = MinecraftJavaId,
        Name = "Minecraft: Java Edition",
        ExecutableNames = ["javaw.exe", "java.exe"],
        TitleStartsWith = "Minecraft",
        TitleExcludes = ["Launcher"],
        BuiltIn = true,
    };

    /// <summary>Minecraft for Windows (Bedrock). Newer releases run as Minecraft.Windows.exe.</summary>
    public static GameRule MinecraftBedrock => new()
    {
        Id = MinecraftBedrockId,
        Name = "Minecraft for Windows (Bedrock)",
        ExecutableNames = ["Minecraft.Windows.exe"],
        BuiltIn = true,
    };

    /// <summary>Older Store (UWP) builds of Bedrock are hosted by ApplicationFrameHost with the title "Minecraft".</summary>
    public static GameRule MinecraftBedrockUwp => new()
    {
        Id = MinecraftBedrockUwpId,
        Name = "Minecraft for Windows (Bedrock, Store app window)",
        ExecutableNames = ["ApplicationFrameHost.exe"],
        TitleStartsWith = "Minecraft",
        TitleExcludes = ["Launcher", "Education"],
        BuiltIn = true,
    };

    public static IReadOnlyList<GameRule> All => [MinecraftJava, MinecraftBedrock, MinecraftBedrockUwp];
}

public static class GameRuleMatcher
{
    /// <summary>First enabled rule that matches, user rules first so they can override built-ins.</summary>
    public static GameRule? Match(ForegroundAppInfo app, IEnumerable<GameRule> rules)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(rules);
        return rules.OrderBy(r => r.BuiltIn).FirstOrDefault(r => r.Matches(app));
    }
}
