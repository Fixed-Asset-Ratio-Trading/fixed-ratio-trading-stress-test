using Microsoft.Extensions.Logging;
using FixedRatioStressTest.Common.Models;
using FixedRatioStressTest.Core.Interfaces;

namespace FixedRatioStressTest.Core.Services
{
    /// <summary>
    /// Handles contract-specific errors with recovery strategies
    /// </summary>
    public interface IContractErrorHandler
    {
        Task<bool> HandleContractError(Exception ex, ThreadContext context);
        bool TryParseContractError(Exception ex, out int errorCode);
        string GetErrorMessage(int errorCode);
    }
    
    public class ContractErrorHandler : IContractErrorHandler
    {
        private readonly ISolanaClientService _solanaClient;
        private readonly IStorageService _storageService;
        private readonly ILogger<ContractErrorHandler> _logger;
        
        public ContractErrorHandler(
            ISolanaClientService solanaClient,
            IStorageService storageService,
            ILogger<ContractErrorHandler> logger)
        {
            _solanaClient = solanaClient;
            _storageService = storageService;
            _logger = logger;
        }
        
        public async Task<bool> HandleContractError(Exception ex, ThreadContext context)
        {
            if (TryParseContractError(ex, out var errorCode))
            {
                _logger.LogWarning("Contract error {ErrorCode} for thread {ThreadId}: {Message}", 
                    errorCode, context.ThreadId, ContractErrorMessages.GetErrorMessage(errorCode));
                
                return errorCode switch
                {
                    ContractErrorCodes.InsufficientFunds => await HandleInsufficientFunds(context),
                    ContractErrorCodes.PoolPaused => await HandlePoolPaused(context),
                    ContractErrorCodes.SystemPaused => await HandleSystemPaused(context),
                    ContractErrorCodes.InsufficientLiquidity => await HandleInsufficientLiquidity(context),
                    ContractErrorCodes.SlippageExceeded => await HandleSlippageExceeded(context),
                    ContractErrorCodes.InvalidTokenAccount => await HandleInvalidTokenAccount(context),
                    ContractErrorCodes.PoolSwapsPaused => await HandlePoolSwapsPaused(context),
                    ContractErrorCodes.InvalidLpTokenType => await HandleInvalidLpTokenType(context),
                    _ => await HandleUnknownError(errorCode, context)
                };
            }
            
            _logger.LogError(ex, "Non-contract error for thread {ThreadId}", context.ThreadId);
            return false;
        }
        
        public bool TryParseContractError(Exception ex, out int errorCode)
        {
            errorCode = 0;
            
            // Look for patterns in exception messages that indicate contract errors
            // Format: "Program log: Error: Custom(1003)"
            // Or: "Program custom error: 0x3eb (1003)"
            
            var message = ex.Message;
            
            // Try to match "Custom(XXXX)" pattern
            var customMatch = System.Text.RegularExpressions.Regex.Match(message, @"Custom\((\d+)\)");
            if (customMatch.Success && int.TryParse(customMatch.Groups[1].Value, out errorCode))
            {
                return true;
            }
            
            // Try to match hex error code pattern
            var hexMatch = System.Text.RegularExpressions.Regex.Match(message, @"0x[0-9a-fA-F]+\s*\((\d+)\)");
            if (hexMatch.Success && int.TryParse(hexMatch.Groups[1].Value, out errorCode))
            {
                return true;
            }
            
            return false;
        }
        
        public string GetErrorMessage(int errorCode)
        {
            return ContractErrorMessages.GetErrorMessage(errorCode);
        }
        
