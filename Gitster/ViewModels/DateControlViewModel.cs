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
    private string _dateText = string.Empty;
    private string _timeText = string.Empty;
    private DateTime? _selectedDate;
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
        get;
        private set => SetProperty(ref field, value);
    } = [];

    public bool IsOpen
    {
        get;
        set => SetProperty(ref field, value);
    }

    public DateTime Date
    {
        get;
        set => SetProperty(ref field, value);
    }

    public EditMode EditMode
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                // Update text displays when edit mode changes
                UpdateTextDisplay();
            }
        }
    } = Controls.EditMode.DateOnly;

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

    public string DateText
    {
        get => _dateText;
        set
        {
            if (value == _dateText)
                return;
            try
            {
                var parsedDate = string.IsNullOrWhiteSpace(value) ? null : GetDate(value);
                if (parsedDate.HasValue && _selectedDate.HasValue)
                {
                    // Preserve time when updating date
                    SelectedDate = new DateTime(parsedDate.Value.Year, parsedDate.Value.Month, parsedDate.Value.Day,
                        _hour, _minute, 0);
                }
                else
                {
                    SelectedDate = parsedDate;
                }
            }
            catch
            {
                // Invalid date format - just update the text
                _dateText = value;
                OnPropertyChanged();
            }
        }
    }

    public string TimeText
    {
        get => _timeText;
        set
        {
            if (value == _timeText)
                return;
            try
            {
                var parsedTime = ParseTime(value);
                if (parsedTime.HasValue)
                {
                    Hour = parsedTime.Value.Hour;
                    Minute = parsedTime.Value.Minute;
                }
            }
            catch
            {
                // Invalid time format - just update the text
                _timeText = value;
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
            
            var newDate = value;
            
            // In DateTime mode, if new date is at midnight and we have an existing time, preserve the time
            if (EditMode == EditMode.DateTime && newDate.HasValue && _selectedDate.HasValue)
            {
                var newDateOnly = newDate.Value;
                var oldTime = _selectedDate.Value;
                
                // If new date is at midnight (00:00:00), preserve existing time
                if (newDateOnly.Hour == 0 && newDateOnly.Minute == 0 && newDateOnly.Second == 0)
                {
                    newDate = new DateTime(newDateOnly.Year, newDateOnly.Month, newDateOnly.Day,
                        oldTime.Hour, oldTime.Minute, oldTime.Second);
                }
            }
            
            _selectedDate = newDate;
            
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
            
            UpdateTextDisplay();
            SetDate(SelectedDate);

            var day = SelectedDate == null ? null : Days.SingleOrDefault(x => x.Date == SelectedDate?.Date);
            day?.IsSelected = true;
        }
    }

    private void UpdateTextDisplay()
    {
        // Update DateText
        _dateText = SelectedDate?.ToString("dd.MM.yyyy") ?? string.Empty;
        OnPropertyChanged(nameof(DateText));

        // Update TimeText
        _timeText = SelectedDate?.ToString("HH:mm") ?? string.Empty;
        OnPropertyChanged(nameof(TimeText));
    }

    private DateTime? ParseTime(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var timeFormats = new[] {
            "HH:mm",
            "HH:mm:ss",
            "H:mm",
            "H:mm:ss",
            "HHmm",
            "Hmm"
        };

        foreach (var format in timeFormats)
        {
            if (DateTime.TryParseExact(text, format, CultureInfo.CurrentCulture,
                DateTimeStyles.AllowWhiteSpaces, out DateTime result))
            {
                return result;
            }
        }

        return null;
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
        
        // In DateTime mode, preserve the current time when selecting a new date
        if (EditMode == EditMode.DateTime && _selectedDate.HasValue)
        {
            var currentTime = _selectedDate.Value;
            SelectedDate = new DateTime(holder.Date.Year, holder.Date.Month, holder.Date.Day,
                currentTime.Hour, currentTime.Minute, currentTime.Second);
        }
        else
        {
            SelectedDate = holder.Date;
        }
    }

    public void ChangeDate(int days)
    {
        SelectedDate = SelectedDate?.AddDays(days);
    }

    public void Reset()
    {
        UpdateTextDisplay();
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
            
            // Update time text
            _timeText = newDateTime.ToString("HH:mm");
            OnPropertyChanged(nameof(TimeText));
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

    public void SetMinute(int result)
    {
        Minute = result;
    }

    public void SetHour(int result)
    {
        Hour  = result;
    }
}
