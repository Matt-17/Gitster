using System.IO;

using Gitster.Services.Git;
using Gitster.Services.History;
using Gitster.Services.Search;
using LibGit2Sharp;
using Microsoft.Data.Sqlite;

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

    [TestMethod]
    public async Task EnsureCompleteAsync_StoresParentShas()
    {
        using var repo = new GitTestRepo();
        var c1 = repo.Commit("c1", "a.txt", "1");
        repo.Commit("c2", "a.txt", "2");

        using var cache = new TempCacheDir();
        var backend = new LibGit2Backend();
        var history = new CommitHistoryService(backend, cache.Path);

        await history.OpenAsync(repo.Path);
        var rows = await history.EnsureCompleteAsync(progress: null);

        CollectionAssert.AreEqual(new[] { c1 }, rows[0].ParentShas!.ToArray());
    }

    [TestMethod]
    public async Task OpenAsync_WhenExistingCacheHasBlankGraphColumns_RebuildsRows()
    {
        using var repo = new GitTestRepo();
        var c1 = repo.Commit("c1", "a.txt", "1");
        repo.Commit("c2", "a.txt", "2");

        using var cache = new TempCacheDir();
        var backend = new LibGit2Backend();
        var history = new CommitHistoryService(backend, cache.Path);

        await history.OpenAsync(repo.Path);
        await history.EnsureCompleteAsync(progress: null);
        BlankGraphColumns(cache.Path);

        await history.OpenAsync(repo.Path);
        var rows = await history.EnsureCompleteAsync(progress: null);

        CollectionAssert.AreEqual(new[] { c1 }, rows[0].ParentShas!.ToArray());
    }

    [TestMethod]
    public async Task EnsureCompleteAsync_AllBranches_RebuildsWhenBranchTipMoves()
    {
        using var repo = new GitTestRepo();
        repo.Commit("base", "a.txt", "1");
        string main;
        using (var r = new Repository(repo.Path))
        {
            main = r.Head.FriendlyName;
            r.CreateBranch("feature", r.Head.Tip);
            Commands.Checkout(r, r.Branches["feature"]);
        }

        repo.Commit("feature work", "feature.txt", "1");
        Checkout(repo.Path, main);
        repo.Commit("main work", "main.txt", "1");

        using var cache = new TempCacheDir();
        var backend = new LibGit2Backend();
        var history = new CommitHistoryService(backend, cache.Path);

        await history.OpenAsync(repo.Path, HistoryScope.AllBranches);
        var firstRows = await history.EnsureCompleteAsync(
            progress: null,
            scope: HistoryScope.AllBranches);

        var featureRow = firstRows.Single(r => r.Message == "feature work");
        Assert.IsTrue(featureRow.RefLabels!.Any(l => l.Name == "feature"));

        Checkout(repo.Path, "feature");
        repo.Commit("feature work 2", "feature.txt", "2");
        Checkout(repo.Path, main);

        await history.OpenAsync(repo.Path, HistoryScope.AllBranches);
        var secondRows = await history.EnsureCompleteAsync(
            progress: null,
            scope: HistoryScope.AllBranches);

        var newFeatureRow = secondRows.Single(r => r.Message == "feature work 2");
        Assert.IsTrue(newFeatureRow.RefLabels!.Any(l => l.Name == "feature"));

        var oldFeatureRow = secondRows.Single(r => r.Message == "feature work");
        Assert.IsFalse(oldFeatureRow.RefLabels!.Any(l => l.Name == "feature"));
    }

    [TestMethod]
    public async Task EnsureCompleteAsync_AllBranches_LabelsCurrentLocalAndRemoteRefsInDeterministicOrder()
    {
        using var repo = new GitTestRepo();
        var tip = repo.Commit("tip", "a.txt", "1");
        string currentBranch;
        using (var r = new Repository(repo.Path))
        {
            currentBranch = r.Head.FriendlyName;
            r.CreateBranch("release", r.Head.Tip);
            AddRemoteTrackingRef(r, "origin/main", tip);
            AddRemoteTrackingRef(r, "origin/HEAD", tip);
        }

        using var cache = new TempCacheDir();
        var backend = new LibGit2Backend();
        var history = new CommitHistoryService(backend, cache.Path);

        await history.OpenAsync(repo.Path, HistoryScope.AllBranches);
        var rows = await history.EnsureCompleteAsync(
            progress: null,
            scope: HistoryScope.AllBranches);

        var labels = rows.Single(r => r.FullSha == tip).RefLabels!;
        CollectionAssert.AreEqual(
            new[] { currentBranch, "release", "origin/main" },
            labels.Select(l => l.Name).ToArray());
        CollectionAssert.AreEqual(
            new[] { CommitRefKind.CurrentBranch, CommitRefKind.LocalBranch, CommitRefKind.RemoteBranch },
            labels.Select(l => l.Kind).ToArray());
        Assert.IsTrue(labels[0].IsCurrent);
        Assert.IsFalse(labels.Any(l =>
            l.Name.Equals("origin", StringComparison.OrdinalIgnoreCase)
            || l.Name.EndsWith("/HEAD", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public async Task EnsureCompleteAsync_CurrentBranch_DoesNotAttachBranchRefLabels()
    {
        using var repo = new GitTestRepo();
        var tip = repo.Commit("tip", "a.txt", "1");
        using (var r = new Repository(repo.Path))
        {
            r.CreateBranch("release", r.Head.Tip);
            AddRemoteTrackingRef(r, "origin/main", tip);
        }

        using var cache = new TempCacheDir();
        var backend = new LibGit2Backend();
        var history = new CommitHistoryService(backend, cache.Path);

        await history.OpenAsync(repo.Path, HistoryScope.CurrentBranch);
        var rows = await history.EnsureCompleteAsync(
            progress: null,
            scope: HistoryScope.CurrentBranch);

        Assert.AreEqual(0, rows.Single(r => r.FullSha == tip).RefLabels!.Count);
    }

    private static void BlankGraphColumns(string cachePath)
    {
        using var conn = new SqliteConnection($"Data Source={Path.Combine(cachePath, "history.sqlite")}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE history_commits SET parent_shas = '', ref_labels = '';";
        cmd.ExecuteNonQuery();
    }

    private static void Checkout(string repoPath, string branchName)
    {
        using var repo = new Repository(repoPath);
        Commands.Checkout(repo, repo.Branches[branchName]);
    }

    private static void AddRemoteTrackingRef(Repository repo, string name, string sha)
    {
        var canonicalName = name.StartsWith("refs/", StringComparison.Ordinal)
            ? name
            : $"refs/remotes/{name}";
        repo.Refs.Add(canonicalName, sha, allowOverwrite: true);
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
