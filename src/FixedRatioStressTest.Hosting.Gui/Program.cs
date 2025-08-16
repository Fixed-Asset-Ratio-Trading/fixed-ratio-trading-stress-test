using System.Windows.Forms;
using FixedRatioStressTest.Abstractions;
using FixedRatioStressTest.Core;
using FixedRatioStressTest.Core.Interfaces;
using FixedRatioStressTest.Core.Services;
using FixedRatioStressTest.Hosting.Gui;
using FixedRatioStressTest.Infrastructure.Services;
using FixedRatioStressTest.Logging;
using FixedRatioStressTest.Logging.Gui;
using FixedRatioStressTest.Logging.Transport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        // Minimal DI container for GUI host
        var services = new ServiceCollection();

        // Configuration (GUI-specific appsettings.json is supported if present)
        var configuration = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
            .Build();

        services.AddSingleton<IConfiguration>(configuration);

        // Configure logging with new GUI logger provider
        services.AddLogging(builder =>
        {
            builder.AddConfiguration(configuration.GetSection("Logging"));
            
            // Add GUI logger provider
            builder.AddGuiLogger(configuration.GetSection("GuiLogging"));

            // Optionally add console logger for debugging
            if (configuration.GetValue<bool>("EnableConsoleLogging", false))
            {
                builder.AddConsole();
            }
        });

        // Add UDP log listener service to receive logs from API
        services.AddUdpLogListener(configuration.GetSection("UdpListener"));

        // Core dependencies
        services.AddSingleton<IStorageService, JsonFileStorageService>();
        services.AddSingleton<IComputeUnitManager, ComputeUnitManager>();
        services.AddSingleton<IContractVersionService, RawRpcContractVersionService>();
        services.AddSingleton<ISolanaClientService, SolanaClientService>();
        services.AddSingleton<ITransactionBuilderService, TransactionBuilderService>();
        services.AddSingleton<IThreadManager, FixedRatioStressTest.Core.Services.ThreadManager>();

        // For backward compatibility during migration, create IEventLogger adapter
        services.AddSingleton<IEventLogger, LoggerEventLoggerAdapter>();

        // Service lifecycle engine
        services.AddSingleton<IServiceLifecycle, StressTestEngine>(sp => new StressTestEngine(
            sp.GetRequiredService<IThreadManager>(),
            sp.GetRequiredService<ISolanaClientService>(),
            sp.GetRequiredService<IContractVersionService>(),
            sp.GetRequiredService<IStorageService>(),
            sp.GetRequiredService<IComputeUnitManager>(),
            sp.GetRequiredService<IEventLogger>(),
            sp.GetRequiredService<IConfiguration>()));

        // GUI host
        services.AddSingleton<GuiServiceHost>(sp => new GuiServiceHost(
            sp.GetRequiredService<IServiceLifecycle>(),
            sp.GetRequiredService<GuiLoggerProvider>(),
            sp.GetService<UdpLogListenerService>(),
            sp.GetRequiredService<IConfiguration>()));

        using var provider = services.BuildServiceProvider();
        var host = provider.GetRequiredService<GuiServiceHost>();

        // Initialize subscriptions and run the form on the STA thread
        host.InitializeAsync().GetAwaiter().GetResult();
        Application.Run(host);
    }
}

// Temporary adapter to bridge ILogger<T> to IEventLogger during migration
internal class LoggerEventLoggerAdapter : IEventLogger
{
    private readonly ILogger<LoggerEventLoggerAdapter> _logger;
    public event EventHandler<LogEventArgs>? LogEntryCreated;

    public LoggerEventLoggerAdapter(ILogger<LoggerEventLoggerAdapter> logger)
    {
        _logger = logger;
    }

    public void LogInformation(string message, params object[] args) =>
        _logger.LogInformation(message, args);

    public void LogWarning(string message, params object[] args) =>
        _logger.LogWarning(message, args);

    public void LogError(string message, Exception? exception, params object[] args) =>
        _logger.LogError(exception, message, args);

    public void LogCritical(string message, Exception? exception, params object[] args) =>
        _logger.LogCritical(exception, message, args);

    public void LogDebug(string message, params object[] args) =>
        _logger.LogDebug(message, args);
}