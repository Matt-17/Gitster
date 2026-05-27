using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using Gitster.Models;
using Gitster.Services.Git;
using Timer = System.Timers.Timer;

namespace Gitster.Services;

public partial class RepositoryStateService : ObservableObject, IDisposable
{
    private readonly IGitBackend _git;
    private FileSystemWatcher? _indexWatcher;
    private FileSystemWatcher? _workingDirWatcher;
    private readonly Timer _debounceTimer;
    private const int DebounceMs = 200;

    [ObservableProperty]
    private WorkingTreeState _workingTreeState = new WorkingTreeState.Clean();

    [ObservableProperty]
    private string? _currentBranch;

    [ObservableProperty]
    private string? _repositoryPath;

    public RepositoryStateService(IGitBackend git)
    {
        _git = git;
        _debounceTimer = new Timer(DebounceMs) { AutoReset = false };
        _debounceTimer.Elapsed += async (_, _) => await RefreshAsync();
    }

    public async Task AttachAsync(string repoPath)
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
            _indexWatcher.Changed += OnGitChanged;
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

        await RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        if (RepositoryPath is null)
            return;

        try
        {
            var state = await _git.GetWorkingTreeStateAsync();
            var branch = await _git.GetCurrentBranchAsync();

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                WorkingTreeState = state;
                CurrentBranch = branch.Name;
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"RepositoryStateService.RefreshAsync failed: {ex}");
        }
    }

    private void OnGitChanged(object sender, FileSystemEventArgs e) => RequestRefresh();

    private void OnWorkingDirChanged(object sender, FileSystemEventArgs e)
    {
        if (e.FullPath.Contains(Path.DirectorySeparatorChar + ".git" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            return;

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

        _workingDirWatcher?.Dispose();
        _workingDirWatcher = null;
    }

    public void Dispose()
    {
        DetachWatchers();
        _debounceTimer.Dispose();
    }
}
