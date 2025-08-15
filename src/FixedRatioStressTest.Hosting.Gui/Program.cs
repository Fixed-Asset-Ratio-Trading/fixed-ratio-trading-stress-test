using System.Windows.Forms;
using FixedRatioStressTest.Abstractions;
using FixedRatioStressTest.Core;
using FixedRatioStressTest.Core.Interfaces;
using FixedRatioStressTest.Core.Services;
using FixedRatioStressTest.Hosting.Gui;
using FixedRatioStressTest.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

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
            .AddJsonFile("appsettings.json", optional: true)
            .Build();

        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();

        // Core dependencies
        services.AddSingleton<IStorageService, JsonFileStorageService>();
        services.AddSingleton<IComputeUnitManager, ComputeUnitManager>();
        services.AddSingleton<IContractVersionService, RawRpcContractVersionService>();
        services.AddSingleton<ISolanaClientService, SolanaClientService>();
        services.AddSingleton<ITransactionBuilderService, TransactionBuilderService>();
        services.AddSingleton<IThreadManager, FixedRatioStressTest.Core.Services.ThreadManager>();

        // GUI logger and engine
        services.AddSingleton<GuiEventLogger>();
        services.AddSingleton<IEventLogger>(sp => sp.GetRequiredService<GuiEventLogger>());
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
            sp.GetRequiredService<GuiEventLogger>()));

        using var provider = services.BuildServiceProvider();
        var host = provider.GetRequiredService<GuiServiceHost>();

        // Initialize subscriptions and run the form on the STA thread
        host.InitializeAsync().GetAwaiter().GetResult();
        Application.Run(host);
    }
}


