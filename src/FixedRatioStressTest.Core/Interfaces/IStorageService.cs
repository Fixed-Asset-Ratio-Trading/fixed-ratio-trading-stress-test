using FixedRatioStressTest.Common.Models;

namespace FixedRatioStressTest.Core.Interfaces;

public interface IStorageService
{
    Task SaveThreadConfigAsync(string threadId, ThreadConfig config);
    Task<ThreadConfig> LoadThreadConfigAsync(string threadId);
    Task<List<ThreadConfig>> LoadAllThreadsAsync();
    Task SaveThreadStatisticsAsync(string threadId, ThreadStatistics statistics);
    Task<ThreadStatistics> LoadThreadStatisticsAsync(string threadId);
    Task AddThreadErrorAsync(string threadId, ThreadError error);
    
    // Persistent pool management
    Task<List<string>> LoadActivePoolIdsAsync();
    Task SaveActivePoolIdsAsync(List<string> poolIds);
    Task CleanupPoolDataAsync(string poolId);
    Task CleanupAllThreadDataForPoolAsync(string poolId);
    
    // Core wallet and real pool management
    Task<CoreWalletConfig?> LoadCoreWalletAsync();
    Task SaveCoreWalletAsync(CoreWalletConfig wallet);
    Task<List<RealPoolData>> LoadRealPoolsAsync();
    Task SaveRealPoolAsync(RealPoolData pool);
    Task DeleteRealPoolAsync(string poolId);
    Task<List<StressTestTokenMint>> LoadTokenMintsAsync();
    Task SaveTokenMintAsync(StressTestTokenMint tokenMint);
}

