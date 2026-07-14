namespace Gitster.Services.Git;

/// <summary>Amends only the author and/or committer of the HEAD commit.</summary>
public sealed record AmendAuthorRequest(
    string? AuthorName    = null,
    string? AuthorEmail   = null,
    string? CommitterName  = null,
    string? CommitterEmail = null);
