using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FixedRatioStressTest.Common.Models;
using FixedRatioStressTest.Core.Interfaces;
using FixedRatioStressTest.Core.Services;
using FixedRatioStressTest.Core;
using FixedRatioStressTest.Infrastructure.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using FixedRatioStressTest.Logging.Gui;
using Microsoft.Extensions.DependencyInjection;

namespace FixedRatioStressTest.Hosting.Gui;

public sealed class InProcessApiHost
{
	private readonly IConfiguration _configuration;
	private readonly ILoggerFactory _loggerFactory;
	private readonly GuiLoggerProvider _guiLoggerProvider;
	private readonly ISolanaClientService _solanaClientService;
	private readonly ISystemStateService _systemStateService;
	private readonly IThreadManager _threadManager;
	private readonly IStorageService _storageService;

	private WebApplication? _app;
	private Task? _runTask;
	private CancellationTokenSource? _cts;

	public InProcessApiHost(
		IConfiguration configuration,
		ILoggerFactory loggerFactory,
		GuiLoggerProvider guiLoggerProvider,
		ISolanaClientService solanaClientService,
		ISystemStateService systemStateService,
		IThreadManager threadManager,
		IStorageService storageService)
	{
		_configuration = configuration;
		_loggerFactory = loggerFactory;
		_guiLoggerProvider = guiLoggerProvider;
		_solanaClientService = solanaClientService;
		_systemStateService = systemStateService;
		_threadManager = threadManager;
		_storageService = storageService;
	}

	public async Task StartAsync(CancellationToken cancellationToken = default)
	{
		if (_app != null)
		{
			return; // already started
		}

		var logger = _loggerFactory.CreateLogger("GuiInProcApi");
		var builder = WebApplication.CreateBuilder(Array.Empty<string>());

		builder.Logging.ClearProviders();
		builder.Logging.AddConfiguration(_configuration.GetSection("Logging"));
		builder.Logging.AddProvider(_guiLoggerProvider);

		// TEMP (dev): Force storage to use repo root .\\data regardless of working dir
		// NOTE: This override will be removed for production.
		try
		{
			var rootData = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../data"));
			if (!Directory.Exists(rootData))
			{
				rootData = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "data"));
			}
			builder.Configuration.AddInMemoryCollection(new Dictionary<string, string>
			{
				["Storage:DataDirectory"] = rootData
			});
		}
		catch { }

		var httpPort = _configuration.GetValue<int>("NetworkConfiguration:HttpPort", 8080);
		builder.WebHost.ConfigureKestrel(options =>
		{
			options.ListenAnyIP(httpPort);
		});


		builder.Services.AddSingleton<ISolanaClientService>(_solanaClientService);
		// Register required services for controllers that depend on threading and storage
		builder.Services.AddSingleton<IStorageService>(_ => _storageService); // Use shared instance from main container
		builder.Services.AddSingleton<IComputeUnitManager, ComputeUnitManager>();
		builder.Services.AddSingleton<IContractVersionService, RawRpcContractVersionService>();
		builder.Services.AddSingleton<ITransactionBuilderService, TransactionBuilderService>();
		builder.Services.AddSingleton<IContractErrorHandler, ContractErrorHandler>();
		builder.Services.AddSingleton<ISystemStateService>(_ => _systemStateService); // Use shared instance from main container
		builder.Services.AddSingleton<IThreadManager>(_ => _threadManager); // Use shared instance from main container
		builder.Services.AddSingleton<IEmptyCommandHandler, EmptyCommandHandler>();
		builder.Services.AddRouting();
		builder.Services.AddControllers().AddApplicationPart(Assembly.Load("FixedRatioStressTest.Web"));

		var app = builder.Build();
		app.MapControllers();

		_app = app;
		_runTask = app.RunAsync();
		await Task.CompletedTask;
	}

	public async Task StopAsync(CancellationToken cancellationToken = default)
	{
		if (_app == null)
		{
			return;
		}

		try
		{
			await _app.StopAsync(cancellationToken);
			if (_runTask != null)
			{
				await _runTask; // observe completion
			}
		}
		finally
		{
			_cts?.Dispose();
			_cts = null;
			_runTask = null;
			_app = null;
		}
	}

	// Minimal API helpers removed; GUI host now relies on shared controllers only.
}


