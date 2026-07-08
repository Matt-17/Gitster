using System.Windows;
using System.Windows.Controls;

using Gitster.Services.Git;

namespace Gitster.Views;

/// <summary>Simple DTO for the commit list in CherryPickDialog.</summary>
public sealed record CherryPickCommitRow(string ShortSha, string Message, string AuthorName, string FullSha);

public partial class CherryPickDialog : Window
{
    private readonly IGitBackend _git;

    /// <summary>The SHA of the commit the user selected to cherry-pick.</summary>
    public string? SelectedSha { get; private set; }

    /// <summary>Optional timestamp override (if the user toggled the check-box).</summary>
    public DateTimeOffset? OverrideDate { get; private set; }

    public CherryPickDialog(IGitBackend git, IReadOnlyList<BranchSummary> branches)
    {
        _git = git;
        InitializeComponent();
        DatePicker.SelectedDate = DateTime.Now;
        DatePicker.IsEnabled    = false;

        BranchCombo.ItemsSource = branches.Select(b => b.Name).ToList();
        if (BranchCombo.Items.Count > 0)
            BranchCombo.SelectedIndex = 0;
    }

    private async void BranchCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (BranchCombo.SelectedItem is not string branchName) return;
        CommitList.ItemsSource = null;
        OKButton.IsEnabled     = false;

        try
        {
            var commits = await _git.GetCommitsForRefAsync(branchName, maxCount: 150);
            CommitList.ItemsSource = commits
                .Select(c => new CherryPickCommitRow(
                    c.ShortSha,
                    c.Message,
                    c.AuthorName,
                    c.FullSha))
                .ToList();
        }
        catch (Exception ex)
        {
            GitsterDialog.Show(
                this,
                $"Could not load commits for '{branchName}':\n{ex.Message}",
                "Gitster", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void CommitList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        OKButton.IsEnabled = CommitList.SelectedItem is CherryPickCommitRow;
    }

    private void OverrideTimeCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (DatePicker != null)
            DatePicker.IsEnabled = OverrideTimeCheck.IsChecked == true;
    }

    private void OK_Click(object sender, RoutedEventArgs e)
    {
        if (CommitList.SelectedItem is not CherryPickCommitRow row)
        {
            GitsterDialog.Show(
                this,
                "Select a commit to cherry-pick first.",
                "Gitster", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        SelectedSha  = row.FullSha;
        OverrideDate = OverrideTimeCheck.IsChecked == true && DatePicker.SelectedDate.HasValue
            ? new DateTimeOffset(DatePicker.SelectedDate.Value)
            : null;

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) =>
        DialogResult = false;
}
