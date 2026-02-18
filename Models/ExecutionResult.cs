namespace PSForge.Models;

/// <summary>
/// Encapsulates the complete result of a PowerShell cmdlet invocation,
/// including output objects, error/warning streams, and timing information.
/// Used by <see cref="PSForge.ViewModels.OutputViewModel"/> to display results.
/// </summary>
public sealed class ExecutionResult
{
    /// <summary>Whether the command completed without errors in the error stream.</summary>
    public bool IsSuccess { get; init; }

    /// <summary>
    /// Output objects returned by the cmdlet. These may be PSObjects, primitives,
    /// or complex .NET objects depending on what the cmdlet returns.
    /// </summary>
    public List<object> OutputObjects { get; init; } = new();

    /// <summary>Error messages from the PowerShell error stream.</summary>
    public List<string> Errors { get; init; } = new();

    /// <summary>Warning messages from the PowerShell warning stream.</summary>
    public List<string> Warnings { get; init; } = new();

    /// <summary>Information messages from the PowerShell information stream.</summary>
    public List<string> InformationMessages { get; init; } = new();

    /// <summary>
    /// The reconstructed PowerShell command string that was executed
    /// (e.g., "Get-Process -Name 'explorer' -Id 1234").
    /// </summary>
    public string CommandString { get; init; } = string.Empty;

    /// <summary>Wall-clock duration of the cmdlet execution.</summary>
    public TimeSpan Duration { get; init; }

    /// <summary>Convenience property: true if any errors were captured.</summary>
    public bool HadErrors => Errors.Count > 0;

    /// <summary>Convenience property: true if any output objects were returned.</summary>
    public bool HasOutput => OutputObjects.Count > 0;
}
