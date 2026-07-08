using Gitster.Models;
using Gitster.Services.Git.LibGit2;

namespace Gitster.Services.Git;

public sealed class LibGit2Backend : IGitBackend, IRepositoryReadProvider
{
    private readonly LibGit2RepositoryContext _context = new();
    private readonly LibGit2HistoryReader _history;
    private readonly LibGit2WorkingTreeOperations _workingTree;
    private readonly LibGit2HistoryRewriteOperations _rewrite;
    private readonly LibGit2StashOperations _stashes;
    private readonly LibGit2BranchOperations _branches;
    private readonly LibGit2AnalysisOperations _analysis;

    public LibGit2Backend()
    {
        _history = new LibGit2HistoryReader(_context);
        _workingTree = new LibGit2WorkingTreeOperations(_context);
        _rewrite = new LibGit2HistoryRewriteOperations(_context);
        _stashes = new LibGit2StashOperations(_context);
        _branches = new LibGit2BranchOperations(_context);
        _analysis = new LibGit2AnalysisOperations(_context);
    }

    public string? RepositoryPath => _context.RepositoryPath;

    public event EventHandler? HeadChanged
    {
        add => _context.HeadChanged += value;
        remove => _context.HeadChanged -= value;
    }

    public GitCapabilities Capabilities =>
        GitCapabilities.Read | GitCapabilities.BasicWrite
        | GitCapabilities.ReflogUndo | GitCapabilities.StashManagement;

    public Task OpenAsync(string path) => _context.OpenAsync(path);

    public LibGit2Sharp.Repository OpenRepository(string repoPath)
    {
        if (string.Equals(RepositoryPath, repoPath, StringComparison.OrdinalIgnoreCase))
            return _context.OpenRepository();

        return new LibGit2Sharp.Repository(repoPath);
    }

    public Task<WorkingTreeState> GetWorkingTreeStateAsync() => _workingTree.GetWorkingTreeStateAsync();

    public Task<BranchInfo> GetCurrentBranchAsync() => _history.GetCurrentBranchAsync();

    public Task<IReadOnlyList<CommitInfo>> GetCommitsAsync(Gitster.ViewModels.CommitFilter? filter = null)
        => _history.GetCommitsAsync(filter);

    public IAsyncEnumerable<CommitInfo> EnumerateCommitsAsync(
        Gitster.ViewModels.CommitFilter? filter = null,
        CancellationToken ct = default)
        => _history.EnumerateCommitsAsync(filter, ct);

    public Task<RemoteSets> ComputeRemoteSetsAsync(CancellationToken ct = default)
        => _history.ComputeRemoteSetsAsync(ct);

    public Task<CommitDetails> GetCommitAsync(string sha) => _history.GetCommitAsync(sha);

    public Task<CommitDiff> GetCommitDiffAsync(string sha, CancellationToken ct = default)
        => _history.GetCommitDiffAsync(sha, ct);

    internal static List<DiffLine> ParseUnifiedDiff(string? patchText)
        => LibGit2DiffParser.ParseUnifiedDiff(patchText);

    public Task<WorkingTreeStatus> GetWorkingTreeStatusAsync() => _workingTree.GetWorkingTreeStatusAsync();

    public Task StageAsync(IEnumerable<string> paths) => _workingTree.StageAsync(paths);

    public Task UnstageAsync(IEnumerable<string> paths) => _workingTree.UnstageAsync(paths);

    public Task StageAllAsync() => _workingTree.StageAllAsync();

    public Task<string> CommitAsync(CommitRequest request) => _workingTree.CommitAsync(request);

    public Task<string> AmendAsync(AmendRequest request) => _workingTree.AmendAsync(request);

    public Task AmendAuthorAsync(AmendAuthorRequest request) => _rewrite.AmendAuthorAsync(request);

    public Task RewriteCommitsAsync(IEnumerable<CommitRewrite> rewrites, string? branchName = null)
        => _rewrite.RewriteCommitsAsync(rewrites, branchName);

    public Task RemoveFileChangeFromCommitAsync(string sha, string path, string? branchName = null)
        => _rewrite.RemoveFileChangeFromCommitAsync(sha, path, branchName);

    public Task FetchAsync(string remoteName = "origin", CancellationToken ct = default)
        => ServerWorkNotSupportedAsync(nameof(FetchAsync));

    public Task PullAsync(string remoteName = "origin", CancellationToken ct = default)
        => ServerWorkNotSupportedAsync(nameof(PullAsync));

    public Task PushAsync(string remoteName = "origin", PushMode mode = PushMode.Normal, CancellationToken ct = default)
        => ServerWorkNotSupportedAsync(nameof(PushAsync));

