using System.Windows;

namespace Gitster.Views;

public partial class CommitToBranchDialog : Window
{
    public string TargetBranch { get; private set; } = string.Empty;
    public string Message      { get; private set; } = string.Empty;
    public string? AuthorName  { get; private set; }
    public string? AuthorEmail { get; private set; }
    public bool IncludeUnstaged => AllChangesRadio.IsChecked == true;
    public bool RemoveFromCurrent => MoveCheck.IsChecked == true;

    public CommitToBranchDialog(IEnumerable<string> branchNames)
    {
        InitializeComponent();
        BranchCombo.ItemsSource = branchNames.ToList();
        if (BranchCombo.Items.Count > 0)
            BranchCombo.SelectedIndex = 0;

        Loaded += (_, _) => MessageBox.Focus();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var target = (BranchCombo.Text ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(target))
        {
            GitsterDialog.Show(this, "Choose or type a target branch.", "Commit to branch",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (string.IsNullOrWhiteSpace(MessageBox.Text))
        {
            GitsterDialog.Show(this, "Enter a commit message.", "Commit to branch",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        TargetBranch = target;
        Message      = MessageBox.Text.Trim();
        AuthorName   = string.IsNullOrWhiteSpace(AuthorNameBox.Text)  ? null : AuthorNameBox.Text.Trim();
        AuthorEmail  = string.IsNullOrWhiteSpace(AuthorEmailBox.Text) ? null : AuthorEmailBox.Text.Trim();
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
