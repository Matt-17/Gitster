using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace Gitster.Controls;

public partial class CommitPanel : UserControl
{
    public CommitPanel()
    {
        InitializeComponent();
    }

    // Closes the parent popup after a dropdown action is chosen.
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
