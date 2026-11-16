using System.IO;
using System.Reflection;

using Gitster.Core;
using Gitster.Core.Git;
using Gitster.Core.History;
using Gitster.ViewModels;

namespace Gitster.Tests;

[TestClass]
public sealed class CommitListViewModelLoadTests
{
    [TestMethod]
    public async Task LoadAsync_WarmCache_RebuildsOnceWithRemoteSectionResolved()
    {
        using var repo = new GitTestRepo();
        repo.Commit("c1", "a.txt", "1");
        repo.Commit("c2", "a.txt", "2");
        var cachePath = Path.Combine(Path.GetTempPath(), "Gitster.Tests", Guid.NewGuid().ToString("N"));

        var git = new LibGit2Backend();
        await git.OpenAsync(repo.Path);
        var vm = new CommitListViewModel(git, new CommitHistoryService(git, cachePath), new UiPreferencesService());

        // First load fills the cache (cold path), then wait for its background stages.
        await vm.LoadAsync();
        await WarmTaskAsync(vm);

        repo.Commit("c3", "a.txt", "3");
        var snapshots = new List<IReadOnlyList<object>>();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(CommitListViewModel.Items))
                snapshots.Add(vm.Items);
        };

        await vm.LoadAsync();
        await WarmTaskAsync(vm);

        Assert.AreEqual(1, snapshots.Count, "a refresh on a warm cache must rebuild the list once");
        var headers = snapshots[0].OfType<CommitSectionHeader>().ToList();
        Assert.IsFalse(headers.Any(h => h.Title.Contains("checking", StringComparison.OrdinalIgnoreCase)),
            "remote info is known on a warm refresh and must not show as pending");
        CollectionAssert.AreEqual(
            new[] { "c3", "c2", "c1" },
            snapshots[0].OfType<CommitItem>().Select(c => c.Message).ToArray());
    }

    [TestMethod]
    public async Task LoadAsync_SelectionChangedDuringLoad_KeepsTheNewSelection()
    {
        using var repo = new GitTestRepo();
        repo.Commit("c1", "a.txt", "1");
        repo.Commit("c2", "a.txt", "2");
        var cachePath = Path.Combine(Path.GetTempPath(), "Gitster.Tests", Guid.NewGuid().ToString("N"));

        var git = new LibGit2Backend();
        await git.OpenAsync(repo.Path);
        var vm = new CommitListViewModel(git, new CommitHistoryService(git, cachePath), new UiPreferencesService());
        await vm.LoadAsync();
        await WarmTaskAsync(vm);

        vm.SelectedCommit = vm.Items.OfType<CommitItem>().First(c => c.Message == "c2");
        var load = vm.LoadAsync();
        // The user clicks another commit while the reload runs.
        vm.SelectedCommit = vm.Items.OfType<CommitItem>().First(c => c.Message == "c1");
        await load;

        Assert.AreEqual("c1", vm.SelectedCommit?.Message);
    }

    private static async Task WarmTaskAsync(CommitListViewModel vm)
    {
        var field = typeof(CommitListViewModel).GetField("_warmHistoryTask", BindingFlags.Instance | BindingFlags.NonPublic)!;
        if (field.GetValue(vm) is Task task)
            await task;
    }
}
