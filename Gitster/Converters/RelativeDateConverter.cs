using System.Globalization;
using System.Windows.Data;

using Gitster.Helpers;

namespace Gitster.Converters;

public enum RelativeDateMode { Relative, Full }

[ValueConversion(typeof(DateTime), typeof(string))]
public class RelativeDateConverter : IValueConverter
{
    public RelativeDateMode Mode { get; set; } = RelativeDateMode.Relative;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not DateTime date)
            return string.Empty;

        if (Mode == RelativeDateMode.Full)
            return date.ToString("dd.MM.yyyy HH:mm");

        var now = SystemTime.Now;
        var diff = now - date;

        if (date.Date == now.Date)
        {
            if (diff.TotalMinutes < 60)
                return $"{Math.Max(1, (int)diff.TotalMinutes)}m ago";
            return $"{(int)diff.TotalHours}h ago";
        }

        if (date.Date == now.Date.AddDays(-1))
            return "yesterday";

        if (diff.TotalDays < 7)
            return date.ToString("ddd", CultureInfo.InvariantCulture);

        var weeks = (int)(diff.TotalDays / 7);
        if (weeks < 4)
            return weeks == 1 ? "1 week ago" : $"{weeks} weeks ago";

        var months = (int)(diff.TotalDays / 30.44);
        if (months < 12)
            return months == 1 ? "1 month ago" : $"{months} months ago";

        var years = (int)(diff.TotalDays / 365.25);
        return years == 1 ? "1 year ago" : $"{years} years ago";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}
