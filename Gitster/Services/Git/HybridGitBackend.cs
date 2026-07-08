using Gitster.Models;

namespace Gitster.Services.Git;

/// <summary>
/// Routes Git operations to the appropriate backend:
/// <list type="bullet">
///   <item><see cref="LibGit2Backend"/> handles local repository work only.</item>
///   <item><see cref="GitCliBackend"/> handles server operations and CLI-only workflows.</item>
/// </list>
/// Replace <c>new LibGit2Backend()</c> in DI with <c>new HybridGitBackend()</c>.
/// </summary>
public sealed class HybridGitBackend : IGitBackend, IRepositoryReadProvider
{
    private readonly LibGit2Backend _lib = new();
    private readonly GitCliBackend  _cli = new();

    public string? RepositoryPath => _lib.RepositoryPath;

    public event EventHandler? HeadChanged;

    public GitCapabilities Capabilities =>
        _lib.Capabilities | _cli.Capabilities;

    public async Task OpenAsync(string path)
    {
        await _lib.OpenAsync(path);
        await _cli.OpenAsync(path);
    }

    public LibGit2Sharp.Repository OpenRepository(string repoPath) => _lib.OpenRepository(repoPath);

    public HybridGitBackend()
    {
        _lib.HeadChanged += (s, e) => HeadChanged?.Invoke(this, e);
        _cli.HeadChanged += (s, e) => HeadChanged?.Invoke(this, e);
    }

    // ── All libgit2 methods ────────────────────────────────────────────────

    public Task<WorkingTreeState> GetWorkingTreeStateAsync()     => _lib.GetWorkingTreeStateAsync();
    public Task<BranchInfo> GetCurrentBranchAsync()             => _lib.GetCurrentBranchAsync();
    public Task<IReadOnlyList<CommitInfo>> GetCommitsAsync(Gitster.ViewModels.CommitFilter? filter = null) => _lib.GetCommitsAsync(filter);
    public IAsyncEnumerable<CommitInfo> EnumerateCommitsAsync(Gitster.ViewModels.CommitFilter? filter = null, CancellationToken ct = default) => _lib.EnumerateCommitsAsync(filter, ct);
    public Task<RemoteSets> ComputeRemoteSetsAsync(CancellationToken ct = default) => _lib.ComputeRemoteSetsAsync(ct);
    public Task<CommitDetails> GetCommitAsync(string sha)       => _lib.GetCommitAsync(sha);
    public Task<CommitDiff> GetCommitDiffAsync(string sha, CancellationToken ct = default) => _lib.GetCommitDiffAsync(sha, ct);
    public Task<WorkingTreeStatus> GetWorkingTreeStatusAsync()  => _lib.GetWorkingTreeStatusAsync();
    public Task StageAsync(IEnumerable<string> paths)           => _lib.StageAsync(paths);
    public Task UnstageAsync(IEnumerable<string> paths)         => _lib.UnstageAsync(paths);
    public Task StageAllAsync()                                 => _lib.StageAllAsync();
    public Task<string> CommitAsync(CommitRequest request)      => _lib.CommitAsync(request);
    public Task<string> AmendAsync(AmendRequest request)        => _lib.AmendAsync(request);
    public Task AmendAuthorAsync(AmendAuthorRequest request)    => _lib.AmendAuthorAsync(request);
    public Task RewriteCommitsAsync(IEnumerable<CommitRewrite> rewrites, string? branchName = null) => _lib.RewriteCommitsAsync(rewrites, branchName);
    public Task RemoveFileChangeFromCommitAsync(string sha, string path, string? branchName = null) => _lib.RemoveFileChangeFromCommitAsync(sha, path, branchName);
    public Task FetchAsync(string remoteName = "origin", CancellationToken ct = default)
        => RunServerOperationAsync("Fetch", () => _cli.FetchAsync(remoteName, ct));

    public Task PullAsync(string remoteName = "origin", CancellationToken ct = default)
        => RunServerOperationAsync("Pull", () => _cli.PullAsync(remoteName, ct));

    public Task PushAsync(string remoteName = "origin", PushMode mode = PushMode.Normal, CancellationToken ct = default)
        => RunServerOperationAsync("Push", () => _cli.PushAsync(remoteName, mode, ct));

    public Task PushThroughCommitAsync(string commitSha, string remoteName = "origin", CancellationToken ct = default)
        => RunServerOperationAsync("Push through commit", () => _cli.PushThroughCommitAsync(commitSha, remoteName, ct));

