namespace Gitster.Services.Git;

/// <summary>Rich branch row for the Branches mode list.</summary>
public sealed record BranchListItem(
    string Name,
    string? UpstreamName,        // tracking branch, null if none
    string TipSha,
    string TipMessage,
    DateTimeOffset LastActivity, // tip commit committer date — for sorting
    int Ahead,                   // commits ahead of upstream
    int Behind,                  // commits behind upstream
    bool IsCurrent,
    bool IsRemote,               // local vs remote-tracking branch
    bool IsMerged);              // merged into current branch (safe to delete)

/// <summary>Request describing a commit-to-another-branch operation (Step B).</summary>
public sealed record CommitToBranchRequest(
    string TargetBranch,
    string Message,
    string? AuthorName,
    string? AuthorEmail,
    bool IncludeUnstaged,     // false = staged only; true = staged + unstaged + untracked
    bool RemoveFromCurrent);  // false = copy (safe default); true = move
