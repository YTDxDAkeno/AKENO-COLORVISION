using System.Windows;
using Akeno.ColorVision.Core.Settings;
using Akeno.ColorVision.Windows;

namespace Akeno.ColorVision.App.Services;

/// <summary>Switches between the dark and light color dictionaries at runtime.</summary>
public static class ThemeService
{
    private const string DarkUri = "pack://application:,,,/Themes/Colors.Dark.xaml";
    private const string LightUri = "pack://application:,,,/Themes/Colors.Light.xaml";

    public static AppTheme Current { get; private set; } = AppTheme.Dark;

    public static void Apply(AppTheme theme)
    {
        var dictionaries = Application.Current.Resources.MergedDictionaries;
        var existing = dictionaries.FirstOrDefault(d => d.Source is not null &&
            (d.Source.OriginalString.EndsWith("Colors.Dark.xaml", StringComparison.OrdinalIgnoreCase) ||
             d.Source.OriginalString.EndsWith("Colors.Light.xaml", StringComparison.OrdinalIgnoreCase)));
        var replacement = new ResourceDictionary { Source = new Uri(theme == AppTheme.Light ? LightUri : DarkUri) };
        if (existing is null)
        {
            dictionaries.Insert(0, replacement);
        }
        else
        {
            dictionaries[dictionaries.IndexOf(existing)] = replacement;
        }

        Current = theme;
        foreach (Window window in Application.Current.Windows)
        {
            ApplyTitleBar(window);
        }
    }

    public static void ApplyTitleBar(Window window)
    {
        var handle = new System.Windows.Interop.WindowInteropHelper(window).Handle;
        WindowEffects.SetDarkTitleBar(handle, Current == AppTheme.Dark);
    }
}
