using LibGit2Sharp;

namespace Gitster.Services.Git;

public interface IRepositoryReadProvider
{
    Repository OpenRepository(string repoPath);
}

internal sealed class LibGit2RepositoryReadProvider : IRepositoryReadProvider
{
    public Repository OpenRepository(string repoPath) => new(repoPath);
}
