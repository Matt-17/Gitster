using System.Windows;
using System.Windows.Threading;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Gitster.Models;
using Gitster.Services.Git;
using Gitster.Services.Search;

namespace Gitster.ViewModels;

/// <summary>
/// Owns the commit list: progressive HEAD→parent loading (A0.1), virtualization-friendly
/// flat item collection with section headers (A0.2/A5), background remote-state computation
/// (A0.4), debounced query filtering (A0.5/A8), and lazy cancellable diff loading (A0.3).
/// </summary>
public partial class CommitListViewModel : BaseViewModel
{
    private const int BatchSize = 200;
    private const int FilterDebounceMs = 150;

    private readonly IGitBackend _git;
    private readonly Action _openFilter;
    private readonly Action _clearDialogFilters;

    /// <summary>Shared UI preferences (date display mode, gravatar) for row bindings.</summary>
    public Gitster.Services.UiPreferencesService Ui { get; }

    private List<CommitItem> _allRows = [];          // full HEAD→parent list (set once per load)
    private List<CommitItem> _incomingRows = [];     // commits on the tracking remote, not local
    private RemoteSets? _remoteSets;
    private CommitQuery _query = CommitQuery.Parse(null);

    private bool _dialogHasActiveFilters;
    private string _dialogFilterStatusText = string.Empty;

    private CancellationTokenSource? _loadCts;
    private CancellationTokenSource? _filterCts;
    private CancellationTokenSource? _diffCts;

    public CommitListViewModel(IGitBackend git, Gitster.Services.UiPreferencesService ui,
        Action openFilter, Action clearDialogFilters)
    {
        _git = git;
        Ui = ui;
        _openFilter = openFilter;
        _clearDialogFilters = clearDialogFilters;
    }

    /// <summary>Flat list of <see cref="CommitSectionHeader"/> and <see cref="CommitItem"/> rows.</summary>
    public RangeObservableCollection<object> Items { get; } = [];

