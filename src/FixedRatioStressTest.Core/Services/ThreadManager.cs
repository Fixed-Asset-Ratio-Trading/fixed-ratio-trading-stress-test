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
    private readonly ConcurrentDictionary<string, Dictionary<string, DateTime>> _resourceRequestTimes;

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
        _resourceRequestTimes = new ConcurrentDictionary<string, Dictionary<string, DateTime>>();
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

        // Generate Solana wallet for the thread (lightweight operation)
        var wallet = _solanaClient.GenerateWallet();
        config.PublicKey = wallet.Account.PublicKey.Key;
        config.PrivateKey = wallet.Account.PrivateKey.KeyBytes;
        config.WalletMnemonic = wallet.Mnemonic?.ToString() ?? "";

        _logger.LogDebug("Generated wallet for thread {ThreadId}: {PublicKey} for pool {PoolId}", 
            config.ThreadId, config.PublicKey, config.PoolId);

        // Save thread config immediately - no resource allocation
        await _storageService.SaveThreadConfigAsync(config.ThreadId, config);

        var statistics = new ThreadStatistics();
        await _storageService.SaveThreadStatisticsAsync(config.ThreadId, statistics);
        
        _logger.LogInformation("Created thread {ThreadId} with instant startup - resources will be allocated on-demand", config.ThreadId);
        return config.ThreadId;
    }

    public async Task StartThreadAsync(string threadId)
    {
        var config = await _storageService.LoadThreadConfigAsync(threadId);

        // Recover from stale Running status on restart
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

        // Restore wallet for operations (lightweight)
        if (config.PrivateKey != null && config.PublicKey != null)
        {
            var wallet = _solanaClient.RestoreWallet(config.PrivateKey);
            _walletCache[threadId] = wallet;
            
            _logger.LogDebug("Restored wallet for thread {ThreadId}: {PublicKey}", 
                threadId, config.PublicKey);
        }

        // Start thread immediately - no resource allocation
        var cancellationToken = new CancellationTokenSource();
        _runningThreads[threadId] = cancellationToken;

        _ = Task.Run(async () => await RunWorkerThread(config, cancellationToken.Token));

        config.Status = ThreadStatus.Running;
        await _storageService.SaveThreadConfigAsync(threadId, config);

        _logger.LogInformation("Started thread {ThreadId} with lazy resource allocation", threadId);
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

                // Update statistics only for successful operations that processed volume
                if (volumeProcessed > 0)
                {
                    var statistics = await _storageService.LoadThreadStatisticsAsync(config.ThreadId);
                    
                    statistics.SuccessfulOperations++;
                    statistics.TotalVolumeProcessed += volumeProcessed;
                    statistics.LastOperationAt = DateTime.UtcNow;
                    
                    await _storageService.SaveThreadStatisticsAsync(config.ThreadId, statistics);
                }

                // NEW: Implement different delays based on operation type
                TimeSpan delay;
                if (operationType.Contains("_waiting"))
                {
                    // For waiting operations (resource requests), wait 1 minute
                    delay = TimeSpan.FromMinutes(1);
                    _logger.LogDebug("Thread {ThreadId} waiting 1 minute before next resource request attempt", config.ThreadId);
                }
                else
                {
                    // For successful operations, use random delay (750-2000ms as per design)
                    delay = TimeSpan.FromMilliseconds(random.Next(750, 2000));
                }
                
                await Task.Delay(delay, cancellationToken);
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





    private async Task<(string operationType, bool success, ulong volume)> HandleDepositOperation(
        ThreadConfig config, Wallet wallet, Random random)
    {
        try
        {
            _logger.LogDebug("Executing deposit operation for thread {ThreadId}", config.ThreadId);

            // NEW: Request SOL if needed (non-blocking)
            var solRequested = await RequestSolIfNeeded(config, wallet);
            if (!solRequested)
            {
                _logger.LogDebug("Waiting for SOL allocation for thread {ThreadId}", config.ThreadId);
                return ("deposit_waiting_sol", true, 0); // Return success=true to keep thread running
            }

            // NEW: Request ATAs if needed (non-blocking)
            var atasReady = await EnsureAtasExistLazy(config, wallet);
            if (!atasReady)
            {
                _logger.LogDebug("Waiting for ATA creation for thread {ThreadId}", config.ThreadId);
                return ("deposit_waiting_atas", true, 0);
            }

            // NEW: Request tokens if needed (non-blocking)
            var tokensAvailable = await EnsureTokensAvailableLazy(config, wallet);
            if (!tokensAvailable)
            {
                _logger.LogDebug("Waiting for token allocation for thread {ThreadId}", config.ThreadId);
                return ("deposit_waiting_tokens", true, 0);
            }

            // Continue with existing deposit logic...
            var pool = await _solanaClient.GetPoolStateAsync(config.PoolId);
            var tokenMint = config.TokenType == TokenType.A ? pool.TokenAMint : pool.TokenBMint;
            
            // Check actual token balance for deposit
            var tokenBalance = await _solanaClient.GetTokenBalanceAsync(wallet.Account.PublicKey.Key, tokenMint);
            if (tokenBalance == 0)
            {
                _logger.LogDebug("No tokens available for deposit in thread {ThreadId}, waiting...", config.ThreadId);
                return ("deposit_waiting", true, 0); // FIXED: Return success=true instead of false
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

            // NEW: Request SOL if needed (non-blocking)
            var solRequested = await RequestSolIfNeeded(config, wallet);
            if (!solRequested)
            {
                _logger.LogDebug("Waiting for SOL allocation for thread {ThreadId}", config.ThreadId);
                return ("withdrawal_waiting_sol", true, 0);
            }

            // NEW: Request ATAs if needed (non-blocking)
            var atasReady = await EnsureAtasExistLazy(config, wallet);
            if (!atasReady)
            {
                _logger.LogDebug("Waiting for ATA creation for thread {ThreadId}", config.ThreadId);
                return ("withdrawal_waiting_atas", true, 0);
            }

            // Check for LP tokens - if none available, wait (don't fail)
            var pool = await _solanaClient.GetPoolStateAsync(config.PoolId);
            var lpMint = config.TokenType == TokenType.A ? pool.LpMintA : pool.LpMintB;
            var lpBalance = await _solanaClient.GetTokenBalanceAsync(wallet.Account.PublicKey.Key, lpMint);
            
            if (lpBalance == 0)
            {
                _logger.LogDebug("No LP tokens available for withdrawal in thread {ThreadId}, waiting for token sharing...", config.ThreadId);
                return ("withdrawal_waiting_lp_tokens", true, 0); // FIXED: Return success=true instead of false
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
            _logger.LogInformation("🎯 STARTING SWAP OPERATION - Thread {ThreadId}, direction: {Direction}", 
                config.ThreadId, config.SwapDirection);

            // NEW: Request SOL if needed (non-blocking)
            var solRequested = await RequestSolIfNeeded(config, wallet);
            if (!solRequested)
            {
                _logger.LogDebug("Waiting for SOL allocation for thread {ThreadId}", config.ThreadId);
                return ("swap_waiting_sol", true, 0);
            }

            // NEW: Request ATAs if needed (non-blocking)
            var atasReady = await EnsureAtasExistLazy(config, wallet);
            if (!atasReady)
            {
                _logger.LogDebug("Waiting for ATA creation for thread {ThreadId}", config.ThreadId);
                return ("swap_waiting_atas", true, 0);
            }

            // Get pool state to determine token mints
            var pool = await _solanaClient.GetPoolStateAsync(config.PoolId);
            var swapDirection = config.SwapDirection ?? SwapDirection.AToB;
            
            // Determine input and output token mints based on swap direction
            var inputMint = swapDirection == SwapDirection.AToB ? pool.TokenAMint : pool.TokenBMint;
            var outputMint = swapDirection == SwapDirection.AToB ? pool.TokenBMint : pool.TokenAMint;
            
            // Check token balance BEFORE any operations
            var inputBalance = await _solanaClient.GetTokenBalanceAsync(wallet.Account.PublicKey.Key, inputMint);
            
            // If no balance and no initial funding, wait for token sharing
            if (inputBalance == 0 && config.InitialAmount == 0)
            {
                _logger.LogInformation("⏳ Swap thread {ThreadId} without initial funding has no tokens, waiting for token sharing...", config.ThreadId);
                return ("swap_waiting_tokens", true, 0);
            }
            
            // NEW: Request tokens if needed (for threads with InitialAmount > 0)
            var tokensAvailable = await EnsureTokensAvailableLazy(config, wallet, inputMint);
            if (!tokensAvailable)
            {
                _logger.LogDebug("Waiting for token allocation for thread {ThreadId}", config.ThreadId);
                return ("swap_waiting_tokens", true, 0);
            }
            
            // Re-check actual input token balance for swap
            inputBalance = await _solanaClient.GetTokenBalanceAsync(wallet.Account.PublicKey.Key, inputMint);
            
            // If no balance, wait for token sharing or resource allocation
            if (inputBalance == 0)
            {
                _logger.LogInformation("⏳ Swap thread {ThreadId} waiting for tokens...", config.ThreadId);
                return ("swap_waiting", true, 0);
            }
            
            _logger.LogDebug("✅ Thread {ThreadId} has {Balance} input tokens, proceeding with swap", config.ThreadId, inputBalance);

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
            
            // ENHANCED DEBUGGING: Use FRTExpectedTokens for validation and logging
            var expectedOutputDebug = FixedRatioStressTest.Common.Utils.FRTExpectedTokens.Calculate(
                swapAmount,
                pool.TokenADecimals,
                pool.TokenBDecimals,
                pool.RatioANumerator,
                pool.RatioBDenominator,
                swapDirection == SwapDirection.AToB
            );
            
            // Log detailed calculation explanation
            var calculationExplanation = FixedRatioStressTest.Common.Utils.FRTExpectedTokens.ExplainCalculation(
                swapAmount,
                pool.TokenADecimals,
                pool.TokenBDecimals,
                pool.RatioANumerator,
                pool.RatioBDenominator,
                swapDirection == SwapDirection.AToB
            );
            
            _logger.LogDebug("🧮 SWAP CALCULATION DEBUG - Thread {ThreadId}", config.ThreadId);
            _logger.LogDebug("  Pool: ratioA={RatioA}, ratioB={RatioB}, decimalsA={DecA}, decimalsB={DecB}",
                pool.RatioANumerator, pool.RatioBDenominator, pool.TokenADecimals, pool.TokenBDecimals);
            _logger.LogDebug("  Calculation: {Explanation}", calculationExplanation);
            _logger.LogDebug("  SwapCalc Output: {SwapCalcOutput}, FRTExpected Output: {FRTOutput}",
                swapCalc.OutputAmount, expectedOutputDebug);
            
            // Validate outputs match
            if (swapCalc.OutputAmount != expectedOutputDebug)
            {
                _logger.LogError("⚠️ OUTPUT MISMATCH - SwapCalc: {SwapCalc} vs FRTExpected: {Expected}",
                    swapCalc.OutputAmount, expectedOutputDebug);
            }
            
            _logger.LogDebug("🧮 OUTPUT VALIDATION - Thread {ThreadId}: expectedOutput={Output}", 
                config.ThreadId, swapCalc.OutputAmount);
            
            // Validate expected output is not zero
            if (swapCalc.OutputAmount == 0)
            {
                _logger.LogWarning("🚨 ZERO OUTPUT AMOUNT - Thread {ThreadId} calculated output is 0 (input: {Input}), waiting...", 
                    config.ThreadId, swapAmount);
                return ("swap_waiting", true, 0);
            }
            
            // Fixed Ratio Trading requires EXACT expected output (no slippage)
            var expectedOutput = swapCalc.OutputAmount;
            var minimumOutput = swapCalc.MinimumOutputAmount;  // Must be exactly the same as expectedOutput
            
            // Submit swap transaction
            var result = await _solanaClient.ExecuteSwapAsync(
                wallet, config.PoolId, swapDirection, swapAmount, minimumOutput, config.ThreadId);
            
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
                            sourceWallet, targetThread.PublicKey, lpMint, sharePerThread, sourceConfig.ThreadId);
                        
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
                            sourceWallet, targetThread.PublicKey, tokenMint, sharePerThread, sourceConfig.ThreadId);
                        
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
                sourceWallet, oppositeThread.PublicKey, tokenMint, tokensToShare, sourceConfig.ThreadId);
            
            _logger.LogInformation("Shared {Amount} tokens from swap thread {Source} ({SourceDir}) to {Target} ({TargetDir})", 
                tokensToShare, sourceConfig.ThreadId, sourceConfig.SwapDirection, 
                oppositeThread.ThreadId, oppositeThread.SwapDirection);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to share tokens with opposite swap thread from {ThreadId}", sourceConfig.ThreadId);
        }
    }

    private async Task<bool> RequestSolIfNeeded(ThreadConfig config, Wallet wallet)
    {
        try
        {
            var solBalance = await _solanaClient.GetSolBalanceAsync(wallet.Account.PublicKey.Key);
            if (solBalance >= 100_000_000) // 0.1 SOL threshold
            {
                return true; // Already has sufficient SOL
            }

            // Check if we recently requested SOL (avoid spam requests)
            var lastSolRequest = GetLastResourceRequest(config.ThreadId, "SOL");
            if (lastSolRequest.HasValue && DateTime.UtcNow - lastSolRequest.Value < TimeSpan.FromMinutes(1))
            {
                _logger.LogDebug("SOL request for thread {ThreadId} still pending (last request: {LastRequest})", 
                    config.ThreadId, lastSolRequest.Value);
                return false; // Still waiting for previous request
            }

            // Request SOL from core wallet
            _logger.LogInformation("Thread {ThreadId} requesting SOL from core wallet", config.ThreadId);
            var transferred = await TransferSolFromCoreWallet(wallet.Account.PublicKey.Key);
            
            // Record the request time
            RecordResourceRequest(config.ThreadId, "SOL");
            
            if (transferred)
            {
                _logger.LogInformation("Successfully requested SOL for thread {ThreadId}", config.ThreadId);
                // Don't wait for confirmation - let the next operation cycle check the balance
                return false; // Return false to wait for next cycle to verify
            }
            else
            {
                _logger.LogWarning("Failed to request SOL for thread {ThreadId}", config.ThreadId);
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error requesting SOL for thread {ThreadId}", config.ThreadId);
            return false;
        }
    }

    private async Task<bool> EnsureAtasExistLazy(ThreadConfig config, Wallet wallet)
    {
        try
        {
            // Check if we recently requested ATAs (avoid spam requests)
            var lastAtaRequest = GetLastResourceRequest(config.ThreadId, "ATA");
            if (lastAtaRequest.HasValue && DateTime.UtcNow - lastAtaRequest.Value < TimeSpan.FromMinutes(1))
            {
                _logger.LogDebug("ATA request for thread {ThreadId} still pending (last request: {LastRequest})", 
                    config.ThreadId, lastAtaRequest.Value);
                return false; // Still waiting for previous request
            }

            var pool = await _solanaClient.GetPoolStateAsync(config.PoolId);
            var atasNeeded = new List<string>();

            // Determine which ATAs are needed based on thread type
            switch (config.ThreadType)
            {
                case ThreadType.Deposit:
                    var tokenMint = config.TokenType == TokenType.A ? pool.TokenAMint : pool.TokenBMint;
                    var lpMint = config.TokenType == TokenType.A ? pool.LpMintA : pool.LpMintB;
                    atasNeeded.AddRange(new[] { tokenMint, lpMint });
                    break;
                    
                case ThreadType.Withdrawal:
                    tokenMint = config.TokenType == TokenType.A ? pool.TokenAMint : pool.TokenBMint;
                    lpMint = config.TokenType == TokenType.A ? pool.LpMintA : pool.LpMintB;
                    atasNeeded.AddRange(new[] { tokenMint, lpMint });
                    break;
                    
                case ThreadType.Swap:
                    atasNeeded.AddRange(new[] { pool.TokenAMint, pool.TokenBMint });
                    break;
            }

            // Check if ATAs exist, create if needed
            var allAtasExist = true;
            foreach (var mint in atasNeeded)
            {
                try
                {
                    // Use existing EnsureAtaExistsAsync which handles creation
                    await _solanaClient.EnsureAtaExistsAsync(wallet, mint);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to ensure ATA exists for thread {ThreadId} and mint {Mint}", config.ThreadId, mint);
                    allAtasExist = false;
                }
            }

            if (!allAtasExist)
            {
                // Record the request time
                RecordResourceRequest(config.ThreadId, "ATA");
                _logger.LogInformation("Requested ATA creation for thread {ThreadId}", config.ThreadId);
                return false; // Return false to wait for next cycle to verify
            }

            return true; // All ATAs exist
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error ensuring ATAs exist for thread {ThreadId}", config.ThreadId);
            return false;
        }
    }

    private async Task<bool> EnsureTokensAvailableLazy(ThreadConfig config, Wallet wallet, string? specificMint = null)
    {
        try
        {
            // Only request tokens for threads that were configured with initial amounts
            if (config.InitialAmount == 0)
            {
                return true; // No initial funding configured, rely on token sharing
            }

            // Check if we recently requested tokens (avoid spam requests)
            var lastTokenRequest = GetLastResourceRequest(config.ThreadId, "TOKENS");
            if (lastTokenRequest.HasValue && DateTime.UtcNow - lastTokenRequest.Value < TimeSpan.FromMinutes(1))
            {
                _logger.LogDebug("Token request for thread {ThreadId} still pending (last request: {LastRequest})", 
                    config.ThreadId, lastTokenRequest.Value);
                return false; // Still waiting for previous request
            }

            string tokenMint;
            if (specificMint != null)
            {
                tokenMint = specificMint;
            }
            else
            {
                var pool = await _solanaClient.GetPoolStateAsync(config.PoolId);
                tokenMint = config.TokenType == TokenType.A ? pool.TokenAMint : pool.TokenBMint;
            }

            // Check current token balance
            var currentBalance = await _solanaClient.GetTokenBalanceAsync(wallet.Account.PublicKey.Key, tokenMint);
            var threshold = config.InitialAmount * 5 / 100; // 5% of initial amount as threshold

            if (currentBalance < threshold)
            {
                _logger.LogInformation("Thread {ThreadId} requesting {Amount} tokens from core wallet (mint: {Mint})", 
                    config.ThreadId, config.InitialAmount, tokenMint);
                
                // Request tokens from core wallet (mint new tokens)
                await _solanaClient.MintTokensAsync(tokenMint, wallet.Account.PublicKey.Key, config.InitialAmount);
                
                // Record the request time
                RecordResourceRequest(config.ThreadId, "TOKENS");
                
                _logger.LogInformation("Requested token minting for thread {ThreadId}", config.ThreadId);
                return false; // Return false to wait for next cycle to verify
            }

            return true; // Sufficient tokens available
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error ensuring tokens available for thread {ThreadId}", config.ThreadId);
            return false;
        }
    }

    private DateTime? GetLastResourceRequest(string threadId, string resourceType)
    {
        if (_resourceRequestTimes.TryGetValue(threadId, out var threadRequests))
        {
            if (threadRequests.TryGetValue(resourceType, out var lastRequest))
            {
                return lastRequest;
            }
        }
        return null;
    }

    private void RecordResourceRequest(string threadId, string resourceType)
    {
        _resourceRequestTimes.AddOrUpdate(threadId, 
            new Dictionary<string, DateTime> { { resourceType, DateTime.UtcNow } },
            (key, existing) => 
            {
                existing[resourceType] = DateTime.UtcNow;
                return existing;
            });
    }
}

