using System.Windows;
using System.Windows.Threading;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Gitster.Models;
using Gitster.Services.Git;
using Gitster.Services.History;
using Gitster.Services.Search;

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
    private readonly CommitGraphLayoutService _graphLayout = new();

    /// <summary>Shared UI preferences (date display mode, gravatar) for row bindings.</summary>
    public Gitster.Services.UiPreferencesService Ui { get; }

    private List<CommitItem> _allRows = [];
    private List<CommitItem> _incomingRows = [];
    private List<CommitItem> _selectedCommits = [];
    private RemoteSets? _remoteSets;
    private CommitQuery _query = CommitQuery.Parse(null);

    private CancellationTokenSource? _loadCts;
    private CancellationTokenSource? _filterCts;
    private CancellationTokenSource? _diffCts;

    public CommitListViewModel(
        IGitBackend git,
        CommitHistoryService history,
        Gitster.Services.UiPreferencesService ui)
    {
        _git = git;
        _history = history;
        Ui = ui;
    }

    /// <summary>Flat list of <see cref="CommitSectionHeader"/> and <see cref="CommitItem"/> rows.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<object> Items { get; set; } = [];

    partial void OnItemsChanged(IReadOnlyList<object> value)
    {
        SelectParentCommitCommand.NotifyCanExecuteChanged();
        SelectChildCommitCommand.NotifyCanExecuteChanged();
    }

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

    [RelayCommand]
    private void FocusSearch() => FocusSearchRequested?.Invoke();

    partial void OnSelectedScopeChanged(HistoryScope value)
    {
        if (!string.IsNullOrWhiteSpace(_git.RepositoryPath))
            _ = LoadAsync();
    }

    public Func<DiffFileEntry?, Task>? RemoveChangeFromCommitAsync { get; set; }

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
                await Application.Current.Dispatcher.InvokeAsync(ClearList, DispatcherPriority.Normal);
                return;
            }

            var scope = SelectedScope;
            await _history.OpenAsync(_git.RepositoryPath, scope, ct, progress);

            progress?.Report(new RepositoryLoadProgress(
                "Loading full history",
                "Preparing virtualized commit rows."));
            var rows = await _history.EnsureCompleteAsync(progress, ct, scope);
            if (ct.IsCancellationRequested) return;

            var newAllRows = rows.Select(r => r.ToCommitItem()).ToList();

            RemoteSets sets = RemoteSets.Empty;
            var newIncomingRows = new List<CommitItem>();
            if (scope == HistoryScope.CurrentBranch)
            {
                progress?.Report(new RepositoryLoadProgress(
                    "Computing remote state",
                    "Checking incoming and outgoing commits.",
                    newAllRows.Count));
                sets = await Task.Run(() => _git.ComputeRemoteSetsAsync(ct), ct).ConfigureAwait(true);
                if (ct.IsCancellationRequested) return;

                await _history.ApplyRemoteSetsAsync(sets, ct);
                ApplyRemoteSets(sets, newAllRows);
                newIncomingRows = sets.Incoming.Select(ToItem).ToList();
            }

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                _allRows = newAllRows;
                _incomingRows = newIncomingRows;
                _remoteSets = sets;
                OnPropertyChanged(nameof(LoadedCommits));

                var remoteRows = BuildRows(FilteredIncoming(), FilteredLocal());
                ApplyRows(remoteRows, priorSha);
            }, DispatcherPriority.Normal);
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer load.
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
        _allRows = [];
        _incomingRows = [];
        _remoteSets = null;
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
        var cts = new CancellationTokenSource();
        _filterCts = cts;
        var token = cts.Token;
        var query = _query;

        Task.Run(async () =>
        {
            try { await Task.Delay(FilterDebounceMs, token); }
            catch (TaskCanceledException) { return; }
            if (token.IsCancellationRequested) return;

            var rows = _allRows;
            var incoming = _incomingRows;
            var matchedLocal = query.IsEmpty
                ? rows
                : rows.Where(c => Match(query, c)).ToList();
            var matchedIncoming = query.IsEmpty
                ? incoming
                : incoming.Where(c => Match(query, c)).ToList();
            var builtRows = BuildRows(matchedIncoming, ApplyOutgoingIncomingFilter(matchedLocal));

            try
            {
                if (token.IsCancellationRequested) return;

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    ApplyRows(builtRows, SelectedCommit?.FullSha);
                });
            }
            catch (OperationCanceledException)
            {
                // Superseded by another filter.
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
        if (SelectedScope == HistoryScope.AllBranches)
        {
            ApplyGraphRows(local);
            return new BuiltCommitRows(local.Cast<object>().ToList(), local.Count, 0);
        }

        int outgoingCount = 0;
        for (int i = 0; i < local.Count; i++)
            if (IsOutgoing(local[i])) outgoingCount++;

        bool hasRemote = _remoteSets?.HasRemote ?? false;

        var result = new List<object>(incoming.Count + local.Count + 2);

        if (hasRemote)
        {
            result.Add(new CommitSectionHeader(CommitSectionKind.RemoteIncoming, incoming.Count,
                _remoteSets?.RemoteName, _remoteSets?.RemoteUrl));

            if (incoming.Count == 0)
                result.Add(new CommitSectionEmptyRow(CommitSectionKind.RemoteIncoming, "no incoming commits"));
            else
            {
                ApplyGraphRows(incoming);
                result.AddRange(incoming);
            }
        }

        if (local.Count > 0 || hasRemote)
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
        if (SelectedScope == HistoryScope.AllBranches)
        {
            return HasActiveFilters
                ? $"{count:N0} matching commit{(count == 1 ? "" : "s")}"
                : $"{count:N0} commit{(count == 1 ? "" : "s")} across branches";
        }

        return HasActiveFilters
            ? $"{count:N0} matching commit{(count == 1 ? "" : "s")}"
            : $"{count:N0} commit{(count == 1 ? "" : "s")}";
    }

    private sealed record BuiltCommitRows(IReadOnlyList<object> Items, int LocalCount, int IncomingCount);

    partial void OnSelectedCommitChanged(CommitItem? value)
    {
        RemoveChangeFromCommitCommand.NotifyCanExecuteChanged();
        SelectParentCommitCommand.NotifyCanExecuteChanged();
        SelectChildCommitCommand.NotifyCanExecuteChanged();
        _ = LoadDiffAsync(value);
    }

    partial void OnShowOutgoingIncomingOnlyChanged(bool value)
    {
        if (value && SelectedScope != HistoryScope.CurrentBranch)
        {
            SelectedScope = HistoryScope.CurrentBranch;
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
