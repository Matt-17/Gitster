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

    private void OnFocusSearchRequested() => SearchBoxControl.FocusInput();

    private void CommitListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_vm == null) return;
        // Only real commits participate in multi-selection; headers are never selectable.
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
        => item is CommitSectionHeader ? HeaderStyle : CommitStyle;
}

/// <summary>Picks the header vs. commit row template for the heterogeneous commit list.</summary>
public sealed class CommitRowTemplateSelector : DataTemplateSelector
{
    public DataTemplate? CommitTemplate { get; set; }
    public DataTemplate? HeaderTemplate { get; set; }

    public override DataTemplate? SelectTemplate(object item, DependencyObject container)
        => item is CommitSectionHeader ? HeaderTemplate : CommitTemplate;
}
