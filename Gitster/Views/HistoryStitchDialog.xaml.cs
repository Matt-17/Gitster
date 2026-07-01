using System.Windows;

using Gitster.Services.Git;

namespace Gitster.Views;

public partial class HistoryStitchDialog : Window
{
    public HistoryStitchPreview Preview { get; }

    public HistoryStitchDialog(HistoryStitchPreview preview)
    {
        InitializeComponent();
        Preview = preview;

        SourceBranchText.Text = $"{preview.SourceRef} ({ShortSha(preview.SourceTipSha)})";
        TargetBranchText.Text = $"{preview.TargetBranch} ({ShortSha(preview.TargetHeadSha)})";
        CommandText.Text = $"git merge --no-ff -s ours {QuoteForDisplay(preview.SourceRef)} -m \"Record original history of {preview.SourceRef}\"";
        GraphText.Text = BuildGraphPreview(preview);

        BlockList.ItemsSource = preview.Blocks;
        BlocksBox.Visibility = preview.Blocks.Count == 0 ? Visibility.Collapsed : Visibility.Visible;

        WarningList.ItemsSource = preview.Warnings;
        WarningsBox.Visibility = preview.Warnings.Count == 0 ? Visibility.Collapsed : Visibility.Visible;

        StitchButton.IsEnabled = preview.CanExecute;
    }

    private static string BuildGraphPreview(HistoryStitchPreview preview)
    {
        var source = ShortSha(preview.SourceTipSha);
        var target = ShortSha(preview.TargetHeadSha);
        var countText = preview.UniqueSourceCommitCount == 1
            ? "1 unique source commit"
            : $"{preview.UniqueSourceCommitCount} unique source commits";
        var matchText = string.IsNullOrEmpty(preview.SquashMatchSha)
            ? "no exact squash-tree match found"
            : $"squash-tree match: {ShortSha(preview.SquashMatchSha)}";

        return
            $"Before:\n" +
            $"  {target}  {preview.TargetBranch}\n" +
            $"     \\\n" +
            $"      {source}  {preview.SourceRef} ({countText})\n\n" +
            $"After:\n" +
            $"  {target}---M  {preview.TargetBranch}\n" +
            $"     \\   /\n" +
            $"      {source}  {preview.SourceRef}\n\n" +
            $"Result tree: current branch files stay unchanged\n" +
            $"Check: {matchText}";
    }

    private static string ShortSha(string sha) =>
        string.IsNullOrEmpty(sha) ? "-" : sha[..Math.Min(7, sha.Length)];

    private static string QuoteForDisplay(string value) =>
        value.Any(char.IsWhiteSpace)
            ? $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\""
            : value;

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (!Preview.CanExecute)
            return;

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) =>
        DialogResult = false;
}
