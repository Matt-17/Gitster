using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Gitster.ViewModels;

/// <summary>
/// View model for displaying commit details.
/// </summary>
public partial class CommitDetailViewModel : BaseViewModel
{
    [ObservableProperty]
    public partial string CommitMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial DateTime? CommitDate { get; set; }

    /// <summary>
    /// Updates the commit details.
    /// </summary>
    public void UpdateCommit(string message, DateTime date)
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
        CommitDate = null;
    }
}
