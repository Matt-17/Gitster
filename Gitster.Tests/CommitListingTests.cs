using Gitster.Services.Git;
using LibGit2Sharp;

namespace Gitster.Tests;

/// <summary>Tests for the A0 progressive-loading / remote-state and A2 commit-panel backend.</summary>
[TestClass]
public sealed class CommitListingTests
{
    [TestMethod]
    public async Task EnumerateCommits_YieldsHeadFirstNewestToOldest()
    {
        using var repo = new GitTestRepo();
        repo.Commit("c1", "a.txt", "1");
        repo.Commit("c2", "a.txt", "2");
        var c3 = repo.Commit("c3", "a.txt", "3");

        var backend = new LibGit2Backend();
        await backend.OpenAsync(repo.Path);

        var list = new List<CommitInfo>();
        await foreach (var c in backend.EnumerateCommitsAsync())
            list.Add(c);

        Assert.AreEqual(3, list.Count);
        Assert.AreEqual("c3", list[0].Message, "HEAD must be first");
        Assert.AreEqual("c2", list[1].Message);
        Assert.AreEqual("c1", list[2].Message, "root must be last");
        Assert.AreEqual(c3, list[0].FullSha);
    }

    [TestMethod]
    public async Task ComputeRemoteSets_NoRemote_ReportsNoTracking()
    {
        using var repo = new GitTestRepo();
        repo.Commit("c1", "a.txt", "1");

        var backend = new LibGit2Backend();
        await backend.OpenAsync(repo.Path);

        var sets = await backend.ComputeRemoteSetsAsync();
        Assert.IsFalse(sets.HasRemote);
        Assert.IsFalse(sets.HasTrackingBranch);
        Assert.AreEqual(0, sets.Incoming.Count);
        Assert.AreEqual(0, sets.OutgoingFullShas.Count);
    }

    [TestMethod]
    public async Task GetWorkingTreeStatus_SeparatesStagedAndUnstaged()
    {
        using var repo = new GitTestRepo();
        repo.Commit("base", "a.txt", "1\n");

        // Stage a new file; leave another untracked.
        repo.Stage("staged.txt", "new staged\n");
        System.IO.File.WriteAllText(System.IO.Path.Combine(repo.Path, "untracked.txt"), "loose\n");

        var backend = new LibGit2Backend();
        await backend.OpenAsync(repo.Path);

        var status = await backend.GetWorkingTreeStatusAsync();

        Assert.IsTrue(status.Staged.Any(f => f.Path == "staged.txt" && f.Staged));
        Assert.IsTrue(status.Unstaged.Any(f => f.Path == "untracked.txt"
            && f.Status == WorkingFileStatus.Untracked));
    }

    [TestMethod]
    public async Task Commit_CreatesCommitOnBranch_WithoutDetaching()
    {
        using var repo = new GitTestRepo();
        repo.Commit("base", "a.txt", "1\n");
        string branchBefore;
        using (var r = new Repository(repo.Path)) branchBefore = r.Head.FriendlyName;

        var backend = new LibGit2Backend();
        await backend.OpenAsync(repo.Path);

        repo.Stage("b.txt", "two\n");
        var sha = await backend.CommitAsync(new CommitRequest("add b"));

        using var check = new Repository(repo.Path);
        Assert.IsFalse(check.Info.IsHeadDetached, "commit must not detach HEAD");
        Assert.AreEqual(branchBefore, check.Head.FriendlyName);
        Assert.AreEqual(sha, check.Head.Tip!.Sha);
        Assert.AreEqual("add b", check.Head.Tip!.MessageShort);
    }

    [TestMethod]
    public async Task Commit_Amend_ReplacesHeadKeepsBranch()
    {
        using var repo = new GitTestRepo();
        repo.Commit("base", "a.txt", "1\n");
        var original = repo.Commit("to amend", "a.txt", "2\n");

        var backend = new LibGit2Backend();
        await backend.OpenAsync(repo.Path);

        var sha = await backend.CommitAsync(new CommitRequest("amended message", Amend: true));

        using var check = new Repository(repo.Path);
        Assert.AreNotEqual(original, sha, "amend rewrites the commit");
        Assert.AreEqual("amended message", check.Head.Tip!.MessageShort);
        Assert.IsFalse(check.Info.IsHeadDetached);
        Assert.AreEqual(2, check.Commits.Count(), "amend must not add a commit");
    }

    [TestMethod]
    public async Task GetCommitDiff_RootCommit_ComparesAgainstEmptyTree()
    {
        using var repo = new GitTestRepo();
        var root = repo.Commit("root", "a.txt", "line1\nline2\n");

        var backend = new LibGit2Backend();
        await backend.OpenAsync(repo.Path);

        var diff = await backend.GetCommitDiffAsync(root);
        Assert.AreEqual(1, diff.Files.Count);
        Assert.AreEqual("a.txt", diff.Files[0].Path);
        Assert.AreEqual("A", diff.Files[0].Status);
        Assert.AreEqual(2, diff.LinesAdded);
    }
}
