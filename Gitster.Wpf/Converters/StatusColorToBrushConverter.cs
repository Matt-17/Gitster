using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

using Gitster.Core.Ui;

namespace Gitster.Converters;

/// <summary>
/// Maps the framework-neutral <see cref="StatusColor"/> that ViewModels expose onto the current
/// theme's brushes, falling back to fixed colours when the theme resource is unavailable.
/// </summary>
public sealed class StatusColorToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        var status = value as StatusColor? ?? StatusColor.Neutral;

        var (key, fallback) = status switch
        {
            StatusColor.Success => ("AccentSuccess", Brushes.ForestGreen),
            StatusColor.Warning => ("AccentWarning", Brushes.DarkOrange),
            StatusColor.Danger  => ("AccentDanger",  Brushes.IndianRed),
            StatusColor.Info    => ("AccentBlue",    Brushes.DodgerBlue),
            _                   => ("TextSecondary", Brushes.Gray),
        };

        return Application.Current?.Resources[key] as Brush ?? (Brush)fallback;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
