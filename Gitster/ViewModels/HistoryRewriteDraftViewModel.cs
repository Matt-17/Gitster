using CommunityToolkit.Mvvm.Input;

using Gitster.Services;
using Gitster.Services.Git;
using Gitster.Services.OperationsLog;

namespace Gitster.ViewModels;

public sealed partial class HistoryRewriteDraftViewModel : BaseViewModel
{
    private readonly IGitBackend? _git;
    private readonly OperationFeedbackService? _feedback;
    private readonly OperationsLogService? _opsLog;
    private readonly SnapshotService? _snapshots;
    private readonly IWindowService? _windowService;
    private readonly Func<string?>? _getBranchName;
    private readonly Func<string?, Task>? _refreshAfterApply;

    private readonly Dictionary<string, CommitDetails> _loadedDetails = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CommitDraft> _drafts = new(StringComparer.OrdinalIgnoreCase);
    private List<CommitItem> _commits = [];
    private CommitItem? _selectedCommit;
    private string _messageText = string.Empty;
    private string _authorName = string.Empty;
    private string _authorEmail = string.Empty;
    private DateTime? _authorDate;
    private bool _updateCommitterTimestamp = true;
    private bool _isLoadingDetails;
    private bool _isApplying;
    private bool _isSelectedCommitEditable;
    private bool _hasDrafts;
    private bool _hasRemoteRewriteRisk;
    private bool _hasLocalOnlyCleanup;
    private int _directEditCount;
    private int _transitiveRewriteCount;
    private int _affectedRewriteCount;
    private string _summaryText = "No pending history edits.";
    private bool _syncingEditor;
    private bool _editorTouched;
    private int _selectionVersion;

    public HistoryRewriteDraftViewModel()
    {
    }

    public HistoryRewriteDraftViewModel(
        IGitBackend git,
        OperationFeedbackService feedback,
        OperationsLogService opsLog,
        SnapshotService snapshots,
        IWindowService windowService,
        RepositoryCommandContext commandContext)
        : this(
            git,
            feedback,
            opsLog,
            snapshots,
            windowService,
            () => commandContext.CurrentBranch,
            commandContext.RefreshAfterHistoryRewrite)
    {
    }

    public HistoryRewriteDraftViewModel(
        IGitBackend git,
        OperationFeedbackService feedback,
        OperationsLogService opsLog,
        SnapshotService snapshots,
        IWindowService windowService,
        Func<string?> getBranchName,
        Func<string?, Task> refreshAfterApply)
    {
        _git = git;
        _feedback = feedback;
        _opsLog = opsLog;
        _snapshots = snapshots;
        _windowService = windowService;
        _getBranchName = getBranchName;
        _refreshAfterApply = refreshAfterApply;
    }

    public CommitItem? SelectedCommit
    {
        get => _selectedCommit;
        private set => SetProperty(ref _selectedCommit, value);
    }

    public string MessageText
    {
        get => _messageText;
        set
        {
            if (SetProperty(ref _messageText, value))
                UpdateDraftFromEditor();
        }
    }

    public string AuthorName
    {
        get => _authorName;
        set
        {
            if (SetProperty(ref _authorName, value))
                UpdateDraftFromEditor();
        }
    }

    public string AuthorEmail
    {
        get => _authorEmail;
        set
        {
            if (SetProperty(ref _authorEmail, value))
                UpdateDraftFromEditor();
        }
    }

    public DateTime? AuthorDate
    {
        get => _authorDate;
        set
        {
            if (SetProperty(ref _authorDate, value))
                UpdateDraftFromEditor();
        }
    }

    public bool UpdateCommitterTimestamp
    {
        get => _updateCommitterTimestamp;
        set
        {
            if (SetProperty(ref _updateCommitterTimestamp, value))
                UpdateDraftFromEditor();
        }
    }

    public bool IsLoadingDetails
    {
        get => _isLoadingDetails;
        private set => SetProperty(ref _isLoadingDetails, value);
    }

    public bool IsApplying
    {
        get => _isApplying;
        private set
        {
            if (SetProperty(ref _isApplying, value))
                RefreshSummary();
        }
    }

    public bool IsSelectedCommitEditable
    {
        get => _isSelectedCommitEditable;
        private set => SetProperty(ref _isSelectedCommitEditable, value);
    }

