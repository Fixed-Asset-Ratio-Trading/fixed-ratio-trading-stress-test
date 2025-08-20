using System.Collections.Concurrent;
using FixedRatioStressTest.Common.Models;
using FixedRatioStressTest.Core.Interfaces;
using Microsoft.Extensions.Logging;
using Solnet.Wallet;

namespace FixedRatioStressTest.Core.Services;

public class ThreadManager : IThreadManager
{
    private readonly IStorageService _storageService;
    private readonly ISolanaClientService _solanaClient;
    private readonly ITransactionBuilderService _transactionBuilder;
    private readonly ILogger<ThreadManager> _logger;
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _runningThreads;
    private readonly ConcurrentDictionary<string, Wallet> _walletCache;

    public ThreadManager(
        IStorageService storageService, 
        ISolanaClientService solanaClient,
        ITransactionBuilderService transactionBuilder,
        ILogger<ThreadManager> logger)
    {
        _storageService = storageService;
        _solanaClient = solanaClient;
        _transactionBuilder = transactionBuilder;
        _logger = logger;
        _runningThreads = new ConcurrentDictionary<string, CancellationTokenSource>();
        _walletCache = new ConcurrentDictionary<string, Wallet>();
    }

    public async Task<string> CreateThreadAsync(ThreadConfig config)
    {
        // CRITICAL FIX: Validate pool exists before creating thread
        if (string.IsNullOrEmpty(config.PoolId))
        {
            throw new InvalidOperationException("PoolId is required when creating a thread");
        }

        try
        {
            var poolState = await _solanaClient.GetPoolStateAsync(config.PoolId);
            _logger.LogDebug("Validated pool {PoolId} exists for thread creation", config.PoolId);
        }
        catch (KeyNotFoundException)
        {
            throw new InvalidOperationException($"Pool {config.PoolId} does not exist. Cannot create thread without a valid pool.");
        }

        // Generate short ID: <type>-<8chars> (<= 12 chars total requirement)
        var shortSuffix = Guid.NewGuid().ToString("N").Substring(0, 8);
        var typePrefix = config.ThreadType switch
        {
            ThreadType.Deposit => "dep",
            ThreadType.Withdrawal => "wd",
            ThreadType.Swap => "sw",
            _ => "th"
        };
        config.ThreadId = $"{typePrefix}-{shortSuffix}";
        config.CreatedAt = DateTime.UtcNow;
        config.Status = ThreadStatus.Created;

        // Phase 2: Generate Solana wallet for the thread
        var wallet = _solanaClient.GenerateWallet();
        config.PublicKey = wallet.Account.PublicKey.Key;
        config.PrivateKey = wallet.Account.PrivateKey.KeyBytes;
        config.WalletMnemonic = wallet.Mnemonic?.ToString() ?? ""; // Handle null mnemonic gracefully

        _logger.LogDebug("Generated wallet for thread {ThreadId}: {PublicKey} for pool {PoolId}", 
            config.ThreadId, config.PublicKey, config.PoolId);

        await _storageService.SaveThreadConfigAsync(config.ThreadId, config);

        var statistics = new ThreadStatistics();
        await _storageService.SaveThreadStatisticsAsync(config.ThreadId, statistics);

        _logger.LogDebug("Created thread {ThreadId} of type {ThreadType} for pool {PoolId}",
            config.ThreadId, config.ThreadType, config.PoolId);

        return config.ThreadId;
    }

