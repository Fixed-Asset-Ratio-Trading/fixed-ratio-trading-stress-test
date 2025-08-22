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
    private readonly IContractErrorHandler _contractErrorHandler;
    private readonly ILogger<ThreadManager> _logger;
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _runningThreads;
    private readonly ConcurrentDictionary<string, Wallet> _walletCache;

    public ThreadManager(
        IStorageService storageService, 
        ISolanaClientService solanaClient,
        ITransactionBuilderService transactionBuilder,
        IContractErrorHandler contractErrorHandler,
        ILogger<ThreadManager> logger)
    {
        _storageService = storageService;
        _solanaClient = solanaClient;
        _transactionBuilder = transactionBuilder;
        _contractErrorHandler = contractErrorHandler;
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

        // Recover from stale Running status on restart: if status says Running but no worker exists, allow start
        if (config.Status == ThreadStatus.Running)
        {
            if (!_runningThreads.ContainsKey(threadId))
            {
                _logger.LogWarning("Thread {ThreadId} marked Running but no worker found. Recovering by restarting.", threadId);
            }
            else
            {
                throw new InvalidOperationException($"Thread {threadId} is already running");
            }
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
        // First stop the thread if it's running
        await StopThreadAsync(threadId);
        
        // Remove from wallet cache
        _walletCache.TryRemove(threadId, out _);
        
        // Actually delete the thread from storage
        await _storageService.DeleteThreadConfigAsync(threadId);
        
        _logger.LogDebug("Deleted thread {ThreadId} from storage and cache", threadId);
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

                // If the operation failed, stop the thread immediately
                if (!operationSuccess)
                {
                    _logger.LogError("Operation {OperationType} failed in thread {ThreadId}. Stopping thread.", operationType, config.ThreadId);
                    await StopThreadAsync(config.ThreadId);
                    break;
                }

                // Update statistics
                var statistics = await _storageService.LoadThreadStatisticsAsync(config.ThreadId);
                
                statistics.SuccessfulOperations++;
                statistics.TotalVolumeProcessed += volumeProcessed;
                
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
                _logger.LogError(ex, "Error in worker thread {ThreadId}; stopping thread", config.ThreadId);

                // Record the error
                var error = new ThreadError
                {
                    Timestamp = DateTime.UtcNow,
                    ErrorMessage = ex.Message,
                    OperationType = "thread_operation"
                };
                await _storageService.AddThreadErrorAsync(config.ThreadId, error);

                // Use existing stop logic to cancel and persist state
                await StopThreadAsync(config.ThreadId);

                // Exit the loop
                break;
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

            // Ensure ATA exists before minting
            await _solanaClient.EnsureAtaExistsAsync(wallet, tokenMint);
            _logger.LogDebug("Ensured ATA exists for thread {ThreadId} and mint {TokenMint}", config.ThreadId, tokenMint);

            // Mint initial tokens to the thread's wallet
            await _solanaClient.MintTokensAsync(tokenMint, wallet.Account.PublicKey.Key, config.InitialAmount);
            
            // Wait for tokens to be visible on blockchain with retry logic
            var actualBalance = await _solanaClient.GetTokenBalanceWithRetryAsync(
                wallet.Account.PublicKey.Key, tokenMint, config.InitialAmount, maxRetries: 5);
            
            _logger.LogInformation("Successfully funded deposit thread {ThreadId} with {Amount} tokens of type {TokenType} (verified balance: {ActualBalance})", 
                config.ThreadId, config.InitialAmount, config.TokenType, actualBalance);
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

                // Ensure ATA exists before minting
                await _solanaClient.EnsureAtaExistsAsync(wallet, tokenMint);
                
                // Mint full initial amount again (not just the deficit)
                await _solanaClient.MintTokensAsync(tokenMint, wallet.Account.PublicKey.Key, config.InitialAmount);
                
                // Wait for tokens to be visible on blockchain with retry logic
                var actualBalance = await _solanaClient.GetTokenBalanceWithRetryAsync(
                    wallet.Account.PublicKey.Key, tokenMint, config.InitialAmount, maxRetries: 5);
                
                _logger.LogInformation("Auto-refilled deposit thread {ThreadId} with {Amount} tokens (verified balance: {ActualBalance})", 
                    config.ThreadId, config.InitialAmount, actualBalance);
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
            if (solBalance < 100000000) // Less than 0.1 SOL for fees
            {
                _logger.LogWarning("Insufficient SOL balance ({Balance} SOL) for fees in thread {ThreadId}, requesting SOL transfer", 
                    solBalance / 1_000_000_000.0, config.ThreadId);
                var transferred = await TransferSolFromCoreWallet(wallet.Account.PublicKey.Key);
                if (!transferred)
                {
                    return ("deposit_insufficient_sol", false, 0);
                }
                // Re-check balance after transfer
                solBalance = await _solanaClient.GetSolBalanceAsync(wallet.Account.PublicKey.Key);
                _logger.LogInformation("SOL balance after transfer: {Balance} SOL for thread {ThreadId}", 
                    solBalance / 1_000_000_000.0, config.ThreadId);
            }

            // Get pool state and determine token mint
            var pool = await _solanaClient.GetPoolStateAsync(config.PoolId);
            var tokenMint = config.TokenType == TokenType.A ? pool.TokenAMint : pool.TokenBMint;
            var lpMint = config.TokenType == TokenType.A ? pool.LpMintA : pool.LpMintB;
            
            // Ensure ATAs exist for both token and LP token
            await _solanaClient.EnsureAtaExistsAsync(wallet, tokenMint);
            await _solanaClient.EnsureAtaExistsAsync(wallet, lpMint);
            
            // Check for auto-refill before checking balance
            await CheckAndExecuteAutoRefill(config, wallet, tokenMint);
            
            // Cooldown to avoid immediate re-triggering while chain converges
            await Task.Delay(500);
            
            // Check actual token balance for deposit with retry logic (in case auto-refill just happened)
            var tokenBalance = await _solanaClient.GetTokenBalanceWithRetryAsync(wallet.Account.PublicKey.Key, tokenMint, expectedMinimum: 1, maxRetries: 3);
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
            // Check if this is a contract error that should stop the thread
            bool isContractError = _contractErrorHandler.TryParseContractError(ex, out _);
            bool isTransactionFailure = ex.Message.Contains("custom program error") || 
                                      ex.Message.Contains("Transaction simulation failed") ||
                                      ex.Message.Contains("Transaction failed");
            
            if (isContractError || isTransactionFailure)
            {
                // Mark stopped and cancel the running thread to stop immediately
                config.Status = ThreadStatus.Stopped;
                await _storageService.SaveThreadConfigAsync(config.ThreadId, config);
                
                if (_runningThreads.TryGetValue(config.ThreadId, out var cts))
                {
                    try { cts.Cancel(); } catch { /* ignore */ }
                }
                _runningThreads.TryRemove(config.ThreadId, out _);
                
                // Re-throw to let the main error handler stop the thread
                throw;
            }
            
            // For other errors, just log and return failure
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
            if (solBalance < 100000000) // Less than 0.1 SOL for fees
            {
                _logger.LogWarning("Insufficient SOL balance ({Balance} SOL) for fees in thread {ThreadId}, requesting SOL transfer", 
                    solBalance / 1_000_000_000.0, config.ThreadId);
                var transferred = await TransferSolFromCoreWallet(wallet.Account.PublicKey.Key);
                if (!transferred)
                {
                    return ("withdrawal_insufficient_sol", false, 0);
                }
                // Re-check balance after transfer
                solBalance = await _solanaClient.GetSolBalanceAsync(wallet.Account.PublicKey.Key);
                _logger.LogInformation("SOL balance after transfer: {Balance} SOL for thread {ThreadId}", 
                    solBalance / 1_000_000_000.0, config.ThreadId);
            }

            // Get pool state and determine LP mint
            var pool = await _solanaClient.GetPoolStateAsync(config.PoolId);
            var lpMint = config.TokenType == TokenType.A ? pool.LpMintA : pool.LpMintB;
            var tokenMint = config.TokenType == TokenType.A ? pool.TokenAMint : pool.TokenBMint;
            
            // Ensure ATAs exist for both LP token and regular token
            await _solanaClient.EnsureAtaExistsAsync(wallet, lpMint);
            await _solanaClient.EnsureAtaExistsAsync(wallet, tokenMint);
            
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
            // Check if this is a contract error that should stop the thread
            bool isContractError = _contractErrorHandler.TryParseContractError(ex, out _);
            bool isTransactionFailure = ex.Message.Contains("custom program error") || 
                                      ex.Message.Contains("Transaction simulation failed") ||
                                      ex.Message.Contains("Transaction failed");
            
            if (isContractError || isTransactionFailure)
            {
                // Mark stopped and cancel the running thread to stop immediately
                config.Status = ThreadStatus.Stopped;
                await _storageService.SaveThreadConfigAsync(config.ThreadId, config);
                
                if (_runningThreads.TryGetValue(config.ThreadId, out var cts))
                {
                    try { cts.Cancel(); } catch { /* ignore */ }
                }
                _runningThreads.TryRemove(config.ThreadId, out _);
                
                // Re-throw to let the main error handler stop the thread
                throw;
            }
            
            // For other errors, just log and return failure
            _logger.LogWarning(ex, "Withdrawal operation failed for thread {ThreadId}", config.ThreadId);
            return ("withdrawal_failed", false, 0);
        }
    }

    private async Task<(string operationType, bool success, ulong volume)> HandleSwapOperation(
        ThreadConfig config, Wallet wallet, Random random)
    {
        try
        {
            _logger.LogInformation("🎯 STARTING SWAP OPERATION - Thread {ThreadId}, direction: {Direction}, initialAmount: {InitialAmount}, autoRefill: {AutoRefill}", 
                config.ThreadId, config.SwapDirection, config.InitialAmount, config.AutoRefill);

            // Check SOL balance for transaction fees
            var solBalance = await _solanaClient.GetSolBalanceAsync(wallet.Account.PublicKey.Key);
            if (solBalance < 100000000) // Less than 0.1 SOL for fees
            {
                _logger.LogWarning("Insufficient SOL balance ({Balance} SOL) for fees in thread {ThreadId}, requesting SOL transfer", 
                    solBalance / 1_000_000_000.0, config.ThreadId);
                var transferred = await TransferSolFromCoreWallet(wallet.Account.PublicKey.Key);
                if (!transferred)
                {
                    return ("swap_insufficient_sol", false, 0);
                }
                // Re-check balance after transfer
                solBalance = await _solanaClient.GetSolBalanceAsync(wallet.Account.PublicKey.Key);
                _logger.LogInformation("SOL balance after transfer: {Balance} SOL for thread {ThreadId}", 
                    solBalance / 1_000_000_000.0, config.ThreadId);
            }

            // Get pool state to determine token mints
            var pool = await _solanaClient.GetPoolStateAsync(config.PoolId);
            var swapDirection = config.SwapDirection ?? SwapDirection.AToB;
            
            // Determine input and output token mints based on swap direction
            var inputMint = swapDirection == SwapDirection.AToB ? pool.TokenAMint : pool.TokenBMint;
            var outputMint = swapDirection == SwapDirection.AToB ? pool.TokenBMint : pool.TokenAMint;
            
            // CRITICAL: Check token balance BEFORE creating ATAs to avoid unnecessary blockchain calls
            var inputBalance = await _solanaClient.GetTokenBalanceAsync(wallet.Account.PublicKey.Key, inputMint);
            
            _logger.LogDebug("🔍 EARLY TOKEN CHECK - Thread {ThreadId}: inputBalance={Balance}, initialAmount={InitialAmount}, autoRefill={AutoRefill}", 
                config.ThreadId, inputBalance, config.InitialAmount, config.AutoRefill);
            
            // If no balance and no initial funding, return immediately without any blockchain operations
            if (inputBalance == 0 && config.InitialAmount == 0)
            {
                _logger.LogInformation("⏳ Swap thread {ThreadId} without initial funding has no tokens, waiting for token sharing...", config.ThreadId);
                return ("swap_waiting", true, 0);
            }
            
            // Only create ATAs if we have tokens or will get tokens
            await _solanaClient.EnsureAtaExistsAsync(wallet, inputMint);
            await _solanaClient.EnsureAtaExistsAsync(wallet, outputMint);
            
            // Check for initial funding if this is the first run and thread was configured with initial amount
            if (config.InitialAmount > 0 && !config.AutoRefill)
            {
                // Mark as initially funded to prevent repeated funding
                config.AutoRefill = true;
                await _storageService.SaveThreadConfigAsync(config.ThreadId, config);
                
                // Fund with initial tokens (only for threads that were created with initial amounts)
                await _solanaClient.MintTokensAsync(inputMint, wallet.Account.PublicKey.Key, config.InitialAmount);
                
                // Wait for tokens to be visible on blockchain with retry logic
                var actualBalance = await _solanaClient.GetTokenBalanceWithRetryAsync(
                    wallet.Account.PublicKey.Key, inputMint, config.InitialAmount, maxRetries: 5);
                
                _logger.LogInformation("Funded swap thread {ThreadId} with {Amount} initial tokens (verified balance: {ActualBalance})", 
                    config.ThreadId, config.InitialAmount, actualBalance);
            }
            
            // Check actual input token balance for swap
            var inputBalance = await _solanaClient.GetTokenBalanceAsync(wallet.Account.PublicKey.Key, inputMint);
            
            _logger.LogDebug("🔍 SWAP LOGIC DEBUG - Thread {ThreadId}: inputBalance={Balance}, initialAmount={InitialAmount}, autoRefill={AutoRefill}", 
                config.ThreadId, inputBalance, config.InitialAmount, config.AutoRefill);
            
            // If no balance, handle according to thread configuration
            if (inputBalance == 0)
            {
                _logger.LogWarning("🚨 ZERO TOKENS DETECTED - Thread {ThreadId} has no input tokens for swap", config.ThreadId);
                
                // Only threads created with InitialAmount > 0 should request more tokens when depleted
                if (config.InitialAmount > 0 && config.AutoRefill)
                {
                    _logger.LogInformation("💰 Swap thread {ThreadId} depleted tokens, requesting {Amount} from core wallet (mint: {InputMint})", 
                        config.ThreadId, config.InitialAmount, inputMint);
                    
                    await _solanaClient.MintTokensAsync(inputMint, wallet.Account.PublicKey.Key, config.InitialAmount);
                    
                    // Re-check balance with retry
                    inputBalance = await _solanaClient.GetTokenBalanceWithRetryAsync(
                        wallet.Account.PublicKey.Key, inputMint, expectedMinimum: 1, maxRetries: 5);
                    
                    _logger.LogInformation("✅ Swap thread {ThreadId} refunded with {Amount} tokens (verified balance: {Balance})", 
                        config.ThreadId, config.InitialAmount, inputBalance);
                }
                
                // If still no balance, wait for token sharing (don't fail the thread)
                if (inputBalance == 0)
                {
                    if (config.InitialAmount > 0)
                    {
                        _logger.LogInformation("⏳ Swap thread {ThreadId} with initial funding waiting for tokens...", config.ThreadId);
                    }
                    else
                    {
                        _logger.LogInformation("⏳ Swap thread {ThreadId} without initial funding waiting for token sharing...", config.ThreadId);
                    }
                    _logger.LogDebug("🔄 RETURNING WAIT STATE - Thread {ThreadId} will retry in next loop iteration", config.ThreadId);
                    return ("swap_waiting", true, 0); // Return success=true to keep thread running
                }
                
                _logger.LogDebug("✅ Thread {ThreadId} now has {Balance} tokens after refunding, proceeding with swap", config.ThreadId, inputBalance);
            }
            else
            {
                _logger.LogDebug("✅ Thread {ThreadId} has {Balance} input tokens, proceeding with swap", config.ThreadId, inputBalance);
            }

            // Calculate random swap amount (up to 2% of input token balance as per design)
            ulong maxPortion = (ulong)Math.Max(1001, (long)(inputBalance * 2 / 100)); // 2% max
            var upper = (int)Math.Min(int.MaxValue, maxPortion);
            var swapAmount = (ulong)random.Next(1000, Math.Max(1001, upper));
            
            // Ensure we don't try to swap more than available
            swapAmount = Math.Min(swapAmount, inputBalance);
            
            // Validate swap amount is not zero or too small
            _logger.LogDebug("🧮 AMOUNT VALIDATION - Thread {ThreadId}: swapAmount={SwapAmount}, inputBalance={InputBalance}", 
                config.ThreadId, swapAmount, inputBalance);
                
            if (swapAmount == 0)
            {
                _logger.LogWarning("🚨 ZERO SWAP AMOUNT - Thread {ThreadId} calculated swap amount is 0, waiting...", config.ThreadId);
                return ("swap_waiting", true, 0);
            }
            
            // Use the fee-aware swap calculation
            var swapCalc = SwapCalculation.Calculate(pool, swapDirection, swapAmount);
            
            _logger.LogDebug("🧮 OUTPUT VALIDATION - Thread {ThreadId}: expectedOutput={Output}", 
                config.ThreadId, swapCalc.OutputAmount);
            
            // Validate expected output is not zero
            if (swapCalc.OutputAmount == 0)
            {
                _logger.LogWarning("🚨 ZERO OUTPUT AMOUNT - Thread {ThreadId} calculated output is 0 (input: {Input}), waiting...", 
                    config.ThreadId, swapAmount);
                return ("swap_waiting", true, 0);
            }
            
            // Log the calculation for debugging
            _logger.LogDebug("Pool decimals - TokenA: {TokenADecimals}, TokenB: {TokenBDecimals}", 
                pool.TokenADecimals, pool.TokenBDecimals);
            
            _logger.LogDebug("Swap calculation for {Direction}: input={Input} basis points, ratioA={RatioA}, ratioB={RatioB}, netOutput={NetOutput} basis points",
                swapDirection, swapAmount, pool.RatioANumerator, pool.RatioBDenominator, 
                swapCalc.OutputAmount);
            
            // Fixed Ratio Trading requires EXACT expected output (no slippage)
            var expectedOutput = swapCalc.OutputAmount;
            var minimumOutput = swapCalc.MinimumOutputAmount;  // Must be exactly the same as expectedOutput
            
            // Submit swap transaction
            var result = await _solanaClient.ExecuteSwapAsync(
                wallet, config.PoolId, swapDirection, swapAmount, minimumOutput);
            
            _logger.LogDebug("Swap completed for thread {ThreadId}: {Input} input tokens -> {Output} output tokens, direction: {Direction}, signature: {Signature}", 
                config.ThreadId, swapAmount, result.OutputTokens, swapDirection, result.TransactionSignature);

            // Share received tokens with opposite-direction swap thread if one exists
            if (result.OutputTokens > 0)
            {
                await ShareTokensWithOppositeSwapThread(config, wallet, result.OutputTokens, outputMint);
            }

            return ("swap", true, swapAmount);
        }
        catch (Exception ex)
        {
            // Check if this is a contract error that should stop the thread
            bool isContractError = _contractErrorHandler.TryParseContractError(ex, out _);
            bool isTransactionFailure = ex.Message.Contains("custom program error") || 
                                      ex.Message.Contains("Transaction simulation failed") ||
                                      ex.Message.Contains("Transaction failed");
            
            if (isContractError || isTransactionFailure)
            {
                // Mark stopped and cancel the running thread to stop immediately
                config.Status = ThreadStatus.Stopped;
                await _storageService.SaveThreadConfigAsync(config.ThreadId, config);
                
                if (_runningThreads.TryGetValue(config.ThreadId, out var cts))
                {
                    try { cts.Cancel(); } catch { /* ignore */ }
                }
                _runningThreads.TryRemove(config.ThreadId, out _);
                
                // Re-throw to let the main error handler stop the thread
                throw;
            }
            
            // For other errors, just log and return failure
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
                        // Ensure target thread has ATA for LP tokens
                        var targetWallet = _walletCache.TryGetValue(targetThread.ThreadId, out var cached) ? cached : 
                            _solanaClient.RestoreWallet(targetThread.PrivateKey!);
                        await _solanaClient.EnsureAtaExistsAsync(targetWallet, lpMint);
                        
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
                        // Ensure target thread has ATA for tokens
                        var targetWallet = _walletCache.TryGetValue(targetThread.ThreadId, out var cached) ? cached : 
                            _solanaClient.RestoreWallet(targetThread.PrivateKey!);
                        await _solanaClient.EnsureAtaExistsAsync(targetWallet, tokenMint);
                        
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

    private async Task<bool> TransferSolFromCoreWallet(string recipientAddress)
    {
        try
        {
            // Get core wallet
            var coreWallet = await _solanaClient.GetOrCreateCoreWalletAsync();
            
            // Check core wallet balance
            var coreBalance = await _solanaClient.GetSolBalanceAsync(coreWallet.PublicKey);
            if (coreBalance <= 100_000_000) // Keep at least 0.1 SOL in core wallet
            {
                _logger.LogWarning("Core wallet balance too low ({Balance} SOL) to transfer SOL", coreBalance / 1_000_000_000.0);
                return false;
            }
            
            // Calculate 1% of remaining balance (minus reserve)
            var availableBalance = coreBalance - 100_000_000; // Subtract reserve
            var transferAmount = availableBalance / 100; // 1% of available
            
            // Ensure minimum transfer of 0.01 SOL
            transferAmount = Math.Max(transferAmount, 10_000_000); // 0.01 SOL minimum
            
            _logger.LogDebug("Transferring {Amount} SOL from core wallet to thread wallet {Recipient}", 
                transferAmount / 1_000_000_000.0, recipientAddress);
            
            // Restore core wallet from private key
            var coreWalletPrivateKey = Convert.FromBase64String(coreWallet.PrivateKey);
            var coreWalletInstance = _solanaClient.RestoreWallet(coreWalletPrivateKey);
            
            // Build and send SOL transfer transaction
            var transferTx = await _transactionBuilder.BuildSolTransferTransactionAsync(
                coreWalletInstance, recipientAddress, transferAmount);
            var signature = await _solanaClient.SendTransactionAsync(transferTx);
            await _solanaClient.ConfirmTransactionAsync(signature);
            
            _logger.LogInformation("Successfully transferred {Amount} SOL to thread wallet {Recipient}", 
                transferAmount / 1_000_000_000.0, recipientAddress);
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to transfer SOL from core wallet to {Recipient}", recipientAddress);
            return false;
        }
    }

    private async Task ShareTokensWithOppositeSwapThread(ThreadConfig sourceConfig, Wallet sourceWallet, ulong tokensToShare, string tokenMint)
    {
        try
        {
            // Find the opposite-direction swap thread for the same pool
            var oppositeDirection = sourceConfig.SwapDirection == SwapDirection.AToB ? SwapDirection.BToA : SwapDirection.AToB;
            var allThreads = await _storageService.LoadAllThreadsAsync();
            var oppositeThread = allThreads.FirstOrDefault(t => 
                t.ThreadType == ThreadType.Swap &&
                t.PoolId == sourceConfig.PoolId &&
                t.SwapDirection == oppositeDirection &&
                t.Status == ThreadStatus.Running);

            if (oppositeThread == null)
            {
                _logger.LogDebug("No opposite-direction swap thread found for pool {PoolId}", sourceConfig.PoolId);
                return;
            }

            if (oppositeThread.PublicKey == null)
            {
                _logger.LogWarning("Opposite swap thread {ThreadId} has no public key", oppositeThread.ThreadId);
                return;
            }

            // Ensure target thread has ATA for the tokens
            var targetWallet = _walletCache.TryGetValue(oppositeThread.ThreadId, out var cached) ? cached : 
                _solanaClient.RestoreWallet(oppositeThread.PrivateKey!);
            await _solanaClient.EnsureAtaExistsAsync(targetWallet, tokenMint);
            
            // Transfer tokens to opposite swap thread
            await _solanaClient.TransferTokensAsync(
                sourceWallet, oppositeThread.PublicKey, tokenMint, tokensToShare);
            
            _logger.LogInformation("Shared {Amount} tokens from swap thread {Source} ({SourceDir}) to {Target} ({TargetDir})", 
                tokensToShare, sourceConfig.ThreadId, sourceConfig.SwapDirection, 
                oppositeThread.ThreadId, oppositeThread.SwapDirection);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to share tokens with opposite swap thread from {ThreadId}", sourceConfig.ThreadId);
        }
    }
}