    public bool HasDrafts
    {
        get => _hasDrafts;
        private set => SetProperty(ref _hasDrafts, value);
    }

    public bool HasRemoteRewriteRisk
    {
        get => _hasRemoteRewriteRisk;
        private set => SetProperty(ref _hasRemoteRewriteRisk, value);
    }

    public bool HasLocalOnlyCleanup
    {
        get => _hasLocalOnlyCleanup;
        private set => SetProperty(ref _hasLocalOnlyCleanup, value);
    }

    public int DirectEditCount
    {
        get => _directEditCount;
        private set => SetProperty(ref _directEditCount, value);
    }

    public int TransitiveRewriteCount
    {
        get => _transitiveRewriteCount;
        private set => SetProperty(ref _transitiveRewriteCount, value);
    }

    public int AffectedRewriteCount
    {
        get => _affectedRewriteCount;
        private set => SetProperty(ref _affectedRewriteCount, value);
    }

    public string SummaryText
    {
        get => _summaryText;
        private set => SetProperty(ref _summaryText, value);
    }

    public bool CanApply => HasDrafts && !IsApplying;
    public bool CanDiscardSelected => SelectedCommit is not null && _drafts.ContainsKey(SelectedCommit.FullSha);

    public void SetCommits(IEnumerable<CommitItem> commits)
    {
        foreach (var row in _commits)
            row.ClearHistoryEditOverlay();

        _commits = commits.ToList();
        RemoveMissingDrafts();
        RefreshProjection();
    }

    public void SetSelectedCommit(CommitItem? commit)
    {
        _selectionVersion++;
        SelectedCommit = commit;
        _editorTouched = false;
        IsSelectedCommitEditable = commit is not null && commit.RemoteState != CommitRemoteState.Incoming;
        OnPropertyChanged(nameof(CanDiscardSelected));

        if (commit is null)
        {
            LoadEditor(null);
            RefreshSummary();
            return;
        }

        LoadEditor(commit);

        if (_git is not null)
            _ = LoadDetailsAsync(commit, _selectionVersion);

        RefreshSummary();
    }

    [RelayCommand]
    private void ResetSelected()
    {
        if (SelectedCommit is null)
            return;

        _drafts.Remove(SelectedCommit.FullSha);
        LoadEditor(SelectedCommit);
        RefreshProjection();
    }

    [RelayCommand]
    private void ClearAll()
    {
        _drafts.Clear();
        LoadEditor(SelectedCommit);
        RefreshProjection();
    }

    [RelayCommand]
    private async Task Apply()
    {
        if (!CanApply || _git is null || _feedback is null || _opsLog is null)
            return;

        var rewrites = BuildRewrites();
        if (rewrites.Count == 0)
            return;

        if (HasRemoteRewriteRisk && _windowService?.Confirm(
            "This rewrite touches commits that already exist on the tracking remote.\n\nAfter applying it, push with force-with-lease to replace the remote history.",
            "Rewrite pushed history") != true)
        {
            return;
        }

        IsApplying = true;
        try
        {
            var rewritePlan = BuildRewritePlan();
            var selectedOriginalSha = SelectedCommit?.FullSha;
            var beforeSha = await _git.GetHeadShaAsync();
            var shortBefore = ShortSha(beforeSha);
            var branchName = _getBranchName?.Invoke() ?? string.Empty;
            var directCount = DirectEditCount;
            var transitiveCount = TransitiveRewriteCount;
            var remoteRisk = HasRemoteRewriteRisk;

            if (_snapshots is not null)
                await _snapshots.CaptureAsync(_git, "Before batch history edit");

            var afterSha = await _feedback.RunAsync(
                "Rewrite history",
                async () =>
                {
                    await Task.Run(() => _git.RewriteCommitsAsync(rewrites, branchName));
                    return await _git.GetHeadShaAsync();
                },
                ShortSha);

            var verification = await VerifyApplyAsync(rewritePlan, selectedOriginalSha);
            if (!verification.Success)
            {
                _windowService?.Error(
                    $"Batch history edit could not be verified:\n{verification.Message}\n\nYour pending edits are still visible.",
                    "Gitster");
                if (_refreshAfterApply is not null)
                    await _refreshAfterApply(verification.PreferredSelectionSha);
                return;
            }

            string? reflogSelector = null;
            try { reflogSelector = await _git.GetReflogSelectorForHeadAsync(); }
            catch { }

            await _opsLog.RecordAsync(new OperationRecord(
                Id: Guid.NewGuid().ToString(),
                Timestamp: DateTimeOffset.Now,
                Kind: OperationKind.HistoryEdit,
                Description: remoteRisk
                    ? $"Batch history edit ({directCount} edit{Plural(directCount)}, {transitiveCount} local descendant rewrite{Plural(transitiveCount)}, force-with-lease required)"
                    : $"Batch history edit ({directCount} edit{Plural(directCount)}, {transitiveCount} local descendant rewrite{Plural(transitiveCount)})",
                BranchName: branchName,
                BeforeSha: shortBefore,
                AfterSha: ShortSha(afterSha),
                ReflogSelector: reflogSelector,
                Status: OperationStatus.Active));

            _drafts.Clear();
            RefreshProjection();

            if (_refreshAfterApply is not null)
                await _refreshAfterApply(verification.PreferredSelectionSha);
        }
        catch (Exception ex)
        {
            _windowService?.Error($"Batch history edit failed:\n{ex.Message}", "Gitster");
        }
        finally
        {
            IsApplying = false;
        }
    }

