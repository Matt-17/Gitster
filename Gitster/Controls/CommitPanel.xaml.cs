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
        => await DropFileAsync(e, staged: true);

    private async void Changes_Drop(object sender, DragEventArgs e)
        => await DropFileAsync(e, staged: false);

    private async Task DropFileAsync(DragEventArgs e, bool staged)
    {
        if (DataContext is not CommitPanelViewModel vm
            || e.Data.GetData(typeof(CommitFileViewModel)) is not CommitFileViewModel file)
        {
            return;
        }

        try
        {
            await vm.SetFileStagedAsync(file.Path, staged);
        }
        catch (Exception ex)
        {
            // async void drop handler — an escaping exception would hit the dispatcher.
            System.Diagnostics.Debug.WriteLine($"Stage/unstage drop failed: {ex}");
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
