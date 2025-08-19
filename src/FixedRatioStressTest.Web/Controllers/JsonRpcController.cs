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
    private readonly IThreadManager _threadManager;
    private readonly ILogger<JsonRpcController> _logger;

    public JsonRpcController(ISolanaClientService solanaClient, IThreadManager threadManager, ILogger<JsonRpcController> logger)
    {
        _solanaClient = solanaClient;
        _threadManager = threadManager;
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
                // Thread management
                "create_deposit_thread" => await CreateDepositThread(request),
                "create_withdrawal_thread" => await CreateWithdrawalThread(request),
                "start_thread" => await StartThreadById(request),
                "stop_thread" => await StopThreadById(request),
                "delete_thread" => await DeleteThreadById(request),
                "get_thread" => await GetThreadById(request),
                "list_threads" => await ListThreads(request),
                "stop_all_pool_threads" => await StopAllPoolThreads(request),
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

    private async Task<ActionResult<object>> CreateDepositThread(JsonRpcRequest request)
    {
        try
        {
            if (request.Params is System.Text.Json.JsonElement el && el.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                var poolId = el.TryGetProperty("pool_id", out var poolEl) ? poolEl.GetString() ?? string.Empty : string.Empty;
                var tokenTypeStr = el.TryGetProperty("token_type", out var tokenEl) ? tokenEl.GetString() ?? "A" : "A";
                var initialAmount = el.TryGetProperty("initial_amount", out var amountEl) && amountEl.TryGetUInt64(out var amt) ? amt : 0UL;
                var autoRefill = el.TryGetProperty("auto_refill", out var arEl) && arEl.ValueKind == System.Text.Json.JsonValueKind.True;
                var shareLpTokens = !el.TryGetProperty("share_lp_tokens", out var shareEl) || (shareEl.ValueKind == System.Text.Json.JsonValueKind.True);
            if (string.IsNullOrWhiteSpace(poolId))
            {
                return Ok(new JsonRpcResponse<object> { Error = new JsonRpcError { Code = -32602, Message = "pool_id is required" }, Id = request.Id });
            }
            var tokenType = string.Equals(tokenTypeStr, "B", StringComparison.OrdinalIgnoreCase) ? TokenType.B : TokenType.A;

            var config = new ThreadConfig
            {
                ThreadType = ThreadType.Deposit,
                PoolId = poolId,
                TokenType = tokenType,
                InitialAmount = initialAmount,
                AutoRefill = autoRefill,
                ShareTokens = shareLpTokens
            };
            var threadId = await _threadManager.CreateThreadAsync(config);
            return Ok(new JsonRpcResponse<object> { Result = new { threadId, status = "created" }, Id = request.Id });
            }
            return Ok(new JsonRpcResponse<object> { Error = new JsonRpcError { Code = -32602, Message = "Invalid params" }, Id = request.Id });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "create_deposit_thread failed");
            return Ok(new JsonRpcResponse<object> { Error = new JsonRpcError { Code = -32603, Message = ex.Message }, Id = request.Id });
        }
    }

    private async Task<ActionResult<object>> CreateWithdrawalThread(JsonRpcRequest request)
    {
        try
        {
            if (request.Params is System.Text.Json.JsonElement el && el.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                var poolId = el.TryGetProperty("pool_id", out var poolEl) ? poolEl.GetString() ?? string.Empty : string.Empty;
                var tokenTypeStr = el.TryGetProperty("token_type", out var tokenEl) ? tokenEl.GetString() ?? "A" : "A";
            if (string.IsNullOrWhiteSpace(poolId))
            {
                return Ok(new JsonRpcResponse<object> { Error = new JsonRpcError { Code = -32602, Message = "pool_id is required" }, Id = request.Id });
            }
            var tokenType = string.Equals(tokenTypeStr, "B", StringComparison.OrdinalIgnoreCase) ? TokenType.B : TokenType.A;
            var config = new ThreadConfig
            {
                ThreadType = ThreadType.Withdrawal,
                PoolId = poolId,
                TokenType = tokenType,
                InitialAmount = 0,
                AutoRefill = false,
                ShareTokens = false
            };
            var threadId = await _threadManager.CreateThreadAsync(config);
            return Ok(new JsonRpcResponse<object> { Result = new { threadId, status = "created" }, Id = request.Id });
            }
            return Ok(new JsonRpcResponse<object> { Error = new JsonRpcError { Code = -32602, Message = "Invalid params" }, Id = request.Id });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "create_withdrawal_thread failed");
            return Ok(new JsonRpcResponse<object> { Error = new JsonRpcError { Code = -32603, Message = ex.Message }, Id = request.Id });
        }
    }

    private async Task<ActionResult<object>> StartThreadById(JsonRpcRequest request)
    {
        var id = ExtractThreadId(request.Params);
        if (string.IsNullOrWhiteSpace(id))
        {
            return Ok(new JsonRpcResponse<object> { Error = new JsonRpcError { Code = -32602, Message = "thread_id is required" }, Id = request.Id });
        }
        await _threadManager.StartThreadAsync(id);
        return Ok(new JsonRpcResponse<object> { Result = new { threadId = id, status = "started" }, Id = request.Id });
    }

    private async Task<ActionResult<object>> StopThreadById(JsonRpcRequest request)
    {
        var id = ExtractThreadId(request.Params);
        if (string.IsNullOrWhiteSpace(id))
        {
            return Ok(new JsonRpcResponse<object> { Error = new JsonRpcError { Code = -32602, Message = "thread_id is required" }, Id = request.Id });
        }
        await _threadManager.StopThreadAsync(id);
        return Ok(new JsonRpcResponse<object> { Result = new { threadId = id, status = "stopped" }, Id = request.Id });
    }

    private async Task<ActionResult<object>> DeleteThreadById(JsonRpcRequest request)
    {
        var id = ExtractThreadId(request.Params);
        if (string.IsNullOrWhiteSpace(id))
        {
            return Ok(new JsonRpcResponse<object> { Error = new JsonRpcError { Code = -32602, Message = "thread_id is required" }, Id = request.Id });
        }
        await _threadManager.DeleteThreadAsync(id);
        return Ok(new JsonRpcResponse<object> { Result = new { threadId = id, status = "deleted" }, Id = request.Id });
    }

    private async Task<ActionResult<object>> GetThreadById(JsonRpcRequest request)
    {
        var id = ExtractThreadId(request.Params);
        if (string.IsNullOrWhiteSpace(id))
        {
            return Ok(new JsonRpcResponse<object> { Error = new JsonRpcError { Code = -32602, Message = "thread_id is required" }, Id = request.Id });
        }
        var cfg = await _threadManager.GetThreadConfigAsync(id);
        var stats = await _threadManager.GetThreadStatisticsAsync(id);
        return Ok(new JsonRpcResponse<object> { Result = new { config = cfg, statistics = stats }, Id = request.Id });
    }

    private async Task<ActionResult<object>> ListThreads(JsonRpcRequest request)
    {
        var list = await _threadManager.GetAllThreadsAsync();
        return Ok(new JsonRpcResponse<object> { Result = new { threads = list }, Id = request.Id });
    }

    private async Task<ActionResult<object>> StopAllPoolThreads(JsonRpcRequest request)
    {
        if (request.Params is System.Text.Json.JsonElement el && el.ValueKind == System.Text.Json.JsonValueKind.Object)
        {
            var poolId = el.TryGetProperty("pool_id", out var poolEl) ? poolEl.GetString() ?? string.Empty : string.Empty;
            var includeSwaps = el.TryGetProperty("include_swaps", out var incEl) && incEl.ValueKind == System.Text.Json.JsonValueKind.True;
            if (string.IsNullOrWhiteSpace(poolId))
            {
                return Ok(new JsonRpcResponse<object> { Error = new JsonRpcError { Code = -32602, Message = "pool_id is required" }, Id = request.Id });
            }
            var count = await _threadManager.StopAllThreadsForPoolAsync(poolId, includeSwaps);
            return Ok(new JsonRpcResponse<object> { Result = new { poolId, stopped = count }, Id = request.Id });
        }
        return Ok(new JsonRpcResponse<object> { Error = new JsonRpcError { Code = -32602, Message = "Invalid params" }, Id = request.Id });
    }

    private static string ExtractThreadId(object? parameters)
    {
        if (parameters is System.Text.Json.JsonElement el && el.ValueKind == System.Text.Json.JsonValueKind.Object)
        {
            if (el.TryGetProperty("thread_id", out var idEl))
            {
                return idEl.GetString() ?? string.Empty;
            }
        }
        return string.Empty;
    }

    private async Task<ActionResult<object>> CreatePool(JsonRpcRequest request)
    {
        _logger.LogInformation("RPC create_pool requested");
        PoolCreationParams parameters;
        try
        {
            parameters = ParsePoolCreationParams(request.Params);
        }
        catch (ArgumentException ex)
        {
            return Ok(new JsonRpcResponse<object>
            {
                Error = new JsonRpcError
                {
                    Code = -32602,
                    Message = ex.Message
                },
                Id = request.Id
            });
        }
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
                // Hard-reject legacy fields
                if (element.TryGetProperty("ratio_whole_number", out _))
                {
                    throw new ArgumentException("'ratio_whole_number' has been removed. Use 'ratio' (e.g., '1:10' or '10:1').");
                }
                if (element.TryGetProperty("ratio_direction", out _))
                {
                    throw new ArgumentException("'ratio_direction' has been removed. Use 'ratio' (e.g., '1:10' or '10:1').");
                }

                if (element.TryGetProperty("token_a_decimals", out var tokenADecimalsElement))
                {
                    result.TokenADecimals = tokenADecimalsElement.GetInt32();
                }
                if (element.TryGetProperty("token_b_decimals", out var tokenBDecimalsElement))
                {
                    result.TokenBDecimals = tokenBDecimalsElement.GetInt32();
                }
                // New simple ratio support: "ratio": "1:10" or "10:1"
                if (element.TryGetProperty("ratio", out var ratioElement) && ratioElement.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    var ratioString = ratioElement.GetString() ?? string.Empty;
                    ratioString = ratioString.Trim();
                    // Basic validation and parsing
                    var parts = ratioString.Split(':');
                    if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[1]))
                    {
                        throw new ArgumentException("Invalid ratio format. Expected '1:x' or 'x:1'.");
                    }

                    if (!ulong.TryParse(parts[0].Trim(), out var left) || !ulong.TryParse(parts[1].Trim(), out var right))
                    {
                        throw new ArgumentException("Invalid ratio numbers. Only whole numbers are allowed.");
                    }

                    if ((left == 1 && right >= 1) && !(right == 1))
                    {
                        // 1 : X  => anchor A to 1, X must be > 1
                        result.RatioDirection = "a_to_b";
                        result.RatioWholeNumber = right;
                    }
                    else if ((right == 1 && left >= 1) && !(left == 1))
                    {
                        // X : 1  => anchor B to 1, X must be > 1
                        result.RatioDirection = "b_to_a";
                        result.RatioWholeNumber = left;
                    }
                    else
                    {
                        throw new ArgumentException("Ratio must have exactly one side equal to 1 (e.g., '1:10' or '10:1').");
                    }
                }
                else
                {
                    // 'ratio' is required now
                    throw new ArgumentException("'ratio' is required. Example: { \"token_a_decimals\": 9, \"token_b_decimals\": 9, \"ratio\": \"1:10\" }");
                }
            }
        }
        catch (Exception)
        {
            // Re-throw to allow caller to return proper JSON-RPC error
            throw;
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


