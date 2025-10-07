using CommunityToolkit.Mvvm.ComponentModel;

namespace Gitster.ViewModels;

/// <summary>
/// View model for displaying commit details.
/// </summary>
public partial class CommitDetailViewModel : BaseViewModel
{
    [ObservableProperty]
    private string _commitMessage = string.Empty;

    [ObservableProperty]
    private string _commitDate = string.Empty;

    /// <summary>
    /// Updates the commit details.
    /// </summary>
    public void UpdateCommit(string message, string date)
    {
        CommitMessage = message;
        CommitDate = date;
    }

    /// <summary>
    /// Clears the commit details.
    /// </summary>
    public void Clear()
    {
        CommitMessage = string.Empty;
        CommitDate = string.Empty;
    }
}
