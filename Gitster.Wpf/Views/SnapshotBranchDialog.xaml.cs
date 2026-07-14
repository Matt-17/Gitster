using System.Windows;

namespace Gitster.Views;

public partial class SnapshotBranchDialog : Window
{
    public string BranchName { get; private set; } = string.Empty;
    public bool IncludeUncommitted => IncludeUncommittedCheck.IsChecked == true;

    public SnapshotBranchDialog()
    {
        InitializeComponent();
        NameBox.Text = $"snapshot/{DateTime.Now:yyyy-MM-dd-HHmm}";
        Loaded += (_, _) =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        };
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NameBox.Text))
        {
            GitsterDialog.Show(this, "Enter a branch name.", "Snapshot to branch",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        BranchName = NameBox.Text.Trim();
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
