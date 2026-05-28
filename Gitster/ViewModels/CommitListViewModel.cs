using System.ComponentModel;
using System.Windows.Data;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Gitster.Models;

namespace Gitster.ViewModels;

public partial class CommitListViewModel : BaseViewModel
{
    private List<CommitItem> _baseCommits = [];
    private bool _dialogHasActiveFilters;
    private string _dialogFilterStatusText = string.Empty;
    private bool _hasTrackingBranch = true;

    private readonly Action _openFilter;
    private readonly Action _clearDialogFilters;

    // ── Section counts (real commits only, excluding placeholders) ───────────
    public int  IncomingCount      { get; private set; }
    public int  OutgoingCount      { get; private set; }
    public int  SyncedCount        { get; private set; }
    public bool HasTrackingBranch  { get; private set; } = true;

    public string IncomingCountText        => HasTrackingBranch ? $"\u00B7 {IncomingCount}" : "\u00B7 unknown";
    public string OutgoingCountText        => $"\u00B7 {OutgoingCount}";
    public string SyncedCountText          => $"\u00B7 {SyncedCount}";
    public string? IncomingNoTrackingTooltip => HasTrackingBranch ? null : "No tracking branch configured";

    public CommitListViewModel(Action openFilter, Action clearDialogFilters)
    {
        _openFilter = openFilter;
        _clearDialogFilters = clearDialogFilters;
        GroupedCommits = CollectionViewSource.GetDefaultView(new List<CommitItem>());
    }

    public event Action? FocusSearchRequested;

    [RelayCommand]
    private void FocusSearch() => FocusSearchRequested?.Invoke();

    [ObservableProperty]
    public partial List<CommitItem> Commits { get; set; } = [];

    /// <summary>Grouped view of <see cref="Commits"/> for the ListView (groups by GroupLabel).</summary>
    public ICollectionView GroupedCommits { get; private set; }

    partial void OnCommitsChanged(List<CommitItem> value)
    {
        var view = CollectionViewSource.GetDefaultView(value);
        view.GroupDescriptions.Clear();
        view.SortDescriptions.Clear();
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(CommitItem.GroupLabel)));
        // Sort by group order so sections appear Incoming → Outgoing → Synced.
        // Within each section the backend's topological order is preserved
        // because SortDescription is stable for items with equal GroupOrder.
        view.SortDescriptions.Add(new SortDescription(nameof(CommitItem.GroupOrder), ListSortDirection.Ascending));
        GroupedCommits = view;
        OnPropertyChanged(nameof(GroupedCommits));
    }

    [ObservableProperty]
    public partial CommitItem? SelectedCommit { get; set; }

    [ObservableProperty]
    public partial string FilterText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasActiveFilters { get; set; }

    [ObservableProperty]
    public partial string FilterStatusText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string DiffHeader { get; set; } = string.Empty;

    [ObservableProperty]
    public partial List<DiffFileEntry> DiffFiles { get; set; } = [];

    public string DiffHeaderDisplay =>
        string.IsNullOrEmpty(DiffHeader) ? "no commit selected" : DiffHeader;

    partial void OnDiffHeaderChanged(string value) =>
        OnPropertyChanged(nameof(DiffHeaderDisplay));

    [ObservableProperty]
    public partial Gitster.Services.Git.CommitRemoteState DiffRemoteState { get; set; }

    public void UpdateDiff(string header, List<DiffFileEntry> files,
        Gitster.Services.Git.CommitRemoteState remoteState = Gitster.Services.Git.CommitRemoteState.LocalOnly)
    {
        DiffHeader = header;
        DiffFiles = files;
        DiffRemoteState = remoteState;
    }

    partial void OnFilterTextChanged(string value) => ApplyLiveFilter();

    /// <summary>
    /// Called by MainWindowViewModel whenever the dialog-filtered commit list changes.
    /// CommitListViewModel applies its own live text filter on top and handles auto-selection.
    /// </summary>
    public void SetBaseCommits(List<CommitItem> commits, bool dialogHasActiveFilters = false, string dialogFilterStatusText = "", bool hasTrackingBranch = true)
    {
        _hasTrackingBranch = hasTrackingBranch;
        _baseCommits = commits;
        _dialogHasActiveFilters = dialogHasActiveFilters;
        _dialogFilterStatusText = dialogFilterStatusText;
        ApplyLiveFilter();
    }

    private void ApplyLiveFilter()
    {
        var text = FilterText.Trim();

        IEnumerable<CommitItem> filtered = _baseCommits;

        if (!string.IsNullOrEmpty(text))
        {
            if (text.StartsWith("author:", StringComparison.OrdinalIgnoreCase))
            {
                var author = text[7..].Trim();
                filtered = filtered.Where(c => c.AuthorName.Contains(author, StringComparison.OrdinalIgnoreCase));
            }
            else
            {
                filtered = filtered.Where(c => c.Message.Contains(text, StringComparison.OrdinalIgnoreCase));
            }
        }

        var result = filtered.ToList();

        // Compute real section counts (before adding placeholders)
        IncomingCount = result.Count(c => c.GroupLabel == "Incoming");
        OutgoingCount = result.Count(c => c.GroupLabel == "Outgoing");
        SyncedCount   = result.Count(c => c.GroupLabel == "Synced");
        HasTrackingBranch = _hasTrackingBranch;

        OnPropertyChanged(nameof(IncomingCount));
        OnPropertyChanged(nameof(OutgoingCount));
        OnPropertyChanged(nameof(SyncedCount));
        OnPropertyChanged(nameof(HasTrackingBranch));
        OnPropertyChanged(nameof(IncomingCountText));
        OnPropertyChanged(nameof(OutgoingCountText));
        OnPropertyChanged(nameof(SyncedCountText));
        OnPropertyChanged(nameof(IncomingNoTrackingTooltip));

        // Inject one invisible placeholder per empty group so the group header always renders.
        if (IncomingCount == 0) result.Add(new CommitItem("", DateTime.MinValue, "", "", "", Services.Git.CommitRemoteState.Incoming,      "") { IsPlaceholder = true });
        if (OutgoingCount == 0) result.Add(new CommitItem("", DateTime.MinValue, "", "", "", Services.Git.CommitRemoteState.LocalOnly,     "") { IsPlaceholder = true });
        if (SyncedCount   == 0) result.Add(new CommitItem("", DateTime.MinValue, "", "", "", Services.Git.CommitRemoteState.OnRemote,      "") { IsPlaceholder = true });

        Commits = result;
        UpdateFilterStatus();
        AutoSelectCommit();
    }

    private void AutoSelectCommit()
    {
        // Never auto-select a placeholder sentinel.
        var realCommits = Commits.Where(c => !c.IsPlaceholder).ToList();

        if (SelectedCommit != null && realCommits.Contains(SelectedCommit))
            return;

        SelectedCommit = realCommits.Count > 0 ? realCommits[0] : null;
    }

    private void UpdateFilterStatus()
    {
        var hasLiveFilter = !string.IsNullOrWhiteSpace(FilterText);
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

    [RelayCommand]
    private void OpenFilter() => _openFilter();

    [RelayCommand]
    private void ClearAllFilters()
    {
        FilterText = string.Empty;
        _clearDialogFilters();
    }
}
