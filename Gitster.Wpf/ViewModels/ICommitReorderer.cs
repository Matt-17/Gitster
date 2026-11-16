namespace Gitster.ViewModels;

/// <summary>
/// Plans a new commit order as a pending history edit. Every method returns null on success,
/// otherwise the reason the move is not possible.
/// </summary>
public interface ICommitReorderer
{
    IReadOnlyList<string>? PendingOrder { get; }

    string? MoveCommitsBy(IReadOnlyList<CommitItem> commits, int delta);

    string? CanMoveCommitsTo(IReadOnlyList<CommitItem> commits, CommitItem target, bool after);

    string? MoveCommitsTo(IReadOnlyList<CommitItem> commits, CommitItem target, bool after);
}