    public Task PushThroughCommitAsync(string commitSha, string remoteName = "origin", CancellationToken ct = default)
        => ServerWorkNotSupportedAsync(nameof(PushThroughCommitAsync));

    public Task<string> GetReflogSelectorForHeadAsync() => _rewrite.GetReflogSelectorForHeadAsync();

    public Task ResetMixedAsync(string targetReference, string? branchName = null)
        => _rewrite.ResetMixedAsync(targetReference, branchName);

    public Task ResetHardAsync(string targetReference, string? branchName = null)
        => _rewrite.ResetHardAsync(targetReference, branchName);

    public Task<string> GetHeadShaAsync() => _history.GetHeadShaAsync();

    public Task<string> ResolveRefAsync(string refSpec) => _history.ResolveRefAsync(refSpec);

    public Task<IReadOnlyList<CommitInfo>> GetCommitsBetweenAsync(string fromSha, string toSha)
        => _history.GetCommitsBetweenAsync(fromSha, toSha);

    public Task<bool> CommitExistsAsync(string sha) => _history.CommitExistsAsync(sha);

    public Task CheckoutCommitDetachedAsync(string sha) => _rewrite.CheckoutCommitDetachedAsync(sha);

    public Task CherryPickAsync(string sha) => _rewrite.CherryPickAsync(sha);

    public Task<string> CreateTagAsync(string name, string targetSha) => _rewrite.CreateTagAsync(name, targetSha);

    public Task<IReadOnlyList<string>> GetTagsForCommitAsync(string sha) => _history.GetTagsForCommitAsync(sha);

    public Task PushTagAsync(string tagName, string remoteName = "origin", CancellationToken ct = default)
        => ServerWorkNotSupportedAsync(nameof(PushTagAsync));

    public Task RevertCommitAsync(string sha) => _rewrite.RevertCommitAsync(sha);

    public Task<Dictionary<string, string>> GetAllRefsAsync() => _history.GetAllRefsAsync();

    public Task<IReadOnlyList<RefCatalogItem>> GetRefCatalogAsync() => _history.GetRefCatalogAsync();

    public Task<int> GetStashCountAsync() => _stashes.GetStashCountAsync();

    public Task<IReadOnlyList<StashInfo>> GetStashesAsync() => _stashes.GetStashesAsync();

    public Task<CommitDiff> GetStashDiffAsync(int stashIndex, CancellationToken ct = default)
        => _stashes.GetStashDiffAsync(stashIndex, ct);

    public Task ApplyStashAsync(int stashIndex, bool reinstateIndex = true)
        => _stashes.ApplyStashAsync(stashIndex, reinstateIndex);

    public Task PopStashAsync(int stashIndex, bool reinstateIndex = true)
        => _stashes.PopStashAsync(stashIndex, reinstateIndex);

    public Task DropStashAsync(int stashIndex) => _stashes.DropStashAsync(stashIndex);

    public Task<string> CreateStashAsync(string message, bool includeUntracked = true)
        => _stashes.CreateStashAsync(message, includeUntracked);

    public Task<string> ConvertStashToBranchAsync(int stashIndex, string branchName)
        => _stashes.ConvertStashToBranchAsync(stashIndex, branchName);

    public Task FixupIntoCommitAsync(string targetSha)
        => throw new NotSupportedException("Route through HybridGitBackend.");

    public Task FixupCommitIntoCommitAsync(string sourceSha, string targetSha)
        => throw new NotSupportedException("Route through HybridGitBackend.");

    public Task RewordCommitAsync(string sha, string newMessage)
        => _rewrite.RewordCommitAsync(sha, newMessage);

    public Task SquashCommitsHeadAsync(
        IReadOnlyList<string> shas,
        string combinedMessage,
        DateTimeOffset? overrideDate)
        => _rewrite.SquashCommitsHeadAsync(shas, combinedMessage, overrideDate);

    public Task ReorderCommitsAsync(
        IReadOnlyList<string> shasNewestFirst,
        IReadOnlyList<string> reorderedShasNewestFirst,
        string? branchName = null)
        => _rewrite.ReorderCommitsAsync(shasNewestFirst, reorderedShasNewestFirst, branchName);

    public Task SplitCommitAsync(
        string sha,
        IReadOnlyList<string> firstCommitPaths,
        string firstMessage,
        string secondMessage,
        string? branchName = null)
        => _rewrite.SplitCommitAsync(sha, firstCommitPaths, firstMessage, secondMessage, branchName);

