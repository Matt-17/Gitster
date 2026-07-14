using Gitster.Core.Features;
using Gitster.Core.Git;
using Gitster.Core.Models;

namespace Gitster.Core.Ui;

/// <summary>
/// Abstracts modal dialogs behind intent-revealing, UI-framework-free methods that return data.
/// The WPF head implements this with the styled dialog windows; a future Blazor head implements it
/// with its own components. ViewModels depend only on this port, never on the concrete dialogs.
/// </summary>
/// <remarks>Methods return <c>null</c> (or the documented "cancelled" value) when the user dismisses
/// the dialog without confirming.</remarks>
public interface IDialogService
{
    /// <summary>Prompts for a new author identity. Returns the entry, or null if cancelled.</summary>
    AuthorEntry? AddAuthor();

    /// <summary>Opens the author-directory editor. Returns the chosen author/committer text, or null if cancelled.</summary>
    AuthorSelection? EditAuthors(AuthorDirectoryService authorDir);

    /// <summary>Single-line text prompt. Returns the entered text, or null if cancelled.</summary>
    string? PromptText(string title, string prompt, string initialValue = "");

    /// <summary>Opens a file picker. Returns the chosen full path, or null if cancelled.</summary>
    string? PickFile(string title, string? initialDirectory);

    /// <summary>Reword a commit message. Returns the new message, or null if cancelled.</summary>
    string? RewordCommit(string currentMessage);

    /// <summary>Squash editor. Returns the combined message + optional date override, or null if cancelled.</summary>
    SquashInput? SquashCommits(string combinedMessage);

    /// <summary>Cherry-pick picker. Returns the chosen commit + optional date override, or null if cancelled.</summary>
    CherryPickInput? CherryPick(IGitBackend git, IReadOnlyList<BranchSummary> branches);

    /// <summary>Commit-to-branch editor. Returns the commit details, or null if cancelled.</summary>
    CommitToBranchInput? CommitToBranch(IEnumerable<string> branchNames);

    /// <summary>Snapshot-to-branch prompt. Returns the branch name + options, or null if cancelled.</summary>
    SnapshotBranchInput? SnapshotBranch();

    /// <summary>Add-worktree editor. Returns the worktree details, or null if cancelled.</summary>
    WorktreeInput? AddWorktree(string repoPath);

    /// <summary>New-stash prompt. Returns the message + options, or null if cancelled.</summary>
    NewStashInput? NewStash();

    /// <summary>Merge-branch strategy picker. Returns the chosen strategy, or null if cancelled.</summary>
    BranchMergeStrategy? MergeBranch(string sourceBranch, string targetBranch);

    /// <summary>Shows the history-stitch preview. Returns true if the user confirmed.</summary>
    bool ConfirmHistoryStitch(HistoryStitchPreview preview);

    /// <summary>Shows conflict guidance. Returns the chosen action, or null if dismissed.</summary>
    ConflictGuidanceAction? ShowConflict(ConflictGuidance guidance);
}

/// <summary>Author/committer identity text chosen in the author-directory editor.</summary>
public sealed record AuthorSelection(string AuthorText, string CommitterText);

/// <summary>Result of the squash editor.</summary>
public sealed record SquashInput(string CombinedMessage, DateTimeOffset? OverrideDate);

/// <summary>Result of the cherry-pick picker. <see cref="SelectedSha"/> is always populated.</summary>
public sealed record CherryPickInput(string SelectedSha, DateTimeOffset? OverrideDate);

/// <summary>Result of the commit-to-branch editor.</summary>
public sealed record CommitToBranchInput(
    string TargetBranch,
    string Message,
    string? AuthorName,
    string? AuthorEmail,
    bool IncludeUnstaged,
    bool RemoveFromCurrent);

/// <summary>Result of the snapshot-to-branch prompt.</summary>
public sealed record SnapshotBranchInput(string BranchName, bool IncludeUncommitted);

/// <summary>Result of the add-worktree editor.</summary>
public sealed record WorktreeInput(string WorktreePath, string BranchName, bool CreateBranch);

/// <summary>Result of the new-stash prompt.</summary>
public sealed record NewStashInput(string StashMessage, bool IncludeUntracked);
