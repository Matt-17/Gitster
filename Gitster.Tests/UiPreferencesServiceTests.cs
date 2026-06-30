using System.IO;

using Gitster.Services;

namespace Gitster.Tests;

[TestClass]
public sealed class UiPreferencesServiceTests
{
    [TestMethod]
    public void Load_OldJsonWithoutSplitterSettings_PreservesExistingPreferences()
    {
        var path = CreateSettingsPath();
        File.WriteAllText(path, """
        {
          "UseRelativeDates": true,
          "GravatarEnabled": true,
          "BranchTreeView": true
        }
        """);

        var service = new UiPreferencesService(path);

        Assert.IsTrue(service.UseRelativeDates);
        Assert.IsTrue(service.GravatarEnabled);
        Assert.IsTrue(service.BranchTreeView);
        Assert.IsNull(service.GetSplitterLength("missing"));
    }

    [TestMethod]
    public void SetSplitterLength_SavesAndReloadsValue()
    {
        var path = CreateSettingsPath();
        var service = new UiPreferencesService(path);

        service.SetSplitterLength("CommitListView.DiffHeight", 184.5);

        var reloaded = new UiPreferencesService(path);
        Assert.AreEqual(184.5, reloaded.GetSplitterLength("CommitListView.DiffHeight")!.Value, 0.001);
    }

    [TestMethod]
    public void Load_InvalidSplitterValues_IgnoresThem()
    {
        var path = CreateSettingsPath();
        File.WriteAllText(path, """
        {
          "SplitterLengths": {
            "valid": 240,
            "zero": 0,
            "negative": -32
          }
        }
        """);

        var service = new UiPreferencesService(path);

        Assert.AreEqual(240, service.GetSplitterLength("valid")!.Value, 0.001);
        Assert.IsNull(service.GetSplitterLength("zero"));
        Assert.IsNull(service.GetSplitterLength("negative"));
    }

    [TestMethod]
    public void SetSplitterLength_PreservesBooleanPreferences()
    {
        var path = CreateSettingsPath();
        File.WriteAllText(path, """
        {
          "UseRelativeDates": true,
          "GravatarEnabled": true,
          "BranchTreeView": true
        }
        """);

        var service = new UiPreferencesService(path);
        service.SetSplitterLength("BranchesMode.ActionPanelWidth", 360);

        var reloaded = new UiPreferencesService(path);
        Assert.IsTrue(reloaded.UseRelativeDates);
        Assert.IsTrue(reloaded.GravatarEnabled);
        Assert.IsTrue(reloaded.BranchTreeView);
        Assert.AreEqual(360, reloaded.GetSplitterLength("BranchesMode.ActionPanelWidth")!.Value, 0.001);
    }

    [TestMethod]
    public void SetSplitterLength_InvalidValues_DoNotPersist()
    {
        var path = CreateSettingsPath();
        var service = new UiPreferencesService(path);

        service.SetSplitterLength("nan", double.NaN);
        service.SetSplitterLength("infinity", double.PositiveInfinity);
        service.SetSplitterLength("zero", 0);

        var reloaded = new UiPreferencesService(path);
        Assert.IsNull(reloaded.GetSplitterLength("nan"));
        Assert.IsNull(reloaded.GetSplitterLength("infinity"));
        Assert.IsNull(reloaded.GetSplitterLength("zero"));
    }

    private static string CreateSettingsPath()
    {
        var dir = Path.Combine(Path.GetTempPath(), "Gitster.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "ui-settings.json");
    }
}
