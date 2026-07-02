using Gitster.Models;
using Gitster.Services;
using Gitster.ViewModels;

using NSubstitute;

namespace Gitster.Tests;

[TestClass]
public sealed class RepositorySwitchCoordinatorTests
{
    [TestMethod]
    public async Task SwitchAsync_CanceledSwitch_RestoresPreviousLoadedRepository()
    {
        var coordinator = CreateCoordinator();
        var state = new SwitchHarnessState();
        var restoredState = (RepositorySwitchState?)null;
        var callbacks = CreateCallbacks(
            coordinator,
            state,
            loadRepositoryAsync: (path, ct, progress) =>
                path == "next"
                    ? throw new OperationCanceledException(ct)
                    : Task.CompletedTask,
            restoreRepositoryAsync: previous =>
            {
                restoredState = previous;
                state.Path = previous.Path;
                state.FolderPath = previous.FolderPath;
                return Task.FromResult(previous.LoadedRepositoryPath);
            });

        var first = await coordinator.SwitchAsync(
            new RepositorySwitchRequest("previous", RecordRecent: true, ShowLoadingWindow: false),
            callbacks);

        var second = await coordinator.SwitchAsync(
            new RepositorySwitchRequest("next", RecordRecent: true, ShowLoadingWindow: false),
            callbacks);

        Assert.IsTrue(first);
        Assert.IsFalse(second);
        Assert.AreEqual("previous", coordinator.LoadedRepositoryPath);
        Assert.AreEqual("previous", restoredState?.LoadedRepositoryPath);
        CollectionAssert.AreEqual(new[] { "previous" }, state.CommittedPaths);
    }

    [TestMethod]
    public async Task SwitchAsync_FailedFirstSwitch_ClearsStateAndReportsError()
    {
        var coordinator = CreateCoordinator();
        var state = new SwitchHarnessState();
        Exception? reportedError = null;
        var callbacks = CreateCallbacks(
            coordinator,
            state,
            loadRepositoryAsync: (_, _, _) => throw new InvalidOperationException("open failed"),
            restoreRepositoryAsync: previous =>
            {
                state.Path = previous.Path;
                state.FolderPath = previous.FolderPath;
                return Task.FromResult<string?>(null);
            },
            showOpenError: ex => reportedError = ex);

        var result = await coordinator.SwitchAsync(
            new RepositorySwitchRequest("broken", RecordRecent: true, ShowLoadingWindow: false),
            callbacks);

        Assert.IsFalse(result);
        Assert.IsNull(coordinator.LoadedRepositoryPath);
        Assert.IsInstanceOfType<InvalidOperationException>(reportedError);
        CollectionAssert.AreEqual(Array.Empty<string>(), state.CommittedPaths);
    }

    [TestMethod]
    public async Task SwitchAsync_LaterSwitch_SupersedesEarlierSwitch()
    {
        var coordinator = CreateCoordinator();
        var state = new SwitchHarnessState();
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var callbacks = CreateCallbacks(
            coordinator,
            state,
            loadRepositoryAsync: async (path, ct, progress) =>
            {
                if (path == "first")
                {
                    firstStarted.SetResult();
                    await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                }
            });

        var firstTask = coordinator.SwitchAsync(
            new RepositorySwitchRequest("first", RecordRecent: true, ShowLoadingWindow: false),
            callbacks);

        await WaitForAsync(firstStarted.Task);

        var secondTask = coordinator.SwitchAsync(
            new RepositorySwitchRequest("second", RecordRecent: true, ShowLoadingWindow: false),
            callbacks);

        var first = await firstTask;
        var second = await secondTask;

        Assert.IsFalse(first);
        Assert.IsTrue(second);
        Assert.AreEqual("second", coordinator.LoadedRepositoryPath);
        CollectionAssert.AreEqual(new[] { "second" }, state.CommittedPaths);
    }

    [TestMethod]
    public async Task LifecycleInitializeAsync_RunsInitialSwitchOnlyOnce()
    {
        var lifecycle = new RepositoryLifecycleCoordinator(CreateCoordinator());
        var opened = new List<string>();

        await lifecycle.InitializeAsync("repo-a", (path, _, _) =>
        {
            opened.Add(path);
            return Task.FromResult(true);
        });
        await lifecycle.InitializeAsync("repo-b", (path, _, _) =>
        {
            opened.Add(path);
            return Task.FromResult(true);
        });

        Assert.IsTrue(lifecycle.InitialRepositoryLoadStarted);
        CollectionAssert.AreEqual(new[] { "repo-a" }, opened);
    }

    private static RepositorySwitchCoordinator CreateCoordinator() =>
        new(Substitute.For<IWindowService>(), new HeadRefreshCoordinator(TimeSpan.FromMilliseconds(1)));

    private static RepositorySwitchCallbacks CreateCallbacks(
        RepositorySwitchCoordinator coordinator,
        SwitchHarnessState state,
        Func<string, CancellationToken, IProgress<RepositoryLoadProgress>?, Task> loadRepositoryAsync,
        Func<RepositorySwitchState, Task<string?>>? restoreRepositoryAsync = null,
        Action<Exception>? showOpenError = null) =>
        new(
            () => new RepositorySwitchState(state.Path, state.FolderPath, coordinator.LoadedRepositoryPath),
            loadRepositoryAsync,
            (path, _) =>
            {
                state.Path = path;
                state.FolderPath = path;
                state.CommittedPaths.Add(path);
            },
            restoreRepositoryAsync ?? (previous =>
            {
                state.Path = previous.Path;
                state.FolderPath = previous.FolderPath;
                return Task.FromResult(previous.LoadedRepositoryPath);
            }),
            showOpenError ?? (_ => { }));

    private static async Task WaitForAsync(Task task)
    {
        var completed = await Task.WhenAny(task, Task.Delay(1000));
        Assert.AreSame(task, completed, "Timed out waiting for repository switching.");
        await task;
    }

    private sealed class SwitchHarnessState
    {
        public string Path { get; set; } = string.Empty;

        public string FolderPath { get; set; } = string.Empty;

        public List<string> CommittedPaths { get; } = [];
    }
}
