using System.IO;

using Gitster.Models;
using Gitster.Services;
using Gitster.Services.Git;

using NSubstitute;

namespace Gitster.Tests;

[TestClass]
public sealed class RepositoryStateServiceTests
{
    [TestMethod]
    public async Task BeginOperationAsync_ExposesOperationRunningUntilDisposed()
    {
        var git = Substitute.For<IGitBackend>();
        using var service = new RepositoryStateService(git);

        using (await service.BeginOperationAsync())
        {
            Assert.IsTrue(service.IsOperationRunning);
        }

        Assert.IsFalse(service.IsOperationRunning);
    }

    [TestMethod]
    public async Task ThrowIfIndexLocked_WhenIndexLockExists_ReportsFriendlyMessage()
    {
        var repo = Path.Combine(Path.GetTempPath(), "Gitster.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(repo, ".git"));
        File.WriteAllText(Path.Combine(repo, ".git", "index.lock"), string.Empty);

        var git = Substitute.For<IGitBackend>();
        git.GetWorkingTreeStateAsync().Returns(Task.FromResult<WorkingTreeState>(new WorkingTreeState.Clean()));
        git.GetCurrentBranchAsync().Returns(Task.FromResult(new BranchInfo("main", 0, 0)));
        using var service = new RepositoryStateService(git);

        await service.AttachAsync(repo);

        var ex = Assert.ThrowsException<InvalidOperationException>(service.ThrowIfIndexLocked);
        StringAssert.Contains(ex.Message, "index.lock");
    }
}
