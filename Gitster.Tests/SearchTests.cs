using Gitster.Core.Git;
using LibGit2Sharp;

namespace Gitster.Tests;

/// <summary>Tests for Phase-4 search/analysis: compare-refs and blame (libgit2), pickaxe (CLI).</summary>
[TestClass]
public sealed class SearchTests
{
    [TestMethod]
    public async Task CompareRefs_TwoDot_ReturnsCommitsOnCompareNotOnBase()
    {
        using var repo = new GitTestRepo();
        repo.Commit("base", "a.txt", "1");
        string main;
        using (var r = new Repository(repo.Path)) main = r.Head.FriendlyName;

        // Branch "feature" with two extra commits.
        using (var r = new Repository(repo.Path))
            Commands.Checkout(r, r.CreateBranch("feature"));
        repo.Commit("f1", "f.txt", "x");
        repo.Commit("f2", "f.txt", "xy");

        var backend = new LibGit2Backend();
        await backend.OpenAsync(repo.Path);

        var result = await backend.CompareRefsAsync(main, "feature", threeDot: false);
        Assert.AreEqual(2, result.Commits.Count, "two commits are on feature but not on main");
        CollectionAssert.AreEquivalent(
            new[] { "f1", "f2" }, result.Commits.Select(c => c.Message).ToArray());
        Assert.IsTrue(result.Diff.Files.Count >= 1);
        StringAssert.Contains(result.Explanation, "two-dot");
    }

    [TestMethod]
    public async Task Blame_AttributesEveryLineToACommit()
    {
        using var repo = new GitTestRepo();
        repo.Commit("c1", "file.txt", "line1\nline2\n");
        repo.Commit("c2", "file.txt", "line1\nline2\nline3\n");

        var backend = new LibGit2Backend();
        await backend.OpenAsync(repo.Path);

        var blame = await backend.BlameAsync("file.txt", ignoreWhitespace: false, followMoves: false);
        Assert.IsTrue(blame.Count >= 3, "every content line should be blamed");
        Assert.IsTrue(blame.All(b => !string.IsNullOrEmpty(b.Sha)));
        Assert.AreEqual(1, blame[0].LineNumber);
    }

    [TestMethod]
    public async Task GetPriorTipFromReflog_ReturnsHeadBeforeLastMove()
    {
        using var repo = new GitTestRepo();
        repo.Commit("c1", "a.txt", "1");
        var firstHead = repo.Head();
        repo.Commit("c2", "a.txt", "2");

        var backend = new LibGit2Backend();
        await backend.OpenAsync(repo.Path);

        var prior = await backend.GetPriorTipFromReflogAsync();
        Assert.AreEqual(firstHead, prior, "prior tip should be the commit before the latest");
    }

    [TestMethod]
    public async Task Pickaxe_FindsAddAndRemoveOfString()
    {
        await GitCli.DetectAsync();
        if (!GitCli.IsAvailable)
            Assert.Inconclusive("Git command-line tool is not available.");

        using var repo = new GitTestRepo();
        repo.Commit("base", "a.txt", "nothing here\n");
        repo.Commit("add needle", "a.txt", "nothing here\nNEEDLE_TOKEN\n");
        repo.Commit("remove needle", "a.txt", "nothing here\n");

        var backend = new HybridGitBackend();
        await backend.OpenAsync(repo.Path);

        var hits = await backend.PickaxeSearchAsync("NEEDLE_TOKEN", null);
        Assert.AreEqual(2, hits.Count, "pickaxe finds the add and the remove");
        CollectionAssert.AreEquivalent(
            new[] { "add needle", "remove needle" }, hits.Select(c => c.Message).ToArray());
    }
}
