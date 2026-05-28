using System.IO;
using LibGit2Sharp;

namespace Gitster.Tests;

/// <summary>
/// A throwaway on-disk Git repository for backend integration tests.
/// Created in a unique temp directory and deleted on dispose.
/// </summary>
public sealed class GitTestRepo : IDisposable
{
    public string Path { get; }

    private static readonly Identity Ident = new("Tester", "tester@gitster.test");

    public GitTestRepo()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "gitster-test-" + Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(Path);
        Repository.Init(Path);

        using var repo = new Repository(Path);
        repo.Config.Set("user.name", Ident.Name);
        repo.Config.Set("user.email", Ident.Email);
        // Deterministic core settings independent of the host's global config.
        repo.Config.Set("commit.gpgsign", false);
        repo.Config.Set("core.autocrlf", false);
    }

    /// <summary>Writes a file, stages it, and commits — returns the new commit SHA.</summary>
    public string Commit(string message, string fileName, string content)
    {
        File.WriteAllText(System.IO.Path.Combine(Path, fileName), content);
        using var repo = new Repository(Path);
        Commands.Stage(repo, fileName);
        var sig = new Signature(Ident, DateTimeOffset.Now);
        var commit = repo.Commit(message, sig, sig);
        return commit.Sha;
    }

    /// <summary>Writes a file and stages it WITHOUT committing (sets up a fixup).</summary>
    public void Stage(string fileName, string content)
    {
        File.WriteAllText(System.IO.Path.Combine(Path, fileName), content);
        using var repo = new Repository(Path);
        Commands.Stage(repo, fileName);
    }

    public string Head()
    {
        using var repo = new Repository(Path);
        return repo.Head.Tip!.Sha;
    }

    public string MessageOf(string sha)
    {
        using var repo = new Repository(Path);
        return repo.Lookup<Commit>(sha)!.MessageShort;
    }

    public int CommitCount()
    {
        using var repo = new Repository(Path);
        return repo.Commits.Count();
    }

    public bool IsRebaseInProgress()
    {
        using var repo = new Repository(Path);
        var gitDir = repo.Info.Path;
        return Directory.Exists(System.IO.Path.Combine(gitDir, "rebase-merge"))
            || Directory.Exists(System.IO.Path.Combine(gitDir, "rebase-apply"));
    }

    public void Dispose()
    {
        try
        {
            // Clear read-only attributes Git sets on pack files, then delete.
            foreach (var file in Directory.EnumerateFiles(Path, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(Path, recursive: true);
        }
        catch { /* best-effort cleanup */ }
    }
}
