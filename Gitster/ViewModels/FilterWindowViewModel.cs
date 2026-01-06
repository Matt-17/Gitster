using System;
using System.Collections.ObjectModel;
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
