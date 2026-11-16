using CommunityToolkit.Mvvm.Input;

using Gitster.Services;
using Gitster.Core;
using Gitster.Core.Git;
using Gitster.Core.OperationsLog;

namespace Gitster.ViewModels;

public sealed partial class HistoryRewriteDraftViewModel : BaseViewModel, ICommitReorderer
{
    private readonly IGitBackend? _git;
    private readonly OperationFeedbackService? _feedback;
    private readonly OperationsLogService? _opsLog;
    private readonly SnapshotService? _snapshots;
    private readonly IWindowService? _windowService;
    private readonly Func<string?>? _getBranchName;
    private readonly Func<string?>? _getViewedRef;
    private readonly Func<string?, Task>? _refreshAfterApply;
    private readonly UiPreferencesService? _uiPreferences;
    private bool _syncCommitterWithAuthor;

    private readonly Dictionary<string, CommitDetails> _loadedDetails = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CommitDraft> _drafts = new(StringComparer.OrdinalIgnoreCase);
    private List<CommitItem> _commits = [];
    private List<CommitItem> _selectedCommits = [];
    private CommitItem? _selectedCommit;
    private string _messageText = string.Empty;
    private string _authorName = string.Empty;
    private string _authorEmail = string.Empty;
    private DateTime? _authorDatePart;
    private DateTime? _authorTimePart;
    private DateTime? _committerDatePart;
    private DateTime? _committerTimePart;
    private bool _isMessageMixed;
    private bool _isAuthorNameMixed;
    private bool _isAuthorEmailMixed;
    private bool _isAuthorDateMixed;
    private bool _isAuthorTimeMixed;
    private bool _isCommitterDateMixed;
    private bool _isCommitterTimeMixed;
    private bool _isLoadingDetails;
    private bool _isApplying;
    private bool _isSelectedCommitEditable;
    private string? _editBlockedReason;
    private bool _hasDrafts;
    private bool _hasRemoteRewriteRisk;
    private bool _hasLocalOnlyCleanup;
    private int _directEditCount;
    private int _transitiveRewriteCount;
    private int _affectedRewriteCount;
    private int _movedCount;

