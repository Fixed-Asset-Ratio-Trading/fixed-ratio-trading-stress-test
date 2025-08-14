using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using FixedRatioStressTest.Core.Interfaces;
using FixedRatioStressTest.Core.Services;
using FixedRatioStressTest.Infrastructure.Services;
using FixedRatioStressTest.Common.Models;

namespace FixedRatioStressTest.Core.Tests.Helpers;

/// <summary>
/// Main test helper class that provides common testing utilities
/// This creates REAL service instances - no mocks or fakes
/// </summary>
public class TestHelper : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly string _testDataDirectory;
    
    public IStorageService StorageService { get; }
    public ISolanaClientService SolanaClientService { get; }
    public IThreadManager ThreadManager { get; }
    public ILogger<TestHelper> Logger { get; }
    public ILoggerFactory LoggerFactory { get; }

    /// <summary>
    /// Creates a new test helper with REAL service implementations
    /// No mocking - tests against actual production code
    /// </summary>
    public TestHelper()
    {
        // Create unique test data directory for this test instance
        _testDataDirectory = Path.Combine(
            TestConstants.TEST_DATA_DIRECTORY, 
            $"test_{Guid.NewGuid():N}");
        
        var services = new ServiceCollection();
        
        // Create test configuration
        var configBuilder = new ConfigurationBuilder();
        configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Storage:DataDirectory"] = _testDataDirectory,
            ["Solana:RpcUrl"] = TestConstants.RPC_URL,
            ["Solana:ProgramId"] = TestConstants.PROGRAM_ID,
            ["Solana:CommitmentLevel"] = TestConstants.COMMITMENT_LEVEL
        });
        var configuration = configBuilder.Build();
        
        // Add logging
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(TestConstants.ENABLE_VERBOSE_LOGGING ? LogLevel.Debug : LogLevel.Information);
        });
        
        // Register configuration
        services.AddSingleton<IConfiguration>(configuration);
        
        // Register REAL services (not mocks)
        services.AddSingleton<IStorageService, JsonFileStorageService>();
        
        // Register ComputeUnitManager for TransactionBuilderService
        services.AddSingleton<IComputeUnitManager, ComputeUnitManager>();
        
        // Use REAL TransactionBuilderService for actual blockchain testing
        services.AddSingleton<ITransactionBuilderService, TransactionBuilderService>();
        
        services.AddSingleton<ISolanaClientService, SolanaClientService>();
        services.AddSingleton<IThreadManager, ThreadManager>();
        
        _serviceProvider = services.BuildServiceProvider();
        
        // Get service instances
        StorageService = _serviceProvider.GetRequiredService<IStorageService>();
        SolanaClientService = _serviceProvider.GetRequiredService<ISolanaClientService>();
        ThreadManager = _serviceProvider.GetRequiredService<IThreadManager>();
        Logger = _serviceProvider.GetRequiredService<ILogger<TestHelper>>();
        LoggerFactory = _serviceProvider.GetRequiredService<ILoggerFactory>();
        
        Logger.LogInformation("TestHelper initialized with real services. Test data directory: {DataDir}", _testDataDirectory);
    }
    
    /// <summary>
    /// Creates a test thread configuration with realistic values and REAL POOL
    /// This method now REQUIRES a valid pool ID - no more fake pools!
    /// </summary>
    public ThreadConfig CreateTestThreadConfig(
        ThreadType threadType = ThreadType.Deposit,
        string? poolId = null,
        TokenType tokenType = TokenType.A,
        SwapDirection? swapDirection = null,
        ulong initialAmount = TestConstants.DEFAULT_DEPOSIT_AMOUNT,
        bool autoRefill = false,
        bool shareTokens = false)
    {
        if (poolId == null)
        {
            throw new InvalidOperationException(
                "PoolId is required. Use CreateTestPool() first or GetOrCreateTestPools() to get valid pool IDs. " +
                "No more fake pools allowed!");
        }

        return new ThreadConfig
        {
            ThreadType = threadType,
            PoolId = poolId,
            TokenType = tokenType,
            SwapDirection = swapDirection,
            InitialAmount = initialAmount,
            AutoRefill = autoRefill,
            ShareTokens = shareTokens,
            Status = ThreadStatus.Created,
            CreatedAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Creates a real test pool on the blockchain
    /// </summary>
    public async Task<string> CreateTestPoolAsync(
        int tokenADecimals = TestConstants.TOKEN_A_DECIMALS,
        int tokenBDecimals = TestConstants.TOKEN_B_DECIMALS,
        ulong ratioWholeNumber = TestConstants.EXCHANGE_RATIO_DENOMINATOR,
        string ratioDirection = "a_to_b")
    {
        var poolParams = new PoolCreationParams
        {
            TokenADecimals = tokenADecimals,
            TokenBDecimals = tokenBDecimals,
            RatioWholeNumber = ratioWholeNumber,
            RatioDirection = ratioDirection
        };

        var poolState = await SolanaClientService.CreatePoolAsync(poolParams);
        Logger.LogInformation("Created REAL test pool {PoolId} with tokens {TokenA}/{TokenB}", 
            poolState.PoolId, poolState.TokenAMint, poolState.TokenBMint);

        return poolState.PoolId;
    }

    /// <summary>
    /// Gets or creates 3 test pools as specified in requirements
    /// If saved pools don't exist, creates new ones and replaces the saved data
    /// </summary>
    public async Task<List<string>> GetOrCreateTestPoolsAsync()
    {
        Logger.LogInformation("🏊 Getting or creating managed test pools...");
        
        // Use the new managed pool system that handles validation, cleanup, and creation
        var poolIds = await SolanaClientService.GetOrCreateManagedPoolsAsync(targetPoolCount: 3);
        
        Logger.LogInformation("✅ Test pools ready: {Count} pools available", poolIds.Count);
        foreach (var poolId in poolIds)
        {
            Logger.LogDebug("  📋 Pool: {PoolId}", poolId);
        }
        
        return poolIds;
    }
    
    /// <summary>
    /// Waits for a thread to reach the target operation count
    /// Returns true if target reached within timeout, false otherwise
    /// </summary>
    public async Task<bool> WaitForTargetOperationsAsync(
        string threadId, 
        int targetOperations = TestConstants.TARGET_OPERATIONS_PER_TEST,
        int timeoutMs = TestConstants.TARGET_OPERATIONS_TIMEOUT_MS)
    {
        var startTime = DateTime.UtcNow;
        var timeout = TimeSpan.FromMilliseconds(timeoutMs);
        
        while (DateTime.UtcNow - startTime < timeout)
        {
            try
            {
                var statistics = await ThreadManager.GetThreadStatisticsAsync(threadId);
                var totalOperations = statistics.SuccessfulOperations + statistics.FailedOperations;
                
                if (TestConstants.LOG_OPERATION_DETAILS)
                {
                    Logger.LogDebug("Thread {ThreadId}: {Successful} successful, {Failed} failed operations (target: {Target})",
                        threadId, statistics.SuccessfulOperations, statistics.FailedOperations, targetOperations);
                }
                
                if (statistics.SuccessfulOperations >= targetOperations)
                {
                    Logger.LogInformation("Thread {ThreadId} reached target of {Target} successful operations", 
                        threadId, targetOperations);
                    return true;
                }
                
                // Check if we should continue waiting or if thread failed too many times
                var errorRate = totalOperations > 0 ? (double)statistics.FailedOperations / totalOperations * 100 : 0;
                if (errorRate > TestConstants.MAX_ERROR_RATE_PERCENT && totalOperations >= 5)
                {
                    Logger.LogWarning("Thread {ThreadId} has high error rate: {ErrorRate:F1}% (failed: {Failed}, total: {Total})",
                        threadId, errorRate, statistics.FailedOperations, totalOperations);
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Error checking thread statistics for {ThreadId}", threadId);
            }
            
            await Task.Delay(TestConstants.OPERATION_CHECK_INTERVAL_MS);
        }
        
        Logger.LogWarning("Timeout waiting for thread {ThreadId} to complete {Target} operations", 
            threadId, targetOperations);
        return false;
    }
    
    /// <summary>
    /// Waits for a thread to stop gracefully
    /// Returns true if stopped within timeout, false otherwise
    /// </summary>
    public async Task<bool> WaitForThreadStopAsync(
        string threadId, 
        int timeoutMs = TestConstants.OPERATION_TIMEOUT_MS)
    {
        var startTime = DateTime.UtcNow;
        var timeout = TimeSpan.FromMilliseconds(timeoutMs);
        
        while (DateTime.UtcNow - startTime < timeout)
        {
            try
            {
                var config = await ThreadManager.GetThreadConfigAsync(threadId);
                if (config.Status == ThreadStatus.Stopped)
                {
                    Logger.LogInformation("Thread {ThreadId} stopped successfully", threadId);
                    return true;
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Error checking thread status for {ThreadId}", threadId);
            }
            
            await Task.Delay(1000); // Check every second
        }
        
        Logger.LogWarning("Timeout waiting for thread {ThreadId} to stop", threadId);
        return false;
    }
    
    /// <summary>
    /// Validates thread statistics meet expected criteria
    /// </summary>
    public async Task<bool> ValidateThreadStatisticsAsync(
        string threadId,
        int expectedSuccessfulOperations = TestConstants.TARGET_OPERATIONS_PER_TEST,
        double maxErrorRatePercent = TestConstants.MAX_ERROR_RATE_PERCENT)
    {
        try
        {
            var statistics = await ThreadManager.GetThreadStatisticsAsync(threadId);
            var totalOperations = statistics.SuccessfulOperations + statistics.FailedOperations;
            var errorRate = totalOperations > 0 ? (double)statistics.FailedOperations / totalOperations * 100 : 0;
            
            Logger.LogInformation(
                "Thread {ThreadId} Statistics: Successful={Successful}, Failed={Failed}, ErrorRate={ErrorRate:F1}%, Volume={Volume}",
                threadId, statistics.SuccessfulOperations, statistics.FailedOperations, errorRate, statistics.TotalVolumeProcessed);
            
            var successfulMatch = statistics.SuccessfulOperations >= expectedSuccessfulOperations;
            var errorRateAcceptable = errorRate <= maxErrorRatePercent;
            
            if (!successfulMatch)
            {
                Logger.LogError("Thread {ThreadId} did not complete expected operations. Expected: {Expected}, Actual: {Actual}",
                    threadId, expectedSuccessfulOperations, statistics.SuccessfulOperations);
            }
            
            if (!errorRateAcceptable)
            {
                Logger.LogError("Thread {ThreadId} error rate too high. Expected: <={Expected}%, Actual: {Actual:F1}%",
                    threadId, maxErrorRatePercent, errorRate);
            }
            
            return successfulMatch && errorRateAcceptable;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error validating thread statistics for {ThreadId}", threadId);
            return false;
        }
    }
    
    /// <summary>
    /// Cleans up test data if configured to do so
    /// </summary>
    public async Task CleanupAsync()
    {
        try
        {
            if (TestConstants.CLEANUP_TEST_DATA && !TestConstants.PRESERVE_TEST_DATA_FOR_DEBUG)
            {
                if (Directory.Exists(_testDataDirectory))
                {
                    Directory.Delete(_testDataDirectory, recursive: true);
                    Logger.LogInformation("Cleaned up test data directory: {DataDir}", _testDataDirectory);
                }
            }
            else
            {
                Logger.LogInformation("Test data preserved for debugging: {DataDir}", _testDataDirectory);
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Error during cleanup of test data directory: {DataDir}", _testDataDirectory);
        }
    }
    
    public void Dispose()
    {
        _serviceProvider?.Dispose();
        
        // Synchronous cleanup for dispose
        if (TestConstants.CLEANUP_TEST_DATA && !TestConstants.PRESERVE_TEST_DATA_FOR_DEBUG)
        {
            try
            {
                if (Directory.Exists(_testDataDirectory))
                {
                    Directory.Delete(_testDataDirectory, recursive: true);
                }
            }
            catch
            {
                // Ignore errors during dispose
            }
        }
    }
}
