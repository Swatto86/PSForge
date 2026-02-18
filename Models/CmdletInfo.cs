namespace PSForge.Models;

/// <summary>
/// Represents metadata for a single cmdlet (or function) within a PowerShell module.
/// Contains verb/noun decomposition, synopsis, and parameter set definitions
/// used to generate the dynamic UI form.
/// </summary>
public sealed class CmdletInfo
{
    /// <summary>Full cmdlet name (e.g., "Get-Process").</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Verb portion of the cmdlet name (e.g., "Get").</summary>
    public string Verb { get; init; } = string.Empty;

    /// <summary>Noun portion of the cmdlet name (e.g., "Process").</summary>
    public string Noun { get; init; } = string.Empty;

    /// <summary>Short description of the cmdlet from Get-Help synopsis.</summary>
    public string Synopsis { get; init; } = string.Empty;

    /// <summary>Detailed description from Get-Help.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>Name of the module that exports this cmdlet.</summary>
    public string ModuleName { get; init; } = string.Empty;

    /// <summary>
    /// Default parameter set name. When a cmdlet has multiple parameter sets,
    /// this indicates which one is active when no set-specific parameters are provided.
    /// </summary>
    public string DefaultParameterSetName { get; init; } = string.Empty;

    /// <summary>
    /// All parameter sets defined by this cmdlet. Each set represents a distinct
    /// combination of parameters that can be used together.
    /// </summary>
    public List<ParameterSetInfo> ParameterSets { get; init; } = new();

    public override string ToString() => Name;
}
