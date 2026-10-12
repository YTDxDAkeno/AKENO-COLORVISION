using Akeno.ColorVision.Core.Hotkeys;
using Akeno.ColorVision.Core.Profiles;
using Akeno.ColorVision.Core.Settings;

namespace Akeno.ColorVision.UnitTests;

public class HotkeyGestureTests
{
    [Theory]
    [InlineData("Ctrl + Alt + Shift + F", HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Shift, "F", 0x46)]
    [InlineData("ctrl+alt+shift+backspace", HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Shift, "Backspace", 0x08)]
    [InlineData("Win + Alt + F12", HotkeyModifiers.Windows | HotkeyModifiers.Alt, "F12", 0x7B)]
    [InlineData("Control+Shift+7", HotkeyModifiers.Control | HotkeyModifiers.Shift, "7", 0x37)]
    [InlineData("Alt+PgUp", HotkeyModifiers.Alt, "PageUp", 0x21)]
    public void Parses(string text, HotkeyModifiers mods, string key, int vk)
    {
        Assert.True(HotkeyGesture.TryParse(text, out var g, out var error), error);
        Assert.Equal(mods, g.Modifiers);
        Assert.Equal(key, g.Key);
        Assert.Equal(vk, g.VirtualKey);
    }

    [Fact]
    public void FormatsCanonically()
    {
        HotkeyGesture.TryParse("shift+ctrl+alt+f", out var g, out _);
        Assert.Equal("Ctrl + Alt + Shift + F", g!.ToString());
        Assert.True(HotkeyGesture.TryParse(g.ToString(), out var again, out _));
        Assert.Equal(g, again);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Ctrl + Alt")]
    [InlineData("Ctrl + A + B")]
    [InlineData("Ctrl + Banana")]
    public void RejectsInvalid(string text) => Assert.False(HotkeyGesture.TryParse(text, out _, out _));

    [Theory]
    [InlineData("F")]
    [InlineData("Shift + F")]
    public void RequiresRealModifier(string text)
    {
        Assert.True(HotkeyGesture.TryParse(text, out var g, out _));
        Assert.NotNull(g.Validate());
    }

    [Theory]
    [InlineData("Ctrl + Alt + Delete")]
    [InlineData("Alt + F4")]
    [InlineData("Win + Ctrl + C")]
    [InlineData("Ctrl + Alt + I")]
    public void RejectsReservedShortcuts(string text)
    {
        Assert.True(HotkeyGesture.TryParse(text, out var g, out _));
        Assert.NotNull(g.Validate());
    }

    [Fact]
    public void DefaultsAreValidAndDistinct()
    {
        var s = new HotkeySettings();
        var gestures = new[] { s.ToggleFilter, s.EmergencyReset, s.IdentifyColor }.Select(t =>
        {
            Assert.True(HotkeyGesture.TryParse(t, out var g, out _));
            Assert.Null(g.Validate());
            return g;
        }).ToList();
        Assert.Equal(3, gestures.Distinct().Count());
    }

    [Fact]
    public void NormalizeRestoresInvalidAndDuplicateShortcuts()
    {
        var s = new HotkeySettings { ToggleFilter = "nonsense", EmergencyReset = "Ctrl + Alt + Shift + C", IdentifyColor = "Ctrl + Alt + Shift + C" };
        var notes = s.Normalize();
        Assert.Equal(HotkeySettings.DefaultToggleFilter, s.ToggleFilter);
        Assert.Equal(HotkeySettings.DefaultEmergencyReset, s.EmergencyReset);
        Assert.NotEmpty(notes);
    }
}

public class SettingsTests
{
    [Fact]
    public void DefaultsAreSafe()
    {
        var s = new AppSettings();
        Assert.False(s.EnableFilterOnStartup);
        Assert.False(s.StartWithWindows);
        Assert.True(s.ErrorLoggingEnabled);
        Assert.Equal(BuiltInPresets.RedGreenId, s.ActiveProfileId);
        Assert.Equal(BuiltInPresets.MinecraftNetherId, s.Gaming.MinecraftProfileId);
    }

    [Fact]
    public void SavesAndLoads()
    {
        using var dir = new TempDir();
        var store = new SettingsStore(dir.Path);
        var s = new AppSettings { EnableFilterOnStartup = true, Theme = AppTheme.Light };
        s.PresetIntensity[BuiltInPresets.StrongId] = 0.5;
        store.Save(s);
        var loaded = store.Load(out var problem);
        Assert.Null(problem);
        Assert.True(loaded.EnableFilterOnStartup);
        Assert.Equal(AppTheme.Light, loaded.Theme);
        Assert.Equal(0.5, loaded.PresetIntensity[BuiltInPresets.StrongId]);
    }

    [Fact]
    public void MissingFileGivesDefaults()
    {
        using var dir = new TempDir();
        var loaded = new SettingsStore(dir.Path).Load(out var problem);
        Assert.Null(problem);
        Assert.False(loaded.EnableFilterOnStartup);
    }

    [Fact]
    public void CorruptFileIsSetAsideAndDefaultsUsed()
    {
        using var dir = new TempDir();
        File.WriteAllText(Path.Combine(dir.Path, SettingsStore.FileName), "{{{ nope");
        var loaded = new SettingsStore(dir.Path).Load(out var problem);
        Assert.NotNull(problem);
        Assert.False(loaded.EnableFilterOnStartup);
        Assert.Single(Directory.GetFiles(dir.Path, SettingsStore.FileName + ".corrupt-*"));
    }

    [Fact]
    public void NormalizeFixesBadValues()
    {
        var s = new AppSettings();
        s.Gaming.RestoreDelaySeconds = -5;
        s.PresetIntensity[Guid.NewGuid()] = 0.5;            // unknown preset id
        s.PresetIntensity[BuiltInPresets.RedGreenId] = 9;   // out of range
        s.Hotkeys.ToggleFilter = "Alt + F4";                // reserved
        s.Normalize();
        Assert.Equal(1.5, s.Gaming.RestoreDelaySeconds);
        Assert.Single(s.PresetIntensity);
        Assert.Equal(1.0, s.PresetIntensity[BuiltInPresets.RedGreenId]);
        Assert.Equal(HotkeySettings.DefaultToggleFilter, s.Hotkeys.ToggleFilter);
    }

    [Fact]
    public void NullSubObjectsAreRecreated()
    {
        using var dir = new TempDir();
        File.WriteAllText(Path.Combine(dir.Path, SettingsStore.FileName), """{"schemaVersion":1,"hotkeys":null,"gaming":null,"identifier":null}""");
        var loaded = new SettingsStore(dir.Path).Load(out var problem);
        Assert.Null(problem);
        Assert.NotNull(loaded.Hotkeys);
        Assert.NotNull(loaded.Gaming);
        Assert.NotNull(loaded.Identifier);
    }
}
