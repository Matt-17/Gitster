using Gitster.Core.Models;
using LibGit2Sharp;
using System.IO;

namespace Gitster.Core.Git.LibGit2;

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

        AppendStatusFallback(repo, staged, unstaged);

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

    private static void AppendStatusFallback(
        Repository repo,
        List<WorkingTreeFile> staged,
        List<WorkingTreeFile> unstaged)
    {
        var stagedPaths = new HashSet<string>(staged.Select(file => file.Path), StringComparer.Ordinal);
        var unstagedPaths = new HashSet<string>(unstaged.Select(file => file.Path), StringComparer.Ordinal);
        RepositoryStatus status;
        try
        {
            status = repo.RetrieveStatus(new StatusOptions
            {
                IncludeUntracked = true,
                RecurseUntrackedDirs = true,
            });
        }
        catch
        {
            AppendManualUntrackedFallback(repo, unstaged, unstagedPaths);
            return;
        }

        foreach (var entry in status)
        {
            if (!stagedPaths.Contains(entry.FilePath) && IsStaged(entry.State))
            {
                staged.Add(new WorkingTreeFile(
                    entry.FilePath,
                    MapStatus(entry.State, staged: true),
                    Staged: true,
                    Added: 0,
                    Deleted: 0));
                stagedPaths.Add(entry.FilePath);
            }

            if (!unstagedPaths.Contains(entry.FilePath) && IsWorkdir(entry.State))
            {
                var statusKind = MapStatus(entry.State, staged: false);
                unstaged.Add(new WorkingTreeFile(
                    entry.FilePath,
                    statusKind,
                    Staged: false,
                    Added: 0,
                    Deleted: 0));
                unstagedPaths.Add(entry.FilePath);
            }
        }
    }

    private static void AppendManualUntrackedFallback(
        Repository repo,
        List<WorkingTreeFile> unstaged,
        HashSet<string> unstagedPaths)
    {
        try
        {
            var workdir = repo.Info.WorkingDirectory;
            var gitDir = Path.GetFullPath(repo.Info.Path).TrimEnd(Path.DirectorySeparatorChar);
            var tracked = new HashSet<string>(repo.Index.Select(entry => NormalizePath(entry.Path)), StringComparer.Ordinal);

            foreach (var file in Directory.EnumerateFiles(workdir, "*", SearchOption.AllDirectories))
            {
                var fullPath = Path.GetFullPath(file);
                if (fullPath.StartsWith(gitDir, StringComparison.OrdinalIgnoreCase))
                    continue;

                var relative = NormalizePath(Path.GetRelativePath(workdir, fullPath));
                if (relative.StartsWith(".git/", StringComparison.OrdinalIgnoreCase)
                    || tracked.Contains(relative)
                    || unstagedPaths.Contains(relative)
                    || IsIgnored(repo, relative))
                {
                    continue;
                }

                unstaged.Add(new WorkingTreeFile(relative, WorkingFileStatus.Untracked, Staged: false, Added: 0, Deleted: 0));
                unstagedPaths.Add(relative);
            }
        }
        catch
        {
            // LibGit2 status is still authoritative when filesystem enumeration is unavailable.
        }
    }

    private static WorkingFileStatus MapStatus(FileStatus status, bool staged)
    {
        if ((status & FileStatus.Conflicted) != 0)
            return WorkingFileStatus.Conflicted;
        if ((status & (FileStatus.RenamedInIndex | FileStatus.RenamedInWorkdir)) != 0)
            return WorkingFileStatus.Renamed;
        if ((status & (FileStatus.TypeChangeInIndex | FileStatus.TypeChangeInWorkdir)) != 0)
            return WorkingFileStatus.TypeChange;
        if ((status & (FileStatus.DeletedFromIndex | FileStatus.DeletedFromWorkdir)) != 0)
            return WorkingFileStatus.Deleted;
        if ((status & FileStatus.NewInIndex) != 0)
            return WorkingFileStatus.Added;
        if (!staged && (status & FileStatus.NewInWorkdir) != 0)
            return WorkingFileStatus.Untracked;

        return WorkingFileStatus.Modified;
    }

    private static bool IsStaged(FileStatus status) =>
        (status & (FileStatus.NewInIndex
            | FileStatus.ModifiedInIndex
            | FileStatus.DeletedFromIndex
            | FileStatus.RenamedInIndex
            | FileStatus.TypeChangeInIndex
            | FileStatus.Conflicted)) != 0;

    private static bool IsWorkdir(FileStatus status) =>
        (status & (FileStatus.NewInWorkdir
            | FileStatus.ModifiedInWorkdir
            | FileStatus.DeletedFromWorkdir
            | FileStatus.RenamedInWorkdir
            | FileStatus.TypeChangeInWorkdir
            | FileStatus.Conflicted)) != 0;

    private static string NormalizePath(string path) => path.Replace('\\', '/');

    private static bool IsIgnored(Repository repo, string relativePath)
    {
        try
        {
            return repo.Ignore.IsPathIgnored(relativePath);
        }
        catch
        {
            return false;
        }
    }

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
