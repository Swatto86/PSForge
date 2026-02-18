using System.Management.Automation;
using System.Management.Automation.Runspaces;
using Microsoft.Extensions.Logging;

namespace PSForge.Core;

/// <summary>
/// Manages the PowerShell runspace lifecycle for the application.
/// Maintains two separate runspaces to prevent state pollution:
///   1. Introspection runspace — used for module discovery, Get-Command, Get-Help
///   2. Execution runspace — used for running user-selected cmdlets
///
/// This separation ensures that introspection queries (which may alter module state)
/// do not affect the environment in which user commands execute.
/// </summary>
public sealed class PowerShellSessionManager : IDisposable
{
    private readonly ILogger<PowerShellSessionManager> _logger;
    private Runspace? _introspectionRunspace;
    private Runspace? _executionRunspace;
    private string? _loadedModuleName;
    private bool _isConnected;
    private bool _disposed;

    /// <summary>Maximum retry attempts for transient PowerShell operations (Rule 11).</summary>
    private const int MaxRetries = 3;

    /// <summary>Initial backoff delay for retries (Rule 11: 50ms → 100ms → 200ms).</summary>
    private static readonly TimeSpan InitialBackoff = TimeSpan.FromMilliseconds(50);

    /// <summary>Whether a module has been loaded into the runspaces.</summary>
    public bool IsModuleLoaded => _loadedModuleName != null;

    /// <summary>Name of the currently loaded module, if any.</summary>
    public string? LoadedModuleName => _loadedModuleName;

    /// <summary>Whether a session-based connection (e.g., Connect-ExchangeOnline) is active.</summary>
    public bool IsConnected => _isConnected;

    public PowerShellSessionManager(ILogger<PowerShellSessionManager> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Initializes both runspaces. Must be called before any other operations.
    /// Each runspace gets its own InitialSessionState so they are fully isolated.
    /// </summary>
    public void Initialize()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _logger.LogDebug("Initializing PowerShell runspaces");

        _introspectionRunspace?.Dispose();
        _executionRunspace?.Dispose();

        // CreateDefault2() loads standard PS commands (Get-Command, Import-Module, etc.)
        // without loading user profile scripts that might have side effects.
        // Set ExecutionPolicy to RemoteSigned on the InitialSessionState BEFORE opening
        // the runspace — this is the only reliable way to set policy for hosted runspaces.
        // Set-ExecutionPolicy inside an already-open runspace does not work for the SDK host.
        var issIntrospection = InitialSessionState.CreateDefault2();
        issIntrospection.ExecutionPolicy = Microsoft.PowerShell.ExecutionPolicy.RemoteSigned;
        _introspectionRunspace = RunspaceFactory.CreateRunspace(issIntrospection);
        _introspectionRunspace.Open();

        var issExecution = InitialSessionState.CreateDefault2();
        issExecution.ExecutionPolicy = Microsoft.PowerShell.ExecutionPolicy.RemoteSigned;
        _executionRunspace = RunspaceFactory.CreateRunspace(issExecution);
        _executionRunspace.Open();

        _logger.LogInformation("PowerShell runspaces initialized (ExecutionPolicy: RemoteSigned)");
    }

    /// <summary>
    /// Creates a PowerShell instance bound to the introspection runspace.
    /// Caller is responsible for disposing the returned instance.
    /// </summary>
    public PowerShell CreateIntrospectionShell()
    {
        EnsureInitialized();
        var ps = PowerShell.Create();
        ps.Runspace = _introspectionRunspace;
        return ps;
    }

    /// <summary>
    /// Creates a PowerShell instance bound to the execution runspace.
    /// Caller is responsible for disposing the returned instance.
    /// </summary>
    public PowerShell CreateExecutionShell()
    {
        EnsureInitialized();
        var ps = PowerShell.Create();
        ps.Runspace = _executionRunspace;
        return ps;
    }

    /// <summary>
    /// Loads a module into both runspaces by name.
    /// Uses Import-Module with -Force to ensure the latest version is loaded.
    /// Retries transient failures with exponential backoff per Rule 11.
    /// </summary>
    /// <param name="moduleName">Name of the module to import.</param>
    /// <exception cref="InvalidOperationException">If module import fails after all retries.</exception>
    public async Task LoadModuleAsync(string moduleName)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureInitialized();

        _logger.LogInformation("Loading module '{ModuleName}' into runspaces", moduleName);

        await Task.Run(() =>
        {
            LoadModuleIntoRunspace(_introspectionRunspace!, moduleName, "introspection");
            LoadModuleIntoRunspace(_executionRunspace!, moduleName, "execution");
            _loadedModuleName = moduleName;
        });

