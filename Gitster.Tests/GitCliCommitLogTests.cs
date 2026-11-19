using Gitster.Core.Git;

using LibGit2Sharp;

namespace Gitster.Tests;

/// <summary>
/// The Git CLI history read must give the same result as the libgit2 reader it replaces
/// in <see cref="HybridGitBackend"/>.
/// </summary>
[TestClass]
public sealed class GitCliCommitLogTests
{
    private static async Task EnsureGitAsync()
    {
        await GitCli.DetectAsync();
        if (!GitCli.IsAvailable)
            Assert.Inconclusive("Git command-line tool is not available on this machine.");
    }

    [TestMethod]
    public async Task GetCommits_RewrittenPushedCommit_MatchesLibGit2Reader()
    {
        await EnsureGitAsync();
        using var repo = new GitTestRepo();
        var baseSha = repo.Commit("base", "a.txt", "1");
        repo.Commit("change", "a.txt", "2");
        using (var r = new Repository(repo.Path))
        {
            // Two remote commits share one tree (the second is empty).
            var sig = new Signature("Tester", "tester@gitster.test", DateTimeOffset.Now);
            var emptySha = r.Commit("empty", sig, sig, new CommitOptions { AllowEmptyCommit = true }).Sha;

            r.Network.Remotes.Add("origin", "https://example.invalid/repo.git");
            r.Refs.Add("refs/remotes/origin/master", emptySha, allowOverwrite: true);
            r.Branches.Update(r.Head, b => b.Remote = "origin", b => b.UpstreamBranch = "refs/heads/master");
            r.Reset(ResetMode.Hard, r.Lookup<Commit>(baseSha)!);
        }

        var rewritten = repo.Commit("change rewritten\n\nwith a body", "a.txt", "2");
        repo.Commit("unpushed", "b.txt", "b");

        var cli = new GitCliBackend();
        await cli.OpenAsync(repo.Path);
        var lib = new LibGit2Backend();
        await lib.OpenAsync(repo.Path);

        var fromCli = await cli.GetCommitsAsync();
        var fromLib = await lib.GetCommitsAsync();

        CollectionAssert.AreEquivalent(
            fromLib.Select(Describe).ToList(),
            fromCli.Select(Describe).ToList());

        // Incoming commits come first, then the local history.
        Assert.IsTrue(fromCli.Take(2).All(c => c.RemoteState == CommitRemoteState.Incoming));
        var local = fromCli.Single(c => c.FullSha == rewritten);
        Assert.AreEqual(CommitRemoteState.LocalOnly, local.RemoteState);
        Assert.IsNotNull(local.OrphanedPairSha);
        Assert.AreEqual("change rewritten", local.Message);
    }

    [TestMethod]
    public async Task GetCommits_NoUpstream_MarksEveryCommitWithoutTrackingBranch()
    {
        await EnsureGitAsync();
        using var repo = new GitTestRepo();
        repo.Commit("one", "a.txt", "1");
        repo.Commit("two", "a.txt", "2");

        var cli = new GitCliBackend();
        await cli.OpenAsync(repo.Path);

        var commits = await cli.GetCommitsAsync();

        Assert.AreEqual(2, commits.Count);
        Assert.IsTrue(commits.All(c => c.RemoteState == CommitRemoteState.NoTrackingBranch));
        Assert.IsTrue(commits.All(c => c.FullSha.Length == 40 && c.Sha.Length == 7));
    }

    [TestMethod]
    public async Task GetCommits_AuthorFilter_KeepsOnlyThatAuthor()
    {
        await EnsureGitAsync();
        using var repo = new GitTestRepo();
        repo.Commit("one", "a.txt", "1");
        using (var r = new Repository(repo.Path))
        {
            var other = new Signature("Someone Else", "else@gitster.test", DateTimeOffset.Now);
            r.Commit("by someone else", other, other, new CommitOptions { AllowEmptyCommit = true });
        }

        var cli = new GitCliBackend();
        await cli.OpenAsync(repo.Path);

        var commits = await cli.GetCommitsAsync(new Core.Git.CommitFilter { SelectedAuthorName = "Someone Else" });

        Assert.AreEqual("by someone else", commits.Single().Message);
    }

    [TestMethod]
    public async Task GetCommits_UnbornBranch_ReturnsEmpty()
    {
        await EnsureGitAsync();
        using var repo = new GitTestRepo();

        var cli = new GitCliBackend();
        await cli.OpenAsync(repo.Path);

        Assert.AreEqual(0, (await cli.GetCommitsAsync()).Count);
    }

    private static string Describe(CommitInfo c) => string.Join("|",
        c.FullSha, c.Sha, c.Message, c.AuthorName, c.AuthorEmail, c.RemoteState,
        c.OrphanedPairSha ?? "-", string.Join(",", c.ParentShas ?? []),
        c.Date.ToString("O"), c.CommitterDate?.ToString("O") ?? "-");
}
