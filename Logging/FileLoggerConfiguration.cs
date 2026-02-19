using System.IO;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;

namespace PSForge.Logging;

/// <summary>
/// Configures file-based logging to %APPDATA%\PSForge\logs.
/// Provides structured, timestamped logs that persist for troubleshooting.
/// </summary>
public static class FileLoggerConfiguration
{
    /// <summary>
    /// Configures Serilog to write structured logs to %APPDATA%\PSForge\logs.
    /// Log files are rotated daily and kept for 14 days.
    /// Debug mode (PSFORGE_DEBUG=1) enables verbose trace-level logging.
    /// </summary>
    public static void Initialize()
    {
        var logDir = GetLogDirectory();
        Directory.CreateDirectory(logDir);

        var logFilePath = Path.Combine(logDir, "psforge-.log");

        // Determine log level from environment variable
        var debugMode = Environment.GetEnvironmentVariable("PSFORGE_DEBUG");
        var minLevel = !string.IsNullOrEmpty(debugMode) && debugMode != "0"
            ? LogEventLevel.Verbose
            : LogEventLevel.Debug;

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Is(minLevel)
            .Enrich.FromLogContext()
            .Enrich.WithThreadId()
            .Enrich.WithProcessId()
            .WriteTo.File(
                logFilePath,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{SourceContext}] (Thread:{ThreadId}) {Message:lj}{NewLine}{Exception}",
                shared: false)
            .WriteTo.Debug() // Also write to debug output for development
            .CreateLogger();
    }

    /// <summary>
    /// Gets the log directory path: %APPDATA%\PSForge\logs
    /// </summary>
    public static string GetLogDirectory()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "PSForge", "logs");
    }

    /// <summary>
    /// Configures Microsoft.Extensions.Logging to use Serilog.
    /// </summary>
    public static ILoggingBuilder AddFileLogging(this ILoggingBuilder builder)
    {
        builder.ClearProviders();
        builder.AddSerilog(dispose: true);
        return builder;
    }
}
