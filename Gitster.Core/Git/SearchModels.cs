using Gitster.Core.Models;

namespace Gitster.Core.Git;

/// <summary>One line of blame output (plan B4).</summary>
public sealed record BlameLine(
    int LineNumber,
    string Sha,
    string Author,
    DateTimeOffset Date,
    string Content)
{
    public string ShortSha => GitSha.Short(Sha);
    public string DateText => Date.ToLocalTime().ToString("dd.MM.yyyy");
}

/// <summary>How a commit changed between two patch series (plan B5).</summary>
public enum RangeDiffStatus { Unchanged, Modified, Added, Removed }

/// <summary>One commit pairing in a range-diff (plan B5).</summary>
public sealed record RangeDiffEntry(
    RangeDiffStatus Status,
    string? LeftSha,
    string? RightSha,
    string Summary);

/// <summary>The result of comparing two refs (plan B6): the commit list plus the diff.</summary>
public sealed record CompareResult(
    IReadOnlyList<CommitInfo> Commits,
    CommitDiff Diff,
    string Explanation)
{
    public static readonly CompareResult Empty =
        new(Array.Empty<CommitInfo>(), CommitDiff.Empty, string.Empty);
}
