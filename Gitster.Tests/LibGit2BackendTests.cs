using Gitster.Services.Git;
using LibGit2Sharp;

namespace Gitster.Tests;

[TestClass]
public sealed class LibGit2BackendTests
{
    [TestMethod]
    public async Task AreCommitsContiguous_TrueForAdjacentRange()
    {
        using var repo = new GitTestRepo();
        repo.Commit("c1", "a.txt", "1");
        var c2 = repo.Commit("c2", "a.txt", "2");
        var c3 = repo.Commit("c3", "a.txt", "3");
        var c4 = repo.Commit("c4", "a.txt", "4");

        var backend = new LibGit2Backend();
        await backend.OpenAsync(repo.Path);

        // Newest-first order, as the commit list provides it.
        Assert.IsTrue(await backend.AreCommitsContiguousAsync(new[] { c4, c3, c2 }));
        Assert.IsTrue(await backend.AreCommitsContiguousAsync(new[] { c3, c2 }));
    }

    [TestMethod]
    public async Task AreCommitsContiguous_FalseWhenGap()
    {
        using var repo = new GitTestRepo();
        var c1 = repo.Commit("c1", "a.txt", "1");
        repo.Commit("c2", "a.txt", "2");
        var c3 = repo.Commit("c3", "a.txt", "3");
        repo.Commit("c4", "a.txt", "4");

        var backend = new LibGit2Backend();
        await backend.OpenAsync(repo.Path);

        // c3 and c1 skip c2 — not contiguous.
        Assert.IsFalse(await backend.AreCommitsContiguousAsync(new[] { c3, c1 }));
    }

    [TestMethod]
    public async Task AreCommitsContiguous_TrueForSingleCommit()
    {
        using var repo = new GitTestRepo();
        repo.Commit("c1", "a.txt", "1");
        var c2 = repo.Commit("c2", "a.txt", "2");

        var backend = new LibGit2Backend();
        await backend.OpenAsync(repo.Path);

        Assert.IsTrue(await backend.AreCommitsContiguousAsync(new[] { c2 }));
    }

    [TestMethod]
    public async Task CherryPick_Conflict_AbortsAndLeavesWorkingTreeClean()
    {
        using var repo = new GitTestRepo();
        repo.Commit("base", "shared.txt", "alpha\n");

        string sideSha;
        using (var r = new Repository(repo.Path))
        {
            var sig = new Signature("Tester", "tester@gitster.test", DateTimeOffset.Now);
            var originalBranch = r.Head.CanonicalName;       // e.g. refs/heads/master|main
            var basis = r.Head.Tip!;

            var side = r.CreateBranch("side", basis);
            Commands.Checkout(r, side);
            System.IO.File.WriteAllText(System.IO.Path.Combine(repo.Path, "shared.txt"), "gamma\n");
            Commands.Stage(r, "shared.txt");
            sideSha = r.Commit("side change", sig, sig).Sha;

            // Return to the original branch so cherry-picking the side commit conflicts.
            Commands.Checkout(r, r.Branches[originalBranch]);
        }

        repo.Commit("main change", "shared.txt", "beta\n");
        var headBefore = repo.Head();

        var backend = new LibGit2Backend();
        await backend.OpenAsync(repo.Path);

        await Assert.ThrowsExceptionAsync<InvalidOperationException>(
            () => backend.CherryPickAsync(sideSha));

        Assert.AreEqual(headBefore, repo.Head(), "HEAD must be unchanged after an aborted cherry-pick");
        using var check = new Repository(repo.Path);
        Assert.AreEqual(CurrentOperation.None, check.Info.CurrentOperation,
            "no cherry-pick state should remain");
        Assert.IsFalse(check.RetrieveStatus().IsDirty, "working tree must be clean after abort");
    }

    [TestMethod]
    public async Task RewriteCommits_RewritesSelectedCommitAndDescendants()
    {
        using var repo = new GitTestRepo();
        var c1 = repo.Commit("c1", "a.txt", "1");
        var c2 = repo.Commit("c2", "a.txt", "2");
        var c3 = repo.Commit("c3", "a.txt", "3");
        string branchName;

        using (var r = new Repository(repo.Path))
        {
            branchName = r.Head.FriendlyName;
        }

        var backend = new LibGit2Backend();
        await backend.OpenAsync(repo.Path);

        await backend.RewriteCommitsAsync([
            new CommitRewrite(c2, NewMessage: "c2 rewritten")
        ]);

        using var check = new Repository(repo.Path);
        Assert.IsFalse(check.Info.IsHeadDetached, "rewriting an older commit must keep the current branch attached");
        Assert.AreEqual(branchName, check.Head.FriendlyName);
        var newHead = check.Head.Tip!;
        var rewrittenC2 = newHead.Parents.Single();

        Assert.AreNotEqual(c3, newHead.Sha, "rewriting an older commit must also rewrite HEAD");
        Assert.AreEqual("c3", newHead.MessageShort, "descendant commit content/message should be replayed");
        Assert.AreNotEqual(c2, rewrittenC2.Sha, "the selected commit should get a new object id");
        Assert.AreEqual("c2 rewritten", rewrittenC2.MessageShort);
        Assert.AreEqual(c1, rewrittenC2.Parents.Single().Sha);
    }

    [TestMethod]
    public async Task ResetMixed_MovesHeadAndKeepsWorkingTreeChanges()
    {
        using var repo = new GitTestRepo();
        var c1 = repo.Commit("c1", "a.txt", "1");
        repo.Commit("c2", "a.txt", "2");

        var backend = new LibGit2Backend();
        await backend.OpenAsync(repo.Path);

        await backend.ResetMixedAsync(c1);

        using var check = new Repository(repo.Path);
        Assert.IsFalse(check.Info.IsHeadDetached);
        Assert.AreEqual(c1, repo.Head());
        Assert.AreEqual("2", System.IO.File.ReadAllText(System.IO.Path.Combine(repo.Path, "a.txt")));
        Assert.IsTrue(check.RetrieveStatus().IsDirty);
    }

