using System.Windows.Controls;
using System.Windows.Input;

using Gitster.ViewModels;

namespace Gitster.Views.Modes;

public partial class BranchesModeView : UserControl
{
    public BranchesModeView()
    {
        InitializeComponent();
    }

    // Double-clicking a leaf node in the branch tree checks it out (A13).
    private void BranchTree_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (BranchTreeView.SelectedItem is BranchTreeNode { IsBranch: true, BranchName: { } name }
            && BranchTreeView.DataContext is BranchesViewModel vm
            && vm.CheckoutNamedCommand.CanExecute(name))
        {
            vm.CheckoutNamedCommand.Execute(name);
        }
    }
}
