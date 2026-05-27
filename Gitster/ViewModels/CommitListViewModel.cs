using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Gitster.Models;

namespace Gitster.ViewModels;

public partial class CommitListViewModel : BaseViewModel
{
    private List<CommitItem> _baseCommits = [];
    private bool _dialogHasActiveFilters;
    private string _dialogFilterStatusText = string.Empty;

    private readonly Action _openFilter;
    private readonly Action _clearDialogFilters;

    public CommitListViewModel(Action openFilter, Action clearDialogFilters)
    {
        _openFilter = openFilter;
        _clearDialogFilters = clearDialogFilters;
    }

    [ObservableProperty]
    public partial List<CommitItem> Commits { get; set; } = [];

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

    public void UpdateDiff(string header, List<DiffFileEntry> files)
    {
        DiffHeader = header;
        DiffFiles = files;
    }

    partial void OnFilterTextChanged(string value) => ApplyLiveFilter();

    /// <summary>
    /// Called by MainWindowViewModel whenever the dialog-filtered commit list changes.
    /// CommitListViewModel applies its own live text filter on top and handles auto-selection.
    /// </summary>
    public void SetBaseCommits(List<CommitItem> commits, bool dialogHasActiveFilters = false, string dialogFilterStatusText = "")
    {
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

        Commits = filtered.ToList();
        UpdateFilterStatus();
        AutoSelectCommit();
    }

    private void AutoSelectCommit()
    {
        if (SelectedCommit != null && Commits.Contains(SelectedCommit))
            return;

        SelectedCommit = Commits.Count > 0 ? Commits[0] : null;
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