    public async Task StartThreadAsync(string threadId)
    {
        var config = await _storageService.LoadThreadConfigAsync(threadId);

        if (config.Status == ThreadStatus.Running)
        {
            throw new InvalidOperationException($"Thread {threadId} is already running");
        }

        // Phase 2: Restore wallet and check initial balance
        // Phase 3: Cache wallet for operations
        if (config.PrivateKey != null && config.PublicKey != null)
        {
            var wallet = _solanaClient.RestoreWallet(config.PrivateKey);
            _walletCache[threadId] = wallet;
            
            var solBalance = await _solanaClient.GetSolBalanceAsync(config.PublicKey);
            
            // Phase 3: Request airdrop if balance is low (devnet/localnet only)
            if (solBalance < 100000000) // Less than 0.1 SOL
            {
                var airdropSignature = await _solanaClient.RequestAirdropAsync(config.PublicKey, 1000000000); // 1 SOL
                var airdropSuccess = !string.IsNullOrEmpty(airdropSignature);
                if (airdropSuccess)
                {
                    _logger.LogDebug("Requested SOL airdrop for thread {ThreadId}", threadId);
                }
            }
            
            _logger.LogDebug("Restored wallet for thread {ThreadId}: {PublicKey}, SOL balance: {Balance} lamports", 
                threadId, config.PublicKey, solBalance);
                
            // Phase 4: Initial token funding for deposit threads
            if (config.ThreadType == ThreadType.Deposit && config.InitialAmount > 0)
            {
                await FundDepositThreadInitially(config, wallet);
            }
        }

        var cancellationToken = new CancellationTokenSource();
        _runningThreads[threadId] = cancellationToken;

        _ = Task.Run(async () => await RunWorkerThread(config, cancellationToken.Token));

        config.Status = ThreadStatus.Running;
        await _storageService.SaveThreadConfigAsync(threadId, config);

        _logger.LogDebug("Started thread {ThreadId}", threadId);
    }

    public async Task StopThreadAsync(string threadId)
    {
        if (_runningThreads.TryRemove(threadId, out var cts))
        {
            cts.Cancel();
        }

        var config = await _storageService.LoadThreadConfigAsync(threadId);
        config.Status = ThreadStatus.Stopped;
        await _storageService.SaveThreadConfigAsync(threadId, config);

        _logger.LogDebug("Stopped thread {ThreadId}", threadId);
    }

    public async Task<int> StopAllThreadsForPoolAsync(string poolId, bool includeSwaps = false)
    {
        var all = await _storageService.LoadAllThreadsAsync();
        var toStop = all.Where(t => t.PoolId == poolId && (includeSwaps || t.ThreadType != ThreadType.Swap)).ToList();
        int stopped = 0;
        foreach (var t in toStop)
        {
            try
            {
                await StopThreadAsync(t.ThreadId);
                stopped++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to stop thread {ThreadId} for pool {PoolId}", t.ThreadId, poolId);
            }
        }
        return stopped;
    }

    public async Task DeleteThreadAsync(string threadId)
    {
        await StopThreadAsync(threadId);
        _logger.LogDebug("Deleted thread {ThreadId}", threadId);
    }

    public Task<ThreadConfig> GetThreadConfigAsync(string threadId)
        => _storageService.LoadThreadConfigAsync(threadId);

    public Task<List<ThreadConfig>> GetAllThreadsAsync()
        => _storageService.LoadAllThreadsAsync();

    public Task<ThreadStatistics> GetThreadStatisticsAsync(string threadId)
        => _storageService.LoadThreadStatisticsAsync(threadId);

