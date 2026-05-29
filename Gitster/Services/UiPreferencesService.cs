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

    private bool _loaded;

    public UiPreferencesService()
    {
        Load();
        _loaded = true;
    }

    private sealed record Prefs(
        bool UseRelativeDates = false,
        bool GravatarEnabled = false,
        bool BranchTreeView = false);

    [ObservableProperty]
    public partial bool UseRelativeDates { get; set; }

    [ObservableProperty]
    public partial bool GravatarEnabled { get; set; }

    [ObservableProperty]
    public partial bool BranchTreeView { get; set; }

    partial void OnUseRelativeDatesChanged(bool value) => Save();
    partial void OnGravatarEnabledChanged(bool value) => Save();
    partial void OnBranchTreeViewChanged(bool value) => Save();

    private void Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return;
            var p = JsonSerializer.Deserialize<Prefs>(File.ReadAllText(FilePath));
            if (p == null) return;
            UseRelativeDates = p.UseRelativeDates;
            GravatarEnabled = p.GravatarEnabled;
            BranchTreeView = p.BranchTreeView;
        }
        catch { /* fall back to defaults */ }
    }

    private void Save()
    {
        if (!_loaded) return; // don't write during initial Load()
        try
        {
            Directory.CreateDirectory(Dir);
            var json = JsonSerializer.Serialize(
                new Prefs(UseRelativeDates, GravatarEnabled, BranchTreeView),
                new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(FilePath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"UiPreferencesService.Save: {ex.Message}");
        }
    }
}
