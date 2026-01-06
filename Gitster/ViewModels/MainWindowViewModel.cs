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
    public partial string TimeText { get; set; } = "00:00";

    [ObservableProperty]
    public partial DateTime? SelectedDate { get; set; }

    [ObservableProperty]
    public partial double HoursValue { get; set; }

    [ObservableProperty]
    public partial double MinutesValue { get; set; }

    [ObservableProperty]
    public partial double DaysValue { get; set; }

    [ObservableProperty]
    public partial double MonthsValue { get; set; }

    [ObservableProperty]
    public partial double DaysMaximum { get; set; } = 31;

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

    public ObservableCollection<CommitItem> Commits { get; } = [];
    public ObservableCollection<string> Remotes { get; } = [];
    
    private ObservableCollection<CommitItem> _allCommits = [];
    private FilterWindowViewModel? _filterViewModel;
    private FilterWindow? _filterWindow;

    public MainWindowViewModel()
    {
        SelectedCommitDetail = new CommitDetailViewModel();
        CurrentCommitDetail = new CommitDetailViewModel();
        
        // Initialize with current date/time
        SelectedDate = DateTime.Now;
        HoursValue = DateTime.Now.Hour;
        MinutesValue = DateTime.Now.Minute;
        DaysValue = DateTime.Now.Day;
        MonthsValue = DateTime.Now.Month;
        
        // Load saved path or use default
        Path = Properties.Settings.Default.Path ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        FolderPath = Path;
        
        UpdateTimeText();
    }

    partial void OnFolderPathChanged(string value)
    {
        Path = value;
        UpdateSettingsPath();
        UpdateElements();
    }

    partial void OnHoursValueChanged(double value)
    {
        UpdateTimeText();
    }

    partial void OnMinutesValueChanged(double value)
    {
        UpdateTimeText();
    }

    partial void OnSelectedDateChanged(DateTime? value)
    {
        if (value == null)
        {
            return;
        }

        var currentDate = value.Value;
        DaysMaximum = DateTime.DaysInMonth(currentDate.Year, currentDate.Month);
        DaysValue = Math.Min(DaysValue, DaysMaximum);
    }

    partial void OnDaysValueChanged(double value)
    {
        UpdateDateFromSliders();
    }

    partial void OnMonthsValueChanged(double value)
    {
        UpdateDateFromSliders();
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

            if (_filterViewModel == null)
            {
                _filterViewModel = new FilterWindowViewModel();
            }

            // Populate author names from all commits
            _filterViewModel.AuthorNames.Clear();
            _filterViewModel.AuthorNames.Add("All");
            
            var distinctAuthors = _allCommits
                .Select(c => c.AuthorName)
                .Where(name => !string.IsNullOrEmpty(name))
                .Distinct()
                .OrderBy(name => name);
            
            foreach (var author in distinctAuthors)
            {
                _filterViewModel.AuthorNames.Add(author);
            }

            // Set default selection to "All" if no author is selected
            if (string.IsNullOrEmpty(_filterViewModel.SelectedAuthorName))
            {
                _filterViewModel.SelectedAuthorName = "All";
            }

            _filterWindow = new FilterWindow(_filterViewModel)
            {
                Owner = Application.Current.MainWindow
            };

            // Subscribe to FiltersApplied event
            _filterWindow.FiltersApplied += (sender, e) => 
            {
                ApplyFilters();
                _filterViewModel.SaveAppliedState();
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
        if (_filterViewModel != null)
        {
            _filterViewModel.ClearAllFilters();
            ApplyFilters();
            _filterViewModel.SaveAppliedState();
        }
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
            var hour = (int)HoursValue;
            var minute = (int)MinutesValue;
            
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
            SelectedDate = new DateTime(SelectedCommit.Date.Year, SelectedCommit.Date.Month, SelectedCommit.Date.Day);
            HoursValue = SelectedCommit.Date.Hour;
            MinutesValue = SelectedCommit.Date.Minute;
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

            SelectedDate = new DateTime(author.When.Year, author.When.Month, author.When.Day);
            HoursValue = author.When.Hour;
            MinutesValue = author.When.Minute;
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

    private void UpdateTimeText()
    {
        TimeText = $"{(int)HoursValue:00}:{(int)MinutesValue:00}";
    }

    private void UpdateDateFromSliders()
    {
        if (SelectedDate == null)
        {
            return;
        }

        try
        {
            var months = (int)MonthsValue;
            var daysSliderMaximum = DateTime.DaysInMonth(SelectedDate.Value.Year, months);
            DaysValue = Math.Min(DaysValue, daysSliderMaximum);

            var days = (int)DaysValue;
            var currentDate = SelectedDate.Value;
            var newDate = new DateTime(currentDate.Year, months, days);

            SelectedDate = newDate;
        }
        catch (Exception)
        {
            // Ignore invalid date exceptions
        }
    }

    private void UpdateSettingsPath()
    {
        var settings = Properties.Settings.Default;
        settings.Path = Path;
        settings.Save();
    }

    private void ApplyFilters()
    {
        if (_filterViewModel == null)
        {
            return;
        }

        Commits.Clear();

        var filteredCommits = _allCommits.AsEnumerable();

        // Apply author filter
        if (!string.IsNullOrEmpty(_filterViewModel.SelectedAuthorName) 
            && _filterViewModel.SelectedAuthorName != "All")
        {
            filteredCommits = filteredCommits.Where(c => 
                c.AuthorName == _filterViewModel.SelectedAuthorName);
        }

        // Apply from date filter
        if (_filterViewModel.FromDate.HasValue)
        {
            var fromDate = _filterViewModel.FromDate.Value.Date;
            filteredCommits = filteredCommits.Where(c => c.Date.Date >= fromDate);
        }

        // Apply to date filter
        if (_filterViewModel.ToDate.HasValue)
        {
            // Include all commits up to the end of the selected day
            var toDateEndOfDay = _filterViewModel.ToDate.Value.Date.AddDays(1);
            filteredCommits = filteredCommits.Where(c => c.Date < toDateEndOfDay);
        }

        foreach (var commit in filteredCommits)
        {
            Commits.Add(commit);
        }

        // Auto-select the second item if available
        if (Commits.Count >= 2)
        {
            SelectedCommit = Commits[1];
        }
        else if (Commits.Count == 1)
        {
            SelectedCommit = Commits[0];
        }
        else
        {
            SelectedCommit = null;
        }

        // Update filter status
        UpdateFilterStatus();
    }

    private void UpdateFilterStatus()
    {
        if (_filterViewModel == null)
        {
            FilterStatusText = string.Empty;
            HasActiveFilters = false;
            return;
        }

        int filterCount = 0;

        if (!string.IsNullOrEmpty(_filterViewModel.SelectedAuthorName) 
            && _filterViewModel.SelectedAuthorName != "All")
        {
            filterCount++;
        }

        if (_filterViewModel.FromDate.HasValue)
        {
            filterCount++;
        }

        if (_filterViewModel.ToDate.HasValue)
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
            if (_filterViewModel != null && _filterViewModel.HasActiveFilters())
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

                // Auto-select the second item if available
                if (Commits.Count >= 2)
                {
                    SelectedCommit = Commits[1];
                }

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
        }
        catch (Exception)
        {
            // Empty all the fields
            CurrentCommitDetail.Clear();
            SelectedCommitDetail.Clear();
            SelectedDate = null;

            MonthsValue = 1;
            DaysValue = 1;
            HoursValue = 0;
            MinutesValue = 0;

            IsGoButtonEnabled = false;

            Commits.Clear();
            Remotes.Clear();
        }
    }
}
