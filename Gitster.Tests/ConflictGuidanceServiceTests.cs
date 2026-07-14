using Gitster.Core.Features;
using Gitster.Core.Git;
using NSubstitute;

namespace Gitster.Tests;

[TestClass]
public sealed class ConflictGuidanceServiceTests
{
    [TestMethod]
    public async Task BuildAsync_WhenWorkingTreeHasConflicts_ListsConflictedFiles()
    {
        var git = Substitute.For<IGitBackend>();
        git.RepositoryPath.Returns("C:/repo");
        git.GetWorkingTreeStatusAsync().Returns(Task.FromResult(new WorkingTreeStatus(
            Staged:
            [
                new WorkingTreeFile("src/app.cs", WorkingFileStatus.Conflicted, Staged: true, Added: 0, Deleted: 0),
            ],
            Unstaged:
            [
                new WorkingTreeFile("readme.md", WorkingFileStatus.Modified, Staged: false, Added: 1, Deleted: 0),
            ])));

        var guidance = await ConflictGuidanceService.BuildAsync(
            git,
            "Cherry-pick",
            new InvalidOperationException("Cherry-pick produced conflicts and was aborted - history and working tree are unchanged."));

        CollectionAssert.AreEqual(new[] { "src/app.cs" }, guidance.Files.ToArray());
        Assert.IsFalse(guidance.RepositoryHalted);
        Assert.IsFalse(guidance.CanOpenMergeTool);
        StringAssert.Contains(guidance.StateSummary, "restored");
    }

    [TestMethod]
    public async Task BuildAsync_WhenRepositoryIsHalted_AllowsMergeTool()
    {
        var git = Substitute.For<IGitBackend>();
        git.RepositoryPath.Returns("C:/repo");
        git.GetWorkingTreeStatusAsync().Returns(Task.FromResult(new WorkingTreeStatus(
            Staged: [],
            Unstaged:
            [
                new WorkingTreeFile("shared.txt", WorkingFileStatus.Conflicted, Staged: false, Added: 0, Deleted: 0),
            ])));

        var guidance = await ConflictGuidanceService.BuildAsync(
            git,
            "Merge",
            new InvalidOperationException("Merge of 'feature' produced conflicts. Resolve them in the working tree."));

        Assert.IsTrue(guidance.RepositoryHalted);
        Assert.IsTrue(guidance.CanOpenMergeTool);
        CollectionAssert.AreEqual(new[] { "shared.txt" }, guidance.Files.ToArray());
    }

    [TestMethod]
    public void ParseConflictFiles_WhenGitOutputNamesFile_ExtractsPath()
    {
        var files = ConflictGuidanceService.ParseConflictFiles(
            "CONFLICT (content): Merge conflict in src/main.cs\nAutomatic merge failed.");

        CollectionAssert.AreEqual(new[] { "src/main.cs" }, files.ToArray());
    }

    [TestMethod]
    public void MergeToolArgs_UsesGitMergetool()
    {
        CollectionAssert.AreEqual(new[] { "mergetool" }, ConflictGuidanceService.MergeToolArgs().ToArray());
    }
}
