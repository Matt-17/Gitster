using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibGit2Sharp;
using Microsoft.Win32;

namespace Gitster.ViewModels;

/// <summary>
/// View model for the main window.
/// </summary>
public partial class MainWindowViewModel : BaseViewModel
{
    [ObservableProperty]
    public partial string Path { get; set; } = string.Empty;

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
    public partial StatusBarViewModel StatusBar { get; set; }

    public ObservableCollection<CommitItem> Commits { get; } = [];
    public ObservableCollection<string> Remotes { get; } = [];
    
    public CommitFilter Filter { get; } = new();
    
    private ObservableCollection<CommitItem> _allCommits = [];
    private FilterWindow? _filterWindow;

    public MainWindowViewModel()
    {
        SelectedCommitDetail = new CommitDetailViewModel();
        CurrentCommitDetail = new CommitDetailViewModel();
        StatusBar = new StatusBarViewModel();
        
        // Subscribe to filter changes
        Filter.PropertyChanged += (s, e) => 
        {
            ApplyFilters();
        };
        
        // Initialize with current date/time
        SelectedDate = DateTime.Now;
        
        // Load saved path or use default
        Path = Properties.Settings.Default.Path ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        FolderPath = Path;
    }

    partial void OnFolderPathChanged(string value)
    {
        Path = value;
        UpdateSettingsPath();
        UpdateElements();
    }

    partial void OnSelectedCommitChanged(CommitItem? value)
    {
        if (value != null)
        {
            SelectedCommitDetail.UpdateCommit(value.Message, value.Date);
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
    private void AmendCommit()
    {
        try
        {
            using var repo = new Repository(Path);

            if (SelectedDate == null)
            {
                MessageBox.Show("Please select a date");
                return;
            }

            var commit = repo.Head.Tip;
            var author = commit.Author;
            var committer = commit.Author;

            var currentTimezoneOffset = DateTimeOffset.Now.Offset;

            var year = SelectedDate.Value.Year;
            var month = SelectedDate.Value.Month;
            var day = SelectedDate.Value.Day;
            var hour = SelectedDate.Value.Hour;
            var minute = SelectedDate.Value.Minute;
            
            var newAuthor = new Signature(author.Name, author.Email, 
                new DateTimeOffset(year, month, day, hour, minute, author.When.Second, currentTimezoneOffset));
            var newCommiter = new Signature(committer.Name, committer.Email, 
                new DateTimeOffset(year, month, day, hour, minute, committer.When.Second, currentTimezoneOffset));
            
            var commitOptions = new CommitOptions
            {
                AmendPreviousCommit = true,
            };

            repo.Commit(commit.Message, newAuthor, newCommiter, commitOptions);

            UpdateElements();
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
    private void Fetch(string? remoteName)
    {
        try
        {
            using var repo = new Repository(Path);
            var remote = string.IsNullOrEmpty(remoteName) ? repo.Network.Remotes.FirstOrDefault() : repo.Network.Remotes[remoteName];
            
            if (remote == null)
            {
                MessageBox.Show("No remote found");
                return;
            }

            var refSpecs = remote.FetchRefSpecs.Select(x => x.Specification);
            Commands.Fetch(repo, remote.Name, refSpecs, null, $"Fetch from {remote.Name}");
            
            MessageBox.Show($"Fetched from {remote.Name} successfully");
            UpdateElements();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error fetching: {ex.Message}");
        }
    }

    [RelayCommand]
    private void Pull(string? remoteName)
    {
        try
        {
            using var repo = new Repository(Path);
            var remote = string.IsNullOrEmpty(remoteName) ? repo.Network.Remotes.FirstOrDefault() : repo.Network.Remotes[remoteName];
            
            if (remote == null)
            {
                MessageBox.Show("No remote found");
                return;
            }

            var signature = repo.Config.BuildSignature(DateTimeOffset.Now);
            var pullOptions = new PullOptions();

            Commands.Pull(repo, signature, pullOptions);
            
            MessageBox.Show($"Pulled from {remote.Name} successfully");
            UpdateElements();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error pulling: {ex.Message}");
        }
    }

    [RelayCommand]
    private void Push(string? remoteName)
    {
        try
        {
            using var repo = new Repository(Path);
            var remote = string.IsNullOrEmpty(remoteName) ? repo.Network.Remotes.FirstOrDefault() : repo.Network.Remotes[remoteName];
            
            if (remote == null)
            {
                MessageBox.Show("No remote found");
                return;
            }

            var pushOptions = new PushOptions();
            repo.Network.Push(repo.Head, pushOptions);
            
            MessageBox.Show($"Pushed to {remote.Name} successfully");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error pushing: {ex.Message}");
        }
    }

    [RelayCommand]
    private void Sync(string? remoteName)
    {
        try
        {
            using var repo = new Repository(Path);
            var remote = string.IsNullOrEmpty(remoteName) ? repo.Network.Remotes.FirstOrDefault() : repo.Network.Remotes[remoteName];
            
            if (remote == null)
            {
                MessageBox.Show("No remote found");
                return;
            }

            // First fetch
            var refSpecs = remote.FetchRefSpecs.Select(x => x.Specification);
            Commands.Fetch(repo, remote.Name, refSpecs, null, $"Fetch from {remote.Name}");

            // Then pull
            var signature = repo.Config.BuildSignature(DateTimeOffset.Now);
            var pullOptions = new PullOptions();
            Commands.Pull(repo, signature, pullOptions);

            // Finally push
            var pushOptions = new PushOptions();
            repo.Network.Push(repo.Head, pushOptions);
            
            MessageBox.Show($"Synced with {remote.Name} successfully");
            UpdateElements();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error syncing: {ex.Message}");
        }
    }

    public void OnWindowActivated()
    {
        UpdateElements();
    }

    private void UpdateSettingsPath()
    {
        var settings = Properties.Settings.Default;
        settings.Path = Path;
        settings.Save();
    }

    private void ApplyFilters()
    {
        Commits.Clear();

        var filteredCommits = _allCommits.AsEnumerable();

        // Apply author filter
        if (!string.IsNullOrEmpty(Filter.SelectedAuthorName) 
            && Filter.SelectedAuthorName != "All")
        {
            filteredCommits = filteredCommits.Where(c => 
                c.AuthorName == Filter.SelectedAuthorName);
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

        foreach (var commit in filteredCommits)
        {
            Commits.Add(commit);
        }

        // Auto-select commit
        AutoSelectCommit();

        // Update filter status
        UpdateFilterStatus();
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

    public void UpdateElements()
    {
        try
        {
            using var repo = new Repository(Path);

            var headTip = repo.Head.Tip;
            CurrentCommitDetail.UpdateCommit(
                headTip.MessageShort,
                headTip.Author.When.DateTime
            );

            var previousCommit = headTip.Parents.First();
            SelectedCommitDetail.UpdateCommit(
                previousCommit.MessageShort,
                previousCommit.Author.When.DateTime
            );

            IsGoButtonEnabled = true;

            // Update commit list
            Commits.Clear();
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
                // No filters, show all commits
                foreach (var commit in _allCommits)
                {
                    Commits.Add(commit);
                }

                // Auto-select commit
                AutoSelectCommit();

                // Update filter status
                UpdateFilterStatus();
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

            IsGoButtonEnabled = false;

            Commits.Clear();
            Remotes.Clear();

            // Clear status bar
            StatusBar.Clear();
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

            // Update status bar with all values at once
            StatusBar.UpdateStatus(branch, repoName, incoming, outgoing);
        }
        catch (Exception)
        {
            // If there's an error, just clear the status bar
            StatusBar.Clear();
        }
    }
}
