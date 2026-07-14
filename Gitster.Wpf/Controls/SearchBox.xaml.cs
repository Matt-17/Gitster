using System.Windows;
using System.Windows.Controls;

namespace Gitster.Controls;

/// <summary>
/// The single rounded search bar used in every mode (plan A12): leading search icon,
/// placeholder text, and an optional trailing action via <see cref="TrailingContent"/>.
/// </summary>
public partial class SearchBox : UserControl
{
    public SearchBox()
    {
        InitializeComponent();
    }

    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(nameof(Text), typeof(string), typeof(SearchBox),
            new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public static readonly DependencyProperty PlaceholderProperty =
        DependencyProperty.Register(nameof(Placeholder), typeof(string), typeof(SearchBox),
            new PropertyMetadata("Search"));

    public string Placeholder
    {
        get => (string)GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    public static readonly DependencyProperty TrailingContentProperty =
        DependencyProperty.Register(nameof(TrailingContent), typeof(object), typeof(SearchBox),
            new PropertyMetadata(null));

    public object? TrailingContent
    {
        get => GetValue(TrailingContentProperty);
        set => SetValue(TrailingContentProperty, value);
    }

    /// <summary>Focuses the inner text box (used by the Ctrl+F focus-search command).</summary>
    public void FocusInput() => Input.Focus();
}
