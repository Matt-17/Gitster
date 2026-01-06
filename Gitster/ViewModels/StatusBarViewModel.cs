using CommunityToolkit.Mvvm.ComponentModel;

namespace Gitster.ViewModels;

/// <summary>
/// View model for the status bar displaying repository state information.
/// </summary>
public partial class StatusBarViewModel : BaseViewModel
{
    [ObservableProperty]
    public partial string CurrentBranch { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string RepositoryName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial int IncomingCount { get; set; }

    [ObservableProperty]
    public partial int OutgoingCount { get; set; }

    [ObservableProperty]
    public partial bool HasIncomingChanges { get; set; }

    [ObservableProperty]
    public partial bool HasOutgoingChanges { get; set; }

    /// <summary>
    /// Updates the status bar with repository information.
    /// </summary>
    public void UpdateStatus(string branch, string repoName, int incoming, int outgoing)
    {
        CurrentBranch = branch;
        RepositoryName = repoName;
        IncomingCount = incoming;
        OutgoingCount = outgoing;
        HasIncomingChanges = incoming > 0;
        HasOutgoingChanges = outgoing > 0;
    }

    /// <summary>
    /// Clears all status bar information.
    /// </summary>
    public void Clear()
    {
        CurrentBranch = string.Empty;
        RepositoryName = string.Empty;
        IncomingCount = 0;
        OutgoingCount = 0;
        HasIncomingChanges = false;
        HasOutgoingChanges = false;
    }
}
