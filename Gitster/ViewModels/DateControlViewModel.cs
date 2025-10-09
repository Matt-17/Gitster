using System;
using System.Globalization;
using System.Linq;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using Gitster.Helpers;
using Gitster.Models;

namespace Gitster.ViewModels;

/// <summary>
/// ViewModel for the DateControl.
/// </summary>
public partial class DateControlViewModel : BaseViewModel
{
    private DateTimeHolder[] _days = [];
    private DateTime _date;
    private bool _isOpen;
    private string _text = string.Empty;
    private DateTime? _selectedDate;

    public DateControlViewModel()
    {
        SetTodayCommand = new RelayCommand(() => SelectedDate = SystemTime.Today);
        SetWeek1Command = new RelayCommand(() => SelectedDate = SystemTime.Today.AddDays(7));
        SetWeek4Command = new RelayCommand(() => SelectedDate = SystemTime.Today.AddDays(28));
        SelectedDate = null;
    }

    public event EventHandler<DateTime?>? SelectedDateChanged;

    public ICommand SetWeek4Command { get; }

    public ICommand SetWeek1Command { get; }

    public ICommand SetTodayCommand { get; }

    public DateTimeHolder[] Days
    {
        get => _days;
        private set => SetProperty(ref _days, value);
    }

    public bool IsOpen
    {
        get => _isOpen;
        set => SetProperty(ref _isOpen, value);
    }

    public DateTime Date
    {
        get => _date;
        set => SetProperty(ref _date, value);
    }

    public string Text
    {
        get => _text;
        set
        {
            if (value == _text)
                return;
            try
            {
                SelectedDate = string.IsNullOrWhiteSpace(value) ? null : GetDate(value);
            }
            finally
            {
                OnPropertyChanged();
            }
        }
    }

    public DateTime? SelectedDate
    {
        get => _selectedDate;
        set
        {
            if (value.Equals(_selectedDate))
                return;
            _selectedDate = value;
            OnPropertyChanged();
            OnSelectedDateChanged(SelectedDate);
            _text = SelectedDate?.ToString("dd.MM.yyyy") ?? string.Empty;
            OnPropertyChanged(nameof(Text));
            SetDate(SelectedDate);

            var day = SelectedDate == null ? null : Days.SingleOrDefault(x => x.Date == SelectedDate?.Date);
            if (day != null)
                day.IsSelected = true;
        }
    }

    public void SetDate(DateTime? date2)
    {
        var date = date2 ?? SystemTime.Today;

        // Das Kalenderblatt ist immer 6 Wochen lang (wie Standard)
        var days = new DateTimeHolder[42];
        Date = date.Date;
        // Ersten des Monats suchen
        date = Date.AddDays(-date.Day + 1);
        var dow = (int)date.DayOfWeek - 1;
        if (dow < 0)
            dow += 7;
        // Montag vor dem ersten
        date = date.AddDays(-dow);
        if (dow < 1)
            date = date.AddDays(-7);
        for (var i = 0; i < days.Length; i++)
        {
            var day = date.AddDays(i);
            days[i] = new DateTimeHolder(day, Date);
        }
        Days = days;
    }

    public void ChangeMonth(int months)
    {
        SetDate(Date.AddMonths(months));
    }

    public void Select(DateTimeHolder holder)
    {
        foreach (var day in Days)
        {
            day.IsSelected = false;
        }
        holder.IsSelected = true;
        SelectedDate = holder.Date;
    }

    public void ChangeDate(int days)
    {
        SelectedDate = SelectedDate?.AddDays(days);
    }

    public void Reset()
    {
        OnPropertyChanged(nameof(Text));
    }

    protected virtual void OnSelectedDateChanged(DateTime? e)
    {
        SelectedDateChanged?.Invoke(this, e);
    }

    private DateTime? GetDate(string text)
    {
        var formate = new[] {
                "ddMMyy",
                "ddMMyyyy",
                "dd-MM-yy",
                "dd-MM-yyyy",
                "dd.MM.yy",
                "dd.MM.yyyy",
                "dd MM yy",
                "dd MM yyyy",
                "d-M-yy",
                "d-M-yyyy",
                "d.M.yy",
                "d.M.yyyy",
                "d M yy",
                "d M yyyy",

                "MMddyy",
                "MMddyyyy",
                "MM-dd-yy",
                "MM-dd-yyyy",
                "MM.dd.yy",
                "MM.dd.yyyy",
                "MM dd yy",
                "MM dd yyyy",
                "M-d-yy",
                "M-d-yyyy",
                "M.d.yy",
                "M.d.yyyy",
                "M d yy",
                "M d yyyy",
                "ddMM",
                "ddMM",
                "dd-MM",
                "dd-MM",
                "dd.MM",
                "dd.MM",
                "dd MM",
                "dd MM",
                "d-M",
                "d-M",
                "d.M",
                "d.M",
                "d M",
                "d M",

                "MMdd",
                "MMdd",
                "MM-dd",
                "MM-dd",
                "MM.dd",
                "MM.dd",
                "MM dd",
                "MM dd",
                "M-d",
                "M-d",
                "M.d",
                "M.d",
                "M d",
                "M d"
        };

        var success = false;
        DateTime dateTime = DateTime.MinValue;

        foreach (var format in formate)
        {
            success = DateTime.TryParseExact(text, format, CultureInfo.CurrentCulture, DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal, out dateTime);
            if (success)
                break;
        }

        if (success)
            return dateTime;

        throw new ArgumentOutOfRangeException();
    }
}
