using Gitster.Helpers;
using Gitster.Models;
using LibGit2Sharp;

namespace Gitster.Services.Git.LibGit2;

internal sealed class LibGit2StashOperations
{
    private readonly LibGit2RepositoryContext _context;

    public LibGit2StashOperations(LibGit2RepositoryContext context) => _context = context;

    public Task<int> GetStashCountAsync()
    {
        using var repo = _context.OpenRepository();
        return Task.FromResult(repo.Stashes.Count());
    }

    public Task<IReadOnlyList<StashInfo>> GetStashesAsync()
    {
        using var repo = _context.OpenRepository();
        var result = new List<StashInfo>();
        var index = 0;

        foreach (var stash in repo.Stashes)
        {
            var rawMessage = stash.Message ?? string.Empty;
            var branchName = StashNamer.ParseBranchFromMessage(rawMessage);
            var commitSha = stash.WorkTree?.Id.Sha ?? string.Empty;
            var createdAt = stash.WorkTree?.Committer.When ?? DateTimeOffset.Now;

            var files = new List<StashFileChange>();
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
                                ChangeKind.Added => StashChangeKind.Added,
                                ChangeKind.Deleted => StashChangeKind.Deleted,
                                ChangeKind.Renamed => StashChangeKind.Renamed,
                                _ => StashChangeKind.Modified,
                            },
                            entry.LinesAdded,
                            entry.LinesDeleted));
                    }
                }
                catch
                {
                }
            }

            var autoName = StashNamer.Generate(rawMessage, branchName, files);
            result.Add(new StashInfo(index, rawMessage, branchName, createdAt, files, autoName, commitSha));
            index++;
        }

        return Task.FromResult<IReadOnlyList<StashInfo>>(result);
    }

    public Task<CommitDiff> GetStashDiffAsync(int stashIndex, CancellationToken ct = default)
    {
        using var repo = _context.OpenRepository();
        var stashList = repo.Stashes.ToList();
        if (stashIndex < 0 || stashIndex >= stashList.Count)
            return Task.FromResult(CommitDiff.Empty);

        var stash = stashList[stashIndex];
        var baseCommit = stash.WorkTree?.Parents.FirstOrDefault();
        if (stash.WorkTree == null || baseCommit == null)
            return Task.FromResult(CommitDiff.Empty);

        ct.ThrowIfCancellationRequested();
        try
        {
            var patch = repo.Diff.Compare<Patch>(baseCommit.Tree, stash.WorkTree.Tree);
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
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return Task.FromResult(CommitDiff.Empty);
        }
    }

    public Task ApplyStashAsync(int stashIndex, bool reinstateIndex = true)
    {
        using var repo = _context.OpenRepository();
        var opts = new StashApplyOptions();
        if (reinstateIndex)
            opts.ApplyModifiers = StashApplyModifiers.ReinstateIndex;

        var status = repo.Stashes.Apply(stashIndex, opts);
        return status switch
        {
            StashApplyStatus.Conflicts => throw new GitConflictException(
                "Applying the stash produced conflicts. Resolve them manually.",
                repositoryHalted: true),
            StashApplyStatus.NotFound => throw new InvalidOperationException(
                $"stash@{{{stashIndex}}} was not found."),
            _ => Task.CompletedTask,
        };
    }

    public Task PopStashAsync(int stashIndex, bool reinstateIndex = true)
    {
        using var repo = _context.OpenRepository();
        var opts = new StashApplyOptions();
        if (reinstateIndex)
            opts.ApplyModifiers = StashApplyModifiers.ReinstateIndex;

        var status = repo.Stashes.Pop(stashIndex, opts);
        if (status == StashApplyStatus.Conflicts)
            throw new GitConflictException(
                "Popping the stash produced conflicts. Resolve them manually.",
                repositoryHalted: true);
        if (status == StashApplyStatus.NotFound)
            throw new InvalidOperationException($"stash@{{{stashIndex}}} was not found.");

        _context.RaiseHeadChanged();
        return Task.CompletedTask;
    }

    public Task DropStashAsync(int stashIndex)
    {
        using var repo = _context.OpenRepository();
        repo.Stashes.Remove(stashIndex);
        return Task.CompletedTask;
    }

    public Task<string> CreateStashAsync(string message, bool includeUntracked = true)
    {
        using var repo = _context.OpenRepository();
        var sig = repo.Config.BuildSignature(DateTimeOffset.Now)
                  ?? new Signature("Gitster", "gitster@local", DateTimeOffset.Now);

        var modifiers = StashModifiers.Default;
        if (includeUntracked)
            modifiers |= StashModifiers.IncludeUntracked;

        var stash = repo.Stashes.Add(sig, message, modifiers);
        if (stash == null)
            throw new InvalidOperationException(
                "Nothing to stash - the working tree is clean.");

        return Task.FromResult(stash.WorkTree?.Id.Sha ?? string.Empty);
    }

    public Task<string> ConvertStashToBranchAsync(int stashIndex, string branchName)
    {
        using var repo = _context.OpenRepository();
        var stashList = repo.Stashes.ToList();
        if (stashIndex < 0 || stashIndex >= stashList.Count)
            throw new InvalidOperationException($"stash@{{{stashIndex}}} was not found.");

        var stash = stashList[stashIndex];
        var baseCommit = stash.WorkTree?.Parents.FirstOrDefault()
                         ?? throw new InvalidOperationException(
                             "Cannot determine the base commit for this stash.");

        var newBranch = repo.CreateBranch(branchName, baseCommit);
        Commands.Checkout(repo, newBranch);

        var opts = new StashApplyOptions { ApplyModifiers = StashApplyModifiers.ReinstateIndex };
        var status = repo.Stashes.Apply(stashIndex, opts);

        if (status == StashApplyStatus.Applied)
        {
            repo.Stashes.Remove(stashIndex);
            _context.RaiseHeadChanged();
            return Task.FromResult(branchName);
        }

        if (status == StashApplyStatus.Conflicts)
        {
            _context.RaiseHeadChanged();
            throw new GitConflictException(
                $"Branch '{branchName}' was created from the stash, but applying produced conflicts. " +
                "Both the branch and the stash are intact - resolve conflicts manually.",
                repositoryHalted: true);
        }

        throw new InvalidOperationException(
            $"Failed to apply stash during branch conversion (status: {status}).");
    }
}
