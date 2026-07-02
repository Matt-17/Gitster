using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;

using Gitster.Models;

namespace Gitster.Services;

/// <summary>
/// Persists app-wide settings in %AppData%/Gitster/settings.json. Recent repositories
/// intentionally remain in recent-repos.json because that file has its own lifecycle.
/// </summary>
public sealed class AppSettingsService
{
    private static readonly string SettingsDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Gitster");

    private static readonly string DefaultSettingsPath = Path.Combine(SettingsDir, "settings.json");
    private static readonly string LegacyWindowSettingsPath = Path.Combine(SettingsDir, "window-settings.json");
    private static readonly string LegacyUiSettingsPath = Path.Combine(SettingsDir, "ui-settings.json");

    private readonly string _settingsPath;

    public AppSettingsService() : this(DefaultSettingsPath)
    {
    }

    public AppSettingsService(string settingsPath)
    {
        _settingsPath = settingsPath;
    }

    public sealed record WindowSettings(
        double Left,
        double Top,
        double Width,
        double Height,
        WindowState State);

    public sealed class UiSettings
    {
        public bool UseRelativeDates { get; set; }
        public bool GravatarEnabled { get; set; }
        public bool BranchTreeView { get; set; }
        public bool UpdateChecksEnabled { get; set; }
        public bool PersistentLoggingEnabled { get; set; }
        public ThemePreference ThemePreference { get; set; } = ThemePreference.System;
        public Dictionary<string, double> SplitterLengths { get; set; } = new(StringComparer.Ordinal);
    }

    public WindowSettings? LoadWindowSettings()
    {
        var document = LoadDocument();
        if (document.Window is not null)
            return document.Window;

        return LoadLegacy<WindowSettings>(LegacyWindowSettingsPath);
    }

    public void SaveWindowSettings(WindowSettings settings)
    {
        var document = LoadDocument();
        document.Window = settings;
        SaveDocument(document);
    }

    public UiSettings LoadUiSettings()
    {
        var document = LoadDocument();
        if (document.Ui is not null)
            return document.Ui;

        return LoadLegacy<UiSettings>(LegacyUiSettingsPath) ?? new UiSettings();
    }

    public void SaveUiSettings(UiSettings settings)
    {
        var document = LoadDocument();
        document.Ui = settings;
        SaveDocument(document);
    }

    public string? LoadRepositoryPath()
    {
        var document = LoadDocument();
        if (!string.IsNullOrWhiteSpace(document.RepositoryPath))
            return document.RepositoryPath;

        var legacy = Properties.Settings.Default.Path;
        return string.IsNullOrWhiteSpace(legacy) ? null : legacy;
    }

    public void SaveRepositoryPath(string path)
    {
        var document = LoadDocument();
        document.RepositoryPath = path;
        SaveDocument(document);
    }

    private SettingsDocument LoadDocument()
    {
        try
        {
            if (!File.Exists(_settingsPath))
                return new SettingsDocument();

            var json = File.ReadAllText(_settingsPath);
            return JsonSerializer.Deserialize(
                json,
                GitsterSettingsJsonContext.Default.SettingsDocument) ?? new SettingsDocument();
        }
        catch
        {
            return new SettingsDocument();
        }
    }

    private void SaveDocument(SettingsDocument document)
    {
        try
        {
            var dir = Path.GetDirectoryName(_settingsPath);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(
                document,
                GitsterSettingsJsonContext.Default.SettingsDocument);
            File.WriteAllText(_settingsPath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"AppSettingsService.Save: {ex.Message}");
        }
    }

    private static T? LoadLegacy<T>(string path)
    {
        try
        {
            if (!File.Exists(path))
                return default;

            return JsonSerializer.Deserialize<T>(File.ReadAllText(path));
        }
        catch
        {
            return default;
        }
    }

    public sealed class SettingsDocument
    {
        public int Version { get; set; } = 1;
        public string? RepositoryPath { get; set; }
        public WindowSettings? Window { get; set; }
        public UiSettings? Ui { get; set; }
    }
}

[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(AppSettingsService.SettingsDocument))]
internal sealed partial class GitsterSettingsJsonContext : JsonSerializerContext;
