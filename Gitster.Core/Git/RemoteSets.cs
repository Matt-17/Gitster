namespace Gitster.Core.Git;

/// <summary>
/// The result of computing a branch's relationship to its remote counterpart (plan A0.4).
/// Computed on a background thread after the commit list is already visible, then used
/// to fill in each row's <see cref="CommitRemoteState"/> and the section headers.
/// Also computed for a branch selected in the refs pane (not just HEAD); for a
/// refs/remotes/* selection the "local" side describes the local counterpart branch.
/// </summary>
/// <remarks><see cref="HasOutgoingBaseline"/> is true when <see cref="OutgoingFullShas"/> was
/// measured against something (the upstream, or — without one — all remote branches), so
/// commits outside the set are known to be on a remote.</remarks>
public sealed record RemoteSets(
    IReadOnlyList<CommitInfo> Incoming,
    IReadOnlySet<string> OutgoingFullShas,
    IReadOnlyDictionary<string, string> OrphanedPairs,
    bool HasTrackingBranch,
    bool HasRemote,
    string? RemoteName,
    string? RemoteUrl,
    bool HasLocalBranch = true,
    string? LocalBranchName = null,
    string? RemoteBranchName = null,
    bool HasSameNameRemoteBranch = false,
    bool HasOutgoingBaseline = false)
{
    public static readonly RemoteSets Empty = new(
        Array.Empty<CommitInfo>(),
        new HashSet<string>(),
        new Dictionary<string, string>(),
        HasTrackingBranch: false,
        HasRemote: false,
        RemoteName: null,
        RemoteUrl: null);
}
