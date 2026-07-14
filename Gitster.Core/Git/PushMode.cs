namespace Gitster.Core.Git;

/// <summary>
/// How a push should be performed (plan A3). Force-with-lease is the safe force and
/// prefers the Git CLI; plain Force is the dangerous variant guarded by a confirmation.
/// </summary>
public enum PushMode
{
    Normal,
    ForceWithLease,
    Force,
}
