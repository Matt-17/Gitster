using System.IO;
using System.Collections.Specialized;
using Gitster.Services;
using Gitster.Core;
using Gitster.ViewModels;

namespace Gitster.Tests;

[TestClass]
public sealed class MemoryAuditTests
{
    [TestMethod]
    public void GravatarCachePrune_WhenMoreThanLimit_DeletesOldestEntries()
    {
        var dir = CreateTempDirectory();
        try
        {
            for (var i = 0; i < 5; i++)
            {
                var path = Path.Combine(dir, $"{i}.png");
                File.WriteAllText(path, i.ToString());
                File.SetLastAccessTimeUtc(path, DateTime.UtcNow.AddMinutes(-i));
            }

            var deleted = GravatarService.PruneCacheDirectory(dir, maxEntries: 2);

            Assert.AreEqual(3, deleted);
            var remaining = Directory.GetFiles(dir, "*.png")
                .Select(Path.GetFileName)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            CollectionAssert.AreEqual(new[] { "0.png", "1.png" }, remaining);
        }
        finally
        {
            DeleteDirectory(dir);
        }
    }

    [TestMethod]
    public void SnapshotCleanup_WhenRecentFilesExceedLimit_KeepsNewestFilesOnly()
    {
        var dir = CreateTempDirectory();
        try
        {
            var now = new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);
            for (var i = 0; i < 5; i++)
            {
                var path = Path.Combine(dir, $"{i}.json");
                File.WriteAllText(path, "{}");
                File.SetCreationTimeUtc(path, now.AddMinutes(-i).UtcDateTime);
            }

            var deleted = SnapshotService.CleanupOldSnapshots(dir, maxSnapshotFiles: 3, now);

            Assert.AreEqual(2, deleted);
            var remaining = Directory.GetFiles(dir, "*.json")
                .Select(Path.GetFileName)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            CollectionAssert.AreEqual(new[] { "0.json", "1.json", "2.json" }, remaining);
        }
        finally
        {
            DeleteDirectory(dir);
        }
    }

    [TestMethod]
    public void SnapshotCleanup_WhenOlderDailyDuplicatesExist_KeepsOnePerDay()
    {
        var dir = CreateTempDirectory();
        try
        {
            var now = new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);
            var older = Path.Combine(dir, "older.json");
            var newer = Path.Combine(dir, "newer.json");
            File.WriteAllText(older, "{}");
            File.WriteAllText(newer, "{}");
            File.SetCreationTimeUtc(older, now.AddDays(-10).AddHours(-1).UtcDateTime);
            File.SetCreationTimeUtc(newer, now.AddDays(-10).UtcDateTime);

            var deleted = SnapshotService.CleanupOldSnapshots(dir, maxSnapshotFiles: 10, now);

            Assert.AreEqual(1, deleted);
            Assert.AreEqual(1, Directory.GetFiles(dir, "*.json").Length);
            Assert.IsTrue(File.Exists(newer), "The newest snapshot for the day should be retained.");
        }
        finally
        {
            DeleteDirectory(dir);
        }
    }

    [TestMethod]
    public void RangeObservableCollection_WhenReplacementIsIdentical_DoesNotRaiseReset()
    {
        var collection = new RangeObservableCollection<int>();
        collection.ReplaceAll([1, 2, 3]);

        var resetCount = 0;
        collection.CollectionChanged += (_, args) =>
        {
            if (args.Action == NotifyCollectionChangedAction.Reset)
                resetCount++;
        };

        collection.ReplaceAll([1, 2, 3]);

        Assert.AreEqual(0, resetCount);
    }

    [TestMethod]
    public void RangeObservableCollection_WhenAddRangeIsEmpty_DoesNotRaiseReset()
    {
        var collection = new RangeObservableCollection<int>();
        var resetCount = 0;
        collection.CollectionChanged += (_, args) =>
        {
            if (args.Action == NotifyCollectionChangedAction.Reset)
                resetCount++;
        };

        collection.AddRange([]);

        Assert.AreEqual(0, resetCount);
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "gitster-memory-audit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectory(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Best-effort cleanup for CI temp files.
        }
    }
}
