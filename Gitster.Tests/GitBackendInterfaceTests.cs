using Gitster.Services.Git;

namespace Gitster.Tests;

[TestClass]
public sealed class GitBackendInterfaceTests
{
    [TestMethod]
    public void IGitBackend_IsUmbrellaForRoleInterfaces()
    {
        var roles = new[]
        {
            typeof(IHistoryReader),
            typeof(IWorkingTreeOps),
            typeof(IHistoryRewriteOps),
            typeof(IRemoteOps),
            typeof(ITagOps),
            typeof(IStashOps),
            typeof(IBranchOps),
            typeof(IArchiveOps),
            typeof(IWorktreeOps),
            typeof(ISearchOps),
        };

        foreach (var role in roles)
            Assert.IsTrue(role.IsAssignableFrom(typeof(IGitBackend)), $"{role.Name} is not part of IGitBackend.");
    }

    [TestMethod]
    public void LibGit2Backends_ExposeRepositoryReadProviderForHistoryCache()
    {
        Assert.IsTrue(typeof(IRepositoryReadProvider).IsAssignableFrom(typeof(LibGit2Backend)));
        Assert.IsTrue(typeof(IRepositoryReadProvider).IsAssignableFrom(typeof(HybridGitBackend)));
    }
}
