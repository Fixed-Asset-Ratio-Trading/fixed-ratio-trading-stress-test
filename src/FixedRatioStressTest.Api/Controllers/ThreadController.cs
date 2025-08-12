using FixedRatioStressTest.Common.Models;
using FixedRatioStressTest.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace FixedRatioStressTest.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ThreadController : ControllerBase
{
    private readonly IThreadManager _threadManager;
    private readonly ILogger<ThreadController> _logger;

    public ThreadController(IThreadManager threadManager, ILogger<ThreadController> logger)
    {
        _threadManager = threadManager;
        _logger = logger;
    }

    [HttpPost("create")]
    public async Task<ActionResult<object>> CreateThread([FromBody] JsonRpcRequest request)
    {
        // Phase 3: Create different thread types for testing
        var random = new Random();
        var threadTypes = Enum.GetValues<ThreadType>();
        var tokenTypes = Enum.GetValues<TokenType>();
        var swapDirections = Enum.GetValues<SwapDirection>();
        
        var threadType = threadTypes[random.Next(threadTypes.Length)];
        var tokenType = tokenTypes[random.Next(tokenTypes.Length)];
        
        var config = new ThreadConfig
        {
            ThreadType = threadType,
            PoolId = "pool_1", // Use mock pool from transaction builder
            TokenType = tokenType,
            InitialAmount = (ulong)random.Next(100000, 10000000), // Random initial amount
            AutoRefill = random.Next(0, 2) == 1,
            ShareTokens = random.Next(0, 2) == 1
        };
        
        // Set swap direction for swap threads
        if (threadType == ThreadType.Swap)
        {
            config.SwapDirection = swapDirections[random.Next(swapDirections.Length)];
        }
        
        var threadId = await _threadManager.CreateThreadAsync(config);

        return Ok(new JsonRpcResponse<object>
        {
            Result = new { threadId, status = "created" },
            Id = request.Id
        });
    }

    [HttpPost("start")]
    public async Task<ActionResult<object>> StartThread([FromBody] JsonRpcRequest request)
    {
        try
        {
            // TODO: Phase 3 - Parse threadId from JSON-RPC params properly
            // For now, get the first available thread to start
            var allThreads = await _threadManager.GetAllThreadsAsync();
            var threadToStart = allThreads.FirstOrDefault(t => t.Status == ThreadStatus.Created);
            
            if (threadToStart == null)
            {
                return Ok(new JsonRpcResponse<object>
                {
                    Error = new JsonRpcError { Code = -32001, Message = "No threads available to start" },
                    Id = request.Id
                });
            }
            
            await _threadManager.StartThreadAsync(threadToStart.ThreadId);
            
            return Ok(new JsonRpcResponse<object>
            {
                Result = new { threadId = threadToStart.ThreadId, status = "started" },
                Id = request.Id
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting thread");
            return Ok(new JsonRpcResponse<object>
            {
                Error = new JsonRpcError { Code = -32603, Message = ex.Message },
                Id = request.Id
            });
        }
    }

    [HttpGet("status/{threadId}")]
    public async Task<ActionResult<object>> GetThreadStatus(string threadId)
    {
        try
        {
            var config = await _threadManager.GetThreadConfigAsync(threadId);
            var statistics = await _threadManager.GetThreadStatisticsAsync(threadId);
            
            // Create response object with masked private key for security
            var response = new
            {
                config = new
                {
                    config.ThreadId,
                    config.ThreadType,
                    config.PoolId,
                    config.TokenType,
                    config.SwapDirection,
                    config.InitialAmount,
                    config.AutoRefill,
                    config.ShareTokens,
                    config.Status,
                    config.CreatedAt,
                    config.LastOperationAt,
                    config.PublicKey,
                    HasWallet = !string.IsNullOrEmpty(config.PublicKey),
                    // Never expose private key or mnemonic in API response
                    PrivateKeySet = config.PrivateKey != null && config.PrivateKey.Length > 0,
                    MnemonicSet = !string.IsNullOrEmpty(config.WalletMnemonic)
                },
                statistics
            };
            
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting thread status");
            return NotFound();
        }
    }

    [HttpGet("all")]
    public async Task<ActionResult<List<ThreadConfig>>> GetAllThreads()
    {
        var threads = await _threadManager.GetAllThreadsAsync();
        return Ok(threads);
    }
}

