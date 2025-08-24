using System.Text.Json;
using System;
using FixedRatioStressTest.Common.Models;
using FixedRatioStressTest.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FixedRatioStressTest.Infrastructure.Services;

public class JsonFileStorageService : IStorageService
{
    private readonly string _dataDirectory;
    private readonly ILogger<JsonFileStorageService> _logger;
    private readonly SemaphoreSlim _fileLock;
    private readonly JsonSerializerOptions _jsonOptions;

    public JsonFileStorageService(IConfiguration configuration, ILogger<JsonFileStorageService> logger)
    {
        _dataDirectory = configuration.GetValue<string>("Storage:DataDirectory")
                        ?? Path.Combine(Environment.CurrentDirectory, "data");

        // TEMP: Hard-code root-level data directory to stabilize state across hosts
        // NOTE: This override is for development only and will be removed for production.
        try
        {
            var repoRootData = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../data"));
            if (Directory.Exists(repoRootData))
            {
                _dataDirectory = repoRootData;
            }
        }
        catch { }
        _logger = logger;
        _fileLock = new SemaphoreSlim(1, 1);

        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        EnsureDirectoriesExist();
        
        // Migrate existing statistics from shared file to individual files if needed
        _ = Task.Run(async () => await MigrateStatisticsToIndividualFilesAsync());
    }

