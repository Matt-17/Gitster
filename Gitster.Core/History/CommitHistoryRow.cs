using Gitster.Core.Git;

namespace Gitster.Core.History;

public sealed record CommitHistoryRow(
    int Sequence,
    string FullSha,
    string ShortSha,
    string Message,
    DateTime Date,
    string AuthorName,
    string AuthorEmail,
    string TreeSha,
    CommitRemoteState RemoteState,
    string? OrphanedPairSha,
    IReadOnlyList<string>? ParentShas = null,
    IReadOnlyList<CommitRefLabel>? RefLabels = null,
    DateTime? CommitterDate = null)
{
    public CommitInfo ToCommitInfo() => new(
        ShortSha,
        Message,
        Date,
        AuthorName,
        AuthorEmail,
        RemoteState,
        FullSha,
        OrphanedPairSha,
        ParentShas ?? Array.Empty<string>(),
        RefLabels ?? Array.Empty<CommitRefLabel>(),
        CommitterDate);
}
