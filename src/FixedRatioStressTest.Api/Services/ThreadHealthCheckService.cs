using Microsoft.Extensions.Diagnostics.HealthChecks;
using FixedRatioStressTest.Core.Interfaces;

namespace FixedRatioStressTest.Api.Services;

public class ThreadHealthCheckService : IHealthCheck
{
    private readonly IThreadManager _threadManager;
    private readonly ISolanaClientService _solanaClient;
    private readonly ILogger<ThreadHealthCheckService> _logger;
    
    public ThreadHealthCheckService(
        IThreadManager threadManager,
        ISolanaClientService solanaClient,
        ILogger<ThreadHealthCheckService> logger)
    {
        _threadManager = threadManager;
        _solanaClient = solanaClient;
        _logger = logger;
    }
    
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, 
        CancellationToken cancellationToken = default)
    {
        try
        {
            var data = new Dictionary<string, object>();
            
            // Check Solana connection
            var solanaHealthy = await _solanaClient.IsHealthyAsync();
            data["solana_healthy"] = solanaHealthy;
            
            if (!solanaHealthy)
            {
                return HealthCheckResult.Unhealthy("Solana RPC connection failed", data: data);
            }
            
            // Get thread statistics
            var threads = await _threadManager.GetAllThreadsAsync();
            var runningThreads = threads.Count(t => t.Status == Common.Models.ThreadStatus.Running);
            var errorThreads = threads.Count(t => t.Status == Common.Models.ThreadStatus.Error);
            
            data["total_threads"] = threads.Count;
            data["running_threads"] = runningThreads;
            data["error_threads"] = errorThreads;
            
            // Calculate overall operations per minute
            var totalOpsPerMinute = 0.0;
            foreach (var thread in threads.Where(t => t.Status == Common.Models.ThreadStatus.Running))
            {
                var stats = await _threadManager.GetThreadStatisticsAsync(thread.ThreadId);
                if (stats.LastOperationAt != DateTime.MinValue)
                {
                    var timeSinceStart = DateTime.UtcNow - thread.CreatedAt;
                    var totalOps = stats.SuccessfulOperations + stats.FailedOperations;
                    if (timeSinceStart.TotalMinutes > 0)
                    {
                        totalOpsPerMinute += totalOps / timeSinceStart.TotalMinutes;
                    }
                }
            }
            
            data["operations_per_minute"] = Math.Round(totalOpsPerMinute, 2);
            
            // Determine health status
            if (errorThreads > threads.Count * 0.5) // More than 50% threads in error
            {
                return HealthCheckResult.Unhealthy("Too many threads in error state", data: data);
            }
            
            if (runningThreads == 0 && threads.Count > 0)
            {
                return HealthCheckResult.Degraded("No threads are running", data: data);
            }
            
            return HealthCheckResult.Healthy("All systems operational", data);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Health check failed");
            return HealthCheckResult.Unhealthy("Health check error", exception: ex);
        }
    }
}
