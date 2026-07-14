using System.IO;

using Gitster.Services;
using Gitster.ApplicationLayer;

namespace Gitster.Tests;

[TestClass]
public sealed class RecentReposServiceTests
{
    [TestMethod]
    public void Pin_Unpin_AndOrder_PersistAcrossReload()
    {
        var path = CreateStoragePath();
        var service = new RecentReposService(path);

        service.Record(@"C:\repos\one");
        service.Record(@"C:\repos\two");
        service.Record(@"C:\repos\three");
        service.Pin(@"C:\repos\one");
        service.Pin(@"C:\repos\three");

        var reloaded = new RecentReposService(path);

        CollectionAssert.AreEqual(
            new[] { @"C:\repos\one", @"C:\repos\three" },
            reloaded.GetPinned().Select(r => r.FullPath).ToArray());

        reloaded.Unpin(@"C:\repos\one");
        Assert.IsFalse(reloaded.IsPinned(@"C:\repos\one"));
    }

    [TestMethod]
    public void Record_TrimsEleventhRecentButKeepsPinned()
    {
        var service = new RecentReposService(CreateStoragePath());
        service.Record(@"C:\repos\pinned");
        service.Pin(@"C:\repos\pinned");

        for (var i = 0; i < 11; i++)
            service.Record($@"C:\repos\repo-{i}");

        Assert.IsTrue(service.IsPinned(@"C:\repos\pinned"));
        Assert.AreEqual(10, service.GetRecent().Count);
        Assert.IsFalse(service.GetRecent().Any(r => r.FullPath == @"C:\repos\repo-0"));
    }

    [TestMethod]
    public void Load_CorruptJson_UsesEmptyList()
    {
        var path = CreateStoragePath();
        File.WriteAllText(path, "{ definitely not json");

        var service = new RecentReposService(path);

        Assert.AreEqual(0, service.Entries.Count);
    }

    private static string CreateStoragePath()
    {
        var dir = Path.Combine(Path.GetTempPath(), "Gitster.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "recent-repos.json");
    }
}