        private async Task<bool> HandleInsufficientFunds(ThreadContext context)
        {
            _logger.LogWarning("Insufficient funds for {ThreadId}, requesting funding", context.ThreadId);
            
            try
            {
                // Check SOL balance - core wallet will handle funding if needed
                var solBalance = await _solanaClient.GetSolBalanceAsync(context.WalletAddress);
                if (solBalance < SolanaConfiguration.MIN_SOL_BALANCE)
                {
                    _logger.LogInformation("Thread {ThreadId} has insufficient SOL balance - core wallet will fund during operations", context.ThreadId);
                }
                
                // For deposit threads, check token balance and request minting if needed
                if (context.ThreadType == ThreadType.Deposit && context.AutoRefill && context.InitialAmount > 0)
                {
                    var tokenBalance = await GetTokenBalance(context);
                    var threshold = (ulong)(context.InitialAmount * SolanaConfiguration.AUTO_REFILL_THRESHOLD);
                    
                    if (tokenBalance < threshold)
                    {
                        _logger.LogInformation("Requesting token refill for {ThreadId}", context.ThreadId);
                        await RequestTokenRefill(context);
                    }
                }
                
                // Wait before retrying
                await Task.Delay(5000);
                return true; // Retry operation
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to handle insufficient funds for {ThreadId}", context.ThreadId);
                return false;
            }
        }
        
        private async Task<bool> HandlePoolPaused(ThreadContext context)
        {
            _logger.LogInformation("Pool {PoolId} is paused, waiting for unpause", context.PoolId);
            
            // Check pool pause status periodically
            var checkCount = 0;
            while (await _solanaClient.IsPoolPausedAsync(context.PoolId))
            {
                if (checkCount++ > 120) // Max 1 hour wait (120 * 30s)
                {
                    _logger.LogError("Pool {PoolId} pause timeout for thread {ThreadId}", context.PoolId, context.ThreadId);
                    return false;
                }
                
                await Task.Delay(30000); // Check every 30 seconds
                
                if (checkCount % 4 == 0) // Log every 2 minutes
                {
                    _logger.LogInformation("Still waiting for pool {PoolId} to unpause ({Minutes} minutes)", 
                        context.PoolId, checkCount / 2);
                }
            }
            
            _logger.LogInformation("Pool {PoolId} is now unpaused, resuming operations", context.PoolId);
            return true; // Retry operation
        }
        
        private async Task<bool> HandleSystemPaused(ThreadContext context)
        {
            _logger.LogInformation("System is paused, waiting for unpause");
            
            // Check system pause status periodically
            var checkCount = 0;
            while (await _solanaClient.IsSystemPausedAsync())
            {
                if (checkCount++ > 240) // Max 2 hours wait (240 * 30s)
                {
                    _logger.LogError("System pause timeout for thread {ThreadId}", context.ThreadId);
                    return false;
                }
                
                await Task.Delay(30000); // Check every 30 seconds
                
                if (checkCount % 4 == 0) // Log every 2 minutes
                {
                    _logger.LogInformation("Still waiting for system to unpause ({Minutes} minutes)", checkCount / 2);
                }
            }
            
            _logger.LogInformation("System is now unpaused, resuming operations");
            return true; // Retry operation
        }
        
        private async Task<bool> HandleInsufficientLiquidity(ThreadContext context)
        {
            _logger.LogWarning("Insufficient liquidity in pool {PoolId} for {ThreadId}", context.PoolId, context.ThreadId);
            
            // For withdrawal/swap threads, wait for liquidity
            if (context.ThreadType == ThreadType.Withdrawal || context.ThreadType == ThreadType.Swap)
            {
                await Task.Delay(10000); // Wait 10 seconds
                return true; // Retry
            }
            
            // For deposit threads, this shouldn't happen
            _logger.LogError("Unexpected insufficient liquidity error for deposit thread {ThreadId}", context.ThreadId);
            return false;
        }
        
        private async Task<bool> HandleSlippageExceeded(ThreadContext context)
        {
            _logger.LogWarning("Slippage exceeded for swap in thread {ThreadId}", context.ThreadId);
            
            // Increase slippage tolerance and retry
            context.SlippageTolerance = Math.Min(context.SlippageTolerance * 1.5, 0.1); // Max 10%
            _logger.LogInformation("Increased slippage tolerance to {Tolerance:P} for thread {ThreadId}", 
                context.SlippageTolerance, context.ThreadId);
            
            await Task.Delay(2000);
            return true; // Retry with higher tolerance
        }
        
