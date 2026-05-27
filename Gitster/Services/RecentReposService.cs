using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;

using CommunityToolkit.Mvvm.ComponentModel;

namespace Gitster.Services;

public record RecentRepoEntry(string FullPath, DateTime LastOpenedAt)
{
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

    public void Record(string path)
    {
        var existing = Entries.FirstOrDefault(e =>
            string.Equals(e.FullPath, path, StringComparison.OrdinalIgnoreCase));
        if (existing != null) Entries.Remove(existing);
        Entries.Insert(0, new RecentRepoEntry(path, DateTime.Now));
        while (Entries.Count > MaxEntries) Entries.RemoveAt(Entries.Count - 1);
        Save();
    }

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
