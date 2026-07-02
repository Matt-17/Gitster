using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Gitster.ViewModels;

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

    private void FileRow_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
            return;

        if (sender is FrameworkElement { DataContext: CommitFileViewModel file })
            DragDrop.DoDragDrop((DependencyObject)sender, file, DragDropEffects.Move);
    }

    private void FileList_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(CommitFileViewModel))
            ? DragDropEffects.Move
            : DragDropEffects.None;
        e.Handled = true;
    }

    private async void Staged_Drop(object sender, DragEventArgs e)
    {
        if (DataContext is CommitPanelViewModel vm
            && e.Data.GetData(typeof(CommitFileViewModel)) is CommitFileViewModel file)
        {
            await vm.SetFileStagedAsync(file.Path, staged: true);
        }
    }

    private async void Changes_Drop(object sender, DragEventArgs e)
    {
        if (DataContext is CommitPanelViewModel vm
            && e.Data.GetData(typeof(CommitFileViewModel)) is CommitFileViewModel file)
        {
            await vm.SetFileStagedAsync(file.Path, staged: false);
        }
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
