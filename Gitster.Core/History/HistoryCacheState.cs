namespace Gitster.Core.History;

internal sealed record HistoryContext(
    string RepoKey,
    string RepoPath,
    string GitDir,
    string BranchName,
    string HeadSha,
    string? UpstreamSha,
    HistoryScope Scope,
    string? TargetRefName = null);

internal sealed record BranchState(
    string HeadSha,
    string? UpstreamSha,
    bool IsComplete,
    int CachedCount);
