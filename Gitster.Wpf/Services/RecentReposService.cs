using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

using CommunityToolkit.Mvvm.ComponentModel;

namespace Gitster.Services;

public record RecentRepoEntry(string FullPath, DateTime LastOpenedAt)
{
    public bool Pinned     { get; init; }
    public int  PinnedOrder{ get; init; }

    [JsonIgnore]
    public string DisplayName
        => Path.GetFileName(FullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

    [JsonIgnore]
    public string DisplayPath
    {
        get
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return FullPath.StartsWith(home, StringComparison.OrdinalIgnoreCase)
                ? "~" + FullPath[home.Length..].Replace('\\', '/')
                : FullPath;
        }
    }
}

public partial class RecentReposService : ObservableObject
{
    private const int MaxEntries = 10;
    private readonly string _storagePath;

    [ObservableProperty]
    public partial ObservableCollection<RecentRepoEntry> Entries { get; set; } = [];

    public RecentReposService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var dir = Path.Combine(appData, "Gitster");
        Directory.CreateDirectory(dir);
        _storagePath = Path.Combine(dir, "recent-repos.json");
        Load();
    }

    public RecentReposService(string storagePath)
    {
        var dir = Path.GetDirectoryName(storagePath);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);

        _storagePath = storagePath;
        Load();
    }

    public void Record(string path)
    {
        var existing = Entries.FirstOrDefault(e =>
            string.Equals(e.FullPath, path, StringComparison.OrdinalIgnoreCase));
        bool wasPinned = existing?.Pinned ?? false;
        int pinnedOrder = existing?.PinnedOrder ?? 0;

        if (existing != null) Entries.Remove(existing);
        Entries.Insert(0, new RecentRepoEntry(path, DateTime.Now)
        {
            Pinned      = wasPinned,
            PinnedOrder = pinnedOrder,
        });

        // Keep at most MaxEntries non-pinned entries; pinned entries are never trimmed.
        var nonPinnedToRemove = Entries.Where(e => !e.Pinned).Skip(MaxEntries).ToList();
        foreach (var r in nonPinnedToRemove) Entries.Remove(r);

        Save();
    }

    /// <summary>Removes a repo from the recent list entirely (used by the switch-repo dropdown's ✕ marker).</summary>
    public void Remove(string path)
    {
        var existing = Entries.FirstOrDefault(e =>
            string.Equals(e.FullPath, path, StringComparison.OrdinalIgnoreCase));
        if (existing == null) return;

        Entries.Remove(existing);
        Save();
    }

    public void Pin(string path)
    {
        var existing = Entries.FirstOrDefault(e =>
            string.Equals(e.FullPath, path, StringComparison.OrdinalIgnoreCase));
        if (existing == null || existing.Pinned) return;

        int nextOrder = Entries.Where(e => e.Pinned).Select(e => e.PinnedOrder).DefaultIfEmpty(-1).Max() + 1;
        var idx = Entries.IndexOf(existing);
        Entries[idx] = existing with { Pinned = true, PinnedOrder = nextOrder };
        Save();
    }

    public void Unpin(string path)
    {
        var existing = Entries.FirstOrDefault(e =>
            string.Equals(e.FullPath, path, StringComparison.OrdinalIgnoreCase));
        if (existing == null || !existing.Pinned) return;

        var idx = Entries.IndexOf(existing);
        Entries[idx] = existing with { Pinned = false, PinnedOrder = 0 };
        Save();
    }

    public bool IsPinned(string path)
        => Entries.Any(e => string.Equals(e.FullPath, path, StringComparison.OrdinalIgnoreCase) && e.Pinned);

    public IReadOnlyList<RecentRepoEntry> GetPinned()
        => [.. Entries.Where(e => e.Pinned).OrderBy(e => e.PinnedOrder)];

    public IReadOnlyList<RecentRepoEntry> GetRecent()
        => [.. Entries.Where(e => !e.Pinned)];

    private void Load()
    {
        if (!File.Exists(_storagePath)) return;
        try
        {
            var json = File.ReadAllText(_storagePath);
            var list = JsonSerializer.Deserialize<List<RecentRepoEntry>>(json) ?? [];
            Entries = new ObservableCollection<RecentRepoEntry>(list);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"RecentReposService.Load: {ex}");
        }
    }

    private void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(Entries.ToList(),
                new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_storagePath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"RecentReposService.Save: {ex}");
        }
    }
}
