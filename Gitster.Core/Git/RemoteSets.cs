namespace Gitster.Core.Git;

/// <summary>
/// The result of computing a branch's relationship to its tracking remote (plan A0.4).
/// Computed on a background thread after the commit list is already visible, then used
/// to fill in each row's <see cref="CommitRemoteState"/> and the section headers.
/// </summary>
public sealed record RemoteSets(
    IReadOnlyList<CommitInfo> Incoming,
    IReadOnlySet<string> OutgoingFullShas,
    IReadOnlyDictionary<string, string> OrphanedPairs,
    bool HasTrackingBranch,
    bool HasRemote,
    string? RemoteName,
    string? RemoteUrl)
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
