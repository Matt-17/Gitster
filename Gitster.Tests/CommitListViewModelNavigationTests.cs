using System.IO;

using Gitster.Services;
using Gitster.Core;
using Gitster.Core.Git;
using Gitster.Core.History;
using Gitster.Core.Search;
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
    public void ShowOutgoingIncomingOnly_BeforeRemoteSetsLoaded_ShowsRemoteHistoryAsChecking()
    {
        var vm = CreateViewModel();
        var local = Item("local", remoteState: CommitRemoteState.LocalOnly);

        SetPrivateField(vm, "_allRows", new List<CommitItem> { local });
        SetPrivateField(vm, "_incomingRows", new List<CommitItem>());
        SetPrivateField<RemoteSets?>(vm, "_remoteSets", null);
        SetPrivateEnumField(vm, "_remoteHistoryState", "Pending");

        vm.ShowOutgoingIncomingOnly = true;

        var remoteHeader = vm.Items.OfType<CommitSectionHeader>().First(h => h.IsIncoming);
        var remoteEmpty = vm.Items.OfType<CommitSectionEmptyRow>().First(r => !r.IsOutgoing);
        var localHeader = vm.Items.OfType<CommitSectionHeader>().First(h => h.IsOutgoing);

        Assert.AreEqual("Remote History (checking...)", remoteHeader.Title);
        Assert.AreEqual("checking remote branch history...", remoteEmpty.Message);
        Assert.AreEqual("Local History (1 outgoing)", localHeader.Title);
    }

    [TestMethod]
    public void ShowOutgoingIncomingOnly_WhenNoRemoteConfigured_KeepsRemoteHistoryHeader()
    {
        var vm = CreateViewModel();
        var local = Item("local", remoteState: CommitRemoteState.NoTrackingBranch);

        SetPrivateField(vm, "_allRows", new List<CommitItem> { local });
        SetPrivateField(vm, "_incomingRows", new List<CommitItem>());
        SetPrivateField(vm, "_remoteSets", RemoteSets.Empty);
        SetPrivateEnumField(vm, "_remoteHistoryState", "Loaded");

        vm.ShowOutgoingIncomingOnly = true;

        var remoteHeader = vm.Items.OfType<CommitSectionHeader>().First(h => h.IsIncoming);
        var remoteEmpty = vm.Items.OfType<CommitSectionEmptyRow>().First(r => !r.IsOutgoing);

        Assert.AreEqual("Remote History (0)", remoteHeader.Title);
        Assert.AreEqual("no remote configured", remoteEmpty.Message);
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

    [TestMethod]
    public void RebuildRows_LocalBranchRefWithoutRemoteBranch_ShowsBothHeadersAndActionLinks()
    {
        var vm = CreateViewModel();
        var local = Item("local", remoteState: CommitRemoteState.NoTrackingBranch);

        SetPrivateField(vm, "_allRows", new List<CommitItem> { local });
        SetPrivateField(vm, "_incomingRows", new List<CommitItem>());
        SetPrivateField(vm, "_remoteSets", RemoteSets.Empty with
        {
            HasRemote = true,
            RemoteName = "origin",
            LocalBranchName = "feature/x",
        });
        SetPrivateEnumField(vm, "_remoteHistoryState", "Loaded");
        SetPrivateField(vm, "_selectedTarget", HistoryTarget.ForRef("refs/heads/feature/x", "feature/x"));

        vm.RebuildRows();

        var headers = vm.Items.OfType<CommitSectionHeader>().ToList();
        Assert.AreEqual(2, headers.Count, "both section headers must always be present for a branch");
        Assert.IsTrue(headers[0].IsIncoming);
        Assert.IsTrue(headers[1].IsOutgoing);

        var remoteEmpty = vm.Items.OfType<CommitSectionEmptyRow>().First(r => !r.IsOutgoing);
        Assert.AreEqual("no remote branch", remoteEmpty.Message);
        Assert.IsTrue(remoteEmpty.HasLinks, "publish link expected");
        Assert.IsTrue(remoteEmpty.Links.Any(l => l.Text == "push"));
    }

    [TestMethod]
    public void RebuildRows_LocalBranchRefWithSameNameRemoteBranch_OffersSetUpstreamLink()
    {
        var vm = CreateViewModel();
        var local = Item("local", remoteState: CommitRemoteState.NoTrackingBranch);

        SetPrivateField(vm, "_allRows", new List<CommitItem> { local });
        SetPrivateField(vm, "_incomingRows", new List<CommitItem>());
        SetPrivateField(vm, "_remoteSets", RemoteSets.Empty with
        {
            HasRemote = true,
            RemoteName = "origin",
            LocalBranchName = "feature/x",
            RemoteBranchName = "origin/feature/x",
            HasSameNameRemoteBranch = true,
        });
        SetPrivateEnumField(vm, "_remoteHistoryState", "Loaded");
        SetPrivateField(vm, "_selectedTarget", HistoryTarget.ForRef("refs/heads/feature/x", "feature/x"));

        vm.RebuildRows();

        var remoteEmpty = vm.Items.OfType<CommitSectionEmptyRow>().First(r => !r.IsOutgoing);
        Assert.IsTrue(remoteEmpty.Links.Any(l => l.Text == "connect to remote branch"));
        Assert.IsTrue(remoteEmpty.Links.Any(l => l.Text == "push"));
    }

    [TestMethod]
    public void RebuildRows_RemoteOnlyBranchRef_ShowsCommitsUnderRemoteAndCheckoutLink()
    {
        var vm = CreateViewModel();
        var remoteCommit = Item("remote", remoteState: CommitRemoteState.Incoming);

        SetPrivateField(vm, "_allRows", new List<CommitItem> { remoteCommit });
        SetPrivateField(vm, "_incomingRows", new List<CommitItem>());
        SetPrivateField(vm, "_remoteSets", RemoteSets.Empty with
        {
            HasRemote = true,
            RemoteName = "origin",
            HasLocalBranch = false,
            RemoteBranchName = "origin/feature/x",
        });
        SetPrivateEnumField(vm, "_remoteHistoryState", "Loaded");
        SetPrivateField(vm, "_selectedTarget", HistoryTarget.ForRef("refs/remotes/origin/feature/x", "origin/feature/x"));

        vm.RebuildRows();

        var headers = vm.Items.OfType<CommitSectionHeader>().ToList();
        Assert.AreEqual(2, headers.Count);
        Assert.AreEqual("Local History (0)", headers[0].Title);
        Assert.AreEqual("Remote History (1)", headers[1].Title);

        // The compact local section sits on top; the remote branch's commits follow below.
        var itemList = vm.Items.ToList();
        Assert.IsTrue(itemList.IndexOf(remoteCommit) > itemList.IndexOf(headers[1]));

        var localEmpty = vm.Items.OfType<CommitSectionEmptyRow>().First(r => r.IsOutgoing);
        Assert.AreEqual("no local branch", localEmpty.Message);
        Assert.IsTrue(localEmpty.Links.Any(l => l.Text.Contains("checkout")));
    }

    [TestMethod]
    public void RebuildRows_RemoteBranchRefWithLocalCounterpart_OffersShowLocalAndCheckout()
    {
        var vm = CreateViewModel();
        var remoteCommit = Item("remote", remoteState: CommitRemoteState.OnRemote);

        SetPrivateField(vm, "_allRows", new List<CommitItem> { remoteCommit });
        SetPrivateField(vm, "_incomingRows", new List<CommitItem>());
        SetPrivateField(vm, "_remoteSets", RemoteSets.Empty with
        {
            HasRemote = true,
            HasTrackingBranch = true,
            RemoteName = "origin",
            HasLocalBranch = true,
            LocalBranchName = "feature/x",
            RemoteBranchName = "origin/feature/x",
        });
        SetPrivateEnumField(vm, "_remoteHistoryState", "Loaded");
        SetPrivateField(vm, "_selectedTarget", HistoryTarget.ForRef("refs/remotes/origin/feature/x", "origin/feature/x"));

        vm.RebuildRows();

        var localEmpty = vm.Items.OfType<CommitSectionEmptyRow>().First(r => r.IsOutgoing);
        StringAssert.Contains(localEmpty.Message, "feature/x");
        Assert.IsTrue(localEmpty.Links.Any(l => l.Text.Contains("show local history")));
        Assert.IsTrue(localEmpty.Links.Any(l => l.Text.Contains("checkout")));
    }

    [TestMethod]
    public void RebuildRows_BranchRefWhilePending_KeepsBothHeadersVisible()
    {
        var vm = CreateViewModel();
        var local = Item("local");

        SetPrivateField(vm, "_allRows", new List<CommitItem> { local });
        SetPrivateField(vm, "_incomingRows", new List<CommitItem>());
        SetPrivateField<RemoteSets?>(vm, "_remoteSets", null);
        SetPrivateEnumField(vm, "_remoteHistoryState", "Pending");
        SetPrivateField(vm, "_selectedTarget", HistoryTarget.ForRef("refs/heads/feature/x", "feature/x"));

        vm.RebuildRows();

        var headers = vm.Items.OfType<CommitSectionHeader>().ToList();
        Assert.AreEqual(2, headers.Count, "switching branches must never drop the section headers");
        Assert.AreEqual("Remote History (checking...)", headers[0].Title);
    }

    [TestMethod]
    public void RebuildRows_TagRef_ShowsPlainListWithoutHeaders()
    {
        var vm = CreateViewModel();
        var commit = Item("tagged");

        SetPrivateField(vm, "_allRows", new List<CommitItem> { commit });
        SetPrivateField(vm, "_incomingRows", new List<CommitItem>());
        SetPrivateEnumField(vm, "_remoteHistoryState", "Loaded");
        SetPrivateField(vm, "_selectedTarget", HistoryTarget.ForRef("refs/tags/v1.0", "v1.0"));

        vm.RebuildRows();

        Assert.AreEqual(0, vm.Items.OfType<CommitSectionHeader>().Count());
        Assert.AreEqual(1, vm.Items.Count);
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

    private static void SetPrivateEnumField(object target, string fieldName, string value)
    {
        var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Field '{fieldName}' was not found.");
        field.SetValue(target, Enum.Parse(field.FieldType, value));
    }
}
