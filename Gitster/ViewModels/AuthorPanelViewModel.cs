using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using System.Windows;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Gitster.Models;
using Gitster.Services;
using Gitster.Services.Git;
using Gitster.Views;

namespace Gitster.ViewModels;

public partial class AuthorPanelViewModel : BaseViewModel
{
    private readonly IGitBackend _git;
    private readonly AuthorDirectoryService _authorDir;

    [ObservableProperty]
    public partial ObservableCollection<AuthorEntry> Authors { get; set; } = [];

    [ObservableProperty]
    public partial string AuthorText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string CommitterText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool SyncAuthorCommitter { get; set; } = true;

    public bool IsCommitterEnabled => !SyncAuthorCommitter;

    [ObservableProperty]
    public partial bool IsApplyEnabled { get; set; }

    public AuthorPanelViewModel(IGitBackend git, AuthorDirectoryService authorDir)
    {
        _git = git;
        _authorDir = authorDir;
        Authors = authorDir.Authors;
        authorDir.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AuthorDirectoryService.Authors))
                Authors = authorDir.Authors;
        };
    }

    partial void OnAuthorTextChanged(string value)
    {
        if (SyncAuthorCommitter)
            CommitterText = value;
    }

    partial void OnSyncAuthorCommitterChanged(bool value)
    {
        OnPropertyChanged(nameof(IsCommitterEnabled));
        if (value) CommitterText = AuthorText;
    }

    public async Task LoadFromCommitAsync(CommitItem? item)
    {
        IsApplyEnabled = false;
        if (item == null)
        {
            AuthorText = string.Empty;
            CommitterText = string.Empty;
            return;
        }

        try
        {
            var details = await _git.GetCommitAsync(item.CommitId);
            AuthorText    = FormatAuthor(details.AuthorName,    details.AuthorEmail);
            CommitterText = FormatAuthor(details.CommitterName, details.CommitterEmail);
            IsApplyEnabled = true;
        }
        catch
        {
            AuthorText    = string.Empty;
            CommitterText = string.Empty;
        }
    }

    [RelayCommand]
    private async Task Apply()
    {
        var (authorName, authorEmail)       = ParseAuthor(AuthorText);
        var (committerName, committerEmail) = ParseAuthor(CommitterText);

        var request = new AmendAuthorRequest(
            AuthorName:    authorName,
            AuthorEmail:   authorEmail,
            CommitterName:  committerName,
            CommitterEmail: committerEmail);

        try
        {
            await _git.AmendAuthorAsync(request);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error amending author: {ex.Message}", "Gitster",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void AddAuthor()
    {
        var dialog = new AddAuthorDialog { Owner = Application.Current.MainWindow };
        if (dialog.ShowDialog() == true && dialog.Result is { } entry)
        {
            _authorDir.Authors.Add(entry);
            AuthorText = entry.DisplayName;
        }
    }

    private static string FormatAuthor(string name, string email) =>
        string.IsNullOrEmpty(email) ? name : $"{name} <{email}>";

    private static (string name, string email) ParseAuthor(string s)
    {
        var m = Regex.Match(s, @"^(.+?)\s*<([^>]*)>\s*$");
        return m.Success
            ? (m.Groups[1].Value.Trim(), m.Groups[2].Value.Trim())
            : (s.Trim(), string.Empty);
    }
}
