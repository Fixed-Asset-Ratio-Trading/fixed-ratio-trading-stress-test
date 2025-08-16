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

        // All logging uses ILogger<T> with GUI providers; no legacy adapters

        // Service lifecycle engine
        services.AddSingleton<IServiceLifecycle, StressTestEngine>(sp => new StressTestEngine(
            sp.GetRequiredService<IThreadManager>(),
            sp.GetRequiredService<ISolanaClientService>(),
            sp.GetRequiredService<IContractVersionService>(),
            sp.GetRequiredService<IStorageService>(),
            sp.GetRequiredService<IComputeUnitManager>(),
            sp.GetRequiredService<ILogger<StressTestEngine>>(),
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

// No legacy adapters; GUI subscribes to GuiLoggerProvider events