using System.IO;

using Gitster.Services.Git;
using LibGit2Sharp;

namespace Gitster.Tests;

[TestClass]
public sealed class LibGit2BackendTests
{
    [TestMethod]
    public async Task GetRefCatalog_ListsBranchesTagsAndUsefulRefs()
    {
        using var repo = new GitTestRepo();
        var baseSha = repo.Commit("base", "base.txt", "1");
        string featureSha;
        using (var r = new Repository(repo.Path))
        {
            r.CreateBranch("feature/work", r.Head.Tip);
            Commands.Checkout(r, r.Branches["feature/work"]);
        }

        featureSha = repo.Commit("feature", "feature.txt", "1");
        using (var r = new Repository(repo.Path))
        {
            Commands.Checkout(r, r.Branches["master"]);
            r.Tags.Add("v-base", r.Lookup<Commit>(baseSha)!);
            r.Refs.Add("refs/remotes/origin/master", baseSha, allowOverwrite: true);
            r.Refs.Add("refs/remotes/origin/HEAD", baseSha, allowOverwrite: true);
            r.Refs.Add("refs/notes/commits", featureSha, allowOverwrite: true);
            r.Refs.Add("refs/bisect/good-test", featureSha, allowOverwrite: true);
        }

        var backend = new LibGit2Backend();
        await backend.OpenAsync(repo.Path);

        var refs = await backend.GetRefCatalogAsync();

        Assert.IsTrue(refs.Any(r => r.Kind == RefCatalogKind.LocalBranch && r.DisplayName == "master" && r.IsCurrent));
        Assert.IsTrue(refs.Any(r => r.Kind == RefCatalogKind.LocalBranch && r.DisplayName == "feature/work" && !r.HasUpstream));
        Assert.IsTrue(refs.Any(r => r.Kind == RefCatalogKind.RemoteBranch && r.DisplayName == "origin/master"));
        Assert.IsTrue(refs.Any(r => r.Kind == RefCatalogKind.Tag && r.DisplayName == "v-base"));
        Assert.IsTrue(refs.Any(r => r.Kind == RefCatalogKind.Note && r.DisplayName == "commits"));
        Assert.IsFalse(refs.Any(r => r.DisplayName.EndsWith("/HEAD", StringComparison.OrdinalIgnoreCase)));
        Assert.IsFalse(refs.Any(r => r.CanonicalName.StartsWith("refs/bisect/", StringComparison.Ordinal)));
    }

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
    public async Task ReorderCommits_IndependentContiguousRange_PreservesTipTree()
    {
        using var repo = new GitTestRepo();
        repo.Commit("base", "base.txt", "base");
        var c2 = repo.Commit("c2", "a.txt", "a");
        var c3 = repo.Commit("c3", "b.txt", "b");
        var c4 = repo.Commit("c4", "c.txt", "c");
        string originalTree;
        using (var r = new Repository(repo.Path))
            originalTree = r.Head.Tip!.Tree.Sha;

        var backend = new LibGit2Backend();
        await backend.OpenAsync(repo.Path);

        await backend.ReorderCommitsAsync(
            [c4, c3, c2],
            [c4, c2, c3]);

        using var check = new Repository(repo.Path);
        Assert.AreEqual(originalTree, check.Head.Tip!.Tree.Sha);
        CollectionAssert.AreEqual(
            new[] { "c4", "c2", "c3", "base" },
            FirstParentMessages(check.Head.Tip!, 4).ToArray());
        Assert.IsFalse(check.RetrieveStatus().IsDirty);
    }

