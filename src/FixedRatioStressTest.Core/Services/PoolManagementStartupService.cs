using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using FixedRatioStressTest.Core.Interfaces;

namespace FixedRatioStressTest.Core.Services;

/// <summary>
/// Hosted service that validates existing pools during application startup
/// Only cleans up invalid pools - does NOT create new pools automatically
/// Pools should be created on-demand via RPC calls
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
            // Step 1: Initialize core wallet (after version check has passed)
            _logger.LogInformation("🔑 Step 1: Initializing core wallet...");
            var coreWallet = await _solanaClient.GetOrCreateCoreWalletAsync();
            _logger.LogInformation("✅ Core wallet ready: {PublicKey} ({Balance} SOL)", 
                coreWallet.PublicKey, coreWallet.CurrentSolBalance / 1_000_000_000.0);

            // Step 2: Validate and automatically import any previously saved pools created by this app
            // - Any saved pools not found on-chain are deleted
            // - Any valid saved pools are reused and added to active set
            _logger.LogInformation("🧹 Step 2: Validating and importing previously saved pools...");
            await _solanaClient.ValidateAndCleanupSavedPoolsAsync();

            // Step 3: Get list of currently valid pools (no automatic creation)
            _logger.LogInformation("🎯 Step 3: Loading existing pools (no automatic creation)...");
            var managedPools = await _solanaClient.GetOrCreateManagedPoolsAsync(targetPoolCount: 0); // 0 = don't create any new pools

            _logger.LogInformation("✅ Pool management startup complete: {Count} existing pools found", managedPools.Count);
            
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
                _logger.LogDebug("📋 No existing pools found - use RPC create_pool to create pools on demand");
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
