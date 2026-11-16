using System.IO;

using Gitster.Core;
using Gitster.Core.Git;
using Gitster.Core.History;
using Gitster.ViewModels;

using NSubstitute;

namespace Gitster.Tests;

[TestClass]
public sealed class CommitRefNavigatorViewModelTests
{
    [TestMethod]
    public async Task SetViewedTarget_MarksViewedRef_AndSurvivesRebuild()
    {
        var vm = await CreateLoadedViewModelAsync();

        // Default view is the current branch, so the checked-out branch carries the marker.
        Assert.IsTrue(FindNode(vm, "refs/heads/master")!.IsViewed);
        Assert.IsFalse(FindNode(vm, "refs/heads/feature/x")!.IsViewed);

        vm.SetViewedTarget(HistoryScope.Ref, "refs/heads/feature/x");

        Assert.IsFalse(FindNode(vm, "refs/heads/master")!.IsViewed);
        Assert.IsTrue(FindNode(vm, "refs/heads/feature/x")!.IsViewed);

        // A filter rebuilds the tree; the marker must stick to the viewed ref.
        vm.FilterText = "feature";
        Assert.IsTrue(FindNode(vm, "refs/heads/feature/x")!.IsViewed);
    }

    [TestMethod]
    public async Task SetViewedTarget_AllBranches_ClearsMarker()
    {
        var vm = await CreateLoadedViewModelAsync();
        vm.SetViewedTarget(HistoryScope.Ref, "refs/remotes/origin/feature/x");

        Assert.IsTrue(FindNode(vm, "refs/remotes/origin/feature/x")!.IsViewed);

        vm.SetViewedTarget(HistoryScope.AllBranches, null);

        Assert.IsFalse(FindNode(vm, "refs/heads/master")!.IsViewed);
        Assert.IsFalse(FindNode(vm, "refs/remotes/origin/feature/x")!.IsViewed);
    }

    [TestMethod]
    public async Task SelectNode_ReclickingSameNode_RetargetsHistoryView()
    {
        var vm = await CreateLoadedViewModelAsync();
        var selectCount = 0;
        vm.SelectRefAsync = _ => { selectCount++; return Task.CompletedTask; };
        var node = FindNode(vm, "refs/heads/feature/x")!;

        vm.SelectNodeCommand.Execute(node);
        vm.SelectNodeCommand.Execute(node);

        Assert.AreEqual(2, selectCount, "a re-click must re-target the history view");
    }

    [TestMethod]
    public async Task ShowCurrentBranch_ClearsTreeSelection_SoTheBranchCanBeReclicked()
    {
        var vm = await CreateLoadedViewModelAsync();
        var selectCount = 0;
        vm.SelectRefAsync = _ => { selectCount++; return Task.CompletedTask; };
        vm.SelectCurrentBranchAsync = () => Task.CompletedTask;
        var node = FindNode(vm, "refs/heads/feature/x")!;

        vm.SelectNodeCommand.Execute(node);
        await vm.ShowCurrentBranchCommand.ExecuteAsync(null);

        Assert.IsNull(vm.SelectedNode);

        vm.SelectNodeCommand.Execute(node);

        Assert.AreEqual(2, selectCount);
    }

    [TestMethod]
    public async Task CheckoutNode_RemoteBranch_InvokesRemoteCheckout()
    {
        var vm = await CreateLoadedViewModelAsync();
        RefCatalogItem? requested = null;
        string? localRequested = null;
        vm.CheckoutRemoteBranchAsync = item => { requested = item; return Task.CompletedTask; };
        vm.CheckoutBranchAsync = name => { localRequested = name; return Task.CompletedTask; };

        await vm.CheckoutNodeCommand.ExecuteAsync(FindNode(vm, "refs/remotes/origin/feature/x"));

        Assert.AreEqual("refs/remotes/origin/feature/x", requested?.CanonicalName);
        Assert.IsNull(localRequested, "a remote branch must not go through the local checkout");
    }

