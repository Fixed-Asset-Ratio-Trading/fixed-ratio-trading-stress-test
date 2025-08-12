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
        // Phase 1 stub: no param parsing, create a default mock deposit thread
        var threadId = await _threadManager.CreateThreadAsync(new ThreadConfig
        {
            ThreadType = ThreadType.Deposit,
            PoolId = "mock_pool_1",
            TokenType = TokenType.A,
            InitialAmount = 1_000_000
        });

        return Ok(new JsonRpcResponse<object>
        {
            Result = new { threadId, status = "created" },
            Id = request.Id
        });
    }

    [HttpPost("start")]
    public async Task<ActionResult<object>> StartThread([FromBody] JsonRpcRequest request)
    {
        // Phase 1 stub: in real phase parse threadId from params
        return Ok(new JsonRpcResponse<object>
        {
            Error = new JsonRpcError { Code = -32602, Message = "Missing threadId in params (Phase 1 stub)" },
            Id = request.Id
        });
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

