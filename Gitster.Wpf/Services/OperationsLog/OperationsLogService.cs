using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

using CommunityToolkit.Mvvm.ComponentModel;

using Gitster.Models;
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

    public async Task DetachAsync()
    {
        await RunOnUiThreadAsync(() =>
        {
            Records.Clear();
            _storagePath = null;
            OnPropertyChanged(nameof(MostRecentActive));
            Changed?.Invoke(this, EventArgs.Empty);
        });
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
        await RunOnUiThreadAsync(() =>
        {
            // Mark the immediate predecessor on the same branch as Replaced.
            // Prefix-tolerant: records persisted by older versions stored short SHAs.
            var previousActive = Records.FirstOrDefault(r =>
                r.Status == OperationStatus.Active
                && r.BranchName == record.BranchName
                && GitSha.Matches(r.AfterSha, record.BeforeSha));

            if (previousActive != null)
            {
                var idx = Records.IndexOf(previousActive);
                Records[idx] = previousActive with { Status = OperationStatus.Replaced };
            }

            Records.Insert(0, record);
            OnPropertyChanged(nameof(MostRecentActive));
            Changed?.Invoke(this, EventArgs.Empty);
        });
        await SaveAsync();
    }

    public async Task MarkUndoneAsync(string recordId)
    {
        var changed = await RunOnUiThreadAsync(() =>
        {
            var record = Records.FirstOrDefault(r => r.Id == recordId);
            if (record is null) return false;
            var idx = Records.IndexOf(record);
            Records[idx] = record with { Status = OperationStatus.Undone };
            OnPropertyChanged(nameof(MostRecentActive));
            Changed?.Invoke(this, EventArgs.Empty);
            return true;
        });
        if (changed)
            await SaveAsync();
    }

    public async Task MarkExpiredAsync(string recordId)
    {
        var changed = await RunOnUiThreadAsync(() =>
        {
            var record = Records.FirstOrDefault(r => r.Id == recordId);
            if (record is null) return false;
            var idx = Records.IndexOf(record);
            Records[idx] = record with { Status = OperationStatus.Expired };
            return true;
        });
        if (changed)
            await SaveAsync();
    }

    /// <summary>
    /// Records is bound to the UI; every mutation must happen on the dispatcher.
    /// Falls back to inline execution in unit tests (no Application).
    /// </summary>
    private static Task RunOnUiThreadAsync(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
            return Task.CompletedTask;
        }

        return dispatcher.InvokeAsync(action).Task;
    }

    private static async Task<T> RunOnUiThreadAsync<T>(Func<T> func)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
            return func();

        return await dispatcher.InvokeAsync(func).Task;
    }

    public async Task<UndoPlan> PrepareUndoAsync(
        OperationRecord record,
        IGitBackend git,
        IProgress<OperationProgress>? progress = null)
    {
        // A.5: use the stored pre-operation SHA directly — stable across reflog changes
        var targetSha = record.BeforeSha;

        progress?.Report(new OperationProgress(
            "Preparing undo",
            "Checking that the pre-operation commit still exists.",
            8));
        var exists = await Task.Run(() => git.CommitExistsAsync(targetSha));
        if (!exists)
        {
            await MarkExpiredAsync(record.Id);
            return new UndoPlan.Expired("Pre-operation commit no longer available (garbage collected).");
        }

        progress?.Report(new OperationProgress(
            "Preparing undo",
            "Reading current HEAD.",
            18));
        var currentHead = await Task.Run(() => git.GetHeadShaAsync());

        progress?.Report(new OperationProgress(
            "Preparing undo",
            "Checking commits that would be affected.",
            28));
        var commitsBetween = await Task.Run(() => git.GetCommitsBetweenAsync(targetSha, currentHead));
        // Prefix-tolerant: records from older Gitster versions stored 7-char SHAs.
        var wouldBeDiscarded = commitsBetween.Where(c => !GitSha.Matches(c.Sha, record.AfterSha)).ToList();

        progress?.Report(new OperationProgress(
            "Preparing undo",
            "Undo plan is ready.",
            35));

        return new UndoPlan.Ready(record, targetSha, wouldBeDiscarded);
    }

    public async Task ExecuteUndoAsync(
        UndoPlan.Ready plan,
        IGitBackend git,
        IProgress<OperationProgress>? progress = null)
    {
        progress?.Report(new OperationProgress(
            "Undoing operation",
            "Resetting HEAD to the saved pre-operation commit.",
            45));
        await Task.Run(() => git.ResetHardAsync(plan.TargetSha));

        progress?.Report(new OperationProgress(
            "Undoing operation",
            "Updating the operations log.",
            75));
        await MarkUndoneAsync(plan.Record.Id);

        progress?.Report(new OperationProgress(
            "Undoing operation",
            "Undo is complete.",
            85));
    }

    public async Task ExecuteUndoWithReplayAsync(
        UndoPlan.Ready plan,
        IGitBackend git,
        IProgress<OperationProgress>? progress = null)
    {
        progress?.Report(new OperationProgress(
            "Undoing operation",
            "Resetting HEAD to the saved pre-operation commit.",
            40));
        await Task.Run(() => git.ResetHardAsync(plan.TargetSha));
        // Replay discarded commits oldest-first on top of the restored HEAD
        var replay = plan.WouldDiscard.Reverse().ToList();
        for (var i = 0; i < replay.Count; i++)
        {
            var commit = replay[i];
            try
            {
                var value = 48 + (i / Math.Max(1d, replay.Count)) * 24;
                progress?.Report(new OperationProgress(
                    "Replaying commits",
                    $"Cherry-picking {commit.Sha}.",
                    value));
                await Task.Run(() => git.CherryPickAsync(commit.Sha));
            }
            catch
            {
                // Roll back to the pre-undo state so the repo is not left in an inconsistent state
                progress?.Report(new OperationProgress(
                    "Undo failed",
                    "Replay conflicted. Restoring the repository state before the undo attempt.",
                    75));
                await Task.Run(() => git.ResetHardAsync(plan.Record.AfterSha));
                throw new GitConflictException(
                    "Replay produced conflicts. Repository restored to state before undo attempt.",
                    repositoryHalted: false);
            }
        }

        progress?.Report(new OperationProgress(
            "Undoing operation",
            "Updating the operations log.",
            75));
        await MarkUndoneAsync(plan.Record.Id);

        progress?.Report(new OperationProgress(
            "Undoing operation",
            "Undo is complete.",
            85));
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
            await RunOnUiThreadAsync(() => Records = new ObservableCollection<OperationRecord>(list));
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

    /// <summary>The user declined the undo confirmation — no message needed.</summary>
    public sealed record Canceled : UndoPlan;
}
