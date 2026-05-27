using Gitster.Models;
using LibGit2Sharp;
using System.IO;

namespace Gitster.Services.Git;

public sealed class LibGit2Backend : IGitBackend
{
    public string? RepositoryPath { get; private set; }

    public event EventHandler? HeadChanged;

    public GitCapabilities Capabilities =>
        GitCapabilities.Read | GitCapabilities.BasicWrite | GitCapabilities.ReflogUndo;

    public Task OpenAsync(string path)
    {
        using var repo = new Repository(path);
        RepositoryPath = repo.Info.WorkingDirectory.TrimEnd(Path.DirectorySeparatorChar);
        return Task.CompletedTask;
    }

    public Task<WorkingTreeState> GetWorkingTreeStateAsync()
    {
        using var repo = OpenRepository();

        var gitDir = repo.Info.Path;
        if (Directory.Exists(Path.Combine(gitDir, "rebase-merge")) || Directory.Exists(Path.Combine(gitDir, "rebase-apply")))
        {
            return Task.FromResult<WorkingTreeState>(new WorkingTreeState.Rebasing(0, 0));
        }

        if (File.Exists(Path.Combine(gitDir, "MERGE_HEAD")))
        {
            return Task.FromResult<WorkingTreeState>(new WorkingTreeState.Merging(repo.Head.FriendlyName));
        }

        if (File.Exists(Path.Combine(gitDir, "CHERRY_PICK_HEAD")))
        {
            var sha = File.ReadAllText(Path.Combine(gitDir, "CHERRY_PICK_HEAD")).Trim();
            return Task.FromResult<WorkingTreeState>(new WorkingTreeState.CherryPicking(sha));
        }

        var status = repo.RetrieveStatus();
        var modified = status.Count(e => (e.State & FileStatus.ModifiedInWorkdir) != 0 || (e.State & FileStatus.TypeChangeInWorkdir) != 0 || (e.State & FileStatus.DeletedFromWorkdir) != 0);
        var staged = status.Count(e => (e.State & FileStatus.NewInIndex) != 0 || (e.State & FileStatus.ModifiedInIndex) != 0 || (e.State & FileStatus.DeletedFromIndex) != 0 || (e.State & FileStatus.RenamedInIndex) != 0 || (e.State & FileStatus.TypeChangeInIndex) != 0);
        var untracked = status.Count(e => (e.State & FileStatus.NewInWorkdir) != 0);

        if (modified == 0 && staged == 0 && untracked == 0)
            return Task.FromResult<WorkingTreeState>(new WorkingTreeState.Clean());

        return Task.FromResult<WorkingTreeState>(new WorkingTreeState.Dirty(modified, staged, untracked));
    }

    public Task<BranchInfo> GetCurrentBranchAsync()
    {
        using var repo = OpenRepository();

        var branch = repo.Head.FriendlyName;
        var incoming = 0;
        var outgoing = 0;

        var tracked = repo.Head.TrackedBranch;
        if (tracked?.Tip != null && repo.Head.Tip != null)
        {
            var divergence = repo.ObjectDatabase.CalculateHistoryDivergence(repo.Head.Tip, tracked.Tip);
            outgoing = divergence.AheadBy ?? 0;
            incoming = divergence.BehindBy ?? 0;
        }

        return Task.FromResult(new BranchInfo(branch, incoming, outgoing));
    }

    public Task<IReadOnlyList<CommitInfo>> GetCommitsAsync(Gitster.ViewModels.CommitFilter? filter = null)
    {
        using var repo = OpenRepository();

        IEnumerable<Commit> commits = repo.Commits.QueryBy(new LibGit2Sharp.CommitFilter
        {
            SortBy = CommitSortStrategies.Topological | CommitSortStrategies.Time,
            IncludeReachableFrom = repo.Head,
        });

        if (filter != null)
        {
            if (!string.IsNullOrEmpty(filter.SelectedAuthorName) && filter.SelectedAuthorName != "All")
                commits = commits.Where(c => string.Equals(c.Author.Name, filter.SelectedAuthorName, StringComparison.Ordinal));

            if (filter.FromDate.HasValue)
            {
                var from = filter.FromDate.Value.Date;
                commits = commits.Where(c => c.Author.When.Date >= from);
            }

            if (filter.ToDate.HasValue)
            {
                var toExclusive = filter.ToDate.Value.Date.AddDays(1);
                commits = commits.Where(c => c.Author.When.DateTime < toExclusive);
            }
        }

        var result = commits
            .Select(c => new CommitInfo(
                c.Id.Sha[..7],
                c.MessageShort,
                c.Author.When.DateTime,
                c.Author.Name ?? string.Empty))
            .ToList();

        return Task.FromResult<IReadOnlyList<CommitInfo>>(result);
    }

    public Task<CommitDetails> GetCommitAsync(string sha)
    {
        using var repo = OpenRepository();
        var commit = repo.Lookup<Commit>(sha) ?? throw new InvalidOperationException("Commit not found.");

        return Task.FromResult(new CommitDetails(
            commit.Id.Sha,
            commit.Message,
            commit.Author.When.DateTime,
            commit.Author.Name ?? string.Empty));
    }