    public IReadOnlyList<CommitRewrite> BuildRewrites()
        => _drafts.Values
            .Where(d => d.HasChanges)
            .Select(d => d.ToRewrite())
            .ToList();

    private async Task LoadDetailsAsync(CommitItem commit, int version)
    {
        if (_git is null)
            return;

        IsLoadingDetails = true;
        try
        {
            var details = await Task.Run(async () => await _git.GetCommitAsync(commit.FullSha));
            if (version != _selectionVersion)
                return;

            _loadedDetails[commit.FullSha] = details;

            if (_drafts.TryGetValue(commit.FullSha, out var draft))
                draft.UpdateOriginalDetails(details, updatePendingIfUnchanged: !_editorTouched);

            if (!_editorTouched)
                LoadEditor(commit);

            RefreshProjection();
        }
        catch
        {
            // The short-row metadata is still enough to edit basic fields.
        }
        finally
        {
            if (version == _selectionVersion)
                IsLoadingDetails = false;
        }
    }

    private void LoadEditor(CommitItem? commit)
    {
        _syncingEditor = true;
        try
        {
            if (commit is null)
            {
                MessageText = string.Empty;
                AuthorName = string.Empty;
                AuthorEmail = string.Empty;
                AuthorDate = null;
                UpdateCommitterTimestamp = true;
                return;
            }

            var original = GetOriginal(commit);
            if (_drafts.TryGetValue(commit.FullSha, out var draft))
            {
                MessageText = draft.NewMessage;
                AuthorName = draft.NewAuthorName;
                AuthorEmail = draft.NewAuthorEmail;
                AuthorDate = draft.NewAuthorDate;
                UpdateCommitterTimestamp = draft.UpdateCommitterTimestamp;
            }
            else
            {
                MessageText = original.Message;
                AuthorName = original.AuthorName;
                AuthorEmail = original.AuthorEmail;
                AuthorDate = original.Date;
                UpdateCommitterTimestamp = true;
            }
        }
        finally
        {
            _syncingEditor = false;
        }

        OnPropertyChanged(nameof(CanDiscardSelected));
    }

    private void UpdateDraftFromEditor()
    {
        if (_syncingEditor || SelectedCommit is null)
            return;

        _editorTouched = true;

        if (!IsSelectedCommitEditable)
            return;

        var draft = GetOrCreateDraft(SelectedCommit);
        draft.NewMessage = MessageText;
        draft.NewAuthorName = AuthorName.Trim();
        draft.NewAuthorEmail = AuthorEmail.Trim();
        draft.NewAuthorDate = AuthorDate;
        draft.UpdateCommitterTimestamp = UpdateCommitterTimestamp;

        if (!draft.HasChanges)
            _drafts.Remove(SelectedCommit.FullSha);

        RefreshProjection();
    }

    private CommitDraft GetOrCreateDraft(CommitItem commit)
    {
        if (_drafts.TryGetValue(commit.FullSha, out var draft))
            return draft;

        var original = GetOriginal(commit);
        draft = new CommitDraft(commit.FullSha, original);
        _drafts[commit.FullSha] = draft;
        return draft;
    }

