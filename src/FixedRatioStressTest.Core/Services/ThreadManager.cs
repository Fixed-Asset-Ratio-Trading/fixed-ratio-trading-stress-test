using System.Collections.Concurrent;
using FixedRatioStressTest.Common.Models;
using FixedRatioStressTest.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace FixedRatioStressTest.Core.Services;

public class ThreadManager : IThreadManager
{
    private readonly IStorageService _storageService;
    private readonly ISolanaClientService _solanaClient;
    private readonly ILogger<ThreadManager> _logger;
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _runningThreads;

    public ThreadManager(IStorageService storageService, ISolanaClientService solanaClient, ILogger<ThreadManager> logger)
    {
        _storageService = storageService;
        _solanaClient = solanaClient;
        _logger = logger;
        _runningThreads = new ConcurrentDictionary<string, CancellationTokenSource>();
    }

    public async Task<string> CreateThreadAsync(ThreadConfig config)
    {
        config.ThreadId = $"{config.ThreadType.ToString().ToLower()}_{Guid.NewGuid():N}";
        config.CreatedAt = DateTime.UtcNow;
        config.Status = ThreadStatus.Created;

        // Phase 2: Generate Solana wallet for the thread
        var wallet = _solanaClient.GenerateWallet();
        config.PublicKey = wallet.Account.PublicKey.Key;
        config.PrivateKey = wallet.Account.PrivateKey.KeyBytes;
        config.WalletMnemonic = wallet.Mnemonic.ToString();

        _logger.LogInformation("Generated wallet for thread {ThreadId}: {PublicKey}", 
            config.ThreadId, config.PublicKey);

        await _storageService.SaveThreadConfigAsync(config.ThreadId, config);

        var statistics = new ThreadStatistics();
        await _storageService.SaveThreadStatisticsAsync(config.ThreadId, statistics);

        _logger.LogInformation("Created thread {ThreadId} of type {ThreadType}",
            config.ThreadId, config.ThreadType);

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
        if (config.PrivateKey != null && config.PublicKey != null)
        {
            var wallet = _solanaClient.RestoreWallet(config.PrivateKey);
            var solBalance = await _solanaClient.GetSolBalanceAsync(config.PublicKey);
            
            _logger.LogInformation("Restored wallet for thread {ThreadId}: {PublicKey}, SOL balance: {Balance} lamports", 
                threadId, config.PublicKey, solBalance);
        }

        var cancellationToken = new CancellationTokenSource();
        _runningThreads[threadId] = cancellationToken;

        _ = Task.Run(async () => await RunMockWorkerThread(config, cancellationToken.Token));

        config.Status = ThreadStatus.Running;
        await _storageService.SaveThreadConfigAsync(threadId, config);

        _logger.LogInformation("Started thread {ThreadId}", threadId);
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

        _logger.LogInformation("Stopped thread {ThreadId}", threadId);
    }

    public async Task DeleteThreadAsync(string threadId)
    {
        await StopThreadAsync(threadId);
        _logger.LogInformation("Deleted thread {ThreadId}", threadId);
    }

    public Task<ThreadConfig> GetThreadConfigAsync(string threadId)
        => _storageService.LoadThreadConfigAsync(threadId);

    public Task<List<ThreadConfig>> GetAllThreadsAsync()
        => _storageService.LoadAllThreadsAsync();

    public Task<ThreadStatistics> GetThreadStatisticsAsync(string threadId)
        => _storageService.LoadThreadStatisticsAsync(threadId);

    private async Task RunMockWorkerThread(ThreadConfig config, CancellationToken cancellationToken)
    {
        var random = new Random();

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                // Phase 2: Add basic Solana operations
                var operationType = "mock_operation";
                
                if (config.PublicKey != null)
                {
                    // Check SOL balance periodically
                    var solBalance = await _solanaClient.GetSolBalanceAsync(config.PublicKey);
                    _logger.LogDebug("Thread {ThreadId} SOL balance: {Balance} lamports", config.ThreadId, solBalance);
                    
                    // Check connection health
                    var isHealthy = await _solanaClient.IsHealthyAsync();
                    if (!isHealthy)
                    {
                        _logger.LogWarning("Solana connection unhealthy for thread {ThreadId}", config.ThreadId);
                    }
                    
                    operationType = config.ThreadType switch
                    {
                        ThreadType.Deposit => "balance_check_deposit",
                        ThreadType.Withdrawal => "balance_check_withdrawal", 
                        ThreadType.Swap => "balance_check_swap",
                        _ => "balance_check"
                    };
                }

                // TODO: Phase 3 - Add actual blockchain operations
                // TODO: Phase 3 - Add token balance checking  
                // TODO: Phase 4 - Add deposit/withdrawal/swap logic

                var statistics = await _storageService.LoadThreadStatisticsAsync(config.ThreadId);
                statistics.SuccessfulOperations++;
                statistics.TotalVolumeProcessed += (ulong)random.Next(1000, 10000);
                statistics.LastOperationAt = DateTime.UtcNow;

                await _storageService.SaveThreadStatisticsAsync(config.ThreadId, statistics);

                // Random delay between operations
                await Task.Delay(random.Next(750, 2000), cancellationToken);
            }
            catch (OperationCanceledException)
            {
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

                // Wait before retrying
                await Task.Delay(5000, cancellationToken);
            }
        }
    }
}

