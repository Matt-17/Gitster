using Gitster.Core.Features;
using Gitster.Core.Git;
using Gitster.Core.Models;

namespace Gitster.Core.Ui;

/// <summary>
/// No-op <see cref="IDialogService"/> used as a safe default when a ViewModel is constructed
/// outside DI (design-time, certain tests). Every prompt reports "cancelled".
/// </summary>
public sealed class NullDialogService : IDialogService
{
    public static readonly NullDialogService Instance = new();

    private NullDialogService() { }

    public AuthorEntry? AddAuthor() => null;
    public AuthorSelection? EditAuthors(AuthorDirectoryService authorDir) => null;
    public string? PromptText(string title, string prompt, string initialValue = "") => null;
    public string? PickFile(string title, string? initialDirectory) => null;
    public string? RewordCommit(string currentMessage) => null;
    public SquashInput? SquashCommits(string combinedMessage) => null;
    public CherryPickInput? CherryPick(IGitBackend git, IReadOnlyList<BranchSummary> branches) => null;
    public CommitToBranchInput? CommitToBranch(IEnumerable<string> branchNames) => null;
    public SnapshotBranchInput? SnapshotBranch() => null;
    public WorktreeInput? AddWorktree(string repoPath) => null;
    public NewStashInput? NewStash() => null;
    public BranchMergeStrategy? MergeBranch(string sourceBranch, string targetBranch) => null;
    public bool ConfirmHistoryStitch(HistoryStitchPreview preview) => false;
    public ConflictGuidanceAction? ShowConflict(ConflictGuidance guidance) => null;
}
