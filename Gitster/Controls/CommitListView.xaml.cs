using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

using Gitster.ViewModels;

namespace Gitster.Controls;

public partial class CommitListView : UserControl
{
    private const string CommitDragFormat = "Gitster.CommitItem";
    private CommitListViewModel? _vm;
    private Point _dragStart;

    public CommitListView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_vm != null)
            _vm.FocusSearchRequested -= OnFocusSearchRequested;
        _vm = e.NewValue as CommitListViewModel;
        if (_vm != null)
            _vm.FocusSearchRequested += OnFocusSearchRequested;
    }

    private void OnFocusSearchRequested() => SearchBoxControl.FocusInput();

    private void CommitListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_vm == null) return;
        // Only real commits participate in multi-selection; headers are never selectable.
        _vm.SelectedCommits = CommitListBox.SelectedItems
            .OfType<CommitItem>()
            .ToList();

        if (_vm.SelectedCommit is not { } selectedCommit)
            return;

        CommitListBox.ScrollIntoView(selectedCommit);
        FocusSelectedCommitIfListHasFocus(selectedCommit);
    }

    private void FocusSelectedCommitIfListHasFocus(CommitItem selectedCommit)
    {
        if (!CommitListBox.IsKeyboardFocusWithin)
            return;

        CommitListBox.Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_vm?.SelectedCommit is not { } currentCommit || !CommitListBox.IsKeyboardFocusWithin)
                return;

            if (!string.Equals(currentCommit.FullSha, selectedCommit.FullSha, StringComparison.OrdinalIgnoreCase))
                return;

            CommitListBox.UpdateLayout();
            if (CommitListBox.ItemContainerGenerator.ContainerFromItem(currentCommit) is ListViewItem item)
                item.Focus();
        }), DispatcherPriority.Loaded);
    }

    private void CommitItem_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ListViewItem item || item.DataContext is not CommitItem commit)
            return;

        var preserveMultiSelection = item.IsSelected && CommitListBox.SelectedItems.Count > 1;
        if (!preserveMultiSelection)
        {
            CommitListBox.SelectedItems.Clear();
            item.IsSelected = true;
        }

        item.Focus();

        if (_vm is null)
            return;

        _vm.SelectedCommit = commit;
        _vm.SelectedCommits = CommitListBox.SelectedItems
            .OfType<CommitItem>()
            .ToList();
    }

    private void CommitItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(null);
    }

    private void CommitItem_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed
            || sender is not ListViewItem item
            || item.DataContext is not CommitItem commit)
        {
            return;
        }

        var current = e.GetPosition(null);
        if (Math.Abs(current.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(current.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var data = new DataObject();
        data.SetData(CommitDragFormat, commit);
        DragDrop.DoDragDrop(item, data, DragDropEffects.Move);
    }

    private void CommitItem_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = DragDropEffects.None;
        if (_vm is null
            || sender is not ListViewItem item
            || item.DataContext is not CommitItem target
            || e.Data.GetData(CommitDragFormat) is not CommitItem source)
        {
            e.Handled = true;
            return;
        }

        if (CommitListViewModel.CanDropCommitForFixup(source, target, out var reason))
        {
            e.Effects = DragDropEffects.Move;
            item.ToolTip = $"Fixup {source.CommitId} into {target.CommitId}";
        }
        else
        {
            item.ToolTip = reason;
        }

        e.Handled = true;
    }

    private void CommitItem_DragLeave(object sender, DragEventArgs e)
    {
        if (sender is ListViewItem item)
            item.ClearValue(ToolTipProperty);
    }

    private async void CommitItem_Drop(object sender, DragEventArgs e)
    {
        if (_vm is null
            || sender is not ListViewItem item
            || item.DataContext is not CommitItem target
            || e.Data.GetData(CommitDragFormat) is not CommitItem source)
        {
            return;
        }

        if (!CommitListViewModel.CanDropCommitForFixup(source, target, out _))
            return;

        item.ClearValue(ToolTipProperty);
        try
        {
            await _vm.DropCommitForFixupAsync(source, target);
        }
        catch (Exception ex)
        {
            // async void drop handler — an escaping exception would hit the dispatcher.
            System.Diagnostics.Debug.WriteLine($"Fixup drop failed: {ex}");
        }
    }
}

/// <summary>Picks the header vs. commit container style for the heterogeneous commit list.</summary>
public sealed class CommitContainerStyleSelector : StyleSelector
{
    public Style? CommitStyle { get; set; }
    public Style? HeaderStyle { get; set; }

    public override Style? SelectStyle(object item, DependencyObject container)
        => item is CommitSectionHeader or CommitSectionEmptyRow ? HeaderStyle : CommitStyle;
}

/// <summary>Picks the header vs. commit row template for the heterogeneous commit list.</summary>
public sealed class CommitRowTemplateSelector : DataTemplateSelector
{
    public DataTemplate? CommitTemplate { get; set; }
    public DataTemplate? HeaderTemplate { get; set; }
    public DataTemplate? EmptyRowTemplate { get; set; }

    public override DataTemplate? SelectTemplate(object item, DependencyObject container)
        => item switch
        {
            CommitSectionHeader => HeaderTemplate,
            CommitSectionEmptyRow => EmptyRowTemplate,
            _ => CommitTemplate
        };
}
