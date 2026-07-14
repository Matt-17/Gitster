using Gitster.Core.Git;
using Gitster.Core.History;

namespace Gitster;

/// <summary>
/// Maps the domain <see cref="CommitHistoryRow"/> (Gitster.Core) onto the presentation-layer
/// <see cref="CommitItem"/> row. Kept out of Core so the history engine stays UI-agnostic.
/// </summary>
public static class CommitHistoryRowExtensions
{
    public static CommitItem ToCommitItem(this CommitHistoryRow row) => new(
        row.Message,
        row.Date,
        row.ShortSha,
        row.AuthorName,
        row.AuthorEmail,
        row.RemoteState,
        row.FullSha,
        row.OrphanedPairSha,
        row.ParentShas ?? Array.Empty<string>(),
        row.RefLabels ?? Array.Empty<CommitRefLabel>());
}
