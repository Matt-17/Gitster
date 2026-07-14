using Gitster.Services;
using Gitster.Core.Git;

namespace Gitster.Tests;

[TestClass]
public sealed class CustomToolsTests
{
    [TestMethod]
    public void Substitute_ReplacesAllPlaceholders()
    {
        var svc = new CustomToolsService();
        using var repo = new GitTestRepo();
        svc.Attach(repo.Path);

        var result = svc.Substitute(
            "echo $REVISION $CUR $ARGS $BRANCH $REPO",
            revision: "abc123",
            args: "hello",
            branch: "main");

        StringAssert.Contains(result, "abc123 abc123 hello main");
        StringAssert.Contains(result, repo.Path);
        Assert.IsFalse(result.Contains('$'), "all placeholders should be substituted");
    }

    [TestMethod]
    public void Substitute_MissingValuesBecomeEmpty()
    {
        var svc = new CustomToolsService();
        using var repo = new GitTestRepo();
        svc.Attach(repo.Path);

        var result = svc.Substitute("run $ARGS done", revision: null, args: null, branch: null);
        Assert.AreEqual("run  done", result);
    }
}

[TestClass]
public sealed class WorktreeParseTests
{
    [TestMethod]
    public void Parse_MainLinkedDetachedLockedPrunable()
    {
        const string porcelain =
            "worktree C:/repos/main\n" +
            "HEAD 1111111111111111111111111111111111111111\n" +
            "branch refs/heads/master\n" +
            "\n" +
            "worktree C:/repos/feature\n" +
            "HEAD 2222222222222222222222222222222222222222\n" +
            "branch refs/heads/feature\n" +
            "locked\n" +
            "\n" +
            "worktree C:/repos/detached\n" +
            "HEAD 3333333333333333333333333333333333333333\n" +
            "detached\n" +
            "\n" +
            "worktree C:/repos/gone\n" +
            "HEAD 4444444444444444444444444444444444444444\n" +
            "branch refs/heads/gone\n" +
            "prunable gitdir file points to non-existent location\n";

        var list = GitCliBackend.ParseWorktreePorcelain(porcelain, openPath: @"C:\repos\feature");

        Assert.AreEqual(4, list.Count);

        Assert.IsTrue(list[0].IsMain);
        Assert.AreEqual("master", list[0].BranchName);

        Assert.AreEqual("feature", list[1].BranchName);
        Assert.IsTrue(list[1].IsLocked);
        Assert.IsTrue(list[1].IsCurrent, "the open path should be flagged as current");
        Assert.IsFalse(list[1].IsMain);

        StringAssert.StartsWith(list[2].BranchName, "detached @");

        Assert.IsTrue(list[3].IsPrunable);
        Assert.AreEqual("gone", list[3].BranchName);
    }

    [TestMethod]
    public void Parse_EmptyInputYieldsNothing()
    {
        var list = GitCliBackend.ParseWorktreePorcelain(string.Empty, openPath: @"C:\x");
        Assert.AreEqual(0, list.Count);
    }
}
