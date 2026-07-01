using Gitster.Models;
using LibGit2Sharp;
using System.IO;

namespace Gitster.Services.Git.LibGit2;

internal sealed class LibGit2WorkingTreeOperations
{
    private readonly LibGit2RepositoryContext _context;

    public LibGit2WorkingTreeOperations(LibGit2RepositoryContext context) => _context = context;

    public Task<WorkingTreeState> GetWorkingTreeStateAsync()
    {
        using var repo = _context.OpenRepository();

        var gitDir = repo.Info.Path;
        var rebaseMerge = Path.Combine(gitDir, "rebase-merge");
        var rebaseApply = Path.Combine(gitDir, "rebase-apply");
        if (Directory.Exists(rebaseMerge) || Directory.Exists(rebaseApply))
        {
            var (step, total) = ReadRebaseProgress(rebaseMerge, rebaseApply);
            return Task.FromResult<WorkingTreeState>(new WorkingTreeState.Rebasing(step, total));
        }

        if (File.Exists(Path.Combine(gitDir, "MERGE_HEAD")))
            return Task.FromResult<WorkingTreeState>(new WorkingTreeState.Merging(repo.Head.FriendlyName));

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

    public Task<WorkingTreeStatus> GetWorkingTreeStatusAsync()
    {
        using var repo = _context.OpenRepository();
        var headTree = repo.Head?.Tip?.Tree;

        var staged = new List<WorkingTreeFile>();
        var unstaged = new List<WorkingTreeFile>();

        try
        {
            foreach (var e in repo.Diff.Compare<Patch>(headTree, DiffTargets.Index))
                staged.Add(new WorkingTreeFile(e.Path, MapStaged(e.Status), Staged: true, e.LinesAdded, e.LinesDeleted));
        }
        catch
        {
        }

        try
        {
            foreach (var e in repo.Diff.Compare<Patch>(null, includeUntracked: true, explicitPathsOptions: null))
                unstaged.Add(new WorkingTreeFile(e.Path, MapWorkdir(e.Status), Staged: false, e.LinesAdded, e.LinesDeleted));
        }
        catch
        {
        }

        return Task.FromResult(new WorkingTreeStatus(staged, unstaged));
    }

    public Task StageAsync(IEnumerable<string> paths)
    {
        using var repo = _context.OpenRepository();
        var list = paths.ToList();
        if (list.Count > 0)
            Commands.Stage(repo, list);
        return Task.CompletedTask;
    }

    public Task UnstageAsync(IEnumerable<string> paths)
    {
        using var repo = _context.OpenRepository();
        var list = paths.ToList();
        if (list.Count > 0)
            Commands.Unstage(repo, list);
        return Task.CompletedTask;
    }

    public Task StageAllAsync()
    {
        using var repo = _context.OpenRepository();
        Commands.Stage(repo, "*");
        return Task.CompletedTask;
    }

    public Task<string> CommitAsync(CommitRequest request)
    {
        using var repo = _context.OpenRepository();
        var fallback = repo.Config.BuildSignature(DateTimeOffset.Now)
            ?? new Signature("Gitster", "gitster@local", DateTimeOffset.Now);

        if (request.Amend)
        {
            var head = repo.Head.Tip ?? throw new InvalidOperationException("No HEAD commit to amend.");
            var author = new Signature(
                request.AuthorName ?? head.Author.Name,
                request.AuthorEmail ?? head.Author.Email,
                head.Author.When);
            var committer = new Signature(
                request.CommitterName ?? fallback.Name,
                request.CommitterEmail ?? fallback.Email,
                DateTimeOffset.Now);
            var amended = repo.Commit(request.Message, author, committer,
                new CommitOptions { AmendPreviousCommit = true });
            _context.RaiseHeadChanged();
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
            var commit = repo.Commit(request.Message, author, committer);
            _context.RaiseHeadChanged();
            return Task.FromResult(commit.Sha);
        }
    }

    public Task<string> AmendAsync(AmendRequest request)
    {
        using var repo = _context.OpenRepository();
        EnsureAttachedHead(repo, "amend commits");

        var commit = repo.Head.Tip ?? throw new InvalidOperationException("No HEAD commit.");
        var offset = DateTimeOffset.Now.Offset;

        var newAuthor = new Signature(
            request.AuthorName ?? commit.Author.Name,
            request.AuthorEmail ?? commit.Author.Email,
            new DateTimeOffset(request.NewDate.Year, request.NewDate.Month, request.NewDate.Day,
                request.NewDate.Hour, request.NewDate.Minute, commit.Author.When.Second, offset));

        var newCommitter = new Signature(
            request.CommitterName ?? commit.Committer.Name,
            request.CommitterEmail ?? commit.Committer.Email,
            new DateTimeOffset(request.NewDate.Year, request.NewDate.Month, request.NewDate.Day,
                request.NewDate.Hour, request.NewDate.Minute, commit.Committer.When.Second, offset));

        var amended = repo.Commit(
            request.NewMessage ?? commit.Message,
            newAuthor, newCommitter,
            new CommitOptions { AmendPreviousCommit = true });
        return Task.FromResult(amended.Id.Sha);
    }

    private static WorkingFileStatus MapStaged(ChangeKind kind) => kind switch
    {
        ChangeKind.Added => WorkingFileStatus.Added,
        ChangeKind.Deleted => WorkingFileStatus.Deleted,
        ChangeKind.Renamed => WorkingFileStatus.Renamed,
        ChangeKind.TypeChanged => WorkingFileStatus.TypeChange,
        ChangeKind.Conflicted => WorkingFileStatus.Conflicted,
        _ => WorkingFileStatus.Modified,
    };

    private static WorkingFileStatus MapWorkdir(ChangeKind kind) => kind switch
    {
        ChangeKind.Added => WorkingFileStatus.Untracked,
        ChangeKind.Deleted => WorkingFileStatus.Deleted,
        ChangeKind.Renamed => WorkingFileStatus.Renamed,
        ChangeKind.TypeChanged => WorkingFileStatus.TypeChange,
        ChangeKind.Conflicted => WorkingFileStatus.Conflicted,
        _ => WorkingFileStatus.Modified,
    };

    private static (int Step, int Total) ReadRebaseProgress(string rebaseMerge, string rebaseApply)
    {
        try
        {
            if (Directory.Exists(rebaseMerge))
            {
                var step = ReadIntFile(Path.Combine(rebaseMerge, "msgnum"));
                var total = ReadIntFile(Path.Combine(rebaseMerge, "end"));
                return (step, total);
            }

            if (Directory.Exists(rebaseApply))
            {
                var step = ReadIntFile(Path.Combine(rebaseApply, "next"));
                var total = ReadIntFile(Path.Combine(rebaseApply, "last"));
                return (step, total);
            }
        }
        catch
        {
        }

        return (0, 0);
    }

    private static int ReadIntFile(string path)
        => File.Exists(path) && int.TryParse(File.ReadAllText(path).Trim(), out var n) ? n : 0;

    private static void EnsureAttachedHead(Repository repo, string operation)
    {
        if (repo.Info.IsHeadDetached)
            throw new InvalidOperationException($"Cannot {operation} while HEAD is detached. Check out a local branch first.");
    }
}