    public Task<string> AmendAsync(AmendRequest request)
    {
        using var repo = OpenRepository();

        var commit = repo.Head.Tip ?? throw new InvalidOperationException("No HEAD commit.");
        var author = commit.Author;
        var committer = commit.Committer;
        var offset = DateTimeOffset.Now.Offset;

        var newAuthor = new Signature(
            author.Name,
            author.Email,
            new DateTimeOffset(request.NewDate.Year, request.NewDate.Month, request.NewDate.Day, request.NewDate.Hour, request.NewDate.Minute, author.When.Second, offset));

        var newCommitter = new Signature(
            committer.Name,
            committer.Email,
            new DateTimeOffset(request.NewDate.Year, request.NewDate.Month, request.NewDate.Day, request.NewDate.Hour, request.NewDate.Minute, committer.When.Second, offset));

        var amended = repo.Commit(commit.Message, newAuthor, newCommitter, new CommitOptions { AmendPreviousCommit = true });
        return Task.FromResult(amended.Id.Sha);
    }

    public Task FetchAsync(string remoteName = "origin")
    {
        using var repo = OpenRepository();
        var remote = ResolveRemote(repo, remoteName);
        var specs = remote.FetchRefSpecs.Select(x => x.Specification);
        Commands.Fetch(repo, remote.Name, specs, null, $"Fetch from {remote.Name}");
        return Task.CompletedTask;
    }

    public Task PullAsync(string remoteName = "origin")
    {
        using var repo = OpenRepository();
        _ = ResolveRemote(repo, remoteName);

        var signature = repo.Config.BuildSignature(DateTimeOffset.Now)
            ?? throw new InvalidOperationException("Could not resolve git user signature for pull.");
        Commands.Pull(repo, signature, new PullOptions());
        return Task.CompletedTask;
    }

    public Task PushAsync(string remoteName = "origin", bool forceWithLease = false)
    {
        using var repo = OpenRepository();
        _ = ResolveRemote(repo, remoteName);

        if (forceWithLease)
        {
            var pushRefSpec = $"+{repo.Head.CanonicalName}:{repo.Head.CanonicalName}";
            repo.Network.Push(repo.Network.Remotes[remoteName], pushRefSpec, new PushOptions());
        }
        else
        {
            repo.Network.Push(repo.Head, new PushOptions());
        }

        return Task.CompletedTask;
    }

    public Task<string> GetReflogSelectorForHeadAsync()
    {
        using var repo = OpenRepository();
        var logEntries = repo.Refs.Log("HEAD").ToList();
        if (logEntries.Count < 2)
            throw new InvalidOperationException("No previous HEAD reflog entry found.");

        return Task.FromResult("HEAD@{1}");
    }

    public Task ResetHardAsync(string targetReference)
    {
        using var repo = OpenRepository();
        var commit = repo.Lookup<Commit>(targetReference)
            ?? throw new InvalidOperationException($"Target reference not found: {targetReference}");

        repo.Reset(ResetMode.Hard, commit);
        HeadChanged?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    public Task<string> GetHeadShaAsync()
    {
        using var repo = OpenRepository();
        var sha = repo.Head.Tip?.Sha
            ?? throw new InvalidOperationException("No HEAD commit.");
        return Task.FromResult(sha);
    }

    public Task<string> ResolveRefAsync(string refSpec)
    {
        using var repo = OpenRepository();
        var obj = repo.Lookup(refSpec)
            ?? throw new InvalidOperationException($"Cannot resolve ref: {refSpec}");
        return Task.FromResult(obj.Sha);
    }

    public Task<IReadOnlyList<CommitInfo>> GetCommitsBetweenAsync(string fromSha, string toSha)
    {
        using var repo = OpenRepository();
        var filter = new LibGit2Sharp.CommitFilter
        {
            IncludeReachableFrom = toSha,
            ExcludeReachableFrom = fromSha,
            SortBy = CommitSortStrategies.Topological,
        };
        var result = repo.Commits.QueryBy(filter)
            .Select(c => new CommitInfo(
                c.Id.Sha.Length >= 7 ? c.Id.Sha[..7] : c.Id.Sha,
                c.MessageShort,
                c.Author.When.DateTime,
                c.Author.Name ?? string.Empty))
            .ToList();
        return Task.FromResult<IReadOnlyList<CommitInfo>>(result);
    }

    public Task<bool> CommitExistsAsync(string sha)
    {
        using var repo = OpenRepository();
        return Task.FromResult(repo.Lookup<Commit>(sha) != null);
    }

    public Task CherryPickAsync(string sha)
    {
        using var repo = OpenRepository();
        var commit = repo.Lookup<Commit>(sha)
            ?? throw new InvalidOperationException($"Commit not found: {sha}");
        var sig = repo.Config.BuildSignature(DateTimeOffset.Now)
            ?? new Signature("Gitster", "gitster@local", DateTimeOffset.Now);
        var result = repo.CherryPick(commit, sig);
        if (result.Status == CherryPickStatus.Conflicts)
            throw new InvalidOperationException($"Cherry-pick produced conflicts on {sha[..Math.Min(7, sha.Length)]}");
        return Task.CompletedTask;
    }

    private Repository OpenRepository()
    {
        if (string.IsNullOrWhiteSpace(RepositoryPath))
            throw new InvalidOperationException("Repository is not opened.");

        return new Repository(RepositoryPath);
    }

    private static Remote ResolveRemote(Repository repo, string remoteName)
    {
        var remote = string.IsNullOrWhiteSpace(remoteName)
            ? repo.Network.Remotes.FirstOrDefault()
            : repo.Network.Remotes[remoteName];

        return remote ?? throw new InvalidOperationException("No remote found.");
    }
}
