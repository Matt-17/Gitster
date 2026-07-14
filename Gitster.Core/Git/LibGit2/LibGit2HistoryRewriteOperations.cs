using LibGit2Sharp;
using System.IO;

namespace Gitster.Core.Git.LibGit2;

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
            throw new GitConflictException(
                $"Cherry-pick of {GitSha.Short(sha)} produced conflicts and was aborted - " +
                "history and working tree are unchanged.",
                repositoryHalted: false);
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
            throw new GitConflictException(
                $"Revert of {GitSha.Short(sha)} produced conflicts and was aborted - " +
                "history and working tree are unchanged.",
                repositoryHalted: false);
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

    public Task ReorderCommitsAsync(
        IReadOnlyList<string> shasNewestFirst,
        IReadOnlyList<string> reorderedShasNewestFirst,
        string? branchName = null)
    {
        if (shasNewestFirst.Count < 2)
            throw new InvalidOperationException("Select at least two commits to reorder.");
        if (shasNewestFirst.Count != reorderedShasNewestFirst.Count)
            throw new InvalidOperationException("The reordered commit list must contain the same commits.");

        using var repo = _context.OpenRepository();
        var branch = AttachBranchForHistoryRewrite(repo, branchName, "reorder commits");
        var branchTip = branch.Tip ?? throw new InvalidOperationException("No HEAD commit.");
        EnsureCleanWorkingTree(repo, "reorder commits");

        var chain = GetFirstParentChainOldestFirst(branchTip);
        var selected = ResolveCommitsInChain(chain, shasNewestFirst);
        var reordered = ResolveCommitsInChain(chain, reorderedShasNewestFirst);
        EnsureSameCommitSet(selected, reordered);

        var selectedSet = selected.Select(c => c.Sha).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var startIndex = chain.FindIndex(c => selectedSet.Contains(c.Sha));
        var selectedRange = chain.Skip(startIndex).Take(selected.Count).ToList();
        if (selectedRange.Count != selected.Count || selectedRange.Any(c => !selectedSet.Contains(c.Sha)))
            throw new InvalidOperationException("Only contiguous first-parent commit ranges can be reordered.");

        EnsureNoMergeCommits(selectedRange, "reorder commits");
        EnsureNoOverlappingPathChanges(repo, selectedRange, "reorder commits");

        var reorderedOldestFirst = reordered.AsEnumerable().Reverse().ToList();
        var mapping = chain.Take(startIndex).ToDictionary(c => c.Sha, c => c, StringComparer.OrdinalIgnoreCase);
        Commit? newParent = selectedRange[0].Parents.FirstOrDefault();

        foreach (var oldCommit in reorderedOldestFirst)
        {
            var oldParentTree = oldCommit.Parents.FirstOrDefault()?.Tree;
            var newTree = BuildReplayedTree(repo, oldParentTree, oldCommit.Tree, newParent?.Tree, excludedPath: null);
            var newCommit = CreateReplayedCommit(repo, oldCommit, newTree, ParentList(newParent));
            mapping[oldCommit.Sha] = newCommit;
            newParent = newCommit;
        }

        foreach (var oldCommit in chain.Skip(startIndex + selectedRange.Count))
        {
            var oldParents = oldCommit.Parents.ToList();
            var newParents = oldParents
                .Select(p => mapping.TryGetValue(p.Sha, out var mapped) ? mapped : p)
                .ToList();
            var newTree = BuildReplayedTree(
                repo,
                oldParents.FirstOrDefault()?.Tree,
                oldCommit.Tree,
                newParents.FirstOrDefault()?.Tree,
                excludedPath: null);
            var newCommit = CreateReplayedCommit(repo, oldCommit, newTree, newParents);
            mapping[oldCommit.Sha] = newCommit;
            newParent = newCommit;
        }

        var newHead = newParent ?? throw new InvalidOperationException("Could not build reordered history.");
        repo.Refs.UpdateTarget(branch.Reference, newHead.Id, "reorder commits");
        _context.RaiseHeadChanged();
        return Task.CompletedTask;
    }

    public Task SplitCommitAsync(
        string sha,
        IReadOnlyList<string> firstCommitPaths,
        string firstMessage,
        string secondMessage,
        string? branchName = null)
    {
        if (firstCommitPaths.Count == 0)
            throw new InvalidOperationException("Choose at least one file for the first split commit.");
        if (string.IsNullOrWhiteSpace(firstMessage) || string.IsNullOrWhiteSpace(secondMessage))
            throw new InvalidOperationException("Both split commit messages are required.");

        using var repo = _context.OpenRepository();
        var branch = AttachBranchForHistoryRewrite(repo, branchName, "split a commit");
        var branchTip = branch.Tip ?? throw new InvalidOperationException("No HEAD commit.");
        EnsureCleanWorkingTree(repo, "split a commit");

        var chain = GetFirstParentChainOldestFirst(branchTip);
        var targetIndex = chain.FindIndex(c => ShaMatches(c, sha));
        if (targetIndex < 0)
            throw new InvalidOperationException("The selected commit is not on the current branch's first-parent history.");

        var affected = chain.Skip(targetIndex).ToList();
        EnsureNoMergeCommits(affected, "split a commit");

        var target = chain[targetIndex];
        var parent = target.Parents.FirstOrDefault();
        var changes = repo.Diff.Compare<TreeChanges>(parent?.Tree, target.Tree).ToList();
        var changedPaths = changes.SelectMany(ChangePaths).ToHashSet(StringComparer.Ordinal);
        var firstSet = firstCommitPaths.Select(NormalizeRepoPath).ToHashSet(StringComparer.Ordinal);
        if (!firstSet.All(changedPaths.Contains))
            throw new InvalidOperationException("The split file list contains paths not changed by the selected commit.");
        if (changedPaths.All(firstSet.Contains))
            throw new InvalidOperationException("Leave at least one changed file for the second split commit.");

        var mapping = chain.Take(targetIndex).ToDictionary(c => c.Sha, c => c, StringComparer.OrdinalIgnoreCase);
        var firstTree = BuildTreeFromChanges(
            repo,
            parent?.Tree,
            target.Tree,
            parent?.Tree,
            change => ChangeTouchesAnyPath(change, firstSet));
        var firstCommit = CreateReplayedCommit(repo, target, firstTree, ParentList(parent), firstMessage);

        var secondTree = BuildTreeFromChanges(
            repo,
            parent?.Tree,
            target.Tree,
            firstTree,
            change => !ChangeTouchesAnyPath(change, firstSet));
        var secondCommit = CreateReplayedCommit(repo, target, secondTree, [firstCommit], secondMessage);
        mapping[target.Sha] = secondCommit;

        Commit newParent = secondCommit;
        foreach (var oldCommit in chain.Skip(targetIndex + 1))
        {
            var oldParents = oldCommit.Parents.ToList();
            var newParents = oldParents
                .Select(p => mapping.TryGetValue(p.Sha, out var mapped) ? mapped : p)
                .ToList();
            var newTree = BuildReplayedTree(
                repo,
                oldParents.FirstOrDefault()?.Tree,
                oldCommit.Tree,
                newParents.FirstOrDefault()?.Tree,
                excludedPath: null);
            newParent = CreateReplayedCommit(repo, oldCommit, newTree, newParents);
            mapping[oldCommit.Sha] = newParent;
        }

        repo.Refs.UpdateTarget(branch.Reference, newParent.Id, $"split {ShortSha(target.Sha)}");
        _context.RaiseHeadChanged();
        return Task.CompletedTask;
    }

    public Task<string> CreateOrphanBranchAsync(string branchName, bool commitCurrentTree)
    {
        if (string.IsNullOrWhiteSpace(branchName))
            throw new ArgumentException("Branch name is required.", nameof(branchName));

        using var repo = _context.OpenRepository();
        EnsureCleanWorkingTree(repo, "create an orphan branch");
        if (repo.Branches[branchName] is not null)
            throw new InvalidOperationException($"Branch already exists: {branchName}");

        var tree = commitCurrentTree && repo.Head.Tip is { } head
            ? head.Tree
            : repo.ObjectDatabase.CreateTree(new TreeDefinition());
        var sig = repo.Config.BuildSignature(DateTimeOffset.Now)
            ?? new Signature("Gitster", "gitster@local", DateTimeOffset.Now);
        var commit = repo.ObjectDatabase.CreateCommit(
            sig,
            sig,
            commitCurrentTree ? "Initial orphan branch snapshot" : "Initial empty orphan branch",
            tree,
            [],
            prettifyMessage: false);
        var branch = repo.Branches.Add(branchName.Trim(), commit);
        Commands.Checkout(repo, branch);
        repo.Reset(ResetMode.Hard, commit);

        _context.RaiseHeadChanged();
        return Task.FromResult(branch.FriendlyName);
    }

    public Task<string> RescueDetachedHeadAsync(string branchName)
    {
        if (string.IsNullOrWhiteSpace(branchName))
            throw new ArgumentException("Branch name is required.", nameof(branchName));

        using var repo = _context.OpenRepository();
        if (!repo.Info.IsHeadDetached)
            throw new InvalidOperationException("HEAD is not detached.");
        var head = repo.Head.Tip ?? throw new InvalidOperationException("No detached HEAD commit.");
        if (repo.Branches[branchName] is not null)
            throw new InvalidOperationException($"Branch already exists: {branchName}");

        var branch = repo.Branches.Add(branchName.Trim(), head);
        Commands.Checkout(repo, branch);
        _context.RaiseHeadChanged();
        return Task.FromResult(branch.FriendlyName);
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
        => BuildTreeFromChanges(
            repo,
            oldParentTree,
            oldCommitTree,
            newParentTree,
            change => excludedPath is null || !ChangeTouchesPath(change, excludedPath));

    private static Tree BuildTreeFromChanges(
        Repository repo,
        Tree? oldParentTree,
        Tree oldCommitTree,
        Tree? newParentTree,
        Func<TreeEntryChanges, bool> includeChange)
    {
        var definition = newParentTree is null
            ? new TreeDefinition()
            : TreeDefinition.From(newParentTree);

        foreach (var change in repo.Diff.Compare<TreeChanges>(oldParentTree, oldCommitTree))
        {
            if (!includeChange(change))
                continue;

            ApplyTreeChange(definition, oldCommitTree, change);
        }

        return repo.ObjectDatabase.CreateTree(definition);
    }

    private static Commit CreateReplayedCommit(
        Repository repo,
        Commit oldCommit,
        Tree newTree,
        IReadOnlyList<Commit> newParents,
        string? message = null) =>
        repo.ObjectDatabase.CreateCommit(
            oldCommit.Author,
            oldCommit.Committer,
            message ?? oldCommit.Message,
            newTree,
            newParents,
            prettifyMessage: false);

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

    private static bool ChangeTouchesAnyPath(TreeEntryChanges change, IReadOnlySet<string> normalizedPaths) =>
        ChangePaths(change).Any(normalizedPaths.Contains);

    private static IEnumerable<string> ChangePaths(TreeEntryChanges change)
    {
        var path = NormalizeRepoPath(change.Path);
        yield return path;
        if (!string.IsNullOrWhiteSpace(change.OldPath))
        {
            var oldPath = NormalizeRepoPath(change.OldPath);
            if (!string.Equals(path, oldPath, StringComparison.Ordinal))
                yield return oldPath;
        }
    }

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

    private static List<Commit> ResolveCommitsInChain(IReadOnlyList<Commit> chain, IReadOnlyList<string> shas)
    {
        var result = new List<Commit>(shas.Count);
        foreach (var sha in shas)
        {
            var match = chain.FirstOrDefault(c => ShaMatches(c, sha))
                ?? throw new InvalidOperationException($"Commit not found on the current branch: {sha}");
            result.Add(match);
        }

        return result;
    }

    private static void EnsureSameCommitSet(IReadOnlyList<Commit> first, IReadOnlyList<Commit> second)
    {
        var firstSet = first.Select(c => c.Sha).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var secondSet = second.Select(c => c.Sha).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!firstSet.SetEquals(secondSet))
            throw new InvalidOperationException("The reordered commit list must contain the same commits.");
    }

    private static void EnsureNoMergeCommits(IEnumerable<Commit> commits, string operation)
    {
        var merge = commits.FirstOrDefault(c => c.Parents.Count() > 1);
        if (merge is not null)
            throw new InvalidOperationException($"Cannot {operation} through merge commit {ShortSha(merge.Sha)}.");
    }

    private static void EnsureNoOverlappingPathChanges(Repository repo, IReadOnlyList<Commit> commits, string operation)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var commit in commits)
        {
            foreach (var path in repo.Diff.Compare<TreeChanges>(commit.Parents.FirstOrDefault()?.Tree, commit.Tree)
                         .SelectMany(ChangePaths))
            {
                if (!seen.Add(path))
                    throw new InvalidOperationException(
                        $"Cannot {operation} because multiple selected commits change '{path}'.");
            }
        }
    }

    private static IReadOnlyList<Commit> ParentList(Commit? parent) =>
        parent is null ? [] : [parent];

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

    private static string ShortSha(string sha) => GitSha.Short(sha);
}
