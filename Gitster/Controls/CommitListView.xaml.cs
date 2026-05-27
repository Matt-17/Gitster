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
}
