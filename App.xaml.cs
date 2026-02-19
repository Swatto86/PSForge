using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PSForge.Core;
using PSForge.Logging;
using PSForge.Services;
using PSForge.ViewModels;
using Serilog;

namespace PSForge;

/// <summary>
/// Application entry point and dependency injection container setup.
/// Configures all services, core components, and ViewModels using
/// Microsoft.Extensions.DependencyInjection.
///
/// Lifecycle:
///   Startup  → builds DI container, initializes PowerShell runspaces
///   Exit     → disposes session manager (closes runspaces cleanly)
/// </summary>
public partial class App : Application
{
    private ServiceProvider? _serviceProvider;

    /// <summary>
    /// Static accessor for the DI service provider.
    /// Used by MainWindow to resolve the MainViewModel.
    /// </summary>
    public static IServiceProvider? Services { get; private set; }

    /// <summary>
    /// Module name passed via command-line argument, if any.
    /// Usage: PSForge.exe ModuleName   (e.g., PSForge.exe Microsoft.PowerShell.Management)
    /// When set, the app auto-discovers modules and immediately loads this one on startup.
    /// </summary>
    public static string? StartupModuleName { get; private set; }

    /// <summary>
    /// Configures the DI container and initializes the PowerShell session manager.
    /// Called before any windows are created.
    /// </summary>
    private void Application_Startup(object sender, StartupEventArgs e)
    {
        // Initialize file logging FIRST so it's available throughout startup
        FileLoggerConfiguration.Initialize();

        var services = new ServiceCollection();
        ConfigureServices(services);

        _serviceProvider = services.BuildServiceProvider();
        Services = _serviceProvider;

        var logger = _serviceProvider.GetRequiredService<ILogger<App>>();
        logger.LogInformation("=== PSForge Application Starting ===");
        logger.LogInformation("Version: {Version}", GetType().Assembly.GetName().Version);
        logger.LogInformation("Log Directory: {LogDir}", FileLoggerConfiguration.GetLogDirectory());

        // Capture command-line arguments: first arg is treated as a module name to auto-load.
        // Usage: PSForge.exe <ModuleName>
        if (e.Args.Length > 0 && !string.IsNullOrWhiteSpace(e.Args[0]))
        {
            StartupModuleName = e.Args[0].Trim();
        }

        // Initialize PowerShell runspaces eagerly so they're ready when the UI loads.
        // This runs on the UI thread during startup (fast operation, <100ms typically).
        var sessionManager = _serviceProvider.GetRequiredService<PowerShellSessionManager>();
        sessionManager.Initialize();

        // Set up global exception handling to prevent unhandled crashes (Rule 11)
        SetupExceptionHandling();
    }

    /// <summary>
    /// Disposes the DI container and all IDisposable services (including runspaces).
    /// </summary>
    private void Application_Exit(object sender, ExitEventArgs e)
    {
        var logger = _serviceProvider?.GetService<ILogger<App>>();
        logger?.LogInformation("=== PSForge Application Exiting (Code: {ExitCode}) ===", e.ApplicationExitCode);
        
        _serviceProvider?.Dispose();
        Log.CloseAndFlush();
    }

    /// <summary>
    /// Registers all services in the DI container.
    /// 
    /// Lifetime choices:
    ///   - Singleton: PowerShellSessionManager (one set of runspaces for the app lifetime)
    ///   - Singleton: ModuleIntrospector, CommandExecutor (stateless, share the session manager)
    ///   - Singleton: Services (caching services need persistent state)
    ///   - Transient: ViewModels (created fresh when needed)
    /// </summary>
    private static void ConfigureServices(IServiceCollection services)
    {
        // Logging - use Serilog with file output to %APPDATA%\PSForge\logs
        services.AddLogging(builder => builder.AddFileLogging());

        // Core (singleton — one session manager with persistent runspaces)
        services.AddSingleton<PowerShellSessionManager>();
        services.AddSingleton<ModuleIntrospector>();
        services.AddSingleton<CommandExecutor>();

        // Services (singleton — caching services maintain state across operations)
        services.AddSingleton<ModuleDiscoveryService>();
        services.AddSingleton<HelpTextService>();
        services.AddSingleton<OutputFormatterService>();

        // ViewModels (transient — created fresh for each resolution)
        services.AddTransient<MainViewModel>();
    }

    /// <summary>
    /// Registers global exception handlers to prevent unhandled crashes.
    /// Surfaces errors as user-friendly messages rather than letting the app terminate.
    /// </summary>
    private bool _isHandlingError;

    private void SetupExceptionHandling()
    {
        // Catch unhandled exceptions on the UI thread.
        // Re-entrance guard prevents cascading MessageBox dialogs that lead to stack overflow:
        // MessageBox.Show pumps WPF messages → triggers more XAML processing → more exceptions → more MessageBoxes.
        DispatcherUnhandledException += (_, args) =>
        {
            args.Handled = true; // Prevent app termination

            if (_isHandlingError) return; // Prevent re-entrant cascades
            _isHandlingError = true;

            try
            {
                // Unwrap TargetInvocationException to show the real error
                var ex = args.Exception;
                while (ex.InnerException != null)
                {
                    ex = ex.InnerException;
                }

                MessageBox.Show(
                    $"An unexpected error occurred:\n\n{ex.GetType().Name}: {ex.Message}\n\nStack trace:\n{ex.StackTrace}",
                    "PSForge — Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                _isHandlingError = false;
            }
        };

        // Catch unhandled exceptions on background threads
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
            {
                // Can't show UI from non-UI thread; use Dispatcher
                Dispatcher.Invoke(() =>
                {
                    MessageBox.Show(
                        $"A background error occurred:\n\n{ex.Message}",
                        "PSForge — Background Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                });
            }
        };

        // Catch unobserved Task exceptions
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            args.SetObserved(); // Prevent process termination
        };
    }
}
