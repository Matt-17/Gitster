namespace Gitster.Core.Git;

/// <summary>A commit that a merge brought in from its second parent.</summary>
public sealed record MergedBranchCommit(string Sha, string Subject);
