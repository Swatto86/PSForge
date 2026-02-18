using System.Windows;
using System.Windows.Controls;
using PSForge.ViewModels;

namespace PSForge.UI.TemplateSelectors;

/// <summary>
/// Selects the appropriate DataTemplate for a parameter based on its type.
/// Maps <see cref="ParameterValueViewModel"/> type properties to specific templates:
///   - Switch parameters → CheckBox toggle
///   - ValidateSet → ComboBox
///   - DateTime → DatePicker
///   - SecureString → PasswordBox
///   - PSCredential → CredentialInputControl
///   - Numeric → TextBox with numeric validation
///   - String array → Multi-line TextBox
///   - Default → Single-line TextBox
///
/// Templates are defined in UI/Styles/DataTemplates.xaml and set via XAML properties.
/// Check order matters: more specific types are checked before general ones.
/// </summary>
public sealed class ParameterDataTemplateSelector : DataTemplateSelector
{
    /// <summary>Template for SwitchParameter (CheckBox/ToggleButton).</summary>
    public DataTemplate? SwitchTemplate { get; set; }

    /// <summary>Template for parameters with [ValidateSet] (ComboBox).</summary>
    public DataTemplate? ValidateSetTemplate { get; set; }

    /// <summary>Template for DateTime/DateTimeOffset (DatePicker).</summary>
    public DataTemplate? DateTimeTemplate { get; set; }

    /// <summary>Template for SecureString (PasswordBox).</summary>
    public DataTemplate? SecureStringTemplate { get; set; }

    /// <summary>Template for PSCredential (username + password composite control).</summary>
    public DataTemplate? CredentialTemplate { get; set; }

    /// <summary>Template for numeric types (TextBox with input validation).</summary>
    public DataTemplate? NumericTemplate { get; set; }

    /// <summary>Template for array types (multi-line TextBox).</summary>
    public DataTemplate? ArrayTemplate { get; set; }

    /// <summary>Template for all other types (single-line TextBox).</summary>
    public DataTemplate? DefaultTemplate { get; set; }

    public override DataTemplate? SelectTemplate(object item, DependencyObject container)
    {
        if (item is not ParameterValueViewModel param)
            return DefaultTemplate;

        // Order: most specific checks first to prevent mis-classification
        if (param.IsCredentialType) return CredentialTemplate ?? DefaultTemplate;
        if (param.IsSecureStringType) return SecureStringTemplate ?? DefaultTemplate;
        if (param.IsSwitchParameter) return SwitchTemplate ?? DefaultTemplate;
        if (param.HasValidateSet) return ValidateSetTemplate ?? DefaultTemplate;
        if (param.IsDateTimeType) return DateTimeTemplate ?? DefaultTemplate;
        if (param.IsNumericType) return NumericTemplate ?? DefaultTemplate;
        if (param.IsArrayType) return ArrayTemplate ?? DefaultTemplate;

        return DefaultTemplate;
    }
}
