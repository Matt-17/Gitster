using System.Runtime.CompilerServices;

using Gitster.Models;
using LibGit2Sharp;

namespace Gitster.Services.Git.LibGit2;

using ViewCommitFilter = Gitster.ViewModels.CommitFilter;

internal sealed class LibGit2HistoryReader
{
    private readonly LibGit2RepositoryContext _context;

    public LibGit2HistoryReader(LibGit2RepositoryContext context) => _context = context;

    public Task<BranchInfo> GetCurrentBranchAsync()
    {
        using var repo = _context.OpenRepository();

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

    public Task<IReadOnlyList<CommitInfo>> GetCommitsAsync(ViewCommitFilter? filter = null)
    {
        using var repo = _context.OpenRepository();

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
                CommitRemoteState.Incoming, c.Id.Sha,
                ParentShas: c.Parents.Select(p => p.Id.Sha).ToList())).ToList();
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
                    c.Id.Sha,
                    ParentShas: c.Parents.Select(p => p.Id.Sha).ToList());
            })
            .ToList();

        if (incomingCommits.Count > 0 && localOnlyShas != null)
        {
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

        var result = incomingCommits.Concat(localResult).ToList();
        return Task.FromResult<IReadOnlyList<CommitInfo>>(result);
    }

    public async IAsyncEnumerable<CommitInfo> EnumerateCommitsAsync(
        ViewCommitFilter? filter = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        using var repo = _context.OpenRepository();
        if (repo.Head?.Tip == null)
            yield break;

        var query = repo.Commits.QueryBy(new LibGit2Sharp.CommitFilter
        {
            SortBy = CommitSortStrategies.Topological | CommitSortStrategies.Time,
            IncludeReachableFrom = repo.Head.Tip,
        });

        var counter = 0;
        foreach (var c in query)
        {
            ct.ThrowIfCancellationRequested();
            if (filter != null && !PassesFilter(c, filter))
                continue;

            yield return new CommitInfo(
                c.Id.Sha[..7],
                c.MessageShort,
                c.Author.When.DateTime,
                c.Author.Name ?? string.Empty,
                c.Author.Email ?? string.Empty,
                CommitRemoteState.OnRemote,
                c.Id.Sha,
                ParentShas: c.Parents.Select(p => p.Id.Sha).ToList());

            if ((++counter & 1023) == 0)
                await Task.Yield();
        }
    }

    public Task<RemoteSets> ComputeRemoteSetsAsync(CancellationToken ct = default)
    {
        using var repo = _context.OpenRepository();
        var headTip = repo.Head?.Tip;
        var tracking = repo.Head?.TrackedBranch;
        var hasRemote = repo.Network.Remotes.Any();
        var hasTracking = tracking?.Tip != null && headTip != null;

        var remoteName = tracking?.RemoteName;
        if (string.IsNullOrEmpty(remoteName) && hasRemote)
            remoteName = repo.Network.Remotes.First().Name;
        string? remoteUrl = null;
        if (!string.IsNullOrEmpty(remoteName))
            remoteUrl = repo.Network.Remotes[remoteName]?.Url;

        var outgoing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var incoming = new List<CommitInfo>();
        var orphaned = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (hasTracking)
        {
            foreach (var c in repo.Commits.QueryBy(new LibGit2Sharp.CommitFilter
            {
                IncludeReachableFrom = headTip,
                ExcludeReachableFrom = tracking!.Tip,
            }))
            {
                ct.ThrowIfCancellationRequested();
                outgoing.Add(c.Id.Sha);
            }

            var incomingCommits = repo.Commits.QueryBy(new LibGit2Sharp.CommitFilter
            {
                SortBy = CommitSortStrategies.Topological | CommitSortStrategies.Time,
                IncludeReachableFrom = tracking.Tip,
                ExcludeReachableFrom = headTip,
            }).ToList();

            foreach (var c in incomingCommits)
                incoming.Add(new CommitInfo(
                    c.Id.Sha[..7], c.MessageShort, c.Author.When.DateTime,
                    c.Author.Name ?? string.Empty, c.Author.Email ?? string.Empty,
                    CommitRemoteState.Incoming, c.Id.Sha,
                    ParentShas: c.Parents.Select(p => p.Id.Sha).ToList()));

            var outgoingTreeMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var sha in outgoing)
            {
                var commit = repo.Lookup<Commit>(sha);
                if (commit?.Tree?.Sha != null)
                    outgoingTreeMap.TryAdd(commit.Tree.Sha, sha);
            }

            foreach (var inc in incomingCommits)
            {
                if (inc.Tree?.Sha != null && outgoingTreeMap.TryGetValue(inc.Tree.Sha, out var outFull))
                {
                    orphaned[inc.Id.Sha] = outFull[..7];
                    orphaned[outFull] = inc.Id.Sha[..7];
                }
            }
        }

        return Task.FromResult(new RemoteSets(
            incoming, outgoing, orphaned, hasTracking, hasRemote, remoteName, remoteUrl));
    }

    public Task<CommitDiff> GetCommitDiffAsync(string sha, CancellationToken ct = default)
    {
        using var repo = _context.OpenRepository();
        var commit = repo.Lookup<Commit>(sha);
        if (commit == null)
            return Task.FromResult(CommitDiff.Empty);

        ct.ThrowIfCancellationRequested();
        var parent = commit.Parents.FirstOrDefault();
        var patch = parent == null
            ? repo.Diff.Compare<Patch>(null, commit.Tree)
            : repo.Diff.Compare<Patch>(parent.Tree, commit.Tree);

        var files = patch
            .Select(e => new DiffFileEntry(e.Path, e.LinesAdded, e.LinesDeleted,
                e.Status switch
                {
                    ChangeKind.Added => "A",
                    ChangeKind.Deleted => "D",
                    ChangeKind.Renamed => "R",
                    _ => "M",
                },
                LibGit2DiffParser.ParseUnifiedDiff(e.Patch)))
            .ToList();

        return Task.FromResult(new CommitDiff(files, patch.LinesAdded, patch.LinesDeleted));
    }

    public Task<CommitDetails> GetCommitAsync(string sha)
    {
        using var repo = _context.OpenRepository();
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

    public Task<string> GetHeadShaAsync()
    {
        using var repo = _context.OpenRepository();
        var sha = repo.Head.Tip?.Sha
            ?? throw new InvalidOperationException("No HEAD commit.");
        return Task.FromResult(sha);
    }

    public Task<string> ResolveRefAsync(string refSpec)
    {
        using var repo = _context.OpenRepository();
        var obj = repo.Lookup(refSpec)
            ?? throw new InvalidOperationException($"Cannot resolve ref: {refSpec}");
        return Task.FromResult(obj.Sha);
    }

    public Task<IReadOnlyList<CommitInfo>> GetCommitsBetweenAsync(string fromSha, string toSha)
    {
        using var repo = _context.OpenRepository();
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
                c.Author.Name ?? string.Empty,
                ParentShas: c.Parents.Select(p => p.Id.Sha).ToList()))
            .ToList();
        return Task.FromResult<IReadOnlyList<CommitInfo>>(result);
    }

    public Task<bool> CommitExistsAsync(string sha)
    {
        using var repo = _context.OpenRepository();
        return Task.FromResult(repo.Lookup<Commit>(sha) != null);
    }

    public Task<IReadOnlyList<string>> GetTagsForCommitAsync(string sha)
    {
        if (string.IsNullOrWhiteSpace(sha))
            throw new ArgumentException("Commit SHA is required.", nameof(sha));

        using var repo = _context.OpenRepository();
        var commit = repo.Lookup<Commit>(sha)
            ?? throw new InvalidOperationException($"Commit not found: {sha}");
        var tags = repo.Tags
            .Where(tag => ResolveTagTargetCommit(tag)?.Id == commit.Id)
            .Select(tag => tag.FriendlyName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        return Task.FromResult<IReadOnlyList<string>>(tags);
    }

    public Task<Dictionary<string, string>> GetAllRefsAsync()
    {
        using var repo = _context.OpenRepository();
        var refs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in repo.Refs)
        {
            var target = r.TargetIdentifier;
            if (target != null)
                refs[r.CanonicalName] = target;
        }
        return Task.FromResult(refs);
    }

    public Task<IReadOnlyList<RefCatalogItem>> GetRefCatalogAsync()
    {
        using var repo = _context.OpenRepository();
        var result = new List<RefCatalogItem>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var branch in repo.Branches.Where(b => b.Tip is not null && !IsRemoteHeadAlias(b)))
        {
            var tip = branch.Tip!;
            var tracked = branch.TrackedBranch;
            var ahead = 0;
            var behind = 0;
            if (tracked?.Tip is not null)
            {
                var divergence = repo.ObjectDatabase.CalculateHistoryDivergence(tip, tracked.Tip);
                ahead = divergence.AheadBy ?? 0;
                behind = divergence.BehindBy ?? 0;
            }

            result.Add(new RefCatalogItem(
                branch.FriendlyName,
                branch.CanonicalName,
                branch.IsRemote ? RefCatalogKind.RemoteBranch : RefCatalogKind.LocalBranch,
                tip.Sha,
                branch.IsCurrentRepositoryHead,
                branch.IsRemote || tracked?.Tip is not null,
                ahead,
                behind));
            seen.Add(branch.CanonicalName);
        }

        foreach (var tag in repo.Tags)
        {
            if (ResolveTagTargetCommit(tag) is not { } commit)
                continue;

            var canonical = $"refs/tags/{tag.FriendlyName}";
            result.Add(new RefCatalogItem(
                tag.FriendlyName,
                canonical,
                RefCatalogKind.Tag,
                commit.Sha,
                IsCurrent: false,
                HasUpstream: true,
                Ahead: 0,
                Behind: 0));
            seen.Add(canonical);
        }

        foreach (var reference in repo.Refs)
        {
            if (seen.Contains(reference.CanonicalName))
                continue;

            if (!TryClassifyUsefulRef(reference.CanonicalName, out var kind, out var displayName))
                continue;

            var commit = repo.Lookup<Commit>(reference.TargetIdentifier);
            if (commit is null)
                continue;

            result.Add(new RefCatalogItem(
                displayName,
                reference.CanonicalName,
                kind,
                commit.Sha,
                IsCurrent: false,
                HasUpstream: true,
                Ahead: 0,
                Behind: 0));
        }

        return Task.FromResult<IReadOnlyList<RefCatalogItem>>(result
            .OrderBy(r => RefCatalogSortOrder(r.Kind))
            .ThenBy(r => r.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList());
    }

    public Task<IReadOnlyList<BranchSummary>> GetBranchesAsync()
    {
        using var repo = _context.OpenRepository();
        var currentName = repo.Head.FriendlyName;
        var result = repo.Branches
            .Select(b => new BranchSummary(
                b.FriendlyName,
                b.Tip?.Sha ?? string.Empty,
                b.IsRemote,
                b.FriendlyName == currentName))
            .OrderBy(b => b.IsRemote)
            .ThenBy(b => b.Name)
            .ToList();
        return Task.FromResult<IReadOnlyList<BranchSummary>>(result);
    }

    public Task<IReadOnlyList<CommitInfo>> GetCommitsForRefAsync(string refName, int maxCount = 200)
    {
        using var repo = _context.OpenRepository();
        var filter = new LibGit2Sharp.CommitFilter
        {
            SortBy = CommitSortStrategies.Topological | CommitSortStrategies.Time,
            IncludeReachableFrom = refName,
        };
        var result = repo.Commits.QueryBy(filter)
            .Take(maxCount)
            .Select(c => new CommitInfo(
                c.Id.Sha.Length >= 7 ? c.Id.Sha[..7] : c.Id.Sha,
                c.MessageShort,
                c.Author.When.DateTime,
                c.Author.Name ?? string.Empty,
                c.Author.Email ?? string.Empty,
                CommitRemoteState.LocalOnly,
                c.Id.Sha,
                ParentShas: c.Parents.Select(p => p.Id.Sha).ToList()))
            .ToList();
        return Task.FromResult<IReadOnlyList<CommitInfo>>(result);
    }

    private static bool PassesFilter(Commit c, ViewCommitFilter filter)
    {
        if (!string.IsNullOrEmpty(filter.SelectedAuthorName) && filter.SelectedAuthorName != "All"
            && !string.Equals(c.Author.Name, filter.SelectedAuthorName, StringComparison.Ordinal))
            return false;
        if (filter.FromDate.HasValue && c.Author.When.Date < filter.FromDate.Value.Date)
            return false;
        if (filter.ToDate.HasValue && c.Author.When.DateTime >= filter.ToDate.Value.Date.AddDays(1))
            return false;
        return true;
    }

    private static Commit? ResolveTagTargetCommit(Tag tag) =>
        tag.PeeledTarget as Commit ?? tag.Target as Commit;

    private static bool IsRemoteHeadAlias(Branch branch) =>
        branch.IsRemote
        && (branch.CanonicalName.EndsWith("/HEAD", StringComparison.OrdinalIgnoreCase)
            || branch.FriendlyName.EndsWith("/HEAD", StringComparison.OrdinalIgnoreCase));

    private static int RefCatalogSortOrder(RefCatalogKind kind) => kind switch
    {
        RefCatalogKind.LocalBranch => 0,
        RefCatalogKind.RemoteBranch => 1,
        RefCatalogKind.Tag => 2,
        RefCatalogKind.Stash => 3,
        RefCatalogKind.Note => 4,
        RefCatalogKind.Replace => 5,
        _ => 6,
    };

    private static bool TryClassifyUsefulRef(
        string canonicalName,
        out RefCatalogKind kind,
        out string displayName)
    {
        if (canonicalName.Equals("refs/stash", StringComparison.Ordinal))
        {
            kind = RefCatalogKind.Stash;
            displayName = "stash";
            return true;
        }

        if (canonicalName.StartsWith("refs/notes/", StringComparison.Ordinal))
        {
            kind = RefCatalogKind.Note;
            displayName = canonicalName["refs/notes/".Length..];
            return !string.IsNullOrWhiteSpace(displayName);
        }

        if (canonicalName.StartsWith("refs/replace/", StringComparison.Ordinal))
        {
            kind = RefCatalogKind.Replace;
            displayName = canonicalName["refs/replace/".Length..];
            return !string.IsNullOrWhiteSpace(displayName);
        }

        kind = default;
        displayName = string.Empty;
        return false;
    }
}
