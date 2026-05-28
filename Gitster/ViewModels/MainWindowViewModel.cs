using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Gitster.Services;
using Gitster.Services.Capabilities;
using Gitster.Services.Git;
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
    private List<CommitItem> _allCommits = [];
    private FilterWindow? _filterWindow;
    private readonly OperationsLogService _opsLogService = new();
    private readonly IGitBackend _gitBackend;
    private readonly RepositoryStateService _stateService;
    private readonly OperationFeedbackService _feedbackService;
    private readonly RecentReposService _recentRepos;
    private readonly AuthorDirectoryService _authorDirService;
    private readonly SnapshotService _snapshotService = new();
    private readonly StashNameService _stashNameService = new();
    private readonly CustomToolsService _customToolsService = new();
    private bool _hasTrackingBranch = true;

    public TitleBarViewModel TitleBarVM { get; }
    public CommitListViewModel CommitListVM { get; }
    public TimestampEditViewModel TimestampEditVM { get; }
    public QuickActionsViewModel QuickActionsVM { get; }
    public UndoBarViewModel UndoBarVM { get; }
    public StatusBarViewModel StatusBarVM { get; }
    public AutoFetchService AutoFetch { get; }
    public AuthorPanelViewModel AuthorPanelVM { get; }
    public SidebarViewModel SidebarVM { get; } = new();
    public StashesViewModel StashesVM { get; }
    public BranchesViewModel BranchesVM { get; }
    public WorktreesViewModel WorktreesVM { get; }
    public OperationsLogService OpsLogService => _opsLogService;

    public MainWindowViewModel()
    {
        _gitBackend = new HybridGitBackend();
        _stateService = new RepositoryStateService(_gitBackend);
        _feedbackService = new OperationFeedbackService();
        _recentRepos = new RecentReposService();
        _authorDirService = new AuthorDirectoryService(_gitBackend);
        AutoFetch = new AutoFetchService(_gitBackend);

        var capabilityService = new CapabilityService(_gitBackend);
        Capability.Initialize(capabilityService);

        SelectedCommitDetail = new CommitDetailViewModel();
        CurrentCommitDetail = new CommitDetailViewModel();
        StatusBarVM = new StatusBarViewModel(_stateService, _feedbackService);
        TitleBarVM = new TitleBarViewModel(BrowseFolder, OpenRepoByPath, AutoFetch, _recentRepos);
        TitleBarVM.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(TitleBarViewModel.RepositoryName) or nameof(TitleBarViewModel.CurrentBranch))
                OnPropertyChanged(nameof(WindowTitle));
        };
        CommitListVM = new CommitListViewModel(OpenFilter, ClearAllFilters);
        CommitListVM.PropertyChanged += OnCommitListVmPropertyChanged;
        TimestampEditVM = new TimestampEditViewModel(
            () => CommitListVM.SelectedCommit,
            () => CurrentCommitDetail.CommitDate);
        QuickActionsVM = new QuickActionsViewModel(
            _gitBackend,
            _feedbackService,
            _opsLogService,
            _snapshotService,
            () => CommitListVM.SelectedCommit,
            () => CommitListVM.SelectedCommits,
            async () => await UpdateElementsAsync());
        UndoBarVM = new UndoBarViewModel(_opsLogService, _gitBackend, _feedbackService);
        AuthorPanelVM = new AuthorPanelViewModel(_gitBackend, _authorDirService);
        StashesVM = new StashesViewModel(
            _gitBackend,
            _feedbackService,
            _opsLogService,
            _snapshotService,
            _stashNameService,
            async () => await RefreshSidebarBadgesAsync());
        BranchesVM = new BranchesViewModel(
            _gitBackend,
            _feedbackService,
            _opsLogService,
            _snapshotService,
            async () => await RefreshSidebarBadgesAsync());
        WorktreesVM = new WorktreesViewModel(
            _gitBackend,
            _feedbackService,
            _snapshotService,
            () => Path,
            OpenRepoByPath);

        // Update ops log badge whenever the log changes
        _opsLogService.Changed += (_, _) =>
            SidebarVM.ActiveOpsCount = _opsLogService.Records.Count(r => r.Status == OperationStatus.Active);

        // A.3 — refresh commit list after any backend HEAD mutation (e.g. Undo via ResetHard)
        _gitBackend.HeadChanged += (_, _) =>
            Application.Current.Dispatcher.BeginInvoke(async () => await UpdateElementsAsync());

        // Subscribe to filter changes
        Filter.PropertyChanged += (s, e) =>
        {
            ApplyFilters();
        };

        // Load saved path or use default
        Path = Properties.Settings.Default.Path ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        FolderPath = Path;

        _stateService.PropertyChanged += OnRepositoryStateChanged;
    }

    [ObservableProperty]
    public partial string Path { get; set; } = string.Empty;

    public string WindowTitle =>
        string.IsNullOrWhiteSpace(TitleBarVM.RepositoryName)
            ? "Gitster"
            : $"{TitleBarVM.RepositoryName} \u00b7 {TitleBarVM.CurrentBranch} \u2013 Gitster";

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

    [ObservableProperty]
    public partial string FilterStatusText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasActiveFilters { get; set; }

    [ObservableProperty]
    public partial List<CommitItem> Commits { get; set; } = [];
    public ObservableCollection<string> Remotes { get; } = [];

    public CommitFilter Filter { get; } = new();

    /// <summary>True when the selected commit is already on the remote — amending it requires a force-push.</summary>
    public bool IsAmendUnsafe => SelectedCommit?.RemoteState == Gitster.Services.Git.CommitRemoteState.OnRemote;

    partial void OnFolderPathChanged(string value)
    {
        Path = value;
        UpdateSettingsPath();
        _ = TryAttachRepositoryServicesAsync();
        _ = UpdateElementsAsync();
    }

    partial void OnSelectedCommitChanged(CommitItem? value)
    {
        if (value != null)
            SelectedCommitDetail.UpdateCommit(value.Message, value.Date);
        else
            SelectedCommitDetail.Clear();

        OnPropertyChanged(nameof(IsAmendUnsafe));
        _ = AuthorPanelVM.LoadFromCommitAsync(value);
    }

    private void OnCommitListVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CommitListViewModel.SelectedCommit))
        {
            SelectedCommit = CommitListVM.SelectedCommit;
            UpdateDiffPreview();
        }
    }

    private void UpdateDiffPreview()
    {
        var commit = CommitListVM.SelectedCommit;
        if (commit == null)
        {
            CommitListVM.UpdateDiff(string.Empty, []);
            return;
        }
        try
        {
            using var repo = new Repository(Path);
            var gitCommit = repo.Lookup<Commit>(commit.CommitId);
            if (gitCommit == null)
            {
                CommitListVM.UpdateDiff(string.Empty, []);
                return;
            }
            var parent = gitCommit.Parents.FirstOrDefault();
            // For the initial commit (no parent), compare against empty tree.
            // LibGit2Sharp handles null oldTree as the empty tree.
            Patch patch;
            if (parent == null)
                patch = repo.Diff.Compare<Patch>(null, gitCommit.Tree);
            else
                patch = repo.Diff.Compare<Patch>(parent.Tree, gitCommit.Tree);

            var files = patch
                .Select(e => new DiffFileEntry(e.Path, e.LinesAdded, e.LinesDeleted,
                    e.Status switch
                    {
                        ChangeKind.Added    => "A",
                        ChangeKind.Deleted  => "D",
                        ChangeKind.Renamed  => "R",
                        _                   => "M"
                    }))
                .ToList();
            var header = $"{files.Count} {(files.Count == 1 ? "file" : "files")} · +{patch.LinesAdded} −{patch.LinesDeleted}";
            CommitListVM.UpdateDiff(header, files, commit.RemoteState);
        }
        catch
        {
            CommitListVM.UpdateDiff(string.Empty, []);
        }
    }

    [RelayCommand]
    private void BrowseFolder()
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

            if (dialog.ShowDialog() == true)
            {
                FolderPath = dialog.FolderName;
                _recentRepos.Record(dialog.FolderName);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error opening folder dialog: {ex.Message}");
        }
    }

    /// <summary>Opens a repository by path directly (used by recent-repos dropdown).</summary>
    private void OpenRepoByPath(string path)
    {
        FolderPath = path;
        _recentRepos.Record(path);
    }

    [RelayCommand]
    private void OpenFilter()
    {
        try
        {
            // If filter window is already open, just activate it
            if (_filterWindow != null)
            {
                _filterWindow.Activate();
                return;
            }

            // Create FilterWindowViewModel with the main filter
            var filterViewModel = new FilterWindowViewModel(Filter);

            // Populate author names from all commits
            filterViewModel.AuthorNames.Clear();
            filterViewModel.AuthorNames.Add("All");

            var distinctAuthors = _allCommits
                .Select(c => c.AuthorName)
                .Where(name => !string.IsNullOrEmpty(name))
                .Distinct()
                .OrderBy(name => name);

            foreach (var author in distinctAuthors)
            {
                filterViewModel.AuthorNames.Add(author);
            }

            _filterWindow = new FilterWindow(filterViewModel)
            {
                Owner = Application.Current.MainWindow
            };

            // Subscribe to FiltersApplied event
            _filterWindow.FiltersApplied += (sender, e) =>
            {
                filterViewModel.ApplyToMainFilter();
            };

            // Clean up when window is closed
            _filterWindow.Closed += (sender, e) => _filterWindow = null;

            _filterWindow.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error opening filter window: {ex.Message}");
        }
    }

    [RelayCommand]
    private void ClearAllFilters()
    {
        Filter.ClearAllFilters();
    }

    [RelayCommand]
    private void OpenRepo(string path)
    {
        FolderPath = path;
        _recentRepos.Record(path);
    }

    [RelayCommand]
    private void Exit() => Application.Current.Shutdown();

    [RelayCommand]
    private async Task Refresh() => await UpdateElementsAsync();

    [RelayCommand]
    private void SwitchBranch() { }

    [RelayCommand]
    private void OpenOperationsLog()
    {
        var window = new OperationsLogWindow(_opsLogService, TitleBarVM.RepositoryName)
        {
            Owner = Application.Current.MainWindow
        };
        window.ShowDialog();
    }

    [RelayCommand]
    private void OpenRepoSettings()
    {
        if (string.IsNullOrWhiteSpace(Path)) return;
        var vm = new RepositorySettingsViewModel(Path);
        var window = new RepositorySettingsWindow(vm) { Owner = Application.Current.MainWindow };
        window.ShowDialog();
    }

    [RelayCommand]
    private void OpenAuthorRepair()
    {
        var vm = new AuthorRepairViewModel(_gitBackend, _authorDirService.Authors);
        var window = new Views.AuthorRepairDialog(vm) { Owner = Application.Current.MainWindow };
        window.ShowDialog();
    }

    [RelayCommand]
    private async Task OpenRewriteTimestamps()
    {
        if (_allCommits.Count == 0) return;

        try
        {
            var beforeSha = await _gitBackend.GetHeadShaAsync();
            var vm = new RangeTimestampViewModel(_gitBackend, _allCommits.ToList());
            var window = new Views.RangeTimestampDialog(vm) { Owner = Application.Current.MainWindow };

            if (window.ShowDialog() == true)
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
            MessageBox.Show($"Error rewriting timestamps: {ex.Message}");
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
        var vm = new ManageToolsViewModel(_customToolsService);
        var window = new Views.ManageToolsDialog(vm) { Owner = Application.Current.MainWindow };
        window.ShowDialog();
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
                MessageBox.Show("Select a commit first — this tool needs one.",
                    tool.Name, MessageBoxButton.OK, MessageBoxImage.Information);
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
                Owner  = Application.Current.MainWindow,
            };
            if (input.ShowDialog() != true) return;
            args = input.Value;
        }

        var command = _customToolsService.Substitute(tool.Command, revision, args, TitleBarVM.CurrentBranch);

        // Confirm, showing the exact command that will run.
        if (!string.IsNullOrEmpty(tool.Confirm))
        {
            var prompt = $"{tool.Confirm}\n\nCommand:\n{command}";
            if (MessageBox.Show(prompt, tool.Name, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;
        }

        // Snapshot before running ANY tool — Gitster can't know what a tool does.
        _ = _snapshotService.CaptureAsync(_gitBackend, $"Before tool: {tool.Name}");

        try
        {
            var result = await _feedbackService.RunAsync(tool.Name,
                () => _customToolsService.RunAsync(command),
                r => r.Success ? "completed" : $"exit {r.ExitCode}");

            var dialog = new Views.ToolResultDialog(tool.Name, result.ExitCode, result.Output)
            {
                Owner = Application.Current.MainWindow,
            };
            dialog.ShowDialog();

            // A tool may have changed the repository — refresh.
            await UpdateElementsAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Tool '{tool.Name}' failed:\n{ex.Message}", "Gitster",
                MessageBoxButton.OK, MessageBoxImage.Error);
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
                MessageBox.Show("Please select a date");
                return;
            }

            var beforeSha = await _gitBackend.GetHeadShaAsync();

            // Collect author/committer changes from AuthorPanelVM
            var (authorName, authorEmail) = AuthorPanelVM.GetPendingAuthor();
            var (committerName, committerEmail) = AuthorPanelVM.GetPendingCommitter();

            var afterSha = await _feedbackService.RunAsync(
                "Amend",
                () => _gitBackend.AmendAsync(new AmendRequest(
                    editDate.Value,
                    AuthorName:    authorName,
                    AuthorEmail:   authorEmail,
                    CommitterName:  committerName,
                    CommitterEmail: committerEmail)),
                sha => sha.Length > 7 ? sha[..7] : sha);

            var reflogSelector = await _gitBackend.GetReflogSelectorForHeadAsync();

            var branchName = TitleBarVM.CurrentBranch;
            var shortBefore = beforeSha.Length >= 7 ? beforeSha[..7] : beforeSha;
            var shortAfter = afterSha.Length >= 7 ? afterSha[..7] : afterSha;

            await _opsLogService.RecordAsync(new OperationRecord(
                Id: Guid.NewGuid().ToString(),
                Timestamp: DateTimeOffset.Now,
                Kind: OperationKind.Amend,
                Description: $"Amend {shortAfter}",
                BranchName: branchName,
                BeforeSha: shortBefore,
                AfterSha: shortAfter,
                ReflogSelector: reflogSelector,
                Status: OperationStatus.Active));

            _ = _snapshotService.CaptureAsync(_gitBackend, "Amend");

            await UpdateElementsAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error amending commit: {ex.Message}");
        }
    }

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
            MessageBox.Show($"Error reading selected commit: {ex.Message}");
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
            MessageBox.Show($"Error reading current commit: {ex.Message}");
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
            MessageBox.Show($"Error fetching: {ex.Message}");
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
            MessageBox.Show($"Error pulling: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task Push(string? remoteName)
    {
        try
        {
            var selectedRemote = string.IsNullOrWhiteSpace(remoteName) ? "origin" : remoteName;
            await _feedbackService.RunAsync("Push", () => _gitBackend.PushAsync(selectedRemote, forceWithLease: true));
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error pushing: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task Sync(string? remoteName)
    {
        try
        {
            var selectedRemote = string.IsNullOrWhiteSpace(remoteName) ? "origin" : remoteName;
            await _feedbackService.RunAsync("Sync", async () =>
            {
                await _gitBackend.FetchAsync(selectedRemote);
                await _gitBackend.PullAsync(selectedRemote);
                await _gitBackend.PushAsync(selectedRemote);
            });

            await UpdateElementsAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error syncing: {ex.Message}");
        }
    }

    public async Task OnWindowActivatedAsync()
    {
        await UpdateElementsAsync();
        await _stateService.RefreshAsync();
    }

    private void UpdateSettingsPath()
    {
        var settings = Properties.Settings.Default;
        settings.Path = Path;
        settings.Save();
    }

    private void ApplyFilters()
    {
        IEnumerable<CommitItem> filteredCommits = _allCommits;

        // Apply author filter
        if (!string.IsNullOrEmpty(Filter.SelectedAuthorName) && Filter.SelectedAuthorName != "All")
        {
            filteredCommits = filteredCommits.Where(c => c.AuthorName == Filter.SelectedAuthorName);
        }

        // Apply from date filter
        if (Filter.FromDate.HasValue)
        {
            var fromDate = Filter.FromDate.Value.Date;
            filteredCommits = filteredCommits.Where(c => c.Date.Date >= fromDate);
        }

        // Apply to date filter
        if (Filter.ToDate.HasValue)
        {
            // Include all commits up to the end of the selected day
            var toDateEndOfDay = Filter.ToDate.Value.Date.AddDays(1);
            filteredCommits = filteredCommits.Where(c => c.Date < toDateEndOfDay);
        }

        Commits = filteredCommits.ToList();

        // Update filter status
        UpdateFilterStatus();

        // Feed CommitListViewModel (handles auto-select and live text filter)
        CommitListVM.SetBaseCommits(Commits, HasActiveFilters, FilterStatusText, _hasTrackingBranch);
    }

    private void AutoSelectCommit()
    {
        // if selected commit is still in the list, keep it selected
        if (SelectedCommit != null && Commits.Contains(SelectedCommit))
        {
            return;
        }

        // Auto-select the first commit if available
        if (Commits.Count > 0)
        {
            SelectedCommit = Commits[0];
        }
        else
        {
            SelectedCommit = null;
            SelectedCommitDetail.Clear();
        }
    }

    private void UpdateFilterStatus()
    {
        int filterCount = 0;

        if (!string.IsNullOrEmpty(Filter.SelectedAuthorName)
            && Filter.SelectedAuthorName != "All")
        {
            filterCount++;
        }

        if (Filter.FromDate.HasValue)
        {
            filterCount++;
        }

        if (Filter.ToDate.HasValue)
        {
            filterCount++;
        }

        if (filterCount > 0)
        {
            FilterStatusText = $"{filterCount} Filter{(filterCount > 1 ? "s" : "")} applied";
            HasActiveFilters = true;
        }
        else
        {
            FilterStatusText = string.Empty;
            HasActiveFilters = false;
        }
    }

    public async Task UpdateElementsAsync()
    {
        Commits = [];
        try
        {
            await _gitBackend.OpenAsync(Path);
            _ = _stateService.AttachAsync(Path);

            using var repo = new Repository(Path);

            // A.1 — guard empty repository (no commits yet)
            var headTip = repo.Head.Tip;
            if (headTip == null)
            {
                CurrentCommitDetail.Clear();
                SelectedCommitDetail.Clear();
                TimestampEditVM.UpdatePreviewBefore("—");
                IsGoButtonEnabled = false;
                _allCommits.Clear();
                Commits = [];
                CommitListVM.SetBaseCommits([], false, string.Empty, hasTrackingBranch: false);
                UpdateStatusBar(repo);
                return;
            }

            CurrentCommitDetail.UpdateCommit(
                headTip.MessageShort,
                headTip.Author.When.DateTime
            );
            var headSha = headTip.Id.Sha.Length >= 6 ? headTip.Id.Sha[..6] : headTip.Id.Sha;
            TimestampEditVM.UpdatePreviewBefore($"{headTip.Author.When.DateTime:dd.MM. HH:mm} · {headSha}");

            // A.1 — guard initial commit (no parents)
            var previousCommit = headTip.Parents.FirstOrDefault();
            if (previousCommit != null)
                SelectedCommitDetail.UpdateCommit(previousCommit.MessageShort, previousCommit.Author.When.DateTime);
            else
                SelectedCommitDetail.Clear();

            IsGoButtonEnabled = true;

            // Update commit list using backend (includes RemoteState computation)
            _allCommits.Clear();
            var commitInfos = await _gitBackend.GetCommitsAsync();
            foreach (var c in commitInfos)
            {
                _allCommits.Add(new CommitItem(
                    c.Message,
                    c.Date,
                    c.Sha,
                    c.AuthorName,
                    c.AuthorEmail,
                    c.RemoteState,
                    c.FullSha,
                    c.OrphanedPairSha));
            }

            // Refresh author directory from loaded commits
            _ = _authorDirService.RefreshAsync();

            // Detect whether the current branch has an upstream tracking branch
            _hasTrackingBranch = repo.Head?.TrackedBranch?.Tip != null;

            // Apply filters if any are active, otherwise show all commits
            if (Filter.HasActiveFilters())
            {
                ApplyFilters();
            }
            else
            {
                Commits = _allCommits.ToList();

                // Update filter status
                UpdateFilterStatus();

                // Feed CommitListViewModel (handles auto-select and live text filter)
                CommitListVM.SetBaseCommits(Commits, HasActiveFilters, FilterStatusText, _hasTrackingBranch);
            }

            // Update remotes list
            Remotes.Clear();
            foreach (var remote in repo.Network.Remotes)
            {
                Remotes.Add(remote.Name);
            }

            // Auto-select the first remote if available
            if (Remotes.Count > 0 && string.IsNullOrEmpty(SelectedRemote))
            {
                SelectedRemote = Remotes[0];
            }

            // Update status bar information
            UpdateStatusBar(repo);

            // Load stashes, branches and worktrees
            await StashesVM.LoadAsync();
            await BranchesVM.LoadAsync();
            await WorktreesVM.LoadAsync();
            SidebarVM.BranchCount = BranchesVM.LocalCount;
        }
        catch (Exception)
        {
            StashesVM.Clear();
            BranchesVM.Clear();
            WorktreesVM.Clear();
            // Empty all the fields
            CurrentCommitDetail.Clear();
            SelectedCommitDetail.Clear();
            SelectedDate = null;
            TimestampEditVM.UpdatePreviewBefore("—");

            IsGoButtonEnabled = false;

            Remotes.Clear();

            // Clear status bar
            TitleBarVM.Clear();
            CommitListVM.SetBaseCommits([], false, string.Empty, hasTrackingBranch: false);
        }

        await RefreshSidebarBadgesAsync();
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

    private async Task TryAttachRepositoryServicesAsync()
    {
        try
        {
            await _gitBackend.OpenAsync(Path);
            _ = _stateService.AttachAsync(Path);
            _ = _opsLogService.AttachAsync(Path);
            _ = _snapshotService.AttachAsync(Path);
            _ = _stashNameService.AttachAsync(Path);
            _customToolsService.Attach(Path);
        }
        catch
        {
            // ignore invalid path while user edits
        }
    }

    private void OnRepositoryStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(RepositoryStateService.CurrentBranch))
            return;

        if (string.IsNullOrWhiteSpace(_stateService.CurrentBranch))
            return;

        TitleBarVM.CurrentBranch = _stateService.CurrentBranch;
    }
}
