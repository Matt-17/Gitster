using System.IO;

using Gitster.Services;
using Gitster.Services.Git;
using Gitster.Services.History;
using Gitster.Services.Search;
using Gitster.ViewModels;
using NSubstitute;
using System.Reflection;

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

    [TestMethod]
    public void SelectParentCommit_SelectsFirstVisibleParent()
    {
        var vm = CreateViewModel();
        var parent = Item("parent");
        var child = Item("child", parentShas: [parent.FullSha]);
        vm.Items = [child, parent];
        vm.SelectedCommit = child;

        vm.SelectParentCommitCommand.Execute(null);

        Assert.AreSame(parent, vm.SelectedCommit);
    }

    [TestMethod]
    public void SelectChildCommit_SelectsNearestVisibleChild()
    {
        var vm = CreateViewModel();
        var parent = Item("parent");
        var child = Item("child", parentShas: [parent.FullSha]);
        var grandchild = Item("grandchild", parentShas: [child.FullSha]);
        vm.Items = [grandchild, child, parent];
        vm.SelectedCommit = child;

        vm.SelectChildCommitCommand.Execute(null);

        Assert.AreSame(grandchild, vm.SelectedCommit);
    }

    [TestMethod]
    public void ShowOutgoingIncomingOnly_WithSearchFilter_HidesSyncedLocalRows()
    {
        var vm = CreateViewModel();
        var outgoingKeep = Item("outgoing-keep", message: "keep outgoing", remoteState: CommitRemoteState.LocalOnly);
        var syncedKeep = Item("synced-keep", message: "keep synced", remoteState: CommitRemoteState.OnRemote);
        var outgoingDrop = Item("outgoing-drop", message: "drop outgoing", remoteState: CommitRemoteState.LocalOnly);
        var incomingKeep = Item("incoming-keep", message: "keep incoming", remoteState: CommitRemoteState.Incoming);

        SetPrivateField(vm, "_allRows", new List<CommitItem> { outgoingKeep, syncedKeep, outgoingDrop });
        SetPrivateField(vm, "_incomingRows", new List<CommitItem> { incomingKeep });
        SetPrivateField(vm, "_remoteSets", new RemoteSets(
            Array.Empty<CommitInfo>(),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { outgoingKeep.FullSha, outgoingDrop.FullSha },
            new Dictionary<string, string>(),
            HasTrackingBranch: true,
            HasRemote: true,
            RemoteName: "origin",
            RemoteUrl: "https://example.test/repo.git"));
        SetPrivateField(vm, "_query", CommitQuery.Parse("message:keep"));

        vm.ShowOutgoingIncomingOnly = true;

        var visibleShas = vm.Items.OfType<CommitItem>().Select(c => c.FullSha).ToList();
        CollectionAssert.AreEquivalent(
            new[] { incomingKeep.FullSha, outgoingKeep.FullSha },
            visibleShas);
    }

    [TestMethod]
    public void GraphColumnWidthForLaneCount_WithDenseGraph_ExpandsBeyondCompactColumn()
    {
        Assert.AreEqual(28, CommitListViewModel.GraphColumnWidthForLaneCount(1));
        Assert.AreEqual(62, CommitListViewModel.GraphColumnWidthForLaneCount(5));
        Assert.AreEqual(240, CommitListViewModel.GraphColumnWidthForLaneCount(80));
    }

    [TestMethod]
    public void CanDropCommitForFixup_WithLocalOnlyCommits_AllowsDrop()
    {
        var source = Item("source", remoteState: CommitRemoteState.LocalOnly);
        var target = Item("target", remoteState: CommitRemoteState.LocalOnly);

        var allowed = CommitListViewModel.CanDropCommitForFixup(source, target, out var reason);

        Assert.IsTrue(allowed);
        Assert.AreEqual(string.Empty, reason);
    }

    [TestMethod]
    public void CanDropCommitForFixup_WithSyncedCommit_BlocksPublishedRewrite()
    {
        var source = Item("source", remoteState: CommitRemoteState.LocalOnly);
        var target = Item("target", remoteState: CommitRemoteState.OnRemote);

        var allowed = CommitListViewModel.CanDropCommitForFixup(source, target, out var reason);

        Assert.IsFalse(allowed);
        StringAssert.Contains(reason, "published history");
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

    private static CommitItem Item(
        string id,
        string? message = null,
        CommitRemoteState remoteState = CommitRemoteState.LocalOnly,
        IReadOnlyList<string>? parentShas = null) =>
        new(
            message ?? $"Message {id}",
            new DateTime(2026, 1, 1),
            id,
            "Tester",
            remoteState: remoteState,
            fullSha: $"{id}-full-sha",
            parentShas: parentShas);

    private static void SetPrivateField<T>(object target, string fieldName, T value)
    {
        var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Field '{fieldName}' was not found.");
        field.SetValue(target, value);
    }
}
