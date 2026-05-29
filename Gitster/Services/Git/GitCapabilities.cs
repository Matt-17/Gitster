namespace Gitster.Services.Git;

[Flags]
public enum GitCapabilities
{
    None = 0,
    Read = 1 << 0,
    BasicWrite = 1 << 1,
    ReflogUndo = 1 << 2,
    InteractiveRebase = 1 << 3,
    FixupAutosquash = 1 << 4,
    PickaxeSearch = 1 << 5,
    RangeDiff = 1 << 6,
    Worktrees = 1 << 7,
    CommitSigning = 1 << 8,
    StashManagement = 1 << 9,
    DiffRegexSearch = 1 << 10,
    BlameFollow = 1 << 11,
}
