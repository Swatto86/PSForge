using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using PSForge.Core;
using PSForge.Models;
using PSForge.Services;
using CmdletInfo = PSForge.Models.CmdletInfo;
using ParameterSetInfo = PSForge.Models.ParameterSetInfo;

namespace PSForge.ViewModels;

/// <summary>
/// ViewModel for the cmdlet detail view. Manages the active parameter set,
/// parameter value state, command preview, and execution.
///
/// Each time a user selects a different cmdlet, a new instance of this ViewModel
/// is created to ensure completely clean state (no stale parameter values).
/// </summary>
public partial class CmdletViewModel : ObservableObject
{
    private readonly CommandExecutor _commandExecutor;
    private readonly HelpTextService _helpTextService;
    private readonly OutputViewModel _outputViewModel;
    private readonly ILogger _logger;
    private CancellationTokenSource? _executionCts;

    /// <summary>The cmdlet metadata this ViewModel represents.</summary>
    public CmdletInfo Cmdlet { get; }

    /// <summary>Display name of the cmdlet (e.g., "Get-Process").</summary>
    public string Name => Cmdlet.Name;

    /// <summary>Brief description of the cmdlet.</summary>
    public string Synopsis => Cmdlet.Synopsis;

    /// <summary>Available parameter sets for tab display.</summary>
    public ObservableCollection<ParameterSetInfo> ParameterSets { get; } = new();

    /// <summary>All parameter value ViewModels for the active parameter set.</summary>
    public ObservableCollection<ParameterValueViewModel> Parameters { get; } = new();

    /// <summary>Mandatory parameters (always visible in the form).</summary>
    public ObservableCollection<ParameterValueViewModel> MandatoryParameters { get; } = new();

    /// <summary>Optional parameters (collapsed by default to reduce visual noise).</summary>
    public ObservableCollection<ParameterValueViewModel> OptionalParameters { get; } = new();

    /// <summary>The currently selected parameter set.</summary>
    [ObservableProperty]
    private ParameterSetInfo? _selectedParameterSet;

    /// <summary>Live preview of the PowerShell command being constructed.</summary>
    [ObservableProperty]
    private string _commandPreview = string.Empty;

    /// <summary>Whether a command is currently executing (disables Run, enables Cancel).</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    private bool _isExecuting;

    /// <summary>Full help text for the cmdlet (loaded asynchronously).</summary>
    [ObservableProperty]
    private string _helpText = string.Empty;

    /// <summary>Whether the optional parameters section is expanded.</summary>
    [ObservableProperty]
    private bool _showOptionalParameters = true;

    /// <summary>The currently selected/focused parameter for the detail panel.</summary>
    [ObservableProperty]
    private ParameterValueViewModel? _selectedParameter;

    /// <summary>Whether a parameter is selected (drives detail panel visibility).</summary>
    public bool HasSelectedParameter => SelectedParameter != null;

    partial void OnSelectedParameterChanged(ParameterValueViewModel? value)
    {
        OnPropertyChanged(nameof(HasSelectedParameter));
    }

    public CmdletViewModel(
        CmdletInfo cmdlet,
        CommandExecutor commandExecutor,
        HelpTextService helpTextService,
        OutputViewModel outputViewModel,
        ILogger logger)
    {
        Cmdlet = cmdlet ?? throw new ArgumentNullException(nameof(cmdlet));
        _commandExecutor = commandExecutor ?? throw new ArgumentNullException(nameof(commandExecutor));
        _helpTextService = helpTextService ?? throw new ArgumentNullException(nameof(helpTextService));
        _outputViewModel = outputViewModel ?? throw new ArgumentNullException(nameof(outputViewModel));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Populate parameter sets from cmdlet metadata
        foreach (var ps in cmdlet.ParameterSets)
        {
            ParameterSets.Add(ps);
        }

        // Select the default parameter set, or the first one
        SelectedParameterSet = cmdlet.ParameterSets.FirstOrDefault(ps => ps.IsDefault)
                               ?? cmdlet.ParameterSets.FirstOrDefault();

        // Load help text and parameter descriptions asynchronously (non-blocking)
        _ = LoadHelpTextAsync();
        _ = EnrichParameterDescriptionsAsync();
    }

    /// <summary>
    /// Called when the selected parameter set changes.
    /// Rebuilds the parameter value ViewModels for the new set.
    /// </summary>
    partial void OnSelectedParameterSetChanged(ParameterSetInfo? value)
    {
        RebuildParameters();
    }

