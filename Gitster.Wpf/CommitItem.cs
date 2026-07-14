using CommunityToolkit.Mvvm.ComponentModel;

using Gitster.Core.Git;
using Gitster.Services.Features;
using Gitster.ApplicationLayer.Features;
using Gitster.Core.History;

namespace Gitster;

/// <summary>
/// A row in the commit list. Observable so that <see cref="RemoteState"/> and
/// <see cref="OrphanedPairSha"/> can be filled in progressively after the initial
/// fast load (plan A0.4 — remote-state dots appear without blocking first paint).
/// </summary>
public partial class CommitItem : ObservableObject
{
    public const int MaxVisibleRefLabels = 3;

    public CommitItem(
        string message,
        DateTime date,
        string commitId,
        string authorName,
        string authorEmail = "",
        CommitRemoteState remoteState = CommitRemoteState.LocalOnly,
        string fullSha = "",
        string? orphanedPairSha = null,
        IReadOnlyList<string>? parentShas = null,
        IReadOnlyList<CommitRefLabel>? refLabels = null)
    {
        Message = message;
        Date = date;
        CommitId = commitId;
        AuthorName = authorName;
        AuthorEmail = authorEmail;
        FullSha = string.IsNullOrEmpty(fullSha) ? commitId : fullSha;
        RemoteState = remoteState;
        OrphanedPairSha = orphanedPairSha;
        ParentShas = parentShas ?? Array.Empty<string>();
        RefLabels = refLabels?.ToArray() ?? Array.Empty<CommitRefLabel>();
        VisibleRefLabels = RefLabels.Take(MaxVisibleRefLabels).ToArray();
        HiddenRefLabelCount = Math.Max(RefLabels.Count - VisibleRefLabels.Count, 0);
        HiddenRefLabelText = HiddenRefLabelCount > 0 ? $"+{HiddenRefLabelCount}" : string.Empty;
        RefLabelsTooltip = string.Join(", ", RefLabels.Select(l => l.Name));
    }

    public string Message { get; }
    public DateTime Date { get; }
    public string CommitId { get; }
    public string AuthorName { get; }
    public string AuthorEmail { get; }
    public string FullSha { get; }
    public IReadOnlyList<string> ParentShas { get; }
    public IReadOnlyList<CommitRefLabel> RefLabels { get; }
    public bool HasRefLabels => RefLabels.Count > 0;
    public IReadOnlyList<CommitRefLabel> VisibleRefLabels { get; }
    public bool HasVisibleRefLabels => VisibleRefLabels.Count > 0;
    public int HiddenRefLabelCount { get; }
    public bool HasHiddenRefLabels => HiddenRefLabelCount > 0;
    public string HiddenRefLabelText { get; }
    public string RefLabelsTooltip { get; }

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
    public partial CommitGraphRow GraphRow { get; set; } = CommitGraphRow.Empty;

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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSigningBadge))]
    [NotifyPropertyChangedFor(nameof(SigningBadgeText))]
    [NotifyPropertyChangedFor(nameof(SigningBadgeTooltip))]
    public partial CommitSigningStatus SigningStatus { get; set; } = CommitSigningStatus.Unknown;

    public bool HasSigningBadge => SigningStatus is
        CommitSigningStatus.Good or
        CommitSigningStatus.Bad or
        CommitSigningStatus.Untrusted;

    public string SigningBadgeText => SigningStatus switch
    {
        CommitSigningStatus.Good => "S",
        CommitSigningStatus.Bad => "!",
        CommitSigningStatus.Untrusted => "?",
        _ => string.Empty,
    };

    public string SigningBadgeTooltip => SigningStatus switch
    {
        CommitSigningStatus.Good => "Signature verified",
        CommitSigningStatus.Bad => "Bad signature",
        CommitSigningStatus.Untrusted => "Signature key is untrusted",
        CommitSigningStatus.NoSignature => "No commit signature",
        _ => string.Empty,
    };

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
