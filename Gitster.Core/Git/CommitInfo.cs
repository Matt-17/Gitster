namespace Gitster.Core.Git;

public enum CommitRemoteState
{
    Incoming,            // fetched from remote, not yet merged into local branch
    LocalOnly,           // outgoing — not yet on the tracking remote
    OnRemote,            // present on both local branch and remote (synced)
    NotAhead,            // HEAD is behind remote
    NoTrackingBranch,    // branch has no configured upstream
}

public sealed record CommitInfo(
    string Sha,
    string Message,
    DateTime Date,
    string AuthorName,
    string AuthorEmail = "",
    CommitRemoteState RemoteState = CommitRemoteState.LocalOnly,
    string FullSha = "",
    string? OrphanedPairSha = null,
    IReadOnlyList<string>? ParentShas = null,
    IReadOnlyList<CommitRefLabel>? RefLabels = null)
{
    /// <summary>7-char display form of <see cref="Sha"/>.</summary>
    public string ShortSha => GitSha.Short(Sha);
}

public enum CommitRefKind
{
    CurrentBranch,
    LocalBranch,
    RemoteBranch,
}

public sealed record CommitRefLabel(
    string Name,
    CommitRefKind Kind,
    bool IsCurrent = false);
