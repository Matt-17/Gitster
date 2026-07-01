using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

using Gitster.ViewModels;

namespace Gitster.Controls;

public partial class CommitListView : UserControl
{
    private CommitListViewModel? _vm;

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
