namespace PSForge.Models;

/// <summary>
/// Represents metadata about an installed PowerShell module.
/// Populated from Get-Module -ListAvailable output by <see cref="PSForge.Core.ModuleIntrospector"/>.
/// </summary>
public sealed class ModuleInfo
{
    /// <summary>Module name as it appears in the PowerShell module repository.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Module version string (e.g., "2.0.1").</summary>
    public string Version { get; init; } = string.Empty;

    /// <summary>Module description from the module manifest.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>Filesystem path to the module root directory.</summary>
    public string ModulePath { get; init; } = string.Empty;

    /// <summary>Module author from the manifest.</summary>
    public string Author { get; init; } = string.Empty;

    /// <summary>Module type: Script, Binary, Manifest, or Cim.</summary>
    public string ModuleType { get; init; } = string.Empty;

    /// <summary>
    /// Indicates this module contains a Connect-* cmdlet, meaning it likely
    /// requires an explicit connection step before cmdlets can be used
    /// (e.g., ExchangeOnlineManagement, MicrosoftTeams).
    /// </summary>
    public bool HasConnectCmdlet { get; init; }

    /// <summary>
    /// Full name of the Connect cmdlet (e.g., "Connect-ExchangeOnline") if discovered.
    /// </summary>
    public string? ConnectCmdletName { get; init; }

    /// <summary>
    /// Full name of the Disconnect cmdlet if discovered.
    /// </summary>
    public string? DisconnectCmdletName { get; init; }

    /// <summary>All cmdlets exported by this module.</summary>
    public List<CmdletInfo> Cmdlets { get; init; } = new();

    public override string ToString() => $"{Name} v{Version}";
}
