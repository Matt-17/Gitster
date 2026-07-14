namespace Gitster.ApplicationLayer;

public sealed class RepositoryCommandContext
{
    public Func<Task>? BrowseFolderAsync { get; set; }
    public Action<string>? OpenRepository { get; set; }
    public Func<Task>? RefreshAllAsync { get; set; }
    public Func<Task>? RefreshSidebarBadgesAsync { get; set; }
    public Func<string?>? GetCurrentBranch { get; set; }
    public Func<string>? GetCurrentPath { get; set; }
    public Func<string?>? GetSelectedRemote { get; set; }
    public Func<string?, Task>? RefreshAfterHistoryRewriteAsync { get; set; }

    public void BrowseFolder()
    {
        if (BrowseFolderAsync is not null)
            _ = BrowseFolderAsync();
    }

    public void OpenRepositoryPath(string path) => OpenRepository?.Invoke(path);

    public Task RefreshAll() => RefreshAllAsync?.Invoke() ?? Task.CompletedTask;

    public Task RefreshSidebarBadges() => RefreshSidebarBadgesAsync?.Invoke() ?? Task.CompletedTask;

    public string CurrentBranch => GetCurrentBranch?.Invoke() ?? string.Empty;

    public string CurrentPath => GetCurrentPath?.Invoke() ?? string.Empty;

    public string? SelectedRemote => GetSelectedRemote?.Invoke();

    public Task RefreshAfterHistoryRewrite(string? preferredSelectionSha) =>
        RefreshAfterHistoryRewriteAsync?.Invoke(preferredSelectionSha) ?? Task.CompletedTask;
}
