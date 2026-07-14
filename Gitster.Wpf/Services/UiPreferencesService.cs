using System.IO;
using System.Text.Json;

using CommunityToolkit.Mvvm.ComponentModel;

using Gitster.Core.Models;

namespace Gitster.Services;

/// <summary>
/// Persisted, observable UI preferences (plan A10 date mode, A13 branch tree, A14 gravatar).
/// Defaults to the consolidated settings.json store; the file-path constructor is retained for tests.
/// </summary>
public partial class UiPreferencesService : ObservableObject
{
    /// <summary>Default branch-tree font: the proportional UI face, not a monospace one.</summary>
    public const string DefaultBranchFontFamily = "Segoe UI Variable Text, Segoe UI";

    private readonly AppSettingsService? _settingsService;
    private readonly string _filePath;
    private readonly Dictionary<string, double> _splitterLengths = new(StringComparer.Ordinal);
    private bool _loaded;

    public UiPreferencesService() : this(new AppSettingsService())
    {
    }

    public UiPreferencesService(AppSettingsService settingsService)
    {
        _settingsService = settingsService;
        _filePath = string.Empty;
        Load();
        _loaded = true;
    }

    public UiPreferencesService(string filePath)
    {
        _filePath = filePath;
        Load();
        _loaded = true;
    }

    private sealed class Prefs
    {
        public bool UseRelativeDates { get; set; }
        public bool GravatarEnabled { get; set; }
        public bool BranchTreeView { get; set; }
        public bool CommitRefPaneCollapsed { get; set; }
        public bool UpdateChecksEnabled { get; set; }
        public bool PersistentLoggingEnabled { get; set; }
        public ThemePreference ThemePreference { get; set; } = ThemePreference.System;
        public string BranchFontFamily { get; set; } = DefaultBranchFontFamily;
        public Dictionary<string, double>? SplitterLengths { get; set; }
    }

    [ObservableProperty]
    public partial bool UseRelativeDates { get; set; }

    [ObservableProperty]
    public partial bool GravatarEnabled { get; set; }

    [ObservableProperty]
    public partial bool BranchTreeView { get; set; }

    [ObservableProperty]
    public partial bool CommitRefPaneCollapsed { get; set; }

    [ObservableProperty]
    public partial bool UpdateChecksEnabled { get; set; }

    [ObservableProperty]
    public partial bool PersistentLoggingEnabled { get; set; }

    [ObservableProperty]
    public partial ThemePreference ThemePreference { get; set; } = ThemePreference.System;

    [ObservableProperty]
    public partial string BranchFontFamily { get; set; } = DefaultBranchFontFamily;

    public bool IsLightTheme => ThemePreference == ThemePreference.Light;
    public bool IsDarkTheme => ThemePreference == ThemePreference.Dark;
    public bool IsSystemTheme => ThemePreference == ThemePreference.System;

    partial void OnUseRelativeDatesChanged(bool value) => Save();
    partial void OnGravatarEnabledChanged(bool value) => Save();
    partial void OnBranchTreeViewChanged(bool value) => Save();
    partial void OnCommitRefPaneCollapsedChanged(bool value) => Save();
    partial void OnUpdateChecksEnabledChanged(bool value) => Save();
    partial void OnPersistentLoggingEnabledChanged(bool value) => Save();
    partial void OnBranchFontFamilyChanged(string value) => Save();
    partial void OnThemePreferenceChanged(ThemePreference value)
    {
        OnPropertyChanged(nameof(IsLightTheme));
        OnPropertyChanged(nameof(IsDarkTheme));
        OnPropertyChanged(nameof(IsSystemTheme));
        Save();
    }

    public double? GetSplitterLength(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return null;

        return _splitterLengths.TryGetValue(key, out var value) && IsValidLength(value)
            ? value
            : null;
    }

    public void SetSplitterLength(string? key, double value)
    {
        if (string.IsNullOrWhiteSpace(key) || !IsValidLength(value))
            return;

        if (_splitterLengths.TryGetValue(key, out var existing)
            && Math.Abs(existing - value) < 0.5)
            return;

        _splitterLengths[key] = value;
        Save();
    }

