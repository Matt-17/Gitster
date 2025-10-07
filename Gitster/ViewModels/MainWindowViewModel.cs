using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibGit2Sharp;

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
    public partial CommitDetailViewModel PreviousCommitDetail { get; set; }

    [ObservableProperty]
    public partial CommitDetailViewModel CurrentCommitDetail { get; set; }

    public ObservableCollection<CommitItem> Commits { get; } = [];

    public MainWindowViewModel()
    {
        PreviousCommitDetail = new CommitDetailViewModel();
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
            PreviousCommitDetail.UpdateCommit(value.Message, value.Date);
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
            var committer = commit.Committer;

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
    private void ReadPreviousCommitTime()
    {
        try
        {
            using var repo = new Repository(Path);

            var commit = repo.Head.Tip.Parents.First();
            var author = commit.Author;
            
            SelectedDate = new DateTime(author.When.Year, author.When.Month, author.When.Day);
            HoursValue = author.When.Hour;
            MinutesValue = author.When.Minute;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error reading previous commit: {ex.Message}");
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

    public void UpdateElements()
    {
        try
        {
            using var repo = new Repository(Path);

            var headTip = repo.Head.Tip;
            CurrentCommitDetail.UpdateCommit(
                headTip.MessageShort,
                headTip.Author.When.ToString(@"dd.MM.yyyy \u\m HH:mm")
            );

            var previousCommit = headTip.Parents.First();
            PreviousCommitDetail.UpdateCommit(
                previousCommit.MessageShort,
                previousCommit.Author.When.ToString(@"dd.MM.yyyy \u\m HH:mm")
            );

            IsGoButtonEnabled = true;

            // Update commit list
            Commits.Clear();
            foreach (var c in repo.Commits)
            {
                Commits.Add(new CommitItem(
                    c.MessageShort,
                    c.Author.When.ToString(@"dd.MM.yyyy HH:mm"),
                    c.Id.Sha.Substring(0, 7)
                ));
            }
        }
        catch (Exception)
        {
            // Empty all the fields
            CurrentCommitDetail.Clear();
            PreviousCommitDetail.Clear();
            SelectedDate = null;

            MonthsValue = 1;
            DaysValue = 1;
            HoursValue = 0;
            MinutesValue = 0;

            IsGoButtonEnabled = false;

            Commits.Clear();
        }
    }
}
