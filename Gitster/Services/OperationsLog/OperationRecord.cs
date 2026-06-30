namespace Gitster.Services.OperationsLog;

public enum OperationKind { Amend, Reword, Reset, Rebase, CherryPick, CommitOnBranch, AuthorRepair, AuthorAmend, RangeRewrite, StashDrop, StashPop, StashConvert, Fixup, Squash, CherryPickTimestamp, Snapshot, Commit, HistoryEdit }
public enum OperationStatus { Active, Undone, Replaced, Expired }

public record OperationRecord(
    string Id,
    DateTimeOffset Timestamp,
    OperationKind Kind,
    string Description,
    string BranchName,
    string BeforeSha,
    string AfterSha,
    string? ReflogSelector,
    OperationStatus Status);
