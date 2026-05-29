using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using System.Windows;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Gitster.Services;
using Gitster.Services.Git;
using Gitster.Services.OperationsLog;

namespace Gitster.ViewModels;

/// <summary>One file row in the commit panel; toggling <see cref="IsStaged"/> stages/unstages it.</summary>
public partial class CommitFileViewModel : ObservableObject
{
    private readonly Func<CommitFileViewModel, bool, Task> _onToggle;
    private bool _suppress;

    public CommitFileViewModel(WorkingTreeFile file, Func<CommitFileViewModel, bool, Task> onToggle)
    {
        _onToggle = onToggle;
        Path = file.Path;
        Badge = file.Badge;
        Added = file.Added;
        Deleted = file.Deleted;
        _suppress = true;
        IsStaged = file.Staged;
        _suppress = false;
    }

    public string Path { get; }
    public string Badge { get; }
    public int Added { get; }
    public int Deleted { get; }

    [ObservableProperty]
    public partial bool IsStaged { get; set; }

    partial void OnIsStagedChanged(bool value)
    {
        if (_suppress) return;
        _ = _onToggle(this, value);
    }
}

/// <summary>
/// The Visual-Studio-style commit panel (plan A2): write a message, stage/unstage files,
/// then Commit / Commit&amp;Push / Commit&amp;Sync, or stash. The single most important
/// pre-release feature — without it Gitster can only edit existing commits.
/// </summary>
public partial class CommitPanelViewModel : BaseViewModel
{
    private readonly IGitBackend _git;
    private readonly OperationFeedbackService _feedback;
    private readonly OperationsLogService _opsLog;
    private readonly SnapshotService _snapshots;
    private readonly AuthorDirectoryService _authorDir;
    private readonly Func<Task> _onChanged;
    private readonly Func<string> _getBranch;
    private readonly Func<string?> _getRemote;

    private string? _lastCommitMessage;
    private string? _authorText;
    private string? _committerText;
    private bool _busy;

    public CommitPanelViewModel(
        IGitBackend git,
        OperationFeedbackService feedback,
        OperationsLogService opsLog,
        SnapshotService snapshots,
        AuthorDirectoryService authorDir,
        Func<Task> onChanged,
        Func<string> getBranch,
        Func<string?> getRemote)
    {
        _git = git;
        _feedback = feedback;
        _opsLog = opsLog;
        _snapshots = snapshots;
        _authorDir = authorDir;
        _onChanged = onChanged;
        _getBranch = getBranch;
        _getRemote = getRemote;
    }

    public ObservableCollection<CommitFileViewModel> Staged { get; } = [];
    public ObservableCollection<CommitFileViewModel> Changes { get; } = [];

