namespace Gitster.Services.Git;

public sealed record CommitDetails(string Sha, string Message, DateTime Date, string AuthorName);
