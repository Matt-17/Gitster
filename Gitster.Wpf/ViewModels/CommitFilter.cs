using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Gitster.ViewModels;

/// <summary>
/// Represents the filter criteria for commits.
/// </summary>
public partial class CommitFilter : ObservableObject
{
    [ObservableProperty]
    public partial string? SelectedAuthorName { get; set; }

    [ObservableProperty]
    public partial DateTime? FromDate { get; set; }

    [ObservableProperty]
    public partial DateTime? ToDate { get; set; }

    public CommitFilter()
    {
        SelectedAuthorName = "All";
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

    /// <summary>
    /// Copies filter values from another CommitFilter.
    /// </summary>
    public void CopyFrom(CommitFilter other)
    {
        SelectedAuthorName = other.SelectedAuthorName;
        FromDate = other.FromDate;
        ToDate = other.ToDate;
    }

    /// <summary>
    /// Copies filter values to another CommitFilter.
    /// </summary>
    public void CopyTo(CommitFilter other)
    {
        other.SelectedAuthorName = SelectedAuthorName;
        other.FromDate = FromDate;
        other.ToDate = ToDate;
    }
}
