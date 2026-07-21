using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

using Gitster.ViewModels;

namespace Gitster.Views.Modes;

public partial class CommitsModeView : UserControl
{
    public CommitsModeView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Double-click in the refs pane checks out the branch. Preview, because the row's own
    /// button swallows the bubbling mouse events.
    /// </summary>
    private void RefTree_PreviewMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left
            || DataContext is not MainWindowViewModel vm
            || FindNode(e.OriginalSource as DependencyObject) is not { } node)
        {
            return;
        }

        if (!vm.CommitRefNavigatorVM.CheckoutNodeCommand.CanExecute(node))
            return;

        vm.CommitRefNavigatorVM.CheckoutNodeCommand.Execute(node);
        e.Handled = true;
    }

    /// <summary>Walks up from the clicked element to the row whose data is a ref node.</summary>
    private static CommitRefNode? FindNode(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is FrameworkElement { DataContext: CommitRefNode node })
                return node;

            source = VisualTreeHelper.GetParent(source);
        }

        return null;
    }
}
