using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Gitster.Core.Models;
using Gitster.Services;
using Gitster.ApplicationLayer;
using Gitster.Core.Git;
using Gitster.Core.History;

namespace Gitster.ViewModels;

public partial class AuthorRepairViewModel : BaseViewModel
{
    private readonly IGitBackend _git;
    private readonly CommitHistoryService _history;
    private readonly IWindowService _windowService;

    public event Action? RewriteCompleted;

    [ObservableProperty]
    public partial ObservableCollection<AuthorEntry> Authors { get; set; }

    [ObservableProperty]
    public partial AuthorEntry? FindAuthor { get; set; }

    [ObservableProperty]
    public partial AuthorEntry? ReplaceAuthor { get; set; }

    [ObservableProperty]
    public partial bool AlsoUpdateCommitter { get; set; }

    [ObservableProperty]
    public partial List<CommitInfo> AffectedCommits { get; set; } = [];

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial bool HasUnsafeCommits { get; set; }

    [ObservableProperty]
    public partial bool IsRewriteEnabled { get; set; }

    public AuthorRepairViewModel(
        IGitBackend git,
        CommitHistoryService history,
        IEnumerable<AuthorEntry> authors,
        IWindowService? windowService = null)
    {
        _git = git;
        _history = history;
        _windowService = windowService ?? new WindowService();
        Authors = new ObservableCollection<AuthorEntry>(authors);
    }

    partial void OnFindAuthorChanged(AuthorEntry? value)
    {
        _ = LoadAffectedCommitsAsync();
        UpdateRewriteEnabled();
    }

    partial void OnReplaceAuthorChanged(AuthorEntry? value) => UpdateRewriteEnabled();
    partial void OnIsLoadingChanged(bool value) => UpdateRewriteEnabled();
    partial void OnAffectedCommitsChanged(List<CommitInfo> value) => UpdateRewriteEnabled();

    private void UpdateRewriteEnabled() =>
        IsRewriteEnabled = FindAuthor != null && ReplaceAuthor != null
            && !ReferenceEquals(FindAuthor, ReplaceAuthor)
            && FindAuthor.DisplayName != ReplaceAuthor.DisplayName
            && AffectedCommits.Count > 0
            && !IsLoading;

    private async Task LoadAffectedCommitsAsync()
    {
        if (FindAuthor == null)
        {
            AffectedCommits = [];
            HasUnsafeCommits = false;
            return;
        }

        IsLoading = true;
        try
        {
            var affected = await _history.GetCommitsByAuthorAsync(FindAuthor);

            AffectedCommits  = affected.ToList();
            HasUnsafeCommits = affected.Any(c => c.RemoteState == CommitRemoteState.OnRemote);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task Rewrite()
    {
        if (!IsRewriteEnabled) return;

        var replace      = ReplaceAuthor!;
        var alsoCommitter = AlsoUpdateCommitter;

        var rewrites = AffectedCommits.Select(c => new CommitRewrite(
            c.Sha,
            NewAuthorName:     replace.Name,
            NewAuthorEmail:    replace.Email,
            NewCommitterName:  alsoCommitter ? replace.Name  : null,
            NewCommitterEmail: alsoCommitter ? replace.Email : null)).ToList();

        try
        {
            await _git.RewriteCommitsAsync(rewrites);
            RewriteCompleted?.Invoke();
        }
        catch (Exception ex)
        {
            _windowService.Error($"Error rewriting commits: {ex.Message}", "Gitster");
        }
    }
}
