namespace Gitster.Services.Git;

/// <summary>Per-file change kind for the commit panel (plan A2).</summary>
public enum WorkingFileStatus { Added, Modified, Deleted, Renamed, Untracked, TypeChange, Conflicted }

/// <summary>A single changed file in the working tree / index, with line counts.</summary>
public sealed record WorkingTreeFile(
    string Path,
    WorkingFileStatus Status,
    bool Staged,
    int Added,
    int Deleted)
{
    /// <summary>Single-letter badge (A/M/D/R/U/T/C) used in the commit panel.</summary>
    public string Badge => Status switch
    {
        WorkingFileStatus.Added       => "A",
        WorkingFileStatus.Modified    => "M",
        WorkingFileStatus.Deleted     => "D",
        WorkingFileStatus.Renamed     => "R",
        WorkingFileStatus.Untracked   => "U",
        WorkingFileStatus.TypeChange  => "T",
        WorkingFileStatus.Conflicted  => "C",
        _                             => "?",
    };
}

/// <summary>Snapshot of the working tree split into staged and unstaged (incl. untracked) lists.</summary>
public sealed record WorkingTreeStatus(
    IReadOnlyList<WorkingTreeFile> Staged,
    IReadOnlyList<WorkingTreeFile> Unstaged)
{
    public static readonly WorkingTreeStatus Empty =
        new(Array.Empty<WorkingTreeFile>(), Array.Empty<WorkingTreeFile>());
}

/// <summary>Carries the parameters for a commit-panel commit (message + amend + identity overrides).</summary>
public sealed record CommitRequest(
    string Message,
    bool Amend = false,
    string? AuthorName = null,
    string? AuthorEmail = null,
    string? CommitterName = null,
    string? CommitterEmail = null);