    // Pending reorder: the wanted order of all local commits, plus the original order it was
    // made against, so a reload with different history drops a stale order.
    private List<string>? _pendingOrder;
    private List<string>? _pendingOrderBase;
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
        RepositoryCommandContext commandContext,
        UiPreferencesService uiPreferences)
        : this(
            git,
            feedback,
            opsLog,
            snapshots,
            windowService,
            () => commandContext.CurrentBranch,
            commandContext.RefreshAfterHistoryRewrite,
            uiPreferences,
            () => commandContext.ViewedRef)
    {
    }

    public HistoryRewriteDraftViewModel(
        IGitBackend git,
        OperationFeedbackService feedback,
        OperationsLogService opsLog,
        SnapshotService snapshots,
        IWindowService windowService,
        Func<string?> getBranchName,
        Func<string?, Task> refreshAfterApply,
        UiPreferencesService? uiPreferences = null,
        Func<string?>? getViewedRef = null)
    {
        _git = git;
        _feedback = feedback;
        _opsLog = opsLog;
        _snapshots = snapshots;
        _windowService = windowService;
        _getBranchName = getBranchName;
        _getViewedRef = getViewedRef;
        _refreshAfterApply = refreshAfterApply;
        _uiPreferences = uiPreferences;
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
                ApplyToSelection((draft, v) => draft.NewMessage = v, value, () => IsMessageMixed = false);
        }
    }

    public string AuthorName
    {
        get => _authorName;
        set
        {
            if (SetProperty(ref _authorName, value))
                ApplyToSelection((draft, v) => draft.NewAuthorName = v.Trim(), value, () => IsAuthorNameMixed = false);
        }
    }

    public string AuthorEmail
    {
        get => _authorEmail;
        set
        {
            if (SetProperty(ref _authorEmail, value))
                ApplyToSelection((draft, v) => draft.NewAuthorEmail = v.Trim(), value, () => IsAuthorEmailMixed = false);
        }
    }

    /// <summary>Date part of the author timestamp; editing it never touches the per-commit time of day.</summary>
    public DateTime? AuthorDatePart
    {
        get => _authorDatePart;
        set
        {
            if (SetProperty(ref _authorDatePart, value))
            {
                ApplyToSelection(
                    (draft, v) =>
                    {
                        draft.NewAuthorDate = CombineDate(v, draft.EffectiveAuthorDate);
                        PullCommitterAlong(draft);
                    },
                    value,
                    () => IsAuthorDateMixed = false);
            }
        }
    }

    /// <summary>Time part of the author timestamp; the carrier date is ignored.</summary>
    public DateTime? AuthorTimePart
    {
        get => _authorTimePart;
        set
        {
            if (SetProperty(ref _authorTimePart, value))
            {
                ApplyToSelection(
                    (draft, v) =>
                    {
                        draft.NewAuthorDate = CombineTime(v, draft.EffectiveAuthorDate);
                        PullCommitterAlong(draft);
                    },
                    value,
                    () => IsAuthorTimeMixed = false);
            }
        }
    }

    public DateTime? CommitterDatePart
    {
        get => _committerDatePart;
        set
        {
            if (SetProperty(ref _committerDatePart, value))
            {
                ApplyToSelection(
                    (draft, v) => draft.NewCommitterDate = CombineDate(v, draft.EffectiveCommitterDate),
                    value,
                    () => IsCommitterDateMixed = false);
            }
        }
    }

    public DateTime? CommitterTimePart
    {
        get => _committerTimePart;
        set
        {
            if (SetProperty(ref _committerTimePart, value))
            {
                ApplyToSelection(
                    (draft, v) => draft.NewCommitterDate = CombineTime(v, draft.EffectiveCommitterDate),
                    value,
                    () => IsCommitterTimeMixed = false);
            }
        }
    }

    public bool IsMessageMixed
    {
        get => _isMessageMixed;
        private set => SetProperty(ref _isMessageMixed, value);
    }

    public bool IsAuthorNameMixed
    {
        get => _isAuthorNameMixed;
        private set => SetProperty(ref _isAuthorNameMixed, value);
    }

    public bool IsAuthorEmailMixed
    {
        get => _isAuthorEmailMixed;
        private set => SetProperty(ref _isAuthorEmailMixed, value);
    }

    public bool IsAuthorDateMixed
    {
        get => _isAuthorDateMixed;
        private set => SetProperty(ref _isAuthorDateMixed, value);
    }

    public bool IsAuthorTimeMixed
    {
        get => _isAuthorTimeMixed;
        private set => SetProperty(ref _isAuthorTimeMixed, value);
    }

    public bool IsCommitterDateMixed
    {
        get => _isCommitterDateMixed;
        private set => SetProperty(ref _isCommitterDateMixed, value);
    }

    public bool IsCommitterTimeMixed
    {
        get => _isCommitterTimeMixed;
        private set => SetProperty(ref _isCommitterTimeMixed, value);
    }

    public int SelectedCommitCount => _selectedCommits.Count;

    public bool IsMultiSelection => _selectedCommits.Count > 1;

    /// <summary>Header suffix that only appears while more than one commit is selected.</summary>
    public string SelectionSuffix => IsMultiSelection ? $" · {SelectedCommitCount} commits" : string.Empty;

    /// <summary>
    /// While on, every author timestamp edit drags the committer timestamp with it. Persisted in the UI settings.
    /// </summary>
    public bool SyncCommitterWithAuthor
    {
        get => _uiPreferences?.SyncCommitterWithAuthorDate ?? _syncCommitterWithAuthor;
        set
        {
            if (value == SyncCommitterWithAuthor)
                return;

            if (_uiPreferences is not null)
                _uiPreferences.SyncCommitterWithAuthorDate = value;
            else
                _syncCommitterWithAuthor = value;

            OnPropertyChanged();
            OnPropertyChanged(nameof(IsCommitterTimestampEditable));

            // Turning it on immediately brings the current selection in line.
            if (value)
                SyncCommitterDate();
        }
    }

    /// <summary>The committer timestamp is only hand-editable while it does not follow the author timestamp.</summary>
    public bool IsCommitterTimestampEditable => !SyncCommitterWithAuthor;

    private void PullCommitterAlong(CommitDraft draft)
    {
        if (SyncCommitterWithAuthor)
            draft.NewCommitterDate = draft.EffectiveAuthorDate;
    }

    /// <summary>Drops everything below the subject line — per commit, so each keeps its own subject.</summary>
    [RelayCommand]
    private void KeepFirstMessageLine()
    {
        if (!IsSelectedCommitEditable)
            return;

        foreach (var commit in _selectedCommits)
        {
            var draft = GetOrCreateDraft(commit);
            draft.NewMessage = FirstMessageLine(draft.NewMessage);
        }

        DropUnchangedDrafts();
        ProjectEditorFromSelection();
        RefreshProjection();
    }

    /// <summary>Takes the author timestamp over into the committer timestamp for every selected commit.</summary>
    [RelayCommand]
    private void SyncCommitterDate()
    {
        if (!IsSelectedCommitEditable)
            return;

        foreach (var commit in _selectedCommits)
        {
            var draft = GetOrCreateDraft(commit);
            draft.NewCommitterDate = draft.EffectiveAuthorDate;
        }

        DropUnchangedDrafts();
        ProjectEditorFromSelection();
        RefreshProjection();
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

    /// <summary>Why the shown history can't be edited (remote branch or tag), or null when it can.</summary>
    public string? EditBlockedReason
    {
        get => _editBlockedReason;
        private set
        {
            if (SetProperty(ref _editBlockedReason, value))
                OnPropertyChanged(nameof(IsEditBlocked));
        }
    }

    public bool IsEditBlocked => EditBlockedReason is not null;

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

    public int MovedCount
    {
        get => _movedCount;
        private set => SetProperty(ref _movedCount, value);
    }

    /// <summary>The wanted order of all local commits (newest first), or null without a reorder.</summary>
    public IReadOnlyList<string>? PendingOrder => _pendingOrder;

    /// <summary>Raised when the pending order changes, so the commit list can show it.</summary>
    public event Action<IReadOnlyList<string>?>? PendingOrderChanged;

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

    public bool CanApply => HasDrafts && !IsApplying && !IsEditBlocked;
    public bool CanDiscardSelected => _selectedCommits.Any(c => _drafts.ContainsKey(c.FullSha) || c.HasPendingMove);

    public void SetCommits(IEnumerable<CommitItem> commits)
    {
        foreach (var row in _commits)
            row.ClearHistoryEditOverlay();

        _commits = commits.ToList();
        RemoveMissingDrafts();
        if (_pendingOrderBase is not null && !_pendingOrderBase.SequenceEqual(LocalShas(), StringComparer.OrdinalIgnoreCase))
            SetPendingOrder(null, notify: true);
        RefreshProjection();
    }

    // ── Reorder ──────────────────────────────────────────────────────────

    /// <summary>Moves the given commits one position up (delta -1, newer) or down (+1, older).</summary>
    public string? MoveCommitsBy(IReadOnlyList<CommitItem> commits, int delta)
    {
        if (delta == 0)
            return null;

        var order = CurrentOrder();
        var selected = SelectedShasIn(order, commits);
        if (selected.Count == 0)
            return "Only commits of the local branch can be moved.";

        if (delta < 0)
        {
            for (var i = 1; i < order.Count; i++)
                if (selected.Contains(order[i]) && !selected.Contains(order[i - 1]))
                    (order[i - 1], order[i]) = (order[i], order[i - 1]);
        }
        else
        {
            for (var i = order.Count - 2; i >= 0; i--)
                if (selected.Contains(order[i]) && !selected.Contains(order[i + 1]))
                    (order[i + 1], order[i]) = (order[i], order[i + 1]);
        }

        return TrySetOrder(order);
    }

    /// <summary>Checks a drag-and-drop move without changing anything; null means allowed.</summary>
    public string? CanMoveCommitsTo(IReadOnlyList<CommitItem> commits, CommitItem target, bool after)
        => BuildMovedOrder(commits, target, after, out var order) ?? ValidateOrder(order!);

    /// <summary>Places the given commits directly above (newer) or below (after) the target.</summary>
    public string? MoveCommitsTo(IReadOnlyList<CommitItem> commits, CommitItem target, bool after)
        => BuildMovedOrder(commits, target, after, out var order) ?? TrySetOrder(order!);

    private string? BuildMovedOrder(IReadOnlyList<CommitItem> commits, CommitItem target, bool after, out List<string>? order)
    {
        order = null;
        if (IsApplying)
            return "A history rewrite is being applied.";

        var current = CurrentOrder();
        var selected = SelectedShasIn(current, commits);
        if (selected.Count == 0)
            return "Only commits of the local branch can be moved.";
        if (selected.Contains(target.FullSha))
            return "Drop onto a commit outside the moved selection.";

        var targetIndex = current.FindIndex(sha => string.Equals(sha, target.FullSha, StringComparison.OrdinalIgnoreCase));
        if (targetIndex < 0)
            return "Commits can only be moved within the local branch.";

        var moving = current.Where(selected.Contains).ToList();
        var rest = current.Where(sha => !selected.Contains(sha)).ToList();
        var insertAt = rest.FindIndex(sha => string.Equals(sha, target.FullSha, StringComparison.OrdinalIgnoreCase)) + (after ? 1 : 0);
        rest.InsertRange(insertAt, moving);
        order = rest;
        return null;
    }

    private string? TrySetOrder(List<string> order)
    {
        if (ValidateOrder(order) is { } error)
            return error;

        SetPendingOrder(order.SequenceEqual(LocalShas(), StringComparer.OrdinalIgnoreCase) ? null : order, notify: true);
        RefreshProjection();
        return null;
    }

    /// <summary>Null when the order can be applied; otherwise the reason it cannot.</summary>
    private string? ValidateOrder(IReadOnlyList<string> order)
    {
        if (IsApplying)
            return "A history rewrite is being applied.";
        if (IsEditBlocked)
            return EditBlockedReason;

        var localRows = _commits.Where(IsLocalBranchRow).ToList();
        var oldestChanged = -1;
        for (var i = localRows.Count - 1; i >= 0; i--)
        {
            if (!string.Equals(localRows[i].FullSha, order[i], StringComparison.OrdinalIgnoreCase))
            {
                oldestChanged = i;
                break;
            }
        }

        if (oldestChanged < 0)
            return null;

        var merge = localRows.Take(oldestChanged + 1).FirstOrDefault(c => c.ParentShas.Count > 1);
        if (merge is not null)
            return $"Commits cannot be moved across merge commit {merge.CommitId}.";

        if (localRows[oldestChanged].ParentShas.Count == 0)
            return "The root commit cannot be moved.";

        return null;
    }

    private void SetPendingOrder(List<string>? order, bool notify)
    {
        _pendingOrder = order;
        _pendingOrderBase = order is null ? null : LocalShas();
        if (notify)
            PendingOrderChanged?.Invoke(_pendingOrder);
    }

    private List<string> CurrentOrder() => (_pendingOrder ?? LocalShas()).ToList();

    private static HashSet<string> SelectedShasIn(IReadOnlyList<string> order, IReadOnlyList<CommitItem> commits)
    {
        var inOrder = order.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return commits
            .Select(c => c.FullSha)
            .Where(inOrder.Contains)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public void SetSelectedCommit(CommitItem? commit)
    {
        SelectedCommit = commit;

        // A single-selection update also defines the editable set unless a wider multi-selection
        // already contains this commit (the list view raises both notifications).
        if (commit is null)
            ApplySelection([]);
        else if (!_selectedCommits.Any(c => string.Equals(c.FullSha, commit.FullSha, StringComparison.OrdinalIgnoreCase)))
            ApplySelection([commit]);
        else
            RefreshSummary();
    }

    public void SetSelectedCommits(IReadOnlyList<CommitItem> commits) => ApplySelection(commits);

    /// <summary>Re-evaluates whether the shown history can be edited; call when the viewed branch changes.</summary>
    public void RefreshEditTarget()
    {
        UpdateEditability();
        OnPropertyChanged(nameof(CanApply));
    }

    private void UpdateEditability()
    {
        EditBlockedReason = ResolveRewriteTarget(_getViewedRef?.Invoke(), _getBranchName?.Invoke()).BlockReason;
        IsSelectedCommitEditable = !IsEditBlocked
            && _selectedCommits.Count > 0
            && _selectedCommits.All(c => c.RemoteState != CommitRemoteState.Incoming);
    }

    private void ApplySelection(IReadOnlyList<CommitItem> commits)
    {
        _selectionVersion++;
        _selectedCommits = commits.ToList();
        _editorTouched = false;
        UpdateEditability();

        OnPropertyChanged(nameof(SelectedCommitCount));
        OnPropertyChanged(nameof(IsMultiSelection));
        OnPropertyChanged(nameof(SelectionSuffix));
        OnPropertyChanged(nameof(CanDiscardSelected));

        ProjectEditorFromSelection();

        if (_git is not null && _selectedCommits.Count > 0)
            _ = LoadDetailsAsync(_selectedCommits.ToList(), _selectionVersion);

        RefreshSummary();
    }

    [RelayCommand]
    private void ResetSelected()
    {
        if (_selectedCommits.Count == 0)
            return;

        foreach (var commit in _selectedCommits)
            _drafts.Remove(commit.FullSha);

        // A move cannot be undone for one commit alone (the others shifted with it), so
        // resetting a moved commit drops the whole pending order.
        if (_selectedCommits.Any(c => c.HasPendingMove))
            SetPendingOrder(null, notify: true);

        ProjectEditorFromSelection();
        RefreshProjection();
    }

    [RelayCommand]
    private void ClearAll()
    {
        _drafts.Clear();
        if (_pendingOrder is not null)
            SetPendingOrder(null, notify: true);
        ProjectEditorFromSelection();
        RefreshProjection();
    }

    [RelayCommand]
    private async Task Apply()
    {
        if (!CanApply || _git is null || _feedback is null || _opsLog is null)
            return;

        var rewrites = BuildRewrites();
        var orderPrefix = BuildRewritePlan().OrderPrefix;
        if (rewrites.Count == 0 && orderPrefix is null)
            return;

        var target = ResolveRewriteTarget(_getViewedRef?.Invoke(), _getBranchName?.Invoke());
        if (target.BlockReason is not null)
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
            var beforeSha = await GetTargetTipShaAsync(target);
            var branchName = target.BranchName ?? string.Empty;
            var directCount = DirectEditCount;
            var transitiveCount = TransitiveRewriteCount;
            var movedCount = MovedCount;
            var remoteRisk = HasRemoteRewriteRisk;

            if (_snapshots is not null)
                await _snapshots.CaptureAsync(_git, "Before batch history edit");

            var afterSha = await _feedback.RunAsync(
                "Rewrite history",
                async () =>
                {
                    if (orderPrefix is null)
                        await Task.Run(() => _git.RewriteCommitsAsync(rewrites, branchName));
                    else
                        await Task.Run(() => _git.RewriteHistoryAsync(rewrites, orderPrefix, branchName));
                    return await GetTargetTipShaAsync(target);
                },
                ShortSha);

            var verification = await VerifyApplyAsync(rewritePlan, selectedOriginalSha, target);
            if (!verification.Success)
            {
                _windowService?.Error(
                    $"Batch history edit could not be verified:\n{verification.Message}\n\nYour pending edits are still visible.",
                    "Gitster");
                if (_refreshAfterApply is not null)
                    await _refreshAfterApply(verification.PreferredSelectionSha);
                return;
            }

            // Undo resets the checked-out branch, so only rewrites of that branch get an undoable record.
            if (target.IsCheckedOut)
            {
                string? reflogSelector = null;
                try { reflogSelector = await _git.GetReflogSelectorForHeadAsync(); }
                catch { }

                await _opsLog.RecordAsync(new OperationRecord(
                    Id: Guid.NewGuid().ToString(),
                    Timestamp: DateTimeOffset.Now,
                    Kind: OperationKind.HistoryEdit,
                    Description: remoteRisk
                        ? $"Batch history edit ({directCount} edit{Plural(directCount)}{MovedSuffix(movedCount)}, {transitiveCount} local descendant rewrite{Plural(transitiveCount)}, force-with-lease required)"
                        : $"Batch history edit ({directCount} edit{Plural(directCount)}{MovedSuffix(movedCount)}, {transitiveCount} local descendant rewrite{Plural(transitiveCount)})",
                    BranchName: branchName,
                    BeforeSha: beforeSha,
                    AfterSha: afterSha,
                    ReflogSelector: reflogSelector,
                    Status: OperationStatus.Active));
            }

            _drafts.Clear();
            SetPendingOrder(null, notify: true);
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

    private async Task LoadDetailsAsync(IReadOnlyList<CommitItem> commits, int version)
    {
        if (_git is null || commits.Count == 0)
            return;

        IsLoadingDetails = true;
        try
        {
            foreach (var commit in commits)
            {
                CommitDetails details;
                try
                {
                    details = await Task.Run(async () => await _git.GetCommitAsync(commit.FullSha));
                }
                catch
                {
                    // The short-row metadata is still enough to edit basic fields.
                    continue;
                }

                if (version != _selectionVersion)
                    return;

                _loadedDetails[commit.FullSha] = details;

                if (_drafts.TryGetValue(commit.FullSha, out var draft))
                    draft.UpdateOriginalDetails(details, updatePendingIfUnchanged: !_editorTouched);
            }

            if (version != _selectionVersion)
                return;

            if (!_editorTouched)
                ProjectEditorFromSelection();

            RefreshProjection();
        }
        finally
        {
            if (version == _selectionVersion)
                IsLoadingDetails = false;
        }
    }

    /// <summary>
    /// Projects the editor fields over the whole selection: a field shows its value when every selected
    /// commit agrees, otherwise it is cleared and flagged as mixed.
    /// </summary>
    private void ProjectEditorFromSelection()
    {
        _syncingEditor = true;
        try
        {
            if (_selectedCommits.Count == 0)
            {
                MessageText = string.Empty;
                AuthorName = string.Empty;
                AuthorEmail = string.Empty;
                AuthorDatePart = null;
                AuthorTimePart = null;
                CommitterDatePart = null;
                CommitterTimePart = null;
                IsMessageMixed = false;
                IsAuthorNameMixed = false;
                IsAuthorEmailMixed = false;
                IsAuthorDateMixed = false;
                IsAuthorTimeMixed = false;
                IsCommitterDateMixed = false;
                IsCommitterTimeMixed = false;
                return;
            }

            var values = _selectedCommits.Select(GetEffectiveValues).ToList();

            IsMessageMixed = !AllEqual(values, v => NormalizeMessage(v.Message), StringComparer.Ordinal);
            MessageText = IsMessageMixed ? string.Empty : values[0].Message;

            IsAuthorNameMixed = !AllEqual(values, v => v.AuthorName, StringComparer.Ordinal);
            AuthorName = IsAuthorNameMixed ? string.Empty : values[0].AuthorName;

            IsAuthorEmailMixed = !AllEqual(values, v => v.AuthorEmail, StringComparer.OrdinalIgnoreCase);
            AuthorEmail = IsAuthorEmailMixed ? string.Empty : values[0].AuthorEmail;

            IsAuthorDateMixed = !AllEqual(values, v => v.AuthorDate.Date, EqualityComparer<DateTime>.Default);
            AuthorDatePart = IsAuthorDateMixed ? null : values[0].AuthorDate.Date;

            IsAuthorTimeMixed = !AllEqual(values, v => MinuteOfDay(v.AuthorDate), EqualityComparer<int>.Default);
            AuthorTimePart = IsAuthorTimeMixed ? null : AsTimeCarrier(values[0].AuthorDate);

            IsCommitterDateMixed = !AllEqual(values, v => v.CommitterDate.Date, EqualityComparer<DateTime>.Default);
            CommitterDatePart = IsCommitterDateMixed ? null : values[0].CommitterDate.Date;

            IsCommitterTimeMixed = !AllEqual(values, v => MinuteOfDay(v.CommitterDate), EqualityComparer<int>.Default);
            CommitterTimePart = IsCommitterTimeMixed ? null : AsTimeCarrier(values[0].CommitterDate);
        }
        finally
        {
            _syncingEditor = false;
        }

        OnPropertyChanged(nameof(CanDiscardSelected));
    }

    /// <summary>Writes a single edited field into every selected commit, leaving all other fields untouched.</summary>
    private void ApplyToSelection<T>(Action<CommitDraft, T> apply, T value, Action clearMixedFlag)
    {
        if (_syncingEditor || _selectedCommits.Count == 0)
            return;

        _editorTouched = true;

        if (!IsSelectedCommitEditable)
            return;

        foreach (var commit in _selectedCommits)
            apply(GetOrCreateDraft(commit), value);

        clearMixedFlag();
        DropUnchangedDrafts();

        // Fields the edit changed indirectly (a pulled-along committer timestamp) must be re-read.
        ProjectEditorFromSelection();
        RefreshProjection();
    }

    private void DropUnchangedDrafts()
    {
        foreach (var sha in _drafts.Where(pair => !pair.Value.HasChanges).Select(pair => pair.Key).ToList())
            _drafts.Remove(sha);
    }

    private EffectiveValues GetEffectiveValues(CommitItem commit)
    {
        var original = GetOriginal(commit);
        if (!_drafts.TryGetValue(commit.FullSha, out var draft))
            return new EffectiveValues(original.Message, original.AuthorName, original.AuthorEmail, original.Date, original.CommitterDate);

        return new EffectiveValues(
            draft.NewMessage,
            draft.NewAuthorName,
            draft.NewAuthorEmail,
            draft.EffectiveAuthorDate,
            draft.EffectiveCommitterDate);
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
                details.Date,
                details.CommitterDate ?? details.Date);
        }

        return new OriginalCommit(
            commit.Message,
            commit.AuthorName,
            commit.AuthorEmail,
            commit.Date,
            commit.CommitterDate ?? commit.Date);
    }

    private void RefreshProjection()
    {
        RemoveMissingDrafts();

        foreach (var row in _commits)
            row.ClearHistoryEditOverlay();

        var plan = BuildRewritePlan();
        if (plan.Direct.Count == 0 && plan.Moved.Count == 0)
        {
            MovedCount = 0;
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
        var moved = plan.Moved.ToDictionary(m => m.FullSha, StringComparer.OrdinalIgnoreCase);
        var remoteRisk = plan.HasRemoteRisk;

        foreach (var affected in plan.Affected)
        {
            var row = affected.Commit;
            row.IsPendingRemoteRisk = IsRemoteRewriteRisk(row);
            row.IsPendingLocalCleanup = !remoteRisk;

            var draft = direct.Contains(row.FullSha) && _drafts.TryGetValue(row.FullSha, out var found) ? found : null;
            var move = moved.GetValueOrDefault(row.FullSha);
            if (draft is not null || move is not null)
            {
                row.IsHistoryEditDirect = true;
                row.HasPendingMove = move is not null;
                if (draft is not null)
                {
                    row.HasPendingMessageChange = draft.HasMessageChange;
                    row.HasPendingAuthorChange = draft.HasAuthorChange;
                    row.HasPendingTimeChange = draft.HasTimeChange || draft.HasCommitterTimeChange;
                    row.PendingMessage = draft.HasMessageChange ? FirstLine(draft.NewMessage) : null;
                    row.PendingAuthorName = draft.HasAuthorChange ? draft.NewAuthorName : null;
                    row.PendingAuthorEmail = draft.HasAuthorChange ? draft.NewAuthorEmail : null;
                    row.PendingDate = draft.HasTimeChange ? draft.NewAuthorDate : null;
                    row.PendingCommitterDate = draft.HasCommitterTimeChange ? draft.NewCommitterDate : null;
                }

                row.HistoryEditTooltip = BuildDirectTooltip(draft, move, remoteRisk);
            }
            else
            {
                row.IsHistoryEditTransitive = true;
                row.HistoryEditTooltip = remoteRisk
                    ? "Local descendant will be rewritten because an older parent changes. Remote contains old copies."
                    : "Will be rewritten because an older parent changes.";
            }
        }

        var directOrMoved = direct.Union(moved.Keys, StringComparer.OrdinalIgnoreCase).Count();
        MovedCount = plan.Moved.Count;
        DirectEditCount = directOrMoved;
        TransitiveRewriteCount = plan.Affected.Count - directOrMoved;
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

        if (_selectedCommits.Any(c => c.RemoteState == CommitRemoteState.Incoming))
        {
            SummaryText = "Incoming commits must be pulled or cherry-picked before they can be edited.";
            return;
        }

        var selectionPrefix = IsMultiSelection
            ? $"{SelectedCommitCount} commits selected. "
            : string.Empty;

        if (!HasDrafts)
        {
            SummaryText = _selectedCommits.Count == 0
                ? "Select a commit to edit history."
                : selectionPrefix + "No pending history edits.";
            return;
        }

        var direct = MovedCount > 0
            ? $"{DirectEditCount} edit{Plural(DirectEditCount)} ({MovedCount} moved)"
            : $"{DirectEditCount} edit{Plural(DirectEditCount)}";
        var transitive = TransitiveRewriteCount == 0
            ? "no local descendant rewrites"
            : $"{TransitiveRewriteCount} local descendant rewrite{Plural(TransitiveRewriteCount)}";
        SummaryText = HasRemoteRewriteRisk
            ? $"{selectionPrefix}{direct}, {transitive}. Remote contains old copies: force-with-lease required."
            : $"{selectionPrefix}{direct}, {transitive}. Local cleanup.";
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

    private static string BuildDirectTooltip(CommitDraft? draft, AffectedCommit? move, bool remoteRisk)
    {
        var parts = new List<string>();
        if (move is not null) parts.Add($"position {move.LocalIndex + 1} → {move.NewIndex + 1}");
        if (draft?.HasMessageChange == true) parts.Add("message");
        if (draft?.HasAuthorChange == true) parts.Add("author");
        if (draft?.HasTimeChange == true) parts.Add("time");
        if (draft?.HasCommitterTimeChange == true) parts.Add("committer time");
        var suffix = remoteRisk ? " Remote contains old copies." : string.Empty;
        return $"Pending edit: {string.Join(", ", parts)}.{suffix}";
    }

    private RewritePlan BuildRewritePlan()
    {
        var localCommits = _commits.Where(IsLocalBranchRow).ToList();
        var newIndexBySha = (_pendingOrder ?? localCommits.Select(c => c.FullSha).ToList())
            .Select((sha, index) => (sha, index))
            .ToDictionary(x => x.sha, x => x.index, StringComparer.OrdinalIgnoreCase);

        var localRows = localCommits
            .Select((commit, index) => new AffectedCommit(
                commit,
                index,
                newIndexBySha.TryGetValue(commit.FullSha, out var newIndex) ? newIndex : index))
            .ToList();

        var direct = localRows
            .Where(row => _drafts.TryGetValue(row.FullSha, out var draft) && draft.HasChanges)
            .ToList();

        var oldestMovedIndex = OldestChangedIndex();
        var moved = oldestMovedIndex < 0 ? new List<AffectedCommit>() : FindMovedRows(localRows, oldestMovedIndex);

        if (direct.Count == 0 && moved.Count == 0)
            return new RewritePlan([], [], [], null, false);

        var oldestAffectedIndex = Math.Max(
            direct.Count == 0 ? -1 : direct.Max(row => row.LocalIndex),
            oldestMovedIndex);
        var affected = localRows
            .Where(row => row.LocalIndex <= oldestAffectedIndex)
            .OrderBy(row => row.NewIndex)
            .ToList();

        var orderPrefix = oldestMovedIndex < 0 ? null : _pendingOrder!.Take(oldestMovedIndex + 1).ToList();
        var hasRemoteRisk = affected.Any(row => IsRemoteRewriteRisk(row.Commit));
        return new RewritePlan(affected, direct, moved, orderPrefix, hasRemoteRisk);
    }

    /// <summary>
    /// Determines which local branch a batch edit rewrites: the branch shown in the history, or the checked-out
    /// branch when the current branch or all branches are shown. Remote branches and tags cannot be rewritten.
    /// </summary>
    internal static RewriteTarget ResolveRewriteTarget(string? viewedRef, string? currentBranch)
    {
        const string localPrefix = "refs/heads/";

        if (string.IsNullOrWhiteSpace(viewedRef))
            return new RewriteTarget(currentBranch, RefName: null, IsCheckedOut: true, BlockReason: null);

        if (!viewedRef.StartsWith(localPrefix, StringComparison.Ordinal))
        {
            return new RewriteTarget(
                BranchName: null,
                RefName: null,
                IsCheckedOut: false,
                BlockReason: "Remote branches and tags can't be edited. Check out or create a local branch to edit its history.");
        }

        var branchName = viewedRef[localPrefix.Length..];
        var isCheckedOut = string.Equals(branchName, currentBranch, StringComparison.Ordinal);
        return new RewriteTarget(branchName, viewedRef, isCheckedOut, BlockReason: null);
    }

    private async Task<string> GetTargetTipShaAsync(RewriteTarget target)
    {
        if (_git is null)
            return string.Empty;

        if (target.IsCheckedOut || target.RefName is null)
            return await _git.GetHeadShaAsync();

        var tip = await Task.Run(async () => await _git.GetCommitsForRefAsync(target.RefName, 1));
        return tip.FirstOrDefault()?.FullSha ?? string.Empty;
    }

    /// <summary>
    /// The commits the user actually moved: everything in the reordered window that is not part
    /// of the longest run keeping its original relative order. Swapping two neighbours marks one,
    /// dragging one commit far up marks only that one, not every commit it passed.
    /// </summary>
    private static List<AffectedCommit> FindMovedRows(IReadOnlyList<AffectedCommit> localRows, int oldestChangedIndex)
    {
        var window = localRows
            .Where(row => row.LocalIndex <= oldestChangedIndex)
            .OrderBy(row => row.NewIndex)
            .ToList();

        // Longest increasing subsequence of the original indices, in new order (patience sort).
        var tails = new List<int>();
        var tailIndex = new List<int>();
        var previous = new int[window.Count];
        for (var i = 0; i < window.Count; i++)
        {
            var value = window[i].LocalIndex;
            var position = tails.BinarySearch(value);
            if (position < 0) position = ~position;
            previous[i] = position > 0 ? tailIndex[position - 1] : -1;
            if (position == tails.Count)
            {
                tails.Add(value);
                tailIndex.Add(i);
            }
            else
            {
                tails[position] = value;
                tailIndex[position] = i;
            }
        }

        var kept = new HashSet<int>();
        for (var i = tailIndex.Count == 0 ? -1 : tailIndex[^1]; i >= 0; i = previous[i])
            kept.Add(i);

        return window.Where((_, i) => !kept.Contains(i)).ToList();
    }

    /// <summary>Original index of the oldest local commit whose position changes, or -1.</summary>
    private int OldestChangedIndex()
    {
        if (_pendingOrder is null)
            return -1;

        var original = LocalShas();
        for (var i = original.Count - 1; i >= 0; i--)
        {
            if (!string.Equals(original[i], _pendingOrder[i], StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }

    private List<string> LocalShas() => _commits.Where(IsLocalBranchRow).Select(c => c.FullSha).ToList();

    private async Task<ApplyVerificationResult> VerifyApplyAsync(
        RewritePlan beforePlan,
        string? selectedOriginalSha,
        RewriteTarget rewriteTarget)
    {
        if (_git is null)
            return ApplyVerificationResult.Failed("Git backend is unavailable.");

        // Positions after the rewrite: a reorder moves commits, so compare against NewIndex.
        var directTargets = beforePlan.Direct
            .Select(row => new VerificationTarget(
                row.Commit.FullSha,
                row.NewIndex,
                _drafts[row.Commit.FullSha].ToRewrite()))
            .ToList();

        if (directTargets.Count == 0 && beforePlan.Moved.Count == 0)
            return ApplyVerificationResult.Failed("No direct edits were available to verify.");

        var refreshed = rewriteTarget.IsCheckedOut || rewriteTarget.RefName is null
            ? await Task.Run(async () => await _git.GetCommitsAsync())
            : await Task.Run(async () => await _git.GetCommitsForRefAsync(rewriteTarget.RefName, int.MaxValue));
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

        if (rewrite.NewCommitterDate is not null)
        {
            var actual = details?.CommitterDate ?? candidate.Commit.CommitterDate;
            if (actual is not null && !SameMinute(actual, rewrite.NewCommitterDate.Value.DateTime))
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
                && selectedAffected.NewIndex >= 0
                && selectedAffected.NewIndex < refreshedLocalRows.Count)
            {
                return refreshedLocalRows[selectedAffected.NewIndex].FullSha;
            }
        }

        var nearestRewritten = beforePlan.Affected
            .OrderBy(row => row.NewIndex)
            .FirstOrDefault(row => row.NewIndex >= 0 && row.NewIndex < refreshedLocalRows.Count);
        return nearestRewritten is null ? refreshedLocalRows.FirstOrDefault()?.FullSha : refreshedLocalRows[nearestRewritten.NewIndex].FullSha;
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

    /// <summary>Subject line of a message, keeping it verbatim (no placeholder for empty messages).</summary>
    private static string FirstMessageLine(string value)
        => NormalizeMessage(value).Split('\n').FirstOrDefault()?.TrimEnd() ?? string.Empty;

    private static string NormalizeMessage(string value)
        => value.Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd('\n');

    private static bool SameMinute(DateTime? left, DateTime right)
        => left.HasValue
        && left.Value.Year == right.Year
        && left.Value.Month == right.Month
        && left.Value.Day == right.Day
        && left.Value.Hour == right.Hour
        && left.Value.Minute == right.Minute;

    private static bool AllEqual<TKey>(
        IReadOnlyList<EffectiveValues> values,
        Func<EffectiveValues, TKey> selector,
        IEqualityComparer<TKey> comparer)
        => values.All(v => comparer.Equals(selector(v), selector(values[0])));

    private static int MinuteOfDay(DateTime value) => (value.Hour * 60) + value.Minute;

    /// <summary>Time-only inputs need a carrier date; only the time of day is ever read back.</summary>
    private static DateTime AsTimeCarrier(DateTime value)
        => new(2000, 1, 1, value.Hour, value.Minute, 0);

    private static DateTime? CombineDate(DateTime? date, DateTime current)
        => date is null ? current : date.Value.Date + current.TimeOfDay;

    private static DateTime? CombineTime(DateTime? time, DateTime current)
        => time is null ? current : current.Date.AddMinutes(MinuteOfDay(time.Value));

    private static string ShortSha(string sha) => GitSha.Short(sha);

    private static string Plural(int count)
        => count == 1 ? string.Empty : "s";

    private static string MovedSuffix(int movedCount)
        => movedCount > 0 ? $", {movedCount} moved" : string.Empty;

    private sealed record EffectiveValues(
        string Message,
        string AuthorName,
        string AuthorEmail,
        DateTime AuthorDate,
        DateTime CommitterDate);

    private sealed record OriginalCommit(
        string Message,
        string AuthorName,
        string AuthorEmail,
        DateTime Date,
        DateTime CommitterDate);

    /// <summary>
    /// <paramref name="Direct"/> holds metadata edits, <paramref name="Moved"/> the commits that
    /// change position; <paramref name="OrderPrefix"/> is the wanted order of the newest commits
    /// (original SHAs) down to the oldest one whose position changes, or null without a reorder.
    /// </summary>
    private sealed record RewritePlan(
        IReadOnlyList<AffectedCommit> Affected,
        IReadOnlyList<AffectedCommit> Direct,
        IReadOnlyList<AffectedCommit> Moved,
        IReadOnlyList<string>? OrderPrefix,
        bool HasRemoteRisk);

    /// <summary>A local commit with its index before and after the pending reorder.</summary>
    private sealed record AffectedCommit(CommitItem Commit, int LocalIndex, int NewIndex)
    {
        public string FullSha => Commit.FullSha;
    }

    private sealed record VerificationTarget(string OriginalSha, int LocalIndex, CommitRewrite Rewrite);

    internal sealed record RewriteTarget(string? BranchName, string? RefName, bool IsCheckedOut, string? BlockReason);

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
            OriginalCommitterDate = original.CommitterDate;
            NewMessage = original.Message;
            NewAuthorName = original.AuthorName;
            NewAuthorEmail = original.AuthorEmail;
            NewAuthorDate = original.Date;
            NewCommitterDate = original.CommitterDate;
        }

        public string Sha { get; }
        public string OriginalMessage { get; private set; }
        public string OriginalAuthorName { get; private set; }
        public string OriginalAuthorEmail { get; private set; }
        public DateTime OriginalAuthorDate { get; private set; }
        public DateTime OriginalCommitterDate { get; private set; }
        public string NewMessage { get; set; }
        public string NewAuthorName { get; set; }
        public string NewAuthorEmail { get; set; }
        public DateTime? NewAuthorDate { get; set; }
        public DateTime? NewCommitterDate { get; set; }

        public DateTime EffectiveAuthorDate => NewAuthorDate ?? OriginalAuthorDate;
        public DateTime EffectiveCommitterDate => NewCommitterDate ?? OriginalCommitterDate;

        public bool HasMessageChange
            => !string.Equals(NormalizeMessage(NewMessage), NormalizeMessage(OriginalMessage), StringComparison.Ordinal);

        public bool HasAuthorChange
            => !string.Equals(NewAuthorName.Trim(), OriginalAuthorName, StringComparison.Ordinal)
            || !string.Equals(NewAuthorEmail.Trim(), OriginalAuthorEmail, StringComparison.OrdinalIgnoreCase);

        public bool HasTimeChange => NewAuthorDate.HasValue && !SameMinute(NewAuthorDate, OriginalAuthorDate);

        public bool HasCommitterTimeChange => NewCommitterDate.HasValue && !SameMinute(NewCommitterDate, OriginalCommitterDate);

        public bool HasChanges => HasMessageChange || HasAuthorChange || HasTimeChange || HasCommitterTimeChange;

        public void UpdateOriginalDetails(CommitDetails details, bool updatePendingIfUnchanged)
        {
            var oldMessage = OriginalMessage;
            var messageWasUnchanged = !HasMessageChange;
            var committerWasUnchanged = !HasCommitterTimeChange;
            OriginalMessage = details.Message;
            OriginalAuthorName = details.AuthorName;
            OriginalAuthorEmail = details.AuthorEmail;
            OriginalAuthorDate = details.Date;
            OriginalCommitterDate = details.CommitterDate ?? details.Date;
            if (committerWasUnchanged)
                NewCommitterDate = OriginalCommitterDate;

            if (messageWasUnchanged && string.Equals(NewMessage, oldMessage, StringComparison.Ordinal))
                NewMessage = details.Message;
            else if (updatePendingIfUnchanged && string.Equals(NewMessage, oldMessage, StringComparison.Ordinal))
                NewMessage = details.Message;
        }

        public CommitRewrite ToRewrite()
        {
            DateTimeOffset? newAuthorDate = null;
            if (HasTimeChange && NewAuthorDate.HasValue)
                newAuthorDate = RewriteDate.Build(NewAuthorDate.Value, OriginalAuthorDate);

            DateTimeOffset? newCommitterDate = null;
            if (HasCommitterTimeChange && NewCommitterDate.HasValue)
                newCommitterDate = RewriteDate.Build(NewCommitterDate.Value, OriginalCommitterDate);

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
