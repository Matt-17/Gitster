using System.IO;
using Gitster.Core.Models;
using Gitster.Core.Features;
using Gitster.Core.Git;
using Gitster.Core.History;
using LibGit2Sharp;

namespace Gitster.Tests;

[TestClass]
public sealed class GitEdgeCaseMatrixTests
{
    [TestMethod]
    public async Task EmptyUnbornRepository_ServiceReadsReturnEmptyState()
    {
        using var repo = new GitTestRepo();
        using var cache = new TempCacheDir();
        var backend = new LibGit2Backend();
        await backend.OpenAsync(repo.Path);

        var branch = await backend.GetCurrentBranchAsync();
        var status = await backend.GetWorkingTreeStatusAsync();
        var state = await backend.GetWorkingTreeStateAsync();
        var history = new CommitHistoryService(backend, cache.Path);

        await history.OpenAsync(repo.Path);
        var rows = await history.EnsureCompleteAsync(progress: null);

        Assert.IsFalse(string.IsNullOrWhiteSpace(branch.Name));
        Assert.AreEqual(0, status.Staged.Count);
        Assert.AreEqual(0, status.Unstaged.Count);
        Assert.IsInstanceOfType(state, typeof(WorkingTreeState.Clean));
        Assert.AreEqual(0, rows.Count);
    }

    [TestMethod]
    public async Task DetachedHead_ServiceReadsShowDetachedBranchAndHistory()
    {
        using var repo = new GitTestRepo();
        var baseSha = repo.Commit("base", "base.txt", "base");
        repo.Commit("tip", "tip.txt", "tip");

        using (var setup = new Repository(repo.Path))
            Commands.Checkout(setup, setup.Lookup<Commit>(baseSha)!);

        using var cache = new TempCacheDir();
        var backend = new LibGit2Backend();
        await backend.OpenAsync(repo.Path);
        var branch = await backend.GetCurrentBranchAsync();
        var history = new CommitHistoryService(backend, cache.Path);

        await history.OpenAsync(repo.Path);
        var rows = await history.EnsureCompleteAsync(progress: null);

        StringAssert.StartsWith(branch.Name, "detached @");
        Assert.AreEqual(1, rows.Count);
        Assert.AreEqual("base", rows[0].Message);
    }

    [TestMethod]
    public async Task ShallowClone_HistoryLoadsAndRewriteOpsRefuseCleanly()
    {
        await EnsureGitAsync();
        using var source = new GitTestRepo();
        source.Commit("one", "one.txt", "one");
        source.Commit("two", "two.txt", "two");
        source.Commit("three", "three.txt", "three");

        var clonePath = TempDirectoryPath("gitster-shallow-");
        try
        {
            var sourceUri = new Uri(source.Path + Path.DirectorySeparatorChar).AbsoluteUri;
            await RunGitOkAsync(null, ["clone", "--depth", "1", sourceUri, clonePath]);
            await ConfigureGitUserAsync(clonePath);

            using var cache = new TempCacheDir();
            var backend = new LibGit2Backend();
            var history = new CommitHistoryService(backend, cache.Path);
            await history.OpenAsync(clonePath);
            var rows = await history.EnsureCompleteAsync(progress: null);

            Assert.AreEqual(1, rows.Count);

            var cli = new GitCliBackend();
            await cli.OpenAsync(clonePath);
            var head = (await RunGitOkAsync(clonePath, ["rev-parse", "HEAD"])).Stdout.Trim();
            var ex = await Assert.ThrowsExceptionAsync<InvalidOperationException>(
                () => cli.RewordCommitAsync(head, "new message"));
            StringAssert.Contains(ex.Message, "shallow clone");
        }
        finally
        {
            DeleteDirectoryIfExists(clonePath);
        }
    }

    [TestMethod]
    public async Task SubmoduleRepository_StatusGlanceParsesInitializedSubmodule()
    {
        await EnsureGitAsync();
        using var child = new GitTestRepo();
        child.Commit("child", "child.txt", "child");
        using var parent = new GitTestRepo();
        parent.Commit("parent", "parent.txt", "parent");

        var childUri = new Uri(child.Path + Path.DirectorySeparatorChar).AbsoluteUri;
        await RunGitOkAsync(
            parent.Path,
            ["-c", "protocol.file.allow=always", "submodule", "add", childUri, "libs/sub"]);
        await RunGitOkAsync(parent.Path, ["commit", "-m", "add submodule"]);

        var statuses = await new GitFeatureService().GetSubmoduleStatusAsync(parent.Path);

        Assert.AreEqual(1, statuses.Count);
        Assert.AreEqual("libs/sub", statuses[0].Path);
        Assert.IsTrue(statuses[0].IsInitialized);
    }

    [TestMethod]
    public async Task UnicodeFilesAndBranchNames_RoundTripThroughStatusAndAllBranchesHistory()
    {
        using var repo = new GitTestRepo();
        var unicodeFile = "\u00f6\u00e4\u00fc-\U0001F600 file.txt";
        repo.Commit("unicode base", unicodeFile, "base");

        using (var setup = new Repository(repo.Path))
            setup.CreateBranch("feature/\u00fcber", setup.Head.Tip);

        File.WriteAllText(Path.Combine(repo.Path, unicodeFile), "changed");

        var backend = new LibGit2Backend();
        await backend.OpenAsync(repo.Path);
        var status = await backend.GetWorkingTreeStatusAsync();

        Assert.IsTrue(status.Unstaged.Any(file => NormalizePath(file.Path) == NormalizePath(unicodeFile)));

        using var cache = new TempCacheDir();
        var history = new CommitHistoryService(backend, cache.Path);
        await history.OpenAsync(repo.Path, HistoryScope.AllBranches);
        var rows = await history.EnsureCompleteAsync(progress: null, scope: HistoryScope.AllBranches);

        Assert.IsTrue(rows[0].RefLabels!.Any(label => label.Name == "feature/\u00fcber"));
    }