    private void Load()
    {
        try
        {
            if (_settingsService is not null)
            {
                ApplySettings(_settingsService.LoadUiSettings());
                return;
            }

            if (!File.Exists(_filePath)) return;
            var p = JsonSerializer.Deserialize<Prefs>(File.ReadAllText(_filePath));
            if (p == null) return;
            ApplyPrefs(p);
        }
        catch { /* fall back to defaults */ }
    }

    private void Save()
    {
        if (!_loaded) return; // don't write during initial Load()
        try
        {
            if (_settingsService is not null)
            {
                _settingsService.SaveUiSettings(new AppSettingsService.UiSettings
                {
                    UseRelativeDates = UseRelativeDates,
                    GravatarEnabled = GravatarEnabled,
                    BranchTreeView = BranchTreeView,
                    CommitRefPaneCollapsed = CommitRefPaneCollapsed,
                    UpdateChecksEnabled = UpdateChecksEnabled,
                    PersistentLoggingEnabled = PersistentLoggingEnabled,
                    ThemePreference = ThemePreference,
                    BranchFontFamily = BranchFontFamily,
                    SplitterLengths = new Dictionary<string, double>(_splitterLengths),
                });
                return;
            }

            var dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(
                new Prefs
                {
                    UseRelativeDates = UseRelativeDates,
                    GravatarEnabled = GravatarEnabled,
                    BranchTreeView = BranchTreeView,
                    CommitRefPaneCollapsed = CommitRefPaneCollapsed,
                    UpdateChecksEnabled = UpdateChecksEnabled,
                    PersistentLoggingEnabled = PersistentLoggingEnabled,
                    ThemePreference = ThemePreference,
                    BranchFontFamily = BranchFontFamily,
                    SplitterLengths = new Dictionary<string, double>(_splitterLengths),
                },
                new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_filePath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"UiPreferencesService.Save: {ex.Message}");
        }
    }

    private static bool IsValidLength(double value) =>
        !double.IsNaN(value) && !double.IsInfinity(value) && value > 0;

    private void ApplySettings(AppSettingsService.UiSettings settings)
    {
        UseRelativeDates = settings.UseRelativeDates;
        GravatarEnabled = settings.GravatarEnabled;
        BranchTreeView = settings.BranchTreeView;
        CommitRefPaneCollapsed = settings.CommitRefPaneCollapsed;
        UpdateChecksEnabled = settings.UpdateChecksEnabled;
        PersistentLoggingEnabled = settings.PersistentLoggingEnabled;
        ThemePreference = settings.ThemePreference;
        BranchFontFamily = string.IsNullOrWhiteSpace(settings.BranchFontFamily)
            ? DefaultBranchFontFamily
            : settings.BranchFontFamily;
        ReplaceSplitterLengths(settings.SplitterLengths);
    }

    private void ApplyPrefs(Prefs p)
    {
        UseRelativeDates = p.UseRelativeDates;
        GravatarEnabled = p.GravatarEnabled;
        BranchTreeView = p.BranchTreeView;
        CommitRefPaneCollapsed = p.CommitRefPaneCollapsed;
        UpdateChecksEnabled = p.UpdateChecksEnabled;
        PersistentLoggingEnabled = p.PersistentLoggingEnabled;
        ThemePreference = p.ThemePreference;
        BranchFontFamily = string.IsNullOrWhiteSpace(p.BranchFontFamily)
            ? DefaultBranchFontFamily
            : p.BranchFontFamily;
        ReplaceSplitterLengths(p.SplitterLengths);
    }

    private void ReplaceSplitterLengths(IReadOnlyDictionary<string, double>? values)
    {
        _splitterLengths.Clear();
        if (values is null)
            return;

        foreach (var (key, value) in values)
        {
            if (!string.IsNullOrWhiteSpace(key) && IsValidLength(value))
                _splitterLengths[key] = value;
        }
    }
}
