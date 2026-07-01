using Gitster.Helpers;
using Gitster.Models;
using LibGit2Sharp;
using System.IO;
using System.Runtime.CompilerServices;

namespace Gitster.Services.Git;

public sealed class LibGit2Backend : IGitBackend
{
    public string? RepositoryPath { get; private set; }

    public event EventHandler? HeadChanged;

    public GitCapabilities Capabilities =>
        GitCapabilities.Read | GitCapabilities.BasicWrite
        | GitCapabilities.ReflogUndo | GitCapabilities.StashManagement;

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
        var rebaseMerge = Path.Combine(gitDir, "rebase-merge");
        var rebaseApply = Path.Combine(gitDir, "rebase-apply");
        if (Directory.Exists(rebaseMerge) || Directory.Exists(rebaseApply))
        {
            var (step, total) = ReadRebaseProgress(rebaseMerge, rebaseApply);
            return Task.FromResult<WorkingTreeState>(new WorkingTreeState.Rebasing(step, total));
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

    // ── A0.1 — progressive HEAD→parent streaming ───────────────────────────

    /// <summary>
    /// Yields commits strictly HEAD→parent (newest first), with a neutral remote
    /// state. The real remote state is filled in later by <see cref="ComputeRemoteSetsAsync"/>
    /// so first paint never waits on a divergence walk.
    /// </summary>
    public async IAsyncEnumerable<CommitInfo> EnumerateCommitsAsync(
        Gitster.ViewModels.CommitFilter? filter = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        using var repo = OpenRepository();
        if (repo.Head?.Tip == null)
            yield break;

        var query = repo.Commits.QueryBy(new LibGit2Sharp.CommitFilter
        {
            // Topological ensures every parent appears after its child regardless of
            // manipulated timestamps; anchored at HEAD this is the clean HEAD→root walk (A1).
            SortBy = CommitSortStrategies.Topological | CommitSortStrategies.Time,
            IncludeReachableFrom = repo.Head.Tip,
        });

        int counter = 0;
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
                c.Id.Sha);

            // Periodically yield the thread so cancellation stays responsive on huge repos.
            if ((++counter & 1023) == 0)
                await Task.Yield();
        }
    }

    private static bool PassesFilter(Commit c, Gitster.ViewModels.CommitFilter filter)
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

    // ── A0.4 — incoming/outgoing computation (background) ──────────────────

    public Task<RemoteSets> ComputeRemoteSetsAsync(CancellationToken ct = default)
    {
        using var repo = OpenRepository();
        var headTip = repo.Head?.Tip;
        var tracking = repo.Head?.TrackedBranch;
        bool hasRemote = repo.Network.Remotes.Any();
        bool hasTracking = tracking?.Tip != null && headTip != null;

        string? remoteName = tracking?.RemoteName;
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
                    CommitRemoteState.Incoming, c.Id.Sha));

            // Orphaned hash-pair detection: an incoming commit whose tree matches an
            // outgoing commit is the pre-amend copy still on the remote.
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

    // ── A2 — commit panel: working-tree status, staging, commit ────────────

    public Task<WorkingTreeStatus> GetWorkingTreeStatusAsync()
    {
        using var repo = OpenRepository();
        var headTree = repo.Head?.Tip?.Tree;

        var staged = new List<WorkingTreeFile>();
        var unstaged = new List<WorkingTreeFile>();

        // Staged = HEAD tree ↔ index.
        try
        {
            foreach (var e in repo.Diff.Compare<Patch>(headTree, DiffTargets.Index))
                staged.Add(new WorkingTreeFile(e.Path, MapStaged(e.Status), Staged: true, e.LinesAdded, e.LinesDeleted));
        }
        catch { /* leave staged empty on diff error */ }

        // Unstaged + untracked = index ↔ working directory (Added here means untracked).
        try
        {
            foreach (var e in repo.Diff.Compare<Patch>(null, includeUntracked: true, explicitPathsOptions: null))
                unstaged.Add(new WorkingTreeFile(e.Path, MapWorkdir(e.Status), Staged: false, e.LinesAdded, e.LinesDeleted));
        }
        catch { /* leave unstaged empty on diff error */ }

        return Task.FromResult(new WorkingTreeStatus(staged, unstaged));
    }

    private static WorkingFileStatus MapStaged(ChangeKind kind) => kind switch
    {
        ChangeKind.Added      => WorkingFileStatus.Added,
        ChangeKind.Deleted    => WorkingFileStatus.Deleted,
        ChangeKind.Renamed    => WorkingFileStatus.Renamed,
        ChangeKind.TypeChanged => WorkingFileStatus.TypeChange,
        ChangeKind.Conflicted => WorkingFileStatus.Conflicted,
        _                     => WorkingFileStatus.Modified,
    };

    private static WorkingFileStatus MapWorkdir(ChangeKind kind) => kind switch
    {
        ChangeKind.Added      => WorkingFileStatus.Untracked, // present in workdir, not index
        ChangeKind.Deleted    => WorkingFileStatus.Deleted,
        ChangeKind.Renamed    => WorkingFileStatus.Renamed,
        ChangeKind.TypeChanged => WorkingFileStatus.TypeChange,
        ChangeKind.Conflicted => WorkingFileStatus.Conflicted,
        _                     => WorkingFileStatus.Modified,
    };

    public Task StageAsync(IEnumerable<string> paths)
    {
        using var repo = OpenRepository();
        var list = paths.ToList();
        if (list.Count > 0)
            Commands.Stage(repo, list);
        return Task.CompletedTask;
    }

    public Task UnstageAsync(IEnumerable<string> paths)
    {
        using var repo = OpenRepository();
        var list = paths.ToList();
        if (list.Count > 0)
            Commands.Unstage(repo, list);
        return Task.CompletedTask;
    }

    public Task StageAllAsync()
    {
        using var repo = OpenRepository();
        Commands.Stage(repo, "*");
        return Task.CompletedTask;
    }

    public Task<string> CommitAsync(CommitRequest request)
    {
        using var repo = OpenRepository();
        var fallback = repo.Config.BuildSignature(DateTimeOffset.Now)
            ?? new Signature("Gitster", "gitster@local", DateTimeOffset.Now);

        if (request.Amend)
        {
            var head = repo.Head.Tip ?? throw new InvalidOperationException("No HEAD commit to amend.");
            var author = new Signature(
                request.AuthorName ?? head.Author.Name,
                request.AuthorEmail ?? head.Author.Email,
                head.Author.When); // keep the original author date on amend
            var committer = new Signature(
                request.CommitterName ?? fallback.Name,
                request.CommitterEmail ?? fallback.Email,
                DateTimeOffset.Now);
            var amended = repo.Commit(request.Message, author, committer,
                new CommitOptions { AmendPreviousCommit = true });
            HeadChanged?.Invoke(this, EventArgs.Empty);
            return Task.FromResult(amended.Sha);
        }
        else
        {
            var when = DateTimeOffset.Now;
            var author = new Signature(
                request.AuthorName ?? fallback.Name,
                request.AuthorEmail ?? fallback.Email, when);
            var committer = new Signature(
                request.CommitterName ?? fallback.Name,
                request.CommitterEmail ?? fallback.Email, when);
            // repo.Commit updates the current branch ref — it never detaches HEAD.
            var commit = repo.Commit(request.Message, author, committer);
            HeadChanged?.Invoke(this, EventArgs.Empty);
            return Task.FromResult(commit.Sha);
        }
    }

    public Task<CommitDiff> GetCommitDiffAsync(string sha, CancellationToken ct = default)
    {
        using var repo = OpenRepository();
        var commit = repo.Lookup<Commit>(sha);
        if (commit == null)
            return Task.FromResult(CommitDiff.Empty);

        ct.ThrowIfCancellationRequested();
        var parent = commit.Parents.FirstOrDefault();
        // The root commit (no parent) diffs against the empty tree (null oldTree).
        var patch = parent == null
            ? repo.Diff.Compare<Patch>(null, commit.Tree)
            : repo.Diff.Compare<Patch>(parent.Tree, commit.Tree);

        var files = patch
            .Select(e => new DiffFileEntry(e.Path, e.LinesAdded, e.LinesDeleted,
                e.Status switch
                {
                    ChangeKind.Added   => "A",
                    ChangeKind.Deleted => "D",
                    ChangeKind.Renamed => "R",
                    _                  => "M",
                },
                ParseUnifiedDiff(e.Patch)))
            .ToList();

        return Task.FromResult(new CommitDiff(files, patch.LinesAdded, patch.LinesDeleted));
    }

    /// <summary>Parses a per-file unified-diff patch body into typed lines for inline rendering (B7).</summary>
    internal static List<DiffLine> ParseUnifiedDiff(string? patchText)
    {
        var lines = new List<DiffLine>();
        if (string.IsNullOrEmpty(patchText)) return lines;

        foreach (var raw in patchText.Replace("\r\n", "\n").Split('\n'))
        {
            if (raw.Length == 0) { lines.Add(new DiffLine(DiffLineKind.Context, string.Empty)); continue; }
            // Skip the file headers libgit2 includes in the per-entry patch; keep hunks + body.
            if (raw.StartsWith("diff ", StringComparison.Ordinal) ||
                raw.StartsWith("index ", StringComparison.Ordinal) ||
                raw.StartsWith("--- ", StringComparison.Ordinal) ||
                raw.StartsWith("+++ ", StringComparison.Ordinal) ||
                raw.StartsWith("new file", StringComparison.Ordinal) ||
                raw.StartsWith("deleted file", StringComparison.Ordinal) ||
                raw.StartsWith("similarity", StringComparison.Ordinal) ||
                raw.StartsWith("rename ", StringComparison.Ordinal))
                continue;

            var kind = raw[0] switch
            {
                '@' => DiffLineKind.Hunk,
                '+' => DiffLineKind.Added,
                '-' => DiffLineKind.Removed,
                _   => DiffLineKind.Context,
            };
            lines.Add(new DiffLine(kind, raw));
        }
        return lines;
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
        EnsureAttachedHead(repo, "amend commits");

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

    public Task RewriteCommitsAsync(IEnumerable<CommitRewrite> rewrites, string? branchName = null)
    {
        using var repo = OpenRepository();
        var branch = AttachBranchForHistoryRewrite(repo, branchName, "rewrite commits");
        var branchTip = branch.Tip ?? throw new InvalidOperationException("No HEAD commit.");

        // Collect all commits oldest-first for deterministic parent mapping
        var allCommits = repo.Commits.QueryBy(new LibGit2Sharp.CommitFilter
        {
            SortBy = CommitSortStrategies.Topological | CommitSortStrategies.Time,
            IncludeReachableFrom = branchTip,
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

        if (anyChanged && mapping.TryGetValue(branchTip.Id.Sha, out var newHead))
        {
            repo.Refs.UpdateTarget(branch.Reference, newHead.Id, "rewrite commits");
        }

        HeadChanged?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    public Task RemoveFileChangeFromCommitAsync(string sha, string path, string? branchName = null)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Path is required.", nameof(path));

        using var repo = OpenRepository();
        var branch = AttachBranchForHistoryRewrite(repo, branchName, "remove a file change from a commit");
        var branchTip = branch.Tip ?? throw new InvalidOperationException("No HEAD commit.");
        var normalizedPath = NormalizeRepoPath(path);

        EnsureCleanWorkingTree(repo, "remove a file change from a commit");

        var firstParentChain = GetFirstParentChainOldestFirst(branchTip);
        var targetIndex = firstParentChain.FindIndex(c => ShaMatches(c, sha));
        if (targetIndex < 0)
            throw new InvalidOperationException(
                "The selected commit is not on the current branch's first-parent history.");

        var affectedCommits = firstParentChain.Skip(targetIndex).ToList();
        var mergeCommit = affectedCommits.FirstOrDefault(c => c.Parents.Count() > 1);
        if (mergeCommit is not null)
            throw new InvalidOperationException(
                $"Cannot remove a file change through merge commit {ShortSha(mergeCommit.Sha)}.");

        var targetCommit = firstParentChain[targetIndex];
        if (!CommitTouchesPath(repo, targetCommit, normalizedPath))
            throw new InvalidOperationException(
                $"Commit {ShortSha(targetCommit.Sha)} does not change '{normalizedPath}'.");

        var laterTouch = affectedCommits
            .Skip(1)
            .FirstOrDefault(c => CommitTouchesPath(repo, c, normalizedPath));
        if (laterTouch is not null)
            throw new InvalidOperationException(
                $"Cannot remove '{normalizedPath}' because later commit {ShortSha(laterTouch.Sha)} also changes it.");

        var mapping = new Dictionary<string, Commit>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < firstParentChain.Count; i++)
        {
            var oldCommit = firstParentChain[i];
            if (i < targetIndex)
            {
                mapping[oldCommit.Sha] = oldCommit;
                continue;
            }

            var oldParents = oldCommit.Parents.ToList();
            var newParents = oldParents
                .Select(p => mapping.TryGetValue(p.Sha, out var mapped) ? mapped : p)
                .ToList();

            var oldParentTree = oldParents.FirstOrDefault()?.Tree;
            var newParentTree = newParents.FirstOrDefault()?.Tree;
            var excludedPath = i == targetIndex ? normalizedPath : null;
            var newTree = BuildReplayedTree(repo, oldParentTree, oldCommit.Tree, newParentTree, excludedPath);

            var newCommit = repo.ObjectDatabase.CreateCommit(
                oldCommit.Author,
                oldCommit.Committer,
                oldCommit.Message,
                newTree,
                newParents,
                prettifyMessage: false);

            mapping[oldCommit.Sha] = newCommit;
        }

        if (mapping.TryGetValue(branchTip.Sha, out var newHead))
            repo.Refs.UpdateTarget(branch.Reference, newHead.Id, $"remove {normalizedPath} from {ShortSha(targetCommit.Sha)}");

        HeadChanged?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    private static Tree BuildReplayedTree(
        Repository repo,
        Tree? oldParentTree,
        Tree oldCommitTree,
        Tree? newParentTree,
        string? excludedPath)
    {
        var definition = newParentTree is null
            ? new TreeDefinition()
            : TreeDefinition.From(newParentTree);

        foreach (var change in repo.Diff.Compare<TreeChanges>(oldParentTree, oldCommitTree))
        {
            if (excludedPath is not null && ChangeTouchesPath(change, excludedPath))
                continue;

            ApplyTreeChange(definition, oldCommitTree, change);
        }

        return repo.ObjectDatabase.CreateTree(definition);
    }

    private static void ApplyTreeChange(TreeDefinition definition, Tree oldCommitTree, TreeEntryChanges change)
    {
        var path = NormalizeRepoPath(change.Path);

        if (change.Status == ChangeKind.Deleted)
        {
            definition.Remove(path);
            return;
        }

        if (change.Status == ChangeKind.Renamed && !string.IsNullOrWhiteSpace(change.OldPath))
            definition.Remove(NormalizeRepoPath(change.OldPath));

        var entry = oldCommitTree[path]
            ?? throw new InvalidOperationException($"Could not find tree entry for '{path}'.");
        definition.Add(path, entry);
    }

    private static bool CommitTouchesPath(Repository repo, Commit commit, string normalizedPath)
    {
        var parent = commit.Parents.FirstOrDefault();
        return repo.Diff.Compare<TreeChanges>(parent?.Tree, commit.Tree)
            .Any(change => ChangeTouchesPath(change, normalizedPath));
    }

    private static bool ChangeTouchesPath(TreeEntryChanges change, string normalizedPath) =>
        PathMatches(change.Path, normalizedPath)
        || (!string.IsNullOrWhiteSpace(change.OldPath) && PathMatches(change.OldPath, normalizedPath));

    private static bool PathMatches(string path, string normalizedPath) =>
        string.Equals(NormalizeRepoPath(path), normalizedPath, StringComparison.Ordinal);

    private static List<Commit> GetFirstParentChainOldestFirst(Commit tip)
    {
        var commits = new List<Commit>();
        Commit? current = tip;
        while (current is not null)
        {
            commits.Add(current);
            current = current.Parents.FirstOrDefault();
        }

        commits.Reverse();
        return commits;
    }

    private static void EnsureCleanWorkingTree(Repository repo, string operation)
    {
        if (HasUncommittedChanges(repo))
            throw new InvalidOperationException(
                $"Cannot {operation} while the working tree has uncommitted changes. Commit or stash them first.");
    }

    private static bool HasUncommittedChanges(Repository repo)
    {
        var status = repo.RetrieveStatus(new StatusOptions
        {
            IncludeUntracked = true,
            RecurseUntrackedDirs = true,
        });

        return status.Any(e => (e.State & FileStatus.Ignored) == 0);
    }

    private static bool ShaMatches(Commit commit, string sha) =>
        commit.Sha.Equals(sha, StringComparison.OrdinalIgnoreCase)
        || commit.Sha.StartsWith(sha, StringComparison.OrdinalIgnoreCase);

    private static string NormalizeRepoPath(string path) =>
        path.Replace('\\', '/').Trim('/');

    private static string ShortSha(string sha) =>
        sha.Length >= 7 ? sha[..7] : sha;

    public Task<string> AmendAsync(AmendRequest request)
    {
        using var repo = OpenRepository();
        EnsureAttachedHead(repo, "amend commits");

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

    public Task PushAsync(string remoteName = "origin", PushMode mode = PushMode.Normal)
    {
        using var repo = OpenRepository();
        _ = ResolveRemote(repo, remoteName);

        // libgit2 has no real force-with-lease — HybridGitBackend routes that to the CLI.
        // Here a lease request degrades to a plain force (the prior behaviour) when no CLI.
        if (mode == PushMode.Force || mode == PushMode.ForceWithLease)
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

    public Task ResetHardAsync(string targetReference, string? branchName = null)
        => ResetAsync(targetReference, ResetMode.Hard, branchName);

    public Task ResetMixedAsync(string targetReference, string? branchName = null)
        => ResetAsync(targetReference, ResetMode.Mixed, branchName);

    private Task ResetAsync(string targetReference, ResetMode mode, string? branchName)
    {
        using var repo = OpenRepository();
        var commit = repo.Lookup<Commit>(targetReference)
            ?? throw new InvalidOperationException($"Target reference not found: {targetReference}");

        AttachBranchForReset(repo, branchName);
        repo.Reset(mode, commit);
        HeadChanged?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    private static void AttachBranchForReset(Repository repo, string? branchName)
    {
        if (string.IsNullOrWhiteSpace(branchName) || IsDetachedBranchLabel(branchName))
            return;

        var branch = repo.Branches[branchName];
        if (branch is null || branch.IsRemote)
        {
            if (repo.Info.IsHeadDetached)
                throw new InvalidOperationException(
                    $"Cannot reset while HEAD is detached because branch '{branchName}' was not found.");
            return;
        }

        if (branch.IsCurrentRepositoryHead)
            return;

        if (repo.Info.IsHeadDetached && repo.Head.Tip is { } detachedTip &&
            branch.Tip?.Id != detachedTip.Id)
        {
            repo.Refs.UpdateTarget(branch.Reference, detachedTip.Id, "reattach branch before reset");
            branch = repo.Branches[branchName]
                ?? throw new InvalidOperationException($"Branch not found after reattach: {branchName}");
        }

        Commands.Checkout(repo, branch);
    }

    private static Branch AttachBranchForHistoryRewrite(
        Repository repo,
        string? branchName,
        string operation)
    {
        if (!repo.Info.IsHeadDetached)
            return GetCurrentLocalBranch(repo, operation);

        if (string.IsNullOrWhiteSpace(branchName) || IsDetachedBranchLabel(branchName))
            throw new InvalidOperationException(
                $"Cannot {operation} while HEAD is detached. Check out a local branch first.");

        var branch = repo.Branches[branchName];
        if (branch is null || branch.IsRemote)
            throw new InvalidOperationException(
                $"Cannot {operation} while HEAD is detached because branch '{branchName}' was not found.");

        if (repo.Head.Tip is { } detachedTip && branch.Tip?.Id != detachedTip.Id)
        {
            repo.Refs.UpdateTarget(branch.Reference, detachedTip.Id, $"reattach branch before {operation}");
            branch = repo.Branches[branchName]
                ?? throw new InvalidOperationException($"Branch not found after reattach: {branchName}");
        }

        Commands.Checkout(repo, branch);
        return repo.Branches[branchName]
            ?? throw new InvalidOperationException($"Branch not found after checkout: {branchName}");
    }

    private static Branch GetCurrentLocalBranch(Repository repo, string operation)
    {
        EnsureAttachedHead(repo, operation);
        var branch = repo.Branches[repo.Head.FriendlyName];
        if (branch is null || branch.IsRemote)
            throw new InvalidOperationException($"Cannot {operation} because the current branch is not a local branch.");

        return branch;
    }

    private static void EnsureAttachedHead(Repository repo, string operation)
    {
        if (repo.Info.IsHeadDetached)
            throw new InvalidOperationException(
                $"Cannot {operation} while HEAD is detached. Check out a local branch first.");
    }

    private static bool IsDetachedBranchLabel(string branchName) =>
        branchName.StartsWith("detached @", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(branchName, "(no branch)", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(branchName, "HEAD", StringComparison.OrdinalIgnoreCase);

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

        var originalHead = repo.Head.Tip;
        var result = repo.CherryPick(commit, sig);

        if (result.Status == CherryPickStatus.Conflicts)
        {
            // Abort: restore the working tree/index and clear the cherry-pick state
            // so the repo is never left half-finished. (Gitster never resolves
            // conflicts — it aborts and reports, by design.)
            if (originalHead != null)
                repo.Reset(ResetMode.Hard, originalHead);
            CleanupSequencerState(repo);
            throw new InvalidOperationException(
                $"Cherry-pick of {sha[..Math.Min(7, sha.Length)]} produced conflicts and was aborted — " +
                "history and working tree are unchanged.");
        }

        HeadChanged?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    /// <summary>Removes leftover cherry-pick/merge state files (libgit2's abort equivalent).</summary>
    private static void CleanupSequencerState(Repository repo)
    {
        var gitDir = repo.Info.Path;
        foreach (var name in new[] { "CHERRY_PICK_HEAD", "MERGE_HEAD", "MERGE_MSG", "MERGE_MODE" })
        {
            try { File.Delete(Path.Combine(gitDir, name)); } catch { /* best-effort */ }
        }
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

    // ── Stash operations (Step A) ──────────────────────────────────────────

    public Task<IReadOnlyList<StashInfo>> GetStashesAsync()
    {
        using var repo = OpenRepository();
        var result = new List<StashInfo>();
        int index = 0;

        foreach (var stash in repo.Stashes)
        {
            var rawMessage = stash.Message ?? string.Empty;
            var branchName = StashNamer.ParseBranchFromMessage(rawMessage);
            var commitSha  = stash.WorkTree?.Id.Sha ?? string.Empty;
            var createdAt  = stash.WorkTree?.Committer.When ?? DateTimeOffset.Now;

            // Diff WorkTree against its first parent (the base commit at stash time)
            var files      = new List<StashFileChange>();
            var baseCommit = stash.WorkTree?.Parents.FirstOrDefault();
            if (stash.WorkTree != null && baseCommit != null)
            {
                try
                {
                    var patch = repo.Diff.Compare<Patch>(baseCommit.Tree, stash.WorkTree.Tree);
                    foreach (var entry in patch)
                    {
                        files.Add(new StashFileChange(
                            entry.Path,
                            entry.Status switch
                            {
                                ChangeKind.Added   => StashChangeKind.Added,
                                ChangeKind.Deleted => StashChangeKind.Deleted,
                                ChangeKind.Renamed => StashChangeKind.Renamed,
                                _                  => StashChangeKind.Modified,
                            },
                            entry.LinesAdded,
                            entry.LinesDeleted));
                    }
                }
                catch { /* ignore diff errors — files stay empty */ }
            }

            var autoName = StashNamer.Generate(rawMessage, branchName, files);
            result.Add(new StashInfo(index, rawMessage, branchName, createdAt, files, autoName, commitSha));
            index++;
        }

        return Task.FromResult<IReadOnlyList<StashInfo>>(result);
    }

    public Task<string> GetStashDiffAsync(int stashIndex)
    {
        using var repo = OpenRepository();
        var stashList = repo.Stashes.ToList();
        if (stashIndex < 0 || stashIndex >= stashList.Count)
            return Task.FromResult(string.Empty);

        var stash      = stashList[stashIndex];
        var baseCommit = stash.WorkTree?.Parents.FirstOrDefault();
        if (stash.WorkTree == null || baseCommit == null)
            return Task.FromResult(string.Empty);

        try
        {
            var patch = repo.Diff.Compare<Patch>(baseCommit.Tree, stash.WorkTree.Tree);
            return Task.FromResult(patch.Content);
        }
        catch
        {
            return Task.FromResult(string.Empty);
        }
    }

    public Task ApplyStashAsync(int stashIndex, bool reinstateIndex = true)
    {
        using var repo = OpenRepository();
        var opts = new StashApplyOptions();
        if (reinstateIndex)
            opts.ApplyModifiers = StashApplyModifiers.ReinstateIndex;

        var status = repo.Stashes.Apply(stashIndex, opts);
        return status switch
        {
            StashApplyStatus.Conflicts => throw new InvalidOperationException(
                "Applying the stash produced conflicts. Resolve them manually."),
            StashApplyStatus.NotFound => throw new InvalidOperationException(
                $"stash@{{{stashIndex}}} was not found."),
            _ => Task.CompletedTask,
        };
    }

    public Task PopStashAsync(int stashIndex, bool reinstateIndex = true)
    {
        using var repo = OpenRepository();
        var opts = new StashApplyOptions();
        if (reinstateIndex)
            opts.ApplyModifiers = StashApplyModifiers.ReinstateIndex;

        var status = repo.Stashes.Pop(stashIndex, opts);
        if (status == StashApplyStatus.Conflicts)
            throw new InvalidOperationException(
                "Popping the stash produced conflicts. Resolve them manually.");
        if (status == StashApplyStatus.NotFound)
            throw new InvalidOperationException($"stash@{{{stashIndex}}} was not found.");

        HeadChanged?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    public Task DropStashAsync(int stashIndex)
    {
        using var repo = OpenRepository();
        repo.Stashes.Remove(stashIndex);
        return Task.CompletedTask;
    }

    public Task<string> CreateStashAsync(string message, bool includeUntracked = true)
    {
        using var repo = OpenRepository();
        var sig = repo.Config.BuildSignature(DateTimeOffset.Now)
                  ?? new Signature("Gitster", "gitster@local", DateTimeOffset.Now);

        var modifiers = StashModifiers.Default;
        if (includeUntracked)
            modifiers |= StashModifiers.IncludeUntracked;

        var stash = repo.Stashes.Add(sig, message, modifiers);
        if (stash == null)
            throw new InvalidOperationException(
                "Nothing to stash — the working tree is clean.");

        return Task.FromResult(stash.WorkTree?.Id.Sha ?? string.Empty);
    }

    public Task<string> ConvertStashToBranchAsync(int stashIndex, string branchName)
    {
        using var repo = OpenRepository();
        var stashList = repo.Stashes.ToList();
        if (stashIndex < 0 || stashIndex >= stashList.Count)
            throw new InvalidOperationException($"stash@{{{stashIndex}}} was not found.");

        var stash      = stashList[stashIndex];
        var baseCommit = stash.WorkTree?.Parents.FirstOrDefault()
                         ?? throw new InvalidOperationException(
                             "Cannot determine the base commit for this stash.");

        // 1. Create branch at the stash's base commit
        var newBranch = repo.CreateBranch(branchName, baseCommit);

        // 2. Checkout the new branch
        Commands.Checkout(repo, newBranch);

        // 3. Apply the stash onto the new branch
        var opts   = new StashApplyOptions { ApplyModifiers = StashApplyModifiers.ReinstateIndex };
        var status = repo.Stashes.Apply(stashIndex, opts);

        if (status == StashApplyStatus.Applied)
        {
            // 4. Drop the stash on clean apply
            repo.Stashes.Remove(stashIndex);
            HeadChanged?.Invoke(this, EventArgs.Empty);
            return Task.FromResult(branchName);
        }

        if (status == StashApplyStatus.Conflicts)
        {
            HeadChanged?.Invoke(this, EventArgs.Empty);
            throw new InvalidOperationException(
                $"Branch '{branchName}' was created from the stash, but applying produced conflicts. " +
                "Both the branch and the stash are intact — resolve conflicts manually.");
        }

        throw new InvalidOperationException(
            $"Failed to apply stash during branch conversion (status: {status}).");
    }

    private Repository OpenRepository()
    {
        if (string.IsNullOrWhiteSpace(RepositoryPath))
            throw new InvalidOperationException("Repository is not opened.");

        return new Repository(RepositoryPath);
    }

    /// <summary>Reads the (current, total) step of an in-progress rebase, 0/0 if unknown.</summary>
    private static (int Step, int Total) ReadRebaseProgress(string rebaseMerge, string rebaseApply)
    {
        try
        {
            // Interactive / merge-based rebase: msgnum + end
            if (Directory.Exists(rebaseMerge))
            {
                var step  = ReadIntFile(Path.Combine(rebaseMerge, "msgnum"));
                var total = ReadIntFile(Path.Combine(rebaseMerge, "end"));
                return (step, total);
            }
            // Apply-based rebase: next + last
            if (Directory.Exists(rebaseApply))
            {
                var step  = ReadIntFile(Path.Combine(rebaseApply, "next"));
                var total = ReadIntFile(Path.Combine(rebaseApply, "last"));
                return (step, total);
            }
        }
        catch { /* fall through */ }
        return (0, 0);
    }

    private static int ReadIntFile(string path)
        => File.Exists(path) && int.TryParse(File.ReadAllText(path).Trim(), out var n) ? n : 0;

    // ── Fixup-workflow methods (Steps F-H) ─────────────────────────────────

    /// <summary>
    /// Not supported directly — <see cref="HybridGitBackend"/> routes fixup to CLI.
    /// </summary>
    public Task FixupIntoCommitAsync(string targetSha) =>
        throw new NotSupportedException("Route through HybridGitBackend.");

    /// <summary>
    /// Rewords HEAD (amend message only) — non-HEAD routing done in HybridGitBackend.
    /// </summary>
    public Task RewordCommitAsync(string sha, string newMessage)
    {
        using var repo = OpenRepository();
        var head = repo.Head.Tip ?? throw new InvalidOperationException("No HEAD commit.");

        // Ensure caller is actually asking for HEAD
        if (!head.Id.Sha.StartsWith(sha, StringComparison.OrdinalIgnoreCase) &&
            !sha.StartsWith(head.Id.Sha, StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("Non-HEAD reword requires CLI. Route through HybridGitBackend.");

        var newAuthor    = new Signature(head.Author.Name,    head.Author.Email,    head.Author.When);
        var newCommitter = new Signature(head.Committer.Name, head.Committer.Email, DateTimeOffset.Now);
        repo.Commit(newMessage, newAuthor, newCommitter, new CommitOptions { AmendPreviousCommit = true });
        HeadChanged?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Squashes a contiguous range of commits that ends at HEAD using a soft reset.
    /// shas are in newest-first order (commit-list order).
    /// </summary>
    public Task SquashCommitsHeadAsync(
        IReadOnlyList<string> shas,
        string combinedMessage,
        DateTimeOffset? overrideDate)
    {
        using var repo = OpenRepository();

        // shas[0] is newest (HEAD), shas[last] is oldest in selection
        var orderedOldestFirst = shas.Reverse().ToList();
        var oldestSha = orderedOldestFirst[0];

        // Find the base commit (parent of the oldest selected commit)
        var oldestCommit = repo.Lookup<Commit>(oldestSha)
            ?? throw new InvalidOperationException($"Commit not found: {oldestSha}");
        var baseCommit = oldestCommit.Parents.FirstOrDefault()
            ?? throw new InvalidOperationException("Cannot squash — the oldest selected commit has no parent.");

        // Soft reset to the base commit (stages all changes)
        repo.Reset(ResetMode.Soft, baseCommit);

        // Commit the staged changes with the combined message
        var sig = repo.Config.BuildSignature(overrideDate ?? DateTimeOffset.Now)
                  ?? new Signature("Gitster", "gitster@local", overrideDate ?? DateTimeOffset.Now);

        // When the user picks a date, apply it to BOTH author and committer — the same
        // convention Gitster's combined-amend uses. Otherwise keep the natural "now".
        var when          = overrideDate ?? DateTimeOffset.Now;
        var committerWhen = overrideDate ?? DateTimeOffset.Now;
        var author    = new Signature(sig.Name, sig.Email, when);
        var committer = new Signature(sig.Name, sig.Email, committerWhen);

        repo.Commit(combinedMessage, author, committer);
        HeadChanged?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    /// <summary>
    /// True when <paramref name="shas"/> form a contiguous first-parent chain (no gaps).
    /// Non-contiguous squash is ill-defined, so callers validate before squashing.
    /// </summary>
    public Task<bool> AreCommitsContiguousAsync(IReadOnlyList<string> shas)
    {
        if (shas.Count <= 1) return Task.FromResult(true);

        using var repo = OpenRepository();
        var commits = shas
            .Select(s => repo.Lookup<Commit>(s))
            .Where(c => c != null)
            .Cast<Commit>()
            .ToList();

        if (commits.Count != shas.Count) return Task.FromResult(false);

        var bySha = commits.ToDictionary(c => c.Id.Sha, c => c, StringComparer.OrdinalIgnoreCase);

        // The "newest" selected commit is the one that is not the first-parent of any
        // other selected commit. A contiguous range has exactly one such head.
        var parentShas = commits
            .Select(c => c.Parents.FirstOrDefault()?.Id.Sha)
            .Where(s => s != null && bySha.ContainsKey(s!))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var heads = commits.Where(c => !parentShas.Contains(c.Id.Sha)).ToList();
        if (heads.Count != 1) return Task.FromResult(false);

        // Walk first-parents from the head; every step must stay inside the selection
        // until we've visited every selected commit.
        var current = heads[0];
        int visited = 1;
        while (true)
        {
            var parent = current.Parents.FirstOrDefault();
            if (parent != null && bySha.TryGetValue(parent.Id.Sha, out var next))
            {
                current = next;
                visited++;
            }
            else break;
        }

        return Task.FromResult(visited == commits.Count);
    }

    // Non-HEAD squash requires CLI — HybridGitBackend routes there.
    public Task SquashCommitsAsync(IReadOnlyList<string> shas, string combinedMessage, DateTimeOffset? overrideDate) =>
        throw new NotSupportedException("Route through HybridGitBackend.");

    /// <summary>Lists all local and remote branches.</summary>
    public Task<IReadOnlyList<BranchSummary>> GetBranchesAsync()
    {
        using var repo = OpenRepository();
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

    /// <summary>Returns up to <paramref name="maxCount"/> commits reachable from <paramref name="refName"/>.</summary>
    public Task<IReadOnlyList<CommitInfo>> GetCommitsForRefAsync(string refName, int maxCount = 200)
    {
        using var repo = OpenRepository();
        var filter = new LibGit2Sharp.CommitFilter
        {
            SortBy             = CommitSortStrategies.Topological | CommitSortStrategies.Time,
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
                c.Id.Sha))
            .ToList();
        return Task.FromResult<IReadOnlyList<CommitInfo>>(result);
    }

    // ── Phase 3: Branch operations ─────────────────────────────────────────

    public Task<IReadOnlyList<BranchListItem>> GetBranchListAsync()
    {
        using var repo = OpenRepository();
        var currentTip = repo.Head.Tip;
        var result = new List<BranchListItem>();

        foreach (var b in repo.Branches)
        {
            var tip = b.Tip;

            int ahead = 0, behind = 0;
            var tracked = b.TrackedBranch;
            if (tracked?.Tip != null && tip != null)
            {
                var div = repo.ObjectDatabase.CalculateHistoryDivergence(tip, tracked.Tip);
                ahead  = div.AheadBy  ?? 0;
                behind = div.BehindBy ?? 0;
            }

            // "Merged" = this branch's tip is already an ancestor of the current HEAD
            // (so deleting it loses nothing). Never flag the current branch.
            bool isMerged = false;
            if (!b.IsCurrentRepositoryHead && tip != null && currentTip != null)
            {
                var mergeBase = repo.ObjectDatabase.FindMergeBase(tip, currentTip);
                isMerged = mergeBase != null &&
                           mergeBase.Sha.Equals(tip.Sha, StringComparison.OrdinalIgnoreCase);
            }

            result.Add(new BranchListItem(
                Name:         b.FriendlyName,
                UpstreamName: tracked?.FriendlyName,
                TipSha:       tip?.Sha ?? string.Empty,
                TipMessage:   tip?.MessageShort ?? string.Empty,
                LastActivity: tip?.Committer.When ?? DateTimeOffset.MinValue,
                Ahead:        ahead,
                Behind:       behind,
                IsCurrent:    b.IsCurrentRepositoryHead,
                IsRemote:     b.IsRemote,
                IsMerged:     isMerged));
        }

        return Task.FromResult<IReadOnlyList<BranchListItem>>(result);
    }

    public Task CheckoutBranchAsync(string branchName)
    {
        using var repo = OpenRepository();
        var branch = repo.Branches[branchName]
            ?? throw new InvalidOperationException($"Branch not found: {branchName}");

        if (branch.IsRemote)
        {
            // Create (or reuse) a local tracking branch and check that out.
            var localName = branchName.Contains('/') ? branchName[(branchName.IndexOf('/') + 1)..] : branchName;
            var local = repo.Branches[localName];
            if (local == null)
            {
                local = repo.CreateBranch(localName, branch.Tip);
                repo.Branches.Update(local, b => b.TrackedBranch = branch.CanonicalName);
            }
            Commands.Checkout(repo, local);
        }
        else
        {
            Commands.Checkout(repo, branch);
        }

        HeadChanged?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    public Task<string> CreateBranchAsync(string name, string startPointSha)
    {
        using var repo = OpenRepository();
        var commit = repo.Lookup<Commit>(startPointSha)
            ?? throw new InvalidOperationException($"Start point not found: {startPointSha}");
        var branch = repo.CreateBranch(name, commit);
        return Task.FromResult(branch.FriendlyName);
    }

    public Task DeleteBranchAsync(string name, bool force)
    {
        using var repo = OpenRepository();
        var branch = repo.Branches[name]
            ?? throw new InvalidOperationException($"Branch not found: {name}");
        if (branch.IsCurrentRepositoryHead)
            throw new InvalidOperationException("Cannot delete the branch that is currently checked out.");
        repo.Branches.Remove(branch);
        return Task.CompletedTask;
    }

    public Task RenameBranchAsync(string oldName, string newName)
    {
        using var repo = OpenRepository();
        var branch = repo.Branches[oldName]
            ?? throw new InvalidOperationException($"Branch not found: {oldName}");
        repo.Branches.Rename(branch, newName);
        if (branch.IsCurrentRepositoryHead)
            HeadChanged?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    public Task<BranchMergeResult> MergeBranchAsync(string branchName, BranchMergeStrategy strategy)
    {
        using var repo = OpenRepository();
        var targetBranch = repo.Info.IsHeadDetached ? "detached HEAD" : repo.Head.FriendlyName;
        var beforeHead = repo.Head.Tip?.Sha
            ?? throw new InvalidOperationException("Cannot merge into a repository without a HEAD commit.");

        var branch = repo.Branches[branchName]
            ?? throw new InvalidOperationException($"Branch not found: {branchName}");

        if (branch.IsCurrentRepositoryHead)
            throw new InvalidOperationException("Cannot merge a branch into itself.");
        if (branch.Tip is null)
            throw new InvalidOperationException($"Branch '{branchName}' has no commits to merge.");

        var signature = repo.Config.BuildSignature(DateTimeOffset.Now)
            ?? new Signature("Gitster", "gitster@local", DateTimeOffset.Now);

        var options = new MergeOptions
        {
            CommitOnSuccess = true,
            FastForwardStrategy = strategy switch
            {
                BranchMergeStrategy.FastForwardOnly => FastForwardStrategy.FastForwardOnly,
                BranchMergeStrategy.NoFastForward => FastForwardStrategy.NoFastForward,
                _ => FastForwardStrategy.Default,
            },
        };

        MergeResult result;
        try
        {
            result = repo.Merge(branch, signature, options);
        }
        catch (NonFastForwardException ex) when (strategy == BranchMergeStrategy.FastForwardOnly)
        {
            throw new InvalidOperationException(
                $"Branch '{branchName}' cannot be fast-forwarded into '{targetBranch}'. Choose another merge strategy if you want a merge commit.",
                ex);
        }

        if (result.Status == MergeStatus.Conflicts)
        {
            throw new InvalidOperationException(
                $"Merge of '{branchName}' produced conflicts. Resolve them in the working tree, then commit or abort the merge.");
        }

        if (strategy == BranchMergeStrategy.FastForwardOnly && result.Status == MergeStatus.NonFastForward)
        {
            throw new InvalidOperationException(
                $"Branch '{branchName}' cannot be fast-forwarded into '{targetBranch}'. Choose another merge strategy if you want a merge commit.");
        }

        var headSha = repo.Head.Tip?.Sha ?? beforeHead;
        var outcome = result.Status switch
        {
            MergeStatus.UpToDate => BranchMergeOutcome.UpToDate,
            MergeStatus.FastForward => BranchMergeOutcome.FastForward,
            MergeStatus.NonFastForward => BranchMergeOutcome.MergeCommit,
            _ => BranchMergeOutcome.MergeCommit,
        };

        if (!headSha.Equals(beforeHead, StringComparison.OrdinalIgnoreCase))
            HeadChanged?.Invoke(this, EventArgs.Empty);

        return Task.FromResult(new BranchMergeResult(branch.FriendlyName, targetBranch, headSha, outcome));
    }

    // ── Phase 3: Commit-to-another-branch (Step B) ──────────────────────────

    public async Task<HistoryStitchPreview> PreviewHistoryStitchAsync(string sourceRef)
    {
        await GitCli.DetectAsync();

        using var repo = OpenRepository();
        var warnings = new List<string>();
        var blocks = new List<string>();
        var head = repo.Head.Tip;
        var targetBranch = repo.Info.IsHeadDetached ? "detached HEAD" : repo.Head.FriendlyName;
        var targetHeadSha = head?.Sha ?? string.Empty;
        var sourceBranch = repo.Branches[sourceRef];
        var source = sourceBranch?.Tip ?? repo.Lookup<Commit>(sourceRef);
        var sourceTipSha = source?.Sha ?? string.Empty;

        if (!GitCli.IsAvailable)
        {
            blocks.Add(
                "Stitching history requires the Git command-line tool because Gitster runs git merge --no-ff -s ours exactly.");
        }

        if (head is null)
        {
            blocks.Add("Cannot stitch history in a repository without a HEAD commit.");
        }

        if (repo.Info.IsHeadDetached)
        {
            blocks.Add("Check out a local branch before stitching history.");
        }

        if (source is null)
        {
            blocks.Add($"Source branch or ref '{sourceRef}' was not found.");
        }
        else if (sourceBranch?.IsCurrentRepositoryHead == true ||
                 (!repo.Info.IsHeadDetached && string.Equals(sourceRef, repo.Head.FriendlyName, StringComparison.Ordinal)))
        {
            blocks.Add("Choose an old source branch, not the current branch.");
        }

        if (HasUncommittedChanges(repo))
        {
            blocks.Add("Commit or stash uncommitted changes before stitching history.");
        }

        var isAlreadyReachable = false;
        var uniqueSourceCommitCount = 0;
        string? squashMatchSha = null;
        if (head is not null && source is not null)
        {
            var mergeBase = repo.ObjectDatabase.FindMergeBase(source, head);
            isAlreadyReachable = mergeBase?.Sha.Equals(source.Sha, StringComparison.OrdinalIgnoreCase) == true;
            if (isAlreadyReachable)
            {
                blocks.Add($"'{sourceRef}' is already reachable from the current branch.");
            }

            uniqueSourceCommitCount = repo.Commits.QueryBy(new LibGit2Sharp.CommitFilter
            {
                SortBy = CommitSortStrategies.Topological | CommitSortStrategies.Time,
                IncludeReachableFrom = source,
                ExcludeReachableFrom = head,
            }).Count();

            squashMatchSha = repo.Commits.QueryBy(new LibGit2Sharp.CommitFilter
            {
                SortBy = CommitSortStrategies.Topological | CommitSortStrategies.Time,
                IncludeReachableFrom = head,
            }).FirstOrDefault(c => c.Tree.Sha.Equals(source.Tree.Sha, StringComparison.OrdinalIgnoreCase))?.Sha;

            if (squashMatchSha is null && !isAlreadyReachable)
            {
                warnings.Add(
                    "No exact squash-tree match was found on the current branch. Continue only if this is still the old history you want to reference.");
            }
        }

        var tracked = repo.Head.TrackedBranch;
        if (head is not null && tracked?.Tip is not null)
        {
            var divergence = repo.ObjectDatabase.CalculateHistoryDivergence(head, tracked.Tip);
            if ((divergence.BehindBy ?? 0) > 0)
            {
                warnings.Add(
                    $"The current branch is behind {tracked.FriendlyName}. Pull or inspect incoming commits before stitching if this branch should be current.");
            }
        }

        return new HistoryStitchPreview(
            sourceRef,
            sourceTipSha,
            targetBranch,
            targetHeadSha,
            isAlreadyReachable,
            uniqueSourceCommitCount,
            squashMatchSha,
            warnings,
            blocks);
    }

    public Task<HistoryStitchResult> StitchHistoryAsync(string sourceRef) =>
        throw new NotSupportedException("History stitch execution requires the Git command-line tool.");

    public Task<string> CommitToBranchAsync(CommitToBranchRequest request)
    {
        using var repo = OpenRepository();

        if (string.Equals(request.TargetBranch, repo.Head.FriendlyName, StringComparison.Ordinal))
            throw new InvalidOperationException(
                "Target is the current branch — use the normal commit/amend flow instead.");

        var target = repo.Branches[request.TargetBranch]
            ?? throw new InvalidOperationException($"Target branch not found: {request.TargetBranch}");
        var targetTip = target.Tip
            ?? throw new InvalidOperationException("Target branch has no commits to build on.");

        // Capture the chosen changes as a tree WITHOUT touching the index or working tree.
        var (tree, capturedPaths) = BuildCapturedTree(repo, request.IncludeUnstaged);
        if (capturedPaths.Count == 0)
            throw new InvalidOperationException("There are no changes to commit.");

        var fallback = repo.Config.BuildSignature(DateTimeOffset.Now)
                       ?? new Signature("Gitster", "gitster@local", DateTimeOffset.Now);
        var author = new Signature(
            request.AuthorName  ?? fallback.Name,
            request.AuthorEmail ?? fallback.Email,
            DateTimeOffset.Now);

        var commit = repo.ObjectDatabase.CreateCommit(
            author, author, request.Message, tree, new[] { targetTip }, prettifyMessage: true);

        repo.Refs.UpdateTarget(target.Reference, commit.Id,
            $"commit to {request.TargetBranch}: {request.Message}");

        // Move (opt-in): remove exactly the captured changes from the current branch.
        if (request.RemoveFromCurrent)
            RemoveCapturedChanges(repo, capturedPaths);

        HeadChanged?.Invoke(this, EventArgs.Empty);
        return Task.FromResult(commit.Sha);
    }

    // ── Phase 3: Branch snapshot (Step C) ───────────────────────────────────

    public Task<string> CreateSnapshotBranchAsync(string branchName, bool includeUncommitted)
    {
        using var repo = OpenRepository();
        var head = repo.Head.Tip
            ?? throw new InvalidOperationException("Cannot snapshot — the repository has no commits yet.");

        var branch = repo.CreateBranch(branchName, head);

        if (includeUncommitted)
        {
            var (tree, capturedPaths) = BuildCapturedTree(repo, includeUnstaged: true);
            if (capturedPaths.Count > 0)
            {
                var sig = repo.Config.BuildSignature(DateTimeOffset.Now)
                          ?? new Signature("Gitster", "gitster@local", DateTimeOffset.Now);
                var commit = repo.ObjectDatabase.CreateCommit(
                    sig, sig, "snapshot: uncommitted changes", tree, new[] { head }, prettifyMessage: true);
                repo.Refs.UpdateTarget(branch.Reference, commit.Id, "snapshot uncommitted changes");
            }
        }

        // The current branch and working tree are deliberately left untouched.
        return Task.FromResult(branch.FriendlyName);
    }

    public Task<ArchiveResult> ArchiveSourceZipAsync(ArchiveRequest request, CancellationToken ct = default) =>
        throw new NotSupportedException("Archive export requires the Git command-line tool.");

    /// <summary>
    /// Builds a tree object capturing the requested changes without disturbing the
    /// index or working tree. Returns the tree and the set of paths it touched
    /// (relative to the work dir). Staged-only uses the index tree; including unstaged
    /// overlays working-dir modifications, additions and deletions on top.
    /// </summary>
    private static (Tree Tree, HashSet<string> Paths) BuildCapturedTree(Repository repo, bool includeUnstaged)
    {
        var headTree = repo.Head.Tip?.Tree;
        var stagedTree = repo.ObjectDatabase.CreateTree(repo.Index);   // tree of the current index

        var paths = new HashSet<string>(StringComparer.Ordinal);

        // Which paths differ between HEAD and the index (i.e. staged)?
        if (headTree != null)
        {
            foreach (var change in repo.Diff.Compare<TreeChanges>(headTree, stagedTree))
                paths.Add(change.Path);
        }
        else
        {
            foreach (var entry in repo.Index)
                paths.Add(entry.Path);
        }

        if (!includeUnstaged)
            return (stagedTree, paths);

        // Overlay working-directory changes on top of the staged tree.
        var td = TreeDefinition.From(stagedTree);
        var workdir = repo.Info.WorkingDirectory;

        var status = repo.RetrieveStatus(new StatusOptions
        {
            IncludeUntracked      = true,
            RecurseUntrackedDirs  = true,
            Show                  = StatusShowOption.WorkDirOnly,
        });

        foreach (var entry in status)
        {
            var state = entry.State;
            if ((state & FileStatus.DeletedFromWorkdir) != 0)
            {
                td.Remove(entry.FilePath);
                paths.Add(entry.FilePath);
            }
            else if ((state & FileStatus.ModifiedInWorkdir) != 0 ||
                     (state & FileStatus.NewInWorkdir) != 0 ||
                     (state & FileStatus.TypeChangeInWorkdir) != 0)
            {
                var full = Path.Combine(workdir, entry.FilePath);
                if (File.Exists(full))
                {
                    var blob = repo.ObjectDatabase.CreateBlob(full);
                    td.Add(entry.FilePath, blob, Mode.NonExecutableFile);
                    paths.Add(entry.FilePath);
                }
            }
        }

        var combined = repo.ObjectDatabase.CreateTree(td);
        return (combined, paths);
    }

    /// <summary>
    /// Removes the given paths' changes from the working tree and index, restoring each
    /// to its HEAD state (or deleting it if it does not exist in HEAD). Used by the
    /// opt-in "move" mode of commit-to-branch. Other paths are left untouched.
    /// </summary>
    private static void RemoveCapturedChanges(Repository repo, HashSet<string> paths)
    {
        var headTip = repo.Head.Tip;
        var headTree = headTip?.Tree;
        var workdir = repo.Info.WorkingDirectory;

        var inHead = new List<string>();
        foreach (var path in paths)
        {
            var existsInHead = headTree?[path] != null;
            if (existsInHead)
            {
                inHead.Add(path);
            }
            else
            {
                // Added by the user — unstage and delete from the working tree.
                repo.Index.Remove(path);
                try { File.Delete(Path.Combine(workdir, path)); } catch { /* best-effort */ }
            }
        }
        repo.Index.Write();

        if (inHead.Count > 0 && headTip != null)
        {
            repo.CheckoutPaths(headTip.Sha, inHead,
                new CheckoutOptions { CheckoutModifiers = CheckoutModifiers.Force });
        }
    }

    // ── Phase 4: Search & Analysis (Part B) ────────────────────────────────

    // Pickaxe (-S), diff-regex (-G) and range-diff are CLI-only — routed via Hybrid.
    public Task<IReadOnlyList<CommitInfo>> PickaxeSearchAsync(string term, string? path, CancellationToken ct = default) =>
        throw new NotSupportedException("Route through HybridGitBackend.");
    public Task<IReadOnlyList<CommitInfo>> DiffRegexSearchAsync(string pattern, string? path, CancellationToken ct = default) =>
        throw new NotSupportedException("Route through HybridGitBackend.");
    public Task<IReadOnlyList<RangeDiffEntry>> RangeDiffAsync(string range1, string range2, CancellationToken ct = default) =>
        throw new NotSupportedException("Route through HybridGitBackend.");

    /// <summary>Basic libgit2 blame fallback (no whitespace/move following — that needs the CLI).</summary>
    public Task<IReadOnlyList<BlameLine>> BlameAsync(string filePath, bool ignoreWhitespace, bool followMoves, CancellationToken ct = default)
    {
        using var repo = OpenRepository();
        var result = new List<BlameLine>();

        var fullPath = Path.Combine(repo.Info.WorkingDirectory, filePath);
        var contentLines = File.Exists(fullPath) ? File.ReadAllLines(fullPath) : Array.Empty<string>();

        var blame = repo.Blame(filePath);
        int line = 0;
        foreach (var hunk in blame)
        {
            var commit = hunk.FinalCommit;
            var author = commit?.Author.Name ?? string.Empty;
            var when = commit?.Author.When ?? DateTimeOffset.MinValue;
            var sha = commit?.Sha ?? string.Empty;
            for (int i = 0; i < hunk.LineCount; i++)
            {
                var content = line < contentLines.Length ? contentLines[line] : string.Empty;
                result.Add(new BlameLine(line + 1, sha, author, when, content));
                line++;
            }
        }
        return Task.FromResult<IReadOnlyList<BlameLine>>(result);
    }

    public Task<string?> GetPriorTipFromReflogAsync()
    {
        using var repo = OpenRepository();
        var recent = repo.Refs.Log("HEAD").FirstOrDefault();
        return Task.FromResult(recent?.From?.Sha);
    }

    public Task<CompareResult> CompareRefsAsync(string baseRef, string compareRef, bool threeDot, CancellationToken ct = default)
    {
        using var repo = OpenRepository();
        var a = ResolveCommittish(repo, baseRef)
            ?? throw new InvalidOperationException($"Could not resolve '{baseRef}'.");
        var b = ResolveCommittish(repo, compareRef)
            ?? throw new InvalidOperationException($"Could not resolve '{compareRef}'.");

        Tree? fromTree;
        List<CommitInfo> commits;
        string explanation;

        if (threeDot)
        {
            var mergeBase = repo.ObjectDatabase.FindMergeBase(a, b);
            fromTree = mergeBase?.Tree;
            var filter = new LibGit2Sharp.CommitFilter
            {
                SortBy = CommitSortStrategies.Topological | CommitSortStrategies.Time,
                IncludeReachableFrom = new[] { a, b },
            };
            if (mergeBase != null) filter.ExcludeReachableFrom = mergeBase;
            commits = repo.Commits.QueryBy(filter).Select(ToInfo).ToList();
            explanation = $"A…B (three-dot): everything that differs since '{baseRef}' and '{compareRef}' diverged (their merge-base).";
        }
        else
        {
            fromTree = a.Tree;
            commits = repo.Commits.QueryBy(new LibGit2Sharp.CommitFilter
            {
                SortBy = CommitSortStrategies.Topological | CommitSortStrategies.Time,
                IncludeReachableFrom = b,
                ExcludeReachableFrom = a,
            }).Select(ToInfo).ToList();
            explanation = $"A..B (two-dot): commits on '{compareRef}' that are not on '{baseRef}'.";
        }

        var patch = repo.Diff.Compare<Patch>(fromTree, b.Tree);
        var files = patch.Select(e => new DiffFileEntry(e.Path, e.LinesAdded, e.LinesDeleted,
            e.Status switch { ChangeKind.Added => "A", ChangeKind.Deleted => "D", ChangeKind.Renamed => "R", _ => "M" },
            ParseUnifiedDiff(e.Patch))).ToList();
        var diff = new CommitDiff(files, patch.LinesAdded, patch.LinesDeleted);

        return Task.FromResult(new CompareResult(commits, diff, explanation));

        static CommitInfo ToInfo(Commit c) => new(
            c.Id.Sha.Length >= 7 ? c.Id.Sha[..7] : c.Id.Sha, c.MessageShort, c.Author.When.DateTime,
            c.Author.Name ?? string.Empty, c.Author.Email ?? string.Empty, CommitRemoteState.LocalOnly, c.Id.Sha);
    }

    /// <summary>Resolves a SHA, local branch, tag or origin/&lt;branch&gt; to a commit.</summary>
    private static Commit? ResolveCommittish(Repository repo, string r)
    {
        if (string.IsNullOrWhiteSpace(r)) return null;
        var c = repo.Lookup<Commit>(r);
        if (c != null) return c;
        if (repo.Branches[r]?.Tip is { } bt) return bt;
        if (repo.Tags[r]?.Target is Commit tc) return tc;
        if (repo.Branches[$"origin/{r}"]?.Tip is { } rt) return rt;
        return null;
    }

    // ── Phase 3: Worktrees — routed to CLI via HybridGitBackend ─────────────

    public Task<IReadOnlyList<WorktreeInfo>> GetWorktreesAsync() =>
        throw new NotSupportedException("Route through HybridGitBackend.");
    public Task<string> AddWorktreeAsync(string path, string branchName, bool createBranch) =>
        throw new NotSupportedException("Route through HybridGitBackend.");
    public Task RemoveWorktreeAsync(string path, bool force) =>
        throw new NotSupportedException("Route through HybridGitBackend.");
    public Task PruneWorktreesAsync() =>
        throw new NotSupportedException("Route through HybridGitBackend.");

    private static Remote ResolveRemote(Repository repo, string remoteName)
    {
        var remote = string.IsNullOrWhiteSpace(remoteName)
            ? repo.Network.Remotes.FirstOrDefault()
            : repo.Network.Remotes[remoteName];

        return remote ?? throw new InvalidOperationException("No remote found.");
    }
}
