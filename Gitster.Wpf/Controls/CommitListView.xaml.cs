using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

using Gitster.ViewModels;

namespace Gitster.Controls;

public partial class CommitListView : UserControl
{
    private const string CommitDragFormat = "Gitster.CommitItem";
    private CommitListViewModel? _vm;
    private Point _dragStart;
    private ListViewItem? _dragSource;

    public CommitListView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        CommitListBox.AddHandler(
            PreviewMouseLeftButtonUpEvent,
            new MouseButtonEventHandler((_, _) => _dragSource = null),
            handledEventsToo: true);
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

    private void SectionEmptyRow_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Let clicks on the action links through; anything else on a placeholder row
        // must not steal the selection from the current commit.
        if (e.OriginalSource is DependencyObject source && FindAncestorButton(source) is not null)
            return;

        e.Handled = true;
    }

    private static System.Windows.Controls.Primitives.ButtonBase? FindAncestorButton(DependencyObject? node)
    {
        while (node is not null and not ListViewItem)
        {
            if (node is System.Windows.Controls.Primitives.ButtonBase button)
                return button;

            node = node is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D
                ? System.Windows.Media.VisualTreeHelper.GetParent(node)
                : LogicalTreeHelper.GetParent(node);
        }

        return null;
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
        _dragSource = sender as ListViewItem;
    }

    private void CommitItem_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            _dragSource = null;
            return;
        }

        // WPF raises a move at the press position before the button-down event itself. Without a press
        // that started on this row, _dragStart is stale and that move would start a drag that swallows the click.
        if (sender is not ListViewItem item
            || !ReferenceEquals(item, _dragSource)
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

        // Dragging a row of a multi-selection moves the whole selection (reorder); fixup
        // always uses the dragged row alone.
        var group = item.IsSelected && CommitListBox.SelectedItems.Count > 1
            ? CommitListBox.SelectedItems.OfType<CommitItem>().ToList()
            : [commit];

        var data = new DataObject();
        data.SetData(CommitDragFormat, commit);
        data.SetData(CommitGroupDragFormat, group);
        _dragSource = null;
        DragDrop.DoDragDrop(item, data, DragDropEffects.Move);
        ClearInsertionMarker();
    }

    private const string CommitGroupDragFormat = "Gitster.CommitItems";

    /// <summary>The outer bands of a row insert above/below it; the middle means fixup.</summary>
    private enum DropZone { Above, Fixup, Below }

    private static DropZone GetDropZone(ListViewItem item, DragEventArgs e)
    {
        var y = e.GetPosition(item).Y;
        var band = item.ActualHeight * 0.3;
        return y < band ? DropZone.Above
            : y > item.ActualHeight - band ? DropZone.Below
            : DropZone.Fixup;
    }

    private void CommitItem_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = DragDropEffects.None;
        if (_vm is null
            || sender is not ListViewItem item
            || item.DataContext is not CommitItem target
            || e.Data.GetData(CommitDragFormat) is not CommitItem source)
        {
            ClearInsertionMarker();
            e.Handled = true;
            return;
        }

        var zone = GetDropZone(item, e);
        if (zone == DropZone.Fixup)
        {
            ClearInsertionMarker();
            if (_vm.HasPendingReorder)
            {
                item.ToolTip = "Apply or discard the pending reorder before a fixup.";
            }
            else if (CommitListViewModel.CanDropCommitForFixup(source, target, out var reason))
            {
                e.Effects = DragDropEffects.Move;
                item.ToolTip = $"Fixup {source.CommitId} into {target.CommitId}";
            }
            else
            {
                item.ToolTip = reason;
            }
        }
        else
        {
            var group = DraggedGroup(e, source);
            var after = zone == DropZone.Below;
            if (_vm.CanMoveCommitsTo(group, target, after) is { } reason)
            {
                ClearInsertionMarker();
                item.ToolTip = reason;
            }
            else
            {
                e.Effects = DragDropEffects.Move;
                ShowInsertionMarker(item, after);
                var what = group.Count == 1 ? source.CommitId : $"{group.Count} commits";
                item.ToolTip = $"Move {what} {(after ? "below" : "above")} {target.CommitId}";
            }
        }

        e.Handled = true;
    }

    private static List<CommitItem> DraggedGroup(DragEventArgs e, CommitItem source)
        => e.Data.GetData(CommitGroupDragFormat) as List<CommitItem> ?? [source];

    private void CommitItem_DragLeave(object sender, DragEventArgs e)
    {
        if (sender is ListViewItem item)
            item.ClearValue(ToolTipProperty);
        ClearInsertionMarker();
    }

    private async void CommitItem_Drop(object sender, DragEventArgs e)
    {
        ClearInsertionMarker();
        if (_vm is null
            || sender is not ListViewItem item
            || item.DataContext is not CommitItem target
            || e.Data.GetData(CommitDragFormat) is not CommitItem source)
        {
            return;
        }

        item.ClearValue(ToolTipProperty);
        var zone = GetDropZone(item, e);
        if (zone != DropZone.Fixup)
        {
            _vm.MoveCommitsTo(DraggedGroup(e, source), target, after: zone == DropZone.Below);
            return;
        }

        if (_vm.HasPendingReorder || !CommitListViewModel.CanDropCommitForFixup(source, target, out _))
            return;

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

    // ── Insertion marker for reorder drops ───────────────────────────────

    private InsertionAdorner? _insertionAdorner;

    private void ShowInsertionMarker(ListViewItem item, bool below)
    {
        if (_insertionAdorner is { } existing && ReferenceEquals(existing.AdornedElement, item) && existing.Below == below)
            return;

        ClearInsertionMarker();
        if (AdornerLayer.GetAdornerLayer(item) is not { } layer)
            return;

        _insertionAdorner = new InsertionAdorner(item, below, (Brush)FindResource("AccentBlue"));
        layer.Add(_insertionAdorner);
    }

    private void ClearInsertionMarker()
    {
        if (_insertionAdorner is null)
            return;

        AdornerLayer.GetAdornerLayer(_insertionAdorner.AdornedElement)?.Remove(_insertionAdorner);
        _insertionAdorner = null;
    }

    /// <summary>A line with end ticks across the top or bottom edge of a row.</summary>
    private sealed class InsertionAdorner : Adorner
    {
        private readonly Pen _pen;

        public InsertionAdorner(UIElement adornedElement, bool below, Brush brush)
            : base(adornedElement)
        {
            Below = below;
            _pen = new Pen(brush, 2);
            _pen.Freeze();
            IsHitTestVisible = false;
        }

        public bool Below { get; }

        protected override void OnRender(DrawingContext drawingContext)
        {
            var size = AdornedElement.RenderSize;
            var y = Below ? size.Height - 1 : 1;
            drawingContext.DrawLine(_pen, new Point(2, y), new Point(size.Width - 2, y));
            drawingContext.DrawLine(_pen, new Point(2, y - 3), new Point(2, y + 3));
            drawingContext.DrawLine(_pen, new Point(size.Width - 2, y - 3), new Point(size.Width - 2, y + 3));
        }
    }
}

/// <summary>Picks the header vs. commit container style for the heterogeneous commit list.</summary>
public sealed class CommitContainerStyleSelector : StyleSelector
{
    public Style? CommitStyle { get; set; }
    public Style? HeaderStyle { get; set; }
    public Style? EmptyRowStyle { get; set; }

    public override Style? SelectStyle(object item, DependencyObject container)
        => item switch
        {
            CommitSectionHeader => HeaderStyle,
            CommitSectionEmptyRow => EmptyRowStyle ?? HeaderStyle,
            _ => CommitStyle,
        };
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
