using System.Collections;
using System.Management.Automation;
using Microsoft.Extensions.Logging;
using PSForge.Models;
using ParameterInfo = PSForge.Models.ParameterInfo;
using CmdletInfo = PSForge.Models.CmdletInfo;
using ModuleInfo = PSForge.Models.ModuleInfo;

namespace PSForge.Core;

/// <summary>
/// Provides PowerShell module introspection capabilities using the introspection runspace.
/// Wraps Get-Module, Get-Command, and Get-Help to produce structured metadata models
/// that drive the dynamic UI generation.
///
/// All operations run against the introspection runspace (never the execution runspace)
/// to avoid polluting the environment where user commands run.
/// </summary>
public sealed class ModuleIntrospector
{
    private readonly PowerShellSessionManager _sessionManager;
    private readonly ILogger<ModuleIntrospector> _logger;

    /// <summary>
    /// Common parameters present on every cmdlet (from CmdletBinding).
    /// Filtered out of the UI since users rarely need to set them via a GUI.
    /// </summary>
    private static readonly HashSet<string> CommonParameterNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Verbose", "Debug", "ErrorAction", "WarningAction", "InformationAction",
        "ErrorVariable", "WarningVariable", "InformationVariable", "OutVariable",
        "OutBuffer", "PipelineVariable", "ProgressAction",
        "WhatIf", "Confirm"
    };

    public ModuleIntrospector(
        PowerShellSessionManager sessionManager,
        ILogger<ModuleIntrospector> logger)
    {
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Discovers all installed PowerShell modules via Get-Module -ListAvailable.
    /// Returns lightweight module metadata without loading the modules.
    /// </summary>
    public async Task<List<ModuleInfo>> GetInstalledModulesAsync()
    {
        _logger.LogDebug("Discovering installed PowerShell modules");

        return await Task.Run(() =>
        {
            using var ps = _sessionManager.CreateIntrospectionShell();
            ps.AddCommand("Get-Module")
                .AddParameter("ListAvailable");

            var results = ps.Invoke();
            var modules = new List<ModuleInfo>();

            foreach (var result in results)
            {
                try
                {
                    var module = ParseModuleInfo(result);
                    if (module != null)
                    {
                        modules.Add(module);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to parse module metadata for an entry, skipping");
                }
            }

            _logger.LogInformation("Discovered {ModuleCount} installed modules", modules.Count);
            return modules;
        });
    }

    /// <summary>
    /// Enumerates all cmdlets/functions exported by a loaded module,
    /// including full parameter set and parameter metadata.
    /// </summary>
    /// <param name="moduleName">Name of the module (must already be loaded via SessionManager).</param>
    public async Task<List<CmdletInfo>> GetModuleCmdletsAsync(string moduleName)
    {
        _logger.LogDebug("Enumerating cmdlets for module '{ModuleName}'", moduleName);

        return await Task.Run(() =>
        {
            using var ps = _sessionManager.CreateIntrospectionShell();
            ps.AddCommand("Get-Command")
                .AddParameter("Module", moduleName);

            var results = ps.Invoke();
            var cmdlets = new List<CmdletInfo>();

            foreach (var result in results)
            {
                try
                {
                    var cmdlet = ParseCmdletInfo(result);
                    if (cmdlet != null)
                    {
                        cmdlets.Add(cmdlet);
                    }
                }
                catch (Exception ex)
                {
                    var name = result.Properties["Name"]?.Value?.ToString() ?? "unknown";
                    _logger.LogWarning(ex, "Failed to parse cmdlet '{CmdletName}', skipping", name);
                }
            }

            _logger.LogInformation("Found {CmdletCount} cmdlets in module '{ModuleName}'",
                cmdlets.Count, moduleName);
            return cmdlets;
        });
    }

    /// <summary>
    /// Gets the synopsis text for a specific cmdlet via Get-Help.
    /// </summary>
    public async Task<string> GetCmdletSynopsisAsync(string cmdletName)
    {
        return await Task.Run(() =>
        {
            try
            {
                using var ps = _sessionManager.CreateIntrospectionShell();
                ps.AddCommand("Get-Help")
                    .AddParameter("Name", cmdletName)
                    .AddParameter("ErrorAction", "SilentlyContinue");

                var results = ps.Invoke();
                if (results.Count > 0)
                {
                    var synopsis = results[0].Properties["Synopsis"]?.Value?.ToString();
                    return synopsis?.Trim() ?? string.Empty;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to get synopsis for '{CmdletName}'", cmdletName);
            }

            return string.Empty;
        });
    }

    /// <summary>
    /// Gets the full help text for a specific cmdlet via Get-Help -Full,
    /// formatted as a single string via Out-String.
    /// </summary>
    public async Task<string> GetCmdletFullHelpAsync(string cmdletName)
    {
        return await Task.Run(() =>
        {
            try
            {
                using var ps = _sessionManager.CreateIntrospectionShell();
                ps.AddCommand("Get-Help")
                    .AddParameter("Name", cmdletName)
                    .AddParameter("Full");
                // Pipe through Out-String to get formatted text output
                ps.AddCommand("Out-String");

                var results = ps.Invoke();
                return results.Count > 0 ? results[0].ToString() : string.Empty;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to get full help for '{CmdletName}'", cmdletName);
                return string.Empty;
            }
        });
    }

    /// <summary>
    /// Gets per-parameter descriptions from Get-Help structured output.
    /// Returns a dictionary mapping parameter name → description text.
    /// Uses the structured help object's parameters collection for rich descriptions
    /// beyond what the [Parameter(HelpMessage)] attribute provides.
    /// </summary>
    public async Task<Dictionary<string, string>> GetParameterDescriptionsAsync(string cmdletName)
    {
        return await Task.Run(() =>
        {
            var descriptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using var ps = _sessionManager.CreateIntrospectionShell();
                ps.AddCommand("Get-Help")
                    .AddParameter("Name", cmdletName)
                    .AddParameter("Parameter", "*")
                    .AddParameter("ErrorAction", "SilentlyContinue");

                var results = ps.Invoke();
                foreach (var result in results)
                {
                    try
                    {
                        var name = result.Properties["name"]?.Value?.ToString();
                        if (string.IsNullOrEmpty(name)) continue;

                        // The description is an array of objects with a Text property
                        var descriptionObj = result.Properties["description"]?.Value;
                        var descText = ExtractHelpText(descriptionObj);

                        if (!string.IsNullOrWhiteSpace(descText))
                        {
                            descriptions[name] = descText.Trim();
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Failed to parse help for a parameter of '{CmdletName}'", cmdletName);
                    }
                }

                _logger.LogDebug("Extracted {Count} parameter descriptions for '{CmdletName}'",
                    descriptions.Count, cmdletName);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to get parameter descriptions for '{CmdletName}'", cmdletName);
            }

            return descriptions;
        });
    }

    /// <summary>
    /// Extracts description text from a Get-Help description object.
    /// Help descriptions can be arrays of PSObjects with a Text property,
    /// or simple strings depending on the help format.
    /// </summary>
    private static string ExtractHelpText(object? descriptionObj)
    {
        if (descriptionObj == null) return string.Empty;

        // Direct string case
        if (descriptionObj is string str) return str;

        // Array of PSObjects with Text property (common MAML format)
        if (descriptionObj is object[] arr)
        {
            var parts = new List<string>();
            foreach (var item in arr)
            {
                if (item is PSObject pso)
                {
                    var text = pso.Properties["Text"]?.Value?.ToString();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        parts.Add(text.Trim());
                    }
                }
                else if (item is string s)
                {
                    parts.Add(s.Trim());
                }
            }
            return string.Join(" ", parts);
        }

        // IEnumerable of PSObjects
        if (descriptionObj is System.Collections.IEnumerable enumerable)
        {
            var parts = new List<string>();
            foreach (var item in enumerable)
            {
                if (item is PSObject pso)
                {
                    var text = pso.Properties["Text"]?.Value?.ToString();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        parts.Add(text.Trim());
                    }
                }
            }
            if (parts.Count > 0) return string.Join(" ", parts);
        }

        // Fallback: ToString
        return descriptionObj.ToString() ?? string.Empty;
    }

    /// <summary>
    /// Parses a PSObject representing a module into a <see cref="ModuleInfo"/>.
    /// Detects Connect-*/Disconnect-* cmdlets by examining ExportedCommands.
    /// </summary>
    private ModuleInfo? ParseModuleInfo(PSObject psObject)
    {
        var name = psObject.Properties["Name"]?.Value?.ToString();
        if (string.IsNullOrEmpty(name)) return null;

        var version = psObject.Properties["Version"]?.Value?.ToString() ?? string.Empty;
        var description = psObject.Properties["Description"]?.Value?.ToString() ?? string.Empty;
        var path = psObject.Properties["ModuleBase"]?.Value?.ToString() ?? string.Empty;
        var author = psObject.Properties["Author"]?.Value?.ToString() ?? string.Empty;
        var moduleType = psObject.Properties["ModuleType"]?.Value?.ToString() ?? string.Empty;

        // Detect Connect-*/Disconnect-* cmdlets from the ExportedCommands dictionary.
        // These indicate session-based modules that require an explicit connection step.
        string? connectCmdlet = null;
        string? disconnectCmdlet = null;

        try
        {
            var exportedCommands = psObject.Properties["ExportedCommands"]?.Value;
            if (exportedCommands is IDictionary dict)
            {
                foreach (var key in dict.Keys)
                {
                    var keyStr = key?.ToString() ?? string.Empty;
                    if (keyStr.StartsWith("Connect-", StringComparison.OrdinalIgnoreCase) && connectCmdlet == null)
                    {
                        connectCmdlet = keyStr;
                    }
                    else if (keyStr.StartsWith("Disconnect-", StringComparison.OrdinalIgnoreCase) && disconnectCmdlet == null)
                    {
                        disconnectCmdlet = keyStr;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to inspect ExportedCommands for module '{ModuleName}'", name);
        }

        return new ModuleInfo
        {
            Name = name,
            Version = version,
            Description = description,
            ModulePath = path,
            Author = author,
            ModuleType = moduleType,
            HasConnectCmdlet = connectCmdlet != null,
            ConnectCmdletName = connectCmdlet,
            DisconnectCmdletName = disconnectCmdlet
        };
    }

    /// <summary>
    /// Parses a PSObject (wrapping a CommandInfo) into a <see cref="CmdletInfo"/>
    /// with its complete parameter set hierarchy.
    /// </summary>
    private CmdletInfo? ParseCmdletInfo(PSObject psObject)
    {
        var name = psObject.Properties["Name"]?.Value?.ToString();
        if (string.IsNullOrEmpty(name)) return null;

        // Split verb-noun from cmdlet name (e.g., "Get-Process" → "Get", "Process")
        var parts = name.Split('-', 2);
        var verb = parts.Length > 0 ? parts[0] : string.Empty;
        var noun = parts.Length > 1 ? parts[1] : string.Empty;

        var moduleName = psObject.Properties["ModuleName"]?.Value?.ToString() ?? string.Empty;
        var parameterSets = new List<Models.ParameterSetInfo>();
        var defaultParameterSet = string.Empty;

        // Access the underlying CommandInfo object for rich parameter metadata.
        // The PSObject wraps the real CommandInfo returned by Get-Command.
        if (psObject.BaseObject is CommandInfo commandInfo)
        {
            // DefaultParameterSet only exists on CmdletInfo, not on the base CommandInfo.
            // Fall back to finding the default set from the ParameterSets collection.
            if (commandInfo is System.Management.Automation.CmdletInfo cmdInfo)
            {
                defaultParameterSet = cmdInfo.DefaultParameterSet ?? string.Empty;
            }

            foreach (var psParamSet in commandInfo.ParameterSets)
            {
                var parameters = psParamSet.Parameters
                    .Where(p => !CommonParameterNames.Contains(p.Name))
                    .Select(p => ParseParameterInfo(p, psParamSet.Name))
                    .ToList();

                parameterSets.Add(new Models.ParameterSetInfo
                {
                    Name = psParamSet.Name,
                    IsDefault = psParamSet.IsDefault,
                    Parameters = parameters
                });
            }
        }

        return new CmdletInfo
        {
            Name = name,
            Verb = verb,
            Noun = noun,
            ModuleName = moduleName,
            DefaultParameterSetName = defaultParameterSet,
            ParameterSets = parameterSets
        };
    }

    /// <summary>
    /// Converts a PowerShell CommandParameterInfo into our <see cref="ParameterInfo"/> model.
    /// Extracts ValidateSet values, aliases, and type metadata.
    /// </summary>
    private ParameterInfo ParseParameterInfo(
        CommandParameterInfo psParam,
        string parameterSetName)
    {
        // Extract ValidateSet values from attributes — these drive ComboBox population
        var validateSetValues = Array.Empty<string>();
        try
        {
            if (psParam.Attributes != null)
            {
                var validateSet = psParam.Attributes
                    .OfType<ValidateSetAttribute>()
                    .FirstOrDefault();

                if (validateSet != null)
                {
                    validateSetValues = validateSet.ValidValues.ToArray();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to extract ValidateSet for parameter '{ParamName}'", psParam.Name);
        }

        // Extract aliases (non-critical metadata)
        var aliases = Array.Empty<string>();
        try
        {
            if (psParam.Aliases != null && psParam.Aliases.Count > 0)
            {
                aliases = psParam.Aliases.ToArray();
            }
        }
        catch
        {
            // Aliases are non-critical; swallow errors silently
        }

        return new ParameterInfo
        {
            Name = psParam.Name,
            ParameterType = psParam.ParameterType ?? typeof(object),
            TypeName = psParam.ParameterType?.Name ?? "Object",
            IsMandatory = psParam.IsMandatory,
            Position = psParam.Position,
            HelpMessage = psParam.HelpMessage ?? string.Empty,
            Aliases = aliases,
            IsDynamic = psParam.IsDynamic,
            ValidateSetValues = validateSetValues,
            ParameterSetName = parameterSetName
        };
    }
}
