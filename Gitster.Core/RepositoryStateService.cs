using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using Gitster.Core.Models;
using Gitster.Core.Git;
using Gitster.Core.Ui;
using Timer = System.Timers.Timer;

namespace Gitster.Core;

[Flags]
public enum RepositoryActivationChange
{
    None = 0,
    WorkingTree = 1,
    GitMetadata = 2,
}

public partial class RepositoryStateService : ObservableObject, IDisposable
{
    private readonly IGitBackend _git;
    private readonly IDispatcher? _dispatcher;
    private FileSystemWatcher? _indexWatcher;
    private FileSystemWatcher? _gitWatcher;
    private FileSystemWatcher? _workingDirWatcher;
    private readonly Timer _debounceTimer;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private RepositoryStateSnapshot? _lastSnapshot;
    private bool _pendingWorkingTreeChange;
    private bool _pendingGitMetadataChange;
    private const int DebounceMs = 200;

    [ObservableProperty]
    private WorkingTreeState _workingTreeState = new WorkingTreeState.Clean();

    [ObservableProperty]
    private string? _currentBranch;

    [ObservableProperty]
    private string? _repositoryPath;

    [ObservableProperty]
    private int _gitMetadataVersion;

    [ObservableProperty]
    private bool _isOperationRunning;

    public RepositoryStateService(IGitBackend git, IDispatcher? dispatcher = null)
    {
        _git = git;
        _dispatcher = dispatcher;
        _debounceTimer = new Timer(DebounceMs) { AutoReset = false };
        _debounceTimer.Elapsed += async (_, _) => await RefreshAsync();
    }

    public async Task AttachAsync(string repoPath, bool refreshImmediately = true)
    {
        DetachWatchers();
        RepositoryPath = repoPath;

        var gitDir = Path.Combine(repoPath, ".git");
        if (File.Exists(gitDir))
        {
            var content = await File.ReadAllTextAsync(gitDir);
            if (content.StartsWith("gitdir:", StringComparison.OrdinalIgnoreCase))
                gitDir = content.Substring(7).Trim();
        }

        var indexPath = Path.Combine(gitDir, "index");
        if (File.Exists(indexPath))
        {
            _indexWatcher = new FileSystemWatcher(gitDir)
            {
                Filter = "index",
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true,
            };
            _indexWatcher.Changed += OnIndexChanged;
        }

        if (Directory.Exists(gitDir))
        {
            _gitWatcher = new FileSystemWatcher(gitDir)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                EnableRaisingEvents = true,
            };
            _gitWatcher.Changed += OnGitMetadataChanged;
            _gitWatcher.Created += OnGitMetadataChanged;
            _gitWatcher.Deleted += OnGitMetadataChanged;
            _gitWatcher.Renamed += OnGitMetadataChanged;
        }

