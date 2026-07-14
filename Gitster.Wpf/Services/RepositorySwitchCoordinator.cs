using System.Windows;
using System.Windows.Threading;

using Gitster.Core.Models;
using Gitster.ViewModels;
using Gitster.Views;

using Gitster.Core;

namespace Gitster.Services;

public sealed record RepositorySwitchRequest(
    string TargetPath,
    bool RecordRecent,
    bool ShowLoadingWindow);

public sealed record RepositorySwitchState(
    string Path,
    string FolderPath,
    string? LoadedRepositoryPath);

public sealed record RepositorySwitchCallbacks(
    Func<RepositorySwitchState> CaptureState,
    Func<string, CancellationToken, IProgress<RepositoryLoadProgress>?, Task> LoadRepositoryAsync,
    Action<string, bool> CommitRepositoryPath,
    Func<RepositorySwitchState, Task<string?>> RestoreRepositoryAsync,
    Action<Exception> ShowOpenError);

public sealed class RepositorySwitchCoordinator
{
    private readonly IWindowService _windowService;
    private readonly HeadRefreshCoordinator _headRefresh;
    private CancellationTokenSource? _repoSwitchCts;

    public RepositorySwitchCoordinator(
        IWindowService windowService,
        HeadRefreshCoordinator headRefresh)
    {
        _windowService = windowService;
        _headRefresh = headRefresh;
    }

    public bool IsSwitchingRepository { get; private set; }

    public string? LoadedRepositoryPath { get; private set; }

    public async Task<bool> SwitchAsync(
        RepositorySwitchRequest request,
        RepositorySwitchCallbacks callbacks)
    {
        if (string.IsNullOrWhiteSpace(request.TargetPath))
            return false;

        if (IsSameRepository(request.TargetPath, LoadedRepositoryPath))
        {
            callbacks.CommitRepositoryPath(request.TargetPath, request.RecordRecent);
            LoadedRepositoryPath = request.TargetPath;
            return true;
        }

        _repoSwitchCts?.Cancel();

        var previousState = callbacks.CaptureState() with { LoadedRepositoryPath = LoadedRepositoryPath };
        using var cts = new CancellationTokenSource();
        _repoSwitchCts = cts;
        IsSwitchingRepository = true;

        using var _ = _headRefresh.Suspend();

        try
        {
            var success = request.ShowLoadingWindow
                ? await RunRepositorySwitchWithDialogAsync(request.TargetPath, callbacks.LoadRepositoryAsync, cts)
                : await RunRepositorySwitchAsync(request.TargetPath, callbacks.LoadRepositoryAsync, cts.Token, progress: null);

            if (_repoSwitchCts != cts)
                return false;

            if (!success)
            {
                LoadedRepositoryPath = await callbacks.RestoreRepositoryAsync(previousState);
                return false;
            }

            callbacks.CommitRepositoryPath(request.TargetPath, request.RecordRecent);
            LoadedRepositoryPath = request.TargetPath;
            return true;
        }
        catch (OperationCanceledException)
        {
            if (_repoSwitchCts != cts)
                return false;

            LoadedRepositoryPath = await callbacks.RestoreRepositoryAsync(previousState);
            return false;
        }
        catch (Exception ex)
        {
            if (_repoSwitchCts != cts)
                return false;

            LoadedRepositoryPath = await callbacks.RestoreRepositoryAsync(previousState);
            callbacks.ShowOpenError(ex);
            return false;
        }
        finally
        {
            if (_repoSwitchCts == cts)
            {
                _repoSwitchCts = null;
                IsSwitchingRepository = false;
            }
        }
    }

    private async Task<bool> RunRepositorySwitchWithDialogAsync(
        string targetPath,
        Func<string, CancellationToken, IProgress<RepositoryLoadProgress>?, Task> loadRepositoryAsync,
        CancellationTokenSource cts)
    {
        var loadingVm = new RepositoryLoadingViewModel(targetPath, cts);
        var loadingWindow = new RepositoryLoadingWindow(loadingVm);
        var progress = new Progress<RepositoryLoadProgress>(loadingVm.Report);

        Task? loadTask = null;
        loadingWindow.ContentRendered += async (_, _) =>
        {
            if (loadTask != null)
                return;

            await loadingWindow.Dispatcher.InvokeAsync(
                () => { },
                DispatcherPriority.ApplicationIdle);

            loadTask = RunRepositorySwitchAsync(targetPath, loadRepositoryAsync, cts.Token, progress);
            _ = loadTask.ContinueWith(task =>
            {
                Application.Current.Dispatcher.BeginInvoke(() =>
                {
                    loadingWindow.Complete(task.Status == TaskStatus.RanToCompletion);
                });
            }, CancellationToken.None);
        };

        var dialogResult = _windowService.ShowDialog(loadingWindow);
        if (dialogResult != true)
            cts.Cancel();

        if (loadTask == null)
            return false;

        await loadTask;
        return true;
    }

    private static async Task<bool> RunRepositorySwitchAsync(
        string targetPath,
        Func<string, CancellationToken, IProgress<RepositoryLoadProgress>?, Task> loadRepositoryAsync,
        CancellationToken ct,
        IProgress<RepositoryLoadProgress>? progress)
    {
        await loadRepositoryAsync(targetPath, ct, progress);
        return true;
    }

    private static bool IsSameRepository(string targetPath, string? loadedRepositoryPath)
    {
        if (string.IsNullOrWhiteSpace(loadedRepositoryPath))
            return false;

        try
        {
            return string.Equals(
                NormalizePath(targetPath),
                NormalizePath(loadedRepositoryPath),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return string.Equals(
                TrimTrailingSeparators(targetPath),
                TrimTrailingSeparators(loadedRepositoryPath),
                StringComparison.OrdinalIgnoreCase);
        }
    }

    private static string NormalizePath(string path) =>
        TrimTrailingSeparators(System.IO.Path.GetFullPath(path));

    private static string TrimTrailingSeparators(string path) =>
        path.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
}