    private static async Task RunServerOperationAsync(string operationName, Func<Task> operation)
    {
        if (!GitCli.IsAvailable)
            await GitCli.DetectAsync();

        if (!GitCli.IsAvailable)
            throw new InvalidOperationException(
                $"{operationName} requires the Git command-line tool. Gitster never uses LibGit2Sharp for server operations because authentication must stay with Git's credential flow. Install Git for Windows and restart Gitster.");

        await operation();
    }
    public Task<string> GetReflogSelectorForHeadAsync()         => _lib.GetReflogSelectorForHeadAsync();
    public Task ResetMixedAsync(string targetReference, string? branchName = null) => _lib.ResetMixedAsync(targetReference, branchName);
    public Task ResetHardAsync(string targetReference, string? branchName = null)  => _lib.ResetHardAsync(targetReference, branchName);
    public Task<string> GetHeadShaAsync()                       => _lib.GetHeadShaAsync();
    public Task<string> ResolveRefAsync(string refSpec)         => _lib.ResolveRefAsync(refSpec);
    public Task<IReadOnlyList<CommitInfo>> GetCommitsBetweenAsync(string a, string b) => _lib.GetCommitsBetweenAsync(a, b);
    public Task<bool> CommitExistsAsync(string sha)             => _lib.CommitExistsAsync(sha);
    public Task CheckoutCommitDetachedAsync(string sha)         => _lib.CheckoutCommitDetachedAsync(sha);
    public Task CherryPickAsync(string sha)                     => _lib.CherryPickAsync(sha);
    public Task<string> CreateTagAsync(string name, string targetSha) => _lib.CreateTagAsync(name, targetSha);
    public Task<IReadOnlyList<string>> GetTagsForCommitAsync(string sha) => _lib.GetTagsForCommitAsync(sha);
    public Task PushTagAsync(string tagName, string remoteName = "origin", CancellationToken ct = default)
        => RunServerOperationAsync("Push tag", () => _cli.PushTagAsync(tagName, remoteName, ct));
    public Task RevertCommitAsync(string sha)                   => _lib.RevertCommitAsync(sha);
    public Task<Dictionary<string, string>> GetAllRefsAsync()   => _lib.GetAllRefsAsync();
    public Task<IReadOnlyList<RefCatalogItem>> GetRefCatalogAsync() => _lib.GetRefCatalogAsync();
    public Task<int> GetStashCountAsync()                       => _lib.GetStashCountAsync();
    public Task<IReadOnlyList<StashInfo>> GetStashesAsync()     => _lib.GetStashesAsync();
    public Task<CommitDiff> GetStashDiffAsync(int stashIndex, CancellationToken ct = default) => _lib.GetStashDiffAsync(stashIndex, ct);
    public Task ApplyStashAsync(int stashIndex, bool reinstateIndex = true) => _lib.ApplyStashAsync(stashIndex, reinstateIndex);
    public Task PopStashAsync(int stashIndex, bool reinstateIndex = true)   => _lib.PopStashAsync(stashIndex, reinstateIndex);
    public Task DropStashAsync(int stashIndex)                  => _lib.DropStashAsync(stashIndex);
    public Task<string> CreateStashAsync(string message, bool includeUntracked = true) => _lib.CreateStashAsync(message, includeUntracked);
    public Task<string> ConvertStashToBranchAsync(int stashIndex, string branchName)   => _lib.ConvertStashToBranchAsync(stashIndex, branchName);
    public Task<IReadOnlyList<BranchSummary>> GetBranchesAsync()               => _lib.GetBranchesAsync();
    public Task<IReadOnlyList<CommitInfo>> GetCommitsForRefAsync(string refName, int maxCount = 200) => _lib.GetCommitsForRefAsync(refName, maxCount);
    public Task<bool> AreCommitsContiguousAsync(IReadOnlyList<string> shas)    => _lib.AreCommitsContiguousAsync(shas);
    public Task ReorderCommitsAsync(IReadOnlyList<string> shasNewestFirst, IReadOnlyList<string> reorderedShasNewestFirst, string? branchName = null) => _lib.ReorderCommitsAsync(shasNewestFirst, reorderedShasNewestFirst, branchName);
    public Task SplitCommitAsync(string sha, IReadOnlyList<string> firstCommitPaths, string firstMessage, string secondMessage, string? branchName = null) => _lib.SplitCommitAsync(sha, firstCommitPaths, firstMessage, secondMessage, branchName);
    public Task<string> CreateOrphanBranchAsync(string branchName, bool commitCurrentTree) => _lib.CreateOrphanBranchAsync(branchName, commitCurrentTree);
    public Task<string> RescueDetachedHeadAsync(string branchName) => _lib.RescueDetachedHeadAsync(branchName);

    // ── Phase 3: branch ops, commit-to-branch, snapshot (libgit2) ─────────
    public Task<IReadOnlyList<BranchListItem>> GetBranchListAsync()            => _lib.GetBranchListAsync();
    public Task CheckoutBranchAsync(string branchName)                        => _lib.CheckoutBranchAsync(branchName);
    public Task<string> CreateBranchAsync(string name, string startPointSha)  => _lib.CreateBranchAsync(name, startPointSha);
    public Task DeleteBranchAsync(string name, bool force)                    => _lib.DeleteBranchAsync(name, force);
    public Task RenameBranchAsync(string oldName, string newName)             => _lib.RenameBranchAsync(oldName, newName);
    public Task<BranchMergeResult> MergeBranchAsync(string branchName, BranchMergeStrategy strategy) => _lib.MergeBranchAsync(branchName, strategy);
    public Task<HistoryStitchPreview> PreviewHistoryStitchAsync(string sourceRef) => _lib.PreviewHistoryStitchAsync(sourceRef);
    public Task<HistoryStitchResult> StitchHistoryAsync(string sourceRef)     => _cli.StitchHistoryAsync(sourceRef);
    public Task<string> CommitToBranchAsync(CommitToBranchRequest request)    => _lib.CommitToBranchAsync(request);
    public Task<string> CreateSnapshotBranchAsync(string branchName, bool includeUncommitted) => _lib.CreateSnapshotBranchAsync(branchName, includeUncommitted);
    public Task<ArchiveResult> ArchiveSourceZipAsync(ArchiveRequest request, CancellationToken ct = default) => _cli.ArchiveSourceZipAsync(request, ct);

