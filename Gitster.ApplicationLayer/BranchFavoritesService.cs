using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;

using CommunityToolkit.Mvvm.ComponentModel;

namespace Gitster.ApplicationLayer;

/// <summary>
/// Persists branch favourites for the title-bar branch picker:
///  • <b>Global favourites</b> — branch names favourited on every repository (managed in File → Options).
///    These show a locked green pin in the dropdown and cannot be unpinned there.
///  • <b>Per-repo pins</b> — branches pinned only within a single repository (blue, removable).
///  • <b>Folder expansion</b> — remembers which "/"-path folders are collapsed, per repository.
/// </summary>
public partial class BranchFavoritesService : ObservableObject
{
    private readonly string _storagePath;
    private Data _data = new();

    /// <summary>Raised when favourites or pins change so open views can rebuild.</summary>
    public event Action? Changed;

    /// <summary>Global favourite branch names (bound directly by the Options dialog list).</summary>
    public ObservableCollection<string> GlobalFavorites { get; } = [];

    public BranchFavoritesService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var dir = Path.Combine(appData, "Gitster");
        Directory.CreateDirectory(dir);
        _storagePath = Path.Combine(dir, "branch-favorites.json");
        Load();
    }

    public BranchFavoritesService(string storagePath)
    {
        var dir = Path.GetDirectoryName(storagePath);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);
        _storagePath = storagePath;
        Load();
    }

    // ── Global favourites ────────────────────────────────────────────────
    public bool IsGlobalFavorite(string name)
        => _data.GlobalFavorites.Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));

    public void AddGlobalFavorite(string name)
    {
        name = name.Trim();
        if (string.IsNullOrEmpty(name) || IsGlobalFavorite(name)) return;

        _data.GlobalFavorites.Add(name);
        _data.GlobalFavorites.Sort(StringComparer.OrdinalIgnoreCase);
        SyncGlobalFavorites();
        Save();
        Changed?.Invoke();
    }

    public void RemoveGlobalFavorite(string name)
    {
        var match = _data.GlobalFavorites.FirstOrDefault(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
        if (match is null) return;

        _data.GlobalFavorites.Remove(match);
        SyncGlobalFavorites();
        Save();
        Changed?.Invoke();
    }

    // ── Per-repo pins ────────────────────────────────────────────────────
    public bool IsPinned(string? repoPath, string name)
        => _data.Pins.TryGetValue(Key(repoPath), out var list)
           && list.Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));

    public void TogglePin(string? repoPath, string name)
    {
        var key = Key(repoPath);
        if (string.IsNullOrEmpty(key)) return;

        if (!_data.Pins.TryGetValue(key, out var list))
            _data.Pins[key] = list = [];

        var match = list.FirstOrDefault(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
        if (match is null)
            list.Add(name);
        else
            list.Remove(match);

        if (list.Count == 0) _data.Pins.Remove(key);
        Save();
        Changed?.Invoke();
    }

    // ── Folder expansion ─────────────────────────────────────────────────
    public bool IsFolderExpanded(string? repoPath, string folderPath)
        => !(_data.CollapsedFolders.TryGetValue(Key(repoPath), out var list)
             && list.Contains(folderPath, StringComparer.Ordinal));

    public void SetFolderExpanded(string? repoPath, string folderPath, bool expanded)
    {
        var key = Key(repoPath);
        if (string.IsNullOrEmpty(key)) return;

        _data.CollapsedFolders.TryGetValue(key, out var list);
        var collapsed = !expanded;

        if (collapsed)
        {
            if (list is null) _data.CollapsedFolders[key] = list = [];
            if (!list.Contains(folderPath, StringComparer.Ordinal)) list.Add(folderPath);
        }
        else
        {
            list?.RemoveAll(f => string.Equals(f, folderPath, StringComparison.Ordinal));
            if (list is { Count: 0 }) _data.CollapsedFolders.Remove(key);
        }
        Save();
    }

    private static string Key(string? repoPath)
        => string.IsNullOrWhiteSpace(repoPath)
            ? string.Empty
            : repoPath.TrimEnd('\\', '/').ToLowerInvariant();

    private void SyncGlobalFavorites()
    {
        GlobalFavorites.Clear();
        foreach (var n in _data.GlobalFavorites)
            GlobalFavorites.Add(n);
    }

    private void Load()
    {
        try
        {
            if (File.Exists(_storagePath))
                _data = JsonSerializer.Deserialize<Data>(File.ReadAllText(_storagePath)) ?? new Data();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"BranchFavoritesService.Load: {ex}");
            _data = new Data();
        }
        _data.GlobalFavorites.Sort(StringComparer.OrdinalIgnoreCase);
        SyncGlobalFavorites();
    }

    private void Save()
    {
        try
        {
            File.WriteAllText(_storagePath,
                JsonSerializer.Serialize(_data, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"BranchFavoritesService.Save: {ex}");
        }
    }

    private sealed class Data
    {
        public List<string> GlobalFavorites { get; set; } = [];
        public Dictionary<string, List<string>> Pins { get; set; } = new(StringComparer.Ordinal);
        public Dictionary<string, List<string>> CollapsedFolders { get; set; } = new(StringComparer.Ordinal);
    }
}
