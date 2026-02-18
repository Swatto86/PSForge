using System.Management.Automation;

namespace PSForge.Models;

/// <summary>
/// Represents metadata about a single parameter of a cmdlet.
/// Captures type information, validation constraints, and parameter set membership
/// to drive dynamic UI control generation via
/// <see cref="PSForge.UI.TemplateSelectors.ParameterDataTemplateSelector"/>.
/// </summary>
public sealed class ParameterInfo
{
    /// <summary>Parameter name without the leading dash (e.g., "Name", "Force").</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// The .NET runtime type of the parameter. Used by the template selector
    /// to choose the appropriate input control (TextBox, CheckBox, DatePicker, etc.).
    /// </summary>
    public Type ParameterType { get; init; } = typeof(string);

    /// <summary>Display-friendly type name (e.g., "String", "Int32", "SwitchParameter").</summary>
    public string TypeName { get; init; } = string.Empty;

    /// <summary>Whether the parameter is required in its parameter set.</summary>
    public bool IsMandatory { get; init; }

    /// <summary>
    /// Positional index (0-based). Int.MinValue indicates the parameter is not positional.
    /// Positional parameters can be supplied without naming them on the command line.
    /// </summary>
    public int Position { get; init; } = int.MinValue;

    /// <summary>Default value if any, extracted from parameter metadata.</summary>
    public object? DefaultValue { get; init; }

    /// <summary>
    /// Allowed values from [ValidateSet("value1","value2",...)] attribute.
    /// When non-empty, the UI renders a ComboBox with these options.
    /// </summary>
    public string[] ValidateSetValues { get; init; } = Array.Empty<string>();

    /// <summary>Help message from [Parameter(HelpMessage="...")] attribute.</summary>
    public string HelpMessage { get; init; } = string.Empty;

    /// <summary>
    /// Rich description text from Get-Help output.
    /// Populated asynchronously after initial introspection via help enrichment.
    /// Uses 'set' (not 'init') because it is assigned after construction.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Alternative names for the parameter (e.g., [Alias("CN")]).</summary>
    public string[] Aliases { get; init; } = Array.Empty<string>();

    /// <summary>Whether this is a dynamic parameter (generated at runtime by the cmdlet).</summary>
    public bool IsDynamic { get; init; }

    /// <summary>Whether the parameter accepts pipeline input (ByValue or ByPropertyName).</summary>
    public bool AcceptsPipelineInput { get; init; }

    /// <summary>Whether the parameter accepts wildcard characters.</summary>
    public bool AcceptsWildcards { get; init; }

    /// <summary>
    /// The parameter set this parameter belongs to.
    /// "__AllParameterSets" is the sentinel value indicating membership in all sets.
    /// </summary>
    public string ParameterSetName { get; init; } = "__AllParameterSets";

    /// <summary>
    /// Whether this is a SwitchParameter, rendered as a toggle/checkbox.
    /// SwitchParameter is PowerShell's boolean flag type — it's present or absent,
    /// distinct from [bool] which requires an explicit $true/$false value.
    /// </summary>
    public bool IsSwitchParameter =>
        ParameterType == typeof(SwitchParameter) ||
        TypeName.Equals("SwitchParameter", StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether this parameter has ValidateSet constraint values.</summary>
    public bool HasValidateSet => ValidateSetValues.Length > 0;

    /// <summary>Whether this is an array/collection type (rendered as multi-line input).</summary>
    public bool IsArrayType =>
        ParameterType.IsArray ||
        TypeName.EndsWith("[]", StringComparison.Ordinal);

    /// <summary>Whether this is a credential type (rendered as username + password input).</summary>
    public bool IsCredentialType =>
        ParameterType == typeof(PSCredential) ||
        TypeName.Equals("PSCredential", StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether this is a SecureString type (rendered as password input).</summary>
    public bool IsSecureStringType =>
        ParameterType == typeof(System.Security.SecureString) ||
        TypeName.Equals("SecureString", StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether this is a DateTime type.</summary>
    public bool IsDateTimeType =>
        ParameterType == typeof(DateTime) ||
        ParameterType == typeof(DateTimeOffset);

    /// <summary>Whether this is a numeric type.</summary>
    public bool IsNumericType =>
        ParameterType == typeof(int) || ParameterType == typeof(long) ||
        ParameterType == typeof(double) || ParameterType == typeof(float) ||
        ParameterType == typeof(decimal) || ParameterType == typeof(short) ||
        ParameterType == typeof(byte);

    public override string ToString() => $"-{Name} [{TypeName}]";
}
