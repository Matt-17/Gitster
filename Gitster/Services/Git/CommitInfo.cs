namespace Gitster.Services.Git;

public enum CommitRemoteState
{
    LocalOnly,           // outgoing — not yet on the tracking remote
    OnRemote,            // already pushed to tracking remote
    NotAhead,            // HEAD is behind remote
    NoTrackingBranch,    // branch has no configured upstream
}

public sealed record CommitInfo(
    string Sha,
    string Message,
    DateTime Date,
    string AuthorName,
    string AuthorEmail = "",
    CommitRemoteState RemoteState = CommitRemoteState.LocalOnly);