    [TestMethod]
    public async Task ResetHard_MovesHeadAndDeletesWorkingTreeChanges()
    {
        using var repo = new GitTestRepo();
        var c1 = repo.Commit("c1", "a.txt", "1");
        repo.Commit("c2", "a.txt", "2");

        var backend = new LibGit2Backend();
        await backend.OpenAsync(repo.Path);

        await backend.ResetHardAsync(c1);

        using var check = new Repository(repo.Path);
        Assert.IsFalse(check.Info.IsHeadDetached);
        Assert.AreEqual(c1, repo.Head());
        Assert.AreEqual("1", System.IO.File.ReadAllText(System.IO.Path.Combine(repo.Path, "a.txt")));
        Assert.IsFalse(check.RetrieveStatus().IsDirty);
    }

    [TestMethod]
    public async Task ResetMixed_FromDetachedHead_ReattachesRequestedBranch()
    {
        using var repo = new GitTestRepo();
        var c1 = repo.Commit("c1", "a.txt", "1");
        var c2 = repo.Commit("c2", "a.txt", "2");
        string branchName;

        using (var r = new Repository(repo.Path))
        {
            branchName = r.Head.FriendlyName;
            Commands.Checkout(r, r.Lookup<Commit>(c2)!);
            Assert.IsTrue(r.Info.IsHeadDetached);
        }

        var backend = new LibGit2Backend();
        await backend.OpenAsync(repo.Path);

        await backend.ResetMixedAsync(c1, branchName);

        using var check = new Repository(repo.Path);
        Assert.IsFalse(check.Info.IsHeadDetached);
        Assert.AreEqual(branchName, check.Head.FriendlyName);
        Assert.AreEqual(c1, check.Head.Tip!.Sha);
        Assert.AreEqual("2", System.IO.File.ReadAllText(System.IO.Path.Combine(repo.Path, "a.txt")));
        Assert.IsTrue(check.RetrieveStatus().IsDirty);
    }

    [TestMethod]
    public async Task ResetHard_FromDetachedHead_ReattachesRequestedBranch()
    {
        using var repo = new GitTestRepo();
        var c1 = repo.Commit("c1", "a.txt", "1");
        var c2 = repo.Commit("c2", "a.txt", "2");
        string branchName;

        using (var r = new Repository(repo.Path))
        {
            branchName = r.Head.FriendlyName;
            Commands.Checkout(r, r.Lookup<Commit>(c2)!);
            Assert.IsTrue(r.Info.IsHeadDetached);
        }

        var backend = new LibGit2Backend();
        await backend.OpenAsync(repo.Path);

        await backend.ResetHardAsync(c1, branchName);

        using var check = new Repository(repo.Path);
        Assert.IsFalse(check.Info.IsHeadDetached);
        Assert.AreEqual(branchName, check.Head.FriendlyName);
        Assert.AreEqual(c1, check.Head.Tip!.Sha);
        Assert.AreEqual("1", System.IO.File.ReadAllText(System.IO.Path.Combine(repo.Path, "a.txt")));
        Assert.IsFalse(check.RetrieveStatus().IsDirty);
    }

    [TestMethod]
    public async Task RewriteCommits_DetachedHead_Throws()
    {
        using var repo = new GitTestRepo();
        repo.Commit("c1", "a.txt", "1");
        var c2 = repo.Commit("c2", "a.txt", "2");

        using (var r = new Repository(repo.Path))
        {
            Commands.Checkout(r, r.Lookup<Commit>(c2)!);
            Assert.IsTrue(r.Info.IsHeadDetached);
        }

        var backend = new LibGit2Backend();
        await backend.OpenAsync(repo.Path);

        await Assert.ThrowsExceptionAsync<InvalidOperationException>(
            () => backend.RewriteCommitsAsync([
                new CommitRewrite(c2, NewMessage: "c2 rewritten")
            ]));
    }

    [TestMethod]
    public async Task RewriteCommits_DetachedHeadWithRequestedBranch_ReattachesAndRewritesBranch()
    {
        using var repo = new GitTestRepo();
        var c1 = repo.Commit("c1", "a.txt", "1");
        var c2 = repo.Commit("c2", "a.txt", "2");
        var c3 = repo.Commit("c3", "a.txt", "3");
        string branchName;

        using (var r = new Repository(repo.Path))
        {
            branchName = r.Head.FriendlyName;
            Commands.Checkout(r, r.Lookup<Commit>(c3)!);
            Assert.IsTrue(r.Info.IsHeadDetached);
        }

        var backend = new LibGit2Backend();
        await backend.OpenAsync(repo.Path);

        await backend.RewriteCommitsAsync([
            new CommitRewrite(c2, NewMessage: "c2 rewritten")
        ], branchName);

        using var check = new Repository(repo.Path);
        Assert.IsFalse(check.Info.IsHeadDetached);
        Assert.AreEqual(branchName, check.Head.FriendlyName);

        var newHead = check.Head.Tip!;
        var rewrittenC2 = newHead.Parents.Single();

        Assert.AreNotEqual(c3, newHead.Sha);
        Assert.AreEqual("c3", newHead.MessageShort);
        Assert.AreNotEqual(c2, rewrittenC2.Sha);
        Assert.AreEqual("c2 rewritten", rewrittenC2.MessageShort);
        Assert.AreEqual(c1, rewrittenC2.Parents.Single().Sha);
    }
}