    [TestMethod]
    public async Task LongPaths_StatusReadDoesNotDropUntrackedFile()
    {
        using var repo = new GitTestRepo();
        repo.Commit("base", "base.txt", "base");

        var relativeDir = Path.Combine(Enumerable
            .Range(0, 7)
            .Select(i => $"segment-{i}-" + new string((char)('a' + i), 30))
            .ToArray());
        var relativePath = Path.Combine(relativeDir, "long-file.txt");
        var fullPath = Path.Combine(repo.Path, relativePath);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, "long");
        }
        catch (Exception ex) when (ex is PathTooLongException or DirectoryNotFoundException or IOException or UnauthorizedAccessException)
        {
            Assert.Inconclusive($"Windows long paths are not enabled in this environment: {ex.Message}");
        }

        if (Path.GetFullPath(fullPath).Length <= 260)
            Assert.Inconclusive("The generated fixture path did not exceed the legacy Windows MAX_PATH limit.");

        var backend = new LibGit2Backend();
        await backend.OpenAsync(repo.Path);
        var status = await backend.GetWorkingTreeStatusAsync();

        Assert.IsTrue(status.Unstaged.Any(file => NormalizePath(file.Path) == NormalizePath(relativePath)));
    }

    [TestMethod]
    public async Task OctopusMerge_HistoryPreservesAllParentsForGraphLayout()
    {
        await EnsureGitAsync();
        using var repo = new GitTestRepo();
        repo.Commit("base", "base.txt", "base");
        var mainBranch = await CurrentBranchAsync(repo.Path);

        for (var i = 1; i <= 3; i++)
        {
            await RunGitOkAsync(repo.Path, ["checkout", "-b", $"octopus-{i}", mainBranch]);
            File.WriteAllText(Path.Combine(repo.Path, $"branch-{i}.txt"), i.ToString());
            await RunGitOkAsync(repo.Path, ["add", $"branch-{i}.txt"]);
            await RunGitOkAsync(repo.Path, ["commit", "-m", $"branch {i}"]);
        }

        await RunGitOkAsync(repo.Path, ["checkout", mainBranch]);
        await RunGitOkAsync(repo.Path, ["merge", "--no-ff", "-m", "octopus merge", "octopus-1", "octopus-2", "octopus-3"]);

        using var cache = new TempCacheDir();
        var backend = new LibGit2Backend();
        var history = new CommitHistoryService(backend, cache.Path);
        await history.OpenAsync(repo.Path, HistoryScope.AllBranches);
        var rows = await history.EnsureCompleteAsync(progress: null, scope: HistoryScope.AllBranches);

        var merge = rows.Single(row => row.Message == "octopus merge");
        Assert.AreEqual(4, merge.ParentShas!.Count);
    }

    [TestMethod]
    public async Task CaseOnlyRename_StagedStatusReportsRename()
    {
        await EnsureGitAsync();
        using var repo = new GitTestRepo();
        repo.Commit("base", "case.txt", "case");
        await RunGitOkAsync(repo.Path, ["config", "core.ignorecase", "false"]);
        await RunGitOkAsync(repo.Path, ["mv", "-f", "case.txt", "Case.txt"]);

        var backend = new LibGit2Backend();
        await backend.OpenAsync(repo.Path);
        var status = await backend.GetWorkingTreeStatusAsync();

        Assert.IsTrue(status.Staged.Any(file =>
            file.Status == WorkingFileStatus.Renamed
            && NormalizePath(file.Path).Equals("Case.txt", StringComparison.Ordinal)));
    }

    private static async Task EnsureGitAsync()
    {
        await GitCli.DetectAsync();
        if (!GitCli.IsAvailable)
            Assert.Inconclusive("Git command-line tool is not available on this machine.");
    }

    private static async Task ConfigureGitUserAsync(string repoPath)
    {
        await RunGitOkAsync(repoPath, ["config", "user.name", "Tester"]);
        await RunGitOkAsync(repoPath, ["config", "user.email", "tester@gitster.test"]);
        await RunGitOkAsync(repoPath, ["config", "commit.gpgsign", "false"]);
    }

    private static async Task<string> CurrentBranchAsync(string repoPath)
    {
        var result = await RunGitOkAsync(repoPath, ["branch", "--show-current"]);
        return result.Stdout.Trim();
    }

    private static async Task<GitResult> RunGitOkAsync(string? workDir, IReadOnlyList<string> args)
    {
        var result = await GitCli.RunAsync(workDir, args);
        if (!result.Success)
            Assert.Fail($"git {string.Join(" ", args)} failed:\n{result.Output}");

        return result;
    }

    private static string NormalizePath(string path) => path.Replace('\\', '/');

    private static string TempDirectoryPath(string prefix) =>
        Path.Combine(Path.GetTempPath(), prefix + Guid.NewGuid().ToString("N")[..12]);

    private static void DeleteDirectoryIfExists(string path)
    {
        try
        {
            if (!Directory.Exists(path))
                return;

            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);

            Directory.Delete(path, recursive: true);
        }
        catch
        {
        }
    }

    private sealed class TempCacheDir : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "gitster-edge-cache-" + Guid.NewGuid().ToString("N")[..12]);

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
            }
        }
    }
}
