using System.Globalization;
using System.Windows.Data;

namespace Gitster.Converters;

/// <summary>
/// Live-switchable date converter (plan A10). Inputs: [DateTime date, bool useRelative].
/// Returns absolute by default, relative when the toggle is on. Because it is a
/// MultiBinding converter, flipping the toggle re-evaluates every visible cell instantly.
/// Set <see cref="Invert"/> on the tooltip instance so the tooltip always shows the other format.
/// </summary>
public sealed class DateDisplayConverter : IMultiValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length == 0 || values[0] is not DateTime date)
            return string.Empty;

        bool useRelative = values.Length > 1 && values[1] is bool b && b;
        if (Invert) useRelative = !useRelative;

        return useRelative ? RelativeDateConverter.Relative(date) : RelativeDateConverter.Absolute(date);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
