using Gitster.Models;

namespace Gitster.Services.Git;

/// <summary>
/// Routes Git operations to the appropriate backend:
/// <list type="bullet">
///   <item><see cref="LibGit2Backend"/> handles everything that works without spawning a process.</item>
///   <item><see cref="GitCliBackend"/> handles rebase-class operations that need the CLI.</item>
/// </list>
/// Replace <c>new LibGit2Backend()</c> in DI with <c>new HybridGitBackend()</c>.
/// </summary>
public sealed class HybridGitBackend : IGitBackend
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

    public HybridGitBackend()
    {
        _lib.HeadChanged += (s, e) => HeadChanged?.Invoke(this, e);
        _cli.HeadChanged += (s, e) => HeadChanged?.Invoke(this, e);
    }

    // ── All libgit2 methods ────────────────────────────────────────────────

    public Task<WorkingTreeState> GetWorkingTreeStateAsync()     => _lib.GetWorkingTreeStateAsync();
    public Task<BranchInfo> GetCurrentBranchAsync()             => _lib.GetCurrentBranchAsync();
    public Task<IReadOnlyList<CommitInfo>> GetCommitsAsync(Gitster.ViewModels.CommitFilter? filter = null) => _lib.GetCommitsAsync(filter);
    public Task<CommitDetails> GetCommitAsync(string sha)       => _lib.GetCommitAsync(sha);
    public Task<string> AmendAsync(AmendRequest request)        => _lib.AmendAsync(request);
    public Task AmendAuthorAsync(AmendAuthorRequest request)    => _lib.AmendAuthorAsync(request);
    public Task RewriteCommitsAsync(IEnumerable<CommitRewrite> rewrites) => _lib.RewriteCommitsAsync(rewrites);
    public Task FetchAsync(string remoteName = "origin")        => _lib.FetchAsync(remoteName);
    public Task PullAsync(string remoteName = "origin")         => _lib.PullAsync(remoteName);
    public Task PushAsync(string remoteName = "origin", bool forceWithLease = false) => _lib.PushAsync(remoteName, forceWithLease);
    public Task<string> GetReflogSelectorForHeadAsync()         => _lib.GetReflogSelectorForHeadAsync();
    public Task ResetHardAsync(string targetReference)          => _lib.ResetHardAsync(targetReference);
    public Task<string> GetHeadShaAsync()                       => _lib.GetHeadShaAsync();
    public Task<string> ResolveRefAsync(string refSpec)         => _lib.ResolveRefAsync(refSpec);
    public Task<IReadOnlyList<CommitInfo>> GetCommitsBetweenAsync(string a, string b) => _lib.GetCommitsBetweenAsync(a, b);
    public Task<bool> CommitExistsAsync(string sha)             => _lib.CommitExistsAsync(sha);
    public Task CherryPickAsync(string sha)                     => _lib.CherryPickAsync(sha);
    public Task<Dictionary<string, string>> GetAllRefsAsync()   => _lib.GetAllRefsAsync();
    public Task<int> GetStashCountAsync()                       => _lib.GetStashCountAsync();
    public Task<IReadOnlyList<StashInfo>> GetStashesAsync()     => _lib.GetStashesAsync();
    public Task<string> GetStashDiffAsync(int stashIndex)       => _lib.GetStashDiffAsync(stashIndex);
    public Task ApplyStashAsync(int stashIndex, bool reinstateIndex = true) => _lib.ApplyStashAsync(stashIndex, reinstateIndex);
    public Task PopStashAsync(int stashIndex, bool reinstateIndex = true)   => _lib.PopStashAsync(stashIndex, reinstateIndex);
    public Task DropStashAsync(int stashIndex)                  => _lib.DropStashAsync(stashIndex);
    public Task<string> CreateStashAsync(string message, bool includeUntracked = true) => _lib.CreateStashAsync(message, includeUntracked);
    public Task<string> ConvertStashToBranchAsync(int stashIndex, string branchName)   => _lib.ConvertStashToBranchAsync(stashIndex, branchName);
    public Task<IReadOnlyList<BranchSummary>> GetBranchesAsync()               => _lib.GetBranchesAsync();
    public Task<IReadOnlyList<CommitInfo>> GetCommitsForRefAsync(string refName, int maxCount = 200) => _lib.GetCommitsForRefAsync(refName, maxCount);

    // ── Routed methods (lib or cli depending on context) ──────────────────

    /// <summary>
    /// Fixup always requires CLI.
    /// </summary>
    public Task FixupIntoCommitAsync(string targetSha) => _cli.FixupIntoCommitAsync(targetSha);

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
