namespace Gitster.Core.Git;

public sealed record CommitDetails(
    string Sha,
    string Message,
    DateTime Date,
    string AuthorName,
    string AuthorEmail = "",
    string CommitterName = "",
    string CommitterEmail = "",
    DateTime? CommitterDate = null);