    private async Task RunWorkerThread(ThreadConfig config, CancellationToken cancellationToken)
    {
        var random = new Random();
        var wallet = _walletCache.TryGetValue(config.ThreadId, out var cachedWallet) ? cachedWallet : null;

        if (wallet == null)
        {
            _logger.LogError("No wallet found for thread {ThreadId}", config.ThreadId);
            return;
        }

        _logger.LogDebug("Starting {ThreadType} worker thread {ThreadId} for pool {PoolId}", 
            config.ThreadType, config.ThreadId, config.PoolId);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var operationType = "unknown";
                var operationSuccess = false;
                var volumeProcessed = 0ul;

                // Phase 3: Implement actual blockchain operations based on thread type
                switch (config.ThreadType)
                {
                    case ThreadType.Deposit:
                        (operationType, operationSuccess, volumeProcessed) = await HandleDepositOperation(config, wallet, random);
                        break;
                        
                    case ThreadType.Withdrawal:
                        (operationType, operationSuccess, volumeProcessed) = await HandleWithdrawalOperation(config, wallet, random);
                        break;
                        
                    case ThreadType.Swap:
                        (operationType, operationSuccess, volumeProcessed) = await HandleSwapOperation(config, wallet, random);
                        break;
                        
                    default:
                        _logger.LogWarning("Unknown thread type {ThreadType} for thread {ThreadId}", 
                            config.ThreadType, config.ThreadId);
                        break;
                }

                // Update statistics
                var statistics = await _storageService.LoadThreadStatisticsAsync(config.ThreadId);
                
                if (operationSuccess)
                {
                    statistics.SuccessfulOperations++;
                    statistics.TotalVolumeProcessed += volumeProcessed;
                }
                else
                {
                    statistics.FailedOperations++;
                }
                
                statistics.LastOperationAt = DateTime.UtcNow;
                await _storageService.SaveThreadStatisticsAsync(config.ThreadId, statistics);

                // Random delay between operations (750-2000ms as per design)
                await Task.Delay(random.Next(750, 2000), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                _logger.LogDebug("Thread {ThreadId} operation cancelled", config.ThreadId);
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in worker thread {ThreadId}", config.ThreadId);

                var error = new ThreadError
                {
                    Timestamp = DateTime.UtcNow,
                    ErrorMessage = ex.Message,
                    OperationType = "thread_operation"
                };

                await _storageService.AddThreadErrorAsync(config.ThreadId, error);

                // Wait before retrying on unexpected errors
                await Task.Delay(5000, cancellationToken);
            }
        }

