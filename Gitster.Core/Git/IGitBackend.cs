using Gitster.Core.Models;

namespace Gitster.Core.Git;

public interface IRepositoryContext
{
    string? RepositoryPath { get; }
    Task OpenAsync(string path);
    event EventHandler? HeadChanged;
    GitCapabilities Capabilities { get; }
}

public interface IHistoryReader : IRepositoryContext
{
    Task<IReadOnlyList<CommitInfo>> GetCommitsAsync(CommitFilter? filter = null);
    IAsyncEnumerable<CommitInfo> EnumerateCommitsAsync(
        CommitFilter? filter = null,
        CancellationToken ct = default);
    /// <summary>
    /// Computes the local/remote relationship for <paramref name="refName"/> (canonical name,
    /// refs/heads/* or refs/remotes/*), or for HEAD when null.
    /// </summary>
    Task<RemoteSets> ComputeRemoteSetsAsync(string? refName = null, CancellationToken ct = default);
    Task<CommitDetails> GetCommitAsync(string sha);
    Task<CommitDiff> GetCommitDiffAsync(string sha, CancellationToken ct = default);
    Task<string> GetReflogSelectorForHeadAsync();
    Task<string> GetHeadShaAsync();
    Task<string> ResolveRefAsync(string refSpec);
    Task<IReadOnlyList<CommitInfo>> GetCommitsBetweenAsync(string fromSha, string toSha);
    Task<bool> CommitExistsAsync(string sha);
    Task<Dictionary<string, string>> GetAllRefsAsync();
    Task<IReadOnlyList<RefCatalogItem>> GetRefCatalogAsync();
    Task<IReadOnlyList<CommitInfo>> GetCommitsForRefAsync(string refName, int maxCount = 200);
}

public interface IWorkingTreeOps : IRepositoryContext
{
    Task<WorkingTreeState> GetWorkingTreeStateAsync();
    Task<BranchInfo> GetCurrentBranchAsync();
    Task<WorkingTreeStatus> GetWorkingTreeStatusAsync();
    Task StageAsync(IEnumerable<string> paths);
    Task UnstageAsync(IEnumerable<string> paths);
    Task StageAllAsync();
    Task<string> CommitAsync(CommitRequest request);
    Task<string> CommitToBranchAsync(CommitToBranchRequest request);
    Task<string> CreateSnapshotBranchAsync(string branchName, bool includeUncommitted);
}

public interface IHistoryRewriteOps : IRepositoryContext
{
    Task<string> AmendAsync(AmendRequest request);
    Task AmendAuthorAsync(AmendAuthorRequest request);
    Task RewriteCommitsAsync(IEnumerable<CommitRewrite> rewrites, string? branchName = null);

    /// <summary>
    /// Applies metadata rewrites and a new order for the newest commits of the branch in one
    /// pass. <paramref name="newOrderNewestFirst"/> lists the branch's newest N commits (by their
    /// original SHAs) in the wanted order; moved commits are replayed with a three-way merge and
    /// a conflict aborts the whole operation with the branch unchanged.
    /// </summary>
    Task RewriteHistoryAsync(IEnumerable<CommitRewrite> rewrites, IReadOnlyList<string> newOrderNewestFirst, string? branchName = null);
    Task RemoveFileChangeFromCommitAsync(string sha, string path, string? branchName = null);
    Task ResetMixedAsync(string targetReference, string? branchName = null);
    Task ResetHardAsync(string targetReference, string? branchName = null);
    Task CheckoutCommitDetachedAsync(string sha);
    Task CherryPickAsync(string sha);
    Task RevertCommitAsync(string sha);
    Task FixupIntoCommitAsync(string targetSha);
    Task FixupCommitIntoCommitAsync(string sourceSha, string targetSha);
    Task RewordCommitAsync(string sha, string newMessage);
    Task SquashCommitsAsync(IReadOnlyList<string> shas, string combinedMessage, DateTimeOffset? overrideDate);

    /// <summary>
    /// The commits a merge brought in: reachable from its second parent but not from its first
    /// (newest first). Empty for a non-merge commit.
    /// </summary>
    Task<IReadOnlyList<MergedBranchCommit>> GetMergedBranchCommitsAsync(string mergeSha);

    /// <summary>
    /// Squash-merge after the fact: replaces a merge on the branch's first-parent line with one
    /// commit that has the merge's tree and its first parent as only parent. The merged branch
    /// commits drop out of the history; newer commits are re-parented with unchanged trees.
    /// </summary>
    Task SquashMergeAsync(string mergeSha, string message, DateTimeOffset? overrideDate, string? branchName = null);
    Task ReorderCommitsAsync(IReadOnlyList<string> shasNewestFirst, IReadOnlyList<string> reorderedShasNewestFirst, string? branchName = null);
    Task SplitCommitAsync(
        string sha,
        IReadOnlyList<string> firstCommitPaths,
        string firstMessage,
        string secondMessage,
        string? branchName = null);
    Task<string> CreateOrphanBranchAsync(string branchName, bool commitCurrentTree);
    Task<string> RescueDetachedHeadAsync(string branchName);
    Task<bool> AreCommitsContiguousAsync(IReadOnlyList<string> shas);
}

