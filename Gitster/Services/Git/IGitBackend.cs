using Gitster.Models;

namespace Gitster.Services.Git;

public interface IGitBackend
{
    string? RepositoryPath { get; }
    Task OpenAsync(string path);

    event EventHandler? HeadChanged;

    Task<WorkingTreeState> GetWorkingTreeStateAsync();
    Task<BranchInfo> GetCurrentBranchAsync();

    Task<IReadOnlyList<CommitInfo>> GetCommitsAsync(Gitster.ViewModels.CommitFilter? filter = null);

    /// <summary>Streams commits HEAD→parent (newest first) for progressive UI loading (plan A0.1).</summary>
    IAsyncEnumerable<CommitInfo> EnumerateCommitsAsync(
        Gitster.ViewModels.CommitFilter? filter = null,
        CancellationToken ct = default);

    /// <summary>Computes incoming/outgoing sets + remote identity off the UI thread (plan A0.4).</summary>
    Task<RemoteSets> ComputeRemoteSetsAsync(CancellationToken ct = default);

    Task<CommitDetails> GetCommitAsync(string sha);

    /// <summary>Computes a commit's file-level diff lazily and cancellably (plan A0.3).</summary>
    Task<CommitDiff> GetCommitDiffAsync(string sha, CancellationToken ct = default);

    // ── Commit panel (plan A2) ────────────────────────────────────────────
    Task<WorkingTreeStatus> GetWorkingTreeStatusAsync();
    Task StageAsync(IEnumerable<string> paths);
    Task UnstageAsync(IEnumerable<string> paths);
    Task StageAllAsync();
    Task<string> CommitAsync(CommitRequest request);

    Task<string> AmendAsync(AmendRequest request);
    Task AmendAuthorAsync(AmendAuthorRequest request);
    Task RewriteCommitsAsync(IEnumerable<CommitRewrite> rewrites);
    Task FetchAsync(string remoteName = "origin");
    Task PullAsync(string remoteName = "origin");
    Task PushAsync(string remoteName = "origin", PushMode mode = PushMode.Normal);

    Task<string> GetReflogSelectorForHeadAsync();
    Task ResetHardAsync(string targetReference);
    Task<string> GetHeadShaAsync();
    Task<string> ResolveRefAsync(string refSpec);
    Task<IReadOnlyList<CommitInfo>> GetCommitsBetweenAsync(string fromSha, string toSha);
    Task<bool> CommitExistsAsync(string sha);
    Task CherryPickAsync(string sha);

    Task<Dictionary<string, string>> GetAllRefsAsync();
    Task<int> GetStashCountAsync();

    // ── Stash operations (Step A) ───────────────────────────────────────
    Task<IReadOnlyList<StashInfo>> GetStashesAsync();
    Task<string> GetStashDiffAsync(int stashIndex);
    Task ApplyStashAsync(int stashIndex, bool reinstateIndex = true);
    Task PopStashAsync(int stashIndex, bool reinstateIndex = true);
    Task DropStashAsync(int stashIndex);
    Task<string> CreateStashAsync(string message, bool includeUntracked = true);
    Task<string> ConvertStashToBranchAsync(int stashIndex, string branchName);

    // ── Fixup-workflow operations (Steps F–H) ─────────────────────
    Task FixupIntoCommitAsync(string targetSha);
    Task RewordCommitAsync(string sha, string newMessage);
    Task SquashCommitsAsync(IReadOnlyList<string> shas, string combinedMessage, DateTimeOffset? overrideDate);
    Task<IReadOnlyList<BranchSummary>> GetBranchesAsync();
    Task<IReadOnlyList<CommitInfo>> GetCommitsForRefAsync(string refName, int maxCount = 200);
    Task<bool> AreCommitsContiguousAsync(IReadOnlyList<string> shas);

    // ── Phase 3: Branch operations (libgit2) ──────────────────────────────
    Task<IReadOnlyList<BranchListItem>> GetBranchListAsync();
    Task CheckoutBranchAsync(string branchName);
    Task<string> CreateBranchAsync(string name, string startPointSha);
    Task DeleteBranchAsync(string name, bool force);
    Task RenameBranchAsync(string oldName, string newName);

    // ── Phase 3: Commit-to-branch & snapshot (libgit2) ────────────────────
    Task<string> CommitToBranchAsync(CommitToBranchRequest request);
    Task<string> CreateSnapshotBranchAsync(string branchName, bool includeUncommitted);

    // ── Phase 3: Worktrees (CLI) ──────────────────────────────────────────
    Task<IReadOnlyList<WorktreeInfo>> GetWorktreesAsync();
    Task<string> AddWorktreeAsync(string path, string branchName, bool createBranch);
    Task RemoveWorktreeAsync(string path, bool force);
    Task PruneWorktreesAsync();

    GitCapabilities Capabilities { get; }
}
