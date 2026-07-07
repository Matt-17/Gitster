namespace Gitster.ViewModels;

public enum CommitSectionKind { RemoteIncoming, LocalOutgoing }

/// <summary>
/// A display-only section header in the flat commit list.
/// </summary>
public sealed class CommitSectionHeader
{
    public CommitSectionHeader(
        CommitSectionKind kind,
        int count,
        string? remoteName = null,
        string? remoteUrl = null,
        bool isLoading = false)
    {
        Kind = kind;
        Count = count;
        RemoteName = remoteName;
        RemoteUrl = remoteUrl;
        IsLoading = isLoading;
    }

    public CommitSectionKind Kind { get; }
    public int Count { get; }
    public string? RemoteName { get; }
    public string? RemoteUrl { get; }
    public bool IsLoading { get; }

    public bool IsIncoming => Kind == CommitSectionKind.RemoteIncoming;
    public bool IsOutgoing => Kind == CommitSectionKind.LocalOutgoing;

    public string Title
    {
        get
        {
            var formattedCount = Count.ToString("N0");
            return IsIncoming
                ? RemoteTitle(formattedCount)
                : Count == 0 ? "Local History (0)" : $"Local History ({formattedCount} outgoing)";
        }
    }

    private string RemoteTitle(string formattedCount)
    {
        if (IsLoading)
            return "Remote History (checking...)";

        return Count == 0
            ? "Remote History (0)"
            : $"Remote History ({formattedCount} incoming)";
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
