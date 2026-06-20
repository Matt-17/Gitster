using System.IO;

using Gitster.Services.Git;
using Gitster.Services.History;
using Gitster.Services.Search;

namespace Gitster.Tests;

[TestClass]
public sealed class CommitHistoryServiceTests
{
    [TestMethod]
    public async Task GetPage_LoadsOnlyRequestedWindowUntilCompleteIsRequested()
    {
        using var repo = new GitTestRepo();
        repo.Commit("c1", "a.txt", "1");
        repo.Commit("c2", "a.txt", "2");
        repo.Commit("c3", "a.txt", "3");

        using var cache = new TempCacheDir();
        var backend = new LibGit2Backend();
        var history = new CommitHistoryService(backend, cache.Path);

        await history.OpenAsync(repo.Path);

        var page = await history.GetPageAsync(CommitQuery.Parse(""), 0, 2);
        Assert.AreEqual(2, page.Count);
        Assert.AreEqual("c3", page[0].Message);
        Assert.AreEqual("c2", page[1].Message);

        var complete = await history.EnsureCompleteAsync(progress: null);
        Assert.AreEqual(3, complete.Count);
        Assert.AreEqual("c1", complete[2].Message);
    }

    [TestMethod]
    public async Task OpenAsync_WhenHeadAdvances_PrependsNewRowsAndKeepsCachedTail()
    {
        using var repo = new GitTestRepo();
        repo.Commit("c1", "a.txt", "1");
        repo.Commit("c2", "a.txt", "2");
        repo.Commit("c3", "a.txt", "3");

        using var cache = new TempCacheDir();
        var backend = new LibGit2Backend();
        var history = new CommitHistoryService(backend, cache.Path);

        await history.OpenAsync(repo.Path);
        await history.EnsureCompleteAsync(progress: null);

        repo.Commit("c4", "a.txt", "4");
        await history.OpenAsync(repo.Path);

        var page = await history.GetPageAsync(CommitQuery.Parse(""), 0, 4);
        CollectionAssert.AreEqual(new[] { "c4", "c3", "c2", "c1" }, page.Select(r => r.Message).ToArray());
    }

    [TestMethod]
    public async Task SearchAsync_UsesCompleteCachedHistory()
    {
        using var repo = new GitTestRepo();
        repo.Commit("feature alpha", "a.txt", "1");
        repo.Commit("fix beta", "a.txt", "2");
        repo.Commit("feature gamma", "a.txt", "3");

        using var cache = new TempCacheDir();
        var backend = new LibGit2Backend();
        var history = new CommitHistoryService(backend, cache.Path);

        await history.OpenAsync(repo.Path);

        var matches = await history.SearchAsync(CommitQuery.Parse("message:feature"), 10);
        CollectionAssert.AreEqual(
            new[] { "feature gamma", "feature alpha" },
            matches.Select(r => r.Message).ToArray());
    }

    private sealed class TempCacheDir : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "gitster-history-cache-" + Guid.NewGuid().ToString("N")[..12]);

        public TempCacheDir() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                    Directory.Delete(Path, recursive: true);
            }
            catch
            {
                // Best-effort cleanup.
            }
        }
    }
}
