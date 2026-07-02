namespace Gitster.Services.History;

internal sealed record HistoryContext(
    string RepoKey,
    string RepoPath,
    string GitDir,
    string BranchName,
    string HeadSha,
    string? UpstreamSha,
    HistoryScope Scope);

internal sealed record BranchState(
    string HeadSha,
    string? UpstreamSha,
    bool IsComplete,
    int CachedCount);
