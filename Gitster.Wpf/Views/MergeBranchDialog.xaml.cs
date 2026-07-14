using System.Windows;

using Gitster.Services.Git;

namespace Gitster.Views;

public partial class MergeBranchDialog : Window
{
    public BranchMergeStrategy SelectedStrategy { get; private set; } = BranchMergeStrategy.FastForwardOnly;

    public MergeBranchDialog(string sourceBranch, string targetBranch)
    {
        InitializeComponent();
        SourceBranchText.Text = sourceBranch;
        TargetBranchText.Text = targetBranch;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        SelectedStrategy =
            NoFastForwardRadio.IsChecked == true ? BranchMergeStrategy.NoFastForward :
            DefaultRadio.IsChecked == true ? BranchMergeStrategy.Default :
            BranchMergeStrategy.FastForwardOnly;

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) =>
        DialogResult = false;
}
