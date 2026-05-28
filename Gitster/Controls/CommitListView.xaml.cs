using System.Windows;
using System.Windows.Controls;

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

    private void OnFocusSearchRequested() => SearchBox.Focus();

    private void CommitListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_vm == null) return;
        _vm.SelectedCommits = CommitListBox.SelectedItems
            .OfType<CommitItem>()
            .Where(c => !c.IsPlaceholder)
            .ToList();
    }
}

/// <summary>
/// Hides invisible placeholder sentinel items and gives real commits the full
/// interactive container style.
/// </summary>
public class CommitListItemStyleSelector : StyleSelector
{
    public Style? CommitStyle { get; set; }

    // Placeholder items are completely invisible and take no space.
    private static readonly Style _placeholderStyle = new(typeof(ListViewItem))
    {
        Setters =
        {
            new Setter(UIElement.VisibilityProperty,       Visibility.Collapsed),
            new Setter(UIElement.IsHitTestVisibleProperty, false),
            new Setter(Control.PaddingProperty,            new Thickness(0)),
            new Setter(Control.BorderThicknessProperty,    new Thickness(0)),
            new Setter(FrameworkElement.HeightProperty,    0d),
        }
    };

    public override Style SelectStyle(object item, DependencyObject container)
        => item is CommitItem { IsPlaceholder: true } ? _placeholderStyle : CommitStyle!;
}
