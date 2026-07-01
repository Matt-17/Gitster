using LibGit2Sharp;
using System.IO;

namespace Gitster.Services.Git.LibGit2;

internal sealed class LibGit2HistoryRewriteOperations
{
    private readonly LibGit2RepositoryContext _context;

    public LibGit2HistoryRewriteOperations(LibGit2RepositoryContext context) => _context = context;

    public Task AmendAuthorAsync(AmendAuthorRequest request)
    {
        using var repo = _context.OpenRepository();
        EnsureAttachedHead(repo, "amend commits");

        var commit = repo.Head.Tip ?? throw new InvalidOperationException("No HEAD commit.");

        var newAuthor = new Signature(
            request.AuthorName ?? commit.Author.Name,
            request.AuthorEmail ?? commit.Author.Email,
            commit.Author.When);

        var newCommitter = new Signature(
            request.CommitterName ?? commit.Committer.Name,
            request.CommitterEmail ?? commit.Committer.Email,
            DateTimeOffset.Now);

        repo.Commit(commit.Message, newAuthor, newCommitter, new CommitOptions { AmendPreviousCommit = true });
        _context.RaiseHeadChanged();
        return Task.CompletedTask;
    }

    public Task RewriteCommitsAsync(IEnumerable<CommitRewrite> rewrites, string? branchName = null)
    {
        using var repo = _context.OpenRepository();
        var branch = AttachBranchForHistoryRewrite(repo, branchName, "rewrite commits");
        var branchTip = branch.Tip ?? throw new InvalidOperationException("No HEAD commit.");

        var allCommits = repo.Commits.QueryBy(new LibGit2Sharp.CommitFilter
        {
            SortBy = CommitSortStrategies.Topological | CommitSortStrategies.Time,
            IncludeReachableFrom = branchTip,
        }).ToList();
        allCommits.Reverse();

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
            _context.RaiseHeadChanged();
            return Task.CompletedTask;
        }

        var mapping = new Dictionary<string, Commit>(StringComparer.OrdinalIgnoreCase);
        var anyChanged = false;

        foreach (var oldCommit in allCommits)
        {
            rewriteByFullSha.TryGetValue(oldCommit.Id.Sha, out var rewrite);

            var oldParents = oldCommit.Parents.ToList();
            var newParents = oldParents
                .Select(p => mapping.TryGetValue(p.Id.Sha, out var mapped) ? mapped : p)
                .ToList();

            var parentsChanged = newParents.Zip(oldParents, (n, o) => n.Id != o.Id).Any(x => x);

            if (rewrite == null && !parentsChanged)
            {
                mapping[oldCommit.Id.Sha] = oldCommit;
                continue;
            }

            anyChanged = true;

            var newAuthor = new Signature(
                rewrite?.NewAuthorName ?? oldCommit.Author.Name,
                rewrite?.NewAuthorEmail ?? oldCommit.Author.Email,
                rewrite?.NewAuthorDate ?? oldCommit.Author.When);

            var newCommitter = new Signature(
                rewrite?.NewCommitterName ?? oldCommit.Committer.Name,
                rewrite?.NewCommitterEmail ?? oldCommit.Committer.Email,
                rewrite?.NewCommitterDate ?? oldCommit.Committer.When);

            var newCommit = repo.ObjectDatabase.CreateCommit(
                newAuthor, newCommitter,
                rewrite?.NewMessage ?? oldCommit.Message,
                oldCommit.Tree,
                newParents,
                prettifyMessage: false);

            mapping[oldCommit.Id.Sha] = newCommit;
        }

        if (anyChanged && mapping.TryGetValue(branchTip.Id.Sha, out var newHead))
            repo.Refs.UpdateTarget(branch.Reference, newHead.Id, "rewrite commits");

