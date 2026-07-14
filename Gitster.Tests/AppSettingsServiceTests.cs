using System.IO;
using System.Text.Json;
using System.Windows;

using Gitster.Core.Models;
using Gitster.Services;
using Gitster.Core;

namespace Gitster.Tests;

[TestClass]
public sealed class AppSettingsServiceTests
{
    [TestMethod]
    public void SaveWindowSettings_WritesConsolidatedSettingsJson()
    {
        var path = SettingsPath();
        var service = new AppSettingsService(path);

        service.SaveWindowSettings(new AppSettingsService.WindowSettings(1, 2, 800, 600, WindowStateKind.Normal));

        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        Assert.AreEqual(1, doc.RootElement.GetProperty("Version").GetInt32());
        Assert.AreEqual(800, doc.RootElement.GetProperty("Window").GetProperty("Width").GetDouble());
    }

    [TestMethod]
    public void UiPreferencesService_WithAppSettingsService_SavesUiSection()
    {
        var path = SettingsPath();
        var service = new UiPreferencesService(new AppSettingsService(path))
        {
            ThemePreference = ThemePreference.Dark,
            UpdateChecksEnabled = true,
            PersistentLoggingEnabled = true,
        };
        service.SetSplitterLength("test", 123);

        var reloaded = new UiPreferencesService(new AppSettingsService(path));

        Assert.AreEqual(ThemePreference.Dark, reloaded.ThemePreference);
        Assert.IsTrue(reloaded.UpdateChecksEnabled);
        Assert.IsTrue(reloaded.PersistentLoggingEnabled);
        Assert.AreEqual(123, reloaded.GetSplitterLength("test")!.Value, 0.001);
    }

    [TestMethod]
    public void UiPreferencesService_WithAppSettingsService_PersistsCommitRefPaneCollapsed()
    {
        var path = SettingsPath();
        _ = new UiPreferencesService(new AppSettingsService(path))
        {
            CommitRefPaneCollapsed = true,
        };

        var reloaded = new UiPreferencesService(new AppSettingsService(path));

        Assert.IsTrue(reloaded.CommitRefPaneCollapsed);
    }

    [TestMethod]
    public void UiPreferencesService_WithAppSettingsService_DefaultsPersistentLoggingDisabled()
    {
        var path = SettingsPath();
        var service = new UiPreferencesService(new AppSettingsService(path));

        Assert.IsFalse(service.PersistentLoggingEnabled);
    }

    private static string SettingsPath()
    {
        var dir = Path.Combine(Path.GetTempPath(), "Gitster.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "settings.json");
    }
}
