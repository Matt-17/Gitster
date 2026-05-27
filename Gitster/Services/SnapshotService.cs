using System.IO;
using System.Text.Json;

using Gitster.Models;
using Gitster.Services.Git;

namespace Gitster.Services;

/// <summary>
/// Captures a snapshot of all repo refs whenever HEAD moves.
/// Stored in .git/gitster/snapshots/ — purely a safety net; the browser UI is backlog.
/// </summary>
public sealed class SnapshotService
{
    private string? _storagePath;
    private string? _lastSnapshotJson;

    public Task AttachAsync(string repoPath)
    {
        var gitDir = System.IO.Path.Combine(repoPath, ".git");
        if (File.Exists(gitDir))
        {
            // Worktree / git-submodule: gitdir: <path>
            var content = File.ReadAllText(gitDir).Trim();
            if (content.StartsWith("gitdir:", StringComparison.OrdinalIgnoreCase))
                gitDir = content[7..].Trim();
        }

        var dir = System.IO.Path.Combine(gitDir, "gitster", "snapshots");
        Directory.CreateDirectory(dir);
        _storagePath = dir;
        _lastSnapshotJson = null;

        _ = Task.Run(CleanupOldSnapshots); // fire-and-forget retention policy
        return Task.CompletedTask;
    }

    public void Detach()
    {
        _storagePath = null;
        _lastSnapshotJson = null;
    }

    public async Task<RepositorySnapshot?> CaptureAsync(IGitBackend git, string triggerDescription)
    {
        if (_storagePath == null) return null;

        try
        {
            var refs = await git.GetAllRefsAsync();
            var snapshot = new RepositorySnapshot(
                Id: Guid.NewGuid().ToString("N"),
                CapturedAt: DateTimeOffset.Now,
                TriggerDescription: triggerDescription,
                RefStates: refs);

            // Skip if identical to last snapshot
            var json = JsonSerializer.Serialize(snapshot.RefStates, new JsonSerializerOptions { WriteIndented = false });
            if (json == _lastSnapshotJson) return null;
            _lastSnapshotJson = json;

            var fileName = $"{snapshot.CapturedAt:yyyyMMdd-HHmmss}-{snapshot.Id[..8]}.json";
            var path = System.IO.Path.Combine(_storagePath, fileName);
            await File.WriteAllTextAsync(path,
                JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true }));

            return snapshot;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"SnapshotService.CaptureAsync: {ex.Message}");
            return null;
        }
    }

    private void CleanupOldSnapshots()
    {
        if (_storagePath == null) return;
        try
        {
            var now = DateTimeOffset.UtcNow;
            var sevenDaysAgo = now.AddDays(-7);
            var ninetyDaysAgo = now.AddDays(-90);

            var files = Directory.GetFiles(_storagePath, "*.json")
                .Select(f => new FileInfo(f))
                .OrderByDescending(f => f.CreationTimeUtc)
                .ToList();

            var keptByDay = new HashSet<string>();
            foreach (var fi in files)
            {
                var age = now - new DateTimeOffset(fi.CreationTimeUtc, TimeSpan.Zero);
                if (age.TotalDays <= 7)
                    continue; // keep all recent

                if (age.TotalDays > 90)
                {
                    fi.Delete();
                    continue;
                }

                // 7–90 days: keep at most one per calendar day
                var dayKey = fi.CreationTimeUtc.ToString("yyyyMMdd");
                if (keptByDay.Contains(dayKey))
                    fi.Delete();
                else
                    keptByDay.Add(dayKey);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"SnapshotService.Cleanup: {ex.Message}");
        }
    }
}
