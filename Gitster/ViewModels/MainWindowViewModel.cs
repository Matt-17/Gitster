using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Gitster.Services;
using Gitster.Services.Capabilities;
using Gitster.Services.Git;
using Gitster.Services.History;
using Gitster.Services.OperationsLog;
using Gitster.Views;
using Gitster.Views.Helper;

using Gitster.Models;

using LibGit2Sharp;

using Microsoft.Win32;

namespace Gitster.ViewModels;

/// <summary>
/// View model for the main window.
/// </summary>
public partial class MainWindowViewModel : BaseViewModel
{
    private readonly OperationsLogService _opsLogService;
    private readonly IWindowService _windowService;
    private readonly IGitBackend _gitBackend;
    private readonly RepositoryStateService _stateService;
    private readonly OperationFeedbackService _feedbackService;
    private readonly RecentReposService _recentRepos;
    private readonly AuthorDirectoryService _authorDirService;
    private readonly CommitHistoryService _historyService;
    private readonly SnapshotService _snapshotService;
    private readonly SourceArchiveService _sourceArchiveService;
    private readonly StashNameService _stashNameService;
    private readonly CustomToolsService _customToolsService;
    private readonly UiPreferencesService _uiPreferences;
    private CancellationTokenSource? _repoSwitchCts;
    private int _headRefreshRequestVersion;
    private bool _headRefreshRequested;
    private bool _headRefreshWorkerRunning;
    private readonly SemaphoreSlim _headRefreshGate = new(1, 1);
    private bool _suppressFolderPathChanged;
    private bool _isSwitchingRepository;
    private bool _initialRepositoryLoadStarted;
    private string? _loadedRepositoryPath;

    public TitleBarViewModel TitleBarVM { get; }
    public CommitListViewModel CommitListVM { get; }
    public TimestampEditViewModel TimestampEditVM { get; }
    public HistoryRewriteDraftViewModel HistoryRewriteDraftVM { get; }
    public QuickActionsViewModel QuickActionsVM { get; }
    public UndoBarViewModel UndoBarVM { get; }
    public StatusBarViewModel StatusBarVM { get; }
    public AutoFetchService AutoFetch { get; }
    public AuthorPanelViewModel AuthorPanelVM { get; }
    public SidebarViewModel SidebarVM { get; } = new();
    public StashesViewModel StashesVM { get; }
    public BranchesViewModel BranchesVM { get; }
    public WorktreesViewModel WorktreesVM { get; }
    public CommitPanelViewModel CommitPanelVM { get; }
    public SearchViewModel SearchVM { get; }
    public OperationsLogService OpsLogService => _opsLogService;
    public UiPreferencesService Ui => _uiPreferences;