    private OriginalCommit GetOriginal(CommitItem commit)
    {
        if (_loadedDetails.TryGetValue(commit.FullSha, out var details))
        {
            return new OriginalCommit(
                details.Message,
                details.AuthorName,
                details.AuthorEmail,
                details.Date);
        }

        return new OriginalCommit(
            commit.Message,
            commit.AuthorName,
            commit.AuthorEmail,
            commit.Date);
    }

    private void RefreshProjection()
    {
        RemoveMissingDrafts();

        foreach (var row in _commits)
            row.ClearHistoryEditOverlay();

        var plan = BuildRewritePlan();
        if (plan.Direct.Count == 0)
        {
            DirectEditCount = 0;
            TransitiveRewriteCount = 0;
            AffectedRewriteCount = 0;
            HasRemoteRewriteRisk = false;
            HasLocalOnlyCleanup = false;
            HasDrafts = false;
            RefreshSummary();
            return;
        }

        var direct = plan.Direct.Select(d => d.FullSha).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var remoteRisk = plan.HasRemoteRisk;

        foreach (var affected in plan.Affected)
        {
            var row = affected.Commit;
            row.IsPendingRemoteRisk = IsRemoteRewriteRisk(row);
            row.IsPendingLocalCleanup = !remoteRisk;

            if (direct.Contains(row.FullSha) && _drafts.TryGetValue(row.FullSha, out var draft))
            {
                row.IsHistoryEditDirect = true;
                row.HasPendingMessageChange = draft.HasMessageChange;
                row.HasPendingAuthorChange = draft.HasAuthorChange;
                row.HasPendingTimeChange = draft.HasTimeChange;
                row.PendingMessage = draft.HasMessageChange ? FirstLine(draft.NewMessage) : null;
                row.PendingAuthorName = draft.HasAuthorChange ? draft.NewAuthorName : null;
                row.PendingAuthorEmail = draft.HasAuthorChange ? draft.NewAuthorEmail : null;
                row.PendingDate = draft.HasTimeChange ? draft.NewAuthorDate : null;
                row.HistoryEditTooltip = BuildDirectTooltip(draft, remoteRisk);
            }
            else
            {
                row.IsHistoryEditTransitive = true;
                row.HistoryEditTooltip = remoteRisk
                    ? "Local descendant will be rewritten because an older parent changes. Remote contains old copies."
                    : "Will be rewritten because an older parent changes.";
            }
        }

        DirectEditCount = plan.Direct.Count;
        TransitiveRewriteCount = plan.Affected.Count - plan.Direct.Count;
        AffectedRewriteCount = plan.Affected.Count;
        HasRemoteRewriteRisk = remoteRisk;
        HasLocalOnlyCleanup = !remoteRisk;
        HasDrafts = true;
        RefreshSummary();
    }

    private void RefreshSummary()
    {
        OnPropertyChanged(nameof(CanApply));
        OnPropertyChanged(nameof(CanDiscardSelected));

        if (IsApplying)
        {
            SummaryText = "Applying history rewrite...";
            return;
        }

        if (SelectedCommit?.RemoteState == CommitRemoteState.Incoming)
        {
            SummaryText = "Incoming commits must be pulled or cherry-picked before they can be edited.";
            return;
        }

        if (!HasDrafts)
        {
            SummaryText = SelectedCommit is null
                ? "Select a commit to edit history."
                : "No pending history edits.";
            return;
        }

        var direct = $"{DirectEditCount} edit{Plural(DirectEditCount)}";
        var transitive = TransitiveRewriteCount == 0
            ? "no local descendant rewrites"
            : $"{TransitiveRewriteCount} local descendant rewrite{Plural(TransitiveRewriteCount)}";
        SummaryText = HasRemoteRewriteRisk
            ? $"{direct}, {transitive}. Remote contains old copies: force-with-lease required."
            : $"{direct}, {transitive}. Local cleanup.";
    }

    private void RemoveMissingDrafts()
    {
        if (_commits.Count == 0)
        {
            _drafts.Clear();
            return;
        }

        var known = new HashSet<string>(
            _commits.Where(IsLocalBranchRow).Select(c => c.FullSha),
            StringComparer.OrdinalIgnoreCase);
        foreach (var sha in _drafts.Keys.Where(k => !known.Contains(k)).ToList())
            _drafts.Remove(sha);
    }

