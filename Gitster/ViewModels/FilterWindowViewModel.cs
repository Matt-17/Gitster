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

    private readonly CommitFilter _mainFilter;
    private string? _appliedAuthorName;
    private DateTime? _appliedFromDate;
    private DateTime? _appliedToDate;

    public FilterWindowViewModel(CommitFilter mainFilter)
    {
        _mainFilter = mainFilter;
        
        // Load current filter values from main filter
        LoadFromMainFilter();
    }

    /// <summary>
    /// Loads filter values from the main filter.
    /// </summary>
    public void LoadFromMainFilter()
    {
        SelectedAuthorName = _mainFilter.SelectedAuthorName;
        FromDate = _mainFilter.FromDate;
        ToDate = _mainFilter.ToDate;
        
        // Set applied state to current values
        _appliedAuthorName = SelectedAuthorName;
        _appliedFromDate = FromDate;
        _appliedToDate = ToDate;
        UpdatePendingChanges();
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

    /// <summary>
    /// Applies the current filter values to the main filter.
    /// </summary>
    public void ApplyToMainFilter()
    {
        _mainFilter.SelectedAuthorName = SelectedAuthorName;
        _mainFilter.FromDate = FromDate;
        _mainFilter.ToDate = ToDate;
        
        // Save applied state
        _appliedAuthorName = SelectedAuthorName;
        _appliedFromDate = FromDate;
        _appliedToDate = ToDate;
        UpdatePendingChanges();
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
}
