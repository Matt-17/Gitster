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
}
