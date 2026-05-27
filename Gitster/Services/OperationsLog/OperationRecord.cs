namespace Gitster.Services.OperationsLog;

public enum OperationKind { Amend, Reword, Reset, Rebase, CherryPick, CommitOnBranch, AuthorRepair, AuthorAmend, RangeRewrite }
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