    private static string BuildDirectTooltip(CommitDraft draft, bool remoteRisk)
    {
        var parts = new List<string>();
        if (draft.HasMessageChange) parts.Add("message");
        if (draft.HasAuthorChange) parts.Add("author");
        if (draft.HasTimeChange) parts.Add("time");
        var suffix = remoteRisk ? " Remote contains old copies." : string.Empty;
        return $"Pending edit: {string.Join(", ", parts)}.{suffix}";
    }

    private RewritePlan BuildRewritePlan()
    {
        var localRows = _commits
            .Where(IsLocalBranchRow)
            .Select((commit, index) => new AffectedCommit(commit, index))
            .ToList();

        var direct = localRows
            .Where(row => _drafts.TryGetValue(row.FullSha, out var draft) && draft.HasChanges)
            .ToList();

        if (direct.Count == 0)
            return new RewritePlan([], [], false);

        var oldestEditedLocalIndex = direct.Max(row => row.LocalIndex);
        var affected = localRows
            .Where(row => row.LocalIndex <= oldestEditedLocalIndex)
            .ToList();

        var hasRemoteRisk = affected.Any(row => IsRemoteRewriteRisk(row.Commit));
        return new RewritePlan(affected, direct, hasRemoteRisk);
    }

    private async Task<ApplyVerificationResult> VerifyApplyAsync(
        RewritePlan beforePlan,
        string? selectedOriginalSha)
    {
        if (_git is null)
            return ApplyVerificationResult.Failed("Git backend is unavailable.");

        var directTargets = beforePlan.Direct
            .Select(row => new VerificationTarget(
                row.Commit.FullSha,
                row.LocalIndex,
                _drafts[row.Commit.FullSha].ToRewrite()))
            .ToList();

        if (directTargets.Count == 0)
            return ApplyVerificationResult.Failed("No direct edits were available to verify.");

        var refreshed = await Task.Run(async () => await _git.GetCommitsAsync());
        var localRows = refreshed
            .Where(IsLocalBranchInfo)
            .Select((commit, index) => new VerificationCommit(commit, index, FullSha(commit)))
            .ToList();

        var localShas = localRows
            .Select(row => row.FullSha)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var target in directTargets)
        {
            if (localShas.Contains(target.OriginalSha))
            {
                return ApplyVerificationResult.Failed(
                    $"Original commit {ShortSha(target.OriginalSha)} is still reachable from the local branch.");
            }
        }

        var replacements = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var usedReplacementShas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var selectedWasDirect = selectedOriginalSha is not null
            && directTargets.Any(target => string.Equals(target.OriginalSha, selectedOriginalSha, StringComparison.OrdinalIgnoreCase));
        foreach (var target in directTargets)
        {
            var replacement = await FindReplacementAsync(target, localRows, usedReplacementShas);
            if (replacement is null)
            {
                if (selectedWasDirect
                    && string.Equals(target.OriginalSha, selectedOriginalSha, StringComparison.OrdinalIgnoreCase))
                {
                    return ApplyVerificationResult.Failed(
                        $"Could not find a replacement commit containing the selected edit for {ShortSha(target.OriginalSha)}.");
                }

                continue;
            }

            replacements[target.OriginalSha] = replacement.FullSha;
            usedReplacementShas.Add(replacement.FullSha);
        }

