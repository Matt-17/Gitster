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

    private readonly IGitBackend _git;
    private readonly CommitHistoryService _history;

    /// <summary>Shared UI preferences (date display mode, gravatar) for row bindings.</summary>
    public Gitster.Services.UiPreferencesService Ui { get; }

    private List<CommitItem> _allRows = [];
    private List<CommitItem> _incomingRows = [];
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

    /// <summary>All cached commits for the current local branch, newest first.</summary>
    public IReadOnlyList<CommitItem> LoadedCommits => _allRows;

    public event Action? FocusSearchRequested;

    [RelayCommand]
    private void FocusSearch() => FocusSearchRequested?.Invoke();

    [ObservableProperty]
    public partial CommitItem? SelectedCommit { get; set; }

    /// <summary>All currently selected commits (populated by the view's SelectionChanged handler).</summary>
    public List<CommitItem> SelectedCommits { get; set; } = [];

    [ObservableProperty]
    public partial string FilterText { get; set; } = string.Empty;

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
    partial void OnDiffLoadingChanged(bool value) => OnPropertyChanged(nameof(DiffHeaderDisplay));

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

            await _history.OpenAsync(_git.RepositoryPath, ct, progress);

            progress?.Report(new RepositoryLoadProgress(
                "Loading full history",
                "Preparing virtualized commit rows."));
            var rows = await _history.EnsureCompleteAsync(progress, ct);
            if (ct.IsCancellationRequested) return;

            var newAllRows = rows.Select(r => r.ToCommitItem()).ToList();

            progress?.Report(new RepositoryLoadProgress(
                "Computing remote state",
                "Checking incoming and outgoing commits.",
                newAllRows.Count));
            var sets = await Task.Run(() => _git.ComputeRemoteSetsAsync(ct), ct).ConfigureAwait(true);
            if (ct.IsCancellationRequested) return;

            await _history.ApplyRemoteSetsAsync(sets, ct);
            ApplyRemoteSets(sets, newAllRows);
            var newIncomingRows = sets.Incoming.Select(ToItem).ToList();

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                _allRows = newAllRows;
                _incomingRows = newIncomingRows;
                _remoteSets = sets;

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
        Items = [];
        SelectedCommit = null;
        HistoryLoading = false;
        HistoryStatusText = string.Empty;
        UpdateFilterStatus();
    }

    private static CommitItem ToItem(CommitInfo info) => new(
        info.Message,
        info.Date,
        info.Sha,
        info.AuthorName,
        info.AuthorEmail,
        info.RemoteState,
        info.FullSha,
        info.OrphanedPairSha);

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
            var builtRows = BuildRows(matchedIncoming, matchedLocal);

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

    private List<CommitItem> FilteredLocal() =>
        _query.IsEmpty ? _allRows : _allRows.Where(c => Match(_query, c)).ToList();

    private static bool IsOutgoing(CommitItem c) =>
        c.RemoteState is CommitRemoteState.LocalOnly or CommitRemoteState.NoTrackingBranch;

    private BuiltCommitRows BuildRows(List<CommitItem> incoming, List<CommitItem> local)
    {
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
                result.AddRange(incoming);
        }

        if (local.Count > 0 || hasRemote)
            result.Add(new CommitSectionHeader(CommitSectionKind.LocalOutgoing, outgoingCount));

        result.AddRange(local);
        return new BuiltCommitRows(result, local.Count, incoming.Count);
    }

    private void ApplyRows(BuiltCommitRows rows, string? priorSha)
    {
        Items = rows.Items;
        UpdateFilterStatus();
        HistoryStatusText = StatusForRows(rows.LocalCount, rows.IncomingCount);
        SelectAfterRebuild(priorSha);
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
        var count = HasActiveFilters ? localCount + incomingCount : _allRows.Count;
        return HasActiveFilters
            ? $"{count:N0} matching commit{(count == 1 ? "" : "s")}"
            : $"{count:N0} commit{(count == 1 ? "" : "s")}";
    }

    private sealed record BuiltCommitRows(IReadOnlyList<object> Items, int LocalCount, int IncomingCount);

    partial void OnSelectedCommitChanged(CommitItem? value) => _ = LoadDiffAsync(value);

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
