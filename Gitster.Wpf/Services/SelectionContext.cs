using CommunityToolkit.Mvvm.ComponentModel;

using Gitster.ApplicationLayer;

namespace Gitster.Services;

public interface ISelectionContext
{
    CommitItem? SelectedCommit { get; set; }
    IReadOnlyList<CommitItem> SelectedCommits { get; set; }
    DateTime? CurrentCommitDate { get; set; }
    string? CurrentBranch { get; set; }
}

public partial class SelectionContext : ObservableObject, ISelectionContext
{
    [ObservableProperty]
    public partial CommitItem? SelectedCommit { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<CommitItem> SelectedCommits { get; set; } = [];

    [ObservableProperty]
    public partial DateTime? CurrentCommitDate { get; set; }

    [ObservableProperty]
    public partial string? CurrentBranch { get; set; }
}
