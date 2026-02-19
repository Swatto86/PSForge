using Microsoft.Extensions.Logging;
using PSForge.Core;
using PSForge.Models;

namespace PSForge.Services;

/// <summary>
/// High-level service for discovering and loading PowerShell modules.
/// Wraps <see cref="ModuleIntrospector"/> and <see cref="PowerShellSessionManager"/>
/// to provide a clean API for the ViewModel layer.
///
/// Caches the module list to avoid repeated expensive Get-Module -ListAvailable calls.
/// The cache has a bounded size to prevent unbounded memory growth (Rule 11).
/// </summary>
public sealed class ModuleDiscoveryService
{
    private readonly PowerShellSessionManager _sessionManager;
    private readonly ModuleIntrospector _introspector;
    private readonly ILogger<ModuleDiscoveryService> _logger;
    private List<ModuleInfo>? _cachedModules;

    /// <summary>Maximum number of modules to cache. Safety bound per Rule 11.</summary>
    private const int MaxCachedModules = 5000;

    public ModuleDiscoveryService(
        PowerShellSessionManager sessionManager,
        ModuleIntrospector introspector,
        ILogger<ModuleDiscoveryService> logger)
    {
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        _introspector = introspector ?? throw new ArgumentNullException(nameof(introspector));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Gets all installed modules. Results are cached after the first call.
    /// Call <see cref="RefreshModulesAsync"/> to force a re-scan.
    /// </summary>
    public async Task<List<ModuleInfo>> GetAvailableModulesAsync()
    {
        if (_cachedModules != null)
        {
            _logger.LogDebug("Returning {Count} cached modules", _cachedModules.Count);
            return _cachedModules;
        }

        _logger.LogInformation("Fetching available modules (no cache)...");
        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            var modules = await _introspector.GetInstalledModulesAsync();
            sw.Stop();

            // Enforce cache size bound
            if (modules.Count > MaxCachedModules)
            {
                _logger.LogWarning(
                    "Module count ({Count}) exceeds cache limit ({Limit}), truncating",
                    modules.Count, MaxCachedModules);
                modules = modules.Take(MaxCachedModules).ToList();
            }

            _cachedModules = modules;
            _logger.LogInformation("Cached {Count} modules (fetched in {ElapsedMs}ms)",
                _cachedModules.Count, sw.ElapsedMilliseconds);
            return _cachedModules;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch available modules");
            throw;
        }
    }

    /// <summary>
    /// Forces a fresh scan of installed modules, clearing the cache.
    /// </summary>
    public async Task<List<ModuleInfo>> RefreshModulesAsync()
    {
        _logger.LogInformation("Refreshing module list");
        _cachedModules = null;
        return await GetAvailableModulesAsync();
    }

    /// <summary>
    /// Loads a module and retrieves its full cmdlet catalog with parameter metadata.
    /// This is the primary entry point for loading a module into the application.
    /// </summary>
    /// <param name="moduleName">Module name to load.</param>
    /// <returns>Updated ModuleInfo with populated Cmdlets list.</returns>
    public async Task<ModuleInfo> LoadModuleWithCmdletsAsync(string moduleName)
    {
        _logger.LogInformation("Loading module '{ModuleName}' with full cmdlet metadata...", moduleName);
        var overallSw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            // Load the module into both runspaces
            _logger.LogDebug("Step 1/2: Importing module '{ModuleName}' into runspaces", moduleName);
            var importSw = System.Diagnostics.Stopwatch.StartNew();
            await _sessionManager.LoadModuleAsync(moduleName);
            importSw.Stop();
            _logger.LogInformation("Module '{ModuleName}' imported in {ElapsedMs}ms", moduleName, importSw.ElapsedMilliseconds);

            // Enumerate all cmdlets with parameter metadata
            _logger.LogDebug("Step 2/2: Enumerating cmdlets for module '{ModuleName}'", moduleName);
            var cmdletSw = System.Diagnostics.Stopwatch.StartNew();
            var cmdlets = await _introspector.GetModuleCmdletsAsync(moduleName);
            cmdletSw.Stop();
            _logger.LogInformation("Enumerated {CmdletCount} cmdlets in {ElapsedMs}ms", 
                cmdlets.Count, cmdletSw.ElapsedMilliseconds);

            // Find the module in cache to preserve its metadata (version, author, etc.)
            var moduleInfo = _cachedModules?.FirstOrDefault(m =>
                m.Name.Equals(moduleName, StringComparison.OrdinalIgnoreCase));

            if (moduleInfo != null)
            {
                _logger.LogDebug("Found module '{ModuleName}' in cache, preserving metadata", moduleName);
                // Return a new instance with the cmdlets populated
                var result = new ModuleInfo
                {
                    Name = moduleInfo.Name,
                    Version = moduleInfo.Version,
                    Description = moduleInfo.Description,
                    ModulePath = moduleInfo.ModulePath,
                    Author = moduleInfo.Author,
                    ModuleType = moduleInfo.ModuleType,
                    HasConnectCmdlet = moduleInfo.HasConnectCmdlet,
                    ConnectCmdletName = moduleInfo.ConnectCmdletName,
                    DisconnectCmdletName = moduleInfo.DisconnectCmdletName,
                    Cmdlets = cmdlets
                };

                overallSw.Stop();
                _logger.LogInformation("Module '{ModuleName}' fully loaded with {CmdletCount} cmdlets (total time: {ElapsedMs}ms)",
                    moduleName, cmdlets.Count, overallSw.ElapsedMilliseconds);
                return result;
            }

            // Not in cache (shouldn't normally happen); build a minimal ModuleInfo
            _logger.LogWarning("Module '{ModuleName}' not found in cache, creating minimal metadata", moduleName);
            var minimalResult = new ModuleInfo
            {
                Name = moduleName,
                Cmdlets = cmdlets
            };

            overallSw.Stop();
            _logger.LogInformation("Module '{ModuleName}' loaded with minimal metadata (total time: {ElapsedMs}ms)",
                moduleName, overallSw.ElapsedMilliseconds);
            return minimalResult;
        }
        catch (Exception ex)
        {
            overallSw.Stop();
            _logger.LogError(ex, "Failed to load module '{ModuleName}' after {ElapsedMs}ms", 
                moduleName, overallSw.ElapsedMilliseconds);
            throw;
        }
    }
}
