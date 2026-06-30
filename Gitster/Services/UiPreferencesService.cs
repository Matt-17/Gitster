using System.IO;
using System.Text.Json;

using CommunityToolkit.Mvvm.ComponentModel;

namespace Gitster.Services;

/// <summary>
/// Persisted, observable UI preferences (plan A10 date mode, A13 branch tree, A14 gravatar).
/// Stored in %AppData%/Gitster/ui-settings.json, a sibling of window-settings.json.
/// </summary>
public partial class UiPreferencesService : ObservableObject
{
    private static readonly string Dir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Gitster");
    private static readonly string FilePath = Path.Combine(Dir, "ui-settings.json");

    private readonly string _filePath;
    private readonly Dictionary<string, double> _splitterLengths = new(StringComparer.Ordinal);
    private bool _loaded;

    public UiPreferencesService() : this(FilePath)
    {
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
        public Dictionary<string, double>? SplitterLengths { get; set; }
    }

    [ObservableProperty]
    public partial bool UseRelativeDates { get; set; }

    [ObservableProperty]
    public partial bool GravatarEnabled { get; set; }

    [ObservableProperty]
    public partial bool BranchTreeView { get; set; }

    partial void OnUseRelativeDatesChanged(bool value) => Save();
    partial void OnGravatarEnabledChanged(bool value) => Save();
    partial void OnBranchTreeViewChanged(bool value) => Save();

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
            if (!File.Exists(_filePath)) return;
            var p = JsonSerializer.Deserialize<Prefs>(File.ReadAllText(_filePath));
            if (p == null) return;
            UseRelativeDates = p.UseRelativeDates;
            GravatarEnabled = p.GravatarEnabled;
            BranchTreeView = p.BranchTreeView;

            _splitterLengths.Clear();
            if (p.SplitterLengths is null)
                return;

            foreach (var (key, value) in p.SplitterLengths)
            {
                if (!string.IsNullOrWhiteSpace(key) && IsValidLength(value))
                    _splitterLengths[key] = value;
            }
        }
        catch { /* fall back to defaults */ }
    }

    private void Save()
    {
        if (!_loaded) return; // don't write during initial Load()
        try
        {
            var dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(
                new Prefs
                {
                    UseRelativeDates = UseRelativeDates,
                    GravatarEnabled = GravatarEnabled,
                    BranchTreeView = BranchTreeView,
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
}
