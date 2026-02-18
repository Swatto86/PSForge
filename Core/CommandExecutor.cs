using System.Diagnostics;
using System.Management.Automation;
using System.Security;
using Microsoft.Extensions.Logging;
using PSForge.Models;

namespace PSForge.Core;

/// <summary>
/// Builds and invokes PowerShell cmdlet pipelines from parameter dictionaries.
/// Executes commands asynchronously in the execution runspace and returns
/// structured <see cref="ExecutionResult"/> objects.
///
/// Supports cancellation via CancellationToken and provides a static method
/// for building command preview strings for the UI.
///
/// Sensitive values (SecureString, PSCredential) are masked in command previews
/// to prevent accidental credential exposure.
/// </summary>
public sealed class CommandExecutor
{
    private readonly PowerShellSessionManager _sessionManager;
    private readonly ILogger<CommandExecutor> _logger;
    private CancellationTokenSource? _currentCts;

    /// <summary>Default timeout for command execution (5 minutes). Configurable per Rule 11.</summary>
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(5);

    public CommandExecutor(
        PowerShellSessionManager sessionManager,
        ILogger<CommandExecutor> logger)
    {
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Executes a cmdlet asynchronously with the given parameters.
    /// Captures all output streams (output, error, warning, information)
    /// and measures execution duration.
    /// </summary>
    /// <param name="cmdletName">Full cmdlet name (e.g., "Get-Process").</param>
    /// <param name="parameters">Dictionary of parameter name → value pairs.</param>
    /// <param name="cancellationToken">Token to cancel execution.</param>
    /// <returns>Structured execution result with all captured output.</returns>
    public async Task<ExecutionResult> ExecuteAsync(
        string cmdletName,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        // Cancel any previously running command before starting a new one
        _currentCts?.Cancel();
        _currentCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var linkedToken = _currentCts.Token;

        var commandString = BuildCommandString(cmdletName, parameters);
        _logger.LogInformation("Executing: {Command}", commandString);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            return await Task.Run(() =>
            {
                using var ps = _sessionManager.CreateExecutionShell();
                ps.AddCommand(cmdletName);

                foreach (var kvp in parameters)
                {
                    if (kvp.Value == null) continue;

                    // SwitchParameters: add as flag (no value) when true, skip when false
                    if (kvp.Value is bool boolVal)
                    {
                        if (boolVal)
                        {
                            ps.AddParameter(kvp.Key);
                        }
                        continue;
                    }

                    ps.AddParameter(kvp.Key, kvp.Value);
                }

                // Register cancellation to abort the pipeline.
                // ps.Stop() is thread-safe and interrupts the running pipeline.
                using var registration = linkedToken.Register(() =>
                {
                    try { ps.Stop(); }
                    catch { /* Pipeline may already be stopped or disposed */ }
                });

                var output = ps.Invoke();
                stopwatch.Stop();

                linkedToken.ThrowIfCancellationRequested();

                var outputObjects = output
                    .Where(o => o != null)
                    .Select(o => (object)o)
                    .ToList();

                var errors = ps.Streams.Error
                    .Select(FormatErrorRecord)
                    .ToList();

                var warnings = ps.Streams.Warning
                    .Select(w => w.ToString())
                    .ToList();

                var information = ps.Streams.Information
                    .Select(i => i.ToString())
                    .ToList();

                var result = new ExecutionResult
                {
                    IsSuccess = !ps.HadErrors,
                    OutputObjects = outputObjects,
                    Errors = errors,
                    Warnings = warnings,
                    InformationMessages = information,
                    CommandString = commandString,
                    Duration = stopwatch.Elapsed
                };

                _logger.LogInformation(
                    "Command completed in {Duration}ms — Success: {IsSuccess}, Objects: {OutputCount}, Errors: {ErrorCount}",
                    stopwatch.ElapsedMilliseconds, result.IsSuccess,
                    result.OutputObjects.Count, result.Errors.Count);

                return result;
            }, linkedToken);
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            _logger.LogWarning("Command execution cancelled after {Duration}ms",
                stopwatch.ElapsedMilliseconds);

            return new ExecutionResult
            {
                IsSuccess = false,
                Errors = new List<string> { "Command execution was cancelled by the user." },
                CommandString = commandString,
                Duration = stopwatch.Elapsed
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Command execution failed after {Duration}ms",
                stopwatch.ElapsedMilliseconds);

            return new ExecutionResult
            {
                IsSuccess = false,
                Errors = new List<string> { $"Execution error: {ex.Message}" },
                CommandString = commandString,
                Duration = stopwatch.Elapsed
            };
        }
    }

    /// <summary>
    /// Cancels any currently running command execution.
    /// </summary>
    public void CancelExecution()
    {
        _currentCts?.Cancel();
        _logger.LogInformation("Execution cancellation requested");
    }

    /// <summary>
    /// Builds the reconstructed PowerShell command string from cmdlet name and parameters.
    /// Used for live preview in the UI and for logging.
    ///
    /// Security: Sensitive values (SecureString, PSCredential) are masked
    /// to avoid accidental credential exposure in logs or UI.
    /// </summary>
    public static string BuildCommandString(string cmdletName, Dictionary<string, object?> parameters)
    {
        var parts = new List<string> { cmdletName };

        foreach (var kvp in parameters)
        {
            if (kvp.Value == null) continue;

            var paramName = $"-{kvp.Key}";

            // Mask sensitive parameter values in the preview string
            if (kvp.Value is SecureString)
            {
                parts.Add($"{paramName} (SecureString)");
            }
            else if (kvp.Value is PSCredential)
            {
                parts.Add($"{paramName} (PSCredential)");
            }
            else if (kvp.Value is bool boolValue)
            {
                // Switch parameters: only show when true, no value needed
                if (boolValue) parts.Add(paramName);
            }
            else if (kvp.Value is string strValue)
            {
                // Quote strings that contain spaces for accurate PS syntax
                var formatted = strValue.Contains(' ') ? $"'{strValue}'" : strValue;
                parts.Add($"{paramName} {formatted}");
            }
            else if (kvp.Value is string[] arrayValue)
            {
                var joined = string.Join(", ", arrayValue.Select(v => $"'{v}'"));
                parts.Add($"{paramName} @({joined})");
            }
            else
            {
                parts.Add($"{paramName} {kvp.Value}");
            }
        }

        return string.Join(" ", parts);
    }

    /// <summary>
    /// Formats a PowerShell ErrorRecord into a human-readable string
    /// with exception type, category, and positional context.
    /// Only technical details go here; user-facing messages are derived separately.
    /// </summary>
    private static string FormatErrorRecord(ErrorRecord error)
    {
        var message = error.Exception?.Message ?? error.ToString();
        var category = error.CategoryInfo?.Category.ToString() ?? "Unknown";
        var target = error.TargetObject?.ToString();

        var parts = new List<string> { $"[{category}] {message}" };

        if (!string.IsNullOrEmpty(target))
        {
            parts.Add($"Target: {target}");
        }

        if (error.InvocationInfo != null)
        {
            parts.Add($"Position: Line {error.InvocationInfo.ScriptLineNumber}");
        }

        return string.Join(" | ", parts);
    }
}
