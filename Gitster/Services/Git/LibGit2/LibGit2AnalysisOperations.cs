using Gitster.Models;
using LibGit2Sharp;
using System.IO;

namespace Gitster.Services.Git.LibGit2;

internal sealed class LibGit2AnalysisOperations
{
    private readonly LibGit2RepositoryContext _context;

    public LibGit2AnalysisOperations(LibGit2RepositoryContext context) => _context = context;

    public Task<IReadOnlyList<BlameLine>> BlameAsync(
        string filePath,
        bool ignoreWhitespace,
        bool followMoves,
        CancellationToken ct = default)
    {
        using var repo = _context.OpenRepository();
        var result = new List<BlameLine>();

        var fullPath = Path.Combine(repo.Info.WorkingDirectory, filePath);
        var contentLines = File.Exists(fullPath) ? File.ReadAllLines(fullPath) : [];

        var blame = repo.Blame(filePath);
        var line = 0;
        foreach (var hunk in blame)
        {
            ct.ThrowIfCancellationRequested();
            var commit = hunk.FinalCommit;
            var author = commit?.Author.Name ?? string.Empty;
            var when = commit?.Author.When ?? DateTimeOffset.MinValue;
            var sha = commit?.Sha ?? string.Empty;
            for (var i = 0; i < hunk.LineCount; i++)
            {
                var content = line < contentLines.Length ? contentLines[line] : string.Empty;
                result.Add(new BlameLine(line + 1, sha, author, when, content));
                line++;
            }
        }

        return Task.FromResult<IReadOnlyList<BlameLine>>(result);
    }

    public Task<CompareResult> CompareRefsAsync(
        string baseRef,
        string compareRef,
        bool threeDot,
        CancellationToken ct = default)
    {
        using var repo = _context.OpenRepository();
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
            if (mergeBase != null)
                filter.ExcludeReachableFrom = mergeBase;
            commits = repo.Commits.QueryBy(filter).Select(ToInfo).ToList();
            explanation = $"A...B (three-dot): everything that differs since '{baseRef}' and '{compareRef}' diverged (their merge-base).";
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

        ct.ThrowIfCancellationRequested();
        var patch = repo.Diff.Compare<Patch>(fromTree, b.Tree);
        var files = patch.Select(e => new DiffFileEntry(e.Path, e.LinesAdded, e.LinesDeleted,
            e.Status switch { ChangeKind.Added => "A", ChangeKind.Deleted => "D", ChangeKind.Renamed => "R", _ => "M" },
            LibGit2DiffParser.ParseUnifiedDiff(e.Patch))).ToList();
        var diff = new CommitDiff(files, patch.LinesAdded, patch.LinesDeleted);

        return Task.FromResult(new CompareResult(commits, diff, explanation));

        static CommitInfo ToInfo(Commit c) => new(
            c.Id.Sha.Length >= 7 ? c.Id.Sha[..7] : c.Id.Sha,
            c.MessageShort,
            c.Author.When.DateTime,
            c.Author.Name ?? string.Empty,
            c.Author.Email ?? string.Empty,
            CommitRemoteState.LocalOnly,
            c.Id.Sha,
            ParentShas: c.Parents.Select(p => p.Id.Sha).ToList());
    }

    private static Commit? ResolveCommittish(Repository repo, string r)
    {
        if (string.IsNullOrWhiteSpace(r))
            return null;
        var c = repo.Lookup<Commit>(r);
        if (c != null)
            return c;
        if (repo.Branches[r]?.Tip is { } bt)
            return bt;
        if (repo.Tags[r] is { } tag && ResolveTagTargetCommit(tag) is { } tc)
            return tc;
        if (repo.Branches[$"origin/{r}"]?.Tip is { } rt)
            return rt;
        return null;
    }

    private static Commit? ResolveTagTargetCommit(Tag tag) =>
        tag.PeeledTarget as Commit;
}
