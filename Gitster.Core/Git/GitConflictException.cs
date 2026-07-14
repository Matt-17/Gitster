namespace Gitster.Core.Git;

/// <summary>
/// A Git operation hit merge/apply conflicts. Preferred over message sniffing:
/// consumers can catch this type and read <see cref="RepositoryHalted"/> instead
/// of parsing exception text (which remains a fallback for CLI-origin errors).
/// </summary>
public sealed class GitConflictException : InvalidOperationException
{
    public GitConflictException(string message, bool repositoryHalted)
        : base(message)
    {
        RepositoryHalted = repositoryHalted;
    }

    /// <summary>
    /// True when the repository was left in a conflict state the user must resolve;
    /// false when the operation was aborted and the pre-operation state restored.
    /// </summary>
    public bool RepositoryHalted { get; }
}