    [TestMethod]
    public async Task SplitCommit_FileLevelSplit_ReplaysDescendantsAndPreservesTipTree()
    {
        using var repo = new GitTestRepo();
        repo.Commit("base", "base.txt", "base");
        var target = CommitFiles(repo.Path, "combined", ("a.txt", "a"), ("b.txt", "b"));
        repo.Commit("descendant", "c.txt", "c");
        string originalTree;
        using (var r = new Repository(repo.Path))
            originalTree = r.Head.Tip!.Tree.Sha;

        var backend = new LibGit2Backend();
        await backend.OpenAsync(repo.Path);

        await backend.SplitCommitAsync(target, ["a.txt"], "part a", "part b");

        using var check = new Repository(repo.Path);
        Assert.AreEqual(originalTree, check.Head.Tip!.Tree.Sha);
        CollectionAssert.AreEqual(
            new[] { "descendant", "part b", "part a", "base" },
            FirstParentMessages(check.Head.Tip!, 4).ToArray());
        Assert.IsFalse(check.RetrieveStatus().IsDirty);
    }

    [TestMethod]
    public async Task CreateOrphanBranch_CurrentTree_CreatesParentlessBranch()
    {
        using var repo = new GitTestRepo();
        repo.Commit("base", "base.txt", "base");
        repo.Commit("tip", "tip.txt", "tip");
        string originalTree;
        using (var r = new Repository(repo.Path))
            originalTree = r.Head.Tip!.Tree.Sha;

        var backend = new LibGit2Backend();
        await backend.OpenAsync(repo.Path);

        var branch = await backend.CreateOrphanBranchAsync("orphan/snapshot", commitCurrentTree: true);

        using var check = new Repository(repo.Path);
        Assert.AreEqual("orphan/snapshot", branch);
        Assert.AreEqual("orphan/snapshot", check.Head.FriendlyName);
        Assert.AreEqual(0, check.Head.Tip!.Parents.Count());
        Assert.AreEqual(originalTree, check.Head.Tip.Tree.Sha);
    }

