namespace Gitster.Services.Git;

public sealed record HistoryStitchPreview(
    string SourceRef,
    string SourceTipSha,
    string TargetBranch,
    string TargetHeadSha,
    bool IsSourceAlreadyReachable,
    int UniqueSourceCommitCount,
    string? SquashMatchSha,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Blocks)
{
    public bool CanExecute => Blocks.Count == 0;
}

public sealed record HistoryStitchResult(
    string SourceRef,
    string SourceTipSha,
    string TargetBranch,
    string BackupBranch,
    string MergeCommitSha);
