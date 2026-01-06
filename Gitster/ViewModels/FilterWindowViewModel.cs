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

    [ObservableProperty]
    public partial bool HasPendingChanges { get; set; }

    public ObservableCollection<string> AuthorNames { get; } = [];

    private string? _appliedAuthorName;
    private DateTime? _appliedFromDate;
    private DateTime? _appliedToDate;

    public FilterWindowViewModel()
    {
    }

    partial void OnSelectedAuthorNameChanged(string? value)
    {
        UpdatePendingChanges();
    }

    partial void OnFromDateChanged(DateTime? value)
    {
        UpdatePendingChanges();
    }

    partial void OnToDateChanged(DateTime? value)
    {
        UpdatePendingChanges();
    }

    private void UpdatePendingChanges()
    {
        HasPendingChanges = _appliedAuthorName != SelectedAuthorName
                         || _appliedFromDate != FromDate
                         || _appliedToDate != ToDate;
    }

    public void SaveAppliedState()
    {
        _appliedAuthorName = SelectedAuthorName;
        _appliedFromDate = FromDate;
        _appliedToDate = ToDate;
        HasPendingChanges = false;
    }

    [RelayCommand]
    private void ClearAuthorName()
    {
        SelectedAuthorName = "All";
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
        return (!string.IsNullOrEmpty(SelectedAuthorName) && SelectedAuthorName != "All") 
               || FromDate.HasValue 
               || ToDate.HasValue;
    }

    /// <summary>
    /// Clears all filters.
    /// </summary>
    public void ClearAllFilters()
    {
        SelectedAuthorName = "All";
        FromDate = null;
        ToDate = null;
    }
}
