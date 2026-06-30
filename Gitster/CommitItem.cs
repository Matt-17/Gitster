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

    public string DisplayMessage => PendingMessage ?? Message;
    public DateTime DisplayDate => PendingDate ?? Date;
    public string DisplayAuthorName => PendingAuthorName ?? AuthorName;
    public string DisplayAuthorEmail => PendingAuthorEmail ?? AuthorEmail;

    public bool HasHistoryEditOverlay => IsHistoryEditDirect || IsHistoryEditTransitive;
    public bool HasAnyHistoryEditIcon =>
        HasPendingMessageChange
        || HasPendingAuthorChange
        || HasPendingTimeChange
        || HasPendingFileChange
        || IsHistoryEditTransitive;

    [ObservableProperty]
    public partial CommitRemoteState RemoteState { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOrphanedPair))]
    public partial string? OrphanedPairSha { get; set; }

    /// <summary>True when this commit and its orphaned pair (same tree, rewritten) are both visible.</summary>
    public bool IsOrphanedPair => OrphanedPairSha != null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayMessage))]
    public partial string? PendingMessage { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayDate))]
    public partial DateTime? PendingDate { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayAuthorName))]
    public partial string? PendingAuthorName { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayAuthorEmail))]
    public partial string? PendingAuthorEmail { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasHistoryEditOverlay))]
    [NotifyPropertyChangedFor(nameof(HasAnyHistoryEditIcon))]
    public partial bool IsHistoryEditDirect { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasHistoryEditOverlay))]
    [NotifyPropertyChangedFor(nameof(HasAnyHistoryEditIcon))]
    public partial bool IsHistoryEditTransitive { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAnyHistoryEditIcon))]
    public partial bool HasPendingMessageChange { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAnyHistoryEditIcon))]
    public partial bool HasPendingAuthorChange { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAnyHistoryEditIcon))]
    public partial bool HasPendingTimeChange { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAnyHistoryEditIcon))]
    public partial bool HasPendingFileChange { get; set; }

    [ObservableProperty]
    public partial bool IsPendingRemoteRisk { get; set; }

    [ObservableProperty]
    public partial bool IsPendingLocalCleanup { get; set; }

    [ObservableProperty]
    public partial string HistoryEditTooltip { get; set; } = string.Empty;

    public void ClearHistoryEditOverlay()
    {
        PendingMessage = null;
        PendingDate = null;
        PendingAuthorName = null;
        PendingAuthorEmail = null;
        IsHistoryEditDirect = false;
        IsHistoryEditTransitive = false;
        HasPendingMessageChange = false;
        HasPendingAuthorChange = false;
        HasPendingTimeChange = false;
        HasPendingFileChange = false;
        IsPendingRemoteRisk = false;
        IsPendingLocalCleanup = false;
        HistoryEditTooltip = string.Empty;
    }
}
