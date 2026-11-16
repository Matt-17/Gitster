using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Gitster.Core.Models;
using Gitster.Core.Git;
using Gitster.Core.Features;
using Gitster.Core.History;
using Gitster.Core.Search;
using Gitster.Core.Ui;

namespace Gitster.ViewModels;

/// <summary>
/// Owns the commit list. The SQLite cache does the expensive git walk; WPF virtualization
/// keeps rendering cheap after the full cached history is loaded.
/// </summary>
public partial class CommitListViewModel : BaseViewModel
{
    private const int FilterDebounceMs = 150;
    private const int PageNavigationCommitCount = 10;
    private const double GraphColumnMinWidth = 28;
    private const double GraphColumnMaxWidth = 240;
    private const double GraphColumnPadding = 12;
    private const double GraphLaneSpacing = 10;

    private readonly IGitBackend _git;
    private readonly CommitHistoryService _history;
    private readonly GitFeatureService _features;
    private readonly IDispatcher? _dispatcher;
    private readonly CommitGraphLayoutService _graphLayout = new();

    /// <summary>Shared UI preferences (date display mode, gravatar) for row bindings.</summary>
    public Gitster.Core.UiPreferencesService Ui { get; }

    private List<CommitItem> _allRows = [];
    private List<CommitItem> _incomingRows = [];
    private List<CommitItem> _selectedCommits = [];
    private RemoteSets? _remoteSets;
    // Keeps the remote name/url on the header while a new branch's sets are still computing,
    // so switching branches never blanks the header line.
    private string? _lastRemoteName;
    private string? _lastRemoteUrl;
    private RemoteHistoryState _remoteHistoryState = RemoteHistoryState.Loaded;
    private CommitQuery _query = CommitQuery.Parse(null);

    private CancellationTokenSource? _loadCts;
    private CancellationTokenSource? _filterCts;
    private CancellationTokenSource? _diffCts;
    private Task? _warmHistoryTask;

    // While a cold-cache load fills the list in stages, the graph column only widens; the
    // final stage sets the exact width.
    private bool _graphWidthGrowOnly;
    private HistoryTarget _selectedTarget = HistoryTarget.CurrentBranch;
    private bool _suppressScopeReload;

    public CommitListViewModel(
        IGitBackend git,
        CommitHistoryService history,
        Gitster.Core.UiPreferencesService ui,
        GitFeatureService? features = null,
        IDispatcher? dispatcher = null)
    {
        _git = git;
        _history = history;
        _features = features ?? new GitFeatureService();
        _dispatcher = dispatcher;
        Ui = ui;
    }

    /// <summary>Flat list of <see cref="CommitSectionHeader"/> and <see cref="CommitItem"/> rows.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<object> Items { get; set; } = [];

