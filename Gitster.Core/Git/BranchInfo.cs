namespace Gitster.Core.Git;

public sealed record BranchInfo(string Name, int Incoming, int Outgoing);

public sealed record BranchSummary(
    string Name,
    string TipSha,
    bool IsRemote,
    bool IsCurrent);