        _context.RaiseHeadChanged();
        return Task.CompletedTask;
    }

    public Task RemoveFileChangeFromCommitAsync(string sha, string path, string? branchName = null)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Path is required.", nameof(path));

        using var repo = _context.OpenRepository();
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

        _context.RaiseHeadChanged();
        return Task.CompletedTask;
    }

    public Task<string> GetReflogSelectorForHeadAsync()
    {
        using var repo = _context.OpenRepository();
        var logEntries = repo.Refs.Log("HEAD").ToList();
        if (logEntries.Count < 2)
            throw new InvalidOperationException("No previous HEAD reflog entry found.");

        return Task.FromResult("HEAD@{1}");
    }

    public Task ResetHardAsync(string targetReference, string? branchName = null)
        => ResetAsync(targetReference, ResetMode.Hard, branchName);

    public Task ResetMixedAsync(string targetReference, string? branchName = null)
        => ResetAsync(targetReference, ResetMode.Mixed, branchName);

    public Task CheckoutCommitDetachedAsync(string sha)
    {
        using var repo = _context.OpenRepository();
        var commit = repo.Lookup<Commit>(sha)
            ?? throw new InvalidOperationException($"Commit not found: {sha}");

        Commands.Checkout(repo, commit);
        _context.RaiseHeadChanged();
        return Task.CompletedTask;
    }

    public Task CherryPickAsync(string sha)
    {
        using var repo = _context.OpenRepository();
        var commit = repo.Lookup<Commit>(sha)
            ?? throw new InvalidOperationException($"Commit not found: {sha}");
        var sig = repo.Config.BuildSignature(DateTimeOffset.Now)
            ?? new Signature("Gitster", "gitster@local", DateTimeOffset.Now);

        var originalHead = repo.Head.Tip;
        var result = repo.CherryPick(commit, sig);

        if (result.Status == CherryPickStatus.Conflicts)
        {
            if (originalHead != null)
                repo.Reset(ResetMode.Hard, originalHead);
            CleanupSequencerState(repo);
            throw new InvalidOperationException(
                $"Cherry-pick of {sha[..Math.Min(7, sha.Length)]} produced conflicts and was aborted - " +
                "history and working tree are unchanged.");
        }

        _context.RaiseHeadChanged();
        return Task.CompletedTask;
    }

    public Task<string> CreateTagAsync(string name, string targetSha)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Tag name is required.", nameof(name));

        using var repo = _context.OpenRepository();
        var commit = repo.Lookup<Commit>(targetSha)
            ?? throw new InvalidOperationException($"Commit not found: {targetSha}");
        var tag = repo.Tags.Add(name.Trim(), commit);
        return Task.FromResult(tag.FriendlyName);
    }

    public Task RevertCommitAsync(string sha)
    {
        using var repo = _context.OpenRepository();
        if (repo.RetrieveStatus().IsDirty)
            throw new InvalidOperationException(
                "Cannot revert while the working tree has uncommitted changes. Commit or stash them first.");

        var commit = repo.Lookup<Commit>(sha)
            ?? throw new InvalidOperationException($"Commit not found: {sha}");
        var sig = repo.Config.BuildSignature(DateTimeOffset.Now)
            ?? new Signature("Gitster", "gitster@local", DateTimeOffset.Now);
        var originalHead = repo.Head.Tip;

        var result = repo.Revert(commit, sig, new RevertOptions());
        if (result.Status == RevertStatus.Conflicts)
        {
            if (originalHead != null)
                repo.Reset(ResetMode.Hard, originalHead);
            CleanupSequencerState(repo);
            throw new InvalidOperationException(
                $"Revert of {sha[..Math.Min(7, sha.Length)]} produced conflicts and was aborted - " +
                "history and working tree are unchanged.");
        }

        if (result.Status == RevertStatus.NothingToRevert)
            throw new InvalidOperationException("There is nothing to revert for the selected commit.");

        _context.RaiseHeadChanged();
        return Task.CompletedTask;
    }

    public Task RewordCommitAsync(string sha, string newMessage)
    {
        using var repo = _context.OpenRepository();
        var head = repo.Head.Tip ?? throw new InvalidOperationException("No HEAD commit.");

        if (!head.Id.Sha.StartsWith(sha, StringComparison.OrdinalIgnoreCase) &&
            !sha.StartsWith(head.Id.Sha, StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("Non-HEAD reword requires CLI. Route through HybridGitBackend.");

        var newAuthor = new Signature(head.Author.Name, head.Author.Email, head.Author.When);
        var newCommitter = new Signature(head.Committer.Name, head.Committer.Email, DateTimeOffset.Now);
        repo.Commit(newMessage, newAuthor, newCommitter, new CommitOptions { AmendPreviousCommit = true });
        _context.RaiseHeadChanged();
        return Task.CompletedTask;
    }

    public Task SquashCommitsHeadAsync(
        IReadOnlyList<string> shas,
        string combinedMessage,
        DateTimeOffset? overrideDate)
    {
        using var repo = _context.OpenRepository();

        var orderedOldestFirst = shas.Reverse().ToList();
        var oldestSha = orderedOldestFirst[0];

        var oldestCommit = repo.Lookup<Commit>(oldestSha)
            ?? throw new InvalidOperationException($"Commit not found: {oldestSha}");
        var baseCommit = oldestCommit.Parents.FirstOrDefault()
            ?? throw new InvalidOperationException("Cannot squash - the oldest selected commit has no parent.");

        repo.Reset(ResetMode.Soft, baseCommit);

        var sig = repo.Config.BuildSignature(overrideDate ?? DateTimeOffset.Now)
                  ?? new Signature("Gitster", "gitster@local", overrideDate ?? DateTimeOffset.Now);

        var when = overrideDate ?? DateTimeOffset.Now;
        var committerWhen = overrideDate ?? DateTimeOffset.Now;
        var author = new Signature(sig.Name, sig.Email, when);
        var committer = new Signature(sig.Name, sig.Email, committerWhen);

        repo.Commit(combinedMessage, author, committer);
        _context.RaiseHeadChanged();
        return Task.CompletedTask;
    }

    public Task<bool> AreCommitsContiguousAsync(IReadOnlyList<string> shas)
    {
        if (shas.Count <= 1)
            return Task.FromResult(true);

        using var repo = _context.OpenRepository();
        var commits = shas
            .Select(s => repo.Lookup<Commit>(s))
            .Where(c => c != null)
            .Cast<Commit>()
            .ToList();

        if (commits.Count != shas.Count)
            return Task.FromResult(false);

        var bySha = commits.ToDictionary(c => c.Id.Sha, c => c, StringComparer.OrdinalIgnoreCase);
        var parentShas = commits
            .Select(c => c.Parents.FirstOrDefault()?.Id.Sha)
            .Where(s => s != null && bySha.ContainsKey(s!))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var heads = commits.Where(c => !parentShas.Contains(c.Id.Sha)).ToList();
        if (heads.Count != 1)
            return Task.FromResult(false);

        var current = heads[0];
        var visited = 1;
        while (true)
        {
            var parent = current.Parents.FirstOrDefault();
            if (parent != null && bySha.TryGetValue(parent.Id.Sha, out var next))
            {
                current = next;
                visited++;
            }
            else
            {
                break;
            }
        }

        return Task.FromResult(visited == commits.Count);
    }

    public Task<string?> GetPriorTipFromReflogAsync()
    {
        using var repo = _context.OpenRepository();
        var recent = repo.Refs.Log("HEAD").FirstOrDefault();
        return Task.FromResult(recent?.From?.Sha);
    }

    private Task ResetAsync(string targetReference, ResetMode mode, string? branchName)
    {
        using var repo = _context.OpenRepository();
        var commit = repo.Lookup<Commit>(targetReference)
            ?? throw new InvalidOperationException($"Target reference not found: {targetReference}");

        AttachBranchForReset(repo, branchName);
        repo.Reset(mode, commit);
        _context.RaiseHeadChanged();
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

    private static void CleanupSequencerState(Repository repo)
    {
        var gitDir = repo.Info.Path;
        foreach (var name in new[] { "CHERRY_PICK_HEAD", "MERGE_HEAD", "MERGE_MSG", "MERGE_MODE" })
        {
            try
            {
                File.Delete(Path.Combine(gitDir, name));
            }
            catch
            {
            }
        }
    }

    private static bool ShaMatches(Commit commit, string sha) =>
        commit.Sha.Equals(sha, StringComparison.OrdinalIgnoreCase)
        || commit.Sha.StartsWith(sha, StringComparison.OrdinalIgnoreCase);

    private static string NormalizeRepoPath(string path) =>
        path.Replace('\\', '/').Trim('/');

    private static string ShortSha(string sha) =>
        sha.Length >= 7 ? sha[..7] : sha;
}
