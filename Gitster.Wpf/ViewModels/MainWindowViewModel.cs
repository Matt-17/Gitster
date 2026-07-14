using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Gitster.Services;
using Gitster.ApplicationLayer;
using Gitster.ApplicationLayer.Capabilities;
using Gitster.Services.Features;
using Gitster.ApplicationLayer.Features;
using Gitster.Core.Git;
using Gitster.Core.History;
using Gitster.Services.OperationsLog;
using Gitster.Views;
using Gitster.Views.Helper;

using Gitster.Core.Models;

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
    private readonly CustomToolRunner _customToolRunner;
    private readonly GitIgnoreTemplateService _gitIgnoreTemplates;
    private readonly UpdateCheckService _updateCheckService;
    private readonly UiPreferencesService _uiPreferences;
    private readonly AppSettingsService _appSettings;
    private readonly ISelectionContext _selectionContext;
    private readonly RepositoryCommandContext _repoCommands;
    private readonly CommitSelectionCoordinator _selectionCoordinator;
    private readonly CapabilityService _capabilityService;
    private readonly HeadRefreshCoordinator _headRefresh;
    private readonly RepositoryLifecycleCoordinator _repositoryLifecycle;
    private bool _suppressFolderPathChanged;
    private readonly HashSet<string> _taggedCommitShas = new(StringComparer.OrdinalIgnoreCase);

    public TitleBarViewModel TitleBarVM { get; }
    public CommitListViewModel CommitListVM { get; }
    public CommitRefNavigatorViewModel CommitRefNavigatorVM { get; }
    public TimestampEditViewModel TimestampEditVM { get; }
    public HistoryRewriteDraftViewModel HistoryRewriteDraftVM { get; }
    public QuickActionsViewModel QuickActionsVM { get; }
    public UndoBarViewModel UndoBarVM { get; }
    public StatusBarViewModel StatusBarVM { get; }
    public AutoFetchService AutoFetch { get; }
    public AuthorPanelViewModel AuthorPanelVM { get; }
    public SidebarViewModel SidebarVM { get; }
    public StashesViewModel StashesVM { get; }
    public BranchesViewModel BranchesVM { get; }
    public WorktreesViewModel WorktreesVM { get; }
    public CommitPanelViewModel CommitPanelVM { get; }
    public SearchViewModel SearchVM { get; }
    public OperationsLogService OpsLogService => _opsLogService;
    public UiPreferencesService Ui => _uiPreferences;

    public MainWindowViewModel(
        MainWindowServices services,
        MainWindowChildViewModels childViewModels,
        MainWindowCoordinators coordinators)
        : this(
            services.WindowService,
            services.GitBackend,
            services.StateService,
            services.FeedbackService,
            services.RecentRepos,
            services.AuthorDirectoryService,
            services.HistoryService,
            services.AutoFetch,
            services.CapabilityService,
            services.OpsLogService,
            services.SnapshotService,
            services.SourceArchiveService,
            services.StashNameService,
            services.CustomToolsService,
            services.CustomToolRunner,
            services.GitIgnoreTemplates,
            services.UpdateCheckService,
            coordinators.HeadRefresh,
            coordinators.RepositoryLifecycle,
            services.UiPreferences,
            services.AppSettings,
            services.SelectionContext,
            coordinators.RepositoryCommands,
            coordinators.SelectionCoordinator,
            childViewModels.StatusBar,
            childViewModels.TitleBar,
            childViewModels.CommitList,
            childViewModels.CommitRefNavigator,
            childViewModels.TimestampEdit,
            childViewModels.HistoryRewriteDraft,
            childViewModels.QuickActions,
            childViewModels.UndoBar,
            childViewModels.AuthorPanel,
            childViewModels.Sidebar,
            childViewModels.Stashes,
            childViewModels.Branches,
            childViewModels.Worktrees,
            childViewModels.CommitPanel,
            childViewModels.Search)
    {
    }

    private MainWindowViewModel(
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
        CustomToolRunner customToolRunner,
        GitIgnoreTemplateService gitIgnoreTemplates,
        UpdateCheckService updateCheckService,
        HeadRefreshCoordinator headRefresh,
        RepositoryLifecycleCoordinator repositoryLifecycle,
        UiPreferencesService uiPreferences,
        AppSettingsService appSettings,
        ISelectionContext selectionContext,
        RepositoryCommandContext repoCommands,
        CommitSelectionCoordinator selectionCoordinator,
        StatusBarViewModel statusBarViewModel,
        TitleBarViewModel titleBarViewModel,
        CommitListViewModel commitListViewModel,
        CommitRefNavigatorViewModel commitRefNavigatorViewModel,
        TimestampEditViewModel timestampEditViewModel,
        HistoryRewriteDraftViewModel historyRewriteDraftViewModel,
        QuickActionsViewModel quickActionsViewModel,
        UndoBarViewModel undoBarViewModel,
        AuthorPanelViewModel authorPanelViewModel,
        SidebarViewModel sidebarViewModel,
        StashesViewModel stashesViewModel,
        BranchesViewModel branchesViewModel,
        WorktreesViewModel worktreesViewModel,
        CommitPanelViewModel commitPanelViewModel,
        SearchViewModel searchViewModel)
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
        _customToolRunner = customToolRunner;
        _gitIgnoreTemplates = gitIgnoreTemplates;
        _updateCheckService = updateCheckService;
        _headRefresh = headRefresh;
        _repositoryLifecycle = repositoryLifecycle;
        _uiPreferences = uiPreferences;
        _appSettings = appSettings;
        _selectionContext = selectionContext;
        _repoCommands = repoCommands;
        _selectionCoordinator = selectionCoordinator;
        _capabilityService = capabilityService;
        ConfigureRepositoryCommandContext();
        _headRefresh.Configure(RefreshAfterHeadChangeCoreAsync);
        PersistedGridSplitter.Initialize(_uiPreferences);

        Capability.Initialize(capabilityService);
        capabilityService.PropertyChanged += OnCapabilityServicePropertyChanged;

        SelectedCommitDetail = new CommitDetailViewModel();
        CurrentCommitDetail = new CommitDetailViewModel();
        StatusBarVM = statusBarViewModel;
        TitleBarVM = titleBarViewModel;
        TitleBarVM.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(TitleBarViewModel.RepositoryName) or nameof(TitleBarViewModel.CurrentBranch))
                OnPropertyChanged(nameof(WindowTitle));
        };
        CommitListVM = commitListViewModel;
        CommitListVM.PropertyChanged += OnCommitListPropertyChanged;
        CommitListVM.RemoveChangeFromCommitAsync = RemoveChangeFromCommitAsync;
        CommitListVM.FixupDroppedCommitAsync = FixupDroppedCommitAsync;
        CommitRefNavigatorVM = commitRefNavigatorViewModel;
        CommitRefNavigatorVM.SelectRefAsync = async item => await CommitListVM.ShowRefAsync(item.CanonicalName, item.DisplayName);
        CommitRefNavigatorVM.SelectCurrentBranchAsync = async () => await CommitListVM.ShowScopeAsync(HistoryScope.CurrentBranch);
        CommitRefNavigatorVM.SelectAllBranchesAsync = async () => await CommitListVM.ShowScopeAsync(HistoryScope.AllBranches);
        TimestampEditVM = timestampEditViewModel;
        QuickActionsVM = quickActionsViewModel;
        QuickActionsVM.RefreshAfterActionAsync = async () => await UpdateElementsAsync();
        HistoryRewriteDraftVM = historyRewriteDraftViewModel;
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
        SidebarVM = sidebarViewModel;
        StashesVM = stashesViewModel;
        BranchesVM = branchesViewModel;
        WorktreesVM = worktreesViewModel;
        CommitPanelVM = commitPanelViewModel;
        SearchVM = searchViewModel;
        _selectionCoordinator.SelectedCommitChanged += commit => SelectedCommit = commit;
        _selectionCoordinator.SelectedCommitsChanged += CompareSelectedCommitsCommand.NotifyCanExecuteChanged;
        _selectionCoordinator.Attach(CommitListVM);

        // Update ops log badge whenever the log changes
        _opsLogService.Changed += (_, _) =>
            SidebarVM.ActiveOpsCount = _opsLogService.Records.Count(r => r.Status == OperationStatus.Active);

        // A.3 — refresh commit list after any backend HEAD mutation (e.g. Undo via ResetHard)
        _gitBackend.HeadChanged += (_, _) =>
            Application.Current.Dispatcher.BeginInvoke(() =>
            {
                if (!_repositoryLifecycle.IsSwitchingRepository)
                    QueueHeadRefresh();
            });

        // Load saved path or use default
        _suppressFolderPathChanged = true;
        Path = _appSettings.LoadRepositoryPath(Properties.Settings.Default.Path) ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        FolderPath = Path;
        _suppressFolderPathChanged = false;

        _stateService.PropertyChanged += OnRepositoryStateChanged;
    }

    private void ConfigureRepositoryCommandContext()
    {
        _repoCommands.BrowseFolderAsync = BrowseFolderAsync;
        _repoCommands.OpenRepository = OpenRepoByPath;
        _repoCommands.RefreshAllAsync = async () => await UpdateElementsAsync();
        _repoCommands.RefreshSidebarBadgesAsync = async () => await RefreshSidebarBadgesAsync();
        _repoCommands.GetCurrentBranch = () => TitleBarVM.CurrentBranch;
        _repoCommands.GetCurrentPath = () => Path;
        _repoCommands.GetSelectedRemote = () => string.IsNullOrEmpty(SelectedRemote)
            ? Remotes.FirstOrDefault()
            : SelectedRemote;
        _repoCommands.RefreshAfterHistoryRewriteAsync = async preferredSelectionSha =>
        {
            ClearPendingHeadRefresh();
            await RefreshAfterHeadChangeAsync();
            CommitListVM.SelectCommitBySha(preferredSelectionSha);
        };
    }

    [ObservableProperty]
    public partial string Path { get; set; } = string.Empty;

    public string WindowTitle =>
        string.IsNullOrWhiteSpace(TitleBarVM.RepositoryName)
            ? "Gitster"
            : $"{TitleBarVM.RepositoryName}";

    public ObservableCollection<RecentRepoEntry> RecentRepos => _recentRepos.Entries;

    [ObservableProperty]
    public partial string FolderPath { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsRepositoryLoading { get; set; }

    public bool ShowEmptyCommitList =>
        !IsRepositoryLoading
        && !string.IsNullOrWhiteSpace(_repositoryLifecycle.LoadedRepositoryPath)
        && CommitListVM.IsEmpty;

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
    public bool IsAmendUnsafe => SelectedCommit?.RemoteState == Gitster.Core.Git.CommitRemoteState.OnRemote;

    public bool CanAmendSelectedCommit =>
        IsGoButtonEnabled
        && SelectedCommit is not null
        && SelectedCommit.RemoteState != Gitster.Core.Git.CommitRemoteState.Incoming;

    partial void OnIsGoButtonEnabledChanged(bool value)
    {
        OnPropertyChanged(nameof(CanAmendSelectedCommit));
        ArchiveHeadCommand.NotifyCanExecuteChanged();
        PushThroughCommitCommand.NotifyCanExecuteChanged();
        NotifyCommitContextCommands();
    }

    partial void OnFolderPathChanged(string value)
    {
        if (_suppressFolderPathChanged)
            return;

        if (!string.Equals(value, Path, StringComparison.OrdinalIgnoreCase))
            _ = SwitchRepositoryAsync(value, recordRecent: false, showLoadingWindow: true);
    }

    partial void OnIsRepositoryLoadingChanged(bool value) =>
        OnPropertyChanged(nameof(ShowEmptyCommitList));

    private void OnCommitListPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(CommitListViewModel.IsEmpty))
            OnPropertyChanged(nameof(ShowEmptyCommitList));
    }

    partial void OnSelectedCommitChanged(CommitItem? value)
    {
        if (value != null)
            SelectedCommitDetail.UpdateCommit(value.Message, value.Date);
        else
            SelectedCommitDetail.Clear();

        OnPropertyChanged(nameof(IsAmendUnsafe));
        OnPropertyChanged(nameof(CanAmendSelectedCommit));
        PushThroughCommitCommand.NotifyCanExecuteChanged();
        NotifyCommitContextCommands();
    }

    private void NotifyCommitContextCommands()
    {
        CheckoutCommitDetachedCommand.NotifyCanExecuteChanged();
        ViewCommitDetailsCommand.NotifyCanExecuteChanged();
        OpenCommitInBrowserCommand.NotifyCanExecuteChanged();
        CompareSelectedCommitsCommand.NotifyCanExecuteChanged();
        NewBranchFromCommitCommand.NotifyCanExecuteChanged();
        NewTagFromCommitCommand.NotifyCanExecuteChanged();
        PushTagCommand.NotifyCanExecuteChanged();
        RevertCommitCommand.NotifyCanExecuteChanged();
        CherryPickCommitCommand.NotifyCanExecuteChanged();
        ForceRemoteToCommitCommand.NotifyCanExecuteChanged();
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
        => await _repositoryLifecycle.InitializeAsync(Path, SwitchRepositoryAsync);

    private async Task<bool> SwitchRepositoryAsync(string targetPath, bool recordRecent, bool showLoadingWindow)
    {
        if (showLoadingWindow)
            IsRepositoryLoading = true;

        try
        {
            return await _repositoryLifecycle.SwitchAsync(
                new RepositorySwitchRequest(targetPath, recordRecent, showLoadingWindow),
                new RepositorySwitchCallbacks(
                    CaptureRepositorySwitchState,
                    UpdateElementsCoreAsync,
                    CommitRepositoryPath,
                    RestoreRepositoryAfterCanceledSwitchAsync,
                    ex => _windowService.Error($"Error opening repository:\n{ex.Message}", "Gitster")));
        }
        finally
        {
            if (showLoadingWindow)
                IsRepositoryLoading = false;

            OnPropertyChanged(nameof(ShowEmptyCommitList));
        }
    }

    private RepositorySwitchState CaptureRepositorySwitchState() =>
        new(Path, FolderPath, _repositoryLifecycle.LoadedRepositoryPath);

    private void CommitRepositoryPath(string targetPath, bool recordRecent)
    {
        _suppressFolderPathChanged = true;
        Path = targetPath;
        FolderPath = targetPath;
        _suppressFolderPathChanged = false;

        UpdateSettingsPath();
        if (recordRecent)
            _recentRepos.Record(targetPath);
    }

    private async Task<string?> RestoreRepositoryAfterCanceledSwitchAsync(RepositorySwitchState previousState)
    {
        _suppressFolderPathChanged = true;
        Path = previousState.Path;
        FolderPath = previousState.FolderPath;
        _suppressFolderPathChanged = false;

        if (string.IsNullOrWhiteSpace(previousState.LoadedRepositoryPath))
        {
            ClearRepositoryUi();
            return null;
        }

        try
        {
            await UpdateElementsCoreAsync(previousState.LoadedRepositoryPath, CancellationToken.None, progress: null);
            return previousState.LoadedRepositoryPath;
        }
        catch
        {
            ClearRepositoryUi();
            // Keep the saved path even if the prior repository cannot be refreshed.
            return null;
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

    private void OnCapabilityServicePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CapabilityService.IsGitCliAvailable))
        {
            PushThroughCommitCommand.NotifyCanExecuteChanged();
            ForceRemoteToCommitCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanArchiveHead() => IsGoButtonEnabled;

    [RelayCommand]
    private void SwitchBranch() => SidebarVM.CurrentMode = AppMode.Branches;

    [RelayCommand]
    private void SetTheme(ThemePreference preference)
    {
        _uiPreferences.ThemePreference = preference;
    }

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
    private void OpenOptions()
    {
        if (BranchesVM.Favorites is not { } favorites) return;
        var window = new OptionsWindow(new OptionsViewModel(favorites, _uiPreferences));
        _windowService.ShowDialog(window);
    }

    [RelayCommand]
    private async Task OpenSnapshotBrowser()
    {
        if (string.IsNullOrWhiteSpace(Path))
            return;

        var snapshots = _snapshotService.LoadSnapshots();
        if (snapshots.Count == 0)
        {
            _windowService.Info("No Gitster snapshots have been captured for this repository yet.", "Snapshots");
            return;
        }

        var dialog = new SnapshotBrowserDialog(snapshots);
        if (_windowService.ShowDialog(dialog) != true || dialog.SelectedSnapshot is null)
            return;

        var choice = dialog.Choice;
        if (choice == SnapshotRestoreChoice.None)
            return;

        var confirmText = choice == SnapshotRestoreChoice.AllRefs
            ? "Restore all local branch and tag refs from this snapshot?"
            : $"Restore the current branch '{TitleBarVM.CurrentBranch}' from this snapshot?";

        if (!_windowService.Confirm(confirmText, "Restore snapshot"))
            return;

        try
        {
            if (choice == SnapshotRestoreChoice.AllRefs)
                await _snapshotService.RestoreAllRefsAsync(Path, dialog.SelectedSnapshot);
            else
                await _snapshotService.RestoreBranchAsync(Path, dialog.SelectedSnapshot, TitleBarVM.CurrentBranch);

            await UpdateElementsAsync();
            _windowService.Info("Snapshot restore completed.", "Snapshots");
        }
        catch (Exception ex)
        {
            _windowService.Warning(ex.Message, "Snapshots");
        }
    }

    [RelayCommand]
    private async Task AppendGitIgnoreTemplate()
    {
        if (string.IsNullOrWhiteSpace(Path))
            return;

        var dialog = new GitIgnoreTemplateDialog(_gitIgnoreTemplates);
        if (_windowService.ShowDialog(dialog) != true)
            return;

        try
        {
            _gitIgnoreTemplates.AppendTemplate(Path, dialog.SelectedTemplate);
            _windowService.Info($".gitignore updated with the {dialog.SelectedTemplate} template.", ".gitignore");
            await CommitPanelVM.LoadAsync();
        }
        catch (Exception ex)
        {
            _windowService.Warning(ex.Message, ".gitignore");
        }
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

                await _opsLogService.RecordAsync(new OperationRecord(
                    Id: Guid.NewGuid().ToString(),
                    Timestamp: DateTimeOffset.Now,
                    Kind: OperationKind.RangeRewrite,
                    Description: $"Rewrite timestamps ({n} commit{(n == 1 ? "" : "s")})",
                    BranchName: branchName,
                    BeforeSha: beforeSha,
                    AfterSha: afterSha,
                    ReflogSelector: null,
                    Status: OperationStatus.Active));

                await _snapshotService.CaptureAsync(_gitBackend, "Range timestamp rewrite");
            }
        }
        catch (Exception ex)
        {
            _windowService.Error($"Error rewriting timestamps: {ex.Message}", "Gitster");
        }
    }

    // ── Custom tools (Phase 3, Step E) ────────────────────────────────────

    /// <summary>All custom tools (repo-scoped first, then global) for the Tools menu.</summary>
    public IReadOnlyList<Gitster.Core.Models.CustomTool> GetCustomTools()
        => _customToolRunner.GetTools();

    [RelayCommand]
    private void ManageTools()
    {
        var vm = new ManageToolsViewModel(_customToolsService, _windowService);
        var window = new Views.ManageToolsDialog(vm);
        _windowService.ShowDialog(window);
    }

    public async Task RunCustomToolAsync(Gitster.Core.Models.CustomTool tool)
    {
        var outcome = await _customToolRunner.RunAsync(
            tool,
            new CustomToolRunContext(
                CommitListVM.SelectedCommit?.FullSha,
                TitleBarVM.CurrentBranch));

        if (outcome == CustomToolRunOutcome.RepositoryMayHaveChanged)
            await UpdateElementsAsync();
    }

    [RelayCommand]
    private void OpenShortcuts()
    {
        var text = string.Join(
            Environment.NewLine,
            ShortcutRegistry.All
                .GroupBy(s => s.Area)
                .SelectMany(group => new[] { group.Key }
                    .Concat(group.Select(s => $"  {s.Gesture,-20} {s.Command}"))));

        _windowService.Info(text, "Keyboard shortcuts");
    }

    [RelayCommand]
    private void OpenCommandPalette()
    {
        var dialog = new TextInputDialog
        {
            Title = "Command palette",
            Prompt = "Type a command: open, commit, fetch, pull, push, sync, search, shortcuts, settings",
        };

        if (_windowService.ShowDialog(dialog) != true)
            return;

        var query = dialog.Value.Trim();
        if (string.IsNullOrWhiteSpace(query))
            return;

        var item = BuildCommandPaletteItems()
            .OrderBy(i => ScoreCommand(i.Name, query))
            .FirstOrDefault(i => ScoreCommand(i.Name, query) < int.MaxValue);
        if (item is null)
        {
            _windowService.Warning($"No command matched '{query}'.", "Command palette");
            return;
        }

        item.Execute();
    }

    [RelayCommand]
    private void OpenAbout()
    {
        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        var versionText = version is null ? "unknown version" : $"Version {version.ToString(3)}";
        _windowService.Info(
            $"Gitster — surgical Git history and metadata operations.\n\n{versionText}",
            "About Gitster");
    }

    [RelayCommand]
    private async Task CheckForUpdates()
    {
        if (!_uiPreferences.UpdateChecksEnabled)
        {
            var enable = _windowService.ShowMessage(
                "Update checks are off. Enable opt-in GitHub release checks and check now?",
                "Check for updates",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (enable != MessageBoxResult.Yes)
                return;

            _uiPreferences.UpdateChecksEnabled = true;
        }

        try
        {
            var result = await _updateCheckService.CheckLatestReleaseAsync("mv", "Gitster");
            if (result.HasUpdate)
            {
                _windowService.Info(
                    $"A newer Gitster release is available: {result.LatestVersion}\n\n{result.ReleaseUrl}",
                    "Update available");
            }
            else
            {
                _windowService.Info("Gitster is up to date, or no release could be found.", "Check for updates");
            }
        }
        catch (Exception ex)
        {
            _windowService.Warning($"Could not check for updates:\n{ex.Message}", "Check for updates");
        }
    }

    private IReadOnlyList<CommandPaletteItem> BuildCommandPaletteItems() =>
    [
        new("open repository", () => BrowseFolderCommand.Execute(null)),
        new("commit panel", () => OpenCommitPanelCommand.Execute(null)),
        new("fetch", () => FetchCommand.Execute(null)),
        new("pull", () => PullCommand.Execute(null)),
        new("push", () => PushCommand.Execute(null)),
        new("sync", () => SyncCommand.Execute(null)),
        new("search", () => SidebarVM.SelectModeCommand.Execute(AppMode.Search)),
        new("keyboard shortcuts", () => OpenShortcutsCommand.Execute(null)),
        new("repository settings", () => OpenRepoSettingsCommand.Execute(null)),
        new("operations log", () => OpenOperationsLogCommand.Execute(null)),
    ];

    private static int ScoreCommand(string command, string query)
    {
        var normalizedCommand = command.Replace(" ", "", StringComparison.OrdinalIgnoreCase);
        var normalizedQuery = query.Replace(" ", "", StringComparison.OrdinalIgnoreCase);
        if (command.Contains(query, StringComparison.OrdinalIgnoreCase))
            return command.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        if (normalizedCommand.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase))
            return normalizedCommand.IndexOf(normalizedQuery, StringComparison.OrdinalIgnoreCase) + 10;

        var pos = 0;
        foreach (var c in normalizedQuery)
        {
            pos = normalizedCommand.IndexOf(c.ToString(), pos, StringComparison.OrdinalIgnoreCase);
            if (pos < 0)
                return int.MaxValue;
            pos++;
        }

        return 100 + normalizedCommand.Length - normalizedQuery.Length;
    }

    private sealed record CommandPaletteItem(string Name, Action Execute);

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

            if (selected.RemoteState == Gitster.Core.Git.CommitRemoteState.Incoming)
            {
                _windowService.Warning("Incoming commits are not on the local branch yet. Pull or cherry-pick the commit before amending it.", "Gitster");
                return;
            }

            if (selected.SigningStatus is CommitSigningStatus.Good or CommitSigningStatus.Bad or CommitSigningStatus.Untrusted)
            {
                var proceed = _windowService.ShowMessage(
                    "This commit has a signature badge. Rewriting it will remove or invalidate that signature.\n\nContinue with the amend?",
                    "Signed commit warning",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);
                if (proceed != MessageBoxResult.Yes)
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

                    var rewriteDate = RewriteDate.Build(editDate.Value, selected.Date);
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
                GitSha.Short);

            var reflogSelector = await _gitBackend.GetReflogSelectorForHeadAsync();

            await _opsLogService.RecordAsync(new OperationRecord(
                Id: Guid.NewGuid().ToString(),
                Timestamp: DateTimeOffset.Now,
                Kind: OperationKind.Amend,
                Description: isHead
                    ? $"Amend {GitSha.Short(afterSha)}"
                    : $"Amend selected {(selected.CommitId.Length > 0 ? selected.CommitId : GitSha.Short(selected.FullSha))}",
                BranchName: branchName,
                BeforeSha: beforeSha,
                AfterSha: afterSha,
                ReflogSelector: reflogSelector,
                Status: OperationStatus.Active));

            await _snapshotService.CaptureAsync(_gitBackend, "Amend");

            ClearPendingHeadRefresh();
            await RefreshAfterHeadChangeAsync();
        }
        catch (Exception ex)
        {
            _windowService.Error($"Error amending commit: {ex.Message}", "Gitster");
        }
    }


    private async Task RemoveChangeFromCommitAsync(DiffFileEntry? file)
    {
        var selected = CommitListVM.SelectedCommit;
        if (selected is null || file is null)
            return;

        if (selected.RemoteState == CommitRemoteState.Incoming)
        {
            _windowService.Warning(
                "Incoming commits are not on the local branch yet. Pull or cherry-pick the commit before editing it.",
                "Gitster");
            return;
        }

        if (!await EnsureCleanWorkingTreeBeforeRemovingChangeAsync())
            return;

        var commitSha = ShortSha(selected.FullSha);
        var confirmText =
            $"Remove the change to '{file.Path}' from commit {commitSha}?\n\n" +
            "This rewrites the selected commit and later commits on this branch. " +
            "The removed file change will be left staged so you can commit it later.";

        if (selected.RemoteState == CommitRemoteState.OnRemote)
            confirmText += "\n\nThis commit has already been pushed. Rewriting it will require a force-push.";

        var confirm = _windowService.ShowMessage(
            confirmText,
            "Remove change from commit",
            MessageBoxButton.YesNo,
            selected.RemoteState == CommitRemoteState.OnRemote ? MessageBoxImage.Warning : MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
            return;

        try
        {
            var beforeSha = await _gitBackend.GetHeadShaAsync();
            var branchName = TitleBarVM.CurrentBranch;
            await _snapshotService.CaptureAsync(_gitBackend, $"Before removing {file.Path} from {commitSha}");

            var afterSha = await _feedbackService.RunAsync(
                "Remove change from commit",
                async () =>
                {
                    await Task.Run(() => _gitBackend.RemoveFileChangeFromCommitAsync(
                        selected.FullSha,
                        file.Path,
                        branchName));
                    return await _gitBackend.GetHeadShaAsync();
                },
                ShortSha);

            var reflogSelector = await TryGetHeadReflogSelectorAsync();

            await _opsLogService.RecordAsync(new OperationRecord(
                Id: Guid.NewGuid().ToString(),
                Timestamp: DateTimeOffset.Now,
                Kind: OperationKind.HistoryEdit,
                Description: $"Remove {file.Path} from {commitSha}",
                BranchName: branchName,
                BeforeSha: beforeSha,
                AfterSha: afterSha,
                ReflogSelector: reflogSelector,
                Status: OperationStatus.Active));

            ClearPendingHeadRefresh();
            await RefreshAfterHeadChangeAsync();
            await CommitPanelVM.LoadAsync();
        }
        catch (Exception ex)
        {
            _windowService.Error($"Remove change from commit failed:\n{ex.Message}", "Gitster");
        }
    }

    private async Task FixupDroppedCommitAsync(CommitItem source, CommitItem target)
    {
        if (!CommitListViewModel.CanDropCommitForFixup(source, target, out var reason))
        {
            _windowService.Warning(reason, "Cannot fixup");
            return;
        }

        if (!_capabilityService.IsGitCliAvailable)
        {
            _windowService.Warning(
                "Commit fixup requires the Git command-line tool. Install Git for Windows and restart Gitster.",
                "Git CLI required");
            return;
        }

        var sourceShort = ShortSha(source.FullSha);
        var targetShort = ShortSha(target.FullSha);
        if (!_windowService.Confirm(
            $"Fixup {sourceShort} into {targetShort}?\n\nThis rewrites local history.",
            "Fixup commit"))
        {
            return;
        }

        try
        {
            var beforeSha = await _gitBackend.GetHeadShaAsync();
            await _snapshotService.CaptureAsync(_gitBackend, $"Before fixup {sourceShort} into {targetShort}");

            await _feedbackService.RunAsync(
                $"Fixup {sourceShort}",
                () => _gitBackend.FixupCommitIntoCommitAsync(source.FullSha, target.FullSha));

            var afterSha = await _gitBackend.GetHeadShaAsync();
            var branch = (await _gitBackend.GetCurrentBranchAsync()).Name;
            await _opsLogService.RecordAsync(new OperationRecord(
                Id: Guid.NewGuid().ToString(),
                Timestamp: DateTimeOffset.Now,
                Kind: OperationKind.Fixup,
                Description: $"Fixup {sourceShort} into {targetShort}",
                BranchName: branch,
                BeforeSha: beforeSha,
                AfterSha: afterSha,
                ReflogSelector: null,
                Status: OperationStatus.Active));

            await UpdateElementsAsync();
        }
        catch (Exception ex)
        {
            if (await ConflictGuidanceService.ShowIfConflictAsync(_windowService, _gitBackend, "Fixup", ex))
                return;

            _windowService.Error($"Fixup failed:\n{ex.Message}", "Gitster");
        }
    }

    [RelayCommand(CanExecute = nameof(HasCommitContextTarget))]
    private async Task CheckoutCommitDetached(CommitItem? commit)
    {
        if (commit is null)
            return;

        var shortSha = ShortSha(commit.FullSha);
        if (!_windowService.Confirm(
            $"Checkout {shortSha} in detached HEAD state?\n\n" +
            "You will leave the current branch. Create a branch before committing new work if you want to keep it.",
            "Checkout detached"))
            return;

        try
        {
            await _feedbackService.RunAsync(
                $"Checkout {shortSha}",
                () => _gitBackend.CheckoutCommitDetachedAsync(commit.FullSha));
            ClearPendingHeadRefresh();
            await RefreshAfterHeadChangeAsync();
        }
        catch (Exception ex)
        {
            _windowService.Error($"Checkout failed:\n{ex.Message}", "Gitster");
        }
    }

    [RelayCommand(CanExecute = nameof(HasCommitContextTarget))]
    private async Task ViewCommitDetails(CommitItem? commit)
    {
        if (commit is null)
            return;

        try
        {
            var details = await _gitBackend.GetCommitAsync(commit.FullSha);
            var window = new CommitDetailsDialog(details);
            _windowService.ShowDialog(window);
        }
        catch (Exception ex)
        {
            _windowService.Error($"Could not load commit details:\n{ex.Message}", "Gitster");
        }
    }

    [RelayCommand(CanExecute = nameof(HasCommitContextTarget))]
    private void OpenCommitInBrowser(CommitItem? commit)
    {
        if (commit is null)
            return;

        try
        {
            using var repo = new Repository(Path);
            var remote = repo.Network.Remotes[ActiveRemote(null)] ?? repo.Network.Remotes.FirstOrDefault();
            if (remote is null)
            {
                _windowService.Warning("No remote is configured for this repository.", "Open in browser");
                return;
            }

            if (!CommitBrowserUrlBuilder.TryBuild(remote.Url, commit.FullSha, out var browserUrl))
            {
                _windowService.Warning(
                    $"Gitster does not know how to build a commit URL for this remote:\n{remote.Url}",
                    "Open in browser");
                return;
            }

            Process.Start(new ProcessStartInfo(browserUrl) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _windowService.Error($"Could not open commit in browser:\n{ex.Message}", "Gitster");
        }
    }

    [RelayCommand(CanExecute = nameof(CanCompareSelectedCommits))]
    private async Task CompareSelectedCommits()
    {
        var selected = CommitListVM.SelectedCommits
            .GroupBy(c => c.FullSha, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
        if (selected.Count != 2)
        {
            _windowService.Info("Select exactly two commits to compare.", "Compare commits");
            return;
        }

        var visible = CommitListVM.Items.OfType<CommitItem>().ToList();
        var ordered = selected
            .OrderBy(c =>
            {
                var index = visible.FindIndex(v => string.Equals(v.FullSha, c.FullSha, StringComparison.OrdinalIgnoreCase));
                return index < 0 ? int.MaxValue : index;
            })
            .ToList();

        var compare = ordered[0];
        var @base = ordered[1];
        SidebarVM.CurrentMode = AppMode.Search;
        await SearchVM.RunCompareRefsAsync(@base.FullSha, compare.FullSha, threeDot: false);
    }

    private bool CanCompareSelectedCommits() => CommitListVM.HasTwoSelectedCommits;

    [RelayCommand(CanExecute = nameof(HasCommitContextTarget))]
    private async Task NewBranchFromCommit(CommitItem? commit)
    {
        if (commit is null)
            return;

        var shortSha = ShortSha(commit.FullSha);
        var dialog = new TextInputDialog
        {
            Title = "Create branch",
            Prompt = $"New branch name (starting from {shortSha}):",
            Value = string.Empty,
        };
        if (_windowService.ShowDialog(dialog) != true)
            return;

        var name = dialog.Value.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return;

        try
        {
            await _snapshotService.CaptureAsync(_gitBackend, $"Create branch {name}");
            await _feedbackService.RunAsync("Create branch", () => _gitBackend.CreateBranchAsync(name, commit.FullSha));
            await BranchesVM.LoadAsync();
            SidebarVM.BranchCount = BranchesVM.LocalCount;
            await CommitListVM.LoadAsync();
        }
        catch (Exception ex)
        {
            _windowService.Error($"Create branch failed:\n{ex.Message}", "Gitster");
        }
    }

    [RelayCommand(CanExecute = nameof(HasCommitContextTarget))]
    private async Task NewTagFromCommit(CommitItem? commit)
    {
        if (commit is null)
            return;

        var shortSha = ShortSha(commit.FullSha);
        var dialog = new TextInputDialog
        {
            Title = "Create tag",
            Prompt = $"New tag name (pointing at {shortSha}):",
            Value = string.Empty,
        };
        if (_windowService.ShowDialog(dialog) != true)
            return;

        var name = dialog.Value.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return;

        try
        {
            await _feedbackService.RunAsync("Create tag", () => _gitBackend.CreateTagAsync(name, commit.FullSha));
            _taggedCommitShas.Add(commit.FullSha);
            PushTagCommand.NotifyCanExecuteChanged();
            await CommitListVM.LoadAsync();
        }
        catch (Exception ex)
        {
            _windowService.Error($"Create tag failed:\n{ex.Message}", "Gitster");
        }
    }

    [RelayCommand(CanExecute = nameof(CanPushTag))]
    private async Task PushTag(CommitItem? commit)
    {
        if (commit is null)
            return;

        var shortSha = ShortSha(commit.FullSha);
        try
        {
            var tags = (await _gitBackend.GetTagsForCommitAsync(commit.FullSha)).ToList();
            if (tags.Count == 0)
            {
                _windowService.Info(
                    $"No local tag points at {shortSha}. Create a tag on this commit first, then push it.",
                    "Push tag");
                return;
            }

            var tagName = tags.Count == 1
                ? tags[0]
                : PromptForTagToPush(shortSha, tags);
            if (string.IsNullOrWhiteSpace(tagName))
                return;

            if (Remotes.Count == 0)
            {
                _windowService.Warning("No remote is configured for this repository.", "Push tag");
                return;
            }

            var remote = ActiveRemote(null);
            await RunRemoteOperationAsync(
                $"Push tag {tagName}",
                ct => _gitBackend.PushTagAsync(tagName, remote, ct));
            await UpdateElementsAsync();
        }
        catch (Exception ex)
        {
            _windowService.Error($"Push tag failed:\n{ex.Message}", "Gitster");
        }
    }

    private bool CanPushTag(CommitItem? commit) =>
        commit is not null
        && HasCommitContextTarget(commit)
        && _taggedCommitShas.Contains(commit.FullSha);

    private string? PromptForTagToPush(string shortSha, IReadOnlyList<string> tags)
    {
        var dialog = new TextInputDialog
        {
            Title = "Push tag",
            Prompt = $"Multiple local tags point at {shortSha}. Enter one to push:\n{string.Join(", ", tags)}",
            Value = tags[0],
        };
        if (_windowService.ShowDialog(dialog) != true)
            return null;

        var name = dialog.Value.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return null;

        if (tags.Contains(name, StringComparer.Ordinal))
            return name;

        _windowService.Warning("Choose one of the tags that points at the selected commit.", "Push tag");
        return null;
    }

    private void RefreshCommitTagAvailability(Repository repo)
    {
        _taggedCommitShas.Clear();
        foreach (var tag in repo.Tags)
        {
            if (tag.PeeledTarget is Commit commit)
                _taggedCommitShas.Add(commit.Id.Sha);
        }

        PushTagCommand.NotifyCanExecuteChanged();
    }

    private void ClearCommitTagAvailability()
    {
        _taggedCommitShas.Clear();
        PushTagCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(HasCommitContextTarget))]
    private async Task RevertCommit(CommitItem? commit)
    {
        if (commit is null)
            return;

        var shortSha = ShortSha(commit.FullSha);
        if (!_windowService.Confirm(
            $"Revert commit {shortSha}?\n\n" +
            "This creates a new commit that applies the inverse of the selected commit.",
            "Revert commit"))
            return;

        try
        {
            var beforeSha = await _gitBackend.GetHeadShaAsync();
            var branchName = TitleBarVM.CurrentBranch;
            await _snapshotService.CaptureAsync(_gitBackend, $"Before revert {shortSha}");

            await _feedbackService.RunAsync(
                $"Revert {shortSha}",
                () => _gitBackend.RevertCommitAsync(commit.FullSha));

            var afterSha = await _gitBackend.GetHeadShaAsync();
            var reflogSelector = await TryGetHeadReflogSelectorAsync();
            await _opsLogService.RecordAsync(new OperationRecord(
                Id: Guid.NewGuid().ToString(),
                Timestamp: DateTimeOffset.Now,
                Kind: OperationKind.Revert,
                Description: $"Revert {shortSha}",
                BranchName: branchName,
                BeforeSha: beforeSha,
                AfterSha: afterSha,
                ReflogSelector: reflogSelector,
                Status: OperationStatus.Active));

            ClearPendingHeadRefresh();
            await RefreshAfterHeadChangeAsync();
        }
        catch (Exception ex)
        {
            _windowService.Error($"Revert failed:\n{ex.Message}", "Gitster");
        }
    }

    [RelayCommand(CanExecute = nameof(HasCommitContextTarget))]
    private async Task CherryPickCommit(CommitItem? commit)
    {
        if (commit is null)
            return;

        var shortSha = ShortSha(commit.FullSha);
        if (!_windowService.Confirm(
            $"Cherry-pick commit {shortSha} onto the current branch?",
            "Cherry-pick"))
            return;

        try
        {
            var beforeSha = await _gitBackend.GetHeadShaAsync();
            await _snapshotService.CaptureAsync(_gitBackend, $"Cherry-pick {shortSha}");
            await _feedbackService.RunAsync($"Cherry-pick {shortSha}", () => _gitBackend.CherryPickAsync(commit.FullSha));
            var afterSha = await _gitBackend.GetHeadShaAsync();
            var branchName = TitleBarVM.CurrentBranch;
            await _opsLogService.RecordAsync(new OperationRecord(
                Id: Guid.NewGuid().ToString(),
                Timestamp: DateTimeOffset.Now,
                Kind: OperationKind.CherryPick,
                Description: $"Cherry-pick {shortSha}",
                BranchName: branchName,
                BeforeSha: beforeSha,
                AfterSha: afterSha,
                ReflogSelector: null,
                Status: OperationStatus.Active));

            ClearPendingHeadRefresh();
            await RefreshAfterHeadChangeAsync();
        }
        catch (Exception ex)
        {
            if (await ConflictGuidanceService.ShowIfConflictAsync(_windowService, _gitBackend, "Cherry-pick", ex))
                return;

            _windowService.Error($"Cherry-pick failed:\n{ex.Message}", "Gitster");
        }
    }

    private bool HasCommitContextTarget(CommitItem? commit) =>
        IsGoButtonEnabled && commit is not null;

    private async Task<bool> EnsureCleanWorkingTreeBeforeRemovingChangeAsync()
    {
        WorkingTreeStatus status;
        try
        {
            status = await _gitBackend.GetWorkingTreeStatusAsync();
        }
        catch (Exception ex)
        {
            _windowService.Warning(
                $"Could not check working-tree state before removing a change from a commit:\n{ex.Message}",
                "Gitster");
            return false;
        }

        if (status.Staged.Count == 0 && status.Unstaged.Count == 0)
            return true;

        _windowService.Warning(
            "Commit, stash, or discard your current changes before rewriting history.\n\n" +
            "Removing a change from an older commit rewrites this branch, then leaves that removed file change staged for a later commit.",
            "Working tree has uncommitted changes");
        return false;
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
                BeforeSha: beforeSha,
                AfterSha: afterSha,
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

    private static string ShortSha(string sha) => GitSha.Short(sha);

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
            await RunRemoteOperationAsync("Fetch", ct => _gitBackend.FetchAsync(selectedRemote, ct));
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
            await RunRemoteOperationAsync("Pull", ct => _gitBackend.PullAsync(selectedRemote, ct));
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

    [RelayCommand(CanExecute = nameof(CanPushThroughCommit))]
    private async Task PushThroughCommit(CommitItem? commit)
    {
        if (!CanPushThroughCommit(commit))
        {
            var message = !_capabilityService.IsGitCliAvailable
                ? "Push through commit requires the Git command-line tool. Install Git for Windows and restart Gitster."
                : "Only local-only commits can be pushed through this action.";
            _windowService.Warning(message, "Gitster");
            return;
        }

        try
        {
            var remote = ActiveRemote(null);
            var shortSha = ShortSha(commit!.FullSha);
            await RunRemoteOperationAsync(
                $"Push through {shortSha}",
                ct => _gitBackend.PushThroughCommitAsync(commit.FullSha, remote, ct));
            await UpdateElementsAsync();
        }
        catch (Exception ex)
        {
            _windowService.Error($"Error pushing through commit: {ex.Message}", "Gitster");
        }
    }

    private bool CanPushThroughCommit(CommitItem? commit) =>
        IsGoButtonEnabled
        && _capabilityService.IsGitCliAvailable
        && commit?.RemoteState == CommitRemoteState.LocalOnly;

    [RelayCommand(CanExecute = nameof(CanForceRemoteToCommit))]
    private async Task ForceRemoteToCommit(CommitItem? commit)
    {
        if (!CanForceRemoteToCommit(commit))
        {
            var message = !_capabilityService.IsGitCliAvailable
                ? "Rewinding the remote requires the Git command-line tool. Install Git for Windows and restart Gitster."
                : "Pick a commit that is already on the remote to rewind the remote branch back to it.";
            _windowService.Warning(message, "Gitster");
            return;
        }

        var shortSha = ShortSha(commit!.FullSha);
        var confirmed = _windowService.Confirm(
            $"This force-pushes the remote branch back to {shortSha}, discarding every remote commit after it. " +
            "Anyone who has already pulled those commits will be affected.\n\n" +
            "It uses --force-with-lease, so it aborts if the remote received commits you haven't fetched.\n\n" +
            "Rewind the remote to this commit?",
            "Dangerous: rewind remote");
        if (!confirmed)
            return;

        try
        {
            var remote = ActiveRemote(null);
            await RunRemoteOperationAsync(
                $"Rewind remote to {shortSha}",
                ct => _gitBackend.ForceRemoteToCommitAsync(commit.FullSha, remote, ct));
            await UpdateElementsAsync();
        }
        catch (Exception ex)
        {
            _windowService.Error($"Error rewinding remote: {ex.Message}", "Gitster");
        }
    }

    private bool CanForceRemoteToCommit(CommitItem? commit) =>
        IsGoButtonEnabled
        && _capabilityService.IsGitCliAvailable
        && commit?.RemoteState == CommitRemoteState.OnRemote;

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
            await RunRemoteOperationAsync(verb, ct => _gitBackend.PushAsync(remote, mode, ct));
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
            await RunRemoteOperationAsync("Sync", async ct =>
            {
                await _gitBackend.FetchAsync(remote, ct);
                await _gitBackend.PullAsync(remote, ct);
                await _gitBackend.PushAsync(remote, ct: ct);
            });

            await UpdateElementsAsync();
        }
        catch (Exception ex)
        {
            _windowService.Error($"Error syncing: {ex.Message}", "Gitster");
        }
    }

    private async Task RunRemoteOperationAsync(
        string title,
        Func<CancellationToken, Task> operation)
    {
        using var cts = new CancellationTokenSource();
        var viewModel = new OperationProgressViewModel(title)
        {
            CanCancel = true,
            StageText = title,
            DetailText = "Contacting remote.",
            ProgressValue = 10,
        };
        viewModel.CancelRequested += (_, _) => cts.Cancel();

        var task = _feedbackService.RunAsync(title, () => operation(cts.Token));
        var delay = Task.Delay(TimeSpan.FromSeconds(1));
        if (await Task.WhenAny(task, delay) == task)
        {
            try
            {
                await task;
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                viewModel.Dispose();
            }
            return;
        }

        viewModel.Report(new OperationProgress(title, "Waiting for the remote operation to complete.", 40));
        var window = new OperationProgressWindow(viewModel);
        _ = task.ContinueWith(t =>
        {
            Application.Current.Dispatcher.BeginInvoke(() =>
                window.Complete(t.Status == TaskStatus.RanToCompletion));
        }, CancellationToken.None);

        _windowService.ShowDialog(window);
        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
        }
    }

    public async Task OnWindowActivatedAsync()
    {
        if (_repositoryLifecycle.IsSwitchingRepository || !_repositoryLifecycle.InitialRepositoryLoadStarted)
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
        _appSettings.SaveRepositoryPath(Path);
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
        IsRepositoryLoading = true;
        try
        {
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

            await refreshTask;
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            IsRepositoryLoading = false;
        }
    }

    private async Task RefreshRepositoryAfterActivationCoreAsync(
        CancellationToken ct,
        IProgress<RepositoryLoadProgress> progress)
    {
        await UpdateElementsAsync(ct, progress);
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

    /// <summary>
    /// Full reload, serialized with queued HEAD refreshes through the coordinator gate
    /// so a manual/remote-op reload can never interleave with a HEAD-change refresh.
    /// </summary>
    public async Task UpdateElementsAsync(
        CancellationToken ct = default,
        IProgress<RepositoryLoadProgress>? progress = null)
        => await _headRefresh.RunExclusiveAsync(token => UpdateElementsUngatedAsync(token, progress), ct);

    /// <summary>Reload body for callers that already hold the coordinator gate.</summary>
    private async Task UpdateElementsUngatedAsync(
        CancellationToken ct,
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

    private void QueueHeadRefresh() => _headRefresh.Queue();

    private void ClearPendingHeadRefresh() => _headRefresh.ClearPending();

    private async Task RefreshAfterHeadChangeAsync(CancellationToken ct = default)
        => await _headRefresh.RunExclusiveAsync(RefreshAfterHeadChangeCoreAsync, ct);

    private async Task RefreshAfterHeadChangeCoreAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(Path))
            return;

        try
        {
            await _gitBackend.OpenAsync(Path);
            ct.ThrowIfCancellationRequested();

            using var repo = new Repository(Path);
            ApplyHeadState(repo);

            await CommitListVM.LoadAsync(ct);
            ct.ThrowIfCancellationRequested();

            RefreshCommitTagAvailability(repo);
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
            // Already running under the coordinator gate — must not re-acquire it.
            await UpdateElementsUngatedAsync(ct);
        }
    }

    private async Task RefreshHeadRelatedSidePanelsAsync()
    {
        try
        {
            await BranchesVM.LoadAsync();
            SidebarVM.BranchCount = BranchesVM.LocalCount;
            await CommitRefNavigatorVM.LoadAsync();
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
        await AttachRepositoryServicesAsync(repoPath, ct, progress);
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

        await CommitRefNavigatorVM.LoadAsync();
        ct.ThrowIfCancellationRequested();

        progress?.Report(new RepositoryLoadProgress("Loading worktrees", "Reading linked worktrees."));
        await WorktreesVM.LoadAsync();
        ct.ThrowIfCancellationRequested();

        SidebarVM.BranchCount = BranchesVM.LocalCount;
        await RefreshSidebarBadgesAsync();
        ct.ThrowIfCancellationRequested();

        await CommitListVM.LoadAsync(ct, progress);
        ct.ThrowIfCancellationRequested();

        RefreshCommitTagAvailability(repo);

        progress?.Report(new RepositoryLoadProgress("Finalizing", "Refreshing author index."));
        _ = _authorDirService.RefreshAsync();
        _ = _stateService.RefreshAsync();
    }

    private bool ApplyHeadState(Repository repo)
    {
        var headTip = repo.Head.Tip;
        if (headTip == null)
        {
            CurrentCommitDetail.Clear();
            _selectionContext.CurrentCommitDate = null;
            SelectedCommitDetail.Clear();
            TimestampEditVM.UpdatePreviewBefore("-");
            IsGoButtonEnabled = false;
            CommitListVM.ClearList();
            ClearCommitTagAvailability();
            UpdateStatusBar(repo);
            return false;
        }

        CurrentCommitDetail.UpdateCommit(
            headTip.MessageShort,
            headTip.Author.When.DateTime);
        _selectionContext.CurrentCommitDate = headTip.Author.When.DateTime;

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
        CommitRefNavigatorVM.Clear();
        WorktreesVM.Clear();
        CurrentCommitDetail.Clear();
        SelectedCommitDetail.Clear();
        SelectedDate = null;
        TimestampEditVM.UpdatePreviewBefore("-");
        IsGoButtonEnabled = false;
        Remotes.Clear();
        TitleBarVM.Clear();
        CommitListVM.ClearList();
        ClearCommitTagAvailability();
        _selectionCoordinator.Clear();
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
            _selectionContext.CurrentBranch = branch;

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

    private async Task AttachRepositoryServicesAsync(
        string repoPath,
        CancellationToken ct,
        IProgress<RepositoryLoadProgress>? progress)
    {
        progress?.Report(new RepositoryLoadProgress(
            "Attaching repository services",
            "Opening Git backends."));
        await _gitBackend.OpenAsync(repoPath);
        ct.ThrowIfCancellationRequested();

        progress?.Report(new RepositoryLoadProgress(
            "Attaching repository services",
            "Starting repository watchers."));
        await _stateService.AttachAsync(repoPath, refreshImmediately: false);
        ct.ThrowIfCancellationRequested();

        progress?.Report(new RepositoryLoadProgress(
            "Attaching repository services",
            "Loading operations log."));
        await _opsLogService.AttachAsync(repoPath);
        ct.ThrowIfCancellationRequested();

        progress?.Report(new RepositoryLoadProgress(
            "Attaching repository services",
            "Preparing safety snapshots."));
        await _snapshotService.AttachAsync(repoPath);
        ct.ThrowIfCancellationRequested();

        progress?.Report(new RepositoryLoadProgress(
            "Attaching repository services",
            "Loading stash names."));
        await _stashNameService.AttachAsync(repoPath);
        ct.ThrowIfCancellationRequested();

        progress?.Report(new RepositoryLoadProgress(
            "Attaching repository services",
            "Loading custom tools."));
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
