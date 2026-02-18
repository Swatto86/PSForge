using System.Globalization;
using System.Windows.Data;

namespace PSForge.UI.Converters;

/// <summary>
/// Converts a parameter type name to a user-friendly description of the expected input format.
/// Used for tooltips and placeholder text in the parameter form.
/// Maps common PowerShell/CLR type names to natural language descriptions.
/// </summary>
[ValueConversion(typeof(string), typeof(string))]
public sealed class TypeToControlConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var typeName = value?.ToString() ?? "Object";

        return typeName.ToUpperInvariant() switch
        {
            "STRING" => "Text",
            "INT32" or "INT64" or "INT16" or "BYTE" => "Number",
            "DOUBLE" or "SINGLE" or "DECIMAL" => "Decimal number",
            "BOOLEAN" => "True/False",
            "SWITCHPARAMETER" => "Switch (on/off)",
            "DATETIME" or "DATETIMEOFFSET" => "Date/Time",
            "SECURESTRING" => "Secure text (masked)",
            "PSCREDENTIAL" => "Username + Password",
            "STRING[]" => "Multiple values (comma-separated)",
            _ when typeName.EndsWith("[]") => $"Array of {typeName.TrimEnd('[', ']')}",
            _ => typeName
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        // One-way converter — used for display only
        throw new NotSupportedException("TypeToControlConverter does not support ConvertBack.");
    }
}
