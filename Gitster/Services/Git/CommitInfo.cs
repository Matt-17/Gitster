namespace Gitster.Services.Git;

public sealed record CommitInfo(string Sha, string Message, DateTime Date, string AuthorName);