    // ── Phase 4: Search & Analysis (CLI for pickaxe/regex/range-diff/blame) ─
    public Task<IReadOnlyList<CommitInfo>> PickaxeSearchAsync(string term, string? path, CancellationToken ct = default) => _cli.PickaxeSearchAsync(term, path, ct);
    public Task<IReadOnlyList<CommitInfo>> DiffRegexSearchAsync(string pattern, string? path, CancellationToken ct = default) => _cli.DiffRegexSearchAsync(pattern, path, ct);
    public Task<IReadOnlyList<RangeDiffEntry>> RangeDiffAsync(string range1, string range2, CancellationToken ct = default) => _cli.RangeDiffAsync(range1, range2, ct);
    public Task<IReadOnlyList<BlameLine>> BlameAsync(string filePath, bool ignoreWhitespace, bool followMoves, CancellationToken ct = default)
        // The robust whitespace/move-following blame needs the CLI; without it fall back to libgit2.
        => (followMoves || ignoreWhitespace) && GitCli.IsAvailable
            ? _cli.BlameAsync(filePath, ignoreWhitespace, followMoves, ct)
            : _lib.BlameAsync(filePath, ignoreWhitespace, followMoves, ct);
    public Task<string?> GetPriorTipFromReflogAsync() => _lib.GetPriorTipFromReflogAsync();
    public Task<CompareResult> CompareRefsAsync(string baseRef, string compareRef, bool threeDot, CancellationToken ct = default) => _lib.CompareRefsAsync(baseRef, compareRef, threeDot, ct);

    // ── Phase 3: worktrees (CLI) ──────────────────────────────────────────
    public Task<IReadOnlyList<WorktreeInfo>> GetWorktreesAsync()              => _cli.GetWorktreesAsync();
    public Task<string> AddWorktreeAsync(string path, string branchName, bool createBranch) => _cli.AddWorktreeAsync(path, branchName, createBranch);
    public Task RemoveWorktreeAsync(string path, bool force)                  => _cli.RemoveWorktreeAsync(path, force);
    public Task PruneWorktreesAsync()                                         => _cli.PruneWorktreesAsync();

    // ── Routed methods (lib or cli depending on context) ──────────────────

    /// <summary>
    /// Fixup always requires CLI.
    /// </summary>
    public Task FixupIntoCommitAsync(string targetSha) => _cli.FixupIntoCommitAsync(targetSha);

    public Task FixupCommitIntoCommitAsync(string sourceSha, string targetSha)
        => _cli.FixupCommitIntoCommitAsync(sourceSha, targetSha);

    /// <summary>
    /// Reword HEAD via libgit2 amend; older commits via CLI interactive rebase.
    /// </summary>
    public async Task RewordCommitAsync(string sha, string newMessage)
    {
        var headSha = await _lib.GetHeadShaAsync();
        var isHead  = headSha.StartsWith(sha, StringComparison.OrdinalIgnoreCase) ||
                      sha.StartsWith(headSha, StringComparison.OrdinalIgnoreCase);

        if (isHead)
        {
            // Fast path: amend the message only, keep the current timestamp
            var headCommit = await _lib.GetCommitAsync(headSha);
            await _lib.AmendAsync(new AmendRequest(headCommit.Date, NewMessage: newMessage));
            HeadChanged?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            await _cli.RewordCommitAsync(sha, newMessage);
        }
    }

    /// <summary>
    /// Squash: uses libgit2 soft-reset when HEAD is in the selection;
    /// otherwise uses CLI interactive rebase.
    /// shas are in newest-first order (as they appear in the commit list).
    /// </summary>
    public async Task SquashCommitsAsync(
        IReadOnlyList<string> shas,
        string combinedMessage,
        DateTimeOffset? overrideDate)
    {
        var headSha = await _lib.GetHeadShaAsync();
        var includesHead = shas.Any(s =>
            headSha.StartsWith(s, StringComparison.OrdinalIgnoreCase) ||
            s.StartsWith(headSha, StringComparison.OrdinalIgnoreCase));

        if (includesHead)
            await _lib.SquashCommitsHeadAsync(shas, combinedMessage, overrideDate);
        else
            await _cli.SquashCommitsAsync(shas, combinedMessage, overrideDate);
    }
}
