using System.Text.Json.Serialization;

using Gitster.Services.Git;

namespace Gitster.Services.OperationsLog;

public enum OperationKind { Amend, Reword, Reset, Rebase, CherryPick, CommitOnBranch, AuthorRepair, AuthorAmend, RangeRewrite, StashDrop, StashPop, StashConvert, Fixup, Squash, CherryPickTimestamp, Snapshot, Commit, HistoryEdit, Merge, Revert }
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
    OperationStatus Status)
{
    /// <summary>Display forms — records store full SHAs (legacy records may hold short ones).</summary>
    [JsonIgnore]
    public string BeforeShaShort => GitSha.Short(BeforeSha);

    [JsonIgnore]
    public string AfterShaShort => GitSha.Short(AfterSha);
}
