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

    private static List<string> AllMessages(GitTestRepo repo)
    {
        using var r = new Repository(repo.Path);
        return r.Commits.Select(c => c.MessageShort).ToList();
    }

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
}
