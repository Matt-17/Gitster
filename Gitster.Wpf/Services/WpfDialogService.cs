using Microsoft.Win32;

using Gitster.Core;
using Gitster.Core.Git;
using Gitster.Core.Models;
using Gitster.Core.Ui;
using Gitster.Views;

namespace Gitster.Services;

/// <summary>
/// WPF implementation of <see cref="IDialogService"/>. Owns construction of the styled dialog
/// windows and marshals their results back as plain data so ViewModels stay UI-agnostic.
/// </summary>
public sealed class WpfDialogService : IDialogService
{
    private readonly IWindowService _windows;

    public WpfDialogService(IWindowService windows) => _windows = windows;

    public AuthorEntry? AddAuthor()
    {
        var dialog = new AddAuthorDialog();
        return _windows.ShowDialog(dialog) == true ? dialog.Result : null;
    }

    public AuthorSelection? EditAuthors(AuthorDirectoryService authorDir)
    {
        var dialog = new EditAuthorsDialog(authorDir);
        return _windows.ShowDialog(dialog) == true
            ? new AuthorSelection(dialog.SelectedAuthorText, dialog.SelectedCommitterText)
            : null;
    }

    public string? PromptText(string title, string prompt, string initialValue = "")
    {
        var dialog = new TextInputDialog { Title = title, Prompt = prompt, Value = initialValue };
        return _windows.ShowDialog(dialog) == true ? dialog.Value : null;
    }

    public string? PickFile(string title, string? initialDirectory)
    {
        var dialog = new OpenFileDialog { Title = title, InitialDirectory = initialDirectory ?? string.Empty };
        return _windows.ShowDialog(dialog) == true ? dialog.FileName : null;
    }

    public string? RewordCommit(string currentMessage)
    {
        var dialog = new RewordDialog(currentMessage);
        return _windows.ShowDialog(dialog) == true ? dialog.NewMessage : null;
    }

    public SquashInput? SquashCommits(string combinedMessage)
    {
        var dialog = new SquashDialog(combinedMessage);
        return _windows.ShowDialog(dialog) == true
            ? new SquashInput(dialog.CombinedMessage, dialog.OverrideDate)
            : null;
    }

    public CherryPickInput? CherryPick(IGitBackend git, IReadOnlyList<BranchSummary> branches)
    {
        var dialog = new CherryPickDialog(git, branches);
        return _windows.ShowDialog(dialog) == true && dialog.SelectedSha is { } sha
            ? new CherryPickInput(sha, dialog.OverrideDate)
            : null;
    }

    public CommitToBranchInput? CommitToBranch(IEnumerable<string> branchNames)
    {
        var dialog = new CommitToBranchDialog(branchNames);
        return _windows.ShowDialog(dialog) == true
            ? new CommitToBranchInput(
                dialog.TargetBranch, dialog.Message, dialog.AuthorName, dialog.AuthorEmail,
                dialog.IncludeUnstaged, dialog.RemoveFromCurrent)
            : null;
    }

    public SnapshotBranchInput? SnapshotBranch()
    {
        var dialog = new SnapshotBranchDialog();
        return _windows.ShowDialog(dialog) == true
            ? new SnapshotBranchInput(dialog.BranchName, dialog.IncludeUncommitted)
            : null;
    }

    public WorktreeInput? AddWorktree(string repoPath)
    {
        var dialog = new AddWorktreeDialog(repoPath);
        return _windows.ShowDialog(dialog) == true
            ? new WorktreeInput(dialog.WorktreePath, dialog.BranchName, dialog.CreateBranch)
            : null;
    }

    public NewStashInput? NewStash()
    {
        var dialog = new NewStashDialog();
        return _windows.ShowDialog(dialog) == true
            ? new NewStashInput(dialog.StashMessage, dialog.IncludeUntracked)
            : null;
    }

    public BranchMergeStrategy? MergeBranch(string sourceBranch, string targetBranch)
    {
        var dialog = new MergeBranchDialog(sourceBranch, targetBranch);
        return _windows.ShowDialog(dialog) == true ? dialog.SelectedStrategy : null;
    }

    public bool ConfirmHistoryStitch(HistoryStitchPreview preview)
    {
        var dialog = new HistoryStitchDialog(preview);
        return _windows.ShowDialog(dialog) == true;
    }
}
