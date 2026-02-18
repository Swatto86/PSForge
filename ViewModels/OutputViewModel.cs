using System.Data;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using PSForge.Models;
using PSForge.Services;

namespace PSForge.ViewModels;

/// <summary>
/// ViewModel for the output panel. Displays command execution results
/// in Grid, Text, or JSON format with export capabilities.
/// Shared across cmdlet switches (results persist until cleared or replaced).
/// </summary>
public partial class OutputViewModel : ObservableObject
{
    private readonly OutputFormatterService _outputFormatter;
    private readonly ILogger _logger;
    private List<object> _rawOutputObjects = new();

    /// <summary>Available output display modes.</summary>
    public enum OutputMode
    {
        Grid,
        Text,
        Json
    }

    /// <summary>The current display mode.</summary>
    [ObservableProperty]
    private OutputMode _selectedOutputMode = OutputMode.Grid;

    /// <summary>DataTable for grid display (bound to DataGrid.ItemsSource).</summary>
    [ObservableProperty]
    private DataTable? _gridData;

    /// <summary>Text output for text/JSON display (bound to TextBox).</summary>
    [ObservableProperty]
    private string _textOutput = string.Empty;

    /// <summary>Whether there is any output to display.</summary>
    [ObservableProperty]
    private bool _hasOutput;

    /// <summary>Whether the last execution had errors.</summary>
    [ObservableProperty]
    private bool _hasError;

    /// <summary>Error message from the last execution.</summary>
    [ObservableProperty]
    private string _errorText = string.Empty;

    /// <summary>Warning messages from the last execution.</summary>
    [ObservableProperty]
    private string _warningText = string.Empty;

    /// <summary>The command that was executed (shown for reference).</summary>
    [ObservableProperty]
    private string _executedCommand = string.Empty;

    /// <summary>Duration of the last execution in human-readable format.</summary>
    [ObservableProperty]
    private string _durationText = string.Empty;

    /// <summary>Status message (e.g., "Command completed successfully", export confirmations).</summary>
    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public OutputViewModel(OutputFormatterService outputFormatter, ILogger logger)
    {
        _outputFormatter = outputFormatter ?? throw new ArgumentNullException(nameof(outputFormatter));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Called when the output mode changes. Re-formats the existing output
    /// in the new mode without re-executing the command.
    /// </summary>
    partial void OnSelectedOutputModeChanged(OutputMode value)
    {
        FormatOutput();
    }

    /// <summary>
    /// Displays the result of a command execution.
    /// Routes output to the appropriate format based on the selected mode.
    /// </summary>
    public void DisplayResult(ExecutionResult result)
    {
        _rawOutputObjects = result.OutputObjects;
        ExecutedCommand = result.CommandString;
        DurationText = $"{result.Duration.TotalMilliseconds:N0}ms";

        HasError = result.HadErrors;
        ErrorText = result.HadErrors
            ? string.Join(Environment.NewLine, result.Errors)
            : string.Empty;

        WarningText = result.Warnings.Count > 0
            ? string.Join(Environment.NewLine, result.Warnings)
            : string.Empty;

        if (result.HasOutput)
        {
            HasOutput = true;
            StatusMessage = string.Empty;
            FormatOutput();
        }
        else if (result.IsSuccess)
        {
            HasOutput = false;
            StatusMessage = "Command completed successfully (no output).";
            GridData = null;
            TextOutput = string.Empty;
        }
        else
        {
            HasOutput = false;
            StatusMessage = string.Empty;
            GridData = null;
            TextOutput = string.Empty;
        }
    }

    /// <summary>
    /// Shows an error message in the output panel (for validation errors, etc.).
    /// </summary>
    public void ShowError(string message)
    {
        HasError = true;
        ErrorText = message;
        HasOutput = false;
        StatusMessage = string.Empty;
    }

    /// <summary>
    /// Formats the raw output objects according to the selected output mode.
    /// </summary>
    private void FormatOutput()
    {
        if (_rawOutputObjects.Count == 0) return;

        switch (SelectedOutputMode)
        {
            case OutputMode.Grid:
                GridData = _outputFormatter.ConvertToDataTable(_rawOutputObjects);
                TextOutput = string.Empty;
                break;

            case OutputMode.Text:
                GridData = null;
                TextOutput = _outputFormatter.FormatAsText(_rawOutputObjects);
                break;

            case OutputMode.Json:
                GridData = null;
                TextOutput = _outputFormatter.FormatAsJson(_rawOutputObjects);
                break;
        }
    }

    /// <summary>
    /// Copies the current output to clipboard as CSV.
    /// </summary>
    [RelayCommand]
    private void ExportCsv()
    {
        try
        {
            var csv = _outputFormatter.FormatAsCsv(_rawOutputObjects);
            Clipboard.SetText(csv);
            StatusMessage = "Copied to clipboard as CSV.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to export CSV");
            ShowError($"Export failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Copies the current output to clipboard as JSON.
    /// </summary>
    [RelayCommand]
    private void ExportJson()
    {
        try
        {
            var json = _outputFormatter.FormatAsJson(_rawOutputObjects);
            Clipboard.SetText(json);
            StatusMessage = "Copied to clipboard as JSON.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to export JSON");
            ShowError($"Export failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Clears all output and resets the panel to its initial state.
    /// </summary>
    [RelayCommand]
    private void ClearOutput()
    {
        _rawOutputObjects.Clear();
        GridData = null;
        TextOutput = string.Empty;
        HasOutput = false;
        HasError = false;
        ErrorText = string.Empty;
        WarningText = string.Empty;
        ExecutedCommand = string.Empty;
        DurationText = string.Empty;
        StatusMessage = string.Empty;
    }
}
