using System.IO;
using System.Windows;

using Microsoft.Win32;

namespace Gitster.Views;

public partial class AddWorktreeDialog : Window
{
    public string WorktreePath { get; private set; } = string.Empty;
    public string BranchName   { get; private set; } = string.Empty;
    public bool   CreateBranch => CreateBranchCheck.IsChecked == true;

    private readonly string _repoPath;

    public AddWorktreeDialog(string repoPath)
    {
        InitializeComponent();
        _repoPath = repoPath ?? string.Empty;

        // Suggest a sibling folder next to the repo.
        var parent = Directory.GetParent(_repoPath.TrimEnd(Path.DirectorySeparatorChar))?.FullName;
        var repoName = Path.GetFileName(_repoPath.TrimEnd(Path.DirectorySeparatorChar));
        if (!string.IsNullOrEmpty(parent))
            PathBox.Text = Path.Combine(parent, $"{repoName}-worktree");

        Loaded += (_, _) => BranchBox.Focus();
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Choose worktree location",
            InitialDirectory = Directory.Exists(_repoPath) ? _repoPath : Environment.CurrentDirectory,
        };
        if (dialog.ShowDialog() == true)
            PathBox.Text = dialog.FolderName;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var path = PathBox.Text.Trim();
        var branch = BranchBox.Text.Trim();

        if (string.IsNullOrEmpty(path))
        {
            GitsterDialog.Show(this, "Choose a folder for the worktree.", "Add worktree",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (string.IsNullOrEmpty(branch))
        {
            GitsterDialog.Show(this, "Enter the branch to check out (or a new branch name).", "Add worktree",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        WorktreePath = path;
        BranchName   = branch;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
