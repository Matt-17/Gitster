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
    private RemoteHistoryState _remoteHistoryState = RemoteHistoryState.Loaded;
    private CommitQuery _query = CommitQuery.Parse(null);

    private CancellationTokenSource? _loadCts;
    private CancellationTokenSource? _filterCts;
    private CancellationTokenSource? _diffCts;
    private Task? _warmHistoryTask;
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
        return string.IsNullOrWhiteSpace(_git.RepositoryPath) ? Task.CompletedTask : LoadAsync();
    }

    public Func<DiffFileEntry?, Task>? RemoveChangeFromCommitAsync { get; set; }
    public Func<CommitItem, CommitItem, Task>? FixupDroppedCommitAsync { get; set; }

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
                _remoteHistoryState = target.Scope == HistoryScope.CurrentBranch
                    ? RemoteHistoryState.Pending
                    : RemoteHistoryState.Loaded;
                OnPropertyChanged(nameof(LoadedCommits));

                ApplyRows(BuildRows(FilteredIncoming(), FilteredLocal()), priorSha);
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

    public void ClearList()
    {
        _loadCts?.Cancel();
        _filterCts?.Cancel();
        _allRows = [];
        _incomingRows = [];
        _remoteSets = null;
        _remoteHistoryState = RemoteHistoryState.Loaded;
        _selectedTarget = HistoryTarget.CurrentBranch;
        SelectedScope = HistoryScope.CurrentBranch;
        SelectedRefDisplayName = string.Empty;
        OnPropertyChanged(nameof(IsNamedRefSelected));
        OnPropertyChanged(nameof(LoadedCommits));
        Items = [];
        GraphColumnWidth = GraphColumnMinWidth;
        SelectedCommit = null;
        HistoryLoading = false;
        HistoryStatusText = string.Empty;
        UpdateFilterStatus();
    }

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
        info.RefLabels);

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

    private void ApplyRemoteSets(RemoteSets sets, IEnumerable<CommitItem> rows)
    {
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
        if (_selectedTarget.Scope is HistoryScope.AllBranches or HistoryScope.Ref)
        {
            ApplyGraphRows(local);
            return new BuiltCommitRows(local.Cast<object>().ToList(), local.Count, 0);
        }

        int outgoingCount = 0;
        for (int i = 0; i < local.Count; i++)
            if (IsOutgoing(local[i])) outgoingCount++;

        var showRemoteSection = _selectedTarget.Scope == HistoryScope.CurrentBranch;
        var remotePending = _remoteHistoryState == RemoteHistoryState.Pending;
        var remoteUnavailable = _remoteHistoryState == RemoteHistoryState.Unavailable;
        var hasRemote = _remoteSets?.HasRemote ?? false;
        var hasTrackingBranch = _remoteSets?.HasTrackingBranch ?? false;

        var result = new List<object>(incoming.Count + local.Count + 2);

        if (showRemoteSection)
        {
            result.Add(new CommitSectionHeader(CommitSectionKind.RemoteIncoming, incoming.Count,
                _remoteSets?.RemoteName, _remoteSets?.RemoteUrl, remotePending));

            if (remotePending)
                result.Add(new CommitSectionEmptyRow(CommitSectionKind.RemoteIncoming, "checking tracking remote history..."));
            else if (remoteUnavailable)
                result.Add(new CommitSectionEmptyRow(CommitSectionKind.RemoteIncoming, "remote history unavailable"));
            else if (!hasRemote)
                result.Add(new CommitSectionEmptyRow(CommitSectionKind.RemoteIncoming, "no remote configured"));
            else if (!hasTrackingBranch)
                result.Add(new CommitSectionEmptyRow(CommitSectionKind.RemoteIncoming, "no tracking branch"));
            else if (incoming.Count == 0)
                result.Add(new CommitSectionEmptyRow(CommitSectionKind.RemoteIncoming, "no incoming commits"));
            else
            {
                ApplyGraphRows(incoming);
                result.AddRange(incoming);
            }
        }

        if (local.Count > 0 || showRemoteSection)
            result.Add(new CommitSectionHeader(CommitSectionKind.LocalOutgoing, outgoingCount));

        ApplyGraphRows(local);
        result.AddRange(local);
        return new BuiltCommitRows(result, local.Count, incoming.Count);
    }

    private void ApplyGraphRows(IReadOnlyList<CommitItem> commits)
    {
        var graphRows = _graphLayout.Layout(commits
            .Select(c => new CommitGraphNode(c.FullSha, c.ParentShas, c.RefLabels))
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

        GraphColumnWidth = GraphColumnWidthForLaneCount(maxLaneCount);
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
            await ApplySigningStatusesAsync(_allRows, ct);

            RemoteSets? sets = null;
            var incomingRows = new List<CommitItem>();
            if (target.Scope == HistoryScope.CurrentBranch)
            {
                sets = await Task.Run(() => _git.ComputeRemoteSetsAsync(ct), ct).ConfigureAwait(true);
                if (ct.IsCancellationRequested || _loadCts != ownerCts)
                    return;

                await _history.ApplyRemoteSetsAsync(sets, ct);
                incomingRows = sets.Incoming.Select(ToItem).ToList();
                await ApplySigningStatusesAsync(incomingRows, ct);
            }

            if (ct.IsCancellationRequested || _loadCts != ownerCts)
                return;

            await InvokeOnUiAsync(() =>
            {
                if (sets is not null)
                {
                    _remoteSets = sets;
                    _remoteHistoryState = RemoteHistoryState.Loaded;
                    _incomingRows = incomingRows;
                    ApplyRemoteSets(sets, _allRows);
                }

                if (_query.IsEmpty)
                    ApplyRows(BuildRows(FilteredIncoming(), FilteredLocal()), SelectedCommit?.FullSha);
                else
                    DebounceFilter();
            });
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer load.
        }
        catch
        {
            if (_loadCts == ownerCts && target.Scope == HistoryScope.CurrentBranch)
                await InvokeOnUiAsync(() =>
                {
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
            UpdateDiff(string.Empty, []);
            return;
        }

        var cts = new CancellationTokenSource();
        _diffCts = cts;
        var token = cts.Token;

        DiffRemoteState = commit.RemoteState;
        DiffFiles = [];
        DiffHeader = string.Empty;
        DiffLoading = true;

        try
        {
            var diff = await Task.Run(() => _git.GetCommitDiffAsync(commit.FullSha, token), token);
            if (token.IsCancellationRequested) return;
            DiffFiles = diff.Files.ToList();
            DiffHeader = diff.Header;
            DiffRemoteState = commit.RemoteState;
            DiffLoading = false;
        }
        catch (OperationCanceledException)
        {
            // Selection changed.
        }
        catch
        {
            DiffLoading = false;
            UpdateDiff(string.Empty, []);
        }
    }

    [RelayCommand]
    private void ClearAllFilters() => FilterText = string.Empty;
}

public sealed record HistoryScopeOption(HistoryScope Scope, string Label);