    public MainWindowViewModel(
        IWindowService windowService,
        IGitBackend gitBackend,
        RepositoryStateService stateService,
        OperationFeedbackService feedbackService,
        RecentReposService recentRepos,
        AuthorDirectoryService authorDirService,
        CommitHistoryService historyService,
        AutoFetchService autoFetch,
        CapabilityService capabilityService,
        OperationsLogService opsLogService,
        SnapshotService snapshotService,
        SourceArchiveService sourceArchiveService,
        StashNameService stashNameService,
        CustomToolsService customToolsService,
        UiPreferencesService uiPreferences,
        StatusBarViewModel statusBarViewModel,
        CommitListViewModel commitListViewModel,
        UndoBarViewModel undoBarViewModel,
        AuthorPanelViewModel authorPanelViewModel)
    {
        _windowService = windowService;
        _gitBackend = gitBackend;
        _stateService = stateService;
        _feedbackService = feedbackService;
        _recentRepos = recentRepos;
        _authorDirService = authorDirService;
        _historyService = historyService;
        AutoFetch = autoFetch;
        _opsLogService = opsLogService;
        _snapshotService = snapshotService;
        _sourceArchiveService = sourceArchiveService;
        _stashNameService = stashNameService;
        _customToolsService = customToolsService;
        _uiPreferences = uiPreferences;
        PersistedGridSplitter.Initialize(_uiPreferences);

        Capability.Initialize(capabilityService);

        SelectedCommitDetail = new CommitDetailViewModel();
        CurrentCommitDetail = new CommitDetailViewModel();
        StatusBarVM = statusBarViewModel;
        TitleBarVM = new TitleBarViewModel(() => _ = BrowseFolderAsync(), OpenRepoByPath, AutoFetch, _recentRepos);
        TitleBarVM.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(TitleBarViewModel.RepositoryName) or nameof(TitleBarViewModel.CurrentBranch))
                OnPropertyChanged(nameof(WindowTitle));
        };
        CommitListVM = commitListViewModel;
        CommitListVM.PropertyChanged += OnCommitListVmPropertyChanged;
        TimestampEditVM = new TimestampEditViewModel(
            () => CommitListVM.SelectedCommit,
            () => CurrentCommitDetail.CommitDate);
        QuickActionsVM = new QuickActionsViewModel(
            _gitBackend,
            _feedbackService,
            _opsLogService,
            _snapshotService,
            _sourceArchiveService,
            _windowService,
            () => CommitListVM.SelectedCommit,
            () => CommitListVM.SelectedCommits,
            async () => await UpdateElementsAsync());
        HistoryRewriteDraftVM = new HistoryRewriteDraftViewModel(
            _gitBackend,
            _feedbackService,
            _opsLogService,
            _snapshotService,
            _windowService,
            () => TitleBarVM.CurrentBranch,
            async preferredSelectionSha =>
            {
                ClearPendingHeadRefresh();
                await RefreshAfterHeadChangeAsync();
                CommitListVM.SelectCommitBySha(preferredSelectionSha);
            });
        UndoBarVM = undoBarViewModel;
        UndoBarVM.AfterUndoAsync = async progress =>
        {
            ClearPendingHeadRefresh();
            progress.Report(new OperationProgress(
                "Refreshing view",
                "Updating HEAD and commit list.",
                92));
            await RefreshAfterHeadChangeAsync();
        };
        UndoBarVM.UndoCompleted += (_, _) => QueueHeadRefresh();
        AuthorPanelVM = authorPanelViewModel;
        StashesVM = new StashesViewModel(
            _gitBackend,
            _feedbackService,
            _opsLogService,
            _snapshotService,
            _stashNameService,
            _windowService,
            async () => await RefreshSidebarBadgesAsync());
        BranchesVM = new BranchesViewModel(
            _gitBackend,
            _feedbackService,
            _opsLogService,
            _snapshotService,
            _sourceArchiveService,
            _uiPreferences,
            _windowService,
            async () => await RefreshSidebarBadgesAsync());
        WorktreesVM = new WorktreesViewModel(
            _gitBackend,
            _feedbackService,
            _snapshotService,
            _windowService,
            () => Path,
            OpenRepoByPath);
        CommitPanelVM = new CommitPanelViewModel(
            _gitBackend,
            _feedbackService,
            _opsLogService,
            _snapshotService,
            _authorDirService,
            _windowService,
            async () => await UpdateElementsAsync(),
            () => TitleBarVM.CurrentBranch,
            () => string.IsNullOrEmpty(SelectedRemote) ? Remotes.FirstOrDefault() : SelectedRemote);
        SearchVM = new SearchViewModel(_gitBackend, _historyService, _windowService);

        // Update ops log badge whenever the log changes
        _opsLogService.Changed += (_, _) =>
            SidebarVM.ActiveOpsCount = _opsLogService.Records.Count(r => r.Status == OperationStatus.Active);

        // A.3 — refresh commit list after any backend HEAD mutation (e.g. Undo via ResetHard)
        _gitBackend.HeadChanged += (_, _) =>
            Application.Current.Dispatcher.BeginInvoke(() =>
            {
                if (!_isSwitchingRepository)
                    QueueHeadRefresh();
            });

        // Load saved path or use default
        _suppressFolderPathChanged = true;
        Path = Properties.Settings.Default.Path ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        FolderPath = Path;
        _suppressFolderPathChanged = false;

        _stateService.PropertyChanged += OnRepositoryStateChanged;
    }

    [ObservableProperty]
    public partial string Path { get; set; } = string.Empty;

    public string WindowTitle =>
        string.IsNullOrWhiteSpace(TitleBarVM.RepositoryName)
            ? "Gitster"
            : $"{TitleBarVM.RepositoryName}";

    public ObservableCollection<RecentRepoEntry> RecentRepos => _recentRepos.Entries;

    [ObservableProperty]
    public partial bool IsDarkMode { get; set; }

    [ObservableProperty]
    public partial string FolderPath { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string CommitName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string CommitDate { get; set; } = string.Empty;

    [ObservableProperty]
    public partial DateTime? SelectedDate { get; set; }

    [ObservableProperty]
    public partial bool IsGoButtonEnabled { get; set; }

    [ObservableProperty]
    public partial CommitItem? SelectedCommit { get; set; }

    [ObservableProperty]
    public partial CommitDetailViewModel SelectedCommitDetail { get; set; }

    [ObservableProperty]
    public partial CommitDetailViewModel CurrentCommitDetail { get; set; }

    [ObservableProperty]
    public partial string SelectedRemote { get; set; } = string.Empty;

    public ObservableCollection<string> Remotes { get; } = [];

    /// <summary>True when the selected commit is already on the remote — amending it requires a force-push.</summary>
    public bool IsAmendUnsafe => SelectedCommit?.RemoteState == Gitster.Services.Git.CommitRemoteState.OnRemote;

    public bool CanAmendSelectedCommit =>
        IsGoButtonEnabled
        && SelectedCommit is not null
        && SelectedCommit.RemoteState != Gitster.Services.Git.CommitRemoteState.Incoming;

    partial void OnIsGoButtonEnabledChanged(bool value)
    {
        OnPropertyChanged(nameof(CanAmendSelectedCommit));
        ArchiveHeadCommand.NotifyCanExecuteChanged();
    }

    partial void OnFolderPathChanged(string value)
    {
        if (_suppressFolderPathChanged)
            return;

        if (!string.Equals(value, Path, StringComparison.OrdinalIgnoreCase))
            _ = SwitchRepositoryAsync(value, recordRecent: false, showLoadingWindow: true);
    }

    partial void OnSelectedCommitChanged(CommitItem? value)
    {
        if (value != null)
            SelectedCommitDetail.UpdateCommit(value.Message, value.Date);
        else
            SelectedCommitDetail.Clear();

        OnPropertyChanged(nameof(IsAmendUnsafe));
        OnPropertyChanged(nameof(CanAmendSelectedCommit));
        _ = AuthorPanelVM.LoadFromCommitAsync(value);
    }

    private void OnCommitListVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // Diff loading lives in CommitListViewModel now (lazy + cancellable, A0.3). The main
        // window only mirrors the selection for the edit/author panels.
        if (e.PropertyName == nameof(CommitListViewModel.SelectedCommit))
        {
            SelectedCommit = CommitListVM.SelectedCommit;
            HistoryRewriteDraftVM.SetSelectedCommit(CommitListVM.SelectedCommit);
            QuickActionsVM.NotifySelectionChanged();
        }
        else if (e.PropertyName == nameof(CommitListViewModel.LoadedCommits))
        {
            HistoryRewriteDraftVM.SetCommits(CommitListVM.LoadedCommits);
        }
    }

    [RelayCommand]
    private async Task BrowseFolderAsync()
    {
        try
        {
            var initialDirectory = string.IsNullOrEmpty(FolderPath)
                ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
                : FolderPath;

            var dialog = new OpenFolderDialog
            {
                Title = "Select Git Repository Folder",
                InitialDirectory = initialDirectory
            };

            if (_windowService.ShowDialog(dialog) == true)
            {
                await SwitchRepositoryAsync(dialog.FolderName, recordRecent: true, showLoadingWindow: true);
            }
        }
        catch (Exception ex)
        {
            _windowService.Error($"Error opening folder dialog: {ex.Message}", "Gitster");
        }
    }

    /// <summary>Opens a repository by path directly (used by recent-repos dropdown).</summary>
    private void OpenRepoByPath(string path)
    {
        _ = SwitchRepositoryAsync(path, recordRecent: true, showLoadingWindow: true);
    }

    [RelayCommand]
    private async Task OpenRepoAsync(string path)
    {
        await SwitchRepositoryAsync(path, recordRecent: true, showLoadingWindow: true);
    }

    public async Task InitializeAsync()
    {
        if (_initialRepositoryLoadStarted)
            return;

        _initialRepositoryLoadStarted = true;
        if (!string.IsNullOrWhiteSpace(Path))
            await SwitchRepositoryAsync(Path, recordRecent: false, showLoadingWindow: true);
    }

    private async Task<bool> SwitchRepositoryAsync(string targetPath, bool recordRecent, bool showLoadingWindow)
    {
        if (string.IsNullOrWhiteSpace(targetPath))
            return false;

        _repoSwitchCts?.Cancel();

        var previousPath = Path;
        var previousFolderPath = FolderPath;
        var previousLoadedPath = _loadedRepositoryPath;
        using var cts = new CancellationTokenSource();
        _repoSwitchCts = cts;
        _isSwitchingRepository = true;

        try
        {
            var success = showLoadingWindow
                ? await RunRepositorySwitchWithDialogAsync(targetPath, cts)
                : await RunRepositorySwitchAsync(targetPath, cts.Token, progress: null);

            if (_repoSwitchCts != cts)
                return false;

            if (!success)
            {
                await RestoreRepositoryAfterCanceledSwitchAsync(previousPath, previousFolderPath, previousLoadedPath);
                return false;
            }

            CommitRepositoryPath(targetPath, recordRecent);
            return true;
        }
        catch (OperationCanceledException)
        {
            if (_repoSwitchCts != cts)
                return false;

            await RestoreRepositoryAfterCanceledSwitchAsync(previousPath, previousFolderPath, previousLoadedPath);
            return false;
        }
        catch (Exception ex)
        {
            if (_repoSwitchCts != cts)
                return false;

            await RestoreRepositoryAfterCanceledSwitchAsync(previousPath, previousFolderPath, previousLoadedPath);
            _windowService.Error($"Error opening repository:\n{ex.Message}", "Gitster");
            return false;
        }
        finally
        {
            if (_repoSwitchCts == cts)
            {
                _repoSwitchCts = null;
                _isSwitchingRepository = false;
            }
        }
    }

    private async Task<bool> RunRepositorySwitchWithDialogAsync(string targetPath, CancellationTokenSource cts)
    {
        var loadingVm = new RepositoryLoadingViewModel(targetPath, cts);
        var loadingWindow = new RepositoryLoadingWindow(loadingVm);
        var progress = new Progress<RepositoryLoadProgress>(loadingVm.Report);

        Task<bool>? loadTask = null;
        loadingWindow.ContentRendered += async (_, _) =>
        {
            if (loadTask != null)
                return;

            await loadingWindow.Dispatcher.InvokeAsync(
                () => { },
                DispatcherPriority.ApplicationIdle);

            loadTask = RunRepositorySwitchAsync(targetPath, cts.Token, progress);
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

    private async Task<bool> RunRepositorySwitchAsync(
        string targetPath,
        CancellationToken ct,
        IProgress<RepositoryLoadProgress>? progress)
    {
        await UpdateElementsCoreAsync(targetPath, ct, progress);
        return true;
    }

    private void CommitRepositoryPath(string targetPath, bool recordRecent)
    {
        _suppressFolderPathChanged = true;
        Path = targetPath;
        FolderPath = targetPath;
        _loadedRepositoryPath = targetPath;
        _suppressFolderPathChanged = false;

        UpdateSettingsPath();
        if (recordRecent)
            _recentRepos.Record(targetPath);
    }

    private async Task RestoreRepositoryAfterCanceledSwitchAsync(
        string previousPath,
        string previousFolderPath,
        string? previousLoadedPath)
    {
        _suppressFolderPathChanged = true;
        Path = previousPath;
        FolderPath = previousFolderPath;
        _suppressFolderPathChanged = false;

        if (string.IsNullOrWhiteSpace(previousLoadedPath))
        {
            _loadedRepositoryPath = null;
            ClearRepositoryUi();
            return;
        }

        try
        {
            await UpdateElementsCoreAsync(previousLoadedPath, CancellationToken.None, progress: null);
            _loadedRepositoryPath = previousLoadedPath;
        }
        catch
        {
            _loadedRepositoryPath = null;
            ClearRepositoryUi();
            // Keep the saved path even if the prior repository cannot be refreshed.
        }
    }

    [RelayCommand]
    private void Exit() => Application.Current.Shutdown();

    [RelayCommand]
    private async Task Refresh() => await UpdateElementsAsync();

    [RelayCommand(CanExecute = nameof(CanArchiveHead))]
    private async Task ArchiveHead()
    {
        if (string.IsNullOrWhiteSpace(Path))
            return;

        try
        {
            var headSha = await _gitBackend.GetHeadShaAsync();
            await _sourceArchiveService.ArchiveRefAsync("HEAD", "HEAD", headSha);
        }
        catch (Exception ex)
        {
            _windowService.Error($"Archive failed:\n{ex.Message}", "Gitster");
        }
    }

    private bool CanArchiveHead() => IsGoButtonEnabled;

    [RelayCommand]
    private void SwitchBranch() { }

    /// <summary>Opens the Visual-Studio-style commit panel (status-bar text, Repository menu, Ctrl+K).</summary>
    [RelayCommand]
    private async Task OpenCommitPanel()
    {
        if (string.IsNullOrWhiteSpace(Path)) return;

        // Clicking the status text / Ctrl+K toggles the flyout.
        if (CommitPanelVM.IsOpen)
        {
            CommitPanelVM.IsOpen = false;
            return;
        }
        try
        {
            var head = await _gitBackend.GetHeadShaAsync();
            var details = await _gitBackend.GetCommitAsync(head);
            CommitPanelVM.SetLastCommitMessage(details.Message);
        }
        catch
        {
            CommitPanelVM.SetLastCommitMessage(null);
        }
        await CommitPanelVM.OpenAsync();
    }

    [RelayCommand]
    private void OpenOperationsLog()
    {
        var window = new OperationsLogWindow(_opsLogService, TitleBarVM.RepositoryName);
        _windowService.ShowDialog(window);
    }

    [RelayCommand]
    private void OpenRepoSettings()
    {
        if (string.IsNullOrWhiteSpace(Path)) return;
        var vm = new RepositorySettingsViewModel(Path);
        var window = new RepositorySettingsWindow(vm);
        _windowService.ShowDialog(window);
    }

    [RelayCommand]
    private void OpenAuthorRepair()
    {
        var vm = new AuthorRepairViewModel(_gitBackend, _historyService, _authorDirService.Authors, _windowService);
        var window = new Views.AuthorRepairDialog(vm);
        _windowService.ShowDialog(window);
    }

    [RelayCommand]
    private async Task OpenRewriteTimestamps()
    {
        try
        {
            var beforeSha = await _gitBackend.GetHeadShaAsync();
            var vm = new RangeTimestampViewModel(_gitBackend, _historyService);
            var window = new Views.RangeTimestampDialog(vm);

            if (_windowService.ShowDialog(window) == true)
            {
                var afterSha = await _gitBackend.GetHeadShaAsync();
                var branchName = TitleBarVM.CurrentBranch;
                var n = vm.Preview.Count;
                var shortBefore = beforeSha.Length >= 7 ? beforeSha[..7] : beforeSha;
                var shortAfter  = afterSha.Length  >= 7 ? afterSha[..7]  : afterSha;

                await _opsLogService.RecordAsync(new OperationRecord(
                    Id: Guid.NewGuid().ToString(),
                    Timestamp: DateTimeOffset.Now,
                    Kind: OperationKind.RangeRewrite,
                    Description: $"Rewrite timestamps ({n} commit{(n == 1 ? "" : "s")})",
                    BranchName: branchName,
                    BeforeSha: shortBefore,
                    AfterSha: shortAfter,
                    ReflogSelector: null,
                    Status: OperationStatus.Active));

                _ = _snapshotService.CaptureAsync(_gitBackend, "Range timestamp rewrite");
            }
        }
        catch (Exception ex)
        {
            _windowService.Error($"Error rewriting timestamps: {ex.Message}", "Gitster");
        }
    }

    // ── Custom tools (Phase 3, Step E) ────────────────────────────────────

    /// <summary>All custom tools (repo-scoped first, then global) for the Tools menu.</summary>
    public IReadOnlyList<Gitster.Models.CustomTool> GetCustomTools()
    {
        try { return _customToolsService.GetTools(); }
        catch { return []; }
    }

    [RelayCommand]
    private void ManageTools()
    {
        var vm = new ManageToolsViewModel(_customToolsService, _windowService);
        var window = new Views.ManageToolsDialog(vm);
        _windowService.ShowDialog(window);
    }

    public async Task RunCustomToolAsync(Gitster.Models.CustomTool tool)
    {
        // Resolve a selected commit if the tool needs one.
        string? revision = null;
        if (tool.NeedsCommit)
        {
            var selected = CommitListVM.SelectedCommit;
            if (selected is null || string.IsNullOrEmpty(selected.FullSha))
            {
                _windowService.Info("Select a commit first — this tool needs one.", tool.Name);
                return;
            }
            revision = selected.FullSha;
        }

        // Ask for $ARGS if the tool prompts.
        string? args = null;
        if (!string.IsNullOrEmpty(tool.Prompt))
        {
            var input = new Views.TextInputDialog
            {
                Title  = tool.Name,
                Prompt = tool.Prompt!,
            };
            if (_windowService.ShowDialog(input) != true) return;
            args = input.Value;
        }

        var command = _customToolsService.Substitute(tool.Command, revision, args, TitleBarVM.CurrentBranch);

        // Confirm, showing the exact command that will run.
        if (!string.IsNullOrEmpty(tool.Confirm))
        {
            var prompt = $"{tool.Confirm}\n\nCommand:\n{command}";
            if (!_windowService.Confirm(prompt, tool.Name))
                return;
        }

        // Snapshot before running ANY tool — Gitster can't know what a tool does.
        _ = _snapshotService.CaptureAsync(_gitBackend, $"Before tool: {tool.Name}");

        try
        {
            var result = await _feedbackService.RunAsync(tool.Name,
                () => _customToolsService.RunAsync(command),
                r => r.Success ? "completed" : $"exit {r.ExitCode}");

            var dialog = new Views.ToolResultDialog(tool.Name, result.ExitCode, result.Output);
            _windowService.ShowDialog(dialog);

            // A tool may have changed the repository — refresh.
            await UpdateElementsAsync();
        }
        catch (Exception ex)
        {
            _windowService.Error($"Tool '{tool.Name}' failed:\n{ex.Message}", "Gitster");
        }
    }

    [RelayCommand]
    private void OpenDocs() { }

    [RelayCommand]
    private void OpenShortcuts() { }

    [RelayCommand]
    private void OpenAbout() { }

    [RelayCommand]
    private async Task AmendCommit()
    {
        try
        {
            var editDate = TimestampEditVM.SelectedDate;
            if (editDate == null)
            {
                _windowService.Warning("Please select a date", "Gitster");
                return;
            }

            var beforeSha = await _gitBackend.GetHeadShaAsync();
            var branchName = TitleBarVM.CurrentBranch;
            var selected = SelectedCommit;
            if (selected is null)
            {
                _windowService.Warning("Please select a commit to amend.", "Gitster");
                return;
            }

            if (selected.RemoteState == Gitster.Services.Git.CommitRemoteState.Incoming)
            {
                _windowService.Warning("Incoming commits are not on the local branch yet. Pull or cherry-pick the commit before amending it.", "Gitster");
                return;
            }

            // Collect author/committer changes from AuthorPanelVM
            var (authorName, authorEmail) = AuthorPanelVM.GetPendingAuthor();
            var (committerName, committerEmail) = AuthorPanelVM.GetPendingCommitter();

            var isHead = string.Equals(selected.FullSha, beforeSha, StringComparison.OrdinalIgnoreCase);
            var afterSha = await _feedbackService.RunAsync(
                isHead ? "Amend" : "Amend selected commit",
                async () =>
                {
                    if (isHead)
                    {
                        return await Task.Run(() => _gitBackend.AmendAsync(new AmendRequest(
                            editDate.Value,
                            AuthorName: authorName,
                            AuthorEmail: authorEmail,
                            CommitterName: committerName,
                            CommitterEmail: committerEmail)));
                    }

                    var rewriteDate = BuildRewriteDate(editDate.Value, selected.Date);
                    var rewrite = new CommitRewrite(
                        selected.FullSha,
                        NewAuthorName: authorName,
                        NewAuthorEmail: authorEmail,
                        NewCommitterName: committerName,
                        NewCommitterEmail: committerEmail,
                        NewAuthorDate: rewriteDate,
                        NewCommitterDate: rewriteDate);

                    await Task.Run(() => _gitBackend.RewriteCommitsAsync([rewrite], branchName));
                    return await _gitBackend.GetHeadShaAsync();
                },
                sha => sha.Length > 7 ? sha[..7] : sha);

            var reflogSelector = await _gitBackend.GetReflogSelectorForHeadAsync();

            var shortBefore = beforeSha.Length >= 7 ? beforeSha[..7] : beforeSha;
            var shortAfter = afterSha.Length >= 7 ? afterSha[..7] : afterSha;

            await _opsLogService.RecordAsync(new OperationRecord(
                Id: Guid.NewGuid().ToString(),
                Timestamp: DateTimeOffset.Now,
                Kind: OperationKind.Amend,
                Description: isHead
                    ? $"Amend {shortAfter}"
                    : $"Amend selected {(selected.CommitId.Length > 0 ? selected.CommitId : selected.FullSha[..Math.Min(7, selected.FullSha.Length)])}",
                BranchName: branchName,
                BeforeSha: shortBefore,
                AfterSha: shortAfter,
                ReflogSelector: reflogSelector,
                Status: OperationStatus.Active));

            _ = _snapshotService.CaptureAsync(_gitBackend, "Amend");

            ClearPendingHeadRefresh();
            await RefreshAfterHeadChangeAsync();
        }
        catch (Exception ex)
        {
            _windowService.Error($"Error amending commit: {ex.Message}", "Gitster");
        }
    }

    private static DateTimeOffset BuildRewriteDate(DateTime newDate, DateTime originalDate)
    {
        var offset = DateTimeOffset.Now.Offset;
        return new DateTimeOffset(
            newDate.Year,
            newDate.Month,
            newDate.Day,
            newDate.Hour,
            newDate.Minute,
            originalDate.Second,
            offset);
    }

    [RelayCommand]
    private async Task ResetToCommitMixed(CommitItem? commit)
        => await ResetToCommitAsync(commit, hard: false);

    [RelayCommand]
    private async Task ResetToCommitHard(CommitItem? commit)
        => await ResetToCommitAsync(commit, hard: true);

    private async Task ResetToCommitAsync(CommitItem? commit, bool hard)
    {
        if (commit is null)
            return;

        var shortTarget = ShortSha(commit.FullSha);
        var mode = hard ? "--hard" : "--mixed";
        var action = hard ? "Reset and delete changes" : "Reset and keep changes";
        var confirmText = hard
            ? $"Reset the current branch to {shortTarget}?\n\n" +
              "This runs git reset --hard. It moves the branch to the selected commit and deletes uncommitted working-tree and index changes.\n\n" +
              "Commits after the selected commit will no longer be on this branch."
            : $"Reset the current branch to {shortTarget}?\n\n" +
              "This runs git reset --mixed. It moves the branch to the selected commit, resets the index, and keeps resulting file changes in the working tree.\n\n" +
              "Commits after the selected commit will no longer be on this branch.";

        if (!_windowService.Confirm(confirmText, action))
            return;

        try
        {
            var beforeSha = await _gitBackend.GetHeadShaAsync();
            var resetBranchName = TitleBarVM.CurrentBranch;
            await _snapshotService.CaptureAsync(_gitBackend, $"Before reset {mode} to {shortTarget}");

            var afterSha = await _feedbackService.RunAsync(
                hard ? "Reset --hard" : "Reset --mixed",
                async () =>
                {
                    if (hard)
                        await Task.Run(() => _gitBackend.ResetHardAsync(commit.FullSha, resetBranchName));
                    else
                        await Task.Run(() => _gitBackend.ResetMixedAsync(commit.FullSha, resetBranchName));

                    return await _gitBackend.GetHeadShaAsync();
                },
                ShortSha);

            var reflogSelector = await TryGetHeadReflogSelectorAsync();
            var branchName = TitleBarVM.CurrentBranch;

            await _opsLogService.RecordAsync(new OperationRecord(
                Id: Guid.NewGuid().ToString(),
                Timestamp: DateTimeOffset.Now,
                Kind: OperationKind.Reset,
                Description: $"Reset {mode} to {shortTarget}",
                BranchName: branchName,
                BeforeSha: ShortSha(beforeSha),
                AfterSha: ShortSha(afterSha),
                ReflogSelector: reflogSelector,
                Status: OperationStatus.Active));

            ClearPendingHeadRefresh();
            await RefreshAfterHeadChangeAsync();
        }
        catch (Exception ex)
        {
            _windowService.Error($"Reset failed: {ex.Message}", "Gitster");
        }
    }

    private async Task<string?> TryGetHeadReflogSelectorAsync()
    {
        try
        {
            return await _gitBackend.GetReflogSelectorForHeadAsync();
        }
        catch
        {
            return null;
        }
    }

    private static string ShortSha(string sha) =>
        sha.Length >= 7 ? sha[..7] : sha;

    [RelayCommand]
    private void ReadSelectedCommitTime()
    {
        if (SelectedCommit == null)
        {
            return;
        }

        try
        {
            SelectedDate = SelectedCommit.Date;
        }
        catch (Exception ex)
        {
            _windowService.Error($"Error reading selected commit: {ex.Message}", "Gitster");
        }
    }

    [RelayCommand]
    private void ReadCurrentCommitTime()
    {
        try
        {
            using var repo = new Repository(Path);
            var commit = repo.Head.Tip;
            if (commit == null) return;

            SelectedDate = commit.Author.When.DateTime;
        }
        catch (Exception ex)
        {
            _windowService.Error($"Error reading current commit: {ex.Message}", "Gitster");
        }
    }

    [RelayCommand]
    private async Task Fetch(string? remoteName)
    {
        try
        {
            var selectedRemote = string.IsNullOrWhiteSpace(remoteName) ? "origin" : remoteName;
            await _feedbackService.RunAsync("Fetch", () => _gitBackend.FetchAsync(selectedRemote));
            await UpdateElementsAsync();
        }
        catch (Exception ex)
        {
            _windowService.Error($"Error fetching: {ex.Message}", "Gitster");
        }
    }

    [RelayCommand]
    private async Task Pull(string? remoteName)
    {
        try
        {
            var selectedRemote = string.IsNullOrWhiteSpace(remoteName) ? "origin" : remoteName;
            await _feedbackService.RunAsync("Pull", () => _gitBackend.PullAsync(selectedRemote));
            await UpdateElementsAsync();
        }
        catch (Exception ex)
        {
            _windowService.Error($"Error pulling: {ex.Message}", "Gitster");
        }
    }

    private string ActiveRemote(string? remoteName) =>
        !string.IsNullOrWhiteSpace(remoteName) ? remoteName
        : !string.IsNullOrWhiteSpace(SelectedRemote) ? SelectedRemote
        : Remotes.FirstOrDefault() ?? "origin";

    [RelayCommand]
    private Task Push(string? remoteName) => PushWithModeAsync(remoteName, PushMode.Normal);

    [RelayCommand]
    private Task PushForceWithLease(string? remoteName) => PushWithModeAsync(remoteName, PushMode.ForceWithLease);

    [RelayCommand]
    private Task PushForce(string? remoteName)
    {
        var confirm = _windowService.Confirm(
            "Force push (--force) overwrites the remote branch and can destroy commits " +
            "other people rely on.\n\nPrefer \"Push (force-with-lease)\" unless you are certain.\n\nForce push anyway?",
            "Dangerous: force push");
        return confirm ? PushWithModeAsync(remoteName, PushMode.Force) : Task.CompletedTask;
    }

    private async Task PushWithModeAsync(string? remoteName, PushMode mode)
    {
        try
        {
            var remote = ActiveRemote(remoteName);
            var verb = mode == PushMode.Normal ? "Push" : mode == PushMode.ForceWithLease ? "Push (lease)" : "Force push";
            await _feedbackService.RunAsync(verb, () => _gitBackend.PushAsync(remote, mode));
            await UpdateElementsAsync();
        }
        catch (Exception ex)
        {
            _windowService.Error($"Error pushing: {ex.Message}", "Gitster");
        }
    }

    [RelayCommand]
    private async Task Sync(string? remoteName)
    {
        try
        {
            var remote = ActiveRemote(remoteName);
            await _feedbackService.RunAsync("Sync", async () =>
            {
                await _gitBackend.FetchAsync(remote);
                await _gitBackend.PullAsync(remote);
                await _gitBackend.PushAsync(remote);
            });

            await UpdateElementsAsync();
        }
        catch (Exception ex)
        {
            _windowService.Error($"Error syncing: {ex.Message}", "Gitster");
        }
    }

    public async Task OnWindowActivatedAsync()
    {
        if (_isSwitchingRepository || !_initialRepositoryLoadStarted)
            return;

        var changes = await _stateService.GetActivationChangesAsync();
        if (changes == RepositoryActivationChange.None)
            return;

        if (changes.HasFlag(RepositoryActivationChange.GitMetadata))
            await RefreshRepositoryAfterActivationWithDialogAsync();
        else
            await RefreshStateAfterActivationWithDialogAsync();
    }

    private void UpdateSettingsPath()
    {
        var settings = Properties.Settings.Default;
        settings.Path = Path;
        settings.Save();
    }

    private async Task RefreshRepositoryAfterActivationWithDialogAsync()
    {
        if (string.IsNullOrWhiteSpace(Path))
            return;

        ClearPendingHeadRefresh();

        using var cts = new CancellationTokenSource();
        var loadingVm = new RepositoryLoadingViewModel(Path, cts, "Refreshing repository");
        var loadingWindow = new RepositoryLoadingWindow(loadingVm);
        var progress = new Progress<RepositoryLoadProgress>(loadingVm.Report);

        Task? refreshTask = null;
        loadingWindow.ContentRendered += async (_, _) =>
        {
            if (refreshTask is not null)
                return;

            await loadingWindow.Dispatcher.InvokeAsync(
                () => { },
                DispatcherPriority.ApplicationIdle);

            refreshTask = RefreshRepositoryAfterActivationCoreAsync(cts.Token, progress);
            _ = refreshTask.ContinueWith(task =>
            {
                Application.Current.Dispatcher.BeginInvoke(() =>
                    loadingWindow.Complete(task.Status == TaskStatus.RanToCompletion));
            }, CancellationToken.None);
        };

        var result = _windowService.ShowDialog(loadingWindow);
        if (result != true)
            cts.Cancel();

        if (refreshTask is null)
            return;

        try
        {
            await refreshTask;
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task RefreshRepositoryAfterActivationCoreAsync(
        CancellationToken ct,
        IProgress<RepositoryLoadProgress> progress)
    {
        await _headRefreshGate.WaitAsync(ct);
        try
        {
            await UpdateElementsAsync(ct, progress);
        }
        finally
        {
            _headRefreshGate.Release();
        }
    }

    private async Task RefreshStateAfterActivationWithDialogAsync()
    {
        var viewModel = new OperationProgressViewModel("Refreshing repository");
        var window = new OperationProgressWindow(viewModel);
        var progress = new Progress<OperationProgress>(viewModel.Report);

        Task? refreshTask = null;
        window.ContentRendered += async (_, _) =>
        {
            if (refreshTask is not null)
                return;

            await window.Dispatcher.InvokeAsync(
                () => { },
                DispatcherPriority.ApplicationIdle);

            refreshTask = _stateService.RefreshAsync(progress);
            _ = refreshTask.ContinueWith(task =>
            {
                Application.Current.Dispatcher.BeginInvoke(() =>
                    window.Complete(task.Status == TaskStatus.RanToCompletion));
            }, CancellationToken.None);
        };

        _windowService.ShowDialog(window);

        if (refreshTask is not null)
            await refreshTask;
    }

    public async Task UpdateElementsAsync(
        CancellationToken ct = default,
        IProgress<RepositoryLoadProgress>? progress = null)
    {
        try
        {
            await UpdateElementsCoreAsync(Path, ct, progress);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            ClearRepositoryUi();
        }

        await RefreshSidebarBadgesAsync();
    }

    private void QueueHeadRefresh()
    {
        if (_isSwitchingRepository)
            return;

        _headRefreshRequested = true;
        _headRefreshRequestVersion++;

        if (!_headRefreshWorkerRunning)
            _ = RunQueuedHeadRefreshAsync();
    }

    private void ClearPendingHeadRefresh()
    {
        _headRefreshRequested = false;
        _headRefreshRequestVersion++;
    }

    private async Task RunQueuedHeadRefreshAsync()
    {
        if (_headRefreshWorkerRunning)
            return;

        _headRefreshWorkerRunning = true;
        try
        {
            while (!_isSwitchingRepository)
            {
                if (!_headRefreshRequested)
                    return;

                var version = _headRefreshRequestVersion;
                await Task.Delay(75);

                if (_isSwitchingRepository || !_headRefreshRequested)
                    return;

                if (version != _headRefreshRequestVersion)
                    continue;

                _headRefreshRequested = false;
                await RefreshAfterHeadChangeAsync();
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _headRefreshWorkerRunning = false;

            if (_headRefreshRequested && !_isSwitchingRepository)
                _ = RunQueuedHeadRefreshAsync();
        }
    }

    private async Task RefreshAfterHeadChangeAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(Path))
            return;

        await _headRefreshGate.WaitAsync(ct);
        try
        {
            await _gitBackend.OpenAsync(Path);
            ct.ThrowIfCancellationRequested();

            using var repo = new Repository(Path);
            ApplyHeadState(repo);

            await CommitListVM.LoadAsync(ct);
            ct.ThrowIfCancellationRequested();

            UpdateStatusBar(repo);
            _ = _authorDirService.RefreshAsync();
            _ = RefreshHeadRelatedSidePanelsAsync();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            await UpdateElementsAsync(ct);
        }
        finally
        {
            _headRefreshGate.Release();
        }
    }

    private async Task RefreshHeadRelatedSidePanelsAsync()
    {
        try
        {
            await BranchesVM.LoadAsync();
            SidebarVM.BranchCount = BranchesVM.LocalCount;
            await WorktreesVM.LoadAsync();
            await RefreshSidebarBadgesAsync();
        }
        catch
        {
            // Background side-panel refresh should not block the commit list update.
        }
    }

    private async Task UpdateElementsCoreAsync(
        string repoPath,
        CancellationToken ct,
        IProgress<RepositoryLoadProgress>? progress)
    {
        progress?.Report(new RepositoryLoadProgress("Opening repository", repoPath));
        await _gitBackend.OpenAsync(repoPath);
        ct.ThrowIfCancellationRequested();

        progress?.Report(new RepositoryLoadProgress(
            "Validating history cache",
            "Preparing cached commit metadata."));
        await _historyService.OpenAsync(repoPath, ct, progress);
        ct.ThrowIfCancellationRequested();

        progress?.Report(new RepositoryLoadProgress(
            "Attaching repository services",
            "Preparing watchers, logs, snapshots, and repo-local settings."));
        await AttachRepositoryServicesAsync(repoPath, ct);
        ct.ThrowIfCancellationRequested();

        using var repo = new Repository(repoPath);

        progress?.Report(new RepositoryLoadProgress(
            "Reading HEAD",
            "Loading current commit and branch state."));

        if (!ApplyHeadState(repo))
            return;

        Remotes.Clear();
        foreach (var remote in repo.Network.Remotes)
            Remotes.Add(remote.Name);
        if (Remotes.Count > 0 && (string.IsNullOrEmpty(SelectedRemote) || !Remotes.Contains(SelectedRemote)))
            SelectedRemote = Remotes[0];

        UpdateStatusBar(repo);

        progress?.Report(new RepositoryLoadProgress("Loading stashes", "Reading stash list."));
        await StashesVM.LoadAsync();
        ct.ThrowIfCancellationRequested();

        progress?.Report(new RepositoryLoadProgress("Loading branches", "Reading local and remote branches."));
        await BranchesVM.LoadAsync();
        ct.ThrowIfCancellationRequested();

        progress?.Report(new RepositoryLoadProgress("Loading worktrees", "Reading linked worktrees."));
        await WorktreesVM.LoadAsync();
        ct.ThrowIfCancellationRequested();

        SidebarVM.BranchCount = BranchesVM.LocalCount;
        await RefreshSidebarBadgesAsync();
        ct.ThrowIfCancellationRequested();

        await CommitListVM.LoadAsync(ct, progress);
        ct.ThrowIfCancellationRequested();

        progress?.Report(new RepositoryLoadProgress("Finalizing", "Refreshing author index."));
        _ = _authorDirService.RefreshAsync();
    }

    private bool ApplyHeadState(Repository repo)
    {
        var headTip = repo.Head.Tip;
        if (headTip == null)
        {
            CurrentCommitDetail.Clear();
            SelectedCommitDetail.Clear();
            TimestampEditVM.UpdatePreviewBefore("-");
            IsGoButtonEnabled = false;
            CommitListVM.ClearList();
            UpdateStatusBar(repo);
            return false;
        }

        CurrentCommitDetail.UpdateCommit(
            headTip.MessageShort,
            headTip.Author.When.DateTime);

        var headSha = headTip.Id.Sha.Length >= 6 ? headTip.Id.Sha[..6] : headTip.Id.Sha;
        TimestampEditVM.UpdatePreviewBefore($"{headTip.Author.When.DateTime:dd.MM. HH:mm} · {headSha}");

        var previousCommit = headTip.Parents.FirstOrDefault();
        if (previousCommit != null)
            SelectedCommitDetail.UpdateCommit(previousCommit.MessageShort, previousCommit.Author.When.DateTime);
        else
            SelectedCommitDetail.Clear();

        IsGoButtonEnabled = true;
        return true;
    }

    private void ClearRepositoryUi()
    {
        StashesVM.Clear();
        BranchesVM.Clear();
        WorktreesVM.Clear();
        CurrentCommitDetail.Clear();
        SelectedCommitDetail.Clear();
        SelectedDate = null;
        TimestampEditVM.UpdatePreviewBefore("-");
        IsGoButtonEnabled = false;
        Remotes.Clear();
        TitleBarVM.Clear();
        CommitListVM.ClearList();
        HistoryRewriteDraftVM.SetSelectedCommit(null);
        HistoryRewriteDraftVM.SetCommits([]);
    }

    private async Task RefreshSidebarBadgesAsync()
    {
        try
        {
            SidebarVM.StashCount = await _gitBackend.GetStashCountAsync();
        }
        catch
        {
            SidebarVM.StashCount = 0;
        }
        SidebarVM.ActiveOpsCount = _opsLogService.Records.Count(r => r.Status == OperationStatus.Active);
    }

    /// <summary>
    /// Updates the status bar with current repository information including branch name,
    /// repository name, and incoming/outgoing commit counts.
    /// </summary>
    /// <param name="repo">The Git repository to get status information from.</param>
    private void UpdateStatusBar(Repository repo)
    {
        try
        {
            string branch;
            if (repo.Info.IsHeadDetached)
            {
                var sha = repo.Head.Tip?.Sha;
                branch = sha != null
                    ? $"detached @ {sha[..Math.Min(7, sha.Length)]}"
                    : "(no branch)";
            }
            else
            {
                branch = repo.Head.FriendlyName;
            }

            // Get repository name from path
            var repoPath = repo.Info.WorkingDirectory.TrimEnd(System.IO.Path.DirectorySeparatorChar);
            var repoName = System.IO.Path.GetFileName(repoPath);
            TitleBarVM.CurrentRepositoryPath = repoPath;

            // Calculate incoming and outgoing commits
            int incoming = 0;
            int outgoing = 0;

            var trackedBranch = repo.Head.TrackedBranch;
            if (trackedBranch != null)
            {
                var localCommit = repo.Head.Tip;
                var remoteCommit = trackedBranch.Tip;

                if (localCommit != null && remoteCommit != null)
                {
                    // Use HistoryDivergence for better performance
                    var divergence = repo.ObjectDatabase.CalculateHistoryDivergence(localCommit, remoteCommit);

                    outgoing = divergence.AheadBy ?? 0;
                    incoming = divergence.BehindBy ?? 0;
                }
            }

            TitleBarVM.UpdateStatus(branch, repoName, incoming, outgoing);
        }
        catch (Exception)
        {
            TitleBarVM.Clear();
        }
    }

    private async Task AttachRepositoryServicesAsync(string repoPath, CancellationToken ct)
    {
        await _gitBackend.OpenAsync(repoPath);
        ct.ThrowIfCancellationRequested();
        await _stateService.AttachAsync(repoPath);
        ct.ThrowIfCancellationRequested();
        await _opsLogService.AttachAsync(repoPath);
        ct.ThrowIfCancellationRequested();
        await _snapshotService.AttachAsync(repoPath);
        ct.ThrowIfCancellationRequested();
        await _stashNameService.AttachAsync(repoPath);
        ct.ThrowIfCancellationRequested();
        _customToolsService.Attach(repoPath);
    }

    private void OnRepositoryStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        // The commit panel mirrors working-tree changes live while it is open (A2).
        if (e.PropertyName == nameof(RepositoryStateService.WorkingTreeState) && CommitPanelVM.IsOpen)
            _ = CommitPanelVM.LoadAsync();

        if (e.PropertyName == nameof(RepositoryStateService.GitMetadataVersion))
        {
            QueueHeadRefresh();
            return;
        }

        if (e.PropertyName != nameof(RepositoryStateService.CurrentBranch))
            return;

        if (string.IsNullOrWhiteSpace(_stateService.CurrentBranch))
            return;

        TitleBarVM.CurrentBranch = _stateService.CurrentBranch;
    }
}
