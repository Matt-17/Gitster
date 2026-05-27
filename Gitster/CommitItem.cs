using Gitster.Services.Git;

namespace Gitster;

public record CommitItem(
    string Message,
    DateTime Date,
    string CommitId,
    string AuthorName,
    string AuthorEmail = "",
    CommitRemoteState RemoteState = CommitRemoteState.LocalOnly)
{
    public string GroupLabel => RemoteState is CommitRemoteState.LocalOnly or CommitRemoteState.NoTrackingBranch
        ? "Local (outgoing)"
        : "On remote";
}
