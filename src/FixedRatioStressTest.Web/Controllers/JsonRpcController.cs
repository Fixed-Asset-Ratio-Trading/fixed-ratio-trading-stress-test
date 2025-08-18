using FixedRatioStressTest.Common.Models;
using FixedRatioStressTest.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace FixedRatioStressTest.Web.Controllers;

[ApiController]
[Route("api/jsonrpc")]
public class JsonRpcController : ControllerBase
{
    private readonly ISolanaClientService _solanaClient;
    private readonly ILogger<JsonRpcController> _logger;

    public JsonRpcController(ISolanaClientService solanaClient, ILogger<JsonRpcController> logger)
    {
        _solanaClient = solanaClient;
        _logger = logger;
    }

    [HttpPost]
    public async Task<ActionResult<object>> Post([FromBody] JsonRpcRequest request)
    {
        try
        {
            _logger.LogInformation("JSON-RPC request: {Method}", request.Method);
            return request.Method switch
            {
                "create_pool" => await CreatePool(request),
                "create_pool_random" => await CreatePoolRandom(request),
                "list_pools" => await ListPools(request),
                "get_pool" => await GetPoolById(request),
                "core_wallet_status" => await GetCoreWalletStatus(request),
                "airdrop_sol" => await AirdropSol(request),
                "stop_service" => await StopService(request),
                _ => BadRequest(new JsonRpcResponse<object>
                {
                    Error = new JsonRpcError
                    {
                        Code = -32601,
                        Message = $"Method {request.Method} not found"
                    },
                    Id = request.Id
                })
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RPC endpoint error");
            return Ok(new JsonRpcResponse<object>
            {
                Error = new JsonRpcError
                {
                    Code = -32603,
                    Message = "Internal error"
                },
                Id = request.Id
            });
        }
    }

    private async Task<ActionResult<object>> CreatePool(JsonRpcRequest request)
    {
        _logger.LogInformation("RPC create_pool requested");
        var parameters = ParsePoolCreationParams(request.Params);
        var realPool = await _solanaClient.CreateRealPoolAsync(parameters);
        var result = new PoolCreationResult
        {
            PoolId = realPool.PoolId,
            TokenAMint = realPool.TokenAMint,
            TokenBMint = realPool.TokenBMint,
            TokenADecimals = realPool.TokenADecimals,
            TokenBDecimals = realPool.TokenBDecimals,
            RatioDisplay = realPool.RatioDisplay,
            CreationSignature = realPool.CreationSignature,
            Status = "created",
            IsBlockchainPool = true
        };
        return Ok(new JsonRpcResponse<PoolCreationResult> { Result = result, Id = request.Id });
    }

    private async Task<ActionResult<object>> CreatePoolRandom(JsonRpcRequest request)
    {
        _logger.LogInformation("RPC create_pool_random requested");
        var random = new Random();
        var parameters = new PoolCreationParams
        {
            TokenADecimals = random.Next(6, 10),
            TokenBDecimals = random.Next(6, 10),
            RatioWholeNumber = (ulong)random.Next(100, 10000),
            RatioDirection = random.Next(2) == 0 ? "a_to_b" : "b_to_a"
        };
        var realPool = await _solanaClient.CreateRealPoolAsync(parameters);
        var result = new PoolCreationResult
        {
            PoolId = realPool.PoolId,
            TokenAMint = realPool.TokenAMint,
            TokenBMint = realPool.TokenBMint,
            TokenADecimals = realPool.TokenADecimals,
            TokenBDecimals = realPool.TokenBDecimals,
            RatioDisplay = realPool.RatioDisplay,
            CreationSignature = realPool.CreationSignature,
            Status = "created",
            IsBlockchainPool = true
        };
        return Ok(new JsonRpcResponse<PoolCreationResult> { Result = result, Id = request.Id });
    }

    private async Task<ActionResult<object>> ListPools(JsonRpcRequest request)
    {
        _logger.LogInformation("RPC list_pools requested (JSON-RPC)");
        var pools = await _solanaClient.GetAllPoolsAsync();
        var result = new PoolListResult
        {
            Pools = pools.Select(p => new PoolSummary
            {
                PoolId = p.PoolId,
                TokenAMint = p.TokenAMint,
                TokenBMint = p.TokenBMint,
                TokenADecimals = p.TokenADecimals,
                TokenBDecimals = p.TokenBDecimals,
                RatioDisplay = p.RatioDisplay,
                IsBlockchainPool = p.IsBlockchainPool,
                CreatedAt = p.CreatedAt
            }).ToList(),
            TotalCount = pools.Count
        };
        return Ok(new JsonRpcResponse<PoolListResult> { Result = result, Id = request.Id });
    }

    private async Task<ActionResult<object>> GetPoolById(JsonRpcRequest request)
    {
        var poolId = ExtractPoolIdFromParams(request.Params);
        if (string.IsNullOrEmpty(poolId))
        {
            return Ok(new JsonRpcResponse<PoolState>
            {
                Error = new JsonRpcError { Code = -32602, Message = "Pool ID is required" },
                Id = request.Id
            });
        }

        try
        {
            var pool = await _solanaClient.GetPoolStateAsync(poolId);
            return Ok(new JsonRpcResponse<PoolState> { Result = pool, Id = request.Id });
        }
        catch (KeyNotFoundException)
        {
            return Ok(new JsonRpcResponse<PoolState>
            {
                Error = new JsonRpcError { Code = -32602, Message = $"Pool {poolId} not found" },
                Id = request.Id
            });
        }
    }

    private async Task<ActionResult<object>> GetCoreWalletStatus(JsonRpcRequest request)
    {
        _logger.LogInformation("RPC core_wallet_status requested");
        var wallet = await _solanaClient.GetOrCreateCoreWalletAsync();
        var result = new CoreWalletStatusResult
        {
            PublicKey = wallet.PublicKey,
            CurrentSolBalanceLamports = wallet.CurrentSolBalance,
            CurrentSolBalance = wallet.CurrentSolBalance / 1_000_000_000.0,
            MinimumSolBalanceLamports = wallet.MinimumSolBalance,
            MinimumSolBalance = wallet.MinimumSolBalance / 1_000_000_000.0,
            CreatedAt = wallet.CreatedAt,
            LastBalanceCheck = wallet.LastBalanceCheck
        };
        return Ok(new JsonRpcResponse<CoreWalletStatusResult> { Result = result, Id = request.Id });
    }

    private async Task<ActionResult<object>> AirdropSol(JsonRpcRequest request)
    {
        _logger.LogInformation("RPC airdrop_sol requested");
        var parameters = ParseAirdropParams(request.Params);
        var wallet = await _solanaClient.GetOrCreateCoreWalletAsync();
        var signature = await _solanaClient.RequestAirdropAsync(wallet.PublicKey, parameters.Lamports);
        var result = new AirdropResult
        {
            WalletAddress = wallet.PublicKey,
            Lamports = parameters.Lamports,
            SolAmount = parameters.Lamports / 1_000_000_000.0,
            Signature = signature,
            Status = "success"
        };
        return Ok(new JsonRpcResponse<AirdropResult> { Result = result, Id = request.Id });
    }

    private async Task<ActionResult<object>> StopService(JsonRpcRequest request)
    {
        _logger.LogInformation("RPC stop_service requested");
        var result = new StopServiceResult
        {
            Message = "Stop service request acknowledged",
            Status = "acknowledged",
            Timestamp = DateTime.UtcNow,
            ServiceState = "running"
        };
        return Ok(new JsonRpcResponse<StopServiceResult> { Result = result, Id = request.Id });
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
        }
        return result;
    }

    private static AirdropParams ParseAirdropParams(object? parameters)
    {
        var result = new AirdropParams();
        if (parameters == null) return result;
        try
        {
            if (parameters is System.Text.Json.JsonElement element && element.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                if (element.TryGetProperty("lamports", out var lamportsElement))
                {
                    result.Lamports = lamportsElement.GetUInt64();
                }
                else if (element.TryGetProperty("sol_amount", out var solElement))
                {
                    result.Lamports = (ulong)(solElement.GetDouble() * 1_000_000_000);
                }
            }
        }
        catch
        {
        }
        return result;
    }

    private static string? ExtractPoolIdFromParams(object? parameters)
    {
        if (parameters == null) return null;
        try
        {
            if (parameters is System.Text.Json.JsonElement element && element.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                if (element.TryGetProperty("pool_id", out var poolIdElement))
                {
                    return poolIdElement.GetString();
                }
            }
        }
        catch
        {
        }
        return null;
    }
}


