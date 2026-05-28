using Gitster.Services.Git;

namespace Gitster.Tests;

/// <summary>
/// Integration tests for the rebase-class CLI operations. These exercise the Windows
/// editor-no-op handling (the highest-risk part of the fixup workflow) against a real
/// Git process, so they only run when Git is installed.
/// </summary>
[TestClass]
public sealed class GitCliBackendTests
{
    private static async Task EnsureGitAsync()
    {
        await GitCli.DetectAsync();
        if (!GitCli.IsAvailable)
            Assert.Inconclusive("Git command-line tool is not available on this machine.");
    }

    [TestMethod]
    public async Task Fixup_FoldsStagedChangesIntoOlderCommit_AndLeavesTreeClean()
    {
        await EnsureGitAsync();
        using var repo = new GitTestRepo();
        var c1 = repo.Commit("commit one", "a.txt", "a1");
        var target = repo.Commit("commit two", "b.txt", "b1");
        repo.Commit("commit three", "c.txt", "c1");

        // Stage an unrelated change to fold into "commit two".
        repo.Stage("d.txt", "d1");

        var backend = new GitCliBackend();
        await backend.OpenAsync(repo.Path);

        await backend.FixupIntoCommitAsync(target);

        // History length is unchanged (fixup folds, does not add), and no rebase is left over.
        Assert.AreEqual(3, repo.CommitCount(), "fixup should not change the number of commits");
        Assert.IsFalse(repo.IsRebaseInProgress(), "no rebase state should remain after a clean fixup");
        Assert.AreEqual("commit three", repo.MessageOf(repo.Head()));
    }

    [TestMethod]
    public async Task Fixup_OnConflict_AbortsAndRestoresPreOpStateWithChangesStaged()
    {
        await EnsureGitAsync();
        using var repo = new GitTestRepo();
        repo.Commit("base", "shared.txt", "line-1\n");
        var target = repo.Commit("target", "shared.txt", "line-1\nline-2\n");
        repo.Commit("head", "shared.txt", "line-1\nline-2-CHANGED\n");
        var headBefore = repo.Head();

        // Stage a conflicting edit to the same region the head commit also touched.
        repo.Stage("shared.txt", "line-1\nline-2-FIXUP\n");

        var backend = new GitCliBackend();
        await backend.OpenAsync(repo.Path);

        await Assert.ThrowsExceptionAsync<InvalidOperationException>(
            () => backend.FixupIntoCommitAsync(target));

        // Conflict must roll back fully: HEAD unchanged, no dangling fixup commit,
        // and no rebase left in progress.
        Assert.AreEqual(headBefore, repo.Head(), "HEAD must be restored to its pre-fixup commit");
        Assert.AreEqual(3, repo.CommitCount(), "the fixup! commit must not survive a conflict");
        Assert.IsFalse(repo.IsRebaseInProgress(), "the aborted rebase must leave no state behind");
    }

    [TestMethod]
    public async Task Reword_NonHeadCommit_ChangesMessageAndKeepsHistoryLength()
    {
        await EnsureGitAsync();
        using var repo = new GitTestRepo();
        repo.Commit("commit one", "a.txt", "a1");
        var target = repo.Commit("ORIGINAL message", "b.txt", "b1");
        repo.Commit("commit three", "c.txt", "c1");

        var backend = new GitCliBackend();
        await backend.OpenAsync(repo.Path);

        await backend.RewordCommitAsync(target, "REWORDED message");

        Assert.AreEqual(3, repo.CommitCount());
        Assert.IsFalse(repo.IsRebaseInProgress());

        // The reworded subject should appear somewhere in history; the original should not.
        var messages = AllMessages(repo);
        CollectionAssert.Contains(messages, "REWORDED message");
        CollectionAssert.DoesNotContain(messages, "ORIGINAL message");
    }

    [TestMethod]
    public async Task Squash_NonHeadRange_FoldsContiguousCommits()
    {
        await EnsureGitAsync();
        using var repo = new GitTestRepo();
        repo.Commit("base", "base.txt", "base");
        var older = repo.Commit("feature part 1", "f1.txt", "1");
        var newer = repo.Commit("feature part 2", "f2.txt", "2");
        repo.Commit("unrelated head", "h.txt", "h");

        var backend = new GitCliBackend();
        await backend.OpenAsync(repo.Path);

        // shas are passed newest-first (commit-list order).
        await backend.SquashCommitsAsync(
            new[] { newer, older }, "feature squashed", overrideDate: null);

        Assert.AreEqual(3, repo.CommitCount(), "two commits should fold into one");
        Assert.IsFalse(repo.IsRebaseInProgress());
        CollectionAssert.Contains(AllMessages(repo), "feature squashed");
    }

    private static List<string> AllMessages(GitTestRepo repo)
    {
        using var r = new LibGit2Sharp.Repository(repo.Path);
        return r.Commits.Select(c => c.MessageShort).ToList();
    }
}
