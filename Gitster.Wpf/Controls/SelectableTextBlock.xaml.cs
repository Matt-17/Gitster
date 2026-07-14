using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Gitster.Controls;

/// <summary>
///     Interaction logic for SelectableTextBloc.xaml
/// </summary>
public partial class SelectableTextBlock : UserControl
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(nameof(Text), typeof(string), typeof(SelectableTextBlock), new PropertyMetadata(default(string)));

    public static readonly DependencyProperty IsSelectedProperty = DependencyProperty.Register(nameof(IsSelected), typeof(bool), typeof(SelectableTextBlock), new PropertyMetadata(false));

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(nameof(Value), typeof(int), typeof(SelectableTextBlock), new PropertyMetadata(-1, ValuePropertyChanged));

    public SelectableTextBlock()
    {
        InitializeComponent();
        LayoutRoot.DataContext = this;
    }

    public bool IsSelected
    {
        get => (bool)GetValue(IsSelectedProperty);
        set => SetValue(IsSelectedProperty, value);
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public int Value
    {
        get => (int)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    private static void ValuePropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SelectableTextBlock control)
        {
            // Update IsSelected based on whether Value matches Text
            if (int.TryParse(control.Text, out int textValue))
            {
                control.IsSelected = textValue == (int)e.NewValue;
            }
        }
    }

    private void BorderOnMouseDown(object sender, MouseButtonEventArgs e) => OnMouseDown(e);
}