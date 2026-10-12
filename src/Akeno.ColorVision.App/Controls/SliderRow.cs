using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;

namespace Akeno.ColorVision.App.Controls;

/// <summary>
/// A labelled slider with the current value as text and optional hints for both ends.
/// Built in code so it needs no extra XAML resources.
/// </summary>
public sealed class SliderRow : UserControl
{
    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(nameof(Label), typeof(string), typeof(SliderRow), new PropertyMetadata(string.Empty, OnTextChanged));
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(nameof(Value), typeof(double), typeof(SliderRow),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnValueChanged));
    public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(nameof(Minimum), typeof(double), typeof(SliderRow), new PropertyMetadata(0.0, OnRangeChanged));
    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(nameof(Maximum), typeof(double), typeof(SliderRow), new PropertyMetadata(1.0, OnRangeChanged));
    public static readonly DependencyProperty StepProperty = DependencyProperty.Register(nameof(Step), typeof(double), typeof(SliderRow), new PropertyMetadata(0.05, OnRangeChanged));
    public static readonly DependencyProperty FormatProperty = DependencyProperty.Register(nameof(Format), typeof(string), typeof(SliderRow), new PropertyMetadata("0.00", OnValueChanged));
    public static readonly DependencyProperty LeftHintProperty = DependencyProperty.Register(nameof(LeftHint), typeof(string), typeof(SliderRow), new PropertyMetadata(string.Empty, OnTextChanged));
    public static readonly DependencyProperty RightHintProperty = DependencyProperty.Register(nameof(RightHint), typeof(string), typeof(SliderRow), new PropertyMetadata(string.Empty, OnTextChanged));
    public static readonly DependencyProperty HelpProperty = DependencyProperty.Register(nameof(Help), typeof(string), typeof(SliderRow), new PropertyMetadata(string.Empty, OnTextChanged));

    private readonly TextBlock _label = new() { FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _value = new() { FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Right };
    private readonly TextBlock _left = new() { FontSize = 13 };
    private readonly TextBlock _right = new() { FontSize = 13, HorizontalAlignment = HorizontalAlignment.Right };
    private readonly TextBlock _help = new() { FontSize = 13, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
    private readonly Slider _slider = new() { Margin = new Thickness(0, 4, 0, 0) };

    public SliderRow()
    {
        Margin = new Thickness(0, 0, 0, 18);
        _label.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        _value.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        foreach (var t in new[] { _left, _right, _help })
        {
            t.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSecondary");
        }

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(_label);
        Grid.SetColumn(_value, 1);
        header.Children.Add(_value);

        var hints = new Grid();
        hints.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        hints.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        hints.Children.Add(_left);
        Grid.SetColumn(_right, 1);
        hints.Children.Add(_right);

        var panel = new StackPanel();
        panel.Children.Add(header);
        panel.Children.Add(_slider);
        panel.Children.Add(hints);
        panel.Children.Add(_help);
        Content = panel;

        _slider.SetBinding(Slider.ValueProperty, new Binding(nameof(Value)) { Source = this, Mode = BindingMode.TwoWay });
        UpdateRange();
        UpdateText();
    }

    public string Label { get => (string)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }

    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }

    public double Minimum { get => (double)GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }

    public double Maximum { get => (double)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }

    public double Step { get => (double)GetValue(StepProperty); set => SetValue(StepProperty, value); }

    public string Format { get => (string)GetValue(FormatProperty); set => SetValue(FormatProperty, value); }

    public string LeftHint { get => (string)GetValue(LeftHintProperty); set => SetValue(LeftHintProperty, value); }

    public string RightHint { get => (string)GetValue(RightHintProperty); set => SetValue(RightHintProperty, value); }

    public string Help { get => (string)GetValue(HelpProperty); set => SetValue(HelpProperty, value); }

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((SliderRow)d).UpdateText();

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((SliderRow)d).UpdateValueText();

    private static void OnRangeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((SliderRow)d).UpdateRange();

    private void UpdateRange()
    {
        _slider.Minimum = Minimum;
        _slider.Maximum = Maximum;
        _slider.SmallChange = Step;
        _slider.LargeChange = Step * 4;
        _slider.TickFrequency = Step;
        _slider.IsSnapToTickEnabled = true;
    }

    private void UpdateText()
    {
        _label.Text = Label;
        _left.Text = LeftHint;
        _right.Text = RightHint;
        _help.Text = Help;
        _help.Visibility = string.IsNullOrEmpty(Help) ? Visibility.Collapsed : Visibility.Visible;
        AutomationProperties.SetName(_slider, Label);
        AutomationProperties.SetHelpText(_slider, Help);
        UpdateValueText();
    }

    private void UpdateValueText() =>
        _value.Text = Value.ToString(Format, CultureInfo.InvariantCulture);
}
