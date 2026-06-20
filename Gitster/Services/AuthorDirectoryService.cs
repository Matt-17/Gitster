using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;

using Gitster.Models;
using Gitster.Services.History;

namespace Gitster.Services;

public partial class AuthorDirectoryService : ObservableObject
{
    private readonly CommitHistoryService _history;

    [ObservableProperty]
    public partial ObservableCollection<AuthorEntry> Authors { get; set; } = [];

    public AuthorDirectoryService(CommitHistoryService history) => _history = history;

    public async Task RefreshAsync()
    {
        var authors = await _history.GetAuthorsAsync();
        Authors = new ObservableCollection<AuthorEntry>(authors);
    }

    public void Clear() => Authors = [];
}
