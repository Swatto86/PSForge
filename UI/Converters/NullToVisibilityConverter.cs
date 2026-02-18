using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace PSForge.UI.Converters;

/// <summary>
/// Converts null/non-null values to <see cref="Visibility"/>.
/// Non-null → Visible, Null → Collapsed.
/// Empty/whitespace strings are treated as null.
/// Supports inversion via ConverterParameter "Invert".
/// </summary>
[ValueConversion(typeof(object), typeof(Visibility))]
public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        var hasValue = value != null;

        // Treat empty/whitespace strings as null for visibility purposes
        if (value is string str && string.IsNullOrWhiteSpace(str))
        {
            hasValue = false;
        }

        if (parameter is string param &&
            param.Equals("Invert", StringComparison.OrdinalIgnoreCase))
        {
            hasValue = !hasValue;
        }

        return hasValue ? Visibility.Visible : Visibility.Collapsed;
    }

    public object? ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        // One-way converter — ConvertBack is not meaningful
        return DependencyProperty.UnsetValue;
    }
}