public interface IRemoteOps : IRepositoryContext
{
    Task FetchAsync(string remoteName = "origin", CancellationToken ct = default);
    Task PullAsync(string remoteName = "origin", CancellationToken ct = default);
    Task PushAsync(string remoteName = "origin", PushMode mode = PushMode.Normal, CancellationToken ct = default);
    /// <summary>Creates the remote branch, pushes and sets it as upstream (git push -u).</summary>
    Task PublishBranchAsync(string branchName, string remoteName = "origin", CancellationToken ct = default);
    Task PushThroughCommitAsync(string commitSha, string remoteName = "origin", PushMode mode = PushMode.Normal, CancellationToken ct = default);
    Task ForceRemoteToCommitAsync(string commitSha, string remoteName = "origin", CancellationToken ct = default);
    Task PushTagAsync(string tagName, string remoteName = "origin", CancellationToken ct = default);
}

public interface ITagOps : IRepositoryContext
{
    Task<string> CreateTagAsync(string name, string targetSha);
    Task<IReadOnlyList<string>> GetTagsForCommitAsync(string sha);
}

public interface IStashOps : IRepositoryContext
{
    Task<int> GetStashCountAsync();
    Task<IReadOnlyList<StashInfo>> GetStashesAsync();
    Task<CommitDiff> GetStashDiffAsync(int stashIndex, CancellationToken ct = default);
    Task ApplyStashAsync(int stashIndex, bool reinstateIndex = true);
    Task PopStashAsync(int stashIndex, bool reinstateIndex = true);
    Task DropStashAsync(int stashIndex);
    Task<string> CreateStashAsync(string message, bool includeUntracked = true);
    Task<string> ConvertStashToBranchAsync(int stashIndex, string branchName);
}

public interface IBranchOps : IRepositoryContext
{
    Task<IReadOnlyList<BranchSummary>> GetBranchesAsync();
    Task<IReadOnlyList<BranchListItem>> GetBranchListAsync();
    Task CheckoutBranchAsync(string branchName);
    /// <summary>Sets an existing remote branch as the upstream of a local branch (no network).</summary>
    Task SetUpstreamAsync(string branchName, string remoteBranchName);
    Task<string> CreateBranchAsync(string name, string startPointSha);
    Task DeleteBranchAsync(string name, bool force);
    Task RenameBranchAsync(string oldName, string newName);
    Task<BranchMergeResult> MergeBranchAsync(string branchName, BranchMergeStrategy strategy);
    Task<HistoryStitchPreview> PreviewHistoryStitchAsync(string sourceRef);
    Task<HistoryStitchResult> StitchHistoryAsync(string sourceRef);
}

public interface IArchiveOps : IRepositoryContext
{
    Task<ArchiveResult> ArchiveSourceZipAsync(ArchiveRequest request, CancellationToken ct = default);
}

public interface IWorktreeOps : IRepositoryContext
{
    Task<IReadOnlyList<WorktreeInfo>> GetWorktreesAsync();
    Task<string> AddWorktreeAsync(string path, string branchName, bool createBranch);
    Task RemoveWorktreeAsync(string path, bool force);
    Task PruneWorktreesAsync();
}

public interface ISearchOps : IRepositoryContext
{
    Task<IReadOnlyList<CommitInfo>> PickaxeSearchAsync(string term, string? path, CancellationToken ct = default);
    Task<IReadOnlyList<CommitInfo>> DiffRegexSearchAsync(string pattern, string? path, CancellationToken ct = default);
    Task<IReadOnlyList<BlameLine>> BlameAsync(string filePath, bool ignoreWhitespace, bool followMoves, CancellationToken ct = default);
    Task<IReadOnlyList<RangeDiffEntry>> RangeDiffAsync(string range1, string range2, CancellationToken ct = default);
    Task<string?> GetPriorTipFromReflogAsync();
    Task<CompareResult> CompareRefsAsync(string baseRef, string compareRef, bool threeDot, CancellationToken ct = default);
}

public interface IGitBackend :
    IHistoryReader,
    IWorkingTreeOps,
    IHistoryRewriteOps,
    IRemoteOps,
    ITagOps,
    IStashOps,
    IBranchOps,
    IArchiveOps,
    IWorktreeOps,
    ISearchOps
{
}
