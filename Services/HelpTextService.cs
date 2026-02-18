using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using PSForge.Core;

namespace PSForge.Services;

/// <summary>
/// Fetches and caches full Get-Help output per cmdlet.
/// Uses a thread-safe concurrent dictionary to cache results,
/// avoiding repeated expensive help lookups for cmdlets the user revisits.
///
/// Cache is bounded to <see cref="MaxCacheSize"/> entries (Rule 11).
/// When the limit is reached, the cache is cleared entirely — a simple strategy
/// that avoids the complexity of LRU eviction for this non-critical cache.
/// </summary>
public sealed class HelpTextService
{
    private readonly ModuleIntrospector _introspector;
    private readonly ILogger<HelpTextService> _logger;

    /// <summary>
    /// Thread-safe cache: cmdlet name → full help text.
    /// ConcurrentDictionary chosen because help may be fetched from background threads
    /// while the UI reads cached values on the dispatcher thread.
    /// </summary>
    private readonly ConcurrentDictionary<string, string> _helpCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Thread-safe cache: cmdlet name → parameter name → description.</summary>
    private readonly ConcurrentDictionary<string, Dictionary<string, string>> _paramDescCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Maximum number of cached help entries to prevent unbounded memory growth.</summary>
    private const int MaxCacheSize = 500;

    public HelpTextService(ModuleIntrospector introspector, ILogger<HelpTextService> logger)
    {
        _introspector = introspector ?? throw new ArgumentNullException(nameof(introspector));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Gets the synopsis (short description) for a cmdlet.
    /// Synopsis is not cached since it's typically fetched once during module load.
    /// </summary>
    public async Task<string> GetSynopsisAsync(string cmdletName)
    {
        return await _introspector.GetCmdletSynopsisAsync(cmdletName);
    }

    /// <summary>
    /// Gets the full help text for a cmdlet, using a cache to avoid repeated lookups.
    /// </summary>
    public async Task<string> GetFullHelpAsync(string cmdletName)
    {
        if (_helpCache.TryGetValue(cmdletName, out var cached))
        {
            _logger.LogDebug("Help cache hit for '{CmdletName}'", cmdletName);
            return cached;
        }

        _logger.LogDebug("Fetching full help for '{CmdletName}'", cmdletName);
        var helpText = await _introspector.GetCmdletFullHelpAsync(cmdletName);

        // Enforce cache size bound: clear when full to prevent unbounded growth
        if (_helpCache.Count >= MaxCacheSize)
        {
            _logger.LogInformation("Help cache reached {MaxCacheSize} entries, clearing", MaxCacheSize);
            _helpCache.Clear();
        }

        _helpCache.TryAdd(cmdletName, helpText);
        return helpText;
    }

    /// <summary>
    /// Gets per-parameter descriptions from Get-Help structured output.
    /// Cached per cmdlet to avoid repeated expensive help lookups.
    /// </summary>
    public async Task<Dictionary<string, string>> GetParameterDescriptionsAsync(string cmdletName)
    {
        if (_paramDescCache.TryGetValue(cmdletName, out var cached))
        {
            _logger.LogDebug("Parameter description cache hit for '{CmdletName}'", cmdletName);
            return cached;
        }

        _logger.LogDebug("Fetching parameter descriptions for '{CmdletName}'", cmdletName);
        var descriptions = await _introspector.GetParameterDescriptionsAsync(cmdletName);

        // Enforce cache size bound
        if (_paramDescCache.Count >= MaxCacheSize)
        {
            _logger.LogInformation("Parameter description cache reached {MaxCacheSize} entries, clearing", MaxCacheSize);
            _paramDescCache.Clear();
        }

        _paramDescCache.TryAdd(cmdletName, descriptions);
        return descriptions;
    }

    /// <summary>
    /// Clears the help cache. Called when switching modules to avoid stale entries.
    /// </summary>
    public void ClearCache()
    {
        _helpCache.Clear();
        _paramDescCache.Clear();
        _logger.LogDebug("Help cache cleared");
    }
}