    [TestMethod]
    public async Task CheckoutNode_LocalBranch_InvokesLocalCheckout()
    {
        var vm = await CreateLoadedViewModelAsync();
        string? localRequested = null;
        var remoteCalled = false;
        vm.CheckoutBranchAsync = name => { localRequested = name; return Task.CompletedTask; };
        vm.CheckoutRemoteBranchAsync = _ => { remoteCalled = true; return Task.CompletedTask; };

        await vm.CheckoutNodeCommand.ExecuteAsync(FindNode(vm, "refs/heads/feature/x"));

        Assert.AreEqual("feature/x", localRequested);
        Assert.IsFalse(remoteCalled);
    }

    [TestMethod]
    [DataRow("origin/feature/x", "feature/x")]
    [DataRow("upstream/main", "main")]
    [DataRow("my/remote/topic", "topic")]
    [DataRow("unknown/topic", "topic")]
    public void SuggestLocalBranchName_StripsLongestMatchingRemote(string remoteBranch, string expected)
    {
        var name = MainWindowViewModel.SuggestLocalBranchName(remoteBranch, ["origin", "upstream", "my", "my/remote"]);

        Assert.AreEqual(expected, name);
    }

    [TestMethod]
    public async Task Rebuild_CompactFoldersOn_MergesSingleChildFolderChain()
    {
        var ui = new UiPreferencesService(TempPrefsPath()) { CompactFolders = true };
        var vm = await CreateLoadedViewModelAsync(ui);

        var remotes = vm.RefTree.Single(n => n.Name.StartsWith("Remotes", StringComparison.Ordinal));
        Assert.AreEqual("Remotes/origin/feature", remotes.Name);
        Assert.AreEqual("x", remotes.Children.Single().Name);

        var branches = vm.RefTree.Single(n => n.Name == "Branches");
        Assert.AreEqual(2, branches.Children.Count, "a folder with a branch and a sub-folder stays as it is");
    }

    [TestMethod]
    public async Task Rebuild_CompactFoldersToggled_RebuildsNestedTree()
    {
        var ui = new UiPreferencesService(TempPrefsPath()) { CompactFolders = true };
        var vm = await CreateLoadedViewModelAsync(ui);

        ui.CompactFolders = false;

        var remotes = vm.RefTree.Single(n => n.Name == "Remotes");
        Assert.AreEqual("origin", remotes.Children.Single().Name);
        Assert.IsNotNull(FindNode(vm, "refs/remotes/origin/feature/x"));
    }

    private static string TempPrefsPath() =>
        Path.Combine(Path.GetTempPath(), "Gitster.Tests", Guid.NewGuid().ToString("N"), "ui.json");

    private static async Task<CommitRefNavigatorViewModel> CreateLoadedViewModelAsync(UiPreferencesService? ui = null)
    {
        var git = Substitute.For<IGitBackend>();
        git.GetRefCatalogAsync().Returns(Task.FromResult<IReadOnlyList<RefCatalogItem>>(
        [
            new("master", "refs/heads/master", RefCatalogKind.LocalBranch, "sha1", IsCurrent: true, HasUpstream: true, Ahead: 0, Behind: 0),
            new("feature/x", "refs/heads/feature/x", RefCatalogKind.LocalBranch, "sha2", IsCurrent: false, HasUpstream: false, Ahead: 0, Behind: 0),
            new("origin/feature/x", "refs/remotes/origin/feature/x", RefCatalogKind.RemoteBranch, "sha3", IsCurrent: false, HasUpstream: false, Ahead: 0, Behind: 0),
        ]));

        var favoritesPath = Path.Combine(Path.GetTempPath(), "Gitster.Tests", Guid.NewGuid().ToString("N"), "favorites.json");
        var vm = new CommitRefNavigatorViewModel(git, ui ?? new UiPreferencesService(), new BranchFavoritesService(favoritesPath));
        await vm.LoadAsync();
        return vm;
    }

    private static CommitRefNode? FindNode(CommitRefNavigatorViewModel vm, string canonicalName)
    {
        foreach (var root in vm.RefTree)
        {
            if (FindNode(root, canonicalName) is { } match)
                return match;
        }

        return null;
    }

    private static CommitRefNode? FindNode(CommitRefNode node, string canonicalName)
    {
        if (string.Equals(node.CanonicalName, canonicalName, StringComparison.Ordinal))
            return node;

        foreach (var child in node.Children)
        {
            if (FindNode(child, canonicalName) is { } match)
                return match;
        }

        return null;
    }
}