    /// <summary>
    /// Executes the cmdlet with the current parameter values.
    /// Validates mandatory parameters before execution (Rule 17: pre-flight validation).
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task RunAsync()
    {
        // Pre-flight: validate all mandatory parameters have values
        var validationErrors = ValidateParameters();
        if (validationErrors.Count > 0)
        {
            _outputViewModel.ShowError(
                "Validation failed:\n" + string.Join("\n", validationErrors));
            return;
        }

        IsExecuting = true;
        _executionCts = new CancellationTokenSource();

        try
        {
            var parameterValues = BuildParameterDictionary();
            var result = await _commandExecutor.ExecuteAsync(
                Cmdlet.Name, parameterValues, _executionCts.Token);

            _outputViewModel.DisplayResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error executing '{CmdletName}'", Cmdlet.Name);
            _outputViewModel.ShowError($"Unexpected error: {ex.Message}");
        }
        finally
        {
            IsExecuting = false;
            _executionCts?.Dispose();
            _executionCts = null;
        }
    }

    private bool CanRun() => !IsExecuting;

    /// <summary>
    /// Cancels the currently executing command.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        _executionCts?.Cancel();
        _commandExecutor.CancelExecution();
    }

    private bool CanCancel() => IsExecuting;

    /// <summary>
    /// Rebuilds parameter ViewModels when the parameter set changes.
    /// Subscribes to PropertyChanged on each parameter to update the command preview live.
    /// </summary>
    private void RebuildParameters()
    {
        Parameters.Clear();
        MandatoryParameters.Clear();
        OptionalParameters.Clear();

        if (SelectedParameterSet == null) return;

        foreach (var paramInfo in SelectedParameterSet.Parameters)
        {
            var paramVm = new ParameterValueViewModel(paramInfo);

            // Subscribe to value changes so the command preview updates live
            paramVm.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName is nameof(ParameterValueViewModel.Value) or
                    nameof(ParameterValueViewModel.StringValue) or
                    nameof(ParameterValueViewModel.BoolValue) or
                    nameof(ParameterValueViewModel.DateTimeValue))
                {
                    UpdateCommandPreview();
                }
            };

            Parameters.Add(paramVm);

            if (paramInfo.IsMandatory)
            {
                MandatoryParameters.Add(paramVm);
            }
            else
            {
                OptionalParameters.Add(paramVm);
            }
        }

        UpdateCommandPreview();
    }

    /// <summary>
    /// Reconstructs the command preview string from current parameter values.
    /// Called whenever any parameter value changes for live update.
    /// </summary>
    private void UpdateCommandPreview()
    {
        var paramDict = BuildParameterDictionary();
        CommandPreview = CommandExecutor.BuildCommandString(Cmdlet.Name, paramDict);
    }

    /// <summary>
    /// Builds a dictionary of parameter name → typed value for execution.
    /// Only includes parameters that have been given values by the user.
    /// </summary>
    private Dictionary<string, object?> BuildParameterDictionary()
    {
        var dict = new Dictionary<string, object?>();

        foreach (var param in Parameters)
        {
            if (param.HasValue)
            {
                dict[param.Name] = param.GetTypedValue();
            }
        }

        return dict;
    }

    /// <summary>
    /// Validates all mandatory parameters have values.
    /// Accumulates ALL errors before reporting (Rule 11).
    /// </summary>
    private List<string> ValidateParameters()
    {
        var errors = new List<string>();

        foreach (var param in Parameters)
        {
            param.Validate();
            if (!string.IsNullOrEmpty(param.ValidationError))
            {
                errors.Add(param.ValidationError);
            }
        }

        return errors;
    }

    /// <summary>
    /// Loads full help text for the cmdlet asynchronously.
    /// Non-blocking; failure is non-critical and logged at debug level.
    /// </summary>
    private async Task LoadHelpTextAsync()
    {
        try
        {
            HelpText = await _helpTextService.GetFullHelpAsync(Cmdlet.Name);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to load help for '{CmdletName}'", Cmdlet.Name);
            HelpText = "Help text not available.";
        }
    }

    /// <summary>
    /// Enriches all parameter ViewModels across all parameter sets with
    /// descriptions from Get-Help. Runs asynchronously after construction.
    /// </summary>
    private async Task EnrichParameterDescriptionsAsync()
    {
        try
        {
            var descriptions = await _helpTextService.GetParameterDescriptionsAsync(Cmdlet.Name);
            if (descriptions.Count == 0) return;

            // Enrich the underlying ParameterInfo models across ALL parameter sets
            foreach (var paramSet in Cmdlet.ParameterSets)
            {
                foreach (var param in paramSet.Parameters)
                {
                    if (descriptions.TryGetValue(param.Name, out var desc) &&
                        !string.IsNullOrWhiteSpace(desc))
                    {
                        param.Description = desc;
                    }
                }
            }

            // Notify the UI that Description properties have changed on current VMs
            foreach (var paramVm in Parameters)
            {
                paramVm.OnPropertyChanged(nameof(ParameterValueViewModel.Description));
                paramVm.OnPropertyChanged(nameof(ParameterValueViewModel.HasDescription));
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to enrich parameter descriptions for '{CmdletName}'", Cmdlet.Name);
        }
    }
}
