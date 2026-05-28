using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Gitster.Models;

namespace Gitster.ViewModels;

public partial class SidebarViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(
        nameof(IsCommitsActive),
        nameof(IsStashesActive),
        nameof(IsBranchesActive),
        nameof(IsWorktreesActive),
        nameof(IsSearchActive),
        nameof(IsOperationsLogActive))]
    public partial AppMode CurrentMode { get; set; } = AppMode.Commits;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStashBadge))]
    public partial int StashCount { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOpsLogBadge))]
    public partial int ActiveOpsCount { get; set; }

    public bool IsCommitsActive       => CurrentMode == AppMode.Commits;
    public bool IsStashesActive       => CurrentMode == AppMode.Stashes;
    public bool IsBranchesActive      => CurrentMode == AppMode.Branches;
    public bool IsWorktreesActive     => CurrentMode == AppMode.Worktrees;
    public bool IsSearchActive        => CurrentMode == AppMode.Search;
    public bool IsOperationsLogActive => CurrentMode == AppMode.OperationsLog;
    public bool HasStashBadge         => StashCount > 0;
    public bool HasOpsLogBadge        => ActiveOpsCount > 0;

    [RelayCommand]
    private void SelectMode(AppMode mode) => CurrentMode = mode;
}
