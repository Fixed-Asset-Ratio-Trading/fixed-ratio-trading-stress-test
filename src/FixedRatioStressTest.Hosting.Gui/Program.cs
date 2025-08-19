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
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using FixedRatioStressTest.Common.Models;
using Microsoft.AspNetCore.Hosting;
using System.IO;
using System.Linq;
using System.Text.Json;

internal static class Program
{
	[STAThread]
	private static void Main(string[] args)
	{
		Application.EnableVisualStyles();
		Application.SetCompatibleTextRenderingDefault(false);

		// Hard-coded repo root for GUI testing per user request
		const string RepoRoot = @"C:\\Users\\Davinci\\code\\fixed-ratio-trading-stress-test";
		var dataDir = Path.Combine(RepoRoot, "data");
		var logsDir = Path.Combine(RepoRoot, "logs");
		Directory.CreateDirectory(dataDir);
		Directory.CreateDirectory(logsDir);

		// Minimal DI container for GUI host
		var services = new ServiceCollection();

		// Configuration (GUI-specific appsettings.json is supported if present) + enforced overrides
		var configuration = new ConfigurationBuilder()
			.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
			.AddInMemoryCollection(new Dictionary<string, string?>
			{
				// Force all GUI data into repo-root data folder
				["Storage:DataDirectory"] = dataDir,
				// Force GUI logs into repo-root logs folder
				["GuiLogging:EnableFileLogging"] = "true",
				["GuiLogging:FileLogging:Path"] = Path.Combine(logsDir, "gui.log"),
			})
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

		// Add UDP log listener service to receive logs
		services.AddUdpLogListener(configuration.GetSection("UdpListener"));

		// Core dependencies
		services.AddSingleton<IStorageService, JsonFileStorageService>();
		services.AddSingleton<IComputeUnitManager, ComputeUnitManager>();
		services.AddSingleton<IContractVersionService, RawRpcContractVersionService>();
		services.AddSingleton<ISolanaClientService, SolanaClientService>();
		services.AddSingleton<ITransactionBuilderService, TransactionBuilderService>();
		services.AddSingleton<IThreadManager, FixedRatioStressTest.Core.Services.ThreadManager>();

		// Service lifecycle engine
		services.AddSingleton<IServiceLifecycle, StressTestEngine>(sp => new StressTestEngine(
			sp.GetRequiredService<IThreadManager>(),
			sp.GetRequiredService<ISolanaClientService>(),
			sp.GetRequiredService<IContractVersionService>(),
			sp.GetRequiredService<IStorageService>(),
			sp.GetRequiredService<IComputeUnitManager>(),
			sp.GetRequiredService<ILogger<StressTestEngine>>(),
			sp.GetRequiredService<IConfiguration>()));

		// In-process API host bound to GUI lifecycle
		services.AddSingleton<InProcessApiHost>(sp => new InProcessApiHost(
			sp.GetRequiredService<IConfiguration>(),
			sp.GetRequiredService<ILoggerFactory>(),
			sp.GetRequiredService<GuiLoggerProvider>(),
			sp.GetRequiredService<ISolanaClientService>()));

		// Parse CLI args
		var autoStart = args.Any(a => string.Equals(a, "--start", StringComparison.OrdinalIgnoreCase));

		// GUI host
		services.AddSingleton<GuiServiceHost>(sp => new GuiServiceHost(
			sp.GetRequiredService<IServiceLifecycle>(),
			sp.GetRequiredService<GuiLoggerProvider>(),
			sp.GetService<UdpLogListenerService>(),
			sp.GetRequiredService<IConfiguration>(),
			sp.GetRequiredService<InProcessApiHost>(),
			autoStart));

		using var provider = services.BuildServiceProvider();
		var host = provider.GetRequiredService<GuiServiceHost>();

		// In-process API now starts/stops with Start/Stop actions via InProcessApiHost

		// Initialize subscriptions and run the form on the STA thread
		host.InitializeAsync().GetAwaiter().GetResult();
		Application.Run(host);
	}

    // In-proc API bootstrap removed; managed by InProcessApiHost
}

// No legacy adapters; GUI subscribes to GuiLoggerProvider events