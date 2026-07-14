namespace Gitster.Core.Git;

/// <summary>A single working tree of the repository (Step D).</summary>
public sealed record WorktreeInfo(
    string Path,
    string BranchName,
    string HeadSha,
    bool IsMain,          // the primary worktree
    bool IsLocked,
    bool IsPrunable,      // directory missing → can be pruned
    bool IsCurrent);      // the worktree Gitster currently has open