        _workingDirWatcher = new FileSystemWatcher(repoPath)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
            EnableRaisingEvents = true,
        };
        _workingDirWatcher.Changed += OnWorkingDirChanged;
        _workingDirWatcher.Created += OnWorkingDirChanged;
        _workingDirWatcher.Deleted += OnWorkingDirChanged;
        _workingDirWatcher.Renamed += OnWorkingDirChanged;

        if (refreshImmediately)
            await RefreshAsync();
    }

    public async Task<RepositoryActivationChange> GetActivationChangesAsync()
    {
        if (RepositoryPath is null)
            return RepositoryActivationChange.None;

        var repoPath = RepositoryPath;
        var snapshot = await Task.Run(() => CaptureSnapshot(repoPath));
        var prior = _lastSnapshot;

        var changes = RepositoryActivationChange.None;
        if (_pendingWorkingTreeChange)
            changes |= RepositoryActivationChange.WorkingTree;
        if (_pendingGitMetadataChange)
            changes |= RepositoryActivationChange.GitMetadata;

        if (prior is null)
            return changes;

        if (!string.Equals(snapshot.GitToken, prior.GitToken, StringComparison.Ordinal))
            changes |= RepositoryActivationChange.GitMetadata;
        if (!string.Equals(snapshot.IndexToken, prior.IndexToken, StringComparison.Ordinal))
            changes |= RepositoryActivationChange.WorkingTree;

        return changes;
    }

    public async Task RefreshAsync(IProgress<OperationProgress>? progress = null)
    {
        if (RepositoryPath is null)
            return;

        var repoPath = RepositoryPath;
        await _refreshGate.WaitAsync();
        try
        {
            progress?.Report(new OperationProgress(
                "Refreshing repository",
                "Checking working tree state.",
                25));

            var priorSnapshot = _lastSnapshot;
            var hadPendingGitMetadataChange = _pendingGitMetadataChange;
            var result = await Task.Run(async () =>
            {
                var state = await _git.GetWorkingTreeStateAsync();
                var branch = await _git.GetCurrentBranchAsync();
                var snapshot = CaptureSnapshot(repoPath);
                return (state, branch, snapshot);
            });
            var gitMetadataChanged = hadPendingGitMetadataChange
                || (priorSnapshot is not null
                    && !string.Equals(result.snapshot.GitToken, priorSnapshot.GitToken, StringComparison.Ordinal));

            if (!string.Equals(RepositoryPath, repoPath, StringComparison.OrdinalIgnoreCase))
                return;

            progress?.Report(new OperationProgress(
                "Refreshing repository",
                "Updating repository status.",
                80));

            if (_dispatcher is null || _dispatcher.IsDispatcherThread)
            {
                if (!string.Equals(RepositoryPath, repoPath, StringComparison.OrdinalIgnoreCase))
                    return;

                ApplyRefreshResult(result.state, result.branch.Name, gitMetadataChanged);
            }
            else
            {
                var applied = await _dispatcher.InvokeAsync(() =>
                {
                    if (!string.Equals(RepositoryPath, repoPath, StringComparison.OrdinalIgnoreCase))
                        return false;

                    ApplyRefreshResult(result.state, result.branch.Name, gitMetadataChanged);
                    return true;
                });
                if (!applied)
                    return;
            }

            _lastSnapshot = result.snapshot;
            _pendingWorkingTreeChange = false;
            _pendingGitMetadataChange = false;

            progress?.Report(new OperationProgress(
                "Refreshing repository",
                "Repository status is current.",
                100));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"RepositoryStateService.RefreshAsync failed: {ex}");
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private void OnIndexChanged(object sender, FileSystemEventArgs e)
    {
        _pendingWorkingTreeChange = true;
        RequestRefresh();
    }

    private void ApplyRefreshResult(
        WorkingTreeState state,
        string branchName,
        bool gitMetadataChanged)
    {
        WorkingTreeState = state;
        CurrentBranch = branchName;
        if (gitMetadataChanged)
            GitMetadataVersion++;
    }

    public async Task<IDisposable> BeginOperationAsync(CancellationToken ct = default)
    {
        await _operationGate.WaitAsync(ct);
        SetOperationRunning(true);
        return new OperationLease(this);
    }

    public void ThrowIfIndexLocked()
    {
        if (RepositoryPath is null)
            return;

        var lockPath = Path.Combine(ResolveGitDir(RepositoryPath), "index.lock");
        if (File.Exists(lockPath))
            throw new InvalidOperationException(
                "Another Git process appears to be running because .git/index.lock exists. " +
                "Wait for it to finish, then retry the operation.");
    }

    private void OnGitMetadataChanged(object sender, FileSystemEventArgs e)
    {
        if (!IsInterestingGitMetadataPath(e.FullPath))
            return;

        _pendingGitMetadataChange = true;
        RequestRefresh();
    }

    private void OnWorkingDirChanged(object sender, FileSystemEventArgs e)
    {
        if (e.FullPath.Contains(Path.DirectorySeparatorChar + ".git" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            return;

        _pendingWorkingTreeChange = true;
        RequestRefresh();
    }

    private void RequestRefresh()
    {
        _debounceTimer.Stop();
        _debounceTimer.Start();
    }

    private void DetachWatchers()
    {
        _indexWatcher?.Dispose();
        _indexWatcher = null;

        _gitWatcher?.Dispose();
        _gitWatcher = null;

        _workingDirWatcher?.Dispose();
        _workingDirWatcher = null;
    }

    private static bool IsInterestingGitMetadataPath(string path)
    {
        var normalized = path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        return normalized.EndsWith(Path.DirectorySeparatorChar + "HEAD", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith(Path.DirectorySeparatorChar + "FETCH_HEAD", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith(Path.DirectorySeparatorChar + "packed-refs", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains(Path.DirectorySeparatorChar + "refs" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || normalized.Contains(Path.DirectorySeparatorChar + "logs" + Path.DirectorySeparatorChar + "HEAD", StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        DetachWatchers();
        _debounceTimer.Dispose();
        _refreshGate.Dispose();
        _operationGate.Dispose();
    }

    private void SetOperationRunning(bool value)
    {
        if (_dispatcher is null || _dispatcher.IsDispatcherThread)
        {
            IsOperationRunning = value;
            return;
        }

        _dispatcher.Post(() => IsOperationRunning = value);
    }

    private static RepositoryStateSnapshot CaptureSnapshot(string repoPath)
    {
        try
        {
            var gitDir = ResolveGitDir(repoPath);
            var headPath = Path.Combine(gitDir, "HEAD");
            var headText = SafeReadText(headPath);
            var headRefToken = ResolveHeadRefToken(gitDir, headText);
            var gitToken = string.Join("|",
                headText,
                headRefToken,
                FileToken(Path.Combine(gitDir, "logs", "HEAD")),
                FileToken(Path.Combine(gitDir, "packed-refs")),
                FileToken(Path.Combine(gitDir, "FETCH_HEAD")),
                DirectoryToken(Path.Combine(gitDir, "refs", "remotes")));

            return new RepositoryStateSnapshot(
                gitToken,
                FileToken(Path.Combine(gitDir, "index")));
        }
        catch
        {
            return new RepositoryStateSnapshot(string.Empty, string.Empty);
        }
    }

    private static string ResolveGitDir(string repoPath)
    {
        var dotGit = Path.Combine(repoPath, ".git");
        if (Directory.Exists(dotGit))
            return dotGit;

        if (!File.Exists(dotGit))
            return dotGit;

        var content = File.ReadAllText(dotGit).Trim();
        if (!content.StartsWith("gitdir:", StringComparison.OrdinalIgnoreCase))
            return dotGit;

        var path = content[7..].Trim();
        return Path.IsPathRooted(path)
            ? path
            : Path.GetFullPath(Path.Combine(repoPath, path));
    }

    private static string ResolveHeadRefToken(string gitDir, string headText)
    {
        const string Prefix = "ref:";
        if (!headText.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
            return headText;

        var refName = headText[Prefix.Length..].Trim();
        var refPath = Path.Combine(gitDir, refName.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(refPath)
            ? $"{refName}:{SafeReadText(refPath)}:{FileToken(refPath)}"
            : $"{refName}:{PackedRefValue(Path.Combine(gitDir, "packed-refs"), refName)}";
    }

    private static string PackedRefValue(string packedRefsPath, string refName)
    {
        if (!File.Exists(packedRefsPath))
            return string.Empty;

        foreach (var line in File.ReadLines(packedRefsPath))
        {
            if (line.Length == 0 || line[0] is '#' or '^')
                continue;

            var parts = line.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2 && string.Equals(parts[1], refName, StringComparison.Ordinal))
                return parts[0];
        }

        return string.Empty;
    }

    private static string SafeReadText(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path).Trim() : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string FileToken(string path)
    {
        try
        {
            if (!File.Exists(path))
                return string.Empty;

            var info = new FileInfo(path);
            return $"{info.LastWriteTimeUtc.Ticks}:{info.Length}";
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string DirectoryToken(string path)
    {
        try
        {
            if (!Directory.Exists(path))
                return string.Empty;

            return string.Join(";", Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .Select(p => $"{Path.GetRelativePath(path, p)}={FileToken(p)}"));
        }
        catch
        {
            return string.Empty;
        }
    }

    private sealed record RepositoryStateSnapshot(string GitToken, string IndexToken);

    private sealed class OperationLease : IDisposable
    {
        private RepositoryStateService? _owner;

        public OperationLease(RepositoryStateService owner) => _owner = owner;

        public void Dispose()
        {
            var owner = Interlocked.Exchange(ref _owner, null);
            if (owner is null)
                return;

            owner.SetOperationRunning(false);
            owner._operationGate.Release();
        }
    }
}
