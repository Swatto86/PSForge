using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using PSForge.Core;
using PSForge.Models;
using PSForge.Services;
using CmdletInfo = PSForge.Models.CmdletInfo;
using ModuleInfo = PSForge.Models.ModuleInfo;

namespace PSForge.ViewModels;

/// <summary>
/// Primary ViewModel for the main application window.
/// Manages module selection, cmdlet browsing, search/filtering, and connection state.
/// Coordinates between the module discovery service and the cmdlet/output ViewModels.
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly ModuleDiscoveryService _discoveryService;
    private readonly PowerShellSessionManager _sessionManager;
    private readonly HelpTextService _helpTextService;
    private readonly CommandExecutor _commandExecutor;
    private readonly OutputFormatterService _outputFormatter;
    private readonly ILogger<MainViewModel> _logger;

    /// <summary>All modules returned by Get-Module -ListAvailable.</summary>
    public ObservableCollection<ModuleInfo> AvailableModules { get; } = new();

    /// <summary>Cmdlets from the currently loaded module, filtered by search text.</summary>
    public ObservableCollection<CmdletInfo> FilteredCmdlets { get; } = new();

    /// <summary>Cmdlets grouped by noun for the tree view.</summary>
    public ObservableCollection<CmdletGroupViewModel> GroupedCmdlets { get; } = new();

    /// <summary>The currently selected module.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadModuleCommand))]
    private ModuleInfo? _selectedModule;

    /// <summary>The currently selected cmdlet in the browser.</summary>
    [ObservableProperty]
    private CmdletInfo? _selectedCmdlet;

    /// <summary>Search/filter text for the cmdlet list.</summary>
    [ObservableProperty]
    private string _searchText = string.Empty;

    /// <summary>Whether a long-running operation is in progress.</summary>
    [ObservableProperty]
    private bool _isLoading;

    /// <summary>Status bar text.</summary>
    [ObservableProperty]
    private string _statusText = "Ready";

    /// <summary>Whether a module has been loaded and its cmdlets are available.</summary>
    [ObservableProperty]
    private bool _isModuleLoaded;

    /// <summary>Connection state for session-based modules.</summary>
    [ObservableProperty]
    private bool _isConnected;

    /// <summary>Whether the loaded module requires a connection step.</summary>
    [ObservableProperty]
    private bool _requiresConnection;

    /// <summary>Error message to display to the user, if any.</summary>
    [ObservableProperty]
    private string? _errorMessage;

    /// <summary>The active cmdlet detail ViewModel.</summary>
    [ObservableProperty]
    private CmdletViewModel? _activeCmdletViewModel;

    /// <summary>Description of the currently loaded module.</summary>
    [ObservableProperty]
    private string _moduleDescription = string.Empty;

    /// <summary>Author of the currently loaded module.</summary>
    [ObservableProperty]
    private string _moduleAuthor = string.Empty;

    /// <summary>Version of the currently loaded module.</summary>
    [ObservableProperty]
    private string _moduleVersion = string.Empty;

    /// <summary>Whether module info is available to display.</summary>
    public bool HasModuleInfo => !string.IsNullOrWhiteSpace(ModuleDescription) ||
                                 !string.IsNullOrWhiteSpace(ModuleAuthor);

    partial void OnModuleDescriptionChanged(string value)
    {
        OnPropertyChanged(nameof(HasModuleInfo));
    }

    /// <summary>The output panel ViewModel (shared across cmdlet switches).</summary>
    public OutputViewModel OutputViewModel { get; }

    // Private backing store for the full unfiltered cmdlet list
    private List<CmdletInfo> _allCmdlets = new();
    private ModuleInfo? _loadedModuleInfo;

    public MainViewModel(
        ModuleDiscoveryService discoveryService,
        PowerShellSessionManager sessionManager,
        HelpTextService helpTextService,
        CommandExecutor commandExecutor,
        OutputFormatterService outputFormatter,
        ILogger<MainViewModel> logger)
    {
        _discoveryService = discoveryService ?? throw new ArgumentNullException(nameof(discoveryService));
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        _helpTextService = helpTextService ?? throw new ArgumentNullException(nameof(helpTextService));
        _commandExecutor = commandExecutor ?? throw new ArgumentNullException(nameof(commandExecutor));
        _outputFormatter = outputFormatter ?? throw new ArgumentNullException(nameof(outputFormatter));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        OutputViewModel = new OutputViewModel(outputFormatter, logger);
    }

    /// <summary>
    /// Called when SearchText changes. Applies the filter to the cmdlet list.
    /// </summary>
    partial void OnSearchTextChanged(string value)
    {
        ApplyFilter();
    }

    /// <summary>
    /// Called when a cmdlet is selected in the browser.
    /// Creates a new CmdletViewModel for the selected cmdlet with a fresh form state.
    /// </summary>
    partial void OnSelectedCmdletChanged(CmdletInfo? value)
    {
        if (value == null)
        {
            ActiveCmdletViewModel = null;
            return;
        }

        ActiveCmdletViewModel = new CmdletViewModel(
            value, _commandExecutor, _helpTextService, OutputViewModel,
            _logger);

        _logger.LogDebug("Selected cmdlet: {CmdletName}", value.Name);
    }

    /// <summary>
    /// Discovers and loads the list of available PowerShell modules.
    /// Called on application startup.
    /// </summary>
    [RelayCommand]
    private async Task DiscoverModulesAsync()
    {
        try
        {
            IsLoading = true;
            ErrorMessage = null;
            StatusText = "Discovering installed modules...";

            var modules = await _discoveryService.GetAvailableModulesAsync();

            AvailableModules.Clear();
            foreach (var module in modules.OrderBy(m => m.Name))
            {
                AvailableModules.Add(module);
            }

            StatusText = $"Found {modules.Count} installed modules";
            _logger.LogInformation("Module discovery complete: {Count} modules", modules.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to discover modules");
            ErrorMessage = $"Failed to discover modules: {ex.Message}";
            StatusText = "Module discovery failed";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Loads the selected module and populates the cmdlet browser.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanLoadModule))]
    private async Task LoadModuleAsync()
    {
        if (SelectedModule == null) return;

        try
        {
            IsLoading = true;
            ErrorMessage = null;
            StatusText = $"Loading module '{SelectedModule.Name}'...";
            _helpTextService.ClearCache();

            _loadedModuleInfo = await _discoveryService.LoadModuleWithCmdletsAsync(SelectedModule.Name);
            _allCmdlets = _loadedModuleInfo.Cmdlets;

            RequiresConnection = _loadedModuleInfo.HasConnectCmdlet;
            IsConnected = _sessionManager.IsConnected;
            IsModuleLoaded = true;

            ApplyFilter();
            BuildGroupedCmdlets();

            // Populate module info for the description panel
            ModuleDescription = _loadedModuleInfo.Description;
            ModuleAuthor = _loadedModuleInfo.Author;
            ModuleVersion = _loadedModuleInfo.Version;

            StatusText = $"Loaded '{SelectedModule.Name}' — {_allCmdlets.Count} cmdlets";
            _logger.LogInformation("Module '{ModuleName}' loaded with {CmdletCount} cmdlets",
                SelectedModule.Name, _allCmdlets.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load module '{ModuleName}'", SelectedModule?.Name);
            ErrorMessage = $"Failed to load module: {ex.Message}";
            StatusText = "Module load failed";
            IsModuleLoaded = false;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private bool CanLoadModule() => SelectedModule != null;

    /// <summary>
    /// Connects to a session-based module by invoking its Connect-* cmdlet.
    /// </summary>
    [RelayCommand]
    private async Task ConnectAsync()
    {
        if (_loadedModuleInfo?.ConnectCmdletName == null) return;

        try
        {
            IsLoading = true;
            ErrorMessage = null;
            StatusText = $"Connecting via {_loadedModuleInfo.ConnectCmdletName}...";

            await _sessionManager.ConnectAsync(_loadedModuleInfo.ConnectCmdletName);
            IsConnected = true;
            StatusText = "Connected";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Connection failed");
            ErrorMessage = $"Connection failed: {ex.Message}";
            StatusText = "Connection failed";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Disconnects from a session-based module.
    /// </summary>
    [RelayCommand]
    private async Task DisconnectAsync()
    {
        if (_loadedModuleInfo?.DisconnectCmdletName == null) return;

        try
        {
            await _sessionManager.DisconnectAsync(_loadedModuleInfo.DisconnectCmdletName);
            IsConnected = false;
            StatusText = "Disconnected";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Disconnection failed");
            ErrorMessage = $"Disconnection failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Refreshes the module list from the system.
    /// </summary>
    [RelayCommand]
    private async Task RefreshModulesAsync()
    {
        try
        {
            IsLoading = true;
            StatusText = "Refreshing module list...";

            var modules = await _discoveryService.RefreshModulesAsync();
            AvailableModules.Clear();
            foreach (var module in modules.OrderBy(m => m.Name))
            {
                AvailableModules.Add(module);
            }

            StatusText = $"Refreshed: {modules.Count} modules";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Refresh failed: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Auto-loads a module by name (used when a module name is passed via CLI argument).
    /// Finds the module in the AvailableModules list (case-insensitive), selects it,
    /// and triggers loading. If the module is not found in the installed list, an error
    /// is shown to the user.
    /// </summary>
    /// <param name="moduleName">Module name to auto-load (e.g., "Microsoft.PowerShell.Management").</param>
    public async Task AutoLoadModuleAsync(string moduleName)
    {
        _logger.LogInformation("Auto-loading module '{ModuleName}' from CLI argument", moduleName);

        // Find the module in the discovered list (case-insensitive)
        var module = AvailableModules.FirstOrDefault(m =>
            m.Name.Equals(moduleName, StringComparison.OrdinalIgnoreCase));

        if (module == null)
        {
            ErrorMessage = $"Module '{moduleName}' was not found among installed modules. " +
                           "Verify the module is installed with: Get-Module -ListAvailable";
            _logger.LogWarning("Auto-load failed: module '{ModuleName}' not found", moduleName);
            return;
        }

        SelectedModule = module;
        await LoadModuleCommand.ExecuteAsync(null);
    }

    /// <summary>
    /// Filters the cmdlet list based on SearchText, matching against
    /// cmdlet name and synopsis (case-insensitive).
    /// </summary>
    private void ApplyFilter()
    {
        FilteredCmdlets.Clear();

        var filtered = string.IsNullOrWhiteSpace(SearchText)
            ? _allCmdlets
            : _allCmdlets.Where(c =>
                c.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                c.Synopsis.Contains(SearchText, StringComparison.OrdinalIgnoreCase));

        foreach (var cmdlet in filtered.OrderBy(c => c.Name))
        {
            FilteredCmdlets.Add(cmdlet);
        }
    }

    /// <summary>
    /// Builds grouped cmdlet collections organized by Noun for tree view display.
    /// </summary>
    private void BuildGroupedCmdlets()
    {
        GroupedCmdlets.Clear();

        var groups = _allCmdlets
            .GroupBy(c => c.Noun)
            .OrderBy(g => g.Key);

        foreach (var group in groups)
        {
            GroupedCmdlets.Add(new CmdletGroupViewModel
            {
                Noun = group.Key,
                Cmdlets = new ObservableCollection<CmdletInfo>(group.OrderBy(c => c.Verb))
            });
        }
    }
}

/// <summary>
/// Groups cmdlets by their Noun for tree view display.
/// Each group represents a resource type (e.g., "Process", "Service").
/// </summary>
public sealed class CmdletGroupViewModel
{
    /// <summary>The noun shared by all cmdlets in this group.</summary>
    public string Noun { get; init; } = string.Empty;

    /// <summary>Cmdlets in this group, ordered by verb.</summary>
    public ObservableCollection<CmdletInfo> Cmdlets { get; init; } = new();
}
