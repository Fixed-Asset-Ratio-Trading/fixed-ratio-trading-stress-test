using Microsoft.Extensions.Logging;
using FixedRatioStressTest.Common.Models;
using FixedRatioStressTest.Core.Interfaces;

namespace FixedRatioStressTest.Core.Services
{
    /// <summary>
    /// Handles empty commands for resource cleanup with guaranteed token burning
    /// </summary>
    public interface IEmptyCommandHandler
    {
        Task<EmptyResult> ExecuteEmptyAsync(ThreadContext context);
        Task<EmptyResult> ExecuteEmptyCommandAsync(string threadId);
    }
    
    public class EmptyCommandHandler : IEmptyCommandHandler
    {
        private readonly ISolanaClientService _solanaClient;
        private readonly ITransactionBuilderService _transactionBuilder;
        private readonly IStorageService _storageService;
        private readonly ILogger<EmptyCommandHandler> _logger;
        
        public EmptyCommandHandler(
            ISolanaClientService solanaClient,
            ITransactionBuilderService transactionBuilder,
            IStorageService storageService,
            ILogger<EmptyCommandHandler> logger)
        {
            _solanaClient = solanaClient;
            _transactionBuilder = transactionBuilder;
            _storageService = storageService;
            _logger = logger;
        }

        public async Task<EmptyResult> ExecuteEmptyCommandAsync(string threadId)
        {
            // Load thread config and build context
            var config = await _storageService.LoadThreadConfigAsync(threadId);
            if (config == null)
            {
                return new EmptyResult
                {
                    ThreadId = threadId,
                    OperationSuccessful = false,
                    ErrorMessage = $"Thread {threadId} not found"
                };
            }

            var wallet = _solanaClient.RestoreWallet(config.PrivateKey);
            var context = new ThreadContext
            {
                ThreadId = config.ThreadId,
                ThreadType = config.ThreadType,
                PoolId = config.PoolId,
                TokenType = config.TokenType,
                SwapDirection = config.SwapDirection,
                WalletAddress = config.PublicKey,
                Wallet = wallet,
                AutoRefill = config.AutoRefill,
                InitialAmount = config.InitialAmount,
                ShareLpTokens = config.ShareTokens
            };

            return await ExecuteEmptyAsync(context);
        }
        
        public async Task<EmptyResult> ExecuteEmptyAsync(ThreadContext context)
        {
            var result = new EmptyResult
            {
                ThreadId = context.ThreadId,
                ThreadType = context.ThreadType.ToString(),
                OperationType = $"{context.ThreadType.ToString().ToLower()}_empty",
                ExecutedAt = DateTime.UtcNow
            };
            
            try
            {
                _logger.LogInformation("Executing empty command for thread {ThreadId} of type {ThreadType}", 
                    context.ThreadId, context.ThreadType);
                
                // Execute the specific empty operation
                EmptyResult specificResult;
                switch (context.ThreadType)
                {
                    case ThreadType.Deposit:
                        specificResult = await ExecuteDepositEmpty(context, result);
                        break;
                    case ThreadType.Withdrawal:
                        specificResult = await ExecuteWithdrawalEmpty(context, result);
                        break;
                    case ThreadType.Swap:
                        specificResult = await ExecuteSwapEmpty(context, result);
                        break;
                    default:
                        throw new InvalidOperationException($"Unknown thread type: {context.ThreadType}");
                }
                
                // Transfer remaining SOL back to core wallet
                await TransferSolToCoreWallet(context, specificResult);
                
                return specificResult;
            }
            catch (Exception ex)
            {
                result.OperationSuccessful = false;
                result.ErrorMessage = ex.Message;
                _logger.LogError(ex, "Empty command failed for thread {ThreadId}", context.ThreadId);
                return result;
            }
        }
        
        private async Task<EmptyResult> ExecuteDepositEmpty(ThreadContext context, EmptyResult result)
        {
            // Get current token balance
            var pool = await _solanaClient.GetPoolStateAsync(context.PoolId);
            var tokenMint = context.TokenType == TokenType.A ? pool.TokenAMint : pool.TokenBMint;
            var tokenBalance = await _solanaClient.GetTokenBalanceAsync(context.WalletAddress, tokenMint);
            
            result.TokensUsed = tokenBalance;
            
            if (tokenBalance == 0)
            {
                result.ErrorMessage = "No tokens available";
                _logger.LogInformation("No tokens to empty for deposit thread {ThreadId}", context.ThreadId);
                return result;
            }
            
            _logger.LogInformation("Emptying {Amount} tokens from deposit thread {ThreadId}", 
                tokenBalance, context.ThreadId);
            
            // Burn tokens first (guaranteed removal)
            await BurnTokens(context, tokenMint, tokenBalance);
            result.TokensBurned = tokenBalance;
            
            try
            {
                // Attempt deposit operation
                var depositResult = await _solanaClient.ExecuteDepositAsync(
                    context.Wallet,
                    context.PoolId,
                    context.TokenType,
                    tokenBalance);
                
                result.LpTokensReceived = depositResult.LpTokensReceived;
                result.TransactionSignature = depositResult.TransactionSignature;
                result.NetworkFeePaid = depositResult.NetworkFeePaid;
                result.OperationSuccessful = true;
                
                // Burn received LP tokens
                var lpMint = context.TokenType == TokenType.A ? pool.LpMintA : pool.LpMintB;
                await BurnTokens(context, lpMint, depositResult.LpTokensReceived);
                result.LpTokensBurned = depositResult.LpTokensReceived;
                
                _logger.LogInformation("Successfully emptied deposit thread {ThreadId}: burned {TokensBurned} tokens and {LpBurned} LP tokens",
                    context.ThreadId, result.TokensBurned, result.LpTokensBurned);
            }
            catch (Exception ex)
            {
                // Operation failed but tokens already burned
                result.ErrorMessage = $"Deposit failed: {ex.Message}";
                _logger.LogWarning("Deposit operation failed for empty command, but tokens were burned: {Error}", ex.Message);
            }
            
            return result;
        }
        
