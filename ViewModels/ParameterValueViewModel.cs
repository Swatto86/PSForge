using CommunityToolkit.Mvvm.ComponentModel;
using PSForge.Models;
using ParameterInfo = PSForge.Models.ParameterInfo;

namespace PSForge.ViewModels;

/// <summary>
/// Wraps a <see cref="ParameterInfo"/> with a mutable value property for data binding.
/// Each instance represents one parameter field in the dynamic form,
/// providing the current value, validation state, and type-specific metadata
/// needed by the <see cref="PSForge.UI.TemplateSelectors.ParameterDataTemplateSelector"/>.
///
/// The partial keyword enables CommunityToolkit.Mvvm source generators to
/// produce INotifyPropertyChanged boilerplate from [ObservableProperty] fields.
/// </summary>
public partial class ParameterValueViewModel : ObservableObject
{
    /// <summary>The underlying parameter metadata from introspection.</summary>
    public ParameterInfo Info { get; }

    /// <summary>Parameter name for display and binding.</summary>
    public string Name => Info.Name;

    /// <summary>Whether the parameter is required.</summary>
    public bool IsMandatory => Info.IsMandatory;

    /// <summary>Display-friendly type name for placeholder/tooltip.</summary>
    public string TypeName => Info.TypeName;

    /// <summary>Help message for tooltip.</summary>
    public string HelpMessage => Info.HelpMessage;

    /// <summary>Rich description from Get-Help, displayed below the parameter label.</summary>
    public string Description => !string.IsNullOrWhiteSpace(Info.Description)
        ? Info.Description
        : (!string.IsNullOrWhiteSpace(Info.HelpMessage) ? Info.HelpMessage : string.Empty);