    public async Task SaveThreadConfigAsync(string threadId, ThreadConfig config)
    {
        await _fileLock.WaitAsync();
        try
        {
            var threadsFile = Path.Combine(_dataDirectory, "threads.json");
            var tempFile = $"{threadsFile}.tmp";

            var threads = await LoadAllThreadsAsync() ?? new List<ThreadConfig>();
            var existingIndex = threads.FindIndex(t => t.ThreadId == threadId);

            if (existingIndex >= 0)
                threads[existingIndex] = config;
            else
                threads.Add(config);

            var json = JsonSerializer.Serialize(new { threads }, _jsonOptions);
            await File.WriteAllTextAsync(tempFile, json);

            if (File.Exists(threadsFile))
                File.Replace(tempFile, threadsFile, $"{threadsFile}.backup");
            else
                File.Move(tempFile, threadsFile);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task<ThreadConfig> LoadThreadConfigAsync(string threadId)
    {
        var threads = await LoadAllThreadsAsync();
        var config = threads?.FirstOrDefault(t => t.ThreadId == threadId);
        if (config is null)
        {
            throw new KeyNotFoundException($"Thread {threadId} not found");
        }
        return config;
    }

    public async Task<List<ThreadConfig>> LoadAllThreadsAsync()
    {
        var threadsFile = Path.Combine(_dataDirectory, "threads.json");
        if (!File.Exists(threadsFile))
            return new List<ThreadConfig>();

        var json = await File.ReadAllTextAsync(threadsFile);
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("threads", out var threadsElement))
        {
            var threads = threadsElement.Deserialize<List<ThreadConfig>>(_jsonOptions);
            return threads ?? new List<ThreadConfig>();
        }
        return new List<ThreadConfig>();
    }

    public async Task DeleteThreadConfigAsync(string threadId)
    {
        await _fileLock.WaitAsync();
        try
        {
            var threadsFile = Path.Combine(_dataDirectory, "threads.json");
            var tempFile = $"{threadsFile}.tmp";

            var threads = await LoadAllThreadsAsync() ?? new List<ThreadConfig>();
            var initialCount = threads.Count;
            
            // Remove the thread from the list
            threads.RemoveAll(t => t.ThreadId == threadId);
            
            if (threads.Count < initialCount)
            {
                // Thread was found and removed, save the updated list
                var json = JsonSerializer.Serialize(new { threads }, _jsonOptions);
                await File.WriteAllTextAsync(tempFile, json);

                if (File.Exists(threadsFile))
                    File.Replace(tempFile, threadsFile, $"{threadsFile}.backup");
                else
                    File.Move(tempFile, threadsFile);
                
                _logger.LogDebug("Removed thread {ThreadId} from storage", threadId);
                
                // Clean up the individual thread statistics file
                await DeleteThreadStatisticsAsync(threadId);
            }
            else
            {
                _logger.LogWarning("Thread {ThreadId} not found in storage for deletion", threadId);
            }
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task SaveThreadStatisticsAsync(string threadId, ThreadStatistics statistics)
    {
        await _fileLock.WaitAsync();
        try
        {
            var statsDirectory = Path.Combine(_dataDirectory, "stats");
            var statsFile = Path.Combine(statsDirectory, $"{threadId}.json");
            var tempFile = $"{statsFile}.tmp";

            var json = JsonSerializer.Serialize(statistics, _jsonOptions);
            await File.WriteAllTextAsync(tempFile, json);

            if (File.Exists(statsFile))
                File.Replace(tempFile, statsFile, $"{statsFile}.backup");
            else
                File.Move(tempFile, statsFile);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task<ThreadStatistics> LoadThreadStatisticsAsync(string threadId)
    {
        var statsDirectory = Path.Combine(_dataDirectory, "stats");
        var statsFile = Path.Combine(statsDirectory, $"{threadId}.json");
        
        if (!File.Exists(statsFile))
            return new ThreadStatistics();

        try
        {
            var json = await File.ReadAllTextAsync(statsFile);
            var statistics = JsonSerializer.Deserialize<ThreadStatistics>(json, _jsonOptions);
            return statistics ?? new ThreadStatistics();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load statistics for thread {ThreadId}, returning empty stats", threadId);
            return new ThreadStatistics();
        }
    }

    public async Task AddThreadErrorAsync(string threadId, ThreadError error)
    {
        var statistics = await LoadThreadStatisticsAsync(threadId);
        statistics.RecentErrors.Add(error);

        if (statistics.RecentErrors.Count > 10)
        {
            statistics.RecentErrors = statistics.RecentErrors
                .OrderByDescending(e => e.Timestamp)
                .Take(10)
                .ToList();
        }

        await SaveThreadStatisticsAsync(threadId, statistics);
    }

    public async Task DeleteThreadStatisticsAsync(string threadId)
    {
        try
        {
            var statsDirectory = Path.Combine(_dataDirectory, "stats");
            var statsFile = Path.Combine(statsDirectory, $"{threadId}.json");
            
            if (File.Exists(statsFile))
            {
                File.Delete(statsFile);
                _logger.LogDebug("Deleted statistics file for thread {ThreadId}", threadId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete statistics file for thread {ThreadId}", threadId);
        }
        
        await Task.CompletedTask;
    }

    public async Task<List<string>> LoadActivePoolIdsAsync()
    {
        var filePath = Path.Combine(_dataDirectory, "active_pools.json");
        
        if (!File.Exists(filePath))
        {
            _logger.LogDebug("No active pools file found, returning empty list");
            return new List<string>();
        }

        try
        {
            var json = await File.ReadAllTextAsync(filePath);
            var poolIds = JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
            _logger.LogDebug("Loaded {Count} active pool IDs from storage", poolIds.Count);
            return poolIds;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load active pool IDs from {FilePath}", filePath);
            return new List<string>();
        }
    }

    public async Task SaveActivePoolIdsAsync(List<string> poolIds)
    {
        var filePath = Path.Combine(_dataDirectory, "active_pools.json");
        
        try
        {
            var json = JsonSerializer.Serialize(poolIds, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(filePath, json);
            _logger.LogDebug("Saved {Count} active pool IDs to storage", poolIds.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save active pool IDs to {FilePath}", filePath);
            throw;
        }
    }

    public async Task CleanupPoolDataAsync(string poolId)
    {
        try
        {
            var poolDataPath = Path.Combine(_dataDirectory, "pools", $"{poolId}.json");
            if (File.Exists(poolDataPath))
            {
                File.Delete(poolDataPath);
                _logger.LogDebug("Cleaned up pool data for {PoolId}", poolId);
            }

            var poolStatsPath = Path.Combine(_dataDirectory, "pool_stats", $"{poolId}.json");
            if (File.Exists(poolStatsPath))
            {
                File.Delete(poolStatsPath);
                _logger.LogDebug("Cleaned up pool statistics for {PoolId}", poolId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cleanup pool data for {PoolId}", poolId);
        }
        
        await Task.CompletedTask;
    }

    public async Task CleanupAllThreadDataForPoolAsync(string poolId)
    {
        try
        {
            var threadDirectory = Path.Combine(_dataDirectory, "threads");
            if (!Directory.Exists(threadDirectory))
            {
                return;
            }

            var threadFiles = Directory.GetFiles(threadDirectory, "*.json");
            var cleanupCount = 0;

            foreach (var threadFile in threadFiles)
            {
                try
                {
                    var threadJson = await File.ReadAllTextAsync(threadFile);
                    var threadConfig = JsonSerializer.Deserialize<ThreadConfig>(threadJson);
                    
                    if (threadConfig?.PoolId == poolId)
                    {
                        // Delete thread config file
                        File.Delete(threadFile);
                        cleanupCount++;

                        // Delete associated statistics file
                        var threadId = Path.GetFileNameWithoutExtension(threadFile);
                        var statsFile = Path.Combine(_dataDirectory, "statistics", $"{threadId}.json");
                        if (File.Exists(statsFile))
                        {
                            File.Delete(statsFile);
                        }

                        _logger.LogDebug("Cleaned up thread {ThreadId} associated with pool {PoolId}", threadId, poolId);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to process thread file {ThreadFile} during pool cleanup", threadFile);
                }
            }

            _logger.LogDebug("Cleaned up {Count} threads associated with pool {PoolId}", cleanupCount, poolId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cleanup thread data for pool {PoolId}", poolId);
        }
    }

    public async Task<CoreWalletConfig?> LoadCoreWalletAsync()
    {
        var filePath = Path.Combine(_dataDirectory, "core_wallet.json");
        
        if (!File.Exists(filePath))
        {
            _logger.LogDebug("No core wallet file found");
            return null;
        }

        try
        {
            var json = await File.ReadAllTextAsync(filePath);
            var wallet = JsonSerializer.Deserialize<CoreWalletConfig>(json);
            _logger.LogDebug("Loaded core wallet: {PublicKey}", wallet?.PublicKey);
            return wallet;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load core wallet from {FilePath}", filePath);
            return null;
        }
    }

    public async Task SaveCoreWalletAsync(CoreWalletConfig wallet)
    {
        var filePath = Path.Combine(_dataDirectory, "core_wallet.json");
        
        try
        {
            var json = JsonSerializer.Serialize(wallet, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(filePath, json);
            _logger.LogDebug("Saved core wallet: {PublicKey}", wallet.PublicKey);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save core wallet to {FilePath}", filePath);
            throw;
        }
    }

    public async Task<List<RealPoolData>> LoadRealPoolsAsync()
    {
        var filePath = Path.Combine(_dataDirectory, "real_pools.json");
        
        if (!File.Exists(filePath))
        {
            _logger.LogDebug("No real pools file found, returning empty list");
            return new List<RealPoolData>();
        }

        try
        {
            var json = await File.ReadAllTextAsync(filePath);
            var pools = JsonSerializer.Deserialize<List<RealPoolData>>(json) ?? new List<RealPoolData>();
            _logger.LogDebug("Loaded {Count} real pools from storage", pools.Count);
            return pools;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load real pools from {FilePath}", filePath);
            return new List<RealPoolData>();
        }
    }

    public async Task SaveRealPoolAsync(RealPoolData pool)
    {
        var pools = await LoadRealPoolsAsync();
        
        // Update existing or add new
        var existingIndex = pools.FindIndex(p => p.PoolId == pool.PoolId);
        if (existingIndex >= 0)
        {
            pools[existingIndex] = pool;
        }
        else
        {
            pools.Add(pool);
        }

        var filePath = Path.Combine(_dataDirectory, "real_pools.json");
        
        try
        {
            var json = JsonSerializer.Serialize(pools, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(filePath, json);
            _logger.LogDebug("Saved real pool: {PoolId}", pool.PoolId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save real pool to {FilePath}", filePath);
            throw;
        }
    }

    public async Task DeleteRealPoolAsync(string poolId)
    {
        var pools = await LoadRealPoolsAsync();
        var initialCount = pools.Count;
        
        pools.RemoveAll(p => p.PoolId == poolId);
        
        if (pools.Count < initialCount)
        {
            var filePath = Path.Combine(_dataDirectory, "real_pools.json");
            
            try
            {
                var json = JsonSerializer.Serialize(pools, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(filePath, json);
                _logger.LogDebug("Deleted real pool: {PoolId}", poolId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete real pool from {FilePath}", filePath);
                throw;
            }
        }
    }

    public async Task<List<StressTestTokenMint>> LoadTokenMintsAsync()
    {
        var filePath = Path.Combine(_dataDirectory, "token_mints.json");
        
        if (!File.Exists(filePath))
        {
            _logger.LogDebug("No token mints file found, returning empty list");
            return new List<StressTestTokenMint>();
        }

        try
        {
            var json = await File.ReadAllTextAsync(filePath);
            var mints = JsonSerializer.Deserialize<List<StressTestTokenMint>>(json) ?? new List<StressTestTokenMint>();
            _logger.LogDebug("Loaded {Count} token mints from storage", mints.Count);
            return mints;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load token mints from {FilePath}", filePath);
            return new List<StressTestTokenMint>();
        }
    }

    public async Task SaveTokenMintAsync(StressTestTokenMint tokenMint)
    {
        var mints = await LoadTokenMintsAsync();
        
        // Update existing or add new
        var existingIndex = mints.FindIndex(m => m.MintAddress == tokenMint.MintAddress);
        if (existingIndex >= 0)
        {
            mints[existingIndex] = tokenMint;
        }
        else
        {
            mints.Add(tokenMint);
        }

        var filePath = Path.Combine(_dataDirectory, "token_mints.json");
        
        try
        {
            var json = JsonSerializer.Serialize(mints, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(filePath, json);
            _logger.LogDebug("Saved token mint: {MintAddress} ({Decimals} decimals)", tokenMint.MintAddress, tokenMint.Decimals);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save token mint to {FilePath}", filePath);
            throw;
        }
    }

    public async Task<StressTestTokenMint?> LoadTokenMintAsync(string mintAddress)
    {
        try
        {
            var allMints = await LoadTokenMintsAsync();
            return allMints.FirstOrDefault(m => m.MintAddress == mintAddress);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load token mint {MintAddress}", mintAddress);
            return null;
        }
    }



    private void EnsureDirectoriesExist()
    {
        if (!Directory.Exists(_dataDirectory))
        {
            Directory.CreateDirectory(_dataDirectory);
        }
        
        // Ensure stats subdirectory exists for individual thread statistics
        var statsDirectory = Path.Combine(_dataDirectory, "stats");
        if (!Directory.Exists(statsDirectory))
        {
            Directory.CreateDirectory(statsDirectory);
        }
    }

    private async Task MigrateStatisticsToIndividualFilesAsync()
    {
        try
        {
            var oldStatsFile = Path.Combine(_dataDirectory, "statistics.json");
            if (!File.Exists(oldStatsFile))
                return;

            _logger.LogInformation("Migrating thread statistics from shared file to individual files...");

            // Read the old statistics file
            var json = await File.ReadAllTextAsync(oldStatsFile);
            using var doc = JsonDocument.Parse(json);
            
            if (doc.RootElement.TryGetProperty("statistics", out var statsElement))
            {
                var allStats = statsElement.Deserialize<Dictionary<string, ThreadStatistics>>(_jsonOptions);
                if (allStats != null)
                {
                    var migratedCount = 0;
                    foreach (var kvp in allStats)
                    {
                        var threadId = kvp.Key;
                        var statistics = kvp.Value;
                        
                        // Save to individual file
                        var statsDirectory = Path.Combine(_dataDirectory, "stats");
                        var individualStatsFile = Path.Combine(statsDirectory, $"{threadId}.json");
                        
                        if (!File.Exists(individualStatsFile))
                        {
                            var individualJson = JsonSerializer.Serialize(statistics, _jsonOptions);
                            await File.WriteAllTextAsync(individualStatsFile, individualJson);
                            migratedCount++;
                        }
                    }
                    
                    if (migratedCount > 0)
                    {
                        _logger.LogInformation("Migrated {Count} thread statistics to individual files", migratedCount);
                        
                        // Rename the old file to keep as backup
                        var backupFile = Path.Combine(_dataDirectory, "statistics.json.backup");
                        if (!File.Exists(backupFile))
                        {
                            File.Move(oldStatsFile, backupFile);
                            _logger.LogInformation("Backed up original statistics file to statistics.json.backup");
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to migrate statistics to individual files");
        }
    }
}

