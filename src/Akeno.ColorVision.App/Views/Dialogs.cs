using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Akeno.ColorVision.App.Services;

namespace Akeno.ColorVision.App.Views;

/// <summary>Small, theme-aware dialogs built in code.</summary>
public static class Dialogs
{
    /// <summary>Asks for a line of text. Returns null when cancelled.</summary>
    public static string? Prompt(string title, string message, string initial)
    {
        var box = new TextBox { Text = initial, Margin = new Thickness(0, 12, 0, 16), MaxLength = 60 };
        string? result = null;
        var window = CreateWindow(title);
        var ok = new Button { Content = "OK", IsDefault = true, MinWidth = 96, Margin = new Thickness(0, 0, 8, 0), Style = (Style)Application.Current.FindResource("AccentButton") };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 96 };
        ok.Click += (_, _) =>
        {
            result = box.Text;
            window.DialogResult = true;
        };
        window.Content = Layout(message, box, ok, cancel);
        window.Loaded += (_, _) =>
        {
            box.Focus();
            box.SelectAll();
        };
        return window.ShowDialog() == true ? result : null;
    }

    public static bool Confirm(string title, string message, string confirmText = "Yes", string cancelText = "Cancel")
    {
        var window = CreateWindow(title);
        var ok = new Button { Content = confirmText, IsDefault = true, MinWidth = 96, Margin = new Thickness(0, 0, 8, 0), Style = (Style)Application.Current.FindResource("AccentButton") };
        var cancel = new Button { Content = cancelText, IsCancel = true, MinWidth = 96 };
        ok.Click += (_, _) => window.DialogResult = true;
        window.Content = Layout(message, null, ok, cancel);
        return window.ShowDialog() == true;
    }

    public static void Info(string title, string message)
    {
        var window = CreateWindow(title);
        var ok = new Button { Content = "OK", IsDefault = true, IsCancel = true, MinWidth = 96, Style = (Style)Application.Current.FindResource("AccentButton") };
        ok.Click += (_, _) => window.DialogResult = true;
        window.Content = Layout(message, null, ok, null);
        window.ShowDialog();
    }

    /// <summary>
    /// Safety countdown after applying unfamiliar settings: reverts automatically unless the user
    /// clicks "Keep" — like the Windows display-resolution confirmation.
    /// </summary>
    public static bool KeepChanges(string message, int seconds = 15)
    {
        var window = CreateWindow("Keep these colors?");
        var remaining = seconds;
        var countdown = new TextBlock { Margin = new Thickness(0, 10, 0, 16), FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
        void UpdateText() => countdown.Text = $"Going back automatically in {remaining} seconds.";
        UpdateText();
        var keep = new Button { Content = "Keep", IsDefault = true, MinWidth = 96, Margin = new Thickness(0, 0, 8, 0), Style = (Style)Application.Current.FindResource("AccentButton") };
        var revert = new Button { Content = "Go back", IsCancel = true, MinWidth = 96 };
        keep.Click += (_, _) => window.DialogResult = true;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) =>
        {
            remaining--;
            UpdateText();
            if (remaining <= 0)
            {
                timer.Stop();
                if (window.IsVisible)
                {
                    window.DialogResult = false;
                }
            }
        };
        window.Content = Layout(message, countdown, keep, revert);
        window.Loaded += (_, _) => timer.Start();
        var result = window.ShowDialog() == true;
        timer.Stop();
        return result;
    }

    private static Window CreateWindow(string title)
    {
        var window = new Window
        {
            Title = title,
            Width = 460,
            SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            FontSize = 15,
        };
        window.SetResourceReference(Control.BackgroundProperty, "Brush.Surface");
        window.SetResourceReference(Control.ForegroundProperty, "Brush.Text");
        var owner = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? Application.Current.MainWindow;
        if (owner is not null && owner.IsVisible && !ReferenceEquals(owner, window))
        {
            window.Owner = owner;
        }
        else
        {
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        window.SourceInitialized += (_, _) => ThemeService.ApplyTitleBar(window);
        return window;
    }

    private static UIElement Layout(string message, UIElement? middle, Button primary, Button? secondary)
    {
        var panel = new StackPanel { Margin = new Thickness(24) };
        var text = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, LineHeight = 22 };
        text.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        panel.Children.Add(text);
        if (middle is not null)
        {
            if (middle is TextBlock tb)
            {
                tb.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
            }

            panel.Children.Add(middle);
        }
        else
        {
            panel.Children.Add(new Border { Height = 16 });
        }

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(primary);
        if (secondary is not null)
        {
            buttons.Children.Add(secondary);
        }

        panel.Children.Add(buttons);
        return panel;
    }
}
