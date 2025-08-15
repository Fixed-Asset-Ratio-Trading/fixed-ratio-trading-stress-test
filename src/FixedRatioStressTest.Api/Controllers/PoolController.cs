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
    private readonly IEventLogger _eventLogger;

    public PoolController(ISolanaClientService solanaClient, ILogger<PoolController> logger, IEventLogger eventLogger)
    {
        _solanaClient = solanaClient;
        _logger = logger;
        _eventLogger = eventLogger;
    }

    /// <summary>
    /// Simulates pool creation to validate transaction format before execution
    /// </summary>
    [HttpPost("simulate")]
    public async Task<ActionResult<JsonRpcResponse<TransactionSimulationResult>>> SimulatePoolCreation([FromBody] JsonRpcRequest request)
    {
        try
        {
            _logger.LogInformation("Pool creation simulation request received.");
            _eventLogger.LogInformation("RPC simulate_pool requested");
            var parameters = System.Text.Json.JsonSerializer.Deserialize<PoolCreationParams>(request.Params?.ToString() ?? "{}");
            var simulationResult = await _solanaClient.SimulatePoolCreationAsync(parameters);
            _eventLogger.LogInformation("RPC simulate_pool completed: Success={0}", simulationResult.IsSuccessful);
            
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
            _logger.LogInformation("Pool creation request received: {Request}", request.Method);
            _eventLogger.LogInformation("RPC create_pool requested");
            
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

            _eventLogger.LogInformation("RPC create_pool completed: PoolId={0}", result.PoolId);

            return Ok(new JsonRpcResponse<PoolCreationResult>
            {
                Result = result,
                Id = request.Id
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating pool");
            _eventLogger.LogError("RPC create_pool failed", ex);
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
            _logger.LogInformation("List pools request received.");
            _eventLogger.LogInformation("RPC list_pools requested");
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

            _eventLogger.LogInformation("RPC list_pools completed: Count={0}", result.TotalCount);

            return Ok(new JsonRpcResponse<PoolListResult>
            {
                Result = result,
                Id = Guid.NewGuid().ToString()
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing pools");
            _eventLogger.LogError("RPC list_pools failed", ex);
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
            _logger.LogInformation("Get pool request: {PoolId}", poolId);
            _eventLogger.LogInformation("RPC get_pool requested: PoolId={0}", poolId);
            var pool = await _solanaClient.GetPoolStateAsync(poolId);
            
            _eventLogger.LogInformation("RPC get_pool completed: PoolId={0}", poolId);
            return Ok(new JsonRpcResponse<PoolState>
            {
                Result = pool,
                Id = Guid.NewGuid().ToString()
            });
        }
        catch (KeyNotFoundException)
        {
            _eventLogger.LogWarning("RPC get_pool not found: PoolId={0}", poolId);
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
            _logger.LogInformation("JSON-RPC request received: {Method}", request.Method);
            _eventLogger.LogInformation("RPC {0} received via /api/jsonrpc", request.Method);
            return request.Method switch
            {
                "create_pool" => await CreatePool(request),
                "list_pools" => await ListPools(),
                "get_pool" => await GetPoolById(request),
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
            _logger.LogError(ex, "Error in JSON-RPC endpoint");
            _eventLogger.LogError("RPC endpoint error", ex);
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