    public Task<string> CreateOrphanBranchAsync(string branchName, bool commitCurrentTree)
        => _rewrite.CreateOrphanBranchAsync(branchName, commitCurrentTree);

    public Task<string> RescueDetachedHeadAsync(string branchName)
        => _rewrite.RescueDetachedHeadAsync(branchName);

    public Task<bool> AreCommitsContiguousAsync(IReadOnlyList<string> shas)
        => _rewrite.AreCommitsContiguousAsync(shas);

    public Task SquashCommitsAsync(
        IReadOnlyList<string> shas,
        string combinedMessage,
        DateTimeOffset? overrideDate)
        => throw new NotSupportedException("Route through HybridGitBackend.");

    public Task<IReadOnlyList<BranchSummary>> GetBranchesAsync() => _history.GetBranchesAsync();

    public Task<IReadOnlyList<CommitInfo>> GetCommitsForRefAsync(string refName, int maxCount = 200)
        => _history.GetCommitsForRefAsync(refName, maxCount);

    public Task<IReadOnlyList<BranchListItem>> GetBranchListAsync() => _branches.GetBranchListAsync();

    public Task CheckoutBranchAsync(string branchName) => _branches.CheckoutBranchAsync(branchName);

    public Task<string> CreateBranchAsync(string name, string startPointSha)
        => _branches.CreateBranchAsync(name, startPointSha);

    public Task DeleteBranchAsync(string name, bool force) => _branches.DeleteBranchAsync(name, force);

    public Task RenameBranchAsync(string oldName, string newName) => _branches.RenameBranchAsync(oldName, newName);

    public Task<BranchMergeResult> MergeBranchAsync(string branchName, BranchMergeStrategy strategy)
        => _branches.MergeBranchAsync(branchName, strategy);

    public Task<HistoryStitchPreview> PreviewHistoryStitchAsync(string sourceRef)
        => _branches.PreviewHistoryStitchAsync(sourceRef);

    public Task<HistoryStitchResult> StitchHistoryAsync(string sourceRef)
        => throw new NotSupportedException("History stitch execution requires the Git command-line tool.");

    public Task<string> CommitToBranchAsync(CommitToBranchRequest request)
        => _branches.CommitToBranchAsync(request);

    public Task<string> CreateSnapshotBranchAsync(string branchName, bool includeUncommitted)
        => _branches.CreateSnapshotBranchAsync(branchName, includeUncommitted);

    public Task<ArchiveResult> ArchiveSourceZipAsync(ArchiveRequest request, CancellationToken ct = default)
        => throw new NotSupportedException("Archive export requires the Git command-line tool.");

    public Task<IReadOnlyList<CommitInfo>> PickaxeSearchAsync(string term, string? path, CancellationToken ct = default)
        => throw new NotSupportedException("Route through HybridGitBackend.");

    public Task<IReadOnlyList<CommitInfo>> DiffRegexSearchAsync(string pattern, string? path, CancellationToken ct = default)
        => throw new NotSupportedException("Route through HybridGitBackend.");

    public Task<IReadOnlyList<RangeDiffEntry>> RangeDiffAsync(string range1, string range2, CancellationToken ct = default)
        => throw new NotSupportedException("Route through HybridGitBackend.");

    public Task<IReadOnlyList<BlameLine>> BlameAsync(
        string filePath,
        bool ignoreWhitespace,
        bool followMoves,
        CancellationToken ct = default)
        => _analysis.BlameAsync(filePath, ignoreWhitespace, followMoves, ct);

    public Task<string?> GetPriorTipFromReflogAsync() => _rewrite.GetPriorTipFromReflogAsync();

    public Task<CompareResult> CompareRefsAsync(
        string baseRef,
        string compareRef,
        bool threeDot,
        CancellationToken ct = default)
        => _analysis.CompareRefsAsync(baseRef, compareRef, threeDot, ct);

    public Task<IReadOnlyList<WorktreeInfo>> GetWorktreesAsync()
        => throw new NotSupportedException("Route through HybridGitBackend.");

    public Task<string> AddWorktreeAsync(string path, string branchName, bool createBranch)
        => throw new NotSupportedException("Route through HybridGitBackend.");

    public Task RemoveWorktreeAsync(string path, bool force)
        => throw new NotSupportedException("Route through HybridGitBackend.");

    public Task PruneWorktreesAsync()
        => throw new NotSupportedException("Route through HybridGitBackend.");

    private static Task ServerWorkNotSupportedAsync(string operationName) =>
        throw new NotSupportedException(
            $"{operationName} is server work. LibGit2Backend is local-only; route server operations through GitCliBackend so authentication stays with Git.");
}
