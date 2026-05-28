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

        string branch;
        if (repo.Info.IsHeadDetached)
        {
            var sha = repo.Head.Tip?.Sha;
            branch = sha != null
                ? $"detached @ {sha[..Math.Min(7, sha.Length)]}"
                : "(no branch)";
        }
        else
        {
            branch = repo.Head.FriendlyName;
        }

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

        var trackingBranch = repo.Head.TrackedBranch;
        HashSet<string>? localOnlyShas = null;
        CommitRemoteState defaultState;

        if (trackingBranch?.Tip == null || repo.Head.Tip == null)
        {
            defaultState = CommitRemoteState.NoTrackingBranch;
        }
        else
        {
            defaultState = CommitRemoteState.OnRemote;
            localOnlyShas = new HashSet<string>(
                repo.Commits.QueryBy(new LibGit2Sharp.CommitFilter
                {
                    IncludeReachableFrom = repo.Head.Tip,
                    ExcludeReachableFrom = trackingBranch.Tip,
                }).Select(c => c.Id.Sha),
                StringComparer.OrdinalIgnoreCase);
        }

        // Incoming commits: on tracking branch but not on local HEAD
        List<CommitInfo> incomingCommits = [];
        if (trackingBranch?.Tip != null && repo.Head.Tip != null)
        {
            IEnumerable<Commit> incoming = repo.Commits.QueryBy(new LibGit2Sharp.CommitFilter
            {
                SortBy = CommitSortStrategies.Topological | CommitSortStrategies.Time,
                IncludeReachableFrom = trackingBranch.Tip,
                ExcludeReachableFrom = repo.Head.Tip,
            });

            if (filter != null)
            {
                if (!string.IsNullOrEmpty(filter.SelectedAuthorName) && filter.SelectedAuthorName != "All")
                    incoming = incoming.Where(c => string.Equals(c.Author.Name, filter.SelectedAuthorName, StringComparison.Ordinal));
                if (filter.FromDate.HasValue)
                    incoming = incoming.Where(c => c.Author.When.Date >= filter.FromDate.Value.Date);
                if (filter.ToDate.HasValue)
                    incoming = incoming.Where(c => c.Author.When.DateTime < filter.ToDate.Value.Date.AddDays(1));
            }

            incomingCommits = incoming.Select(c => new CommitInfo(
                c.Id.Sha[..7], c.MessageShort, c.Author.When.DateTime,
                c.Author.Name ?? string.Empty, c.Author.Email ?? string.Empty,
                CommitRemoteState.Incoming, c.Id.Sha)).ToList();
        }

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

        var localResult = commits
            .Select(c =>
            {
                var remoteState = localOnlyShas == null
                    ? defaultState
                    : localOnlyShas.Contains(c.Id.Sha) ? CommitRemoteState.LocalOnly : CommitRemoteState.OnRemote;

                return new CommitInfo(
                    c.Id.Sha[..7],
                    c.MessageShort,
                    c.Author.When.DateTime,
                    c.Author.Name ?? string.Empty,
                    c.Author.Email ?? string.Empty,
                    remoteState,
                    c.Id.Sha);
            })
            .ToList();

        // Detect orphaned hash-pairs: incoming commit with same tree as an outgoing commit.
        // This happens when a synced commit is amended locally — old SHA is still on remote
        // (appears as Incoming), new SHA is local (Outgoing). Mark both with the partner's SHA.
        if (incomingCommits.Count > 0 && localOnlyShas != null)
        {
            // Build tree → outgoing-sha map to detect pairs efficiently
            var outgoingTreeMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var commit in repo.Commits.QueryBy(new LibGit2Sharp.CommitFilter
            {
                IncludeReachableFrom = repo.Head.Tip,
                ExcludeReachableFrom = trackingBranch?.Tip,
            }))
            {
                if (commit.Tree?.Sha != null)
                    outgoingTreeMap.TryAdd(commit.Tree.Sha, commit.Id.Sha[..7]);
            }

            // Find incoming commits whose tree matches an outgoing commit
            var incomingPairsBySha = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var inc in incomingCommits)
            {
                var incCommit = repo.Lookup<Commit>(inc.Sha);
                if (incCommit?.Tree?.Sha != null &&
                    outgoingTreeMap.TryGetValue(incCommit.Tree.Sha, out var outSha))
                {
                    incomingPairsBySha[inc.Sha] = outSha;
                }
            }

            if (incomingPairsBySha.Count > 0)
            {
                // Rebuild lists with orphaned-pair annotations
                var outgoingPairBySha = incomingPairsBySha
                    .ToDictionary(kv => kv.Value, kv => kv.Key, StringComparer.OrdinalIgnoreCase);

                incomingCommits = incomingCommits.Select(c =>
                    incomingPairsBySha.TryGetValue(c.Sha, out var partner)
                        ? c with { OrphanedPairSha = partner }
                        : c).ToList();

                localResult = localResult.Select(c =>
                    outgoingPairBySha.TryGetValue(c.Sha, out var partner)
                        ? c with { OrphanedPairSha = partner }
                        : c).ToList();
            }
        }

        // Incoming first, then local (outgoing + synced)
        var result = incomingCommits.Concat(localResult).ToList();
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
            commit.Author.Name ?? string.Empty,
            commit.Author.Email ?? string.Empty,
            commit.Committer.Name ?? string.Empty,
            commit.Committer.Email ?? string.Empty));
    }

    public Task AmendAuthorAsync(AmendAuthorRequest request)
    {
        using var repo = OpenRepository();

        var commit = repo.Head.Tip ?? throw new InvalidOperationException("No HEAD commit.");

        var newAuthor = new Signature(
            request.AuthorName    ?? commit.Author.Name,
            request.AuthorEmail   ?? commit.Author.Email,
            commit.Author.When);

        var newCommitter = new Signature(
            request.CommitterName  ?? commit.Committer.Name,
            request.CommitterEmail ?? commit.Committer.Email,
            DateTimeOffset.Now);

        repo.Commit(commit.Message, newAuthor, newCommitter, new CommitOptions { AmendPreviousCommit = true });
        HeadChanged?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    public Task RewriteCommitsAsync(IEnumerable<CommitRewrite> rewrites)
    {
        using var repo = OpenRepository();

        // Collect all commits oldest-first for deterministic parent mapping
        var allCommits = repo.Commits.QueryBy(new LibGit2Sharp.CommitFilter
        {
            SortBy = CommitSortStrategies.Topological | CommitSortStrategies.Time,
            IncludeReachableFrom = repo.Head,
        }).ToList();
        allCommits.Reverse(); // oldest-first

        // Resolve short SHAs from rewrites to full SHAs
        var rewriteByFullSha = new Dictionary<string, CommitRewrite>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in rewrites)
        {
            var match = allCommits.FirstOrDefault(c =>
                c.Id.Sha.Equals(r.Sha, StringComparison.OrdinalIgnoreCase) ||
                c.Id.Sha.StartsWith(r.Sha, StringComparison.OrdinalIgnoreCase));
            if (match != null)
                rewriteByFullSha[match.Id.Sha] = r;
        }

        if (rewriteByFullSha.Count == 0)
        {
            HeadChanged?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }

        var mapping = new Dictionary<string, Commit>(StringComparer.OrdinalIgnoreCase);
        bool anyChanged = false;

        foreach (var oldCommit in allCommits)
        {
            rewriteByFullSha.TryGetValue(oldCommit.Id.Sha, out var rewrite);

            var oldParents = oldCommit.Parents.ToList();
            var newParents = oldParents
                .Select(p => mapping.TryGetValue(p.Id.Sha, out var mapped) ? mapped : p)
                .ToList();

            bool parentsChanged = newParents.Zip(oldParents, (n, o) => n.Id != o.Id).Any(x => x);

            if (rewrite == null && !parentsChanged)
            {
                mapping[oldCommit.Id.Sha] = oldCommit;
                continue;
            }

            anyChanged = true;

            var newAuthor = new Signature(
                rewrite?.NewAuthorName    ?? oldCommit.Author.Name,
                rewrite?.NewAuthorEmail   ?? oldCommit.Author.Email,
                rewrite?.NewAuthorDate    ?? oldCommit.Author.When);

            var newCommitter = new Signature(
                rewrite?.NewCommitterName  ?? oldCommit.Committer.Name,
                rewrite?.NewCommitterEmail ?? oldCommit.Committer.Email,
                rewrite?.NewCommitterDate  ?? oldCommit.Committer.When);

            var newCommit = repo.ObjectDatabase.CreateCommit(
                newAuthor, newCommitter,
                rewrite?.NewMessage ?? oldCommit.Message,
                oldCommit.Tree,
                newParents,
                prettifyMessage: false);

            mapping[oldCommit.Id.Sha] = newCommit;
        }

        if (anyChanged && repo.Head.Tip != null &&
            mapping.TryGetValue(repo.Head.Tip.Id.Sha, out var newHead))
        {
            repo.Refs.UpdateTarget(repo.Head.Reference, newHead.Id);
        }

        HeadChanged?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    public Task<string> AmendAsync(AmendRequest request)
    {
        using var repo = OpenRepository();

        var commit = repo.Head.Tip ?? throw new InvalidOperationException("No HEAD commit.");
        var offset = DateTimeOffset.Now.Offset;

        var newAuthor = new Signature(
            request.AuthorName    ?? commit.Author.Name,
            request.AuthorEmail   ?? commit.Author.Email,
            new DateTimeOffset(request.NewDate.Year, request.NewDate.Month, request.NewDate.Day,
                request.NewDate.Hour, request.NewDate.Minute, commit.Author.When.Second, offset));

        var newCommitter = new Signature(
            request.CommitterName  ?? commit.Committer.Name,
            request.CommitterEmail ?? commit.Committer.Email,
            new DateTimeOffset(request.NewDate.Year, request.NewDate.Month, request.NewDate.Day,
                request.NewDate.Hour, request.NewDate.Minute, commit.Committer.When.Second, offset));

        var amended = repo.Commit(
            request.NewMessage ?? commit.Message,
            newAuthor, newCommitter,
            new CommitOptions { AmendPreviousCommit = true });
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

    public Task<Dictionary<string, string>> GetAllRefsAsync()
    {
        using var repo = OpenRepository();
        var refs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in repo.Refs)
        {
            var target = r.TargetIdentifier;
            if (target != null)
                refs[r.CanonicalName] = target;
        }
        return Task.FromResult(refs);
    }

    public Task<int> GetStashCountAsync()
    {
        using var repo = OpenRepository();
        return Task.FromResult(repo.Stashes.Count());
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
