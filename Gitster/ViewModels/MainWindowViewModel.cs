using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Gitster.Services;
using Gitster.Services.Git;

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
    private readonly OperationsLog _operationsLog = new();
    private readonly IGitBackend _gitBackend;
    private readonly RepositoryStateService _stateService;
    private readonly OperationFeedbackService _feedbackService;

    public TitleBarViewModel TitleBarVM { get; }
    public CommitListViewModel CommitListVM { get; }
    public TimestampEditViewModel TimestampEditVM { get; }
    public QuickActionsViewModel QuickActionsVM { get; }
    public UndoBarViewModel UndoBarVM { get; }
    public StatusBarViewModel StatusBarVM { get; }
    public AutoFetchService AutoFetch { get; }

    public MainWindowViewModel()
    {
        _gitBackend = new LibGit2Backend();
        _stateService = new RepositoryStateService(_gitBackend);
        _feedbackService = new OperationFeedbackService();
        AutoFetch = new AutoFetchService(_gitBackend);

        SelectedCommitDetail = new CommitDetailViewModel();
        CurrentCommitDetail = new CommitDetailViewModel();
        StatusBarVM = new StatusBarViewModel(_stateService, _feedbackService);
        TitleBarVM = new TitleBarViewModel(BrowseFolder, AutoFetch);
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
        QuickActionsVM = new QuickActionsViewModel();
        UndoBarVM = new UndoBarViewModel(_operationsLog);

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
            var patch = repo.Diff.Compare<Patch>(parent?.Tree, gitCommit.Tree);
            var files = patch
                .Select(e => new DiffFileEntry(e.Path, e.LinesAdded, e.LinesDeleted))
                .ToList();
            var header = $"{files.Count} {(files.Count == 1 ? "file" : "files")} · +{patch.LinesAdded} −{patch.LinesDeleted}";
            CommitListVM.UpdateDiff(header, files);
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
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error opening folder dialog: {ex.Message}");
        }
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

            var amendedSha = await _feedbackService.RunAsync(
                "Amend",
                () => _gitBackend.AmendAsync(new AmendRequest(editDate.Value)),
                sha => sha.Length > 7 ? sha[..7] : sha);

            var shortSha = amendedSha.Length >= 6 ? amendedSha[..6] : amendedSha;
            _operationsLog.Record(new OperationRecord(
                $"Amend of {shortSha}",
                shortSha,
                DateTime.Now,
                () => Task.CompletedTask));

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
            var author = commit.Author;

            SelectedDate = author.When.DateTime;
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
            await _feedbackService.RunAsync("Push", () => _gitBackend.PushAsync(selectedRemote));
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
        CommitListVM.SetBaseCommits(Commits, HasActiveFilters, FilterStatusText);
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

            var headTip = repo.Head.Tip;
            CurrentCommitDetail.UpdateCommit(
                headTip.MessageShort,
                headTip.Author.When.DateTime
            );
            var headSha = headTip.Id.Sha.Length >= 6 ? headTip.Id.Sha[..6] : headTip.Id.Sha;
            TimestampEditVM.UpdatePreviewBefore($"{headTip.Author.When.DateTime:dd.MM. HH:mm} · {headSha}");

            var previousCommit = headTip.Parents.First();
            SelectedCommitDetail.UpdateCommit(
                previousCommit.MessageShort,
                previousCommit.Author.When.DateTime
            );

            IsGoButtonEnabled = true;

            // Update commit list
            _allCommits.Clear();
            foreach (var c in repo.Commits)
            {
                if (c.Author == null)
                {
                    continue;
                }

                var commitId = c.Id.Sha.Length >= 7 ? c.Id.Sha.Substring(0, 7) : c.Id.Sha;
                var commitItem = new CommitItem(
                    c.MessageShort,
                    c.Author.When.DateTime,
                    commitId,
                    c.Author.Name ?? string.Empty
                );
                _allCommits.Add(commitItem);
            }

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
                CommitListVM.SetBaseCommits(Commits, HasActiveFilters, FilterStatusText);
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
        }
        catch (Exception)
        {
            // Empty all the fields
            CurrentCommitDetail.Clear();
            SelectedCommitDetail.Clear();
            SelectedDate = null;
            TimestampEditVM.UpdatePreviewBefore("—");

            IsGoButtonEnabled = false;

            Remotes.Clear();

            // Clear status bar
            TitleBarVM.Clear();
            CommitListVM.SetBaseCommits([], false, string.Empty);
        }
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
            // Get current branch
            var branch = repo.Head.FriendlyName;

            // Get repository name from path
            var repoPath = repo.Info.WorkingDirectory.TrimEnd(System.IO.Path.DirectorySeparatorChar);
            var repoName = System.IO.Path.GetFileName(repoPath);

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