        private async Task<EmptyResult> ExecuteWithdrawalEmpty(ThreadContext context, EmptyResult result)
        {
            // Get current LP token balance
            var pool = await _solanaClient.GetPoolStateAsync(context.PoolId);
            var lpMint = context.TokenType == TokenType.A ? pool.LpMintA : pool.LpMintB;
            var lpTokenBalance = await _solanaClient.GetTokenBalanceAsync(context.WalletAddress, lpMint);
            
            result.LpTokensUsed = lpTokenBalance;
            
            if (lpTokenBalance == 0)
            {
                result.ErrorMessage = "No LP tokens available";
                _logger.LogInformation("No LP tokens to empty for withdrawal thread {ThreadId}", context.ThreadId);
                return result;
            }
            
            _logger.LogInformation("Emptying {Amount} LP tokens from withdrawal thread {ThreadId}", 
                lpTokenBalance, context.ThreadId);
            
            // Burn LP tokens first (guaranteed removal)
            await BurnTokens(context, lpMint, lpTokenBalance);
            result.LpTokensBurned = lpTokenBalance;
            
            try
            {
                // Attempt withdrawal operation
                var withdrawalResult = await _solanaClient.ExecuteWithdrawalAsync(
                    context.Wallet,
                    context.PoolId,
                    context.TokenType,
                    lpTokenBalance);
                
                result.TokensWithdrawn = withdrawalResult.TokensWithdrawn;
                result.TransactionSignature = withdrawalResult.TransactionSignature;
                result.NetworkFeePaid = withdrawalResult.NetworkFeePaid;
                result.OperationSuccessful = true;
                
                // Burn withdrawn tokens
                var tokenMint = context.TokenType == TokenType.A ? pool.TokenAMint : pool.TokenBMint;
                await BurnTokens(context, tokenMint, withdrawalResult.TokensWithdrawn);
                result.TokensBurned = withdrawalResult.TokensWithdrawn;
                
                _logger.LogInformation("Successfully emptied withdrawal thread {ThreadId}: burned {LpBurned} LP tokens and {TokensBurned} tokens",
                    context.ThreadId, result.LpTokensBurned, result.TokensBurned);
            }
            catch (Exception ex)
            {
                // Operation failed but LP tokens already burned
                result.ErrorMessage = $"Withdrawal failed: {ex.Message}";
                _logger.LogWarning("Withdrawal operation failed for empty command, but LP tokens were burned: {Error}", ex.Message);
            }
            
            return result;
        }
        
        private async Task<EmptyResult> ExecuteSwapEmpty(ThreadContext context, EmptyResult result)
        {
            // Get current input token balance
            var pool = await _solanaClient.GetPoolStateAsync(context.PoolId);
            var swapDirection = context.SwapDirection ?? SwapDirection.AToB;
            var inputMint = swapDirection == SwapDirection.AToB ? pool.TokenAMint : pool.TokenBMint;
            var outputMint = swapDirection == SwapDirection.AToB ? pool.TokenBMint : pool.TokenAMint;
            
            var inputBalance = await _solanaClient.GetTokenBalanceAsync(context.WalletAddress, inputMint);
            result.TokensSwappedIn = inputBalance;
            result.SwapDirection = swapDirection.ToString();
            
            if (inputBalance == 0)
            {
                result.ErrorMessage = "No input tokens available";
                _logger.LogInformation("No tokens to empty for swap thread {ThreadId}", context.ThreadId);
                return result;
            }
            
            _logger.LogInformation("Emptying {Amount} input tokens from swap thread {ThreadId} direction {Direction}", 
                inputBalance, context.ThreadId, swapDirection);
            
            // Burn input tokens first (guaranteed removal)
            await BurnTokens(context, inputMint, inputBalance);
            result.TokensBurned = inputBalance;
            
            try
            {
                // Calculate expected output
                var swapCalc = SwapCalculation.Calculate(pool, swapDirection, inputBalance);
                
                // Attempt swap operation
                var swapResult = await _solanaClient.ExecuteSwapAsync(
                    context.Wallet,
                    context.PoolId,
                    swapDirection,
                    inputBalance,
                    (ulong)(swapCalc.OutputAmount * 0.9)); // 10% slippage tolerance
                
                result.TokensSwappedOut = swapResult.OutputTokens;
                result.TransactionSignature = swapResult.TransactionSignature;
                result.NetworkFeePaid = swapResult.NetworkFeePaid;
                result.OperationSuccessful = true;
                
                // Burn output tokens
                await BurnTokens(context, outputMint, swapResult.OutputTokens);
                result.TokensBurned += swapResult.OutputTokens; // Total burned = input + output
                
                _logger.LogInformation("Successfully emptied swap thread {ThreadId}: burned {InputBurned} input and {OutputBurned} output tokens",
                    context.ThreadId, inputBalance, swapResult.OutputTokens);
            }
            catch (Exception ex)
            {
                // Operation failed but input tokens already burned
                result.ErrorMessage = $"Swap failed: {ex.Message}";
                _logger.LogWarning("Swap operation failed for empty command, but input tokens were burned: {Error}", ex.Message);
            }
            
            return result;
        }
        
