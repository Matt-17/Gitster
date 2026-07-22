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

    private static async Task<CommitRefNavigatorViewModel> CreateLoadedViewModelAsync()
    {
        var git = Substitute.For<IGitBackend>();
        git.GetRefCatalogAsync().Returns(Task.FromResult<IReadOnlyList<RefCatalogItem>>(
        [
            new("master", "refs/heads/master", RefCatalogKind.LocalBranch, "sha1", IsCurrent: true, HasUpstream: true, Ahead: 0, Behind: 0),
            new("feature/x", "refs/heads/feature/x", RefCatalogKind.LocalBranch, "sha2", IsCurrent: false, HasUpstream: false, Ahead: 0, Behind: 0),
            new("origin/feature/x", "refs/remotes/origin/feature/x", RefCatalogKind.RemoteBranch, "sha3", IsCurrent: false, HasUpstream: false, Ahead: 0, Behind: 0),
        ]));

        var favoritesPath = Path.Combine(Path.GetTempPath(), "Gitster.Tests", Guid.NewGuid().ToString("N"), "favorites.json");
        var vm = new CommitRefNavigatorViewModel(git, new UiPreferencesService(), new BranchFavoritesService(favoritesPath));
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
