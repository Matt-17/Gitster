namespace Gitster.ViewModels;

public enum CommitSectionKind { RemoteIncoming, LocalOutgoing }

/// <summary>
/// A display-only section header in the flat commit list.
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

    public string Title
    {
        get
        {
            var formattedCount = Count.ToString("N0");
            return IsIncoming
                ? Count == 0 ? "Remote (0)" : $"Remote ({formattedCount} incoming)"
                : Count == 0 ? "Local History (0)" : $"Local History ({formattedCount} outgoing)";
        }
    }

    public bool HasRemoteName => IsIncoming && !string.IsNullOrWhiteSpace(RemoteName);
    public string RemoteNameDisplay => RemoteName ?? string.Empty;
    public bool HasRemoteUrl => IsIncoming && !string.IsNullOrWhiteSpace(RemoteUrl);
    public string RemoteUrlDisplay => RemoteUrl ?? string.Empty;
}

public sealed class CommitSectionEmptyRow
{
    public CommitSectionEmptyRow(CommitSectionKind kind, string message)
    {
        Kind = kind;
        Message = message;
    }

    public CommitSectionKind Kind { get; }
    public string Message { get; }

    public bool IsOutgoing => Kind == CommitSectionKind.LocalOutgoing;
}
