using Gitster.Services.Git;

namespace Gitster;

public record CommitItem(
    string Message,
    DateTime Date,
    string CommitId,
    string AuthorName,
    string AuthorEmail = "",
    CommitRemoteState RemoteState = CommitRemoteState.LocalOnly,
    string FullSha = "",
    string? OrphanedPairSha = null)
{
    public string GroupLabel => RemoteState switch
    {
        CommitRemoteState.Incoming => "Incoming",
        CommitRemoteState.LocalOnly or CommitRemoteState.NoTrackingBranch => "Outgoing",
        _ => "Synced"
    };

    public int GroupOrder => RemoteState switch
    {
        CommitRemoteState.Incoming => 0,
        CommitRemoteState.LocalOnly or CommitRemoteState.NoTrackingBranch => 1,
        _ => 2
    };

    /// <summary>True when this commit and its orphaned pair (same tree, rewritten) are visible in the list.</summary>
    public bool IsOrphanedPair => OrphanedPairSha != null;
}
