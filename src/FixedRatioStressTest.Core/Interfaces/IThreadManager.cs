using FixedRatioStressTest.Common.Models;

namespace FixedRatioStressTest.Core.Interfaces;

public interface IThreadManager
{
    Task<string> CreateThreadAsync(ThreadConfig config);
    Task StartThreadAsync(string threadId);
    Task StopThreadAsync(string threadId);
    Task<int> StopAllThreadsForPoolAsync(string poolId, bool includeSwaps = false);
    Task DeleteThreadAsync(string threadId);
    Task<ThreadConfig> GetThreadConfigAsync(string threadId);
    Task<List<ThreadConfig>> GetAllThreadsAsync();
    Task<ThreadStatistics> GetThreadStatisticsAsync(string threadId);
}

