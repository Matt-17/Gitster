using LibGit2Sharp;
using System.IO;

namespace Gitster.Services.Git.LibGit2;

internal sealed class LibGit2BranchOperations
{
    private readonly LibGit2RepositoryContext _context;

    public LibGit2BranchOperations(LibGit2RepositoryContext context) => _context = context;

    public Task<IReadOnlyList<BranchListItem>> GetBranchListAsync()
    {
        using var repo = _context.OpenRepository();
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
                ahead = div.AheadBy ?? 0;
                behind = div.BehindBy ?? 0;
            }

            var isMerged = false;
            if (!b.IsCurrentRepositoryHead && tip != null && currentTip != null)
            {
                var mergeBase = repo.ObjectDatabase.FindMergeBase(tip, currentTip);
                isMerged = mergeBase != null &&
                           mergeBase.Sha.Equals(tip.Sha, StringComparison.OrdinalIgnoreCase);
            }

            result.Add(new BranchListItem(
                Name: b.FriendlyName,
                UpstreamName: tracked?.FriendlyName,
                TipSha: tip?.Sha ?? string.Empty,
                TipMessage: tip?.MessageShort ?? string.Empty,
                LastActivity: tip?.Committer.When ?? DateTimeOffset.MinValue,
                Ahead: ahead,
                Behind: behind,
                IsCurrent: b.IsCurrentRepositoryHead,
                IsRemote: b.IsRemote,
                IsMerged: isMerged));
        }

        return Task.FromResult<IReadOnlyList<BranchListItem>>(result);
    }

    public Task CheckoutBranchAsync(string branchName)
    {
        using var repo = _context.OpenRepository();
        var branch = repo.Branches[branchName]
            ?? throw new InvalidOperationException($"Branch not found: {branchName}");

        if (branch.IsRemote)
        {
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

        _context.RaiseHeadChanged();
        return Task.CompletedTask;
    }

    public Task<string> CreateBranchAsync(string name, string startPointSha)
    {
        using var repo = _context.OpenRepository();
        var commit = repo.Lookup<Commit>(startPointSha)
            ?? throw new InvalidOperationException($"Start point not found: {startPointSha}");
        var branch = repo.CreateBranch(name, commit);
        return Task.FromResult(branch.FriendlyName);
    }

    public Task DeleteBranchAsync(string name, bool force)
    {
        using var repo = _context.OpenRepository();
        var branch = repo.Branches[name]
            ?? throw new InvalidOperationException($"Branch not found: {name}");
        if (branch.IsCurrentRepositoryHead)
            throw new InvalidOperationException("Cannot delete the branch that is currently checked out.");
        repo.Branches.Remove(branch);
        return Task.CompletedTask;
    }

    public Task RenameBranchAsync(string oldName, string newName)
    {
        using var repo = _context.OpenRepository();
        var branch = repo.Branches[oldName]
            ?? throw new InvalidOperationException($"Branch not found: {oldName}");
        repo.Branches.Rename(branch, newName);
        if (branch.IsCurrentRepositoryHead)
            _context.RaiseHeadChanged();
        return Task.CompletedTask;
    }

    public Task<BranchMergeResult> MergeBranchAsync(string branchName, BranchMergeStrategy strategy)
    {
        using var repo = _context.OpenRepository();
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
            throw new GitConflictException(
                $"Merge of '{branchName}' produced conflicts. Resolve them in the working tree, then commit or abort the merge.",
                repositoryHalted: true);

        if (strategy == BranchMergeStrategy.FastForwardOnly && result.Status == MergeStatus.NonFastForward)
            throw new InvalidOperationException(
                $"Branch '{branchName}' cannot be fast-forwarded into '{targetBranch}'. Choose another merge strategy if you want a merge commit.");

        var headSha = repo.Head.Tip?.Sha ?? beforeHead;
        var outcome = result.Status switch
        {
            MergeStatus.UpToDate => BranchMergeOutcome.UpToDate,
            MergeStatus.FastForward => BranchMergeOutcome.FastForward,
            MergeStatus.NonFastForward => BranchMergeOutcome.MergeCommit,
            _ => BranchMergeOutcome.MergeCommit,
        };

        if (!headSha.Equals(beforeHead, StringComparison.OrdinalIgnoreCase))
            _context.RaiseHeadChanged();

        return Task.FromResult(new BranchMergeResult(branch.FriendlyName, targetBranch, headSha, outcome));
    }

    public async Task<HistoryStitchPreview> PreviewHistoryStitchAsync(string sourceRef)
    {
        await GitCli.DetectAsync();

        using var repo = _context.OpenRepository();
        var warnings = new List<string>();
        var blocks = new List<string>();
        var head = repo.Head.Tip;
        var targetBranch = repo.Info.IsHeadDetached ? "detached HEAD" : repo.Head.FriendlyName;
        var targetHeadSha = head?.Sha ?? string.Empty;
        var sourceBranch = repo.Branches[sourceRef];
        var source = sourceBranch?.Tip ?? repo.Lookup<Commit>(sourceRef);
        var sourceTipSha = source?.Sha ?? string.Empty;

        if (!GitCli.IsAvailable)
            blocks.Add(
                "Stitching history requires the Git command-line tool because Gitster runs git merge --no-ff -s ours exactly.");

        if (head is null)
            blocks.Add("Cannot stitch history in a repository without a HEAD commit.");

        if (repo.Info.IsHeadDetached)
            blocks.Add("Check out a local branch before stitching history.");

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
            blocks.Add("Commit or stash uncommitted changes before stitching history.");

        var isAlreadyReachable = false;
        var uniqueSourceCommitCount = 0;
        string? squashMatchSha = null;
        if (head is not null && source is not null)
        {
            var mergeBase = repo.ObjectDatabase.FindMergeBase(source, head);
            isAlreadyReachable = mergeBase?.Sha.Equals(source.Sha, StringComparison.OrdinalIgnoreCase) == true;
            if (isAlreadyReachable)
                blocks.Add($"'{sourceRef}' is already reachable from the current branch.");

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

    public Task<string> CommitToBranchAsync(CommitToBranchRequest request)
    {
        using var repo = _context.OpenRepository();

        if (string.Equals(request.TargetBranch, repo.Head.FriendlyName, StringComparison.Ordinal))
            throw new InvalidOperationException(
                "Target is the current branch - use the normal commit/amend flow instead.");

        var target = repo.Branches[request.TargetBranch]
            ?? throw new InvalidOperationException($"Target branch not found: {request.TargetBranch}");
        var targetTip = target.Tip
            ?? throw new InvalidOperationException("Target branch has no commits to build on.");

        var (tree, capturedPaths) = BuildCapturedTree(repo, request.IncludeUnstaged);
        if (capturedPaths.Count == 0)
            throw new InvalidOperationException("There are no changes to commit.");

        var fallback = repo.Config.BuildSignature(DateTimeOffset.Now)
                       ?? new Signature("Gitster", "gitster@local", DateTimeOffset.Now);
        var author = new Signature(
            request.AuthorName ?? fallback.Name,
            request.AuthorEmail ?? fallback.Email,
            DateTimeOffset.Now);

        var commit = repo.ObjectDatabase.CreateCommit(
            author, author, request.Message, tree, new[] { targetTip }, prettifyMessage: true);

        repo.Refs.UpdateTarget(target.Reference, commit.Id,
            $"commit to {request.TargetBranch}: {request.Message}");

        if (request.RemoveFromCurrent)
            RemoveCapturedChanges(repo, capturedPaths);

        _context.RaiseHeadChanged();
        return Task.FromResult(commit.Sha);
    }

    public Task<string> CreateSnapshotBranchAsync(string branchName, bool includeUncommitted)
    {
        using var repo = _context.OpenRepository();
        var head = repo.Head.Tip
            ?? throw new InvalidOperationException("Cannot snapshot - the repository has no commits yet.");

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

        return Task.FromResult(branch.FriendlyName);
    }

    private static (Tree Tree, HashSet<string> Paths) BuildCapturedTree(Repository repo, bool includeUnstaged)
    {
        var headTree = repo.Head.Tip?.Tree;
        var stagedTree = repo.ObjectDatabase.CreateTree(repo.Index);

        var paths = new HashSet<string>(StringComparer.Ordinal);

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

        var td = TreeDefinition.From(stagedTree);
        var workdir = repo.Info.WorkingDirectory;

        var status = repo.RetrieveStatus(new StatusOptions
        {
            IncludeUntracked = true,
            RecurseUntrackedDirs = true,
            Show = StatusShowOption.WorkDirOnly,
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
                repo.Index.Remove(path);
                try
                {
                    File.Delete(Path.Combine(workdir, path));
                }
                catch
                {
                }
            }
        }
        repo.Index.Write();

        if (inHead.Count > 0 && headTip != null)
        {
            repo.CheckoutPaths(headTip.Sha, inHead,
                new CheckoutOptions { CheckoutModifiers = CheckoutModifiers.Force });
        }
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
}
