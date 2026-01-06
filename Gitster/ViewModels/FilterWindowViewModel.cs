using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Gitster.ViewModels;

/// <summary>
/// View model for the filter window.
/// </summary>
public partial class FilterWindowViewModel : BaseViewModel
{
    [ObservableProperty]
    public partial string? SelectedAuthorName { get; set; }

    [ObservableProperty]
    public partial DateTime? FromDate { get; set; }

    [ObservableProperty]
    public partial DateTime? ToDate { get; set; }

    public ObservableCollection<string> AuthorNames { get; } = [];

    public FilterWindowViewModel()
    {
    }

    /// <summary>
    /// Populates the author names list from commits.
    /// </summary>
    public void PopulateAuthorNames(ObservableCollection<CommitItem> commits)
    {
        AuthorNames.Clear();
        
        // Add "All" option at the beginning
        AuthorNames.Add("All");
        
        // Get distinct author names from commits
        var distinctAuthors = commits
            .Select(c => c.Message.Split('\n').FirstOrDefault() ?? string.Empty)
            .Distinct()
            .OrderBy(name => name)
            .ToList();

        // For now, we'll need to get actual author names from the repository
        // This is a placeholder that will be updated when connected to the repository
    }

    [RelayCommand]
    private void ClearAuthorName()
    {
        SelectedAuthorName = null;
    }

    [RelayCommand]
    private void ClearFromDate()
    {
        FromDate = null;
    }

    [RelayCommand]
    private void ClearToDate()
    {
        ToDate = null;
    }

    /// <summary>
    /// Checks if any filter is active.
    /// </summary>
    public bool HasActiveFilters()
    {
        return !string.IsNullOrEmpty(SelectedAuthorName) && SelectedAuthorName != "All" 
               || FromDate.HasValue 
               || ToDate.HasValue;
    }

    /// <summary>
    /// Clears all filters.
    /// </summary>
    public void ClearAllFilters()
    {
        SelectedAuthorName = null;
        FromDate = null;
        ToDate = null;
    }
}
