using System.Windows;

using CommunityToolkit.Mvvm.Input;

using Gitster.Services;
using Gitster.Services.Git;
using Gitster.Services.OperationsLog;
using Gitster.Views;

namespace Gitster.ViewModels;

/// <summary>
/// Commands for the QuickActions panel (Reword, Fixup, Squash, Cherry-pick).
/// </summary>
public partial class QuickActionsViewModel : BaseViewModel
{
    private readonly IGitBackend             _git;
    private readonly OperationFeedbackService _feedback;
    private readonly OperationsLogService     _opsLog;
    private readonly SnapshotService          _snapshots;
    private readonly Func<CommitItem?>        _getSelected;
    private readonly Func<List<CommitItem>>   _getMultiSelected;
    private readonly Func<Task>               _onRefresh;

    public QuickActionsViewModel(
        IGitBackend             git,
        OperationFeedbackService feedback,
        OperationsLogService     opsLog,
        SnapshotService          snapshots,
        Func<CommitItem?>        getSelected,
        Func<List<CommitItem>>   getMultiSelected,
        Func<Task>               onRefresh)
    {
        _git              = git;
        _feedback         = feedback;
        _opsLog           = opsLog;
        _snapshots        = snapshots;
        _getSelected      = getSelected;
        _getMultiSelected = getMultiSelected;
        _onRefresh        = onRefresh;
    }