        var preferredSelectionSha = ResolvePreferredSelection(beforePlan, selectedOriginalSha, replacements, localRows);
        return ApplyVerificationResult.Passed(preferredSelectionSha);
    }

    private async Task<VerificationCommit?> FindReplacementAsync(
        VerificationTarget target,
        IReadOnlyList<VerificationCommit> localRows,
        HashSet<string> usedReplacementShas)
    {
        if (target.LocalIndex >= 0 && target.LocalIndex < localRows.Count)
        {
            var samePosition = localRows[target.LocalIndex];
            if (!usedReplacementShas.Contains(samePosition.FullSha)
                && !string.Equals(samePosition.FullSha, target.OriginalSha, StringComparison.OrdinalIgnoreCase)
                && await MatchesRewriteAsync(samePosition, target.Rewrite))
            {
                return samePosition;
            }
        }

        foreach (var row in localRows)
        {
            if (usedReplacementShas.Contains(row.FullSha)
                || string.Equals(row.FullSha, target.OriginalSha, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (await MatchesRewriteAsync(row, target.Rewrite))
                return row;
        }

        return null;
    }

    private async Task<bool> MatchesRewriteAsync(VerificationCommit candidate, CommitRewrite rewrite)
    {
        CommitDetails? details = null;
        if (_git is not null)
        {
            try { details = await Task.Run(async () => await _git.GetCommitAsync(candidate.FullSha)); }
            catch { }
        }

        if (rewrite.NewMessage is not null)
        {
            if (details is not null)
            {
                if (!string.Equals(NormalizeMessage(details.Message), NormalizeMessage(rewrite.NewMessage), StringComparison.Ordinal))
                    return false;
            }
            else if (!string.Equals(candidate.Commit.Message, FirstLine(rewrite.NewMessage), StringComparison.Ordinal))
            {
                return false;
            }
        }

        if (rewrite.NewAuthorName is not null)
        {
            var actual = details?.AuthorName ?? candidate.Commit.AuthorName;
            if (!string.Equals(actual, rewrite.NewAuthorName, StringComparison.Ordinal))
                return false;
        }

        if (rewrite.NewAuthorEmail is not null)
        {
            var actual = details?.AuthorEmail ?? candidate.Commit.AuthorEmail;
            if (!string.Equals(actual, rewrite.NewAuthorEmail, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        if (rewrite.NewAuthorDate is not null)
        {
            var actual = details?.Date ?? candidate.Commit.Date;
            if (!SameMinute(actual, rewrite.NewAuthorDate.Value.DateTime))
                return false;
        }

        return true;
    }

    private static string? ResolvePreferredSelection(
        RewritePlan beforePlan,
        string? selectedOriginalSha,
        IReadOnlyDictionary<string, string> replacements,
        IReadOnlyList<VerificationCommit> refreshedLocalRows)
    {
        if (selectedOriginalSha is not null
            && replacements.TryGetValue(selectedOriginalSha, out var directReplacement))
        {
            return directReplacement;
        }

        if (selectedOriginalSha is not null)
        {
            var selectedAffected = beforePlan.Affected.FirstOrDefault(row =>
                string.Equals(row.FullSha, selectedOriginalSha, StringComparison.OrdinalIgnoreCase));
            if (selectedAffected is not null
                && selectedAffected.LocalIndex >= 0
                && selectedAffected.LocalIndex < refreshedLocalRows.Count)
            {
                return refreshedLocalRows[selectedAffected.LocalIndex].FullSha;
            }
        }

        var nearestRewritten = beforePlan.Affected
            .OrderBy(row => row.LocalIndex)
            .FirstOrDefault(row => row.LocalIndex >= 0 && row.LocalIndex < refreshedLocalRows.Count);
        return nearestRewritten is null ? refreshedLocalRows.FirstOrDefault()?.FullSha : refreshedLocalRows[nearestRewritten.LocalIndex].FullSha;
    }

    private static bool IsLocalBranchRow(CommitItem row)
        => row.RemoteState != CommitRemoteState.Incoming;

    private static bool IsLocalBranchInfo(CommitInfo commit)
        => commit.RemoteState != CommitRemoteState.Incoming;

    private static bool IsRemoteRewriteRisk(CommitItem row)
        => row.RemoteState == CommitRemoteState.OnRemote || row.IsOrphanedPair;

    private static string FullSha(CommitInfo commit)
        => string.IsNullOrWhiteSpace(commit.FullSha) ? commit.Sha : commit.FullSha;

    private static string FirstLine(string value)
    {
        var normalized = value.Replace("\r\n", "\n").Replace('\r', '\n');
        var first = normalized.Split('\n').FirstOrDefault() ?? string.Empty;
        return string.IsNullOrWhiteSpace(first) ? "(empty message)" : first;
    }

    private static string NormalizeMessage(string value)
        => value.Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd('\n');

    private static bool SameMinute(DateTime? left, DateTime right)
        => left.HasValue
        && left.Value.Year == right.Year
        && left.Value.Month == right.Month
        && left.Value.Day == right.Day
        && left.Value.Hour == right.Hour
        && left.Value.Minute == right.Minute;

    private static DateTimeOffset BuildRewriteDate(DateTime newDate, DateTime originalDate)
    {
        var offset = DateTimeOffset.Now.Offset;
        return new DateTimeOffset(
            newDate.Year,
            newDate.Month,
            newDate.Day,
            newDate.Hour,
            newDate.Minute,
            originalDate.Second,
            offset);
    }

    private static string ShortSha(string sha)
        => sha.Length >= 7 ? sha[..7] : sha;

    private static string Plural(int count)
        => count == 1 ? string.Empty : "s";

    private sealed record OriginalCommit(
        string Message,
        string AuthorName,
        string AuthorEmail,
        DateTime Date);

    private sealed record RewritePlan(
        IReadOnlyList<AffectedCommit> Affected,
        IReadOnlyList<AffectedCommit> Direct,
        bool HasRemoteRisk);

    private sealed record AffectedCommit(CommitItem Commit, int LocalIndex)
    {
        public string FullSha => Commit.FullSha;
    }

    private sealed record VerificationTarget(string OriginalSha, int LocalIndex, CommitRewrite Rewrite);

    private sealed record VerificationCommit(CommitInfo Commit, int LocalIndex, string FullSha);

    private sealed record ApplyVerificationResult(bool Success, string? Message, string? PreferredSelectionSha)
    {
        public static ApplyVerificationResult Passed(string? preferredSelectionSha)
            => new(true, null, preferredSelectionSha);

        public static ApplyVerificationResult Failed(string message)
            => new(false, message, null);
    }

    private sealed class CommitDraft
    {
        public CommitDraft(string sha, OriginalCommit original)
        {
            Sha = sha;
            OriginalMessage = original.Message;
            OriginalAuthorName = original.AuthorName;
            OriginalAuthorEmail = original.AuthorEmail;
            OriginalAuthorDate = original.Date;
            NewMessage = original.Message;
            NewAuthorName = original.AuthorName;
            NewAuthorEmail = original.AuthorEmail;
            NewAuthorDate = original.Date;
            UpdateCommitterTimestamp = true;
        }

        public string Sha { get; }
        public string OriginalMessage { get; private set; }
        public string OriginalAuthorName { get; private set; }
        public string OriginalAuthorEmail { get; private set; }
        public DateTime OriginalAuthorDate { get; private set; }
        public string NewMessage { get; set; }
        public string NewAuthorName { get; set; }
        public string NewAuthorEmail { get; set; }
        public DateTime? NewAuthorDate { get; set; }
        public bool UpdateCommitterTimestamp { get; set; }

        public bool HasMessageChange
            => !string.Equals(NormalizeMessage(NewMessage), NormalizeMessage(OriginalMessage), StringComparison.Ordinal);

        public bool HasAuthorChange
            => !string.Equals(NewAuthorName.Trim(), OriginalAuthorName, StringComparison.Ordinal)
            || !string.Equals(NewAuthorEmail.Trim(), OriginalAuthorEmail, StringComparison.OrdinalIgnoreCase);

        public bool HasTimeChange => NewAuthorDate.HasValue && !SameMinute(NewAuthorDate, OriginalAuthorDate);

        public bool HasChanges => HasMessageChange || HasAuthorChange || HasTimeChange;

        public void UpdateOriginalDetails(CommitDetails details, bool updatePendingIfUnchanged)
        {
            var oldMessage = OriginalMessage;
            var messageWasUnchanged = !HasMessageChange;
            OriginalMessage = details.Message;
            OriginalAuthorName = details.AuthorName;
            OriginalAuthorEmail = details.AuthorEmail;
            OriginalAuthorDate = details.Date;

            if (messageWasUnchanged && string.Equals(NewMessage, oldMessage, StringComparison.Ordinal))
                NewMessage = details.Message;
            else if (updatePendingIfUnchanged && string.Equals(NewMessage, oldMessage, StringComparison.Ordinal))
                NewMessage = details.Message;
        }

        public CommitRewrite ToRewrite()
        {
            DateTimeOffset? newAuthorDate = null;
            DateTimeOffset? newCommitterDate = null;
            if (HasTimeChange && NewAuthorDate.HasValue)
            {
                newAuthorDate = BuildRewriteDate(NewAuthorDate.Value, OriginalAuthorDate);
                if (UpdateCommitterTimestamp)
                    newCommitterDate = newAuthorDate;
            }

            return new CommitRewrite(
                Sha,
                NewAuthorName: HasAuthorChange ? NewAuthorName : null,
                NewAuthorEmail: HasAuthorChange ? NewAuthorEmail : null,
                NewAuthorDate: newAuthorDate,
                NewCommitterDate: newCommitterDate,
                NewMessage: HasMessageChange ? NewMessage : null);
        }
    }
}
