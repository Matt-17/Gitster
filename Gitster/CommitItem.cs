using CommunityToolkit.Mvvm.ComponentModel;

using Gitster.Services.Git;

namespace Gitster;

/// <summary>
/// A row in the commit list. Observable so that <see cref="RemoteState"/> and
/// <see cref="OrphanedPairSha"/> can be filled in progressively after the initial
/// fast load (plan A0.4 — remote-state dots appear without blocking first paint).
/// </summary>
public partial class CommitItem : ObservableObject
{
    public CommitItem(
        string message,
        DateTime date,
        string commitId,
        string authorName,
        string authorEmail = "",
        CommitRemoteState remoteState = CommitRemoteState.LocalOnly,
        string fullSha = "",
        string? orphanedPairSha = null)
    {
        Message = message;
        Date = date;
        CommitId = commitId;
        AuthorName = authorName;
        AuthorEmail = authorEmail;
        FullSha = string.IsNullOrEmpty(fullSha) ? commitId : fullSha;
        RemoteState = remoteState;
        OrphanedPairSha = orphanedPairSha;
    }

    public string Message { get; }
    public DateTime Date { get; }
    public string CommitId { get; }
    public string AuthorName { get; }
    public string AuthorEmail { get; }
    public string FullSha { get; }

    [ObservableProperty]
    public partial CommitRemoteState RemoteState { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOrphanedPair))]
    public partial string? OrphanedPairSha { get; set; }

    /// <summary>True when this commit and its orphaned pair (same tree, rewritten) are both visible.</summary>
    public bool IsOrphanedPair => OrphanedPairSha != null;
}
