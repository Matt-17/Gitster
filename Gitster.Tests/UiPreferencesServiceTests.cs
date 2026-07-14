using System.IO;
using System.Xml.Linq;

using Gitster.Core.Models;
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

    [TestMethod]
    public void ThemePreference_SavesAndReloadsValue()
    {
        var path = CreateSettingsPath();
        var service = new UiPreferencesService(path);

        service.ThemePreference = ThemePreference.Dark;

        var reloaded = new UiPreferencesService(path);
        Assert.AreEqual(ThemePreference.Dark, reloaded.ThemePreference);
        Assert.IsTrue(reloaded.IsDarkTheme);
        Assert.IsFalse(reloaded.IsLightTheme);
        Assert.IsFalse(reloaded.IsSystemTheme);
    }

    [TestMethod]
    public void UpdateChecksEnabled_FilePathConstructor_SavesAndReloadsValue()
    {
        var path = CreateSettingsPath();
        var service = new UiPreferencesService(path);

        service.UpdateChecksEnabled = true;

        var reloaded = new UiPreferencesService(path);
        Assert.IsTrue(reloaded.UpdateChecksEnabled);
    }

    [TestMethod]
    public void PersistentLoggingEnabled_FilePathConstructor_DefaultsFalseAndRoundTrips()
    {
        var path = CreateSettingsPath();
        var service = new UiPreferencesService(path);

        Assert.IsFalse(service.PersistentLoggingEnabled);

        service.PersistentLoggingEnabled = true;

        var reloaded = new UiPreferencesService(path);
        Assert.IsTrue(reloaded.PersistentLoggingEnabled);
    }

    [TestMethod]
    public void ResolveEffectiveTheme_SystemUsesWindowsTheme()
    {
        Assert.AreEqual(
            ThemePreference.Light,
            ThemeService.ResolveEffectiveTheme(ThemePreference.System, () => true));
        Assert.AreEqual(
            ThemePreference.Dark,
            ThemeService.ResolveEffectiveTheme(ThemePreference.System, () => false));
    }

    [TestMethod]
    public void PaletteDictionaries_ExposeSameKeys()
    {
        var root = FindRepositoryRoot();
        var light = ReadResourceKeys(Path.Combine(root, "Gitster.Wpf", "Themes", "Palette.Light.xaml"));
        var dark = ReadResourceKeys(Path.Combine(root, "Gitster.Wpf", "Themes", "Palette.Dark.xaml"));

        CollectionAssert.AreEqual(light.Order().ToArray(), dark.Order().ToArray());
    }

    private static string CreateSettingsPath()
    {
        var dir = Path.Combine(Path.GetTempPath(), "Gitster.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "ui-settings.json");
    }

    private static string FindRepositoryRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (!string.IsNullOrWhiteSpace(dir))
        {
            if (File.Exists(Path.Combine(dir, "Gitster.slnx")))
                return dir;

            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new DirectoryNotFoundException("Could not find Gitster.slnx from test output directory.");
    }

    private static string[] ReadResourceKeys(string path)
    {
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        return XDocument.Load(path)
            .Root!
            .Elements()
            .Select(e => (string?)e.Attribute(xaml + "Key"))
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Cast<string>()
            .Order(StringComparer.Ordinal)
            .ToArray();
    }
}