    /// <summary>Whether this parameter has a description to display.</summary>
    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);

    /// <summary>Label indicating whether the parameter is Required or Optional.</summary>
    public string RequiredLabel => IsMandatory ? "Required" : "Optional";

    /// <summary>ValidateSet values for ComboBox population.</summary>
    public string[] ValidateSetValues => Info.ValidateSetValues;

    // Type classification properties — delegated to ParameterInfo
    // These drive the ParameterDataTemplateSelector
    public bool IsSwitchParameter => Info.IsSwitchParameter;
    public bool HasValidateSet => Info.HasValidateSet;
    public bool IsArrayType => Info.IsArrayType;
    public bool IsCredentialType => Info.IsCredentialType;
    public bool IsSecureStringType => Info.IsSecureStringType;
    public bool IsDateTimeType => Info.IsDateTimeType;
    public bool IsNumericType => Info.IsNumericType;

    // Rich metadata exposed for the parameter detail panel
    /// <summary>Aliases for this parameter (e.g., [Alias("CN")]).</summary>
    public string[] Aliases => Info.Aliases;

    /// <summary>Whether this parameter has aliases.</summary>
    public bool HasAliases => Info.Aliases.Length > 0;

    /// <summary>Formatted aliases string for display.</summary>
    public string AliasesDisplay => HasAliases ? string.Join(", ", Info.Aliases.Select(a => $"-{a}")) : string.Empty;

    /// <summary>Positional index display. Empty if not positional.</summary>
    public string PositionDisplay => Info.Position != int.MinValue ? Info.Position.ToString() : "Named";

    /// <summary>Whether this parameter accepts pipeline input.</summary>
    public bool AcceptsPipelineInput => Info.AcceptsPipelineInput;

    /// <summary>Whether this parameter accepts wildcards.</summary>
    public bool AcceptsWildcards => Info.AcceptsWildcards;

    /// <summary>Whether this is a dynamic parameter.</summary>
    public bool IsDynamic => Info.IsDynamic;

    /// <summary>The parameter set this parameter belongs to.</summary>
    public string ParameterSetName => Info.ParameterSetName;

    /// <summary>ValidateSet values formatted for display.</summary>
    public string ValidateSetDisplay => Info.HasValidateSet
        ? string.Join(", ", Info.ValidateSetValues)
        : string.Empty;

    /// <summary>Default value display.</summary>
    public string DefaultValueDisplay => Info.DefaultValue != null
        ? Info.DefaultValue.ToString() ?? "(none)"
        : "(none)";

    /// <summary>
    /// The current value of the parameter, bound to the appropriate input control.
    /// Type varies based on the parameter: string, bool, DateTime, SecureString, etc.
    /// </summary>
    [ObservableProperty]
    private object? _value;

    /// <summary>
    /// String representation of the value, used for TextBox-based inputs.
    /// Changes are propagated to <see cref="Value"/> via the generated partial method.
    /// </summary>
    [ObservableProperty]
    private string _stringValue = string.Empty;

    /// <summary>
    /// Boolean value, used for switch parameters and checkboxes.
    /// </summary>
    [ObservableProperty]
    private bool _boolValue;

    /// <summary>
    /// DateTime value, used for DatePicker controls.
    /// </summary>
    [ObservableProperty]
    private DateTime? _dateTimeValue;

    /// <summary>Current validation error message, if any.</summary>
    [ObservableProperty]
    private string? _validationError;

    /// <summary>Whether this parameter should be visible (based on active parameter set filter).</summary>
    [ObservableProperty]
    private bool _isVisible = true;

    public ParameterValueViewModel(ParameterInfo parameterInfo)
    {
        Info = parameterInfo ?? throw new ArgumentNullException(nameof(parameterInfo));
    }

    /// <summary>
    /// Notifies the UI that a property has changed.
    /// Exposed publicly so that external enrichment (e.g. help description loading)
    /// can trigger UI updates after modifying the underlying ParameterInfo model.
    /// </summary>
    public new void OnPropertyChanged(string? propertyName)
    {
        base.OnPropertyChanged(propertyName);
    }

    /// <summary>
    /// Called when StringValue changes. Synchronises the typed Value property
    /// and performs basic type validation.
    /// </summary>
    partial void OnStringValueChanged(string value)
    {
        if (Info.IsSwitchParameter) return;
        Value = string.IsNullOrEmpty(value) ? null : value;
        Validate();
    }

    /// <summary>Synchronises boolean changes to the Value property.</summary>
    partial void OnBoolValueChanged(bool value)
    {
        Value = value;
    }

    /// <summary>Synchronises DateTime changes to the Value property.</summary>
    partial void OnDateTimeValueChanged(DateTime? value)
    {
        Value = value;
    }

    /// <summary>
    /// Returns the typed value suitable for passing to the PowerShell pipeline.
    /// Handles type conversion from the string representation for arrays, booleans, etc.
    /// </summary>
    public object? GetTypedValue()
    {
        if (Info.IsSwitchParameter) return BoolValue;
        if (Info.IsDateTimeType) return DateTimeValue;
        if (Value == null) return null;

        // For array types, split comma/newline-separated values into a string[]
        if (Info.IsArrayType && Value is string strVal)
        {
            return strVal
                .Split(new[] { ',', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrEmpty(s))
                .ToArray();
        }

        return Value;
    }

    /// <summary>
    /// Validates the current value against the parameter's constraints.
    /// Accumulates errors per Rule 11 (though single-parameter scope here).
    /// </summary>
    public void Validate()
    {
        ValidationError = null;

        // Check mandatory constraint
        if (IsMandatory && (Value == null || (Value is string s && string.IsNullOrWhiteSpace(s))))
        {
            ValidationError = $"'{Name}' is required.";
            return;
        }

        // Check numeric validity
        if (Info.IsNumericType && !string.IsNullOrEmpty(StringValue))
        {
            if (!double.TryParse(StringValue, out _))
            {
                ValidationError = $"'{Name}' must be a valid number.";
            }
        }
    }

    /// <summary>
    /// Whether the parameter currently has a non-empty/non-default value.
    /// Used to determine which parameters to include in the command string.
    /// </summary>
    public bool HasValue
    {
        get
        {
            if (Info.IsSwitchParameter) return BoolValue;
            if (Info.IsDateTimeType) return DateTimeValue.HasValue;
            if (Value is string str) return !string.IsNullOrWhiteSpace(str);
            return Value != null;
        }
    }
}