    [TestMethod]
    public async Task RescueDetachedHead_CreatesBranchAtDetachedHead()
    {
        using var repo = new GitTestRepo();
        var first = repo.Commit("first", "a.txt", "1");
        repo.Commit("second", "b.txt", "2");
        using (var r = new Repository(repo.Path))
            Commands.Checkout(r, r.Lookup<Commit>(first)!);

        var backend = new LibGit2Backend();
        await backend.OpenAsync(repo.Path);

        var branch = await backend.RescueDetachedHeadAsync("rescue/first");

        using var check = new Repository(repo.Path);
        Assert.AreEqual("rescue/first", branch);
        Assert.IsFalse(check.Info.IsHeadDetached);
        Assert.AreEqual("rescue/first", check.Head.FriendlyName);
        Assert.AreEqual(first, check.Head.Tip!.Sha);
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
    public async Task CheckoutCommitDetached_MovesHeadToSelectedCommitDetached()
    {
        using var repo = new GitTestRepo();
        var first = repo.Commit("first", "a.txt", "1");
        repo.Commit("second", "a.txt", "2");

        var backend = new LibGit2Backend();
        await backend.OpenAsync(repo.Path);

        await backend.CheckoutCommitDetachedAsync(first);

        using var check = new Repository(repo.Path);
        Assert.IsTrue(check.Info.IsHeadDetached);
        Assert.AreEqual(first, check.Head.Tip!.Sha);
    }

    [TestMethod]
    public async Task CreateTag_PointsTagAtSelectedCommit()
    {
        using var repo = new GitTestRepo();
        var commit = repo.Commit("tagged", "a.txt", "1");

        var backend = new LibGit2Backend();
        await backend.OpenAsync(repo.Path);

        var tagName = await backend.CreateTagAsync("v-test", commit);

        Assert.AreEqual("v-test", tagName);
        using var check = new Repository(repo.Path);
        Assert.AreEqual(commit, ((Commit)check.Tags["v-test"]!.Target).Sha);
    }

    [TestMethod]
    public async Task GetTagsForCommit_ReturnsTagsPointingAtSelectedCommit()
    {
        using var repo = new GitTestRepo();
        var first = repo.Commit("first", "a.txt", "1");
        var second = repo.Commit("second", "a.txt", "2");

        var backend = new LibGit2Backend();
        await backend.OpenAsync(repo.Path);
        await backend.CreateTagAsync("v-first", first);
        await backend.CreateTagAsync("release/first", first);
        await backend.CreateTagAsync("v-second", second);

        var tags = await backend.GetTagsForCommitAsync(first);

        CollectionAssert.AreEqual(new[] { "release/first", "v-first" }, tags.ToArray());
    }

    [TestMethod]
    public async Task RevertCommit_CleanCommit_CreatesRevertCommit()
    {
        using var repo = new GitTestRepo();
        repo.Commit("first", "a.txt", "1\n");
        var second = repo.Commit("second", "a.txt", "2\n");
        var headBefore = repo.Head();

        var backend = new LibGit2Backend();
        await backend.OpenAsync(repo.Path);

        await backend.RevertCommitAsync(second);

        using var check = new Repository(repo.Path);
        Assert.AreNotEqual(headBefore, check.Head.Tip!.Sha);
        StringAssert.StartsWith(check.Head.Tip.MessageShort, "Revert");
        Assert.AreEqual("1\n", File.ReadAllText(Path.Combine(repo.Path, "a.txt")));
    }

    [TestMethod]
    public async Task RevertCommit_Conflict_AbortsAndLeavesWorkingTreeClean()
    {
        using var repo = new GitTestRepo();
        repo.Commit("first", "a.txt", "1\n");
        var second = repo.Commit("second", "a.txt", "2\n");
        repo.Commit("third", "a.txt", "3\n");
        var headBefore = repo.Head();

        var backend = new LibGit2Backend();
        await backend.OpenAsync(repo.Path);

        await Assert.ThrowsExceptionAsync<InvalidOperationException>(
            () => backend.RevertCommitAsync(second));

        Assert.AreEqual(headBefore, repo.Head());
        using var check = new Repository(repo.Path);
        Assert.AreEqual(CurrentOperation.None, check.Info.CurrentOperation);
        Assert.IsFalse(check.RetrieveStatus().IsDirty);
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
    public async Task RewriteCommits_MultipleMetadataEdits_AppliesOneBatchAndRewritesDescendants()
    {
        using var repo = new GitTestRepo();
        var c1 = repo.Commit("c1", "a.txt", "1");
        var c2 = repo.Commit("c2", "a.txt", "2");
        var c3 = repo.Commit("c3", "a.txt", "3");
        var c4 = repo.Commit("c4", "a.txt", "4");

        var newDate = new DateTimeOffset(2026, 2, 3, 4, 5, 6, TimeSpan.Zero);
        var backend = new LibGit2Backend();
        await backend.OpenAsync(repo.Path);

        await backend.RewriteCommitsAsync([
            new CommitRewrite(c2,
                NewMessage: "c2 rewritten",
                NewAuthorName: "Alice",
                NewAuthorEmail: "alice@gitster.test"),
            new CommitRewrite(c3,
                NewAuthorDate: newDate,
                NewCommitterDate: newDate)
        ]);

        using var check = new Repository(repo.Path);
        Assert.IsFalse(check.Info.IsHeadDetached);

        var newHead = check.Head.Tip!;
        var rewrittenC3 = newHead.Parents.Single();
        var rewrittenC2 = rewrittenC3.Parents.Single();

        Assert.AreNotEqual(c4, newHead.Sha, "descendant HEAD should be rewritten");
        Assert.AreNotEqual(c3, rewrittenC3.Sha, "second edited commit should be rewritten");
        Assert.AreNotEqual(c2, rewrittenC2.Sha, "first edited commit should be rewritten");
        Assert.AreEqual("c4", newHead.MessageShort);
        Assert.AreEqual("c2 rewritten", rewrittenC2.MessageShort);
        Assert.AreEqual("Alice", rewrittenC2.Author.Name);
        Assert.AreEqual("alice@gitster.test", rewrittenC2.Author.Email);
        Assert.AreEqual(newDate, rewrittenC3.Author.When);
        Assert.AreEqual(newDate, rewrittenC3.Committer.When);
        Assert.AreEqual(c1, rewrittenC2.Parents.Single().Sha);
    }

    [TestMethod]
    public async Task RemoveFileChangeFromCommit_AddedFile_RemovesFromCommitAndLeavesFileStaged()
    {
        using var repo = new GitTestRepo();
        repo.Commit("base", "base.txt", "base");
        var target = repo.Commit("add foo", "foo.bar", "foo");
        var headBefore = repo.Commit("other work", "other.txt", "other");

        var backend = new LibGit2Backend();
        await backend.OpenAsync(repo.Path);

        await backend.RemoveFileChangeFromCommitAsync(target, "foo.bar");

        using var check = new Repository(repo.Path);
        Assert.IsFalse(check.Info.IsHeadDetached);
        Assert.AreNotEqual(headBefore, check.Head.Tip!.Sha, "descendant HEAD should be rewritten");
        Assert.IsNull(check.Head.Tip.Tree["foo.bar"], "rewritten branch history should no longer contain the added file");
        Assert.IsNotNull(check.Head.Tip.Tree["other.txt"], "unrelated descendant content should be replayed");

        var rewrittenTarget = check.Head.Tip.Parents.Single();
        Assert.AreEqual("add foo", rewrittenTarget.MessageShort);
        Assert.IsNull(rewrittenTarget.Tree["foo.bar"], "selected commit should no longer contain the added file");

        var status = check.RetrieveStatus(new StatusOptions { IncludeUntracked = true });
        var foo = status.Single(e => e.FilePath == "foo.bar");
        Assert.IsTrue((foo.State & FileStatus.NewInIndex) != 0, "removed change should be staged as an add");
        Assert.IsFalse((foo.State & FileStatus.NewInWorkdir) != 0, "removed change should not also be unstaged");
        Assert.AreEqual("foo", System.IO.File.ReadAllText(System.IO.Path.Combine(repo.Path, "foo.bar")));
    }

    [TestMethod]
    public async Task RemoveFileChangeFromCommit_ModifiedFile_RestoresCommitAndLeavesModificationStaged()
    {
        using var repo = new GitTestRepo();
        repo.Commit("base", "tracked.txt", "one");
        var target = repo.Commit("modify tracked", "tracked.txt", "two");
        repo.Commit("other work", "other.txt", "other");

        var backend = new LibGit2Backend();
        await backend.OpenAsync(repo.Path);

        await backend.RemoveFileChangeFromCommitAsync(target, "tracked.txt");

        using var check = new Repository(repo.Path);
        Assert.AreEqual("one", ((Blob)check.Head.Tip!["tracked.txt"].Target).GetContentText());
        Assert.AreEqual("two", System.IO.File.ReadAllText(System.IO.Path.Combine(repo.Path, "tracked.txt")));

        var status = check.RetrieveStatus();
        var tracked = status.Single(e => e.FilePath == "tracked.txt");
        Assert.IsTrue((tracked.State & FileStatus.ModifiedInIndex) != 0, "removed modification should be staged");
        Assert.IsFalse((tracked.State & FileStatus.ModifiedInWorkdir) != 0, "removed modification should not also be unstaged");
    }

    [TestMethod]
    public async Task RemoveFileChangeFromCommit_LaterCommitTouchesSamePath_ThrowsAndLeavesHeadUnchanged()
    {
        using var repo = new GitTestRepo();
        repo.Commit("base", "base.txt", "base");
        var target = repo.Commit("add foo", "foo.bar", "one");
        var headBefore = repo.Commit("modify foo", "foo.bar", "two");

        var backend = new LibGit2Backend();
        await backend.OpenAsync(repo.Path);

        var ex = await Assert.ThrowsExceptionAsync<InvalidOperationException>(
            () => backend.RemoveFileChangeFromCommitAsync(target, "foo.bar"));

        StringAssert.Contains(ex.Message, "later commit");
        using var check = new Repository(repo.Path);
        Assert.AreEqual(headBefore, check.Head.Tip!.Sha);
        Assert.IsFalse(check.RetrieveStatus().IsDirty);
    }

    [TestMethod]
    public async Task RemoveFileChangeFromCommit_DirtyWorkingTree_ThrowsAndLeavesHeadUnchanged()
    {
        using var repo = new GitTestRepo();
        repo.Commit("base", "base.txt", "base");
        var target = repo.Commit("add foo", "foo.bar", "foo");
        var headBefore = repo.Commit("other work", "other.txt", "other");
        System.IO.File.WriteAllText(System.IO.Path.Combine(repo.Path, "dirty.txt"), "dirty");

        var backend = new LibGit2Backend();
        await backend.OpenAsync(repo.Path);

        var ex = await Assert.ThrowsExceptionAsync<InvalidOperationException>(
            () => backend.RemoveFileChangeFromCommitAsync(target, "foo.bar"));

        StringAssert.Contains(ex.Message, "uncommitted changes");
        using var check = new Repository(repo.Path);
        Assert.AreEqual(headBefore, check.Head.Tip!.Sha);
        Assert.IsTrue(System.IO.File.Exists(System.IO.Path.Combine(repo.Path, "dirty.txt")));
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
    public async Task ServerOperations_ThrowBecauseLibGit2BackendIsLocalOnly()
    {
        var backend = new LibGit2Backend();

        await AssertLocalOnlyAsync(() => backend.FetchAsync());
        await AssertLocalOnlyAsync(() => backend.PullAsync());
        await AssertLocalOnlyAsync(() => backend.PushAsync());
        await AssertLocalOnlyAsync(() => backend.PushThroughCommitAsync("abc123"));
        await AssertLocalOnlyAsync(() => backend.PushTagAsync("v-test"));
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

    [TestMethod]
    public async Task MergeBranch_FastForwardOnly_AdvancesCurrentBranchWithoutMergeCommit()
    {
        using var repo = new GitTestRepo();
        var baseSha = repo.Commit("base", "base.txt", "base");
        string currentBranch;
        string featureTip;

        using (var r = new Repository(repo.Path))
        {
            currentBranch = r.Head.FriendlyName;
            var feature = r.CreateBranch("feature", r.Head.Tip);
            Commands.Checkout(r, feature);
            System.IO.File.WriteAllText(System.IO.Path.Combine(repo.Path, "feature.txt"), "feature");
            Commands.Stage(r, "feature.txt");
            var sig = new Signature("Tester", "tester@gitster.test", DateTimeOffset.Now);
            featureTip = r.Commit("feature work", sig, sig).Sha;
            Commands.Checkout(r, r.Branches[currentBranch]);
        }

        var backend = new LibGit2Backend();
        await backend.OpenAsync(repo.Path);

        var result = await backend.MergeBranchAsync("feature", BranchMergeStrategy.FastForwardOnly);

        Assert.AreEqual(BranchMergeOutcome.FastForward, result.Outcome);
        Assert.AreEqual(featureTip, result.HeadSha);

        using var check = new Repository(repo.Path);
        Assert.IsFalse(check.Info.IsHeadDetached);
        Assert.AreEqual(currentBranch, check.Head.FriendlyName);
        Assert.AreEqual(featureTip, check.Head.Tip!.Sha);
        Assert.AreEqual(baseSha, check.Head.Tip.Parents.Single().Sha);
    }

    [TestMethod]
    public async Task MergeBranch_NoFastForward_CreatesMergeCommitWhenFastForwardIsPossible()
    {
        using var repo = new GitTestRepo();
        var baseSha = repo.Commit("base", "base.txt", "base");
        string currentBranch;
        string featureTip;

        using (var r = new Repository(repo.Path))
        {
            currentBranch = r.Head.FriendlyName;
            var feature = r.CreateBranch("feature", r.Head.Tip);
            Commands.Checkout(r, feature);
            System.IO.File.WriteAllText(System.IO.Path.Combine(repo.Path, "feature.txt"), "feature");
            Commands.Stage(r, "feature.txt");
            var sig = new Signature("Tester", "tester@gitster.test", DateTimeOffset.Now);
            featureTip = r.Commit("feature work", sig, sig).Sha;
            Commands.Checkout(r, r.Branches[currentBranch]);
        }

        var backend = new LibGit2Backend();
        await backend.OpenAsync(repo.Path);

        var result = await backend.MergeBranchAsync("feature", BranchMergeStrategy.NoFastForward);

        Assert.AreEqual(BranchMergeOutcome.MergeCommit, result.Outcome);
        Assert.AreNotEqual(featureTip, result.HeadSha);

        using var check = new Repository(repo.Path);
        Assert.IsFalse(check.Info.IsHeadDetached);
        Assert.AreEqual(currentBranch, check.Head.FriendlyName);
        var mergeCommit = check.Head.Tip!;
        Assert.AreEqual(2, mergeCommit.Parents.Count());
        Assert.IsTrue(mergeCommit.Parents.Any(p => p.Sha == baseSha));
        Assert.IsTrue(mergeCommit.Parents.Any(p => p.Sha == featureTip));
    }

    [TestMethod]
    public async Task MergeBranch_FastForwardOnly_WhenDiverged_ThrowsAndLeavesHeadUnchanged()
    {
        using var repo = new GitTestRepo();
        repo.Commit("base", "shared.txt", "base");
        string currentBranch;

        using (var r = new Repository(repo.Path))
        {
            currentBranch = r.Head.FriendlyName;
            var feature = r.CreateBranch("feature", r.Head.Tip);
            Commands.Checkout(r, feature);
            System.IO.File.WriteAllText(System.IO.Path.Combine(repo.Path, "feature.txt"), "feature");
            Commands.Stage(r, "feature.txt");
            var sig = new Signature("Tester", "tester@gitster.test", DateTimeOffset.Now);
            r.Commit("feature work", sig, sig);
            Commands.Checkout(r, r.Branches[currentBranch]);
        }

        var beforeHead = repo.Commit("main work", "main.txt", "main");

        var backend = new LibGit2Backend();
        await backend.OpenAsync(repo.Path);

        await Assert.ThrowsExceptionAsync<InvalidOperationException>(
            () => backend.MergeBranchAsync("feature", BranchMergeStrategy.FastForwardOnly));

        using var check = new Repository(repo.Path);
        Assert.IsFalse(check.Info.IsHeadDetached);
        Assert.AreEqual(currentBranch, check.Head.FriendlyName);
        Assert.AreEqual(beforeHead, check.Head.Tip!.Sha);
        Assert.IsFalse(check.RetrieveStatus().IsDirty);
    }

    private static string CommitFiles(string repoPath, string message, params (string Path, string Content)[] files)
    {
        using var repo = new Repository(repoPath);
        foreach (var file in files)
        {
            System.IO.File.WriteAllText(System.IO.Path.Combine(repoPath, file.Path), file.Content);
            Commands.Stage(repo, file.Path);
        }

        var sig = new Signature("Tester", "tester@gitster.test", DateTimeOffset.Now);
        return repo.Commit(message, sig, sig).Sha;
    }

    private static IEnumerable<string> FirstParentMessages(Commit tip, int maxCount)
    {
        Commit? current = tip;
        for (var i = 0; current is not null && i < maxCount; i++)
        {
            yield return current.MessageShort;
            current = current.Parents.FirstOrDefault();
        }
    }

    private static async Task AssertLocalOnlyAsync(Func<Task> operation)
    {
        var ex = await Assert.ThrowsExceptionAsync<NotSupportedException>(operation);

        StringAssert.Contains(ex.Message, "LibGit2Backend is local-only");
    }
}
