using Akeno.ColorVision.Core.Gaming;

namespace Akeno.ColorVision.UnitTests;

public class GameRuleTests
{
    private static readonly IReadOnlyList<GameRule> Rules = BuiltInGameRules.All;

    [Theory]
    [InlineData("javaw.exe", "Minecraft 1.21.4")]
    [InlineData("javaw.exe", "Minecraft* 1.21.4 - Singleplayer")]
    [InlineData("java.exe", "Minecraft 1.20.1 - Multiplayer (3rd-party Server)")]
    [InlineData("JAVAW.EXE", "minecraft 1.8.9")]
    public void DetectsMinecraftJava(string exe, string title) =>
        Assert.Equal(BuiltInGameRules.MinecraftJavaId, GameRuleMatcher.Match(new ForegroundAppInfo(exe, title, 1), Rules)?.Id);

    [Theory]
    [InlineData("javaw.exe", "Minecraft Launcher")]
    [InlineData("MinecraftLauncher.exe", "Minecraft Launcher")]
    [InlineData("javaw.exe", "IntelliJ IDEA")]
    [InlineData("chrome.exe", "Minecraft Wiki - Google Chrome")]
    [InlineData("", "Minecraft 1.21")]
    public void IgnoresNonGames(string exe, string title) =>
        Assert.Null(GameRuleMatcher.Match(new ForegroundAppInfo(exe, title, 1), Rules));

    [Fact]
    public void DetectsBedrock()
    {
        Assert.Equal(BuiltInGameRules.MinecraftBedrockId, GameRuleMatcher.Match(new ForegroundAppInfo("Minecraft.Windows.exe", "Minecraft", 1), Rules)?.Id);
        Assert.Equal(BuiltInGameRules.MinecraftBedrockUwpId, GameRuleMatcher.Match(new ForegroundAppInfo("ApplicationFrameHost.exe", "Minecraft", 1), Rules)?.Id);
        Assert.Null(GameRuleMatcher.Match(new ForegroundAppInfo("ApplicationFrameHost.exe", "Calculator", 1), Rules));
    }

    [Fact]
    public void DisabledRulesDoNotMatch()
    {
        var rule = BuiltInGameRules.MinecraftJava;
        rule.Enabled = false;
        Assert.Null(GameRuleMatcher.Match(new ForegroundAppInfo("javaw.exe", "Minecraft 1.21", 1), [rule]));
    }

    [Fact]
    public void UserRulesWinOverBuiltIns()
    {
        var custom = new GameRule { Name = "My Java game", ExecutableNames = ["javaw.exe"], ProfileId = Guid.NewGuid() };
        var match = GameRuleMatcher.Match(new ForegroundAppInfo("javaw.exe", "Minecraft 1.21", 1), [.. Rules, custom]);
        Assert.Same(custom, match);
    }
}

public class AutoProfileSwitcherTests
{
    private DateTimeOffset _now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private readonly Guid _everyday = Guid.NewGuid();
    private readonly Guid _game = Guid.NewGuid();
    private readonly GameRule _minecraft = BuiltInGameRules.MinecraftJava;

    private AutoProfileSwitcher Create() => new(() => _now) { RestoreDelay = TimeSpan.FromSeconds(1.5) };

    [Fact]
    public void ActivatesAndRestoresAfterDelay()
    {
        var s = Create();
        var a = s.OnForegroundChanged(_minecraft, _game, _everyday, filterOn: false, enableFilterInGame: true);
        Assert.Equal(SwitchActionKind.ActivateGameProfile, a.Kind);
        Assert.Equal(_game, a.ProfileId);
        Assert.True(a.FilterOn);
        Assert.True(s.InGameMode);

        Assert.Equal(SwitchActionKind.None, s.OnForegroundChanged(null, _game, _game, filterOn: true, enableFilterInGame: true).Kind);
        _now += TimeSpan.FromSeconds(1);
        Assert.Equal(SwitchActionKind.None, s.Tick().Kind);
        _now += TimeSpan.FromSeconds(1);
        var restore = s.Tick();
        Assert.Equal(SwitchActionKind.RestorePrevious, restore.Kind);
        Assert.Equal(_everyday, restore.ProfileId);
        Assert.False(restore.FilterOn);
        Assert.False(s.InGameMode);
    }

    [Fact]
    public void QuickAltTabDoesNotFlicker()
    {
        var s = Create();
        s.OnForegroundChanged(_minecraft, _game, _everyday, true, false);
        s.OnForegroundChanged(null, _game, _game, true, false);
        _now += TimeSpan.FromSeconds(0.5);
        // Back in the game before the delay ends: no switch, no restore.
        Assert.Equal(SwitchActionKind.None, s.OnForegroundChanged(_minecraft, _game, _game, true, false).Kind);
        _now += TimeSpan.FromSeconds(5);
        Assert.Equal(SwitchActionKind.None, s.Tick().Kind);
        Assert.True(s.InGameMode);
    }

    [Fact]
    public void DoesNotForceFilterOnUnlessAsked()
    {
        var s = Create();
        var a = s.OnForegroundChanged(_minecraft, _game, _everyday, false, enableFilterInGame: false);
        Assert.Null(a.FilterOn);
    }

    [Fact]
    public void UserOverrideCancelsRestore()
    {
        var s = Create();
        s.OnForegroundChanged(_minecraft, _game, _everyday, true, false);
        s.OnForegroundChanged(null, _game, _game, true, false);
        s.OnUserOverride();
        _now += TimeSpan.FromSeconds(10);
        Assert.Equal(SwitchActionKind.None, s.Tick().Kind);
        Assert.False(s.InGameMode);
    }

    [Fact]
    public void ResetRestoresImmediately()
    {
        var s = Create();
        s.OnForegroundChanged(_minecraft, _game, _everyday, true, false);
        var r = s.Reset();
        Assert.Equal(SwitchActionKind.RestorePrevious, r.Kind);
        Assert.Equal(_everyday, r.ProfileId);
        Assert.Equal(SwitchActionKind.None, s.Reset().Kind);
    }

    [Fact]
    public void NonGameForegroundWithoutGameModeDoesNothing()
    {
        var s = Create();
        Assert.Equal(SwitchActionKind.None, s.OnForegroundChanged(null, _game, _everyday, true, true).Kind);
        _now += TimeSpan.FromSeconds(10);
        Assert.Equal(SwitchActionKind.None, s.Tick().Kind);
    }
}
