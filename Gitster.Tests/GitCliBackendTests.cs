using System.IO;
using System.IO.Compression;

using Gitster.Services.Git;

using LibGit2Sharp;

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

    [TestMethod]
    public async Task ArchiveSourceZip_Head_CreatesZipWithPrefixAndTrackedFiles()
    {
        await EnsureGitAsync();
        using var repo = new GitTestRepo();
        var head = repo.Commit("initial", "tracked.txt", "hello");
        var output = TempZipPath();

        try
        {
            var backend = new GitCliBackend();
            await backend.OpenAsync(repo.Path);

            var result = await backend.ArchiveSourceZipAsync(new ArchiveRequest("HEAD", output, "head-export"));
            var entries = ReadZipEntries(output);

            Assert.AreEqual(System.IO.Path.GetFullPath(output), result.OutputPath);
            Assert.AreEqual(head, result.TreeishSha);
            Assert.IsTrue(result.SizeBytes > 0);
            Assert.AreEqual("hello", entries["head-export/tracked.txt"]);
        }
        finally
        {
            DeleteIfExists(output);
        }
    }

    [TestMethod]
    public async Task ArchiveSourceZip_OlderCommit_UsesThatCommitTreeAndExcludesLaterFiles()
    {
        await EnsureGitAsync();
        using var repo = new GitTestRepo();
        var older = repo.Commit("older", "old.txt", "old");
        repo.Commit("newer", "new.txt", "new");
        var output = TempZipPath();

        try
        {
            var backend = new GitCliBackend();
            await backend.OpenAsync(repo.Path);

            await backend.ArchiveSourceZipAsync(new ArchiveRequest(older, output, "old-export"));
            var entries = ReadZipEntries(output);

            Assert.AreEqual("old", entries["old-export/old.txt"]);
            Assert.IsFalse(entries.ContainsKey("old-export/new.txt"));
        }
        finally
        {
            DeleteIfExists(output);
        }
    }

    [TestMethod]
    public async Task ArchiveSourceZip_OtherBranch_ExportsBranchFilesAndLeavesHeadUnchanged()
    {
        await EnsureGitAsync();
        using var repo = new GitTestRepo();
        repo.Commit("base", "base.txt", "base");
        var currentHead = repo.Head();

        using (var r = new Repository(repo.Path))
        {
            var originalBranch = r.Head.FriendlyName;
            var branch = r.CreateBranch("feature/archive");
            Commands.Checkout(r, branch);
            System.IO.File.WriteAllText(System.IO.Path.Combine(repo.Path, "feature.txt"), "feature");
            Commands.Stage(r, "feature.txt");
            var sig = new Signature("Tester", "tester@gitster.test", DateTimeOffset.Now);
            r.Commit("feature work", sig, sig);
            Commands.Checkout(r, r.Branches[originalBranch]);
        }

        var output = TempZipPath();
        try
        {
            var backend = new GitCliBackend();
            await backend.OpenAsync(repo.Path);

            await backend.ArchiveSourceZipAsync(new ArchiveRequest("feature/archive", output, "branch-export"));
            var entries = ReadZipEntries(output);

            Assert.AreEqual(currentHead, repo.Head(), "archiving another branch must not move HEAD");
            Assert.AreEqual("feature", entries["branch-export/feature.txt"]);
            Assert.IsFalse(entries.ContainsKey("branch-export/.git/HEAD"));
        }
        finally
        {
            DeleteIfExists(output);
        }
    }

    [TestMethod]
    public async Task ArchiveSourceZip_DirtyWorkingTree_ExportsCommittedContentOnly()
    {
        await EnsureGitAsync();
        using var repo = new GitTestRepo();
        repo.Commit("initial", "tracked.txt", "committed");
        System.IO.File.WriteAllText(System.IO.Path.Combine(repo.Path, "tracked.txt"), "dirty");
        System.IO.File.WriteAllText(System.IO.Path.Combine(repo.Path, "untracked.txt"), "untracked");
        var output = TempZipPath();

        try
        {
            var backend = new GitCliBackend();
            await backend.OpenAsync(repo.Path);

            await backend.ArchiveSourceZipAsync(new ArchiveRequest("HEAD", output, "dirty-export"));
            var entries = ReadZipEntries(output);

            Assert.AreEqual("committed", entries["dirty-export/tracked.txt"]);
            Assert.IsFalse(entries.ContainsKey("dirty-export/untracked.txt"));
        }
        finally
        {
            DeleteIfExists(output);
        }
    }

    [TestMethod]
    public async Task ArchiveSourceZip_InvalidRef_FailsAndDoesNotCreateArchive()
    {
        await EnsureGitAsync();
        using var repo = new GitTestRepo();
        repo.Commit("initial", "tracked.txt", "hello");
        var output = TempZipPath();

        var backend = new GitCliBackend();
        await backend.OpenAsync(repo.Path);

        await Assert.ThrowsExceptionAsync<InvalidOperationException>(
            () => backend.ArchiveSourceZipAsync(new ArchiveRequest("missing-ref", output, "bad-export")));
        Assert.IsFalse(System.IO.File.Exists(output));
    }

    [TestMethod]
    public async Task ArchiveSourceZip_InvalidOutputPath_FailsAndCleansTemporaryArchive()
    {
        await EnsureGitAsync();
        using var repo = new GitTestRepo();
        repo.Commit("initial", "tracked.txt", "hello");
        var parent = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "gitster-archive-invalid-" + Guid.NewGuid().ToString("N"));
        var invalidOutput = System.IO.Path.Combine(parent, "existing-directory");
        Directory.CreateDirectory(invalidOutput);

        try
        {
            var backend = new GitCliBackend();
            await backend.OpenAsync(repo.Path);

            await Assert.ThrowsExceptionAsync<InvalidOperationException>(
                () => backend.ArchiveSourceZipAsync(new ArchiveRequest("HEAD", invalidOutput, "bad-output")));

            Assert.IsTrue(Directory.Exists(invalidOutput), "invalid archive output must not delete the target directory");
            Assert.AreEqual(0, Directory.EnumerateFiles(parent, ".gitster-archive-*.tmp").Count());
        }
        finally
        {
            try
            {
                Directory.Delete(parent, recursive: true);
            }
            catch
            {
            }
        }
    }

    [TestMethod]
    public async Task Fetch_RemoteHasNewCommit_UpdatesRemoteTrackingBranchOnly()
    {
        await EnsureGitAsync();
        using var repo = new GitTestRepo();
        var baseSha = repo.Commit("base", "base.txt", "base");
        var remotePath = TempDirectoryPath("gitster-remote-");

        try
        {
            var branch = await AddBareOriginAndPushAsync(repo, remotePath);
            var remoteTip = await PushCommitFromCloneAsync(remotePath, branch, "remote update", "remote.txt", "remote");

            var backend = new GitCliBackend();
            await backend.OpenAsync(repo.Path);

            await backend.FetchAsync("origin");

            using var check = new Repository(repo.Path);
            Assert.AreEqual(baseSha, check.Head.Tip!.Sha, "fetch must not move local HEAD");
            Assert.AreEqual(remoteTip, check.Branches[$"origin/{branch}"]!.Tip!.Sha);
        }
        finally
        {
            DeleteDirectoryIfExists(remotePath);
        }
    }

    [TestMethod]
    public async Task Pull_RemoteHasLinearUpdate_FastForwardsCurrentBranch()
    {
        await EnsureGitAsync();
        using var repo = new GitTestRepo();
        repo.Commit("base", "base.txt", "base");
        var remotePath = TempDirectoryPath("gitster-remote-");

        try
        {
            var branch = await AddBareOriginAndPushAsync(repo, remotePath);
            var remoteTip = await PushCommitFromCloneAsync(remotePath, branch, "remote update", "remote.txt", "remote");

            var backend = new GitCliBackend();
            await backend.OpenAsync(repo.Path);

            await backend.PullAsync("origin");

            using var check = new Repository(repo.Path);
            Assert.AreEqual(remoteTip, check.Head.Tip!.Sha);
        }
        finally
        {
            DeleteDirectoryIfExists(remotePath);
        }
    }

    [TestMethod]
    public async Task Push_LocalCommit_UpdatesRemoteAndTrackingRef()
    {
        await EnsureGitAsync();
        using var repo = new GitTestRepo();
        repo.Commit("base", "base.txt", "base");
        var remotePath = TempDirectoryPath("gitster-remote-");

        try
        {
            var branch = await AddBareOriginAndPushAsync(repo, remotePath);
            var localTip = repo.Commit("local update", "local.txt", "local");

            var backend = new GitCliBackend();
            await backend.OpenAsync(repo.Path);

            await backend.PushAsync("origin");

            using var local = new Repository(repo.Path);
            using var remote = new Repository(remotePath);
            Assert.AreEqual(localTip, remote.Branches[branch]!.Tip!.Sha);
            Assert.AreEqual(localTip, local.Head.TrackedBranch.Tip!.Sha);
        }
        finally
        {
            DeleteDirectoryIfExists(remotePath);
        }
    }

    [TestMethod]
    public async Task PushThroughCommit_LocalOnlyCommit_PushesSelectedCommitOnly()
    {
        await EnsureGitAsync();
        using var repo = new GitTestRepo();
        repo.Commit("base", "base.txt", "base");
        var remotePath = TempDirectoryPath("gitster-remote-");

        try
        {
            var branch = await AddBareOriginAndPushAsync(repo, remotePath);
            var selected = repo.Commit("selected", "selected.txt", "selected");
            var newer = repo.Commit("newer", "newer.txt", "newer");

            var backend = new GitCliBackend();
            await backend.OpenAsync(repo.Path);

            await backend.PushThroughCommitAsync(selected);

            using var local = new Repository(repo.Path);
            using var remote = new Repository(remotePath);
            Assert.AreEqual(newer, local.Head.Tip!.Sha, "partial push must not move local HEAD");
            Assert.AreEqual(selected, local.Head.TrackedBranch.Tip!.Sha, "tracking ref should reflect the partial push");
            Assert.AreEqual(selected, remote.Branches[branch]!.Tip!.Sha, "remote branch should stop at the selected commit");
        }
        finally
        {
            DeleteDirectoryIfExists(remotePath);
        }
    }

    [TestMethod]
    public async Task PushTag_PushesSelectedTagToRemote()
    {
        await EnsureGitAsync();
        using var repo = new GitTestRepo();
        var commit = repo.Commit("tagged", "tagged.txt", "tagged");
        var remotePath = TempDirectoryPath("gitster-remote-");

        try
        {
            await AddBareOriginAndPushAsync(repo, remotePath);
            using (var setup = new Repository(repo.Path))
                setup.Tags.Add("v-test", setup.Lookup<Commit>(commit)!);

            var backend = new GitCliBackend();
            await backend.OpenAsync(repo.Path);

            await backend.PushTagAsync("v-test");

            using var remote = new Repository(remotePath);
            var tag = remote.Tags["v-test"];
            Assert.IsNotNull(tag);
            Assert.AreEqual(commit, ((Commit)tag!.PeeledTarget).Sha);
        }
        finally
        {
            DeleteDirectoryIfExists(remotePath);
        }
    }

    [TestMethod]
    public async Task HybridFetch_RemoteHasNewCommit_UsesGitCliServerPath()
    {
        await EnsureGitAsync();
        using var repo = new GitTestRepo();
        repo.Commit("base", "base.txt", "base");
        var remotePath = TempDirectoryPath("gitster-remote-");

        try
        {
            var branch = await AddBareOriginAndPushAsync(repo, remotePath);
            var remoteTip = await PushCommitFromCloneAsync(remotePath, branch, "remote update", "remote.txt", "remote");

            var backend = new HybridGitBackend();
            await backend.OpenAsync(repo.Path);

            await backend.FetchAsync("origin");

            using var check = new Repository(repo.Path);
            Assert.AreEqual(remoteTip, check.Branches[$"origin/{branch}"]!.Tip!.Sha);
        }
        finally
        {
            DeleteDirectoryIfExists(remotePath);
        }
    }

    [TestMethod]
    public async Task StitchHistory_SquashedBranch_CreatesOursMergeAndBackup()
    {
        await EnsureGitAsync();
        using var repo = new GitTestRepo();
        var fixture = CreateSquashedFeatureFixture(repo);
        var fingerprintBefore = WorkingTreeFingerprint(repo.Path);

        var backend = new GitCliBackend();
        await backend.OpenAsync(repo.Path);

        var result = await backend.StitchHistoryAsync(fixture.SourceBranch);

        using var check = new Repository(repo.Path);
        var merge = check.Head.Tip!;
        var parents = merge.Parents.ToList();

        Assert.AreEqual(fixture.SourceBranch, result.SourceRef);
        Assert.AreEqual(fixture.SourceTip, result.SourceTipSha);
        Assert.AreEqual(fixture.CurrentBranch, result.TargetBranch);
        Assert.AreEqual(2, parents.Count, "stitch must create a two-parent merge commit");
        Assert.AreEqual(fixture.HeadBeforeStitch, parents[0].Sha, "first parent should be the pre-stitch target HEAD");
        Assert.AreEqual(fixture.SourceTip, parents[1].Sha, "second parent should be the stitched source tip");
        Assert.AreEqual(fixture.HeadTreeBeforeStitch, merge.Tree.Sha, "ours merge must keep the target tree unchanged");
        Assert.AreEqual(fixture.HeadBeforeStitch, check.Branches[result.BackupBranch]!.Tip!.Sha);

        var sourceTip = check.Lookup<Commit>(fixture.SourceTip)!;
        var mergeBase = check.ObjectDatabase.FindMergeBase(sourceTip, merge);
        Assert.AreEqual(fixture.SourceTip, mergeBase!.Sha, "source tip should become reachable from HEAD");
        Assert.AreEqual(fingerprintBefore, WorkingTreeFingerprint(repo.Path), "working tree and index should stay clean");
    }

    [TestMethod]
    public async Task StitchHistory_DirtyWorkingTree_ThrowsAndLeavesHeadUnchanged()
    {
        await EnsureGitAsync();
        using var repo = new GitTestRepo();
        var fixture = CreateSquashedFeatureFixture(repo);
        System.IO.File.WriteAllText(System.IO.Path.Combine(repo.Path, "dirty.txt"), "dirty");

        var backend = new GitCliBackend();
        await backend.OpenAsync(repo.Path);

        var ex = await Assert.ThrowsExceptionAsync<InvalidOperationException>(
            () => backend.StitchHistoryAsync(fixture.SourceBranch));

        StringAssert.Contains(ex.Message, "uncommitted changes");
        Assert.AreEqual(fixture.HeadBeforeStitch, repo.Head());
    }

    [TestMethod]
    public async Task StitchHistory_DetachedHead_Throws()
    {
        await EnsureGitAsync();
        using var repo = new GitTestRepo();
        var fixture = CreateSquashedFeatureFixture(repo);

        using (var r = new Repository(repo.Path))
        {
            Commands.Checkout(r, r.Lookup<Commit>(fixture.HeadBeforeStitch)!);
            Assert.IsTrue(r.Info.IsHeadDetached);
        }

        var backend = new GitCliBackend();
        await backend.OpenAsync(repo.Path);

        var ex = await Assert.ThrowsExceptionAsync<InvalidOperationException>(
            () => backend.StitchHistoryAsync(fixture.SourceBranch));

        StringAssert.Contains(ex.Message, "Check out a local branch");
    }

    [TestMethod]
    public async Task StitchHistory_MissingSource_Throws()
    {
        await EnsureGitAsync();
        using var repo = new GitTestRepo();
        repo.Commit("base", "base.txt", "base");

        var backend = new GitCliBackend();
        await backend.OpenAsync(repo.Path);

        var ex = await Assert.ThrowsExceptionAsync<InvalidOperationException>(
            () => backend.StitchHistoryAsync("missing/source"));

        StringAssert.Contains(ex.Message, "was not found");
    }

    [TestMethod]
    public async Task StitchHistory_CurrentBranchSource_Throws()
    {
        await EnsureGitAsync();
        using var repo = new GitTestRepo();
        repo.Commit("base", "base.txt", "base");
        string current;
        using (var r = new Repository(repo.Path))
            current = r.Head.FriendlyName;

        var backend = new GitCliBackend();
        await backend.OpenAsync(repo.Path);

        var ex = await Assert.ThrowsExceptionAsync<InvalidOperationException>(
            () => backend.StitchHistoryAsync(current));

        StringAssert.Contains(ex.Message, "not the current branch");
    }

    [TestMethod]
    public async Task StitchHistory_AlreadyReachableSource_Throws()
    {
        await EnsureGitAsync();
        using var repo = new GitTestRepo();
        var baseSha = repo.Commit("base", "base.txt", "base");
        using (var r = new Repository(repo.Path))
            r.CreateBranch("old/history", r.Lookup<Commit>(baseSha));
        repo.Commit("main work", "main.txt", "main");

        var backend = new GitCliBackend();
        await backend.OpenAsync(repo.Path);

        var ex = await Assert.ThrowsExceptionAsync<InvalidOperationException>(
            () => backend.StitchHistoryAsync("old/history"));

        StringAssert.Contains(ex.Message, "already reachable");
    }

    [TestMethod]
    public async Task PreviewHistoryStitch_SquashedBranch_ReportsUniqueCommitsAndSquashMatch()
    {
        await EnsureGitAsync();
        using var repo = new GitTestRepo();
        var fixture = CreateSquashedFeatureFixture(repo);

        var backend = new LibGit2Backend();
        await backend.OpenAsync(repo.Path);

        var preview = await backend.PreviewHistoryStitchAsync(fixture.SourceBranch);

        Assert.IsTrue(preview.CanExecute, string.Join(Environment.NewLine, preview.Blocks));
        Assert.AreEqual(fixture.SourceTip, preview.SourceTipSha);
        Assert.AreEqual(fixture.CurrentBranch, preview.TargetBranch);
        Assert.AreEqual(2, preview.UniqueSourceCommitCount);
        Assert.AreEqual(fixture.SquashCommit, preview.SquashMatchSha);
        Assert.IsFalse(preview.Warnings.Any(w => w.Contains("No exact squash-tree match", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task PreviewHistoryStitch_NoSquashTreeMatch_WarnsButAllows()
    {
        await EnsureGitAsync();
        using var repo = new GitTestRepo();
        repo.Commit("base", "base.txt", "base");

        string current;
        using (var r = new Repository(repo.Path))
        {
            current = r.Head.FriendlyName;
            var feature = r.CreateBranch("old/history", r.Head.Tip);
            Commands.Checkout(r, feature);
            System.IO.File.WriteAllText(System.IO.Path.Combine(repo.Path, "feature.txt"), "feature");
            Commands.Stage(r, "feature.txt");
            var sig = new Signature("Tester", "tester@gitster.test", DateTimeOffset.Now);
            r.Commit("feature work", sig, sig);
            Commands.Checkout(r, r.Branches[current]);
        }

        repo.Commit("different main work", "main.txt", "main");

        var backend = new LibGit2Backend();
        await backend.OpenAsync(repo.Path);

        var preview = await backend.PreviewHistoryStitchAsync("old/history");

        Assert.IsTrue(preview.CanExecute, string.Join(Environment.NewLine, preview.Blocks));
        Assert.IsTrue(preview.Warnings.Any(w => w.Contains("No exact squash-tree match", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task PreviewHistoryStitch_AlreadyReachableSource_Blocks()
    {
        await EnsureGitAsync();
        using var repo = new GitTestRepo();
        var baseSha = repo.Commit("base", "base.txt", "base");
        using (var r = new Repository(repo.Path))
            r.CreateBranch("old/history", r.Lookup<Commit>(baseSha));
        repo.Commit("main work", "main.txt", "main");

        var backend = new LibGit2Backend();
        await backend.OpenAsync(repo.Path);

        var preview = await backend.PreviewHistoryStitchAsync("old/history");

        Assert.IsFalse(preview.CanExecute);
        Assert.IsTrue(preview.IsSourceAlreadyReachable);
        Assert.IsTrue(preview.Blocks.Any(b => b.Contains("already reachable", StringComparison.Ordinal)));
    }

    private static List<string> AllMessages(GitTestRepo repo)
    {
        using var r = new Repository(repo.Path);
        return r.Commits.Select(c => c.MessageShort).ToList();
    }

    private static async Task<string> AddBareOriginAndPushAsync(GitTestRepo repo, string remotePath)
    {
        Repository.Init(remotePath, isBare: true);

        string branch;
        using (var local = new Repository(repo.Path))
            branch = local.Head.FriendlyName;

        await RunGitOkAsync(repo.Path, ["remote", "add", "origin", remotePath]);
        await RunGitOkAsync(repo.Path, ["push", "-u", "origin", branch]);
        await RunGitOkAsync(null, ["--git-dir", remotePath, "symbolic-ref", "HEAD", $"refs/heads/{branch}"]);

        return branch;
    }

    private static async Task<string> PushCommitFromCloneAsync(
        string remotePath,
        string branch,
        string message,
        string fileName,
        string content)
    {
        var clonePath = TempDirectoryPath("gitster-remote-writer-");

        try
        {
            await RunGitOkAsync(null, ["clone", remotePath, clonePath]);
            await ConfigureGitUserAsync(clonePath);

            await System.IO.File.WriteAllTextAsync(System.IO.Path.Combine(clonePath, fileName), content);
            await RunGitOkAsync(clonePath, ["add", fileName]);
            await RunGitOkAsync(clonePath, ["commit", "-m", message]);
            await RunGitOkAsync(clonePath, ["push", "origin", branch]);

            var head = await RunGitOkAsync(clonePath, ["rev-parse", "--verify", "HEAD"]);
            return head.Stdout.Trim();
        }
        finally
        {
            DeleteDirectoryIfExists(clonePath);
        }
    }

    private static async Task ConfigureGitUserAsync(string repoPath)
    {
        await RunGitOkAsync(repoPath, ["config", "user.name", "Tester"]);
        await RunGitOkAsync(repoPath, ["config", "user.email", "tester@gitster.test"]);
        await RunGitOkAsync(repoPath, ["config", "commit.gpgsign", "false"]);
    }

    private static async Task<GitResult> RunGitOkAsync(string? workDir, IReadOnlyList<string> args)
    {
        var result = await GitCli.RunAsync(workDir, args);
        if (!result.Success)
            Assert.Fail($"git {string.Join(" ", args)} failed:\n{result.Output}");

        return result;
    }

    private static string TempDirectoryPath(string prefix) =>
        System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            prefix + Guid.NewGuid().ToString("N")[..12]);

    private static string TempZipPath() =>
        System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "gitster-archive-test-" + Guid.NewGuid().ToString("N") + ".zip");

    private static Dictionary<string, string> ReadZipEntries(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name))
                continue;

            using var stream = entry.Open();
            using var reader = new StreamReader(stream);
            result[entry.FullName] = reader.ReadToEnd();
        }

        return result;
    }

    private static void DeleteIfExists(string path)
    {
        try
        {
            if (System.IO.File.Exists(path))
                System.IO.File.Delete(path);
        }
        catch
        {
        }
    }

    private static void DeleteDirectoryIfExists(string path)
    {
        try
        {
            if (!Directory.Exists(path))
                return;

            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                System.IO.File.SetAttributes(file, FileAttributes.Normal);

            Directory.Delete(path, recursive: true);
        }
        catch
        {
        }
    }

    private static SquashedFeatureFixture CreateSquashedFeatureFixture(GitTestRepo repo)
    {
        repo.Commit("base", "base.txt", "base");
        const string sourceBranch = "feature/stitch";
        string currentBranch;
        string sourceTip;
        string squashCommit;

        using (var r = new Repository(repo.Path))
        {
            currentBranch = r.Head.FriendlyName;
            var feature = r.CreateBranch(sourceBranch, r.Head.Tip);
            Commands.Checkout(r, feature);

            var sig = new Signature("Tester", "tester@gitster.test", DateTimeOffset.Now);
            System.IO.File.WriteAllText(System.IO.Path.Combine(repo.Path, "feature-1.txt"), "one");
            Commands.Stage(r, "feature-1.txt");
            r.Commit("feature part 1", sig, sig);

            System.IO.File.WriteAllText(System.IO.Path.Combine(repo.Path, "feature-2.txt"), "two");
            Commands.Stage(r, "feature-2.txt");
            sourceTip = r.Commit("feature part 2", sig, sig).Sha;

            Commands.Checkout(r, r.Branches[currentBranch]);
            System.IO.File.WriteAllText(System.IO.Path.Combine(repo.Path, "feature-1.txt"), "one");
            System.IO.File.WriteAllText(System.IO.Path.Combine(repo.Path, "feature-2.txt"), "two");
            Commands.Stage(r, ["feature-1.txt", "feature-2.txt"]);
            squashCommit = r.Commit("squash feature", sig, sig).Sha;
        }

        var headBeforeStitch = repo.Commit("main after squash", "main.txt", "main");
        using var check = new Repository(repo.Path);
        return new SquashedFeatureFixture(
            currentBranch,
            sourceBranch,
            sourceTip,
            squashCommit,
            headBeforeStitch,
            check.Head.Tip!.Tree.Sha);
    }

    private static string WorkingTreeFingerprint(string repoPath)
    {
        using var r = new Repository(repoPath);
        var entries = r.RetrieveStatus(new StatusOptions { IncludeUntracked = true })
            .Select(e => $"{e.FilePath}:{e.State}")
            .OrderBy(s => s, StringComparer.Ordinal);
        return string.Join("|", entries);
    }

    private sealed record SquashedFeatureFixture(
        string CurrentBranch,
        string SourceBranch,
        string SourceTip,
        string SquashCommit,
        string HeadBeforeStitch,
        string HeadTreeBeforeStitch);
}
