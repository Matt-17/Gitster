namespace Gitster.Models;

public abstract record WorkingTreeState
{
    public sealed record Clean : WorkingTreeState;
    public sealed record Dirty(int Modified, int Staged, int Untracked) : WorkingTreeState;
    public sealed record Merging(string FromBranch) : WorkingTreeState;
    public sealed record Rebasing(int CurrentStep, int TotalSteps) : WorkingTreeState;
    public sealed record CherryPicking(string Sha) : WorkingTreeState;
}
