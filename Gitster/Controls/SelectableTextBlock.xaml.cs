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


    private void BorderOnMouseDown(object sender, MouseButtonEventArgs e) => OnMouseDown(e);
}