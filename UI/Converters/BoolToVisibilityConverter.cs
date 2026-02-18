using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace PSForge.UI.Converters;

/// <summary>
/// Converts a boolean value to a <see cref="Visibility"/> value.
/// True → Visible, False → Collapsed.
/// Supports inversion via ConverterParameter "Invert" for cases where
/// you want to show content when a condition is false.
/// </summary>
[ValueConversion(typeof(bool), typeof(Visibility))]
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var boolValue = value is true;

        if (parameter is string param &&
            param.Equals("Invert", StringComparison.OrdinalIgnoreCase))
        {
            boolValue = !boolValue;
        }

        return boolValue ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var visibility = value is Visibility vis ? vis : Visibility.Collapsed;
        var result = visibility == Visibility.Visible;

        if (parameter is string param &&
            param.Equals("Invert", StringComparison.OrdinalIgnoreCase))
        {
            result = !result;
        }

        return result;
    }
}