        private async Task BurnTokens(ThreadContext context, string tokenMint, ulong amount)
        {
            if (amount == 0) return;
            
            try
            {
                _logger.LogDebug("Burning {Amount} tokens of mint {Mint} for thread {ThreadId}", 
                    amount, tokenMint, context.ThreadId);
                
                // In a real implementation, this would burn tokens
                // For Phase 3.5, we simulate by transferring to a burn address
                var burnAddress = "11111111111111111111111111111111"; // System program address
                
                await _solanaClient.TransferTokensAsync(
                    context.Wallet,
                    burnAddress,
                    tokenMint,
                    amount);
                
                _logger.LogDebug("Successfully burned {Amount} tokens", amount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to burn {Amount} tokens of mint {Mint}", amount, tokenMint);
                throw;
            }
        }
        
        private async Task TransferSolToCoreWallet(ThreadContext context, EmptyResult result)
        {
            try
            {
                // Get current SOL balance of the thread wallet
                var currentBalance = await _solanaClient.GetSolBalanceAsync(context.WalletAddress);
                
                // Reserve some SOL for transaction fees (0.01 SOL = 10,000,000 lamports)
                const ulong feeReserve = 10_000_000;
                
                if (currentBalance <= feeReserve)
                {
                    _logger.LogInformation("Thread {ThreadId} has insufficient SOL to transfer ({Balance} lamports, need > {Reserve})", 
                        context.ThreadId, currentBalance, feeReserve);
                    return;
                }
                
                var transferAmount = currentBalance - feeReserve;
                
                // Get core wallet address
                var coreWallet = await _solanaClient.GetOrCreateCoreWalletAsync();
                
                _logger.LogInformation("Transferring {Amount} lamports from thread {ThreadId} to core wallet", 
                    transferAmount, context.ThreadId);
                
                // Build and send SOL transfer transaction
                var transaction = await _transactionBuilder.BuildSolTransferTransactionAsync(
                    context.Wallet,
                    coreWallet.PublicKey,
                    transferAmount);
                
                var signature = await _solanaClient.SendTransactionAsync(transaction);
                
                result.SolReturned = transferAmount;
                _logger.LogInformation("Successfully transferred {Amount} lamports to core wallet. Signature: {Signature}", 
                    transferAmount, signature);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to transfer SOL to core wallet for thread {ThreadId}: {Error}", 
                    context.ThreadId, ex.Message);
                // Don't fail the entire empty operation if SOL transfer fails
            }
        }
    }
    
    /// <summary>
    /// Result of an empty command execution
    /// </summary>
    public class EmptyResult
    {
        public string ThreadId { get; set; } = string.Empty;
        public string ThreadType { get; set; } = string.Empty;
        public string OperationType { get; set; } = string.Empty;
        public bool OperationSuccessful { get; set; }
        public string? ErrorMessage { get; set; }
        public string? TransactionSignature { get; set; }
        public DateTime ExecutedAt { get; set; }
        
        // Amounts
        public ulong TokensUsed { get; set; }
        public ulong TokensBurned { get; set; }
        public ulong LpTokensReceived { get; set; }
        public ulong LpTokensUsed { get; set; }
        public ulong LpTokensBurned { get; set; }
        public ulong TokensWithdrawn { get; set; }
        public ulong TokensSwappedIn { get; set; }
        public ulong TokensSwappedOut { get; set; }
        public ulong NetworkFeePaid { get; set; }
        public ulong SolReturned { get; set; }
        
        // Additional info
        public string? SwapDirection { get; set; }
        
        public static EmptyResult NoTokensAvailable()
        {
            return new EmptyResult
            {
                OperationSuccessful = false,
                ErrorMessage = "No tokens available"
            };
        }
        
        public static EmptyResult NoLpTokensAvailable()
        {
            return new EmptyResult
            {
                OperationSuccessful = false,
                ErrorMessage = "No LP tokens available"
            };
        }
        
        public static EmptyResult NoInputTokensAvailable()
        {
            return new EmptyResult
            {
                OperationSuccessful = false,
                ErrorMessage = "No input tokens available"  
            };
        }
    }
}
