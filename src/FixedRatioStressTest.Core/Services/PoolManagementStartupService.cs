using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using FixedRatioStressTest.Core.Interfaces;

namespace FixedRatioStressTest.Core.Services;

/// <summary>
/// Hosted service that manages pool lifecycle during application startup
/// Ensures target number of pools exist and cleans up invalid pools
/// </summary>
public class PoolManagementStartupService : IHostedService
{
    private readonly ISolanaClientService _solanaClient;
    private readonly ILogger<PoolManagementStartupService> _logger;

    public PoolManagementStartupService(
        ISolanaClientService solanaClient,
        ILogger<PoolManagementStartupService> logger)
    {
        _solanaClient = solanaClient;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("🏊 Starting pool management during application startup...");

        try
        {
            // Step 1: Clean up any invalid pools first
            _logger.LogInformation("🧹 Step 1: Cleaning up invalid pools...");
            await _solanaClient.CleanupInvalidPoolsAsync();

            // Step 2: Ensure we have the target number of managed pools
            _logger.LogInformation("🎯 Step 2: Ensuring target pool count...");
            var managedPools = await _solanaClient.GetOrCreateManagedPoolsAsync(targetPoolCount: 3);

            _logger.LogInformation("✅ Pool management startup complete: {Count} pools ready", managedPools.Count);
            
            if (managedPools.Count > 0)
            {
                _logger.LogInformation("📋 Available pools:");
                foreach (var poolId in managedPools)
                {
                    _logger.LogInformation("  - {PoolId}", poolId);
                }
            }
            else
            {
                _logger.LogWarning("⚠️ No pools available - service may not function correctly without pools");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "💥 Pool management startup failed");
            _logger.LogWarning("⚠️ Service will continue but pool operations may not work correctly");
            // Don't fail startup for pool management issues - the service can still run
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Pool management startup service stopping.");
        return Task.CompletedTask;
    }
}
