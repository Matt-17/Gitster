namespace Gitster.ViewModels;

public enum CommitSectionKind { RemoteIncoming, LocalOutgoing }

/// <summary>
/// A section-header row in the flat commit list (plan A5). Two kinds only:
/// "Remote (incoming · N)" in blue and "Local (outgoing · N)" in amber. Synced commits
/// flow below with no header of their own.
/// </summary>
public sealed class CommitSectionHeader
{
    public CommitSectionHeader(CommitSectionKind kind, int count, string? remoteName = null, string? remoteUrl = null)
    {
        Kind = kind;
        Count = count;
        RemoteName = remoteName;
        RemoteUrl = remoteUrl;
    }

    public CommitSectionKind Kind { get; }
    public int Count { get; }
    public string? RemoteName { get; }
    public string? RemoteUrl { get; }

    public bool IsIncoming => Kind == CommitSectionKind.RemoteIncoming;
    public bool IsOutgoing => Kind == CommitSectionKind.LocalOutgoing;

    public string Title => IsIncoming
        ? $"Remote (incoming · {Count})"
        : $"Local (outgoing · {Count})";

    /// <summary>"— origin" appended to the incoming header when the remote name is known.</summary>
    public string RemoteLabel =>
        IsIncoming && !string.IsNullOrEmpty(RemoteName) ? $"— {RemoteName}" : string.Empty;

    public bool HasRemoteUrl => IsIncoming && !string.IsNullOrEmpty(RemoteUrl);
    public string RemoteUrlDisplay => RemoteUrl ?? string.Empty;

    /// <summary>When a remote exists but nothing is incoming, hint that a fetch would refresh it.</summary>
    public bool ShowHint => IsIncoming && Count == 0;
    public string Hint => ShowHint ? "up to date — fetch to check" : string.Empty;
}
