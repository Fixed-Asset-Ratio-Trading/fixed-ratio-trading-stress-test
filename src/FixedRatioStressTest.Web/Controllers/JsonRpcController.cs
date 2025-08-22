using FixedRatioStressTest.Common.Models;
using FixedRatioStressTest.Core.Interfaces;
using FixedRatioStressTest.Core.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace FixedRatioStressTest.Web.Controllers;

[ApiController]
[Route("api/jsonrpc")]
public class JsonRpcController : ControllerBase
{
    private readonly ISolanaClientService _solanaClient;
    private readonly IThreadManager _threadManager;
    private readonly IEmptyCommandHandler _emptyHandler;
    private readonly ILogger<JsonRpcController> _logger;

    public JsonRpcController(ISolanaClientService solanaClient, IThreadManager threadManager, IEmptyCommandHandler emptyHandler, ILogger<JsonRpcController> logger)
    {
        _solanaClient = solanaClient;
        _threadManager = threadManager;
        _emptyHandler = emptyHandler;
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
                "create_swap_thread" => await CreateSwapThread(request),
                "start_thread" => await StartThreadById(request),
                "stop_thread" => await StopThreadById(request),
                "delete_thread" => await DeleteThreadById(request),
                "get_thread" => await GetThreadById(request),
                "get_thread_stats" => await GetThreadStats(request),
                "list_threads" => await ListThreads(request),
                "stop_all_pool_threads" => await StopAllPoolThreads(request),
                "empty_thread" => await EmptyThread(request),
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

    private async Task<ActionResult<object>> CreateSwapThread(JsonRpcRequest request)
    {
        try
        {
            if (request.Params is System.Text.Json.JsonElement el && el.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                var poolId = el.TryGetProperty("pool_id", out var poolEl) ? poolEl.GetString() ?? string.Empty : string.Empty;
                var directionStr = el.TryGetProperty("direction", out var dirEl) ? dirEl.GetString() ?? "a_to_b" : "a_to_b";
                var initialAmount = el.TryGetProperty("initial_amount", out var amountEl) && amountEl.TryGetUInt64(out var amt) ? amt : 0UL;
                
                if (string.IsNullOrWhiteSpace(poolId))
                {
                    return Ok(new JsonRpcResponse<object> { Error = new JsonRpcError { Code = -32602, Message = "pool_id is required" }, Id = request.Id });
                }

                // Parse swap direction
                var swapDirection = string.Equals(directionStr, "b_to_a", StringComparison.OrdinalIgnoreCase) ? SwapDirection.BToA : SwapDirection.AToB;

                // Check if a swap thread with this direction already exists for the pool
                var existingThreads = await _threadManager.GetAllThreadsAsync();
                var existingSwapThread = existingThreads.FirstOrDefault(t => 
                    t.ThreadType == ThreadType.Swap && 
                    t.PoolId == poolId && 
                    t.SwapDirection == swapDirection);

                if (existingSwapThread != null)
                {
                    return Ok(new JsonRpcResponse<object> 
                    { 
                        Error = new JsonRpcError 
                        { 
                            Code = -1001, 
                            Message = $"Swap thread for direction '{directionStr}' already exists for pool {poolId}" 
                        }, 
                        Id = request.Id 
                    });
                }

                var config = new ThreadConfig
                {
                    ThreadType = ThreadType.Swap,
                    PoolId = poolId,
                    SwapDirection = swapDirection,
                    InitialAmount = initialAmount,
                    AutoRefill = false,
                    ShareTokens = true // Swap threads always share tokens with opposite direction
                };

                var threadId = await _threadManager.CreateThreadAsync(config);
                var result = new { threadId, walletAddress = config.PublicKey, status = "created" };
                return Ok(new JsonRpcResponse<object> { Result = result, Id = request.Id });
            }
            return Ok(new JsonRpcResponse<object> { Error = new JsonRpcError { Code = -32602, Message = "Invalid params for swap thread" }, Id = request.Id });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create swap thread");
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
        
        // Automatically empty the thread before deletion
        try
        {
            var emptyResult = await _emptyHandler.ExecuteEmptyCommandAsync(id);
            _logger.LogInformation("Thread {ThreadId} emptied before deletion. SOL returned: {SolReturned}", 
                id, emptyResult.SolReturned);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed to empty thread {ThreadId} before deletion: {Error}", id, ex.Message);
            // Continue with deletion even if empty fails
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

        // Provide a human-readable swap direction string in addition to enum
        string? swapDirString = null;
        if (cfg.SwapDirection.HasValue)
        {
            swapDirString = cfg.SwapDirection.Value == SwapDirection.AToB ? "a_to_b" : "b_to_a";
        }

        var enhancedConfig = new
        {
            threadId = cfg.ThreadId,
            threadType = cfg.ThreadType.ToString().ToLowerInvariant(), // human-readable: deposit, withdrawal, swap
            poolId = cfg.PoolId,
            tokenType = cfg.TokenType.ToString().ToLowerInvariant(), // human-readable: a, b
            swapDirection = cfg.SwapDirection, // original enum value
            swap_direction = swapDirString,     // human-readable string
            initialAmount = cfg.InitialAmount,
            autoRefill = cfg.AutoRefill,
            shareTokens = cfg.ShareTokens,
            status = cfg.Status.ToString().ToLowerInvariant(), // human-readable: running, stopped, etc.
            createdAt = cfg.CreatedAt,
            lastOperationAt = cfg.LastOperationAt,
            publicKey = cfg.PublicKey,
            privateKey = cfg.PrivateKey,
            walletMnemonic = cfg.WalletMnemonic
        };

        return Ok(new JsonRpcResponse<object> { Result = new { config = enhancedConfig, statistics = stats }, Id = request.Id });
    }

    private async Task<ActionResult<object>> GetThreadStats(JsonRpcRequest request)
    {
        var id = ExtractThreadId(request.Params);
        if (string.IsNullOrWhiteSpace(id))
        {
            return Ok(new JsonRpcResponse<object> { Error = new JsonRpcError { Code = -32602, Message = "thread_id is required" }, Id = request.Id });
        }
        var stats = await _threadManager.GetThreadStatisticsAsync(id);
        return Ok(new JsonRpcResponse<object> { Result = stats, Id = request.Id });
    }

    private async Task<ActionResult<object>> ListThreads(JsonRpcRequest request)
    {
        try
        {
            // Parse optional filters from request params
            string? poolIdFilter = null;
            ThreadType? threadTypeFilter = null;
            
            if (request.Params is System.Text.Json.JsonElement el && el.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                // Optional pool_id filter
                if (el.TryGetProperty("pool_id", out var poolEl) && poolEl.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    poolIdFilter = poolEl.GetString();
                }
                
                // Optional thread_type filter (deposit, withdrawal, swap)
                if (el.TryGetProperty("thread_type", out var typeEl) && typeEl.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    var typeStr = typeEl.GetString();
                    if (Enum.TryParse<ThreadType>(typeStr, true, out var parsedType))
                    {
                        threadTypeFilter = parsedType;
                    }
                }
            }
            
            // Get all threads and apply filters
            var allThreads = await _threadManager.GetAllThreadsAsync();
            var filteredThreads = allThreads.AsEnumerable();
            
            if (!string.IsNullOrWhiteSpace(poolIdFilter))
            {
                filteredThreads = filteredThreads.Where(t => t.PoolId.Equals(poolIdFilter, StringComparison.OrdinalIgnoreCase));
            }
            
            if (threadTypeFilter.HasValue)
            {
                filteredThreads = filteredThreads.Where(t => t.ThreadType == threadTypeFilter.Value);
            }
            
            // Transform threads to include enhanced display information
            var enhancedThreads = filteredThreads.Select(thread => new
            {
                thread_id = thread.ThreadId,
                thread_type = thread.ThreadType.ToString().ToLowerInvariant(),
                pool_id = thread.PoolId,
                token_type = thread.TokenType.ToString().ToLowerInvariant(), // a or b
                token_info = GetTokenDisplayInfo(thread),
                swap_direction = thread.SwapDirection.HasValue ? 
                    (thread.SwapDirection.Value == SwapDirection.AToB ? "a_to_b" : "b_to_a") : null,
                status = thread.Status.ToString().ToLowerInvariant(),
                initial_amount = thread.InitialAmount,
                auto_refill = thread.AutoRefill,
                share_tokens = thread.ShareTokens,
                created_at = thread.CreatedAt,
                last_operation_at = thread.LastOperationAt,
                public_key = thread.PublicKey
            }).ToList();
            
            return Ok(new JsonRpcResponse<object> 
            { 
                Result = new 
                { 
                    threads = enhancedThreads,
                    total_count = enhancedThreads.Count,
                    filters_applied = new
                    {
                        pool_id = poolIdFilter,
                        thread_type = threadTypeFilter?.ToString().ToLowerInvariant()
                    }
                }, 
                Id = request.Id 
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing threads");
            return Ok(new JsonRpcResponse<object> 
            { 
                Error = new JsonRpcError { Code = -32603, Message = $"Internal error: {ex.Message}" }, 
                Id = request.Id 
            });
        }
    }
    
    private string GetTokenDisplayInfo(ThreadConfig thread)
    {
        return thread.ThreadType switch
        {
            ThreadType.Deposit => $"Depositing Token {thread.TokenType} → LP Token {thread.TokenType}",
            ThreadType.Withdrawal => $"Withdrawing LP Token {thread.TokenType} → Token {thread.TokenType}",
            ThreadType.Swap when thread.SwapDirection == SwapDirection.AToB => "Swapping Token A → Token B",
            ThreadType.Swap when thread.SwapDirection == SwapDirection.BToA => "Swapping Token B → Token A",
            ThreadType.Swap => $"Swapping (direction: {thread.SwapDirection})",
            _ => $"Operating on Token {thread.TokenType}"
        };
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

    private async Task<ActionResult<object>> EmptyThread(JsonRpcRequest request)
    {
        try
        {
            var id = ExtractThreadId(request.Params);
            if (string.IsNullOrWhiteSpace(id))
            {
                return Ok(new JsonRpcResponse<object> { Error = new JsonRpcError { Code = -32602, Message = "thread_id is required" }, Id = request.Id });
            }

            var result = await _emptyHandler.ExecuteEmptyCommandAsync(id);
            
            return Ok(new JsonRpcResponse<object> 
            { 
                Result = new 
                { 
                    thread_id = result.ThreadId,
                    thread_type = result.ThreadType,
                    empty_operation = new
                    {
                        tokens_used = result.TokensUsed,
                        lp_tokens_received = result.LpTokensReceived,
                        lp_tokens_used = result.LpTokensUsed,
                        tokens_withdrawn = result.TokensWithdrawn,
                        tokens_swapped_in = result.TokensSwappedIn,
                        tokens_swapped_out = result.TokensSwappedOut,
                        tokens_burned = result.TokensBurned,
                        sol_returned = result.SolReturned,
                        operation_successful = result.OperationSuccessful,
                        error_message = result.ErrorMessage,
                        transaction_signature = result.TransactionSignature,
                        network_fee_paid = result.NetworkFeePaid,
                        swap_direction = result.SwapDirection
                    }
                }, 
                Id = request.Id 
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to empty thread");
            return Ok(new JsonRpcResponse<object> { Error = new JsonRpcError { Code = -32603, Message = ex.Message }, Id = request.Id });
        }
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


