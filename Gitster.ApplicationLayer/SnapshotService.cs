using System.IO;
using System.Text.Json;

using Gitster.Core.Models;
using Gitster.Core.Git;

namespace Gitster.ApplicationLayer;

/// <summary>
/// Captures a snapshot of all repo refs whenever HEAD moves.
/// Stored in .git/gitster/snapshots/ — purely a safety net; the browser UI is backlog.
/// </summary>
public sealed class SnapshotService
{
    private const int DefaultMaxSnapshotFiles = 1000;
    private readonly int _maxSnapshotFiles;
    private string? _storagePath;
    private string? _lastSnapshotJson;

    public SnapshotService()
        : this(DefaultMaxSnapshotFiles)
    {
    }

    public SnapshotService(int maxSnapshotFiles)
    {
        if (maxSnapshotFiles < 1)
            throw new ArgumentOutOfRangeException(nameof(maxSnapshotFiles), "Snapshot retention must keep at least one file.");

        _maxSnapshotFiles = maxSnapshotFiles;
    }

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

        _ = Task.Run(() => CleanupOldSnapshots(dir, _maxSnapshotFiles, DateTimeOffset.UtcNow)); // fire-and-forget retention policy
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

            _ = Task.Run(() => CleanupOldSnapshots(_storagePath, _maxSnapshotFiles, DateTimeOffset.UtcNow));
            return snapshot;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"SnapshotService.CaptureAsync: {ex.Message}");
            return null;
        }
    }

    public IReadOnlyList<RepositorySnapshot> LoadSnapshots()
    {
        if (_storagePath is null || !Directory.Exists(_storagePath))
            return [];

        return Directory.GetFiles(_storagePath, "*.json")
            .Select(TryReadSnapshot)
            .Where(s => s is not null)
            .Cast<RepositorySnapshot>()
            .OrderByDescending(s => s.CapturedAt)
            .ToList();
    }

    public async Task RestoreAllRefsAsync(string repoPath, RepositorySnapshot snapshot)
    {
        foreach (var (refName, sha) in snapshot.RefStates)
        {
            if (!IsRestorableRef(refName))
                continue;

            var result = await GitCli.RunAsync(repoPath, ["update-ref", refName, sha]);
            if (!result.Success)
                throw new InvalidOperationException($"Could not restore {refName}:\n{result.Output}");
        }
    }

    public async Task RestoreBranchAsync(string repoPath, RepositorySnapshot snapshot, string branchName)
    {
        var refName = branchName.StartsWith("refs/", StringComparison.Ordinal)
            ? branchName
            : $"refs/heads/{branchName}";

        if (!snapshot.RefStates.TryGetValue(refName, out var sha))
            throw new InvalidOperationException($"Snapshot does not contain {refName}.");

        var result = await GitCli.RunAsync(repoPath, ["update-ref", refName, sha]);
        if (!result.Success)
            throw new InvalidOperationException($"Could not restore {refName}:\n{result.Output}");
    }

    private static bool IsRestorableRef(string refName) =>
        refName.StartsWith("refs/heads/", StringComparison.Ordinal)
        || refName.StartsWith("refs/tags/", StringComparison.Ordinal);

    private static RepositorySnapshot? TryReadSnapshot(string path)
    {
        try
        {
            return JsonSerializer.Deserialize<RepositorySnapshot>(File.ReadAllText(path));
        }
        catch
        {
            return null;
        }
    }

    internal static int CleanupOldSnapshots(string? storagePath, int maxSnapshotFiles, DateTimeOffset now)
    {
        if (maxSnapshotFiles < 1)
            throw new ArgumentOutOfRangeException(nameof(maxSnapshotFiles), "Snapshot retention must keep at least one file.");

        if (storagePath == null || !Directory.Exists(storagePath))
            return 0;

        try
        {
            var ninetyDaysAgo = now.AddDays(-90);

            var files = Directory.GetFiles(storagePath, "*.json")
                .Select(f => new FileInfo(f))
                .OrderByDescending(f => f.CreationTimeUtc)
                .ThenByDescending(f => f.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var deleted = 0;
            var keptByDay = new HashSet<string>();
            foreach (var fi in files)
            {
                var age = now - new DateTimeOffset(fi.CreationTimeUtc, TimeSpan.Zero);
                if (age.TotalDays <= 7)
                    continue; // keep all recent

                if (age.TotalDays > 90)
                {
                    fi.Delete();
                    deleted++;
                    continue;
                }

                // 7–90 days: keep at most one per calendar day
                var dayKey = fi.CreationTimeUtc.ToString("yyyyMMdd");
                if (keptByDay.Contains(dayKey))
                {
                    fi.Delete();
                    deleted++;
                }
                else
                    keptByDay.Add(dayKey);
            }

            files = Directory.GetFiles(storagePath, "*.json")
                .Select(f => new FileInfo(f))
                .OrderByDescending(f => f.CreationTimeUtc)
                .ThenByDescending(f => f.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var fi in files.Skip(maxSnapshotFiles))
            {
                fi.Delete();
                deleted++;
            }

            return deleted;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"SnapshotService.Cleanup: {ex.Message}");
            return 0;
        }
    }
}
