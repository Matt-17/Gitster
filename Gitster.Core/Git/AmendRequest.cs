namespace Gitster.Core.Git;

/// <summary>
/// Combined amend request — applies timestamp, author, committer, and/or message
/// in a single operation. All fields except NewDate are optional; null means
/// "keep the existing value".
/// </summary>
public sealed record AmendRequest(
    DateTime NewDate,
    string? AuthorName    = null,
    string? AuthorEmail   = null,
    string? CommitterName  = null,
    string? CommitterEmail = null,
    string? NewMessage    = null);
