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
}

