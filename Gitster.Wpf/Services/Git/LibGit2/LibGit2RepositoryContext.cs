using LibGit2Sharp;

namespace Gitster.Services.Git.LibGit2;

internal sealed class LibGit2RepositoryContext
{
    public string? RepositoryPath { get; private set; }

    public event EventHandler? HeadChanged;

    public Task OpenAsync(string path)
    {
        using var repo = new Repository(path);
        RepositoryPath = path;
        return Task.CompletedTask;
    }

    public Repository OpenRepository()
    {
        if (RepositoryPath == null)
            throw new InvalidOperationException("Repository not open.");

        return new Repository(RepositoryPath);
    }

    public void RaiseHeadChanged() => HeadChanged?.Invoke(this, EventArgs.Empty);
}
