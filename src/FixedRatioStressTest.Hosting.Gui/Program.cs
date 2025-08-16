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
	private static void Main()
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

		// GUI host
		services.AddSingleton<GuiServiceHost>(sp => new GuiServiceHost(
			sp.GetRequiredService<IServiceLifecycle>(),
			sp.GetRequiredService<GuiLoggerProvider>(),
			sp.GetService<UdpLogListenerService>(),
			sp.GetRequiredService<IConfiguration>()));

		using var provider = services.BuildServiceProvider();
		var host = provider.GetRequiredService<GuiServiceHost>();

		// Start a lightweight HTTP API for testing in-process
		StartInProcessApi(provider, bindAddress: "127.0.0.1", httpPort: configuration.GetValue<int>("NetworkConfiguration:HttpPort", 8080));

		// Initialize subscriptions and run the form on the STA thread
		host.InitializeAsync().GetAwaiter().GetResult();
		Application.Run(host);
	}

	private static void StartInProcessApi(ServiceProvider provider, string bindAddress, int httpPort)
	{
		var config = provider.GetRequiredService<IConfiguration>();
		var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("GuiInProcApi");

		var builder = WebApplication.CreateBuilder(new string[] { });

		// Reuse GUI logging configuration
		builder.Logging.ClearProviders();
		builder.Logging.AddConfiguration(config.GetSection("Logging"));
		builder.Logging.AddProvider(provider.GetRequiredService<GuiLoggerProvider>());

		// Configure Kestrel directly
		builder.WebHost.ConfigureKestrel(options =>
		{
			options.ListenAnyIP(httpPort);
		});

		builder.Services.AddSingleton(provider.GetRequiredService<ISolanaClientService>());
		builder.Services.AddRouting();

		var app = builder.Build();
		var solana = provider.GetRequiredService<ISolanaClientService>();

		var route = app.MapGroup("/api/pool");
		route.MapGet("/list", async (HttpContext ctx) =>
		{
			logger.LogInformation("RPC list_pools requested (in-proc)");
			var pools = await solana.GetAllPoolsAsync();
			return Results.Ok(new
			{
				result = new
				{
					pools = pools.Select(p => new
					{
						poolId = p.PoolId,
						tokenAMint = p.TokenAMint,
						tokenBMint = p.TokenBMint,
						tokenADecimals = p.TokenADecimals,
						tokenBDecimals = p.TokenBDecimals,
						ratioDisplay = p.RatioDisplay,
						isBlockchainPool = p.IsBlockchainPool,
						createdAt = p.CreatedAt
					}).ToList(),
					totalCount = pools.Count
				}
			});
		});

		route.MapPost("/create", async (HttpContext ctx) =>
		{
			var body = await new StreamReader(ctx.Request.Body).ReadToEndAsync();
			logger.LogInformation("RPC create_pool requested (in-proc): {Body}", body);
			var request = JsonSerializer.Deserialize<FixedRatioStressTest.Common.Models.JsonRpcRequest>(body);
			var parameters = ParsePoolCreationParams(request?.Params);
			var realPool = await solana.CreateRealPoolAsync(parameters);
			return Results.Ok(new
			{
				result = new
				{
					poolId = realPool.PoolId,
					tokenAMint = realPool.TokenAMint,
					tokenBMint = realPool.TokenBMint,
					tokenADecimals = realPool.TokenADecimals,
					tokenBDecimals = realPool.TokenBDecimals,
					ratioDisplay = realPool.RatioDisplay,
					creationSignature = realPool.CreationSignature,
					status = "created",
					isBlockchainPool = true
				}
			});
		});

		app.RunAsync();
	}

	private static PoolCreationParams ParsePoolCreationParams(object? parameters)
	{
		var result = new PoolCreationParams();
		if (parameters == null) return result;
		try
		{
			if (parameters is System.Text.Json.JsonElement element && element.ValueKind == System.Text.Json.JsonValueKind.Object)
			{
				if (element.TryGetProperty("token_a_decimals", out var tokenADecimalsElement))
				{
					result.TokenADecimals = tokenADecimalsElement.GetInt32();
				}
				if (element.TryGetProperty("token_b_decimals", out var tokenBDecimalsElement))
				{
					result.TokenBDecimals = tokenBDecimalsElement.GetInt32();
				}
				if (element.TryGetProperty("ratio_whole_number", out var ratioElement))
				{
					result.RatioWholeNumber = ratioElement.GetUInt64();
				}
				if (element.TryGetProperty("ratio_direction", out var directionElement))
				{
					result.RatioDirection = directionElement.GetString();
				}
			}
		}
		catch
		{
			// ignore and keep defaults
		}
		return result;
	}
}

// No legacy adapters; GUI subscribes to GuiLoggerProvider events