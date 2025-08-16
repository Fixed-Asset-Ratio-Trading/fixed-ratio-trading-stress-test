using FixedRatioStressTest.Common.Models;
using FixedRatioStressTest.Abstractions;
using FixedRatioStressTest.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace FixedRatioStressTest.Api.Controllers;

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

    /// <summary>
    /// Simulates pool creation to validate transaction format before execution
    /// </summary>
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

    /// <summary>
    /// Creates a new pool on the blockchain as specified in the design documents
    /// This includes simulation validation before actual creation
    /// </summary>
    [HttpPost("create")]
    public async Task<ActionResult<JsonRpcResponse<PoolCreationResult>>> CreatePool([FromBody] JsonRpcRequest request)
    {
        try
        {
            _logger.LogInformation("RPC create_pool requested: {Method}", request.Method);
            
            // Parse parameters from JSON-RPC request
            var parameters = ParsePoolCreationParams(request.Params);
            
            // Create REAL pool on blockchain using core wallet
            // This will: 1) Create/load core wallet, 2) Check SOL balance, 3) Attempt airdrop if needed, 4) Create pool
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
                IsBlockchainPool = true // Always true for real pools
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

    /// <summary>
    /// Creates a new pool with random parameters
    /// </summary>
    [HttpPost("create_random")]
    public async Task<ActionResult<JsonRpcResponse<PoolCreationResult>>> CreatePoolRandom([FromBody] JsonRpcRequest request)
    {
        try
        {
            _logger.LogInformation("RPC create_pool_random requested");
            
            // Generate random parameters
            var random = new Random();
            var parameters = new PoolCreationParams
            {
                TokenADecimals = random.Next(6, 10), // 6-9 decimals
                TokenBDecimals = random.Next(6, 10), // 6-9 decimals
                RatioWholeNumber = (ulong)random.Next(100, 10000), // 100-9999 ratio
                RatioDirection = random.Next(2) == 0 ? "a_to_b" : "b_to_a"
            };
            
            _logger.LogInformation("Generated random parameters: TokenA={TokenADecimals} decimals, TokenB={TokenBDecimals} decimals, Ratio={RatioWholeNumber} {Direction}",
                parameters.TokenADecimals, parameters.TokenBDecimals, parameters.RatioWholeNumber, parameters.RatioDirection);
            
            // Create REAL pool on blockchain using core wallet
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
                IsBlockchainPool = true // Always true for real pools
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

    /// <summary>
    /// Lists all available pools
    /// </summary>
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

    /// <summary>
    /// Gets detailed information about a specific pool
    /// </summary>
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

    /// <summary>
    /// JSON-RPC endpoint for pool creation (matches design document API)
    /// </summary>
    [HttpPost("/api/jsonrpc")]
    public async Task<ActionResult<object>> JsonRpcEndpoint([FromBody] JsonRpcRequest request)
    {
        try
        {
            _logger.LogInformation("JSON-RPC request received via /api/jsonrpc: {Method}", request.Method);
            return request.Method switch
            {
                "create_pool" => await CreatePool(request),
                "create_pool_random" => await CreatePoolRandom(request),
                "list_pools" => await ListPools(),
                "get_pool" => await GetPoolById(request),
                "core_wallet_status" => await GetCoreWalletStatus(request),
                "airdrop_sol" => await AirdropSol(request),
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

    private async Task<ActionResult<JsonRpcResponse<PoolState>>> GetPoolById(JsonRpcRequest request)
    {
        // Extract pool ID from params
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
            
            // Parse parameters
            var parameters = ParseAirdropParams(request.Params);
            var lamports = parameters.Lamports;
            
            // Get core wallet
            var wallet = await _solanaClient.GetOrCreateCoreWalletAsync();
            
            // Request airdrop
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

// Response models for pool API
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
    public ulong Lamports { get; set; } = 1_000_000_000; // Default 1 SOL
}
