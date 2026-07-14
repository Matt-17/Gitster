using System.IO;
using System.Text.Json;

namespace Gitster.Core;

/// <summary>
/// Persists user-assigned stash display names in .git/gitster/stash-names.json,
/// keyed by the stash commit SHA (which is stable until the stash is dropped).
/// The auto-name from StashNamer is used as the fallback when no user name is set.
/// </summary>
public sealed class StashNameService
{
    private string? _filePath;
    private Dictionary<string, string> _names = new(StringComparer.OrdinalIgnoreCase);

    public async Task AttachAsync(string repoPath)
    {
        var gitDir = ResolveGitDir(repoPath);
        var dir    = Path.Combine(gitDir, "gitster");
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, "stash-names.json");
        await LoadAsync();
    }

    public void Detach()
    {
        _filePath = null;
        _names.Clear();
    }

    public string? GetName(string commitSha)
        => _names.TryGetValue(commitSha, out var name) ? name : null;

    public async Task SetNameAsync(string commitSha, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            _names.Remove(commitSha);
        else
            _names[commitSha] = name.Trim();
        await SaveAsync();
    }

    public async Task RemoveAsync(string commitSha)
    {
        if (_names.Remove(commitSha))
            await SaveAsync();
    }

    // ── Private ────────────────────────────────────────────────────────────

    private async Task LoadAsync()
    {
        _names.Clear();
        if (_filePath == null || !File.Exists(_filePath)) return;
        try
        {
            var json = await File.ReadAllTextAsync(_filePath);
            var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            if (dict != null)
                _names = new Dictionary<string, string>(dict, StringComparer.OrdinalIgnoreCase);
        }
        catch { /* corrupt file — start fresh */ }
    }

    private async Task SaveAsync()
    {
        if (_filePath == null) return;
        try
        {
            var json = JsonSerializer.Serialize(_names,
                new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(_filePath, json);
        }
        catch { /* best-effort; names are cosmetic */ }
    }

    private static string ResolveGitDir(string repoPath)
    {
        var candidate = Path.Combine(repoPath, ".git");
        if (File.Exists(candidate))
        {
            var content = File.ReadAllText(candidate).Trim();
            if (content.StartsWith("gitdir:", StringComparison.OrdinalIgnoreCase))
                return content[7..].Trim();
        }
        return candidate;
    }
}