    /// <summary>The fully-loaded HEAD→parent commits (for author lists, range tools, etc.).</summary>
    public IReadOnlyList<CommitItem> AllCommits => _allRows;

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
    public partial string DiffHeader { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool DiffLoading { get; set; }

    [ObservableProperty]
    public partial List<DiffFileEntry> DiffFiles { get; set; } = [];

    [ObservableProperty]
    public partial CommitRemoteState DiffRemoteState { get; set; }

    public string DiffHeaderDisplay =>
        DiffLoading ? "loading…"
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

    // ── Loading ────────────────────────────────────────────────────────────

    /// <summary>
    /// Loads the commit list for the current repository. The dialog filter (author/date)
    /// is applied at the git level; the inline query filter is applied in-memory on top.
    /// </summary>
    public async Task LoadAsync(CommitFilter? dialogFilter, bool dialogHasActiveFilters, string dialogFilterStatusText)
    {
        _dialogHasActiveFilters = dialogHasActiveFilters;
        _dialogFilterStatusText = dialogFilterStatusText;

        _loadCts?.Cancel();
        var cts = new CancellationTokenSource();
        _loadCts = cts;
        var ct = cts.Token;

        var priorSha = SelectedCommit?.FullSha;

        Items.Clear();
        _allRows = [];
        _incomingRows = [];
        _remoteSets = null;

        var loaded = new List<CommitItem>(1024);

        try
        {
            await Task.Run(async () =>
            {
                var batch = new List<CommitItem>(BatchSize);
                bool firstBatch = true;

                await foreach (var info in _git.EnumerateCommitsAsync(dialogFilter, ct).ConfigureAwait(false))
                {
                    batch.Add(new CommitItem(
                        info.Message, info.Date, info.Sha, info.AuthorName,
                        info.AuthorEmail, info.RemoteState, info.FullSha, info.OrphanedPairSha));

                    if (batch.Count >= BatchSize)
                    {
                        await DispatchBatchAsync(loaded, batch, firstBatch, ct).ConfigureAwait(false);
                        batch = new List<CommitItem>(BatchSize);
                        firstBatch = false;
                    }
                }

                if (batch.Count > 0)
                    await DispatchBatchAsync(loaded, batch, firstBatch, ct).ConfigureAwait(false);
            }, ct).ConfigureAwait(true);

            if (ct.IsCancellationRequested) return;

            // Remote-state computation happens after the list is already visible.
            var sets = await Task.Run(() => _git.ComputeRemoteSetsAsync(ct), ct).ConfigureAwait(true);
            if (ct.IsCancellationRequested) return;

            _allRows = loaded;
            ApplyRemoteSets(sets);
            BuildAndApply(FilteredIncoming(), FilteredLocal(), priorSha);
        }
        catch (OperationCanceledException) { /* superseded by a newer load */ }
    }

    private async Task DispatchBatchAsync(List<CommitItem> loaded, List<CommitItem> batch, bool first, CancellationToken ct)
    {
        var toAdd = batch;
        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            if (ct.IsCancellationRequested) return;
            loaded.AddRange(toAdd);
            // Show rows immediately only when no inline filter is pending; otherwise the
            // final BuildAndApply will lay them out. Headers/dots come after remote sets.
            if (_query.IsEmpty)
                foreach (var c in toAdd) Items.Add(c);
        }, first ? DispatcherPriority.Normal : DispatcherPriority.Background);
    }

    public void ClearList()
    {
        _loadCts?.Cancel();
        _allRows = [];
        _incomingRows = [];
        _remoteSets = null;
        Items.Clear();
        SelectedCommit = null;
        UpdateFilterStatus();
    }

    private void ApplyRemoteSets(RemoteSets sets)
    {
        _remoteSets = sets;
        var outgoing = sets.OutgoingFullShas;
        var orphans = sets.OrphanedPairs;

        foreach (var row in _allRows)
        {
            row.RemoteState = outgoing.Contains(row.FullSha)
                ? CommitRemoteState.LocalOnly
                : sets.HasTrackingBranch ? CommitRemoteState.OnRemote : CommitRemoteState.NoTrackingBranch;
            row.OrphanedPairSha = orphans.TryGetValue(row.FullSha, out var p) ? p : null;
        }

        _incomingRows = sets.Incoming.Select(c =>
        {
            var item = new CommitItem(c.Message, c.Date, c.Sha, c.AuthorName, c.AuthorEmail,
                CommitRemoteState.Incoming, c.FullSha);
            if (orphans.TryGetValue(c.FullSha, out var p)) item.OrphanedPairSha = p;
            return item;
        }).ToList();
    }

    // ── Filtering (A8 / A0.5) ───────────────────────────────────────────────

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
        var rows = _allRows;          // immutable after load → safe to read off-thread
        var incoming = _incomingRows;

        Task.Run(async () =>
        {
            try { await Task.Delay(FilterDebounceMs, token); }
            catch (TaskCanceledException) { return; }
            if (token.IsCancellationRequested) return;

            var matchedLocal = query.IsEmpty ? rows : rows.Where(c => Match(query, c)).ToList();
            var matchedIncoming = query.IsEmpty ? incoming : incoming.Where(c => Match(query, c)).ToList();

            if (token.IsCancellationRequested) return;
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (token.IsCancellationRequested) return;
                BuildAndApply(matchedIncoming, matchedLocal, SelectedCommit?.FullSha);
            });
        });
    }

    private static bool Match(CommitQuery query, CommitItem c) =>
        query.Matches(c.Message, c.AuthorName, c.AuthorEmail, c.FullSha);

    private List<CommitItem> FilteredLocal() =>
        _query.IsEmpty ? _allRows : _allRows.Where(c => Match(_query, c)).ToList();

    private List<CommitItem> FilteredIncoming() =>
        _query.IsEmpty ? _incomingRows : _incomingRows.Where(c => Match(_query, c)).ToList();

    private static bool IsOutgoing(CommitItem c) =>
        c.RemoteState is CommitRemoteState.LocalOnly or CommitRemoteState.NoTrackingBranch;

    /// <summary>Constructs the flat [Remote header][incoming][Local header][outgoing+synced] list.</summary>
    private void BuildAndApply(List<CommitItem> incoming, List<CommitItem> local, string? priorSha)
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
            result.AddRange(incoming);
        }

        if (outgoingCount > 0)
            result.Add(new CommitSectionHeader(CommitSectionKind.LocalOutgoing, outgoingCount));

        result.AddRange(local);

        Items.ReplaceAll(result);
        UpdateFilterStatus();
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
        var hasLiveFilter = !_query.IsEmpty;
        HasActiveFilters = hasLiveFilter || _dialogHasActiveFilters;

        if (!HasActiveFilters)
        {
            FilterStatusText = string.Empty;
            return;
        }

        var parts = new List<string>();
        if (!string.IsNullOrEmpty(_dialogFilterStatusText))
            parts.Add(_dialogFilterStatusText);
        if (hasLiveFilter)
            parts.Add($"\"{FilterText.Trim()}\"");

        FilterStatusText = string.Join(", ", parts);
    }

    // ── Diff loading (A0.3) ──────────────────────────────────────────────────

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
        catch (OperationCanceledException) { /* selection changed — ignore */ }
        catch
        {
            DiffLoading = false;
            UpdateDiff(string.Empty, []);
        }
    }

    // ── Commands ─────────────────────────────────────────────────────────────

    [RelayCommand]
    private void OpenFilter() => _openFilter();

    [RelayCommand]
    private void ClearAllFilters()
    {
        FilterText = string.Empty;
        _clearDialogFilters();
    }
}
