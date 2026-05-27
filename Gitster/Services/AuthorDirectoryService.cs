using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;

using Gitster.Models;
using Gitster.Services.Git;

namespace Gitster.Services;

public partial class AuthorDirectoryService : ObservableObject
{
    private readonly IGitBackend _git;

    [ObservableProperty]
    public partial ObservableCollection<AuthorEntry> Authors { get; set; } = [];

    public AuthorDirectoryService(IGitBackend git) => _git = git;

    public async Task RefreshAsync()
    {
        var commits = await _git.GetCommitsAsync();

        var seen = new Dictionary<string, AuthorEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in commits)
        {
            if (string.IsNullOrWhiteSpace(c.AuthorName)) continue;
            var key = $"{c.AuthorName}|{c.AuthorEmail}".ToLowerInvariant();
            if (!seen.ContainsKey(key))
                seen[key] = new AuthorEntry(c.AuthorName, c.AuthorEmail);
        }

        Authors = new ObservableCollection<AuthorEntry>(
            seen.Values.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase));
    }

    public void Clear() => Authors = [];
}
