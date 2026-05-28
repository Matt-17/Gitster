using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Gitster.Services;
using Gitster.Services.Git;
using Gitster.Views;

namespace Gitster.ViewModels;

/// <summary>UI wrapper around a <see cref="WorktreeInfo"/>.</summary>
public sealed class WorktreeRow
{
    public WorktreeRow(WorktreeInfo info) => Info = info;

    public WorktreeInfo Info { get; }

    public string Path        => Info.Path;
    public string FolderName  => System.IO.Path.GetFileName(Info.Path.TrimEnd(System.IO.Path.DirectorySeparatorChar));
    public string BranchName  => Info.BranchName;
    public string ShortSha    => Info.HeadSha.Length >= 7 ? Info.HeadSha[..7] : Info.HeadSha;
    public bool   IsMain      => Info.IsMain;
    public bool   IsLocked    => Info.IsLocked;
    public bool   IsPrunable  => Info.IsPrunable;
    public bool   IsCurrent   => Info.IsCurrent;
}

public partial class WorktreesViewModel : BaseViewModel
{
    private readonly IGitBackend              _git;
    private readonly OperationFeedbackService _feedback;
    private readonly SnapshotService          _snapshots;
    private readonly Func<string>             _getCurrentPath;
    private readonly Action<string>           _openInGitster;

    public ObservableCollection<WorktreeRow> Worktrees { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyPropertyChangedFor(nameof(CanRemove))]
    [NotifyPropertyChangedFor(nameof(CanOpenInGitster))]
    [NotifyCanExecuteChangedFor(nameof(RemoveCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenInExplorerCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenInGitsterCommand))]
    public partial WorktreeRow? SelectedWorktree { get; set; }

    [ObservableProperty]
    public partial bool IsCliAvailable { get; set; } = true;

    [ObservableProperty]
    public partial bool HasWorktrees { get; set; }

    [ObservableProperty]
    public partial bool HasPrunable { get; set; }

    public bool HasSelection      => SelectedWorktree != null;
    public bool CanRemove         => SelectedWorktree is { IsMain: false };
    public bool CanOpenInGitster  => SelectedWorktree is { IsCurrent: false, IsPrunable: false };

    public WorktreesViewModel(
        IGitBackend              git,
        OperationFeedbackService feedback,
        SnapshotService          snapshots,
        Func<string>             getCurrentPath,
        Action<string>           openInGitster)
    {
        _git            = git;
        _feedback       = feedback;
        _snapshots      = snapshots;
        _getCurrentPath = getCurrentPath;
        _openInGitster  = openInGitster;
    }

    public async Task LoadAsync()
    {
        IsCliAvailable = GitCli.IsAvailable;
        if (!IsCliAvailable)
        {
            Clear();
            return;
        }

        List<WorktreeRow> rows;
        try
        {
            var items = await _git.GetWorktreesAsync();
            rows = items.Select(w => new WorktreeRow(w)).ToList();
        }
        catch
        {
            rows = [];
        }

        var prev = SelectedWorktree?.Path;
        Worktrees.Clear();
        foreach (var w in rows) Worktrees.Add(w);

        SelectedWorktree = Worktrees.FirstOrDefault(w => w.Path == prev)
                           ?? Worktrees.FirstOrDefault(w => w.IsCurrent)
                           ?? Worktrees.FirstOrDefault();

        HasWorktrees = Worktrees.Count > 0;
        HasPrunable  = Worktrees.Any(w => w.IsPrunable);
    }

    public void Clear()
    {
        Worktrees.Clear();
        SelectedWorktree = null;
        HasWorktrees = false;
        HasPrunable = false;
    }

    [RelayCommand]
    private async Task Add()
    {
        var dialog = new AddWorktreeDialog(_getCurrentPath()) { Owner = Application.Current.MainWindow };
        if (dialog.ShowDialog() != true) return;

        try
        {
            _ = _snapshots.CaptureAsync(_git, $"Add worktree {dialog.BranchName}");
            await _feedback.RunAsync("Add worktree",
                () => _git.AddWorktreeAsync(dialog.WorktreePath, dialog.BranchName, dialog.CreateBranch));
            await LoadAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Add worktree failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void OpenInExplorer()
    {
        if (SelectedWorktree is not { } w) return;
        if (!Directory.Exists(w.Path))
        {
            MessageBox.Show("That worktree directory no longer exists.",
                "Open folder", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{w.Path}\"") { UseShellExecute = true }); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Open folder", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    [RelayCommand(CanExecute = nameof(CanOpenInGitster))]
    private void OpenInGitster()
    {
        if (SelectedWorktree is not { } w) return;
        if (!Directory.Exists(w.Path))
        {
            MessageBox.Show("That worktree directory no longer exists.",
                "Open in Gitster", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        _openInGitster(w.Path);
    }

    [RelayCommand(CanExecute = nameof(CanRemove))]
    private async Task Remove()
    {
        if (SelectedWorktree is not { } w) return;

        var confirm = MessageBox.Show(
            $"Remove the worktree at:\n{w.Path}\n\n" +
            "This deletes the working directory (the branch itself is kept).",
            "Remove worktree", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;

        try
        {
            _ = _snapshots.CaptureAsync(_git, $"Remove worktree {w.FolderName}");
            await _feedback.RunAsync("Remove worktree", () => _git.RemoveWorktreeAsync(w.Path, force: false));
            await LoadAsync();
        }
        catch (Exception)
        {
            // Likely dirty/locked — offer a forced removal.
            var force = MessageBox.Show(
                "The worktree could not be removed cleanly (it may have uncommitted changes or be locked).\n\n" +
                "Force removal?",
                "Remove worktree", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (force != MessageBoxResult.Yes) return;

            try
            {
                await _feedback.RunAsync("Force-remove worktree", () => _git.RemoveWorktreeAsync(w.Path, force: true));
                await LoadAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Remove failed", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }

    [RelayCommand(CanExecute = nameof(HasPrunable))]
    private async Task Prune()
    {
        var prunable = Worktrees.Where(w => w.IsPrunable).Select(w => w.Path).ToList();
        var preview = prunable.Count > 0
            ? string.Join("\n", prunable)
            : "(stale administrative entries)";

        var confirm = MessageBox.Show(
            $"Prune stale worktree entries?\n\n{preview}",
            "Prune worktrees", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        try
        {
            await _feedback.RunAsync("Prune worktrees", () => _git.PruneWorktreesAsync());
            await LoadAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Prune failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
