using Gitster.Services.Git;
using LibGit2Sharp;

namespace Gitster.Tests;

[TestClass]
public sealed class CommitToBranchTests
{
    private static (LibGit2Backend backend, string otherBranch, string mainHeadBefore) Setup(GitTestRepo repo)
    {
        repo.Commit("base", "base.txt", "base-content");

        string otherName;
        using (var r = new Repository(repo.Path))
        {
            otherName = "feature";
            r.CreateBranch(otherName, r.Head.Tip);
        }

        var backend = new LibGit2Backend();
        backend.OpenAsync(repo.Path).GetAwaiter().GetResult();
        return (backend, otherName, repo.Head());
    }

    [TestMethod]
    public async Task CommitToBranch_Copy_AdvancesTargetAndLeavesWorkingTreeUntouched()
    {
        using var repo = new GitTestRepo();
        var (backend, other, mainHead) = Setup(repo);

        // Stage a change on the current branch.
        repo.Stage("new.txt", "staged-content");

        // Snapshot the working state before the operation.
        var statusBefore = WorkingTreeFingerprint(repo.Path);
        var otherTipBefore = TipSha(repo.Path, other);

        var newSha = await backend.CommitToBranchAsync(new CommitToBranchRequest(
            TargetBranch: other, Message: "moved work", AuthorName: null, AuthorEmail: null,
            IncludeUnstaged: false, RemoveFromCurrent: false));

        // Target advanced to the new commit.
        Assert.AreEqual(newSha, TipSha(repo.Path, other));
        Assert.AreNotEqual(otherTipBefore, TipSha(repo.Path, other));

        // The new commit contains the staged file.
        Assert.IsTrue(TreeContainsPath(repo.Path, newSha, "new.txt"));

        // Current branch HEAD is unchanged.
        Assert.AreEqual(mainHead, repo.Head(), "the current branch must not move (copy mode)");

        // Working tree + index are byte-for-byte unchanged.
        Assert.AreEqual(statusBefore, WorkingTreeFingerprint(repo.Path),
            "copy mode must leave the working tree and index exactly as they were");
    }

    [TestMethod]
    public async Task CommitToBranch_Move_RemovesAddedFileFromCurrentBranch()
    {
        using var repo = new GitTestRepo();
        var (backend, other, _) = Setup(repo);

        repo.Stage("moved.txt", "to-move");

        await backend.CommitToBranchAsync(new CommitToBranchRequest(
            TargetBranch: other, Message: "moved", AuthorName: null, AuthorEmail: null,
            IncludeUnstaged: false, RemoveFromCurrent: true));

        // The added file is now committed on the target...
        Assert.IsTrue(TreeContainsPath(repo.Path, TipSha(repo.Path, other), "moved.txt"));
        // ...and removed from the current working tree (it was new, so deleted).
        Assert.IsFalse(System.IO.File.Exists(System.IO.Path.Combine(repo.Path, "moved.txt")),
            "move mode should remove the newly-added file from the current branch");
    }

    [TestMethod]
    public async Task CommitToBranch_TargetIsCurrentBranch_Throws()
    {
        using var repo = new GitTestRepo();
        var (backend, _, _) = Setup(repo);
        repo.Stage("x.txt", "x");

        string current;
        using (var r = new Repository(repo.Path)) current = r.Head.FriendlyName;

        await Assert.ThrowsExceptionAsync<InvalidOperationException>(
            () => backend.CommitToBranchAsync(new CommitToBranchRequest(
                TargetBranch: current, Message: "m", AuthorName: null, AuthorEmail: null,
                IncludeUnstaged: false, RemoveFromCurrent: false)));
    }

    [TestMethod]
    public async Task Snapshot_WithUncommitted_CreatesBranchWithoutDisturbingWorkingTree()
    {
        using var repo = new GitTestRepo();
        repo.Commit("base", "base.txt", "base");
        var backend = new LibGit2Backend();
        await backend.OpenAsync(repo.Path);

        repo.Stage("wip.txt", "work-in-progress");
        var fingerprintBefore = WorkingTreeFingerprint(repo.Path);
        var headBefore = repo.Head();

        var name = await backend.CreateSnapshotBranchAsync("snapshot/test", includeUncommitted: true);

        Assert.AreEqual("snapshot/test", name);
        // The snapshot branch captured the uncommitted file as a commit on top of HEAD.
        var snapTip = TipSha(repo.Path, "snapshot/test");
        Assert.AreNotEqual(headBefore, snapTip);
        Assert.IsTrue(TreeContainsPath(repo.Path, snapTip, "wip.txt"));

        // Current branch + working tree untouched.
        Assert.AreEqual(headBefore, repo.Head());
        Assert.AreEqual(fingerprintBefore, WorkingTreeFingerprint(repo.Path));
    }

    // ── helpers ──────────────────────────────────────────────────────────

    private static string TipSha(string repoPath, string branch)
    {
        using var r = new Repository(repoPath);
        return r.Branches[branch]!.Tip!.Sha;
    }

    private static bool TreeContainsPath(string repoPath, string commitSha, string path)
    {
        using var r = new Repository(repoPath);
        var commit = r.Lookup<Commit>(commitSha)!;
        return commit.Tree[path] != null;
    }

    /// <summary>A stable fingerprint of the working tree + index for change detection.</summary>
    private static string WorkingTreeFingerprint(string repoPath)
    {
        using var r = new Repository(repoPath);
        var entries = r.RetrieveStatus(new StatusOptions { IncludeUntracked = true })
            .Select(e => $"{e.FilePath}:{e.State}")
            .OrderBy(s => s, StringComparer.Ordinal);
        return string.Join("|", entries);
    }
}
