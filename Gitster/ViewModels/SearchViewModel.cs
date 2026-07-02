using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using System.Windows;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Gitster.Models;
using Gitster.Services;
using Gitster.Services.Git;
using Gitster.Services.Features;
using Gitster.Services.History;
using Gitster.Services.Search;
using Gitster.Views;

using Microsoft.Win32;

namespace Gitster.ViewModels;

public enum SearchKind { Commits, Pickaxe, DiffRegex, Blame, RangeDiff, CompareRefs, Reflog }

/// <summary>
/// The Phase-4 Search &amp; Analysis mode (plan B1–B6): a query bar + type selector that swaps
/// the parameter inputs and result area, with selection driving the shared DiffView.
/// </summary>
public partial class SearchViewModel : BaseViewModel
{
    private readonly IGitBackend _git;
    private readonly CommitHistoryService _history;
    private readonly IWindowService _windowService;
    private readonly GitFeatureService _features;
    private CancellationTokenSource? _cts;

    public SearchViewModel(IGitBackend git, CommitHistoryService history, IWindowService? windowService)
        : this(git, history, windowService, new GitFeatureService())
    {
    }

    public SearchViewModel(
        IGitBackend git,
        CommitHistoryService history,
        IWindowService? windowService,
        GitFeatureService features)
    {
        _git = git;
        _history = history;
        _windowService = windowService ?? new WindowService();
        _features = features;
    }

