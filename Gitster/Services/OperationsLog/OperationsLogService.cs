using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

using CommunityToolkit.Mvvm.ComponentModel;

using Gitster.Services.Git;

namespace Gitster.Services.OperationsLog;

public partial class OperationsLogService : ObservableObject
{
    private string? _storagePath;

    [ObservableProperty]
    public partial ObservableCollection<OperationRecord> Records { get; set; } = [];

    public event EventHandler? Changed;

    public OperationRecord? MostRecentActive
        => Records.FirstOrDefault(r => r.Status == OperationStatus.Active);

    public Task DetachAsync()
    {
        Records.Clear();
        _storagePath = null;
        OnPropertyChanged(nameof(MostRecentActive));
        Changed?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    public async Task AttachAsync(string repoPath)
    {
        await DetachAsync();
        _storagePath = ResolveStoragePath(repoPath);
        await LoadAsync();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task RecordAsync(OperationRecord record)
    {
        // Mark the immediate predecessor on the same branch as Replaced
        var previousActive = Records.FirstOrDefault(r =>
            r.Status == OperationStatus.Active
            && r.BranchName == record.BranchName
            && r.AfterSha == record.BeforeSha);

        if (previousActive != null)
        {
            var idx = Records.IndexOf(previousActive);
            Records[idx] = previousActive with { Status = OperationStatus.Replaced };
        }

        Records.Insert(0, record);
        await SaveAsync();
        OnPropertyChanged(nameof(MostRecentActive));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task MarkUndoneAsync(string recordId)
    {
        var record = Records.FirstOrDefault(r => r.Id == recordId);
        if (record is null) return;
        var idx = Records.IndexOf(record);
        Records[idx] = record with { Status = OperationStatus.Undone };
        await SaveAsync();
        OnPropertyChanged(nameof(MostRecentActive));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task MarkExpiredAsync(string recordId)
    {
        var record = Records.FirstOrDefault(r => r.Id == recordId);
        if (record is null) return;
        var idx = Records.IndexOf(record);
        Records[idx] = record with { Status = OperationStatus.Expired };
        await SaveAsync();
    }

    public async Task<UndoPlan> PrepareUndoAsync(OperationRecord record, IGitBackend git)
    {
        // A.5: use the stored pre-operation SHA directly — stable across reflog changes
        var targetSha = record.BeforeSha;

        var exists = await git.CommitExistsAsync(targetSha);
        if (!exists)
        {
            await MarkExpiredAsync(record.Id);
            return new UndoPlan.Expired("Pre-operation commit no longer available (garbage collected).");
        }

        var currentHead = await git.GetHeadShaAsync();
        var commitsBetween = await git.GetCommitsBetweenAsync(targetSha, currentHead);
        var wouldBeDiscarded = commitsBetween.Where(c => c.Sha != record.AfterSha).ToList();

        return new UndoPlan.Ready(record, targetSha, wouldBeDiscarded);
    }

    public async Task ExecuteUndoAsync(UndoPlan.Ready plan, IGitBackend git)
    {
        await git.ResetHardAsync(plan.TargetSha);
        await MarkUndoneAsync(plan.Record.Id);
    }

    public async Task ExecuteUndoWithReplayAsync(UndoPlan.Ready plan, IGitBackend git)
    {
        await git.ResetHardAsync(plan.TargetSha);
        // Replay discarded commits oldest-first on top of the restored HEAD
        foreach (var commit in plan.WouldDiscard.Reverse())
        {
            try
            {
                await git.CherryPickAsync(commit.Sha);
            }
            catch
            {
                // Roll back to the pre-undo state so the repo is not left in an inconsistent state
                await git.ResetHardAsync(plan.Record.AfterSha);
                throw new InvalidOperationException(
                    "Replay produced conflicts. Repository restored to state before undo attempt.");
            }
        }
        await MarkUndoneAsync(plan.Record.Id);
    }

    private static string ResolveStoragePath(string repoPath)
    {
        var gitDir = Path.Combine(repoPath, ".git");
        if (Directory.Exists(gitDir))
        {
            var dir = Path.Combine(gitDir, "gitster");
            try
            {
                Directory.CreateDirectory(dir);
                return Path.Combine(dir, "operations.json");
            }
            catch { /* fall through */ }
        }

        var fallbackDir = Path.Combine(repoPath, ".gitster");
        Directory.CreateDirectory(fallbackDir);
        EnsureGitIgnore(repoPath, ".gitster/");
        return Path.Combine(fallbackDir, "operations.json");
    }

    private static void EnsureGitIgnore(string repoPath, string entry)
    {
        var path = Path.Combine(repoPath, ".gitignore");
        var lines = File.Exists(path) ? File.ReadAllLines(path).ToList() : [];
        if (!lines.Contains(entry))
        {
            lines.Add(entry);
            File.WriteAllLines(path, lines);
        }
    }

    private async Task LoadAsync()
    {
        if (_storagePath is null || !File.Exists(_storagePath)) return;
        try
        {
            var json = await File.ReadAllTextAsync(_storagePath);
            var options = new JsonSerializerOptions
            {
                Converters = { new JsonStringEnumConverter() }
            };
            var list = JsonSerializer.Deserialize<List<OperationRecord>>(json, options) ?? [];
            Records = new ObservableCollection<OperationRecord>(list);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"OperationsLog.Load: {ex}");
        }
    }

    private async Task SaveAsync()
    {
        if (_storagePath is null) return;
        try
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Converters = { new JsonStringEnumConverter() }
            };
            var json = JsonSerializer.Serialize(Records.ToList(), options);
            await File.WriteAllTextAsync(_storagePath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"OperationsLog.Save: {ex}");
        }
    }
}

public abstract record UndoPlan
{
    public sealed record Ready(
        OperationRecord Record,
        string TargetSha,
        IReadOnlyList<CommitInfo> WouldDiscard) : UndoPlan;

    public sealed record NotAvailable(string Reason) : UndoPlan;
    public sealed record Expired(string Reason) : UndoPlan;
}
