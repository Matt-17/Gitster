using System.IO;
using Gitster.Services;
using Gitster.Services.Git;
using Gitster.Services.OperationsLog;
using LibGit2Sharp;

namespace Gitster.Tests;

[TestClass]
public sealed class UndoSnapshotFuzzTests
{
    private const int Seed = 731_2026;

    [TestMethod]
    [TestCategory("Slow")]
    public async Task UndoAndSnapshots_SeededHistoryFuzz_KeepsRepositoryConsistent()
    {
        await EnsureGitAsync();
        using var repo = new GitTestRepo();
        repo.Commit("base", "base.txt", "base");
        repo.Commit("seed one", "seed-1.txt", "one");
        repo.Commit("seed two", "seed-2.txt", "two");
        await ConfigureGitUserAsync(repo.Path);

        var git = new LibGit2Backend();
        var cli = new GitCliBackend();
        await git.OpenAsync(repo.Path);
        await cli.OpenAsync(repo.Path);

        var opsLog = new OperationsLogService();
        await opsLog.AttachAsync(repo.Path);
        var snapshots = new SnapshotService(maxSnapshotFiles: 64);
        await snapshots.AttachAsync(repo.Path);

        var random = new Random(Seed);
        for (var step = 0; step < 30; step++)
        {
            var before = await git.GetHeadShaAsync();
            await snapshots.CaptureAsync(git, $"fuzz before {step}");

            var executed = await ExecuteFuzzOperationAsync(repo.Path, git, cli, random, step);
            var after = await git.GetHeadShaAsync();

            if (executed.IsUndoable && !string.Equals(before, after, StringComparison.OrdinalIgnoreCase))
            {
                var branch = await git.GetCurrentBranchAsync();
                var record = new OperationRecord(
                    Guid.NewGuid().ToString("N"),
                    DateTimeOffset.Now,
                    executed.Kind,
                    executed.Description,
                    branch.Name,
                    before,
                    after,
                    ReflogSelector: null,
                    OperationStatus.Active);
                await opsLog.RecordAsync(record);

                if (random.NextDouble() < 0.5)
                {
                    var plan = await opsLog.PrepareUndoAsync(record, git);
                    Assert.IsInstanceOfType(plan, typeof(UndoPlan.Ready), $"Seed {Seed}, step {step}: undo plan was not ready.");
                    await opsLog.ExecuteUndoAsync((UndoPlan.Ready)plan, git);
                    Assert.AreEqual(before, await git.GetHeadShaAsync(), $"Seed {Seed}, step {step}: undo did not restore HEAD.");
                }
            }

            await snapshots.CaptureAsync(git, $"fuzz after {step}");
            await AssertRepositoryHealthyAsync(repo.Path, git, snapshots, step);
        }
    }

    private static async Task<FuzzOperationResult> ExecuteFuzzOperationAsync(
        string repoPath,
        LibGit2Backend git,
        GitCliBackend cli,
        Random random,
        int step)
    {
        var attempts = 0;
        while (attempts++ < 8)
        {
            switch ((FuzzOperation)random.Next(Enum.GetValues<FuzzOperation>().Length))
            {
                case FuzzOperation.Commit:
                    return await CommitAsync(repoPath, git, step);

                case FuzzOperation.Amend:
                    return await AmendAsync(git, step);

                case FuzzOperation.Reword:
                    if (HeadLineage(repoPath).Count >= 3)
                        return await RewordAsync(cli, repoPath, step);
                    break;

                case FuzzOperation.Squash:
                    if (HeadLineage(repoPath).Count >= 4)
                        return await SquashAsync(cli, repoPath, step);
                    break;

                case FuzzOperation.Branch:
                    return await CreateBranchAsync(git, step);

                case FuzzOperation.Stash:
                    return await StashRoundTripAsync(repoPath, git, step);
            }
        }

        return await CommitAsync(repoPath, git, step);
    }

    private static async Task<FuzzOperationResult> CommitAsync(string repoPath, LibGit2Backend git, int step)
    {
        var fileName = $"fuzz-{step:D2}.txt";
        await File.WriteAllTextAsync(Path.Combine(repoPath, fileName), $"fuzz {step}");
        await git.StageAsync([fileName]);
        await git.CommitAsync(new CommitRequest($"fuzz commit {step}"));
        return new FuzzOperationResult(OperationKind.Commit, $"Commit {step}", IsUndoable: true);
    }

