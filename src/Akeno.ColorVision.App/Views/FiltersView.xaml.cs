using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Akeno.ColorVision.App.Views;

public partial class FiltersView : UserControl
{
    public FiltersView() => InitializeComponent();

    private void OnCompareDown(object sender, MouseButtonEventArgs e) => App.Host.Filter.BeginHoldCompare();

    private void OnCompareUp(object sender, MouseEventArgs e) => App.Host.Filter.EndHoldCompare();

    private void OnCompareKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space && !e.IsRepeat)
        {
            App.Host.Filter.BeginHoldCompare();
            e.Handled = true;
        }
    }

    private void OnCompareKeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space)
        {
            App.Host.Filter.EndHoldCompare();
            e.Handled = true;
        }
    }
}