    // ── Reword (Step G) ───────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanReword))]
    private async Task Reword()
    {
        var commit = _getSelected();
        if (commit is null) return;

        var isNonHead = await IsNonHeadAsync(commit.FullSha);
        if (isNonHead && !GitCli.IsAvailable)
        {
            MessageBox.Show(
                "Rewording an older commit requires the Git command-line tool.\n" +
                "Install Git for Windows and restart Gitster.",
                "Git CLI required", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // A reword rewrites the commit (new SHA) regardless of whether it's HEAD, so the
        // force-push warning must fire for any already-pushed commit.
        if (commit.RemoteState == CommitRemoteState.OnRemote)
        {
            var r = MessageBox.Show(
                "This commit has already been pushed. Rewording it will require a force-push.\n\nContinue?",
                "Force-push warning", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (r != MessageBoxResult.Yes) return;
        }

        var dlg = new RewordDialog(commit.Message) { Owner = Application.Current.MainWindow };
        if (dlg.ShowDialog() != true) return;

        var newMessage = dlg.NewMessage;

        try
        {
            var beforeSha = await _git.GetHeadShaAsync();
            _ = _snapshots.CaptureAsync(_git, $"Reword {commit.CommitId}");

            await _feedback.RunAsync("Reword", () => _git.RewordCommitAsync(commit.FullSha, newMessage));

            var afterSha = await _git.GetHeadShaAsync();
            var branch   = (await _git.GetCurrentBranchAsync()).Name;
            var short7b  = beforeSha.Length >= 7 ? beforeSha[..7] : beforeSha;
            var short7a  = afterSha.Length  >= 7 ? afterSha[..7]  : afterSha;

            await _opsLog.RecordAsync(new OperationRecord(
                Id:             Guid.NewGuid().ToString(),
                Timestamp:      DateTimeOffset.Now,
                Kind:           OperationKind.Reword,
                Description:    $"Reword {short7b}",
                BranchName:     branch,
                BeforeSha:      short7b,
                AfterSha:       short7a,
                ReflogSelector: null,
                Status:         OperationStatus.Active));

            await _onRefresh();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Reword failed:\n{ex.Message}", "Gitster", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private bool CanReword() => _getSelected() is not null;

    // ── Fixup (Step F) ────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanFixup))]
    private async Task Fixup()
    {
        var commit = _getSelected();
        if (commit is null) return;

        if (commit.RemoteState == CommitRemoteState.OnRemote)
        {
            var r = MessageBox.Show(
                "This commit has already been pushed. Fixup will rewrite history and require a force-push.\n\nContinue?",
                "Force-push warning", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (r != MessageBoxResult.Yes) return;
        }

        try
        {
            // BeforeSha must be the pre-operation HEAD (the undo target), NOT the fixup
            // target commit — otherwise undo would reset to an older commit and discard work.
            var beforeSha = await _git.GetHeadShaAsync();
            _ = _snapshots.CaptureAsync(_git, $"Fixup into {commit.CommitId}");

            await _feedback.RunAsync("Fixup", () => _git.FixupIntoCommitAsync(commit.FullSha));

            var afterSha = await _git.GetHeadShaAsync();
            var branch   = (await _git.GetCurrentBranchAsync()).Name;
            var target7  = commit.FullSha.Length >= 7 ? commit.FullSha[..7] : commit.FullSha;
            var short7b  = beforeSha.Length >= 7 ? beforeSha[..7] : beforeSha;
            var short7a  = afterSha.Length >= 7 ? afterSha[..7] : afterSha;

            await _opsLog.RecordAsync(new OperationRecord(
                Id:             Guid.NewGuid().ToString(),
                Timestamp:      DateTimeOffset.Now,
                Kind:           OperationKind.Fixup,
                Description:    $"Fixup into {target7}",
                BranchName:     branch,
                BeforeSha:      short7b,
                AfterSha:       short7a,
                ReflogSelector: null,
                Status:         OperationStatus.Active));

            await _onRefresh();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Fixup failed:\n{ex.Message}", "Gitster", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private bool CanFixup() => GitCli.IsAvailable && _getSelected() is not null;

    // ── Squash (Step H) ───────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanSquash))]
    private async Task SquashSelected()
    {
        var commits = _getMultiSelected();
        if (commits.Count < 2) return;

        // Non-contiguous squash is ill-defined (a gap would silently fold unselected
        // commits in). Require a continuous range and tell the user if there's a gap.
        var selectedShas = commits.Select(c => c.FullSha).ToList();
        bool contiguous;
        try { contiguous = await _git.AreCommitsContiguousAsync(selectedShas); }
        catch { contiguous = false; }

        if (!contiguous)
        {
            MessageBox.Show(
                "Squash needs a contiguous range of commits — your selection has a gap.\n\n" +
                "Select commits that are directly next to each other in history and try again.",
                "Cannot squash", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var anyOnRemote = commits.Any(c => c.RemoteState == CommitRemoteState.OnRemote);
        if (anyOnRemote)
        {
            var r = MessageBox.Show(
                "One or more selected commits have already been pushed. Squashing will rewrite history and require a force-push.\n\nContinue?",
                "Force-push warning", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (r != MessageBoxResult.Yes) return;
        }

        var combined = string.Join("\n\n", commits.Select(c => c.Message));
        var dlg = new SquashDialog(combined) { Owner = Application.Current.MainWindow };
        if (dlg.ShowDialog() != true) return;

        try
        {
            var beforeSha = await _git.GetHeadShaAsync();
            _ = _snapshots.CaptureAsync(_git, $"Squash {commits.Count} commits");

            await _feedback.RunAsync("Squash",
                () => _git.SquashCommitsAsync(selectedShas, dlg.CombinedMessage, dlg.OverrideDate));

            var afterSha  = await _git.GetHeadShaAsync();
            var branch    = (await _git.GetCurrentBranchAsync()).Name;
            var short7b   = beforeSha.Length >= 7 ? beforeSha[..7] : beforeSha;
            var short7a   = afterSha.Length  >= 7 ? afterSha[..7]  : afterSha;

            await _opsLog.RecordAsync(new OperationRecord(
                Id:             Guid.NewGuid().ToString(),
                Timestamp:      DateTimeOffset.Now,
                Kind:           OperationKind.Squash,
                Description:    $"Squash {commits.Count} commits",
                BranchName:     branch,
                BeforeSha:      short7b,
                AfterSha:       short7a,
                ReflogSelector: null,
                Status:         OperationStatus.Active));

            await _onRefresh();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Squash failed:\n{ex.Message}", "Gitster", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private bool CanSquash() => _getMultiSelected().Count >= 2;

    // ── Cherry-pick (Step H) ──────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanCherryPick))]
    private async Task CherryPick()
    {
        IReadOnlyList<BranchSummary> branches;
        try
        {
            branches = await _git.GetBranchesAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not load branches:\n{ex.Message}", "Gitster",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var dlg = new CherryPickDialog(_git, branches) { Owner = Application.Current.MainWindow };
        if (dlg.ShowDialog() != true || dlg.SelectedSha is null) return;

        try
        {
            var beforeSha = await _git.GetHeadShaAsync();
            _ = _snapshots.CaptureAsync(_git, $"Cherry-pick {dlg.SelectedSha[..7]}");

            if (dlg.OverrideDate.HasValue)
            {
                // Cherry-pick then amend the date
                await _feedback.RunAsync("Cherry-pick", async () =>
                {
                    await _git.CherryPickAsync(dlg.SelectedSha);
                    await _git.AmendAsync(new AmendRequest(dlg.OverrideDate.Value.DateTime));
                });
            }
            else
            {
                await _feedback.RunAsync("Cherry-pick", () => _git.CherryPickAsync(dlg.SelectedSha));
            }

            var afterSha = await _git.GetHeadShaAsync();
            var branch   = (await _git.GetCurrentBranchAsync()).Name;
            var source7  = dlg.SelectedSha.Length >= 7 ? dlg.SelectedSha[..7] : dlg.SelectedSha;
            // BeforeSha is the pre-op HEAD (undo target), not the cherry-picked source.
            var short7b  = beforeSha.Length >= 7 ? beforeSha[..7] : beforeSha;
            var short7a  = afterSha.Length >= 7 ? afterSha[..7] : afterSha;

            await _opsLog.RecordAsync(new OperationRecord(
                Id:             Guid.NewGuid().ToString(),
                Timestamp:      DateTimeOffset.Now,
                Kind:           dlg.OverrideDate.HasValue ? OperationKind.CherryPickTimestamp : OperationKind.CherryPick,
                Description:    $"Cherry-pick {source7}",
                BranchName:     branch,
                BeforeSha:      short7b,
                AfterSha:       short7a,
                ReflogSelector: null,
                Status:         OperationStatus.Active));

            await _onRefresh();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Cherry-pick failed:\n{ex.Message}", "Gitster",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private bool CanCherryPick() => true; // Always enabled — dialog guides user

    // ── Commit to another branch (Phase 3, Step B) ────────────────────────

    [RelayCommand]
    private async Task CommitToBranch()
    {
        IReadOnlyList<BranchListItem> branches;
        string current;
        try
        {
            branches = await _git.GetBranchListAsync();
            current  = (await _git.GetCurrentBranchAsync()).Name;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not load branches:\n{ex.Message}", "Gitster",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var localTargets = branches
            .Where(b => !b.IsRemote && !string.Equals(b.Name, current, StringComparison.Ordinal))
            .Select(b => b.Name)
            .ToList();

        var dlg = new CommitToBranchDialog(localTargets) { Owner = Application.Current.MainWindow };
        if (dlg.ShowDialog() != true) return;

        try
        {
            var beforeSha = await _git.GetHeadShaAsync();
            _ = _snapshots.CaptureAsync(_git, $"Commit to branch {dlg.TargetBranch}");

            // If the user typed a brand-new branch name, create it at the current HEAD first.
            var exists = branches.Any(b => !b.IsRemote &&
                string.Equals(b.Name, dlg.TargetBranch, StringComparison.Ordinal));
            if (!exists)
                await _git.CreateBranchAsync(dlg.TargetBranch, beforeSha);

            var newSha = await _feedback.RunAsync("Commit to branch",
                () => _git.CommitToBranchAsync(new CommitToBranchRequest(
                    dlg.TargetBranch, dlg.Message, dlg.AuthorName, dlg.AuthorEmail,
                    dlg.IncludeUnstaged, dlg.RemoveFromCurrent)),
                sha => sha.Length > 7 ? sha[..7] : sha);

            // The current branch HEAD does not move; record for the audit log with the
            // current HEAD as before/after (the snapshot above is the recovery net).
            var afterHead = await _git.GetHeadShaAsync();
            var short7b   = beforeSha.Length >= 7 ? beforeSha[..7] : beforeSha;
            var short7a   = afterHead.Length >= 7 ? afterHead[..7] : afterHead;
            var target7   = newSha.Length >= 7 ? newSha[..7] : newSha;

            await _opsLog.RecordAsync(new OperationRecord(
                Id:             Guid.NewGuid().ToString(),
                Timestamp:      DateTimeOffset.Now,
                Kind:           OperationKind.CommitOnBranch,
                Description:    $"Commit {target7} to {dlg.TargetBranch}",
                BranchName:     current,
                BeforeSha:      short7b,
                AfterSha:       short7a,
                ReflogSelector: null,
                Status:         OperationStatus.Active));

            await _onRefresh();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Commit to branch failed:\n{ex.Message}", "Gitster",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ── Snapshot to branch (Phase 3, Step C) ──────────────────────────────

    [RelayCommand]
    private async Task SnapshotToBranch()
    {
        var dlg = new SnapshotBranchDialog { Owner = Application.Current.MainWindow };
        if (dlg.ShowDialog() != true) return;

        try
        {
            var beforeSha = await _git.GetHeadShaAsync();
            _ = _snapshots.CaptureAsync(_git, $"Snapshot to branch {dlg.BranchName}");

            var created = await _feedback.RunAsync("Snapshot",
                () => _git.CreateSnapshotBranchAsync(dlg.BranchName, dlg.IncludeUncommitted),
                name => name);

            var short7 = beforeSha.Length >= 7 ? beforeSha[..7] : beforeSha;
            await _opsLog.RecordAsync(new OperationRecord(
                Id:             Guid.NewGuid().ToString(),
                Timestamp:      DateTimeOffset.Now,
                Kind:           OperationKind.Snapshot,
                Description:    $"Snapshot → branch '{created}'",
                BranchName:     (await _git.GetCurrentBranchAsync()).Name,
                BeforeSha:      short7,
                AfterSha:       short7,
                ReflogSelector: null,
                Status:         OperationStatus.Active));

            await _onRefresh();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Snapshot failed:\n{ex.Message}", "Gitster",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ── Change Author ─────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanChangeAuthor))]
    private void ChangeAuthor() { }
    private static bool CanChangeAuthor() => false;

    // ── Helpers ────────────────────────────────────────────────────────────

    private async Task<bool> IsNonHeadAsync(string sha)
    {
        try
        {
            var headSha = await _git.GetHeadShaAsync();
            return !headSha.StartsWith(sha, StringComparison.OrdinalIgnoreCase) &&
                   !sha.StartsWith(headSha, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }
}