    [ObservableProperty]
    public partial bool IsOpen { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CommitCommand))]
    [NotifyCanExecuteChangedFor(nameof(CommitAndPushCommand))]
    [NotifyCanExecuteChangedFor(nameof(CommitAndSyncCommand))]
    public partial string Message { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CommitCommand))]
    [NotifyCanExecuteChangedFor(nameof(CommitAndPushCommand))]
    [NotifyCanExecuteChangedFor(nameof(CommitAndSyncCommand))]
    public partial bool AmendLastCommit { get; set; }

    [ObservableProperty]
    public partial string AuthorLabel { get; set; } = "Author: default";

    [ObservableProperty]
    public partial bool HasStaged { get; set; }

    [ObservableProperty]
    public partial bool HasChanges { get; set; }

    [ObservableProperty]
    public partial string Summary { get; set; } = string.Empty;

    /// <summary>Commit is allowed when there's a message and either staged files or an amend.</summary>
    private bool CanCommit() =>
        !_busy && !string.IsNullOrWhiteSpace(Message) && (Staged.Count > 0 || AmendLastCommit);

    public async Task OpenAsync()
    {
        _authorText = null;
        _committerText = null;
        AuthorLabel = "Author: default";
        await LoadAsync();
        IsOpen = true;
    }

    /// <summary>Reloads the file lists (called on open and when the live-watch detects changes).</summary>
    public async Task LoadAsync()
    {
        WorkingTreeStatus status;
        try { status = await _git.GetWorkingTreeStatusAsync(); }
        catch { status = WorkingTreeStatus.Empty; }

        Staged.Clear();
        foreach (var f in status.Staged.OrderBy(f => f.Path))
            Staged.Add(new CommitFileViewModel(f, OnFileToggledAsync));

        Changes.Clear();
        foreach (var f in status.Unstaged.OrderBy(f => f.Path))
            Changes.Add(new CommitFileViewModel(f, OnFileToggledAsync));

        HasStaged = Staged.Count > 0;
        HasChanges = Changes.Count > 0;
        Summary = $"{Changes.Count} changed · {Staged.Count} staged";
        CommitCommand.NotifyCanExecuteChanged();
    }

    private async Task OnFileToggledAsync(CommitFileViewModel file, bool staged)
    {
        try
        {
            if (staged) await _git.StageAsync([file.Path]);
            else await _git.UnstageAsync([file.Path]);
            await LoadAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Stage failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    partial void OnAmendLastCommitChanged(bool value)
    {
        if (value)
        {
            // Pre-fill with the last commit's message (only if the box is empty so we
            // don't clobber what the user already typed).
            if (string.IsNullOrWhiteSpace(Message) && _lastCommitMessage != null)
                Message = _lastCommitMessage;
        }
    }

    /// <summary>Called by the host with the current HEAD message so amend can pre-fill it.</summary>
    public void SetLastCommitMessage(string? message) => _lastCommitMessage = message;

    [RelayCommand]
    private void EditAuthor()
    {
        var dialog = new Views.EditAuthorsDialog(_authorDir) { Owner = Application.Current.MainWindow };
        if (dialog.ShowDialog() == true)
        {
            _authorText = dialog.SelectedAuthorText;
            _committerText = dialog.SelectedCommitterText;
            AuthorLabel = string.IsNullOrWhiteSpace(_authorText)
                ? "Author: default"
                : $"Author: {_authorText}";
        }
    }

    [RelayCommand]
    private async Task StageAll()
    {
        try { await _git.StageAllAsync(); await LoadAsync(); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Stage failed", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    [RelayCommand]
    private async Task UnstageAll()
    {
        try
        {
            await _git.UnstageAsync(Staged.Select(f => f.Path).ToList());
            await LoadAsync();
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Unstage failed", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    [RelayCommand(CanExecute = nameof(CanCommit))]
    private Task Commit() => DoCommitAsync(CommitFollowUp.None);

    [RelayCommand(CanExecute = nameof(CanCommit))]
    private Task CommitAndPush() => DoCommitAsync(CommitFollowUp.Push);

    [RelayCommand(CanExecute = nameof(CanCommit))]
    private Task CommitAndSync() => DoCommitAsync(CommitFollowUp.Sync);

    private enum CommitFollowUp { None, Push, Sync }

    private async Task DoCommitAsync(CommitFollowUp followUp)
    {
        if (!CanCommit()) return;
        _busy = true;
        CommitCommand.NotifyCanExecuteChanged();

        var (authorName, authorEmail) = ParseIdentity(_authorText);
        var (committerName, committerEmail) = ParseIdentity(_committerText);
        var branch = _getBranch();
        var amend = AmendLastCommit;

        try
        {
            _ = _snapshots.CaptureAsync(_git, amend ? "Amend (commit panel)" : "Commit");

            var beforeSha = await SafeHeadAsync();
            var newSha = await _feedback.RunAsync("Commit",
                () => _git.CommitAsync(new CommitRequest(
                    Message.Trim(), amend, authorName, authorEmail, committerName, committerEmail)),
                sha => sha.Length > 7 ? sha[..7] : sha);

            var shortBefore = beforeSha is { Length: >= 7 } ? beforeSha[..7] : beforeSha ?? string.Empty;
            var shortAfter = newSha.Length >= 7 ? newSha[..7] : newSha;
            await _opsLog.RecordAsync(new OperationRecord(
                Id: Guid.NewGuid().ToString(),
                Timestamp: DateTimeOffset.Now,
                Kind: amend ? OperationKind.Amend : OperationKind.Commit,
                Description: amend ? $"Amend {shortAfter}" : $"Commit {shortAfter}: {Message.Trim()}",
                BranchName: branch,
                BeforeSha: shortBefore,
                AfterSha: shortAfter,
                ReflogSelector: null,
                Status: OperationStatus.Active));

            // Follow-up remote action. Amend that rewrote an already-pushed commit needs
            // force-with-lease; a fresh commit is a fast-forward push.
            var remote = _getRemote() ?? "origin";
            if (followUp == CommitFollowUp.Push)
            {
                await _feedback.RunAsync("Push", () => _git.PushAsync(remote, forceWithLease: amend));
            }
            else if (followUp == CommitFollowUp.Sync)
            {
                await _feedback.RunAsync("Sync", async () =>
                {
                    await _git.FetchAsync(remote);
                    await _git.PullAsync(remote);
                    await _git.PushAsync(remote, forceWithLease: amend);
                });
            }

            // Reset and close.
            Message = string.Empty;
            AmendLastCommit = false;
            _authorText = _committerText = null;
            AuthorLabel = "Author: default";
            IsOpen = false;
            await _onChanged();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Commit failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _busy = false;
            CommitCommand.NotifyCanExecuteChanged();
        }
    }

    [RelayCommand]
    private Task StashAll() => DoStashAsync(includeUntracked: true, keepStaged: false);

    [RelayCommand]
    private Task StashKeepStaged() => DoStashAsync(includeUntracked: true, keepStaged: true);

    private async Task DoStashAsync(bool includeUntracked, bool keepStaged)
    {
        // "Keep staged" re-stages the index after stashing so the staged set survives.
        var stagedPaths = keepStaged ? Staged.Select(f => f.Path).ToList() : [];
        try
        {
            _ = _snapshots.CaptureAsync(_git, "Stash (commit panel)");
            await _feedback.RunAsync("Stash",
                () => _git.CreateStashAsync($"WIP on {_getBranch()}", includeUntracked));

            if (keepStaged && stagedPaths.Count > 0)
            {
                try { await _git.StageAsync(stagedPaths); } catch { /* best-effort */ }
            }

            IsOpen = false;
            await _onChanged();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Stash failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    [RelayCommand]
    private void Close() => IsOpen = false;

    private async Task<string?> SafeHeadAsync()
    {
        try { return await _git.GetHeadShaAsync(); } catch { return null; }
    }

    private static (string? name, string? email) ParseIdentity(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return (null, null);
        var m = Regex.Match(text, @"^(.+?)\s*<([^>]*)>\s*$");
        return m.Success
            ? (m.Groups[1].Value.Trim(), m.Groups[2].Value.Trim())
            : (text.Trim(), null);
    }
}
