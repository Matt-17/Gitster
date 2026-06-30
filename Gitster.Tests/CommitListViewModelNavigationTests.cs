using System.IO;

using Gitster.Services;
using Gitster.Services.Git;
using Gitster.Services.History;
using Gitster.ViewModels;
using NSubstitute;

namespace Gitster.Tests;

[TestClass]
public sealed class CommitListViewModelNavigationTests
{
    [TestMethod]
    public void SelectNextCommit_WithDisplayOnlyRows_SkipsHeadersAndEmptyRows()
    {
        var vm = CreateViewModel();
        var first = Item("first");
        var second = Item("second");
        vm.Items =
        [
            new CommitSectionHeader(CommitSectionKind.RemoteIncoming, 0),
            new CommitSectionEmptyRow(CommitSectionKind.RemoteIncoming, "no incoming commits"),
            new CommitSectionHeader(CommitSectionKind.LocalOutgoing, 2),
            first,
            second,
        ];
        vm.SelectedCommit = first;

        vm.SelectNextCommitCommand.Execute(null);

        Assert.AreSame(second, vm.SelectedCommit);
    }

    [TestMethod]
    public void SelectPreviousCommit_WithDisplayOnlyRows_SkipsHeadersAndEmptyRows()
    {
        var vm = CreateViewModel();
        var first = Item("first");
        var second = Item("second");
        vm.Items =
        [
            new CommitSectionHeader(CommitSectionKind.LocalOutgoing, 2),
            first,
            new CommitSectionHeader(CommitSectionKind.RemoteIncoming, 0),
            new CommitSectionEmptyRow(CommitSectionKind.RemoteIncoming, "no incoming commits"),
            second,
        ];
        vm.SelectedCommit = second;

        vm.SelectPreviousCommitCommand.Execute(null);

        Assert.AreSame(first, vm.SelectedCommit);
    }

    [TestMethod]
    public void SelectNextCommitPage_FromMiddle_JumpsTenCommitsAndClampsAtEnd()
    {
        var vm = CreateViewModel();
        var commits = Enumerable.Range(0, 18).Select(i => Item($"commit-{i:D2}")).ToList();
        vm.Items = commits.Cast<object>().ToList();
        vm.SelectedCommit = commits[7];

        vm.SelectNextCommitPageCommand.Execute(null);

        Assert.AreSame(commits[17], vm.SelectedCommit);

        vm.SelectNextCommitPageCommand.Execute(null);

        Assert.AreSame(commits[17], vm.SelectedCommit);
    }

    [TestMethod]
    public void SelectPreviousCommitPage_FromMiddle_JumpsTenCommitsAndClampsAtStart()
    {
        var vm = CreateViewModel();
        var commits = Enumerable.Range(0, 18).Select(i => Item($"commit-{i:D2}")).ToList();
        vm.Items = commits.Cast<object>().ToList();
        vm.SelectedCommit = commits[12];

        vm.SelectPreviousCommitPageCommand.Execute(null);

        Assert.AreSame(commits[2], vm.SelectedCommit);

        vm.SelectPreviousCommitPageCommand.Execute(null);

        Assert.AreSame(commits[0], vm.SelectedCommit);
    }

    [TestMethod]
    public void SelectFirstAndLastCommit_WithDisplayOnlyRows_SelectsCommitBoundaries()
    {
        var vm = CreateViewModel();
        var first = Item("first");
        var last = Item("last");
        vm.Items =
        [
            new CommitSectionHeader(CommitSectionKind.RemoteIncoming, 0),
            new CommitSectionEmptyRow(CommitSectionKind.RemoteIncoming, "no incoming commits"),
            first,
            last,
        ];

        vm.SelectLastCommitCommand.Execute(null);

        Assert.AreSame(last, vm.SelectedCommit);

        vm.SelectFirstCommitCommand.Execute(null);

        Assert.AreSame(first, vm.SelectedCommit);
    }

    private static CommitListViewModel CreateViewModel()
    {
        var git = Substitute.For<IGitBackend>();
        git.GetCommitDiffAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(CommitDiff.Empty));

        return new CommitListViewModel(
            git,
            new CommitHistoryService(git, Path.Combine(Path.GetTempPath(), "Gitster.Tests", Guid.NewGuid().ToString("N"))),
            new UiPreferencesService());
    }

    private static CommitItem Item(string id) =>
        new(
            $"Message {id}",
            new DateTime(2026, 1, 1),
            id,
            "Tester",
            fullSha: $"{id}-full-sha");
}