        _logger.LogInformation("Module '{ModuleName}' loaded successfully", moduleName);
    }

    /// <summary>
    /// Invokes a Connect-* cmdlet in the execution runspace for session-based modules.
    /// </summary>
    /// <param name="connectCmdletName">Full name of the connect cmdlet (e.g., Connect-ExchangeOnline).</param>
    /// <param name="parameters">Optional parameters to pass to the connect cmdlet.</param>
    public async Task ConnectAsync(string connectCmdletName, Dictionary<string, object>? parameters = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureInitialized();

        _logger.LogInformation("Connecting via '{ConnectCmdlet}'", connectCmdletName);

        await Task.Run(() =>
        {
            using var ps = PowerShell.Create();
            ps.Runspace = _executionRunspace;
            ps.AddCommand(connectCmdletName);

            if (parameters != null)
            {
                foreach (var kvp in parameters)
                {
                    ps.AddParameter(kvp.Key, kvp.Value);
                }
            }

            ps.Invoke();

            if (ps.HadErrors)
            {
                var errors = string.Join(Environment.NewLine,
                    ps.Streams.Error.Select(e => e.ToString()));
                throw new InvalidOperationException(
                    $"Connection via '{connectCmdletName}' failed: {errors}");
            }

            _isConnected = true;
        });

        _logger.LogInformation("Connected successfully via '{ConnectCmdlet}'", connectCmdletName);
    }

    /// <summary>
    /// Disconnects from a session-based module by invoking its Disconnect-* cmdlet.
    /// </summary>
    public async Task DisconnectAsync(string disconnectCmdletName)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _logger.LogInformation("Disconnecting via '{DisconnectCmdlet}'", disconnectCmdletName);

        await Task.Run(() =>
        {
            using var ps = PowerShell.Create();
            ps.Runspace = _executionRunspace;
            ps.AddCommand(disconnectCmdletName);
            ps.Invoke();
            _isConnected = false;
        });

        _logger.LogInformation("Disconnected successfully");
    }

    /// <summary>
    /// Unloads the current module by disposing and re-creating both runspaces.
    /// This ensures a completely clean state with no residual module artifacts.
    /// </summary>
    public void UnloadModule()
    {
        _logger.LogInformation("Unloading module '{ModuleName}'", _loadedModuleName);
        _loadedModuleName = null;
        _isConnected = false;
        Initialize();
    }

    /// <summary>
    /// Loads a module into a specific runspace with retry logic for transient errors.
    /// </summary>
    private void LoadModuleIntoRunspace(Runspace runspace, string moduleName, string runspaceName)
    {
        Exception? lastException = null;

        for (int attempt = 1; attempt <= MaxRetries; attempt++)
        {
            try
            {
                using var ps = PowerShell.Create();
                ps.Runspace = runspace;
                ps.AddCommand("Import-Module")
                    .AddParameter("Name", moduleName)
                    .AddParameter("Force");

                ps.Invoke();

                if (ps.HadErrors)
                {
                    var errors = string.Join(Environment.NewLine,
                        ps.Streams.Error.Select(e => e.ToString()));
                    throw new InvalidOperationException(
                        $"Failed to load module '{moduleName}' into {runspaceName} runspace: {errors}");
                }

                return; // Success
            }
            catch (Exception ex) when (attempt < MaxRetries && IsTransientError(ex))
            {
                lastException = ex;
                var delay = InitialBackoff * Math.Pow(2, attempt - 1);
                _logger.LogWarning(ex,
                    "Transient error loading module '{ModuleName}' (attempt {Attempt}/{MaxRetries}), retrying in {Delay}ms",
                    moduleName, attempt, MaxRetries, delay.TotalMilliseconds);
                Thread.Sleep(delay);
            }
        }

        _logger.LogError(lastException,
            "All {MaxRetries} attempts to load module '{ModuleName}' exhausted",
            MaxRetries, moduleName);
        throw lastException ?? new InvalidOperationException($"Failed to load module '{moduleName}'");
    }

    /// <summary>
    /// Determines if an exception represents a transient error that may succeed on retry.
    /// File locks, busy runspaces, and certain runtime errors are considered transient.
    /// </summary>
    private static bool IsTransientError(Exception ex)
    {
        return ex is RuntimeException ||
               ex is InvalidRunspaceStateException ||
               ex.Message.Contains("locked", StringComparison.OrdinalIgnoreCase) ||
               ex.Message.Contains("busy", StringComparison.OrdinalIgnoreCase);
    }

    private void EnsureInitialized()
    {
        if (_introspectionRunspace == null ||
            _introspectionRunspace.RunspaceStateInfo.State != RunspaceState.Opened ||
            _executionRunspace == null ||
            _executionRunspace.RunspaceStateInfo.State != RunspaceState.Opened)
        {
            throw new InvalidOperationException(
                "Session manager is not initialized or runspaces are not open. Call Initialize() first.");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _logger.LogDebug("Disposing PowerShell session manager");
        _introspectionRunspace?.Dispose();
        _executionRunspace?.Dispose();
    }
}
