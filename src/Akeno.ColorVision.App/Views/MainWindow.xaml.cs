using System.ComponentModel;
using System.Windows;
using Akeno.ColorVision.App.Services;
using Akeno.ColorVision.App.ViewModels;

namespace Akeno.ColorVision.App.Views;

public partial class MainWindow : Window
{
    private bool _allowClose;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        DataContext = viewModel;
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.CurrentPage))
            {
                PageScroller.ScrollToTop();
            }
        };
    }

    public MainViewModel ViewModel { get; }

    public void AllowRealClose() => _allowClose = true;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ThemeService.ApplyTitleBar(this);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (_allowClose)
        {
            return;
        }

        // Closing the window keeps the filter running in the tray unless the user chose otherwise.
        if (App.Host.Settings.CloseToTray)
        {
            e.Cancel = true;
            ViewModel.CurrentPage.OnNavigatedFrom();
            Hide();
            App.Host.Tray?.ShowRunningInTrayHint();
            return;
        }

        _allowClose = true;
        Dispatcher.BeginInvoke(() => ((App)Application.Current).ExitApplication());
    }
}