        private async Task<bool> HandleInvalidTokenAccount(ThreadContext context)
        {
            _logger.LogError("Invalid token account for thread {ThreadId}", context.ThreadId);
            
            // This is likely a configuration error
            await RecordError(context, "Invalid token account - check pool configuration");
            return false; // Don't retry
        }
        
        private async Task<bool> HandlePoolSwapsPaused(ThreadContext context)
        {
            _logger.LogInformation("Pool swaps are paused for {PoolId}, waiting", context.PoolId);
            
            // Similar to pool pause, but specifically for swaps
            if (context.ThreadType != ThreadType.Swap)
            {
                // Non-swap threads can continue
                return true;
            }
            
            var checkCount = 0;
            while (await _solanaClient.ArePoolSwapsPausedAsync(context.PoolId))
            {
                if (checkCount++ > 60) // Max 30 minutes wait
                {
                    _logger.LogError("Pool swaps pause timeout for thread {ThreadId}", context.ThreadId);
                    return false;
                }
                
                await Task.Delay(30000);
            }
            
            return true; // Retry
        }
        
        private async Task<bool> HandleInvalidLpTokenType(ThreadContext context)
        {
            _logger.LogError("Invalid LP token type for withdrawal thread {ThreadId}", context.ThreadId);
            
            // This indicates LP token mismatch
            await RecordError(context, "LP token type mismatch - check token configuration");
            return false; // Don't retry
        }
        
        private async Task<bool> HandleUnknownError(int errorCode, ThreadContext context)
        {
            _logger.LogError("Unknown contract error {ErrorCode} for thread {ThreadId}", errorCode, context.ThreadId);
            
            await RecordError(context, $"Unknown contract error: {errorCode}");
            
            // For unknown errors, retry a few times
            if (context.RetryCount < 3)
            {
                context.RetryCount++;
                await Task.Delay(5000);
                return true;
            }
            
            return false;
        }
        
        private async Task<ulong> GetTokenBalance(ThreadContext context)
        {
            var pool = await _solanaClient.GetPoolStateAsync(context.PoolId);
            var tokenMint = context.TokenType == TokenType.A ? pool.TokenAMint : pool.TokenBMint;
            return await _solanaClient.GetTokenBalanceAsync(context.WalletAddress, tokenMint);
        }
        
        private async Task RequestTokenRefill(ThreadContext context)
        {
            var pool = await _solanaClient.GetPoolStateAsync(context.PoolId);
            var tokenMint = context.TokenType == TokenType.A ? pool.TokenAMint : pool.TokenBMint;
            
            _logger.LogInformation("Minting {Amount} tokens for thread {ThreadId}", 
                context.InitialAmount, context.ThreadId);
                
            await _solanaClient.MintTokensAsync(tokenMint, context.WalletAddress, context.InitialAmount);
        }
        
        private async Task RecordError(ThreadContext context, string errorMessage)
        {
            var error = new ThreadError
            {
                Timestamp = DateTime.UtcNow,
                ErrorMessage = errorMessage,
                OperationType = context.LastOperationType
            };
            
            await _storageService.AddThreadErrorAsync(context.ThreadId, error);
        }
    }
    
    /// <summary>
    /// Thread execution context for error handling
    /// </summary>
    public class ThreadContext
    {
        public string ThreadId { get; set; } = string.Empty;
        public ThreadType ThreadType { get; set; }
        public string PoolId { get; set; } = string.Empty;
        public TokenType TokenType { get; set; }
        public SwapDirection? SwapDirection { get; set; } // For swap threads
        public string WalletAddress { get; set; } = string.Empty;
        public Solnet.Wallet.Wallet Wallet { get; set; } = null!;
        public bool AutoRefill { get; set; }
        public ulong InitialAmount { get; set; }
        public bool ShareLpTokens { get; set; }
        public double SlippageTolerance { get; set; } = 0.01; // 1% default
        public int RetryCount { get; set; }
        public string LastOperationType { get; set; } = string.Empty;
        public ThreadStatistics Statistics { get; set; } = new();
        public ThreadStatus Status { get; set; }
    }
}
