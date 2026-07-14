namespace Gitster.Models;

public record StashInfo(
    int Index,
    string RawMessage,
    string BranchName,
    DateTimeOffset CreatedAt,
    IReadOnlyList<StashFileChange> Files,
    string AutoName,
    string CommitSha);

public record StashFileChange(
    string Path,
    StashChangeKind Kind,
    int Added,
    int Removed);

public enum StashChangeKind { Added, Modified, Deleted, Renamed }
