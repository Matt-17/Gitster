using System.Windows;
using System.Windows.Controls;
using Microsoft.Xaml.Behaviors;

namespace Gitster.Behaviors;

/// <summary>
/// Behavior that selects all text in a TextBox when it receives focus.
/// </summary>
public class FocusBehavior : Behavior<TextBox>
{
    public static readonly DependencyProperty SelectOnFocusProperty =
        DependencyProperty.Register(
            nameof(SelectOnFocus),
            typeof(bool),
            typeof(FocusBehavior),
            new PropertyMetadata(false));

    public bool SelectOnFocus
    {
        get => (bool)GetValue(SelectOnFocusProperty);
        set => SetValue(SelectOnFocusProperty, value);
    }

    protected override void OnAttached()
    {
        base.OnAttached();
        if (AssociatedObject != null)
        {
            AssociatedObject.GotKeyboardFocus += OnGotKeyboardFocus;
            AssociatedObject.PreviewMouseLeftButtonDown += OnPreviewMouseLeftButtonDown;
        }
    }

    protected override void OnDetaching()
    {
        base.OnDetaching();
        if (AssociatedObject != null)
        {
            AssociatedObject.GotKeyboardFocus -= OnGotKeyboardFocus;
            AssociatedObject.PreviewMouseLeftButtonDown -= OnPreviewMouseLeftButtonDown;
        }
    }

    private void OnGotKeyboardFocus(object sender, RoutedEventArgs e)
    {
        if (SelectOnFocus && sender is TextBox textBox)
        {
            textBox.SelectAll();
        }
    }

    private void OnPreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (SelectOnFocus && sender is TextBox textBox && !textBox.IsKeyboardFocusWithin)
        {
            textBox.Focus();
            e.Handled = true;
        }
    }
}