    // ── Type selection ───────────────────────────────────────────────────
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCommits), nameof(IsPickaxe), nameof(IsDiffRegex),
        nameof(IsBlame), nameof(IsRangeDiff), nameof(IsCompareRefs),
        nameof(IsReflog), nameof(IsCommitResults), nameof(QueryHint), nameof(HasAnyResults), nameof(IsEmpty))]
    public partial SearchKind CurrentKind { get; set; } = SearchKind.Commits;

    public bool IsCommits     => CurrentKind == SearchKind.Commits;
    public bool IsPickaxe     => CurrentKind == SearchKind.Pickaxe;
    public bool IsDiffRegex   => CurrentKind == SearchKind.DiffRegex;
    public bool IsBlame       => CurrentKind == SearchKind.Blame;
    public bool IsRangeDiff   => CurrentKind == SearchKind.RangeDiff;
    public bool IsCompareRefs => CurrentKind == SearchKind.CompareRefs;
    public bool IsReflog      => CurrentKind == SearchKind.Reflog;

    /// <summary>True for the kinds whose result is a commit list (drives the shared list + diff).</summary>
    public bool IsCommitResults => CurrentKind is SearchKind.Commits or SearchKind.Pickaxe
        or SearchKind.DiffRegex or SearchKind.CompareRefs;

    public string QueryHint => CurrentKind switch
    {
        SearchKind.Commits   => "Query the loaded history — author:Name  message:\"fix\"  after:2026-01-01",
        SearchKind.Pickaxe   => "Find commits that added or removed this exact string (git log -S)",
        SearchKind.DiffRegex => "Find commits whose diff matches this regex (git log -G)",
        SearchKind.Reflog    => "Load HEAD reflog entries, then create a branch or copy a SHA from a row",
        _ => string.Empty,
    };

    [RelayCommand]
    private void SetKind(SearchKind kind) => CurrentKind = kind;

    partial void OnCurrentKindChanged(SearchKind value)
    {
        OnPropertyChanged(nameof(HasAnyResults));
        OnPropertyChanged(nameof(IsEmpty));
    }

    // ── Inputs ────────────────────────────────────────────────────────────
    [ObservableProperty] public partial string QueryText { get; set; } = string.Empty;
    [ObservableProperty] public partial string PathFilter { get; set; } = string.Empty;
    [ObservableProperty] public partial string BlameFilePath { get; set; } = string.Empty;
    [ObservableProperty] public partial bool BlameIgnoreWhitespace { get; set; } = true;
    [ObservableProperty] public partial bool BlameFollowMoves { get; set; } = true;
    [ObservableProperty] public partial string Range1 { get; set; } = "@{u}";
    [ObservableProperty] public partial string Range2 { get; set; } = "HEAD";
    [ObservableProperty] public partial string BaseRef { get; set; } = string.Empty;
    [ObservableProperty] public partial string CompareRef { get; set; } = "HEAD";
    [ObservableProperty] public partial bool ThreeDot { get; set; }

    // ── Results ─────────────────────────────────────────────────────────────
    public ObservableCollection<CommitItem> Results { get; } = [];
    public ObservableCollection<BlameLine> BlameLines { get; } = [];
    public ObservableCollection<RangeDiffEntry> RangeResults { get; } = [];
    public ObservableCollection<ReflogEntry> ReflogEntries { get; } = [];

    [ObservableProperty] public partial CommitItem? SelectedResult { get; set; }
    [ObservableProperty] public partial BlameLine? SelectedBlameLine { get; set; }
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(BranchFromReflogCommand))]
    [NotifyCanExecuteChangedFor(nameof(CopyReflogShaCommand))]
    public partial ReflogEntry? SelectedReflogEntry { get; set; }

    [ObservableProperty] public partial List<DiffFileEntry> DiffFiles { get; set; } = [];
    [ObservableProperty] public partial string DiffHeader { get; set; } = "no selection";
    [ObservableProperty] public partial string StatusText { get; set; } = string.Empty;
    [ObservableProperty] public partial string Explanation { get; set; } = string.Empty;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial bool IsBusy { get; set; }

    public string ResultCountText => CurrentKind == SearchKind.Blame
        ? $"{BlameLines.Count} lines"
        : CurrentKind == SearchKind.RangeDiff
            ? $"{RangeResults.Count} commits"
            : CurrentKind == SearchKind.Reflog
                ? $"{ReflogEntries.Count} reflog entries"
            : $"{Results.Count} commits";

    public bool HasAnyResults => CurrentKind switch
    {
        SearchKind.Blame => BlameLines.Count > 0,
        SearchKind.RangeDiff => RangeResults.Count > 0,
        SearchKind.Reflog => ReflogEntries.Count > 0,
        _ => Results.Count > 0,
    };

    public bool IsEmpty => !IsBusy && !HasAnyResults;

    partial void OnSelectedResultChanged(CommitItem? value) => _ = LoadDiffAsync(value?.FullSha);
    partial void OnSelectedBlameLineChanged(BlameLine? value) => _ = LoadDiffAsync(value?.Sha);

    private async Task LoadDiffAsync(string? sha)
    {
        if (string.IsNullOrEmpty(sha)) return;
        try
        {
            var diff = await Task.Run(() => _git.GetCommitDiffAsync(sha));
            DiffFiles = diff.Files.ToList();
            DiffHeader = diff.Header;
        }
        catch { DiffFiles = []; DiffHeader = "—"; }
    }

    // ── Run ─────────────────────────────────────────────────────────────────
    [RelayCommand]
    private async Task Run()
    {
        _cts?.Cancel();
        var cts = new CancellationTokenSource();
        _cts = cts;
        var ct = cts.Token;

        IsBusy = true;
        StatusText = "Searching…";
        Explanation = string.Empty;
        try
        {
            switch (CurrentKind)
            {
                case SearchKind.Commits:   await RunCommitsQueryAsync(ct); break;
                case SearchKind.Pickaxe:   await RunPickaxeAsync(ct); break;
                case SearchKind.DiffRegex: await RunDiffRegexAsync(ct); break;
                case SearchKind.Blame:     await RunBlameAsync(ct); break;
                case SearchKind.RangeDiff: await RunRangeDiffAsync(ct); break;
                case SearchKind.CompareRefs: await RunCompareAsync(ct); break;
                case SearchKind.Reflog: await RunReflogAsync(ct); break;
            }
            StatusText = ResultCountText;
            NotifyResultsChanged();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            StatusText = "Search failed";
            _windowService.Warning(ex.Message, "Search");
        }
        finally { IsBusy = false; }
    }

    public async Task RunCompareRefsAsync(string baseRef, string compareRef, bool threeDot = false)
    {
        CurrentKind = SearchKind.CompareRefs;
        BaseRef = baseRef;
        CompareRef = compareRef;
        ThreeDot = threeDot;
        await Run();
    }

    private async Task RunCommitsQueryAsync(CancellationToken ct)
    {
        var query = CommitQuery.Parse(QueryText);
        var matches = await _history.SearchAsync(query, maxResults: 5000, ct);
        ReplaceResults(matches.Select(r => r.ToCommitItem()));
    }

    private async Task RunPickaxeAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(QueryText)) { ReplaceResults([]); return; }
        var hits = await _git.PickaxeSearchAsync(QueryText, NullIfBlank(PathFilter), ct);
        ReplaceResults(hits.Select(ToItem));
    }

    private async Task RunDiffRegexAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(QueryText)) { ReplaceResults([]); return; }
        // Validate the regex client-side before invoking git.
        try { _ = new Regex(QueryText); }
        catch (ArgumentException ex)
        {
            StatusText = "Invalid regex";
            _windowService.Warning($"Invalid regular expression:\n{ex.Message}", "Diff regex");
            return;
        }
        var hits = await _git.DiffRegexSearchAsync(QueryText, NullIfBlank(PathFilter), ct);
        ReplaceResults(hits.Select(ToItem));
    }

    private async Task RunBlameAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(BlameFilePath))
        {
            _windowService.Info("Pick a file to blame first.", "Blame");
            return;
        }
        var lines = await _git.BlameAsync(BlameFilePath, BlameIgnoreWhitespace, BlameFollowMoves, ct);
        BlameLines.Clear();
        foreach (var l in lines) BlameLines.Add(l);
        NotifyResultsChanged();
    }

    private async Task RunRangeDiffAsync(CancellationToken ct)
    {
        var entries = await _git.RangeDiffAsync(Range1.Trim(), Range2.Trim(), ct);
        RangeResults.Clear();
        foreach (var e in entries) RangeResults.Add(e);
        NotifyResultsChanged();
    }

    private async Task RunReflogAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_git.RepositoryPath))
        {
            _windowService.Info("Open a repository before loading the reflog.", "Reflog");
            return;
        }

        var entries = await _features.GetReflogAsync(_git.RepositoryPath, ct: ct);
        ReflogEntries.Clear();
        foreach (var entry in entries)
            ReflogEntries.Add(entry);
        SelectedReflogEntry = ReflogEntries.FirstOrDefault();
        NotifyResultsChanged();
    }

    private async Task RunCompareAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(BaseRef) || string.IsNullOrWhiteSpace(CompareRef))
        {
            _windowService.Info("Enter both refs to compare.", "Compare refs");
            return;
        }
        var result = await _git.CompareRefsAsync(BaseRef.Trim(), CompareRef.Trim(), ThreeDot, ct);
        ReplaceResults(result.Commits.Select(ToItem));
        DiffFiles = result.Diff.Files.ToList();
        DiffHeader = result.Diff.Header;
        Explanation = result.Explanation;
    }

    /// <summary>Loads the prior HEAD tip from the reflog into Range1 (before/after rebase preset, B5).</summary>
    [RelayCommand]
    private async Task UseRebasePreset()
    {
        var prior = await _git.GetPriorTipFromReflogAsync();
        if (string.IsNullOrEmpty(prior))
        {
            _windowService.Info("No prior HEAD position found in the reflog.", "Range-diff");
            return;
        }
        Range1 = prior;
        Range2 = "HEAD";
    }

    [RelayCommand]
    private void PickBlameFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Pick a file to blame",
            InitialDirectory = _git.RepositoryPath,
        };
        if (_windowService.ShowDialog(dialog) != true) return;

        var root = _git.RepositoryPath?.TrimEnd('\\', '/');
        var picked = dialog.FileName;
        // Store a repo-relative path for git.
        BlameFilePath = !string.IsNullOrEmpty(root) && picked.StartsWith(root, StringComparison.OrdinalIgnoreCase)
            ? picked[(root.Length + 1)..].Replace('\\', '/')
            : picked;
    }

    public void Clear()
    {
        Results.Clear();
        BlameLines.Clear();
        RangeResults.Clear();
        ReflogEntries.Clear();
        SelectedReflogEntry = null;
        DiffFiles = [];
        DiffHeader = "no selection";
        StatusText = string.Empty;
        Explanation = string.Empty;
        NotifyResultsChanged();
    }

    private void ReplaceResults(IEnumerable<CommitItem> items)
    {
        Results.Clear();
        foreach (var i in items) Results.Add(i);
        SelectedResult = Results.FirstOrDefault();
        NotifyResultsChanged();
    }

    [RelayCommand(CanExecute = nameof(CanUseSelectedReflogEntry))]
    private async Task BranchFromReflog()
    {
        if (SelectedReflogEntry is not { } entry)
            return;

        var shortSha = entry.Sha.Length >= 7 ? entry.Sha[..7] : entry.Sha;
        var dialog = new TextInputDialog
        {
            Title = "Create branch from reflog",
            Prompt = $"New branch name at {entry.Selector} ({shortSha}):",
            Value = $"rescue/{shortSha}",
        };

        if (_windowService.ShowDialog(dialog) != true)
            return;

        var branchName = dialog.Value.Trim();
        if (string.IsNullOrWhiteSpace(branchName))
            return;

        try
        {
            await _git.CreateBranchAsync(branchName, entry.Sha);
            _windowService.Info($"Created branch '{branchName}' at {shortSha}.", "Reflog");
        }
        catch (Exception ex)
        {
            _windowService.Warning(ex.Message, "Reflog");
        }
    }

    [RelayCommand(CanExecute = nameof(CanUseSelectedReflogEntry))]
    private void CopyReflogSha()
    {
        if (SelectedReflogEntry is not { } entry)
            return;

        try
        {
            Clipboard.SetText(entry.Sha);
        }
        catch (Exception ex)
        {
            _windowService.Warning(ex.Message, "Copy SHA");
        }
    }

    private bool CanUseSelectedReflogEntry() =>
        SelectedReflogEntry is { Sha.Length: > 0 };

    private void NotifyResultsChanged()
    {
        OnPropertyChanged(nameof(ResultCountText));
        OnPropertyChanged(nameof(HasAnyResults));
        OnPropertyChanged(nameof(IsEmpty));
    }

    private static CommitItem ToItem(CommitInfo c) =>
        new(c.Message, c.Date, c.Sha, c.AuthorName, c.AuthorEmail, c.RemoteState, c.FullSha);

    private static string? NullIfBlank(string s) => string.IsNullOrWhiteSpace(s) ? null : s;
}
