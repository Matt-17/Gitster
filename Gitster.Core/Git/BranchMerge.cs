namespace Gitster.Core.Git;

public enum BranchMergeStrategy
{
    Default,
    FastForwardOnly,
    NoFastForward,
}

public enum BranchMergeOutcome
{
    UpToDate,
    FastForward,
    MergeCommit,
}

public sealed record BranchMergeResult(
    string SourceBranch,
    string TargetBranch,
    string HeadSha,
    BranchMergeOutcome Outcome);