    partial void OnItemsChanged(IReadOnlyList<object> value)
    {
        SelectParentCommitCommand.NotifyCanExecuteChanged();
        SelectChildCommitCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(IsEmpty));
    }

    public bool HasItems => Items.Count > 0;
    public bool IsEmpty => !HistoryLoading && Items.Count == 0;

    [ObservableProperty]
    public partial double GraphColumnWidth { get; set; } = GraphColumnMinWidth;

    /// <summary>All cached commits for the current local branch, newest first.</summary>
    public IReadOnlyList<CommitItem> LoadedCommits => _allRows;

    public event Action? FocusSearchRequested;

    public IReadOnlyList<HistoryScopeOption> ScopeOptions { get; } =
    [
        new(HistoryScope.CurrentBranch, "Current branch"),
        new(HistoryScope.AllBranches, "All branches"),
    ];

    [ObservableProperty]
    public partial HistoryScope SelectedScope { get; set; } = HistoryScope.CurrentBranch;

    [ObservableProperty]
    public partial string SelectedRefDisplayName { get; set; } = string.Empty;

    public bool IsNamedRefSelected => _selectedTarget.Scope == HistoryScope.Ref;

    /// <summary>Canonical name of the shown ref, or null when the current branch or all branches are shown.</summary>
    public string? ViewedRefName => _selectedTarget.Scope == HistoryScope.Ref ? _selectedTarget.RefName : null;

    /// <summary>Raised with (scope, canonical ref name) whenever the shown history target
    /// changes, so the refs pane can highlight the viewed branch.</summary>
    public Action<HistoryScope, string?>? ViewTargetChanged { get; set; }

    private void NotifyViewTargetChanged() =>
        ViewTargetChanged?.Invoke(_selectedTarget.Scope, _selectedTarget.RefName);

    [RelayCommand]
    private void FocusSearch() => FocusSearchRequested?.Invoke();

    partial void OnSelectedScopeChanged(HistoryScope value)
    {
        if (_suppressScopeReload)
            return;

        if (_selectedTarget.Scope == value && value != HistoryScope.Ref)
            return;

        _selectedTarget = HistoryTarget.ForScope(value);
        SelectedRefDisplayName = string.Empty;
        OnPropertyChanged(nameof(IsNamedRefSelected));
        NotifyViewTargetChanged();

        if (!string.IsNullOrWhiteSpace(_git.RepositoryPath))
            _ = LoadAsync();
    }

    public Task ShowScopeAsync(HistoryScope scope)
    {
        var normalized = scope == HistoryScope.Ref ? HistoryScope.CurrentBranch : scope;
        _selectedTarget = HistoryTarget.ForScope(normalized);
        SelectedRefDisplayName = string.Empty;
        if (normalized != HistoryScope.CurrentBranch)
            ShowOutgoingIncomingOnly = false;
        OnPropertyChanged(nameof(IsNamedRefSelected));
        NotifyViewTargetChanged();
        _suppressScopeReload = true;
        try
        {
            SelectedScope = normalized;
        }
        finally
        {
            _suppressScopeReload = false;
        }

        return string.IsNullOrWhiteSpace(_git.RepositoryPath) ? Task.CompletedTask : LoadAsync();
    }

    public Task ShowRefAsync(string canonicalName, string displayName)
    {
        _selectedTarget = HistoryTarget.ForRef(canonicalName, displayName);
        SelectedRefDisplayName = displayName;
        ShowOutgoingIncomingOnly = false;
        OnPropertyChanged(nameof(IsNamedRefSelected));
        NotifyViewTargetChanged();
        return string.IsNullOrWhiteSpace(_git.RepositoryPath) ? Task.CompletedTask : LoadAsync();
    }

    public Func<DiffFileEntry?, Task>? RemoveChangeFromCommitAsync { get; set; }
    public Func<CommitItem, CommitItem, Task>? FixupDroppedCommitAsync { get; set; }

    /// <summary>Checks out a branch by name (local, or remote like "origin/foo" which creates the local branch).</summary>
    public Func<string, Task>? CheckoutBranchAsync { get; set; }

    /// <summary>Publishes a local branch: (branchName, remoteName) → git push -u.</summary>
    public Func<string, string, Task>? PublishBranchAsync { get; set; }

    /// <summary>Assigns an existing remote branch as upstream: (branchName, remoteBranchName).</summary>
    public Func<string, string, Task>? SetUpstreamAsync { get; set; }

    // ── Reorder (pending history edit) ───────────────────────────────────

    /// <summary>Plans commit moves as a pending history edit (the history draft).</summary>
    public ICommitReorderer? Reorderer { get; set; }

    /// <summary>Shows a reorder error, e.g. as a warning dialog.</summary>
    public Action<string>? ShowReorderError { get; set; }

    private IReadOnlyList<string>? _pendingLocalOrder;

    public bool HasPendingReorder => _pendingLocalOrder is not null;

    /// <summary>Shows the pending order in the current-branch view; null restores the real order.</summary>
    public void SetPendingLocalOrder(IReadOnlyList<string>? order)
    {
        _pendingLocalOrder = order;
        OnPropertyChanged(nameof(HasPendingReorder));
        RebuildRows();
    }

    /// <summary>Why commits cannot be moved in the current view, or null when they can.</summary>
    public string? ReorderBlockedReason =>
        Reorderer is null ? "Reordering is not available."
        : _selectedTarget.Scope != HistoryScope.CurrentBranch ? "Switch to the current branch to reorder commits."
        : !_query.IsEmpty || ShowOutgoingIncomingOnly ? "Clear the filter to reorder commits."
        : null;

    public string? CanMoveCommitsTo(IReadOnlyList<CommitItem> commits, CommitItem target, bool after)
        => ReorderBlockedReason ?? Reorderer!.CanMoveCommitsTo(commits, target, after);

    public void MoveCommitsTo(IReadOnlyList<CommitItem> commits, CommitItem target, bool after)
        => ReportReorder(ReorderBlockedReason ?? Reorderer!.MoveCommitsTo(commits, target, after));

    [RelayCommand]
    private void MoveCommitUp() => MoveSelectedBy(-1);

    [RelayCommand]
    private void MoveCommitDown() => MoveSelectedBy(1);

    private void MoveSelectedBy(int delta)
    {
        List<CommitItem> commits = SelectedCommits.Count > 0
            ? SelectedCommits
            : SelectedCommit is { } single ? [single] : [];
        if (commits.Count == 0)
            return;

        ReportReorder(ReorderBlockedReason ?? Reorderer!.MoveCommitsBy(commits, delta));
    }

    private void ReportReorder(string? error)
    {
        if (error is not null)
            ShowReorderError?.Invoke(error);
    }

    /// <summary>Puts the local rows into the pending order (current-branch view only).</summary>
    private List<CommitItem> ApplyPendingOrder(List<CommitItem> local)
    {
        if (_pendingLocalOrder is null || _selectedTarget.Scope != HistoryScope.CurrentBranch)
            return local;

        var position = _pendingLocalOrder
            .Select((sha, index) => (sha, index))
            .ToDictionary(x => x.sha, x => x.index, StringComparer.OrdinalIgnoreCase);
        return local
            .Select((commit, index) => (commit, index))
            .OrderBy(x => position.TryGetValue(x.commit.FullSha, out var p) ? p : x.index)
            .Select(x => x.commit)
            .ToList();
    }

    public static bool CanDropCommitForFixup(
        CommitItem? source,
        CommitItem? target,
        out string reason)
    {
        reason = string.Empty;
        if (source is null || target is null)
        {
            reason = "Drag one commit onto another commit.";
            return false;
        }

        if (source.FullSha.Equals(target.FullSha, StringComparison.OrdinalIgnoreCase))
        {
            reason = "Choose two different commits.";
            return false;
        }

        if (source.RemoteState == CommitRemoteState.Incoming || target.RemoteState == CommitRemoteState.Incoming)
        {
            reason = "Incoming commits must be pulled or cherry-picked before fixup.";
            return false;
        }

        if (source.RemoteState == CommitRemoteState.OnRemote || target.RemoteState == CommitRemoteState.OnRemote)
        {
            reason = "Synced commits are blocked because fixup rewrites published history.";
            return false;
        }

        return true;
    }

    public Task DropCommitForFixupAsync(CommitItem source, CommitItem target) =>
        FixupDroppedCommitAsync is null
            ? Task.CompletedTask
            : FixupDroppedCommitAsync(source, target);

    [RelayCommand(CanExecute = nameof(CanRemoveChangeFromCommit))]
    private async Task RemoveChangeFromCommit(DiffFileEntry? file)
    {
        if (RemoveChangeFromCommitAsync is null)
            return;

        await RemoveChangeFromCommitAsync(file);
    }

    private bool CanRemoveChangeFromCommit(DiffFileEntry? file) =>
        RemoveChangeFromCommitAsync is not null
        && !DiffLoading
        && !DiffStale
        && file is not null
        && SelectedCommit is not null
        && SelectedCommit.RemoteState != CommitRemoteState.Incoming;

    [ObservableProperty]
    public partial CommitItem? SelectedCommit { get; set; }

    /// <summary>All currently selected commits (populated by the view's SelectionChanged handler).</summary>
    public List<CommitItem> SelectedCommits
    {
        get => _selectedCommits;
        set
        {
            if (SetProperty(ref _selectedCommits, value))
                OnPropertyChanged(nameof(HasTwoSelectedCommits));
        }
    }

    public bool HasTwoSelectedCommits =>
        SelectedCommits.Select(c => c.FullSha).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 2;

    [ObservableProperty]
    public partial string FilterText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool ShowOutgoingIncomingOnly { get; set; }

    [ObservableProperty]
    public partial bool HasActiveFilters { get; set; }

    [ObservableProperty]
    public partial string FilterStatusText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HistoryLoading { get; set; }

    [ObservableProperty]
    public partial string HistoryStatusText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string DiffHeader { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool DiffLoading { get; set; }

    /// <summary>
    /// True while the diff panel still shows the previous commit's files because the new diff has not
    /// arrived yet. Keeping them avoids tearing the panel down and rebuilding it for every selection.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveChangeFromCommitCommand))]
    public partial bool DiffStale { get; set; }

    /// <summary>Only after this long without a diff does the panel switch to its loading state.</summary>
    private static readonly TimeSpan DiffLoadingIndicatorDelay = TimeSpan.FromMilliseconds(150);

    [ObservableProperty]
    public partial List<DiffFileEntry> DiffFiles { get; set; } = [];

    [ObservableProperty]
    public partial CommitRemoteState DiffRemoteState { get; set; }

    public string DiffHeaderDisplay =>
        DiffLoading ? "loading..."
        : string.IsNullOrEmpty(DiffHeader) ? "no commit selected"
        : DiffHeader;

    partial void OnDiffHeaderChanged(string value) => OnPropertyChanged(nameof(DiffHeaderDisplay));
    partial void OnDiffLoadingChanged(bool value)
    {
        OnPropertyChanged(nameof(DiffHeaderDisplay));
        RemoveChangeFromCommitCommand.NotifyCanExecuteChanged();
    }

    partial void OnHistoryLoadingChanged(bool value) => OnPropertyChanged(nameof(IsEmpty));

    public void UpdateDiff(string header, List<DiffFileEntry> files,
        CommitRemoteState remoteState = CommitRemoteState.LocalOnly)
    {
        DiffHeader = header;
        DiffFiles = files;
        DiffRemoteState = remoteState;
    }

    public async Task LoadAsync(
        CancellationToken cancellationToken = default,
        IProgress<RepositoryLoadProgress>? progress = null)
    {
        _loadCts?.Cancel();
        _filterCts?.Cancel();
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _loadCts = cts;
        var ct = cts.Token;

        var priorSha = SelectedCommit?.FullSha;

        HistoryLoading = true;
        HistoryStatusText = "Loading history...";

        try
        {
            if (string.IsNullOrWhiteSpace(_git.RepositoryPath))
            {
                await InvokeOnUiAsync(ClearList);
                return;
            }

            var target = _selectedTarget;
            await _history.OpenAsync(_git.RepositoryPath, target, ct, progress);

            // Warm cache (the usual case after a commit, amend or refresh): load the full
            // history and the remote state together and rebuild the list once, instead of
            // showing a first page without remote info and rebuilding twice more.
            if (await _history.IsCompleteAsync(target, ct))
            {
                await LoadCompleteAsync(target, cts, priorSha, progress);
                return;
            }

            // Cold cache: show the first page quickly; later stages may only widen the graph
            // column so the rows do not jump back and forth while history fills in.
            _graphWidthGrowOnly = true;

            progress?.Report(new RepositoryLoadProgress(
                "Loading history",
                "Loading the first commit page."));
            var rows = await _history.GetPageAsync(
                CommitQuery.Parse(null),
                0,
                CommitHistoryService.DefaultPageSize,
                target,
                ct,
                progress);
            if (ct.IsCancellationRequested) return;

            var newAllRows = rows.Select(r => r.ToCommitItem()).ToList();

            await InvokeOnUiAsync(() =>
            {
                _allRows = newAllRows;
                _incomingRows = [];
                _remoteSets = null;
                _remoteHistoryState = ShowsSections(target)
                    ? RemoteHistoryState.Pending
                    : RemoteHistoryState.Loaded;
                OnPropertyChanged(nameof(LoadedCommits));

                ApplyRows(BuildRows(FilteredIncoming(), FilteredLocal()), SelectedCommit?.FullSha ?? priorSha);
                HistoryLoading = false;
            });

            _warmHistoryTask = WarmHistoryAndEnrichAsync(target, cts, progress);
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer load.
        }
        catch (Exception ex)
        {
            // Several callers fire-and-forget this method (scope/ref changes); an
            // escaping exception would only surface as an UnobservedTaskException.
            System.Diagnostics.Debug.WriteLine($"History load failed: {ex}");
            if (_loadCts == cts)
                HistoryStatusText = $"Could not load history: {ex.Message}";
        }
        finally
        {
            if (_loadCts == cts)
                HistoryLoading = false;
        }
    }

    /// <summary>
    /// Loads the complete cached history and the remote sets in parallel and applies both in
    /// one rebuild. Signing badges are filled in afterwards on the existing rows.
    /// </summary>
    private async Task LoadCompleteAsync(
        HistoryTarget target,
        CancellationTokenSource ownerCts,
        string? priorSha,
        IProgress<RepositoryLoadProgress>? progress)
    {
        var ct = ownerCts.Token;
        var rowsTask = _history.EnsureCompleteAsync(progress, ct, target);
        var setsTask = ShowsSections(target)
            ? Task.Run(() => _git.ComputeRemoteSetsAsync(target.RefName, ct), ct)
            : Task.FromResult<RemoteSets>(RemoteSets.Empty);

        var rows = await rowsTask;
        RemoteSets? sets = null;
        var remoteFailed = false;
        try
        {
            sets = await setsTask;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            remoteFailed = true;
        }

        if (ct.IsCancellationRequested || _loadCts != ownerCts)
            return;

        var newAllRows = rows.Select(r => r.ToCommitItem()).ToList();
        var incomingRows = sets is not null && ShowsSections(target) && !IsRemoteBranchRef(target)
            ? sets.Incoming.Select(ToItem).ToList()
            : new List<CommitItem>();

        await InvokeOnUiAsync(() =>
        {
            _graphWidthGrowOnly = false;
            _allRows = newAllRows;
            _incomingRows = incomingRows;
            if (!ShowsSections(target))
            {
                _remoteSets = null;
                _remoteHistoryState = RemoteHistoryState.Loaded;
            }
            else if (sets is not null)
            {
                _remoteSets = sets;
                _lastRemoteName = sets.RemoteName ?? _lastRemoteName;
                _lastRemoteUrl = sets.RemoteUrl ?? _lastRemoteUrl;
                _remoteHistoryState = RemoteHistoryState.Loaded;
                ApplyRemoteSets(sets, _allRows);
            }
            else
            {
                _remoteSets = null;
                _remoteHistoryState = remoteFailed ? RemoteHistoryState.Unavailable : RemoteHistoryState.Loaded;
            }

            OnPropertyChanged(nameof(LoadedCommits));
            if (_query.IsEmpty)
                ApplyRows(BuildRows(FilteredIncoming(), FilteredLocal()), SelectedCommit?.FullSha ?? priorSha);
            else
                DebounceFilter();
            HistoryLoading = false;
        });

        // Background bookkeeping that does not change the row layout.
        _warmHistoryTask = Task.Run(async () =>
        {
            try
            {
                if (sets is not null && target.Scope == HistoryScope.CurrentBranch)
                    await _history.ApplyRemoteSetsAsync(sets, ct);
                await ApplySigningStatusesAsync(newAllRows, ct);
                await ApplySigningStatusesAsync(incomingRows, ct);
            }
            catch (OperationCanceledException)
            {
                // Superseded by a newer load.
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"History enrichment failed: {ex}");
            }
        }, ct);
    }

    public void ClearList()
    {
        _loadCts?.Cancel();
        _filterCts?.Cancel();
        _allRows = [];
        _incomingRows = [];
        _remoteSets = null;
        _lastRemoteName = null;
        _lastRemoteUrl = null;
        _remoteHistoryState = RemoteHistoryState.Loaded;
        _selectedTarget = HistoryTarget.CurrentBranch;
        SelectedScope = HistoryScope.CurrentBranch;
        SelectedRefDisplayName = string.Empty;
        OnPropertyChanged(nameof(IsNamedRefSelected));
        NotifyViewTargetChanged();
        OnPropertyChanged(nameof(LoadedCommits));
        Items = [];
        GraphColumnWidth = GraphColumnMinWidth;
        SelectedCommit = null;
        HistoryLoading = false;
        HistoryStatusText = string.Empty;
        UpdateFilterStatus();
    }

    /// <summary>Rebuilds the visible rows from the cached data without reloading history.</summary>
    public void RebuildRows() =>
        ApplyRows(BuildRows(FilteredIncoming(), FilteredLocal()), SelectedCommit?.FullSha);

    public bool SelectCommitBySha(string? fullSha)
    {
        if (string.IsNullOrWhiteSpace(fullSha))
            return false;

        var match = Items
            .OfType<CommitItem>()
            .FirstOrDefault(c => string.Equals(c.FullSha, fullSha, StringComparison.OrdinalIgnoreCase));
        if (match is null)
            return false;

        SelectedCommit = match;
        return true;
    }

    [RelayCommand]
    private void SelectPreviousCommit() => SelectCommitOffset(-1);

    [RelayCommand]
    private void SelectNextCommit() => SelectCommitOffset(1);

    [RelayCommand]
    private void SelectPreviousCommitPage() => SelectCommitOffset(-PageNavigationCommitCount);

    [RelayCommand]
    private void SelectNextCommitPage() => SelectCommitOffset(PageNavigationCommitCount);

    [RelayCommand]
    private void SelectFirstCommit() => SelectCommitBoundary(first: true);

    [RelayCommand]
    private void SelectLastCommit() => SelectCommitBoundary(first: false);

    [RelayCommand(CanExecute = nameof(CanSelectParentCommit))]
    private void SelectParentCommit()
    {
        if (SelectedCommit?.ParentShas.FirstOrDefault() is not { } parentSha)
            return;

        var parent = VisibleCommits()
            .FirstOrDefault(c => string.Equals(c.FullSha, parentSha, StringComparison.OrdinalIgnoreCase));
        if (parent is not null)
            SelectedCommit = parent;
    }

    private bool CanSelectParentCommit() =>
        SelectedCommit?.ParentShas.Count > 0
        && VisibleCommits().Any(c => string.Equals(
            c.FullSha,
            SelectedCommit.ParentShas[0],
            StringComparison.OrdinalIgnoreCase));

    [RelayCommand(CanExecute = nameof(CanSelectChildCommit))]
    private void SelectChildCommit()
    {
        if (SelectedCommit is null)
            return;

        var commits = VisibleCommits();
        var currentIndex = FindSelectedCommitIndex(commits);
        if (currentIndex < 0)
            return;

        for (var i = currentIndex - 1; i >= 0; i--)
        {
            if (commits[i].ParentShas.Any(p =>
                string.Equals(p, SelectedCommit.FullSha, StringComparison.OrdinalIgnoreCase)))
            {
                SelectedCommit = commits[i];
                return;
            }
        }
    }

    private bool CanSelectChildCommit()
    {
        if (SelectedCommit is null)
            return false;

        return VisibleCommits().Any(c => c.ParentShas.Any(p =>
            string.Equals(p, SelectedCommit.FullSha, StringComparison.OrdinalIgnoreCase)));
    }

    private void SelectCommitOffset(int offset)
    {
        var commits = VisibleCommits();
        if (commits.Count == 0)
        {
            SelectedCommit = null;
            return;
        }

        var currentIndex = FindSelectedCommitIndex(commits);
        var targetIndex = currentIndex < 0
            ? offset < 0 ? commits.Count - 1 : 0
            : Math.Clamp(currentIndex + offset, 0, commits.Count - 1);

        SelectedCommit = commits[targetIndex];
    }

    private void SelectCommitBoundary(bool first)
    {
        var commits = VisibleCommits();
        SelectedCommit = commits.Count == 0
            ? null
            : commits[first ? 0 : commits.Count - 1];
    }

    private int FindSelectedCommitIndex(List<CommitItem> commits)
    {
        if (SelectedCommit is null)
            return -1;

        var selected = SelectedCommit;
        var index = commits.FindIndex(c => ReferenceEquals(c, selected));
        if (index >= 0)
            return index;

        return commits.FindIndex(c =>
            string.Equals(c.FullSha, selected.FullSha, StringComparison.OrdinalIgnoreCase));
    }

    private List<CommitItem> VisibleCommits() => Items.OfType<CommitItem>().ToList();

    private static CommitItem ToItem(CommitInfo info) => new(
        info.Message,
        info.Date,
        info.Sha,
        info.AuthorName,
        info.AuthorEmail,
        info.RemoteState,
        info.FullSha,
        info.OrphanedPairSha,
        info.ParentShas,
        info.RefLabels,
        info.CommitterDate);

    private async Task ApplySigningStatusesAsync(IReadOnlyList<CommitItem> rows, CancellationToken ct)
    {
        if (rows.Count == 0 || string.IsNullOrWhiteSpace(_git.RepositoryPath) || !GitCli.IsAvailable)
            return;

        try
        {
            var statuses = await _features.GetSigningStatusesAsync(
                _git.RepositoryPath,
                rows.Select(r => r.FullSha).Distinct(StringComparer.OrdinalIgnoreCase).Take(500).ToList(),
                ct);

            await InvokeOnUiAsync(() =>
            {
                foreach (var row in rows)
                    if (statuses.TryGetValue(row.FullSha, out var status))
                        row.SigningStatus = status;
            });
        }
        catch
        {
            await InvokeOnUiAsync(() =>
            {
                foreach (var row in rows)
                    row.SigningStatus = CommitSigningStatus.Unknown;
            });
        }
    }

    /// <summary>True for every target that must render the Incoming/Outgoing sections:
    /// the current branch and any local or remote branch picked in the refs pane.</summary>
    private static bool ShowsSections(HistoryTarget target) =>
        target.Scope == HistoryScope.CurrentBranch || IsBranchRef(target);

    private static bool IsBranchRef(HistoryTarget target) =>
        target.Scope == HistoryScope.Ref
        && target.RefName is { } refName
        && (refName.StartsWith("refs/heads/", StringComparison.Ordinal)
            || refName.StartsWith("refs/remotes/", StringComparison.Ordinal));

    private static bool IsRemoteBranchRef(HistoryTarget target) =>
        target.Scope == HistoryScope.Ref
        && target.RefName?.StartsWith("refs/remotes/", StringComparison.Ordinal) == true;

    private void ApplyRemoteSets(RemoteSets sets, IEnumerable<CommitItem> rows)
    {
        if (IsRemoteBranchRef(_selectedTarget))
        {
            // The listed history IS the remote branch; commits missing from the local
            // counterpart (or all of them, without one) count as incoming.
            var incomingShas = new HashSet<string>(
                sets.Incoming.Select(c => c.FullSha),
                StringComparer.OrdinalIgnoreCase);

            foreach (var row in rows)
            {
                row.RemoteState = !sets.HasLocalBranch || incomingShas.Contains(row.FullSha)
                    ? CommitRemoteState.Incoming
                    : CommitRemoteState.OnRemote;
                row.OrphanedPairSha = null;
            }

            return;
        }

        var outgoing = sets.OutgoingFullShas;
        var orphans = sets.OrphanedPairs;

        foreach (var row in rows)
        {
            row.RemoteState = outgoing.Contains(row.FullSha)
                ? CommitRemoteState.LocalOnly
                : sets.HasTrackingBranch ? CommitRemoteState.OnRemote : CommitRemoteState.NoTrackingBranch;
            row.OrphanedPairSha = orphans.TryGetValue(row.FullSha, out var p) ? p : null;
        }
    }

    partial void OnFilterTextChanged(string value)
    {
        _query = CommitQuery.Parse(value);
        DebounceFilter();
    }

    private void DebounceFilter()
    {
        _filterCts?.Cancel();
        var cts = _loadCts is null
            ? new CancellationTokenSource()
            : CancellationTokenSource.CreateLinkedTokenSource(_loadCts.Token);
        _filterCts = cts;
        var token = cts.Token;
        var query = _query;

        Task.Run(async () =>
        {
            try { await Task.Delay(FilterDebounceMs, token); }
            catch (TaskCanceledException) { return; }
            if (token.IsCancellationRequested) return;

            try
            {
                BuiltCommitRows builtRows;
                if (query.IsEmpty)
                {
                    var rows = _allRows;
                    var incoming = _incomingRows;
                    builtRows = BuildRows(incoming, ApplyOutgoingIncomingFilter(rows));
                }
                else
                {
                    await InvokeOnUiAsync(() =>
                    {
                        HistoryLoading = true;
                        HistoryStatusText = "Indexing history for complete search...";
                    });

                    var rows = await _history.SearchAsync(
                        query,
                        int.MaxValue,
                        _selectedTarget,
                        token,
                        offset: 0,
                        progress: new Progress<RepositoryLoadProgress>(p =>
                        {
                            var statusText = string.IsNullOrWhiteSpace(p.CounterText)
                                ? p.Detail
                                : $"{p.Detail} {p.CounterText}";
                            _ = InvokeOnUiAsync(() => HistoryStatusText = statusText);
                        }));
                    var matchedLocal = rows.Select(r => r.ToCommitItem()).ToList();
                    if (_remoteSets is not null)
                        ApplyRemoteSets(_remoteSets, matchedLocal);

                    var matchedIncoming = _incomingRows.Where(c => Match(query, c)).ToList();
                    builtRows = BuildRows(matchedIncoming, ApplyOutgoingIncomingFilter(matchedLocal));
                }

                if (token.IsCancellationRequested) return;

                await InvokeOnUiAsync(() =>
                {
                    ApplyRows(builtRows, SelectedCommit?.FullSha);
                    if (_filterCts == cts)
                        HistoryLoading = false;
                });
            }
            catch (OperationCanceledException)
            {
                // Superseded by another filter.
            }
            finally
            {
                if (_filterCts == cts)
                    await InvokeOnUiAsync(() => HistoryLoading = false);
            }
        });
    }

    private static bool Match(CommitQuery query, CommitItem c) =>
        query.Matches(c.Message, c.AuthorName, c.AuthorEmail, c.FullSha, c.Date);

    private List<CommitItem> FilteredIncoming() =>
        _query.IsEmpty ? _incomingRows : _incomingRows.Where(c => Match(_query, c)).ToList();

    private List<CommitItem> FilteredLocal()
    {
        var rows = _query.IsEmpty ? _allRows : _allRows.Where(c => Match(_query, c)).ToList();
        return ApplyOutgoingIncomingFilter(rows);
    }

    private List<CommitItem> ApplyOutgoingIncomingFilter(IEnumerable<CommitItem> rows) =>
        ShowOutgoingIncomingOnly
            ? rows.Where(IsOutgoing).ToList()
            : rows.ToList();

    private static bool IsOutgoing(CommitItem c) =>
        c.RemoteState is CommitRemoteState.LocalOnly or CommitRemoteState.NoTrackingBranch;

    private BuiltCommitRows BuildRows(List<CommitItem> incoming, List<CommitItem> local)
    {
        // GRUNDREGEL: every branch view (current, local ref, remote ref) always renders
        // BOTH the Remote History and Local History sections. Only the all-branches
        // graph and non-branch refs (tags, stashes) fall back to a plain list.
        if (!ShowsSections(_selectedTarget))
        {
            ApplyGraphRows(local);
            return new BuiltCommitRows(local.Cast<object>().ToList(), local.Count, 0);
        }

        if (IsRemoteBranchRef(_selectedTarget))
            return BuildRemoteBranchRows(local);

        int outgoingCount = 0;
        for (int i = 0; i < local.Count; i++)
            if (IsOutgoing(local[i])) outgoingCount++;

        var remotePending = _remoteHistoryState == RemoteHistoryState.Pending;
        var remoteUnavailable = _remoteHistoryState == RemoteHistoryState.Unavailable;
        var hasRemote = _remoteSets?.HasRemote ?? false;
        var hasTrackingBranch = _remoteSets?.HasTrackingBranch ?? false;

        var result = new List<object>(incoming.Count + local.Count + 4);

        result.Add(new CommitSectionHeader(CommitSectionKind.RemoteIncoming, incoming.Count,
            RemoteNameForHeader(), RemoteUrlForHeader(), remotePending));

        if (remotePending)
            result.Add(new CommitSectionEmptyRow(CommitSectionKind.RemoteIncoming, "checking remote branch history..."));
        else if (remoteUnavailable)
            result.Add(new CommitSectionEmptyRow(CommitSectionKind.RemoteIncoming, "remote history unavailable"));
        else if (!hasRemote)
            result.Add(new CommitSectionEmptyRow(CommitSectionKind.RemoteIncoming, "no remote configured"));
        else if (!hasTrackingBranch)
            result.Add(BuildNoRemoteBranchRow());
        else if (incoming.Count == 0)
            result.Add(new CommitSectionEmptyRow(CommitSectionKind.RemoteIncoming, "no incoming commits"));
        else
        {
            ApplyGraphRows(incoming);
            result.AddRange(incoming);
        }

        result.Add(new CommitSectionHeader(CommitSectionKind.LocalOutgoing, outgoingCount));
        if (local.Count == 0)
            result.Add(new CommitSectionEmptyRow(CommitSectionKind.LocalOutgoing, "no local commits"));

        var ordered = ApplyPendingOrder(local);
        ApplyGraphRows(ordered, PendingParents(local, ordered));
        result.AddRange(ordered);
        return new BuiltCommitRows(result, local.Count, incoming.Count);
    }

    /// <summary>A refs/remotes/* selection: the commits live in the Remote History section;
    /// the Local History section explains whether a local counterpart exists.</summary>
    private BuiltCommitRows BuildRemoteBranchRows(List<CommitItem> remoteRows)
    {
        var remotePending = _remoteHistoryState == RemoteHistoryState.Pending;
        var incomingCount = 0;
        for (int i = 0; i < remoteRows.Count; i++)
            if (remoteRows[i].RemoteState == CommitRemoteState.Incoming) incomingCount++;

        var result = new List<object>(remoteRows.Count + 4);

        // The one-line local section goes first so it stays visible above the full
        // remote history — mirroring the current-branch layout, where the compact
        // status section sits on top of the long one.
        result.Add(new CommitSectionHeader(CommitSectionKind.LocalOutgoing, 0));

        if (remotePending)
        {
            result.Add(new CommitSectionEmptyRow(CommitSectionKind.LocalOutgoing, "checking for a local branch..."));
        }
        else if (_remoteSets is { HasLocalBranch: true, LocalBranchName: { } localName })
        {
            result.Add(new CommitSectionEmptyRow(
                CommitSectionKind.LocalOutgoing,
                $"local branch '{localName}'",
                [
                    new CommitSectionLink("show local history",
                        () => ShowRefAsync($"refs/heads/{localName}", localName)),
                    new CommitSectionLink("checkout", () => CheckoutThenShowCurrentAsync(localName)),
                ]));
        }
        else
        {
            var remoteBranch = _remoteSets?.RemoteBranchName ?? _selectedTarget.DisplayName;
            var links = string.IsNullOrWhiteSpace(remoteBranch)
                ? Array.Empty<CommitSectionLink>()
                : [new CommitSectionLink("checkout as local branch", () => CheckoutThenShowCurrentAsync(remoteBranch))];
            result.Add(new CommitSectionEmptyRow(CommitSectionKind.LocalOutgoing, "no local branch", links));
        }

        result.Add(new CommitSectionHeader(CommitSectionKind.RemoteIncoming, remoteRows.Count,
            RemoteNameForHeader(), RemoteUrlForHeader(), remotePending, plainCount: true));

        if (remoteRows.Count == 0)
        {
            result.Add(new CommitSectionEmptyRow(CommitSectionKind.RemoteIncoming, "no commits on this remote branch"));
        }
        else
        {
            ApplyGraphRows(remoteRows);
            result.AddRange(remoteRows);
        }

        return new BuiltCommitRows(result, remoteRows.Count, incomingCount);
    }

    /// <summary>The "no remote branch" placeholder with assign/create+push actions.</summary>
    private CommitSectionEmptyRow BuildNoRemoteBranchRow()
    {
        var sets = _remoteSets;
        var links = new List<CommitSectionLink>();

        if (sets?.LocalBranchName is { } branchName)
        {
            if (sets is { HasSameNameRemoteBranch: true, RemoteBranchName: { } remoteBranch })
                links.Add(new CommitSectionLink(
                    "connect to remote branch",
                    () => AssignUpstreamAsync(branchName, remoteBranch)));

            var remoteName = sets.RemoteName ?? "origin";
            links.Add(new CommitSectionLink(
                "push",
                () => PublishAsync(branchName, remoteName)));
        }

        return new CommitSectionEmptyRow(CommitSectionKind.RemoteIncoming, "no remote branch", links);
    }

    private string? RemoteNameForHeader() => _remoteSets?.RemoteName ?? _lastRemoteName;
    private string? RemoteUrlForHeader() => _remoteSets?.RemoteUrl ?? _lastRemoteUrl;

    private async Task CheckoutThenShowCurrentAsync(string branchName)
    {
        if (CheckoutBranchAsync is null)
            return;

        await CheckoutBranchAsync(branchName);
        await ShowScopeAsync(HistoryScope.CurrentBranch);
    }

    private async Task AssignUpstreamAsync(string branchName, string remoteBranchName)
    {
        if (SetUpstreamAsync is null)
            return;

        await SetUpstreamAsync(branchName, remoteBranchName);
        await LoadAsync();
    }

    private async Task PublishAsync(string branchName, string remoteName)
    {
        if (PublishBranchAsync is null)
            return;

        await PublishBranchAsync(branchName, remoteName);
        await LoadAsync();
    }

    /// <summary>
    /// For a pending reorder: the parents the reordered run will have after applying (each commit
    /// on top of the one below it), so the graph draws the planned history. Null without a reorder.
    /// </summary>
    private static Dictionary<string, IReadOnlyList<string>>? PendingParents(
        IReadOnlyList<CommitItem> original,
        IReadOnlyList<CommitItem> ordered)
    {
        var oldestChanged = -1;
        for (var i = ordered.Count - 1; i >= 0; i--)
        {
            if (!ReferenceEquals(original[i], ordered[i]))
            {
                oldestChanged = i;
                break;
            }
        }

        if (oldestChanged < 0)
            return null;

        var parents = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i <= oldestChanged; i++)
        {
            parents[ordered[i].FullSha] = i + 1 < ordered.Count
                ? [ordered[i + 1].FullSha]
                : original[oldestChanged].ParentShas;
        }

        return parents;
    }

    private void ApplyGraphRows(
        IReadOnlyList<CommitItem> commits,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? parentOverrides = null)
    {
        var graphRows = _graphLayout.Layout(commits
            .Select(c => new CommitGraphNode(
                c.FullSha,
                parentOverrides is not null && parentOverrides.TryGetValue(c.FullSha, out var parents) ? parents : c.ParentShas,
                c.RefLabels))
            .ToList());

        foreach (var commit in commits)
            commit.GraphRow = graphRows.TryGetValue(commit.FullSha, out var row)
                ? row
                : CommitGraphRow.Empty;
    }

    private void ApplyRows(BuiltCommitRows rows, string? priorSha)
    {
        UpdateGraphColumnWidth(rows.Items);
        Items = rows.Items;
        UpdateFilterStatus();
        HistoryStatusText = StatusForRows(rows.LocalCount, rows.IncomingCount);
        SelectAfterRebuild(priorSha);
    }

    private void UpdateGraphColumnWidth(IReadOnlyList<object> items)
    {
        var maxLaneCount = items
            .OfType<CommitItem>()
            .Select(c => c.GraphRow.LaneCount)
            .DefaultIfEmpty(1)
            .Max();

        var width = GraphColumnWidthForLaneCount(maxLaneCount);
        GraphColumnWidth = _graphWidthGrowOnly ? Math.Max(GraphColumnWidth, width) : width;
    }

    public static double GraphColumnWidthForLaneCount(int laneCount)
    {
        var safeLaneCount = Math.Max(1, laneCount);
        return Math.Clamp(
            GraphColumnPadding + (safeLaneCount * GraphLaneSpacing),
            GraphColumnMinWidth,
            GraphColumnMaxWidth);
    }

    private void SelectAfterRebuild(string? priorSha)
    {
        var commits = Items.OfType<CommitItem>();
        var match = priorSha != null ? commits.FirstOrDefault(c => c.FullSha == priorSha) : null;
        SelectedCommit = match ?? commits.FirstOrDefault();
    }

    private void UpdateFilterStatus()
    {
        HasActiveFilters = !_query.IsEmpty;
        FilterStatusText = HasActiveFilters ? $"\"{FilterText.Trim()}\"" : string.Empty;
    }

    private string StatusForRows(int localCount, int incomingCount)
    {
        var count = HasActiveFilters || ShowOutgoingIncomingOnly ? localCount + incomingCount : _allRows.Count;
        if (_selectedTarget.Scope == HistoryScope.AllBranches)
        {
            return HasActiveFilters
                ? $"{count:N0} matching commit{(count == 1 ? "" : "s")}"
                : $"{count:N0} commit{(count == 1 ? "" : "s")} across branches";
        }

        if (_selectedTarget.Scope == HistoryScope.Ref)
        {
            var name = string.IsNullOrWhiteSpace(_selectedTarget.DisplayName)
                ? "selected ref"
                : _selectedTarget.DisplayName;
            return HasActiveFilters
                ? $"{count:N0} matching commit{(count == 1 ? "" : "s")}"
                : $"{count:N0} commit{(count == 1 ? "" : "s")} on {name}";
        }

        return HasActiveFilters
            ? $"{count:N0} matching commit{(count == 1 ? "" : "s")}"
            : $"{count:N0} commit{(count == 1 ? "" : "s")}";
    }

    private sealed record BuiltCommitRows(IReadOnlyList<object> Items, int LocalCount, int IncomingCount);

    private async Task WarmHistoryAndEnrichAsync(
        HistoryTarget target,
        CancellationTokenSource ownerCts,
        IProgress<RepositoryLoadProgress>? progress)
    {
        var ct = ownerCts.Token;
        try
        {
            await Task.Yield();
            var wasComplete = await _history.IsCompleteAsync(target, ct);
            if (!wasComplete)
                await InvokeOnUiAsync(() =>
                {
                    HistoryLoading = true;
                    HistoryStatusText = "Indexing history...";
                });

            var rows = await _history.EnsureCompleteAsync(progress, ct, target);
            if (ct.IsCancellationRequested || _loadCts != ownerCts)
                return;

            var completeRows = rows.Select(r => r.ToCommitItem()).ToList();
            await InvokeOnUiAsync(() =>
            {
                _allRows = completeRows;
                OnPropertyChanged(nameof(LoadedCommits));
                if (_query.IsEmpty)
                    ApplyRows(BuildRows(FilteredIncoming(), FilteredLocal()), SelectedCommit?.FullSha);
                HistoryLoading = false;
            });

            await EnrichRemoteAndSigningAsync(target, ownerCts);
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer load.
        }
        catch
        {
            if (_loadCts == ownerCts)
                await InvokeOnUiAsync(() =>
                {
                    HistoryLoading = false;
                    HistoryStatusText = "History index failed.";
                });
        }
    }

    private async Task EnrichRemoteAndSigningAsync(
        HistoryTarget target,
        CancellationTokenSource ownerCts)
    {
        var ct = ownerCts.Token;
        try
        {
            RemoteSets? sets = null;
            var incomingRows = new List<CommitItem>();
            if (ShowsSections(target))
            {
                sets = await Task.Run(() => _git.ComputeRemoteSetsAsync(target.RefName, ct), ct).ConfigureAwait(true);
                if (ct.IsCancellationRequested || _loadCts != ownerCts)
                    return;

                // The SQLite cache only tracks the current branch's remote states.
                if (target.Scope == HistoryScope.CurrentBranch)
                    await _history.ApplyRemoteSetsAsync(sets, ct);

                // For a remote-branch view the incoming markers live on the listed rows
                // themselves (via ApplyRemoteSets); there is no separate incoming block.
                if (!IsRemoteBranchRef(target))
                    incomingRows = sets.Incoming.Select(ToItem).ToList();
            }

            if (ct.IsCancellationRequested || _loadCts != ownerCts)
                return;

            // Remote info first: signing checks can be slow and must not hold it back.
            await InvokeOnUiAsync(() =>
            {
                _graphWidthGrowOnly = false;
                if (sets is not null)
                {
                    _remoteSets = sets;
                    _lastRemoteName = sets.RemoteName ?? _lastRemoteName;
                    _lastRemoteUrl = sets.RemoteUrl ?? _lastRemoteUrl;
                    _remoteHistoryState = RemoteHistoryState.Loaded;
                    _incomingRows = incomingRows;
                    ApplyRemoteSets(sets, _allRows);
                }

                if (_query.IsEmpty)
                    ApplyRows(BuildRows(FilteredIncoming(), FilteredLocal()), SelectedCommit?.FullSha);
                else
                    DebounceFilter();
            });

            // Signing badges update the existing rows in place, no rebuild.
            await ApplySigningStatusesAsync(_allRows, ct);
            await ApplySigningStatusesAsync(incomingRows, ct);
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer load.
        }
        catch
        {
            if (_loadCts == ownerCts && ShowsSections(target))
                await InvokeOnUiAsync(() =>
                {
                    _graphWidthGrowOnly = false;
                    _remoteHistoryState = RemoteHistoryState.Unavailable;
                    if (_query.IsEmpty)
                        ApplyRows(BuildRows(FilteredIncoming(), FilteredLocal()), SelectedCommit?.FullSha);
                    else
                        DebounceFilter();
                });
        }
    }

    private enum RemoteHistoryState
    {
        Pending,
        Loaded,
        Unavailable,
    }

    private Task InvokeOnUiAsync(Action action)
    {
        if (_dispatcher is null || _dispatcher.IsDispatcherThread)
        {
            action();
            return Task.CompletedTask;
        }

        return _dispatcher.InvokeAsync(action);
    }

    partial void OnSelectedCommitChanged(CommitItem? value)
    {
        RemoveChangeFromCommitCommand.NotifyCanExecuteChanged();
        SelectParentCommitCommand.NotifyCanExecuteChanged();
        SelectChildCommitCommand.NotifyCanExecuteChanged();
        _ = LoadDiffAsync(value);
    }

    partial void OnShowOutgoingIncomingOnlyChanged(bool value)
    {
        if (value && _selectedTarget.Scope != HistoryScope.CurrentBranch)
        {
            _ = ShowScopeAsync(HistoryScope.CurrentBranch);
            return;
        }

        ApplyRows(BuildRows(FilteredIncoming(), FilteredLocal()), SelectedCommit?.FullSha);
    }

    private async Task LoadDiffAsync(CommitItem? commit)
    {
        _diffCts?.Cancel();

        if (commit == null)
        {
            DiffLoading = false;
            DiffStale = false;
            UpdateDiff(string.Empty, []);
            return;
        }

        var cts = new CancellationTokenSource();
        _diffCts = cts;
        var token = cts.Token;

        DiffStale = DiffFiles.Count > 0;

        try
        {
            var diffTask = Task.Run(() => _git.GetCommitDiffAsync(commit.FullSha, token), token);

            // Fast diffs replace the old one in a single pass; only slow ones show the loading state.
            if (await Task.WhenAny(diffTask, Task.Delay(DiffLoadingIndicatorDelay, token)) != diffTask)
            {
                if (token.IsCancellationRequested) return;
                DiffRemoteState = commit.RemoteState;
                DiffFiles = [];
                DiffHeader = string.Empty;
                DiffStale = false;
                DiffLoading = true;
            }

            var diff = await diffTask;
            if (token.IsCancellationRequested) return;
            DiffFiles = diff.Files.ToList();
            DiffHeader = diff.Header;
            DiffRemoteState = commit.RemoteState;
            DiffStale = false;
            DiffLoading = false;
        }
        catch (OperationCanceledException)
        {
            // Selection changed.
        }
        catch
        {
            if (token.IsCancellationRequested) return;
            DiffStale = false;
            DiffLoading = false;
            UpdateDiff(string.Empty, []);
        }
    }

    [RelayCommand]
    private void ClearAllFilters() => FilterText = string.Empty;
}

public sealed record HistoryScopeOption(HistoryScope Scope, string Label);
