namespace PSForge.Models;

/// <summary>
/// Represents a named parameter set for a cmdlet.
/// Parameter sets define mutually exclusive groups of parameters — only one set
/// can be active at a time. Each tab in the cmdlet detail view corresponds to one set.
/// </summary>
public sealed class ParameterSetInfo
{
    /// <summary>Name of the parameter set (e.g., "ByName", "ByInputObject").</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Whether this is the default parameter set when none is explicitly specified.</summary>
    public bool IsDefault { get; init; }

    /// <summary>All parameters that belong to this parameter set.</summary>
    public List<ParameterInfo> Parameters { get; init; } = new();

    public override string ToString() => IsDefault ? $"{Name} (Default)" : Name;
}
