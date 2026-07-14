using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace Gitster.Controls;

/// <summary>
/// The neutral toolbar (plan A6 layout): switch-repo dropdown (A4), branch dropdown (A11),
/// Fetch/Pull/Push-split/Sync (A3), auto-fetch toggle and ahead/behind.
/// </summary>
public partial class Toolbar : UserControl
{
    public Toolbar()
    {
        InitializeComponent();
    }

    private void ClosePopup(object sender, RoutedEventArgs e)
    {
        var popup = FindParent<Popup>(sender as DependencyObject);
        if (popup != null) popup.IsOpen = false;
    }

    private static T? FindParent<T>(DependencyObject? child) where T : DependencyObject
    {
        while (child != null)
        {
            if (child is T match) return match;
            child = VisualTreeHelper.GetParent(child) ?? LogicalTreeHelper.GetParent(child);
        }
        return null;
    }
}
