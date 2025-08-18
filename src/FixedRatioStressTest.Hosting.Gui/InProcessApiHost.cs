using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FixedRatioStressTest.Common.Models;
using FixedRatioStressTest.Core.Interfaces;
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

	private WebApplication? _app;
	private Task? _runTask;
	private CancellationTokenSource? _cts;

	public InProcessApiHost(
		IConfiguration configuration,
		ILoggerFactory loggerFactory,
		GuiLoggerProvider guiLoggerProvider,
		ISolanaClientService solanaClientService)
	{
		_configuration = configuration;
		_loggerFactory = loggerFactory;
		_guiLoggerProvider = guiLoggerProvider;
		_solanaClientService = solanaClientService;
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

		var httpPort = _configuration.GetValue<int>("NetworkConfiguration:HttpPort", 8080);
		builder.WebHost.ConfigureKestrel(options =>
		{
			options.ListenAnyIP(httpPort);
		});


		builder.Services.AddSingleton<ISolanaClientService>(_solanaClientService);
		builder.Services.AddRouting();

		var app = builder.Build();
		var solana = _solanaClientService;

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
			var request = JsonSerializer.Deserialize<JsonRpcRequest>(body);
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

		app.MapPost("/api/jsonrpc", async (HttpContext ctx) =>
		{
			var body = await new StreamReader(ctx.Request.Body).ReadToEndAsync();
			var request = JsonSerializer.Deserialize<JsonRpcRequest>(body);
			if (request == null || string.IsNullOrWhiteSpace(request.Method))
			{
				return Results.BadRequest(new JsonRpcResponse<object>
				{
					Error = new JsonRpcError { Code = -32600, Message = "Invalid Request" },
					Id = request?.Id
				});
			}

			try
			{
				switch (request.Method)
				{
					case "core_wallet_status":
					{
						logger.LogInformation("RPC core_wallet_status requested (in-proc)");
						var wallet = await solana.GetOrCreateCoreWalletAsync();
						var result = new
						{
							publicKey = wallet.PublicKey,
							currentSolBalanceLamports = wallet.CurrentSolBalance,
							currentSolBalance = wallet.CurrentSolBalance / 1_000_000_000.0,
							minimumSolBalanceLamports = wallet.MinimumSolBalance,
							minimumSolBalance = wallet.MinimumSolBalance / 1_000_000_000.0,
							createdAt = wallet.CreatedAt,
							lastBalanceCheck = wallet.LastBalanceCheck
						};
						return Results.Ok(new JsonRpcResponse<object>
						{
							Result = result,
							Id = request.Id
						});
					}
					case "airdrop_sol":
					{
						logger.LogInformation("RPC airdrop_sol requested (in-proc)");
						var wallet = await solana.GetOrCreateCoreWalletAsync();

						ulong lamports = 1_000_000_000; // Default 1 SOL
						if (request.Params is JsonElement element && element.ValueKind == JsonValueKind.Object)
						{
							if (element.TryGetProperty("lamports", out var lamportsElement))
							{
								lamports = lamportsElement.GetUInt64();
							}
							else if (element.TryGetProperty("sol_amount", out var solElement))
							{
								lamports = (ulong)(solElement.GetDouble() * 1_000_000_000);
							}
						}

						var signature = await solana.RequestAirdropAsync(wallet.PublicKey, lamports);
						var result = new
						{
							walletAddress = wallet.PublicKey,
							lamports = lamports,
							solAmount = lamports / 1_000_000_000.0,
							signature = signature,
							status = "success"
						};
						return Results.Ok(new JsonRpcResponse<object>
						{
							Result = result,
							Id = request.Id
						});
					}
					case "create_pool_random":
					{
						logger.LogInformation("RPC create_pool_random requested (in-proc)");
						var random = new Random();
						var parameters = new PoolCreationParams
						{
							TokenADecimals = random.Next(6, 10),
							TokenBDecimals = random.Next(6, 10),
							RatioWholeNumber = (ulong)random.Next(100, 10000),
							RatioDirection = random.Next(2) == 0 ? "a_to_b" : "b_to_a"
						};

						var realPool = await solana.CreateRealPoolAsync(parameters);
						var result = new
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
						};
						return Results.Ok(new JsonRpcResponse<object>
						{
							Result = result,
							Id = request.Id
						});
					}
					case "list_pools":
					{
						logger.LogInformation("RPC list_pools requested (in-proc JSON-RPC)");
						var pools = await solana.GetAllPoolsAsync();
						var result = new
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
						};
						return Results.Ok(new JsonRpcResponse<object>
						{
							Result = result,
							Id = request.Id
						});
					}
					default:
						return Results.BadRequest(new JsonRpcResponse<object>
						{
							Error = new JsonRpcError
							{
								Code = -32601,
								Message = $"Method {request.Method} not found"
							},
							Id = request.Id
						});
				}
			}
			catch (Exception ex)
			{
				var errLogger = _loggerFactory.CreateLogger("GuiInProcApi");
				errLogger.LogError(ex, "Error handling JSON-RPC request {Method}", request.Method);
				return Results.Ok(new JsonRpcResponse<object>
				{
					Error = new JsonRpcError
					{
						Code = -32603,
						Message = "Internal error"
					},
					Id = request.Id
				});
			}
		});

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

	private static PoolCreationParams ParsePoolCreationParams(object? parameters)
	{
		var result = new PoolCreationParams();
		if (parameters == null) return result;
		try
		{
			if (parameters is JsonElement element && element.ValueKind == JsonValueKind.Object)
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


