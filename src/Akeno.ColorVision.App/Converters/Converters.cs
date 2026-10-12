using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Akeno.ColorVision.App.Imaging;
using Akeno.ColorVision.ColorScience;

namespace Akeno.ColorVision.App.Converters;

/// <summary>true → Visible, false → Collapsed. Parameter "invert" reverses it.</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var flag = value is true;
        if (parameter is string s && s.Equals("invert", StringComparison.OrdinalIgnoreCase))
        {
            flag = !flag;
        }

        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is Visibility.Visible;
}

/// <summary>null/empty → Collapsed, otherwise Visible.</summary>
public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        var hasValue = value switch
        {
            null => false,
            string s => s.Length > 0,
            System.Collections.ICollection c => c.Count > 0,
            _ => true,
        };
        if (parameter is string p && p.Equals("invert", StringComparison.OrdinalIgnoreCase))
        {
            hasValue = !hasValue;
        }

        return hasValue ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;
}

/// <summary>Rgb → SolidColorBrush.</summary>
public sealed class RgbToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is Rgb rgb ? rgb.ToBrush() : Brushes.Transparent;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Formats 0..1 as a percentage ("70 %").</summary>
public sealed class PercentConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is double d ? string.Format(CultureInfo.InvariantCulture, "{0:0} %", d * 100) : string.Empty;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Compares a value with the converter parameter (for radio-button style bindings).</summary>
public sealed class EqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.OrdinalIgnoreCase);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is true && parameter is string s)
        {
            if (targetType.IsEnum)
            {
                return Enum.Parse(targetType, s);
            }

            return s;
        }

        return Binding.DoNothing;
    }
}
