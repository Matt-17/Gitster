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

        if (isNonHead && commit.RemoteState == CommitRemoteState.OnRemote)
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
            _ = _snapshots.CaptureAsync(_git, $"Fixup into {commit.CommitId}");

            await _feedback.RunAsync("Fixup", () => _git.FixupIntoCommitAsync(commit.FullSha));

            var afterSha = await _git.GetHeadShaAsync();
            var branch   = (await _git.GetCurrentBranchAsync()).Name;
            var short7   = commit.FullSha.Length >= 7 ? commit.FullSha[..7] : commit.FullSha;
            var short7a  = afterSha.Length >= 7 ? afterSha[..7] : afterSha;

            await _opsLog.RecordAsync(new OperationRecord(
                Id:             Guid.NewGuid().ToString(),
                Timestamp:      DateTimeOffset.Now,
                Kind:           OperationKind.Fixup,
                Description:    $"Fixup into {short7}",
                BranchName:     branch,
                BeforeSha:      short7,
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
            var shas      = commits.Select(c => c.FullSha).ToList();
            var beforeSha = await _git.GetHeadShaAsync();
            _ = _snapshots.CaptureAsync(_git, $"Squash {commits.Count} commits");

            await _feedback.RunAsync("Squash",
                () => _git.SquashCommitsAsync(shas, dlg.CombinedMessage, dlg.OverrideDate));

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
            var short7   = dlg.SelectedSha.Length >= 7 ? dlg.SelectedSha[..7] : dlg.SelectedSha;
            var short7a  = afterSha.Length >= 7 ? afterSha[..7] : afterSha;

            await _opsLog.RecordAsync(new OperationRecord(
                Id:             Guid.NewGuid().ToString(),
                Timestamp:      DateTimeOffset.Now,
                Kind:           dlg.OverrideDate.HasValue ? OperationKind.CherryPickTimestamp : OperationKind.CherryPick,
                Description:    $"Cherry-pick {short7}",
                BranchName:     branch,
                BeforeSha:      short7,
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
