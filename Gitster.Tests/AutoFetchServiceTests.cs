using Gitster.Services;
using Gitster.Services.Git;

using NSubstitute;

namespace Gitster.Tests;

[TestClass]
public sealed class AutoFetchServiceTests
{
    [TestMethod]
    public async Task RunOnceAsync_AfterThreeFailures_BacksOffToFiveMinutes()
    {
        var git = Substitute.For<IGitBackend>();
        git.FetchAsync().Returns(Task.FromException(new InvalidOperationException("offline")));
        var service = new AutoFetchService(git);

        await service.RunOnceAsync();
        await service.RunOnceAsync();
        await service.RunOnceAsync();

        Assert.AreEqual(300, service.IntervalSeconds);
    }

    [TestMethod]
    public async Task RunOnceAsync_SuccessAfterFailure_ResetsBackoff()
    {
        var git = Substitute.For<IGitBackend>();
        git.FetchAsync()
            .Returns(
                Task.FromException(new InvalidOperationException("offline")),
                Task.FromException(new InvalidOperationException("offline")),
                Task.FromException(new InvalidOperationException("offline")),
                Task.CompletedTask);
        var service = new AutoFetchService(git);

        await service.RunOnceAsync();
        await service.RunOnceAsync();
        await service.RunOnceAsync();
        await service.RunOnceAsync();

        Assert.AreEqual(60, service.IntervalSeconds);
        Assert.IsNotNull(service.LastFetchAt);
    }

    [TestMethod]
    public async Task RunOnceAsync_WhenRemoteOperationIsRunning_SkipsFetch()
    {
        var git = Substitute.For<IGitBackend>();
        var service = new AutoFetchService(git)
        {
            IsRemoteOperationRunning = true,
        };

        await service.RunOnceAsync();

        await git.DidNotReceive().FetchAsync();
    }

    [TestMethod]
    public async Task RunOnceAsync_WhenRepositoryOperationIsRunning_SkipsFetch()
    {
        var git = Substitute.For<IGitBackend>();
        using var state = new RepositoryStateService(git);
        var service = new AutoFetchService(git, state);

        using (await state.BeginOperationAsync())
        {
            await service.RunOnceAsync();
        }

        await git.DidNotReceive().FetchAsync();
    }
}