        _logger.LogDebug("Worker thread {ThreadId} completed", config.ThreadId);
    }

    private async Task FundDepositThreadInitially(ThreadConfig config, Wallet wallet)
    {
        try
        {
            _logger.LogDebug("Providing initial funding for deposit thread {ThreadId}: {Amount} basis points", 
                config.ThreadId, config.InitialAmount);

            // Get pool state to determine token mint
            var pool = await _solanaClient.GetPoolStateAsync(config.PoolId);
            var tokenMint = config.TokenType == TokenType.A ? pool.TokenAMint : pool.TokenBMint;

            // Mint initial tokens to the thread's wallet
            await _solanaClient.MintTokensAsync(tokenMint, wallet.Account.PublicKey.Key, config.InitialAmount);
            
            _logger.LogInformation("Successfully funded deposit thread {ThreadId} with {Amount} tokens of type {TokenType}", 
                config.ThreadId, config.InitialAmount, config.TokenType);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to provide initial funding for deposit thread {ThreadId}", config.ThreadId);
            // Don't throw - let the thread start and wait for token sharing instead
        }
    }

    private async Task CheckAndExecuteAutoRefill(ThreadConfig config, Wallet wallet, string tokenMint)
    {
        try
        {
            // Only auto-refill if enabled and initial amount was set
            if (!config.AutoRefill || config.InitialAmount == 0)
                return;

            var currentBalance = await _solanaClient.GetTokenBalanceAsync(wallet.Account.PublicKey.Key, tokenMint);
            var threshold = config.InitialAmount * 5 / 100; // 5% of initial amount

            if (currentBalance < threshold)
            {
                _logger.LogDebug("Auto-refill triggered for thread {ThreadId}: balance {Current} < threshold {Threshold}", 
                    config.ThreadId, currentBalance, threshold);

                // Mint full initial amount again (not just the deficit)
                await _solanaClient.MintTokensAsync(tokenMint, wallet.Account.PublicKey.Key, config.InitialAmount);
                
                _logger.LogInformation("Auto-refilled deposit thread {ThreadId} with {Amount} tokens", 
                    config.ThreadId, config.InitialAmount);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to execute auto-refill for deposit thread {ThreadId}", config.ThreadId);
            // Don't throw - let the thread continue with current balance
        }
    }

    private async Task<(string operationType, bool success, ulong volume)> HandleDepositOperation(
        ThreadConfig config, Wallet wallet, Random random)
    {
        try
        {
            _logger.LogDebug("Executing deposit operation for thread {ThreadId}", config.ThreadId);

            // Check SOL balance for transaction fees
            var solBalance = await _solanaClient.GetSolBalanceAsync(wallet.Account.PublicKey.Key);
            if (solBalance < 1000000) // Less than 0.001 SOL for fees
            {
                _logger.LogWarning("Insufficient SOL balance for fees in thread {ThreadId}", config.ThreadId);
                return ("deposit_insufficient_sol", false, 0);
            }

            // Get pool state and determine token mint
            var pool = await _solanaClient.GetPoolStateAsync(config.PoolId);
            var tokenMint = config.TokenType == TokenType.A ? pool.TokenAMint : pool.TokenBMint;
            
            // Check for auto-refill before checking balance
            await CheckAndExecuteAutoRefill(config, wallet, tokenMint);
            
            // Check actual token balance for deposit
            var tokenBalance = await _solanaClient.GetTokenBalanceAsync(wallet.Account.PublicKey.Key, tokenMint);
            if (tokenBalance == 0)
            {
                _logger.LogDebug("No tokens available for deposit in thread {ThreadId}, waiting...", config.ThreadId);
                return ("deposit_waiting", false, 0);
            }

            // Calculate random deposit amount (1 basis point to 5% of token balance)
            ulong maxPortion = (ulong)Math.Max(1001, (long)(tokenBalance * 5 / 100));
            var upper = (int)Math.Min(int.MaxValue, maxPortion);
            var depositAmount = (ulong)random.Next(1000, Math.Max(1001, upper));
            
            // Ensure we don't try to deposit more than available
            depositAmount = Math.Min(depositAmount, tokenBalance);
            
            // Submit deposit transaction
            var result = await _solanaClient.ExecuteDepositAsync(
                wallet, config.PoolId, config.TokenType, depositAmount);
            var signature = result.TransactionSignature;

            _logger.LogDebug("Deposit completed for thread {ThreadId}: {Amount} tokens, signature: {Signature}", 
                config.ThreadId, depositAmount, signature);

            // Share LP tokens with withdrawal threads if sharing is enabled
            if (config.ShareTokens && result.LpTokensReceived > 0)
            {
                await ShareLpTokensWithWithdrawalThreads(config, wallet, result.LpTokensReceived);
            }

            return ("deposit", true, depositAmount);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Deposit operation failed for thread {ThreadId}", config.ThreadId);
            return ("deposit_failed", false, 0);
        }
    }

    private async Task<(string operationType, bool success, ulong volume)> HandleWithdrawalOperation(
        ThreadConfig config, Wallet wallet, Random random)
    {
        try
        {
            _logger.LogDebug("Executing withdrawal operation for thread {ThreadId}", config.ThreadId);

            // Check SOL balance for transaction fees
            var solBalance = await _solanaClient.GetSolBalanceAsync(wallet.Account.PublicKey.Key);
            if (solBalance < 1000000) // Less than 0.001 SOL for fees
            {
                _logger.LogWarning("Insufficient SOL balance for fees in thread {ThreadId}", config.ThreadId);
                return ("withdrawal_insufficient_sol", false, 0);
            }

            // Get pool state and determine LP mint
            var pool = await _solanaClient.GetPoolStateAsync(config.PoolId);
            var lpMint = config.TokenType == TokenType.A ? pool.LpMintA : pool.LpMintB;
            
            // Check actual LP token balance for withdrawal
            var lpBalance = await _solanaClient.GetTokenBalanceAsync(wallet.Account.PublicKey.Key, lpMint);
            if (lpBalance == 0)
            {
                _logger.LogDebug("No LP tokens available for withdrawal in thread {ThreadId}, waiting...", config.ThreadId);
                return ("withdrawal_waiting", false, 0);
            }

            // Calculate random withdrawal amount (1 basis point to 5% of LP balance)
            ulong maxPortion = (ulong)Math.Max(1001, (long)(lpBalance * 5 / 100));
            var upper = (int)Math.Min(int.MaxValue, maxPortion);
            var lpTokenAmount = (ulong)random.Next(1000, Math.Max(1001, upper));
            
            // Ensure we don't try to withdraw more than available
            lpTokenAmount = Math.Min(lpTokenAmount, lpBalance);
            
            // Submit withdrawal transaction
            var result = await _solanaClient.ExecuteWithdrawalAsync(
                wallet, config.PoolId, config.TokenType, lpTokenAmount);
            var signature = result.TransactionSignature;

            _logger.LogDebug("Withdrawal completed for thread {ThreadId}: {Amount} LP tokens, signature: {Signature}", 
                config.ThreadId, lpTokenAmount, signature);

            // Share withdrawn tokens with deposit threads if sharing is enabled
            if (config.ShareTokens && result.TokensWithdrawn > 0)
            {
                await ShareTokensWithDepositThreads(config, wallet, result.TokensWithdrawn);
            }

            return ("withdrawal", true, lpTokenAmount);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Withdrawal operation failed for thread {ThreadId}", config.ThreadId);
            return ("withdrawal_failed", false, 0);
        }
    }

    private async Task<(string operationType, bool success, ulong volume)> HandleSwapOperation(
        ThreadConfig config, Wallet wallet, Random random)
    {
        try
        {
            // Phase 3: Implement swap logic
            _logger.LogDebug("Executing swap operation for thread {ThreadId}", config.ThreadId);

            // Check balance for input token
            var solBalance = await _solanaClient.GetSolBalanceAsync(wallet.Account.PublicKey.Key);
            if (solBalance < 1000000) // Less than 0.001 SOL for fees
            {
                _logger.LogWarning("Insufficient balance for swap operation in thread {ThreadId}", config.ThreadId);
                return ("swap_insufficient_balance", false, 0);
            }

            // Calculate swap amount (up to 2% of balance as per design)
            var swapAmount = (ulong)random.Next(1000, Math.Max(1001, (int)(solBalance * 0.02)));
            var minimumOutput = swapAmount * 95 / 100; // 5% slippage tolerance
            
            // Submit swap transaction
            var result = await _solanaClient.ExecuteSwapAsync(
                wallet, config.PoolId, config.SwapDirection ?? SwapDirection.AToB, swapAmount, minimumOutput);
            var signature = result.TransactionSignature;

            _logger.LogDebug("Swap completed for thread {ThreadId}: {Amount} input, direction: {Direction}, signature: {Signature}", 
                config.ThreadId, swapAmount, config.SwapDirection, signature);

            return ("swap", true, swapAmount);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Swap operation failed for thread {ThreadId}", config.ThreadId);
            return ("swap_failed", false, 0);
        }
    }

    private async Task ShareLpTokensWithWithdrawalThreads(ThreadConfig sourceConfig, Wallet sourceWallet, ulong lpTokensToShare)
    {
        try
        {
            // Find active withdrawal threads for the same pool and token type
            var allThreads = await _storageService.LoadAllThreadsAsync();
            var eligibleThreads = allThreads.Where(t => 
                t.ThreadType == ThreadType.Withdrawal &&
                t.PoolId == sourceConfig.PoolId &&
                t.TokenType == sourceConfig.TokenType &&
                t.Status == ThreadStatus.Running &&
                t.ThreadId != sourceConfig.ThreadId).ToList();

            if (!eligibleThreads.Any())
            {
                _logger.LogDebug("No eligible withdrawal threads found for LP token sharing from {ThreadId}", sourceConfig.ThreadId);
                return;
            }

            // Calculate equal share for each thread
            var sharePerThread = lpTokensToShare / (ulong)eligibleThreads.Count;
            if (sharePerThread == 0)
            {
                _logger.LogDebug("LP token amount too small to share among {Count} withdrawal threads", eligibleThreads.Count);
                return;
            }

            // Get pool state to determine LP mint
            var pool = await _solanaClient.GetPoolStateAsync(sourceConfig.PoolId);
            var lpMint = sourceConfig.TokenType == TokenType.A ? pool.LpMintA : pool.LpMintB;

            // Transfer LP tokens to each eligible withdrawal thread
            foreach (var targetThread in eligibleThreads)
            {
                try
                {
                    if (targetThread.PublicKey != null)
                    {
                        await _solanaClient.TransferTokensAsync(
                            sourceWallet, targetThread.PublicKey, lpMint, sharePerThread);
                        
                        _logger.LogDebug("Shared {Amount} LP tokens from deposit thread {Source} to withdrawal thread {Target}", 
                            sharePerThread, sourceConfig.ThreadId, targetThread.ThreadId);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to share LP tokens with withdrawal thread {ThreadId}", targetThread.ThreadId);
                }
            }

            _logger.LogInformation("Shared {Total} LP tokens among {Count} withdrawal threads from deposit thread {ThreadId}", 
                lpTokensToShare, eligibleThreads.Count, sourceConfig.ThreadId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to share LP tokens from deposit thread {ThreadId}", sourceConfig.ThreadId);
        }
    }

    private async Task ShareTokensWithDepositThreads(ThreadConfig sourceConfig, Wallet sourceWallet, ulong tokensToShare)
    {
        try
        {
            // Find active deposit threads for the same pool and token type
            var allThreads = await _storageService.LoadAllThreadsAsync();
            var eligibleThreads = allThreads.Where(t => 
                t.ThreadType == ThreadType.Deposit &&
                t.PoolId == sourceConfig.PoolId &&
                t.TokenType == sourceConfig.TokenType &&
                t.Status == ThreadStatus.Running &&
                t.ThreadId != sourceConfig.ThreadId).ToList();

            if (!eligibleThreads.Any())
            {
                _logger.LogDebug("No eligible deposit threads found for token sharing from {ThreadId}", sourceConfig.ThreadId);
                return;
            }

            // Calculate equal share for each thread
            var sharePerThread = tokensToShare / (ulong)eligibleThreads.Count;
            if (sharePerThread == 0)
            {
                _logger.LogDebug("Token amount too small to share among {Count} deposit threads", eligibleThreads.Count);
                return;
            }

            // Get pool state to determine token mint
            var pool = await _solanaClient.GetPoolStateAsync(sourceConfig.PoolId);
            var tokenMint = sourceConfig.TokenType == TokenType.A ? pool.TokenAMint : pool.TokenBMint;

            // Transfer tokens to each eligible deposit thread
            foreach (var targetThread in eligibleThreads)
            {
                try
                {
                    if (targetThread.PublicKey != null)
                    {
                        await _solanaClient.TransferTokensAsync(
                            sourceWallet, targetThread.PublicKey, tokenMint, sharePerThread);
                        
                        _logger.LogDebug("Shared {Amount} tokens from withdrawal thread {Source} to deposit thread {Target}", 
                            sharePerThread, sourceConfig.ThreadId, targetThread.ThreadId);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to share tokens with deposit thread {ThreadId}", targetThread.ThreadId);
                }
            }

            _logger.LogInformation("Shared {Total} tokens among {Count} deposit threads from withdrawal thread {ThreadId}", 
                tokensToShare, eligibleThreads.Count, sourceConfig.ThreadId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to share tokens from withdrawal thread {ThreadId}", sourceConfig.ThreadId);
        }
    }
}

