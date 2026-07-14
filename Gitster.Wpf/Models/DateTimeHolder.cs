using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Gitster.Models;

/// <summary>
/// Represents a single day in the calendar control.
/// </summary>
public partial class DateTimeHolder : ObservableObject
{
    [ObservableProperty]
    private bool _isSelected;

    public DateTime Date { get; }
    public bool IsToday { get; }
    public bool IsSelectedMonth { get; }

    public DateTimeHolder(DateTime date, DateTime selectedDate)
    {
        Date = date;
        IsToday = date.Date == DateTime.Today;
        IsSelectedMonth = date.Month == selectedDate.Month;
        IsSelected = date.Date == selectedDate.Date;
    }
}
