using System.IO;

using Gitster.Core.Features;

namespace Gitster.Tests;

[TestClass]
public sealed class FeatureServicesTests
{
    [TestMethod]
    public void ReflogEntry_Parse_ReadsSelectorShaActionMessageAndDate()
    {
        var row = ReflogEntry.Parse("HEAD@{3}\tabc123\treset\tmoving to HEAD~1\t2026-07-01T10:00:00+02:00");

        Assert.AreEqual("HEAD@{3}", row.Selector);
        Assert.AreEqual("abc123", row.Sha);
        Assert.AreEqual("reset", row.Action);
        Assert.AreEqual("moving to HEAD~1", row.Message);
        Assert.IsNotNull(row.Date);
    }

    [TestMethod]
    public void CommitSigningStatusParser_MapsGitMarkers()
    {
        Assert.AreEqual(CommitSigningStatus.Good, CommitSigningStatusParser.Parse("G"));
        Assert.AreEqual(CommitSigningStatus.Bad, CommitSigningStatusParser.Parse("B"));
        Assert.AreEqual(CommitSigningStatus.NoSignature, CommitSigningStatusParser.Parse("N"));
    }

    [TestMethod]
    public void CommitItem_HasSigningBadge_HidesUnsignedCommits()
    {
        var item = new CommitItem(
            "Unsigned commit",
            new DateTime(2026, 1, 1),
            "abc1234",
            "Tester",
            remoteState: Gitster.Core.Git.CommitRemoteState.OnRemote,
            fullSha: "abc1234-full");

        item.SigningStatus = CommitSigningStatus.NoSignature;
        Assert.IsFalse(item.HasSigningBadge);

        item.SigningStatus = CommitSigningStatus.Good;
        Assert.IsTrue(item.HasSigningBadge);
    }

    [TestMethod]
    public void SubmoduleStatusParser_ReadsInitializedDirtyAndUninitializedRows()
    {
        var rows = SubmoduleStatusParser.Parse("""
         abc123 libs/clean
        +def456 libs/dirty
        -000000 libs/missing
        """);

        Assert.AreEqual(3, rows.Count);
        Assert.IsTrue(rows[0].IsInitialized);
        Assert.IsTrue(rows[1].HasChanges);
        Assert.IsFalse(rows[2].IsInitialized);
    }

    [TestMethod]
    public void GitIgnoreTemplateService_AppendsMarkerAndDedupesLines()
    {
        var dir = Path.Combine(Path.GetTempPath(), "Gitster.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, ".gitignore"), "bin/\n");
            var service = new GitIgnoreTemplateService();

            service.AppendTemplate(dir, "VisualStudio");
            service.AppendTemplate(dir, "VisualStudio");

            var text = File.ReadAllText(Path.Combine(dir, ".gitignore"));
            Assert.AreEqual(1, CountOccurrences(text, "obj/"));
            Assert.AreEqual(1, CountOccurrences(text, "# --- Gitster: VisualStudio ---"));
            Assert.AreEqual(1, CountOccurrences(text, "bin/"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [TestMethod]
    public void UpdateCheckService_HandlesPrefixesAndPrereleaseLabels()
    {
        Assert.IsTrue(UpdateCheckService.IsNewerVersion("1.2.0", "v1.3.0"));
        Assert.IsFalse(UpdateCheckService.IsNewerVersion("1.3.0", "v1.3.0-beta.1"));
    }

    [TestMethod]
    public void TimestampPresetResolver_ResolvesRelativePresets()
    {
        var now = new DateTime(2026, 7, 1, 12, 0, 0);

        Assert.AreEqual(new DateTime(2026, 6, 30, 9, 0, 0), TimestampPresetResolver.Resolve("yesterday 09:00", now));
        Assert.AreEqual(new DateTime(2026, 6, 26, 17, 30, 0), TimestampPresetResolver.Resolve("last Friday 17:30", now));
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }
}