    private static async Task<FuzzOperationResult> AmendAsync(LibGit2Backend git, int step)
    {
        await git.AmendAsync(new AmendRequest(
            DateTime.Now.AddMinutes(step),
            NewMessage: $"fuzz amend {step}"));
        return new FuzzOperationResult(OperationKind.Amend, $"Amend {step}", IsUndoable: true);
    }

    private static async Task<FuzzOperationResult> RewordAsync(GitCliBackend cli, string repoPath, int step)
    {
        var target = HeadLineage(repoPath)[1];
        await cli.RewordCommitAsync(target, $"fuzz reword {step}");
        return new FuzzOperationResult(OperationKind.Reword, $"Reword {step}", IsUndoable: true);
    }

    private static async Task<FuzzOperationResult> SquashAsync(GitCliBackend cli, string repoPath, int step)
    {
        var lineage = HeadLineage(repoPath);
        await cli.SquashCommitsAsync([lineage[1], lineage[2]], $"fuzz squash {step}", overrideDate: null);
        return new FuzzOperationResult(OperationKind.Squash, $"Squash {step}", IsUndoable: true);
    }

    private static async Task<FuzzOperationResult> CreateBranchAsync(LibGit2Backend git, int step)
    {
        var head = await git.GetHeadShaAsync();
        var name = $"fuzz/branch-{step:D2}";
        await git.CreateBranchAsync(name, head);
        return new FuzzOperationResult(OperationKind.CommitOnBranch, $"Create branch {name}", IsUndoable: false);
    }

    private static async Task<FuzzOperationResult> StashRoundTripAsync(string repoPath, LibGit2Backend git, int step)
    {
        var fileName = $"stash-{step:D2}.txt";
        await File.WriteAllTextAsync(Path.Combine(repoPath, fileName), $"stash {step}");
        await git.CreateStashAsync($"fuzz stash {step}", includeUntracked: true);
        Assert.AreEqual(1, await git.GetStashCountAsync(), $"Seed {Seed}, step {step}: stash was not created.");
        await git.DropStashAsync(0);
        Assert.AreEqual(0, await git.GetStashCountAsync(), $"Seed {Seed}, step {step}: stash was not dropped.");
        return new FuzzOperationResult(OperationKind.StashDrop, $"Stash round-trip {step}", IsUndoable: false);
    }

    private static async Task AssertRepositoryHealthyAsync(
        string repoPath,
        LibGit2Backend git,
        SnapshotService snapshots,
        int step)
    {
        var fsck = await GitCli.RunAsync(repoPath, ["fsck", "--no-progress"]);
        Assert.IsTrue(fsck.Success, $"Seed {Seed}, step {step}: git fsck failed:\n{fsck.Output}");

        var status = await GitCli.RunAsync(repoPath, ["status", "--porcelain"]);
        Assert.IsTrue(status.Success, $"Seed {Seed}, step {step}: git status failed:\n{status.Output}");
        Assert.AreEqual(string.Empty, status.Stdout.Trim(), $"Seed {Seed}, step {step}: working tree is dirty.");

        var head = await git.GetHeadShaAsync();
        Assert.IsTrue(await git.CommitExistsAsync(head), $"Seed {Seed}, step {step}: HEAD is not reachable.");

        var loadedSnapshots = snapshots.LoadSnapshots();
        Assert.IsTrue(loadedSnapshots.Count > 0, $"Seed {Seed}, step {step}: no snapshots were readable.");
        foreach (var snapshot in loadedSnapshots.Take(5))
        {
            Assert.IsTrue(snapshot.RefStates.Count > 0, $"Seed {Seed}, step {step}: snapshot had no refs.");
            foreach (var sha in snapshot.RefStates.Values.Take(3))
                Assert.IsTrue(await git.CommitExistsAsync(sha), $"Seed {Seed}, step {step}: snapshot ref {sha} was missing.");
        }
    }

    private static List<string> HeadLineage(string repoPath)
    {
        using var repo = new Repository(repoPath);
        var result = new List<string>();
        var commit = repo.Head.Tip;
        while (commit is not null)
        {
            result.Add(commit.Sha);
            commit = commit.Parents.FirstOrDefault();
        }

        return result;
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

    private static async Task<GitResult> RunGitOkAsync(string repoPath, IReadOnlyList<string> args)
    {
        var result = await GitCli.RunAsync(repoPath, args);
        if (!result.Success)
            Assert.Fail($"git {string.Join(" ", args)} failed:\n{result.Output}");

        return result;
    }

    private enum FuzzOperation
    {
        Commit,
        Amend,
        Reword,
        Squash,
        Branch,
        Stash,
    }

    private sealed record FuzzOperationResult(OperationKind Kind, string Description, bool IsUndoable);
}
