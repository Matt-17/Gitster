using CommunityToolkit.Mvvm.Input;

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
        bool isLoading = false,
        bool plainCount = false)
    {
        Kind = kind;
        Count = count;
        RemoteName = remoteName;
        RemoteUrl = remoteUrl;
        IsLoading = isLoading;
        PlainCount = plainCount;
    }

    public CommitSectionKind Kind { get; }
    public int Count { get; }
    public string? RemoteName { get; }
    public string? RemoteUrl { get; }
    public bool IsLoading { get; }

    /// <summary>True when the section lists a branch's full history (remote-branch view),
    /// so the count is a plain commit count rather than an incoming/outgoing delta.</summary>
    public bool PlainCount { get; }

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

        if (PlainCount)
            return $"Remote History ({formattedCount})";

        return Count == 0
            ? "Remote History (0)"
            : $"Remote History ({formattedCount} incoming)";
    }

    public bool HasRemoteName => IsIncoming && !string.IsNullOrWhiteSpace(RemoteName);
    public string RemoteNameDisplay => RemoteName ?? string.Empty;
    public bool HasRemoteUrl => IsIncoming && !string.IsNullOrWhiteSpace(RemoteUrl);
    public string RemoteUrlDisplay => RemoteUrl ?? string.Empty;
}

/// <summary>An inline action ("checkout", "create &amp; push", ...) on a placeholder row.</summary>
public sealed class CommitSectionLink
{
    public CommitSectionLink(string text, Func<Task> action)
    {
        Text = text;
        Command = new AsyncRelayCommand(action);
    }

    public string Text { get; }
    public IAsyncRelayCommand Command { get; }
}

public sealed class CommitSectionEmptyRow
{
    public CommitSectionEmptyRow(
        CommitSectionKind kind,
        string message,
        IReadOnlyList<CommitSectionLink>? links = null)
    {
        Kind = kind;
        Message = message;
        Links = links ?? [];
    }

    public CommitSectionKind Kind { get; }
    public string Message { get; }
    public IReadOnlyList<CommitSectionLink> Links { get; }
    public bool HasLinks => Links.Count > 0;

    public bool IsOutgoing => Kind == CommitSectionKind.LocalOutgoing;
}
