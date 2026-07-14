using System.Collections.ObjectModel;
using System.Text.RegularExpressions;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Gitster.Core.Models;
using Gitster.Services;
using Gitster.Core;
using Gitster.Core.Git;
using Gitster.Core.Ui;

namespace Gitster.ViewModels;

public partial class AuthorPanelViewModel : BaseViewModel
{
    private readonly IGitBackend? _git;
    private readonly AuthorDirectoryService _authorDir;
    private readonly IUserInteraction _windowService;
    private readonly IDialogService _dialogs;

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

    public AuthorPanelViewModel(IGitBackend? git, AuthorDirectoryService authorDir, IUserInteraction? windowService = null, IDialogService? dialogs = null)
    {
        _git = git;
        _authorDir = authorDir;
        _windowService = windowService ?? NullUserInteraction.Instance;
        _dialogs = dialogs ?? NullDialogService.Instance;
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
        if (item == null || _git == null)
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
        if (_git == null) return;

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
            _windowService.Error($"Error amending author: {ex.Message}", "Gitster");
        }
    }

    [RelayCommand]
    private void AddAuthor()
    {
        if (_dialogs.AddAuthor() is { } entry)
        {
            _authorDir.Authors.Add(entry);
            AuthorText = entry.DisplayName;
        }
    }

    [RelayCommand]
    private void OpenEditAuthors()
    {
        if (_dialogs.EditAuthors(_authorDir) is { } selection)
        {
            AuthorText    = selection.AuthorText;
            CommitterText = selection.CommitterText;
        }
    }

    /// <summary>
    /// Returns (name, email) for the pending author, or (null, null) if no change.
    /// Used by the combined Amend command.
    /// </summary>
    public (string? name, string? email) GetPendingAuthor()
    {
        if (!IsApplyEnabled || string.IsNullOrWhiteSpace(AuthorText)) return (null, null);
        var (n, e) = ParseAuthor(AuthorText);
        return (string.IsNullOrEmpty(n) ? null : n, string.IsNullOrEmpty(e) ? null : e);
    }

    /// <summary>
    /// Returns (name, email) for the pending committer, or (null, null) if no change.
    /// Used by the combined Amend command.
    /// </summary>
    public (string? name, string? email) GetPendingCommitter()
    {
        if (!IsApplyEnabled || string.IsNullOrWhiteSpace(CommitterText)) return (null, null);
        var (n, e) = ParseAuthor(CommitterText);
        return (string.IsNullOrEmpty(n) ? null : n, string.IsNullOrEmpty(e) ? null : e);
    }

    private static string FormatAuthor(string name, string email) =>
        string.IsNullOrEmpty(email) ? name : $"{name} <{email}>";

    private static (string name, string email) ParseAuthor(string s)
        => GitIdentityFormat.Parse(s);
}
