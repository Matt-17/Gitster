using System;
using System.Globalization;
using System.Linq;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using Gitster.Controls;
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
    private EditMode _editMode = Controls.EditMode.DateOnly;
    private int _hour;
    private int _minute;

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

    public EditMode EditMode
    {
        get => _editMode;
        set => SetProperty(ref _editMode, value);
    }

    public int Hour
    {
        get => _hour;
        set
        {
            if (SetProperty(ref _hour, value))
            {
                UpdateSelectedDateWithTime();
            }
        }
    }

    public int Minute
    {
        get => _minute;
        set
        {
            if (SetProperty(ref _minute, value))
            {
                UpdateSelectedDateWithTime();
            }
        }
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
                if (EditMode == Controls.EditMode.DateOnly)
                {
                    SelectedDate = string.IsNullOrWhiteSpace(value) ? null : GetDate(value);
                }
                else
                {
                    SelectedDate = string.IsNullOrWhiteSpace(value) ? null : GetDateTime(value);
                }
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
            
            // Update hour and minute from the selected date
            if (_selectedDate.HasValue)
            {
                _hour = _selectedDate.Value.Hour;
                _minute = _selectedDate.Value.Minute;
                OnPropertyChanged(nameof(Hour));
                OnPropertyChanged(nameof(Minute));
            }
            
            OnPropertyChanged();
            OnSelectedDateChanged(SelectedDate);
            
            // Update text based on edit mode
            if (EditMode == Controls.EditMode.DateOnly)
            {
                _text = SelectedDate?.ToString("dd.MM.yyyy") ?? string.Empty;
            }
            else if (EditMode == Controls.EditMode.TimeOnly)
            {
                _text = SelectedDate?.ToString("HH:mm") ?? string.Empty;
            }
            else // DateTime
            {
                _text = SelectedDate?.ToString("dd.MM.yyyy HH:mm") ?? string.Empty;
            }
            
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

    public void ChangeHour(int hours)
    {
        if (SelectedDate.HasValue)
        {
            var newHour = (Hour + hours + 24) % 24;
            Hour = newHour;
        }
    }

    public void ChangeMinute(int minutes)
    {
        if (SelectedDate.HasValue)
        {
            var newMinute = (Minute + minutes + 60) % 60;
            Minute = newMinute;
        }
    }

    private void UpdateSelectedDateWithTime()
    {
        if (_selectedDate.HasValue)
        {
            var date = _selectedDate.Value.Date;
            var newDateTime = new DateTime(date.Year, date.Month, date.Day, _hour, _minute, 0);
            
            // Update without triggering the setter loop
            _selectedDate = newDateTime;
            OnPropertyChanged(nameof(SelectedDate));
            OnSelectedDateChanged(newDateTime);
            
            // Update text
            if (EditMode == Controls.EditMode.TimeOnly)
            {
                _text = newDateTime.ToString("HH:mm");
            }
            else // DateTime
            {
                _text = newDateTime.ToString("dd.MM.yyyy HH:mm");
            }
            OnPropertyChanged(nameof(Text));
        }
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

    private DateTime? GetDateTime(string text)
    {
        // Try parsing as full datetime first
        var dateTimeFormats = new[] {
            "dd.MM.yyyy HH:mm",
            "dd.MM.yyyy HH:mm:ss",
            "dd-MM-yyyy HH:mm",
            "dd-MM-yyyy HH:mm:ss",
            "dd/MM/yyyy HH:mm",
            "dd/MM/yyyy HH:mm:ss",
            "d.M.yyyy HH:mm",
            "d.M.yyyy HH:mm:ss",
            "dd.MM.yy HH:mm",
            "dd.MM.yy HH:mm:ss",
            "HH:mm",
            "HH:mm:ss"
        };

        foreach (var format in dateTimeFormats)
        {
            if (DateTime.TryParseExact(text, format, CultureInfo.CurrentCulture, 
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal, out DateTime result))
            {
                return result;
            }
        }

        // If that fails, try parsing as date only and add current time
        try
        {
            var dateOnly = GetDate(text);
            if (dateOnly.HasValue)
            {
                return new DateTime(dateOnly.Value.Year, dateOnly.Value.Month, dateOnly.Value.Day, 
                    _hour, _minute, 0);
            }
        }
        catch
        {
            // Continue to exception below
        }

        throw new ArgumentOutOfRangeException();
    }

    public void SetMinute(int result)
    {
        Minute = result;
    }

    public void SetHour(int result)
    {
        Hour  = result;
    }
}
