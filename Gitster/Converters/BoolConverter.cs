using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Gitster.Converters;

/// <summary>
/// Multi-purpose boolean converter for various boolean-to-value conversions.
/// </summary>
public class BoolConverter : IValueConverter
{
    public BoolConverterType Type { get; set; } = BoolConverterType.Visible;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var boolValue = value is bool b && b;

        return Type switch
        {
            BoolConverterType.Visible => boolValue ? Visibility.Visible : Visibility.Collapsed,
            BoolConverterType.Collapsed => boolValue ? Visibility.Collapsed : Visibility.Visible,
            BoolConverterType.FontWeightBold => boolValue ? FontWeights.Bold : FontWeights.Normal,
            BoolConverterType.Opaque => boolValue ? 1.0 : 0.5,
            _ => value
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public enum BoolConverterType
{
    Visible,
    Collapsed,
    FontWeightBold,
    Opaque
}
