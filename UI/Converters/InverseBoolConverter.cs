using System.Globalization;
using System.Windows.Data;

namespace PSForge.UI.Converters;

/// <summary>
/// Inverts a boolean value. True → False, False → True.
/// Useful for binding <c>IsEnabled</c> to a negated boolean property
/// (e.g., disabled while loading).
/// </summary>
[ValueConversion(typeof(bool), typeof(bool))]
public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is not true;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is not true;
    }
}
