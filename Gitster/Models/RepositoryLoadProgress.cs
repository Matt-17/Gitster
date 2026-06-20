namespace Gitster.Models;

public sealed record RepositoryLoadProgress(
    string Stage,
    string Detail = "",
    int? CommitCount = null,
    int? TotalCommitCount = null)
{
    public string CounterText => CommitCount.HasValue
        ? TotalCommitCount.HasValue
            ? $"{CommitCount.Value:N0} / {TotalCommitCount.Value:N0} commits"
            : $"{CommitCount.Value:N0} commit{(CommitCount.Value == 1 ? "" : "s")}"
        : string.Empty;
}
