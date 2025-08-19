using FixedRatioStressTest.Common.Models;
using FixedRatioStressTest.Abstractions;
using FixedRatioStressTest.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace FixedRatioStressTest.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PoolController : ControllerBase
{
    private readonly ISolanaClientService _solanaClient;
    private readonly ILogger<PoolController> _logger;

    public PoolController(ISolanaClientService solanaClient, ILogger<PoolController> logger)
    {
        _solanaClient = solanaClient;
        _logger = logger;
    }

    [HttpPost("simulate")]
    public async Task<ActionResult<JsonRpcResponse<TransactionSimulationResult>>> SimulatePoolCreation([FromBody] JsonRpcRequest request)
    {
        try
        {
            _logger.LogInformation("RPC simulate_pool requested: pool creation simulation");
            var parameters = System.Text.Json.JsonSerializer.Deserialize<PoolCreationParams>(request.Params?.ToString() ?? "{}");
            var simulationResult = await _solanaClient.SimulatePoolCreationAsync(parameters);
            _logger.LogInformation("RPC simulate_pool completed: Success={Success}", simulationResult.IsSuccessful);
            
            return Ok(new JsonRpcResponse<TransactionSimulationResult>
            {
                Id = request.Id,
                Result = simulationResult
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error simulating pool creation.");
            return StatusCode(500, new JsonRpcResponse<object>
            {
                Id = request.Id,
                Error = new JsonRpcError { Code = -32000, Message = ex.Message }
            });
        }
    }

    [HttpPost("create")]
    public async Task<ActionResult<JsonRpcResponse<PoolCreationResult>>> CreatePool([FromBody] JsonRpcRequest request)
    {
        try
        {
            _logger.LogInformation("RPC create_pool requested: {Method}", request.Method);
            
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

            _logger.LogInformation("RPC create_pool completed: PoolId={PoolId}", result.PoolId);

            return Ok(new JsonRpcResponse<PoolCreationResult>
            {
                Result = result,
                Id = request.Id
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RPC create_pool failed");
            return Ok(new JsonRpcResponse<PoolCreationResult>
            {
                Error = new JsonRpcError
                {
                    Code = -1,
                    Message = ex.Message
                },
                Id = request.Id
            });
        }
    }

    [HttpPost("create_random")]
    public async Task<ActionResult<JsonRpcResponse<PoolCreationResult>>> CreatePoolRandom([FromBody] JsonRpcRequest request)
    {
        try
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

            _logger.LogInformation("RPC create_pool_random completed: PoolId={PoolId}", result.PoolId);

            return Ok(new JsonRpcResponse<PoolCreationResult>
            {
                Result = result,
                Id = request.Id
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RPC create_pool_random failed");
            return Ok(new JsonRpcResponse<PoolCreationResult>
            {
                Error = new JsonRpcError
                {
                    Code = -1,
                    Message = ex.Message
                },
                Id = request.Id
            });
        }
    }

    [HttpGet("list")]
    public async Task<ActionResult<JsonRpcResponse<PoolListResult>>> ListPools()
    {
        try
        {
            _logger.LogInformation("RPC list_pools requested");
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

            _logger.LogInformation("RPC list_pools completed: Count={Count}", result.TotalCount);

            return Ok(new JsonRpcResponse<PoolListResult>
            {
                Result = result,
                Id = Guid.NewGuid().ToString()
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RPC list_pools failed");
            return Ok(new JsonRpcResponse<PoolListResult>
            {
                Error = new JsonRpcError
                {
                    Code = -1,
                    Message = ex.Message
                },
                Id = Guid.NewGuid().ToString()
            });
        }
    }

    [HttpGet("{poolId}")]
    public async Task<ActionResult<JsonRpcResponse<PoolState>>> GetPool(string poolId)
    {
        try
        {
            _logger.LogInformation("RPC get_pool requested: PoolId={PoolId}", poolId);
            var pool = await _solanaClient.GetPoolStateAsync(poolId);
            
            _logger.LogInformation("RPC get_pool completed: PoolId={PoolId}", poolId);
            return Ok(new JsonRpcResponse<PoolState>
            {
                Result = pool,
                Id = Guid.NewGuid().ToString()
            });
        }
        catch (KeyNotFoundException)
        {
            _logger.LogWarning("RPC get_pool not found: PoolId={PoolId}", poolId);
            return Ok(new JsonRpcResponse<PoolState>
            {
                Error = new JsonRpcError
                {
                    Code = -32602,
                    Message = $"Pool {poolId} not found"
                },
                Id = Guid.NewGuid().ToString()
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting pool {PoolId}", poolId);
            return Ok(new JsonRpcResponse<PoolState>
            {
                Error = new JsonRpcError
                {
                    Code = -1,
                    Message = ex.Message
                },
                Id = Guid.NewGuid().ToString()
            });
        }
    }

    // JSON-RPC moved to JsonRpcController to avoid route conflicts

    private async Task<ActionResult<JsonRpcResponse<PoolState>>> GetPoolById(JsonRpcRequest request)
    {
        var poolId = ExtractPoolIdFromParams(request.Params);
        if (string.IsNullOrEmpty(poolId))
        {
            return Ok(new JsonRpcResponse<PoolState>
            {
                Error = new JsonRpcError
                {
                    Code = -32602,
                    Message = "Pool ID is required"
                },
                Id = request.Id
            });
        }

        return await GetPool(poolId);
    }

    private async Task<ActionResult<JsonRpcResponse<CoreWalletStatusResult>>> GetCoreWalletStatus(JsonRpcRequest request)
    {
        try
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

            _logger.LogInformation("RPC core_wallet_status completed: PublicKey={PublicKey} BalanceSOL={Balance}",
                result.PublicKey, result.CurrentSolBalance);

            return Ok(new JsonRpcResponse<CoreWalletStatusResult>
            {
                Result = result,
                Id = request.Id
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RPC core_wallet_status failed");
            return Ok(new JsonRpcResponse<CoreWalletStatusResult>
            {
                Error = new JsonRpcError
                {
                    Code = -1,
                    Message = ex.Message
                },
                Id = request.Id
            });
        }
    }

    private async Task<ActionResult<JsonRpcResponse<AirdropResult>>> AirdropSol(JsonRpcRequest request)
    {
        try
        {
            _logger.LogInformation("RPC airdrop_sol requested");
            
            var parameters = ParseAirdropParams(request.Params);
            var lamports = parameters.Lamports;
            
            var wallet = await _solanaClient.GetOrCreateCoreWalletAsync();
            var signature = await _solanaClient.RequestAirdropAsync(wallet.PublicKey, lamports);
            
            var result = new AirdropResult
            {
                WalletAddress = wallet.PublicKey,
                Lamports = lamports,
                SolAmount = lamports / 1_000_000_000.0,
                Signature = signature,
                Status = "success"
            };

            _logger.LogInformation("RPC airdrop_sol completed: {Lamports} lamports ({Sol} SOL) to {Address}",
                lamports, result.SolAmount, wallet.PublicKey);

            return Ok(new JsonRpcResponse<AirdropResult>
            {
                Result = result,
                Id = request.Id
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RPC airdrop_sol failed");
            return Ok(new JsonRpcResponse<AirdropResult>
            {
                Error = new JsonRpcError
                {
                    Code = -1,
                    Message = ex.Message
                },
                Id = request.Id
            });
        }
    }

    private async Task<ActionResult<JsonRpcResponse<StopServiceResult>>> StopService(JsonRpcRequest request)
    {
        try
        {
            _logger.LogInformation("RPC stop_service requested");
            
            var result = new StopServiceResult
            {
                Message = "Stop service request acknowledged",
                Status = "acknowledged",
                Timestamp = DateTime.UtcNow,
                ServiceState = "running"
            };

            _logger.LogInformation("RPC stop_service acknowledged (service not actually stopped)");

            return Ok(new JsonRpcResponse<StopServiceResult>
            {
                Result = result,
                Id = request.Id
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RPC stop_service failed");
            return Ok(new JsonRpcResponse<StopServiceResult>
            {
                Error = new JsonRpcError
                {
                    Code = -1,
                    Message = ex.Message
                },
                Id = request.Id
            });
        }
    }

    private PoolCreationParams ParsePoolCreationParams(object? parameters)
    {
        var result = new PoolCreationParams();
        
        if (parameters == null)
            return result;

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
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse pool creation parameters, using defaults");
        }

        return result;
    }

    private AirdropParams ParseAirdropParams(object? parameters)
    {
        var result = new AirdropParams();
        
        if (parameters == null)
            return result;

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
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse airdrop parameters, using default");
        }

        return result;
    }

    private string? ExtractPoolIdFromParams(object? parameters)
    {
        if (parameters == null)
            return null;

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
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to extract pool ID from parameters");
        }

        return null;
    }
}

public class PoolCreationResult
{
    public string PoolId { get; set; } = string.Empty;
    public string TokenAMint { get; set; } = string.Empty;
    public string TokenBMint { get; set; } = string.Empty;
    public int TokenADecimals { get; set; }
    public int TokenBDecimals { get; set; }
    public string RatioDisplay { get; set; } = string.Empty;
    public string? CreationSignature { get; set; }
    public string Status { get; set; } = string.Empty;
    public bool IsBlockchainPool { get; set; }
}

public class PoolListResult
{
    public List<PoolSummary> Pools { get; set; } = new();
    public int TotalCount { get; set; }
}

public class PoolSummary
{
    public string PoolId { get; set; } = string.Empty;
    public string TokenAMint { get; set; } = string.Empty;
    public string TokenBMint { get; set; } = string.Empty;
    public int TokenADecimals { get; set; }
    public int TokenBDecimals { get; set; }
    public string RatioDisplay { get; set; } = string.Empty;
    public bool IsBlockchainPool { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class CoreWalletStatusResult
{
    public string PublicKey { get; set; } = string.Empty;
    public ulong CurrentSolBalanceLamports { get; set; }
    public double CurrentSolBalance { get; set; }
    public ulong MinimumSolBalanceLamports { get; set; }
    public double MinimumSolBalance { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime LastBalanceCheck { get; set; }
}

public class AirdropResult
{
    public string WalletAddress { get; set; } = string.Empty;
    public ulong Lamports { get; set; }
    public double SolAmount { get; set; }
    public string Signature { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}

public class AirdropParams
{
    public ulong Lamports { get; set; } = 1_000_000_000;
}

public class StopServiceResult
{
    public string Message { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public string ServiceState { get; set; } = string.Empty;
}


