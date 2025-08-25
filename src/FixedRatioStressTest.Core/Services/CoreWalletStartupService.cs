using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using FixedRatioStressTest.Core.Interfaces;
using FixedRatioStressTest.Common.Models;

namespace FixedRatioStressTest.Core.Services;

/// <summary>
/// Hosted service that initializes the core wallet on startup
/// For localnet testing (Program ID 4aeVqtWhrUh6wpX8acNj2hpWXKEQwxjA3PYb2sHhNyCn), 
/// attempts airdrop if wallet is empty. Failure to airdrop does not stop the app.
/// </summary>
public class CoreWalletStartupService : IHostedService
{
    private readonly ISolanaClientService _solanaClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<CoreWalletStartupService> _logger;

    public CoreWalletStartupService(
        ISolanaClientService solanaClient,
        IConfiguration configuration,
        ILogger<CoreWalletStartupService> logger)
    {
        _solanaClient = solanaClient;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("🔑 Starting core wallet initialization...");

        try
        {
            // Step 1: Load or create core wallet
            _logger.LogInformation("📂 Loading or creating core wallet...");
            var coreWallet = await _solanaClient.GetOrCreateCoreWalletAsync();
            
            _logger.LogInformation("✅ Core wallet ready: {PublicKey}", coreWallet.PublicKey);
            _logger.LogInformation("💰 Current balance: {Balance} SOL ({Lamports} lamports)", 
                coreWallet.CurrentSolBalance / 1_000_000_000.0, coreWallet.CurrentSolBalance);

            // Step 2: Check if this is localnet testing based on program ID
            var isLocalnetTesting = IsLocalnetTesting();
            
            if (isLocalnetTesting)
            {
                _logger.LogInformation("🧪 Localnet testing detected (Program ID: {ProgramId})", 
                    SolanaConfiguration.LocalnetProgramId);
                
                // Step 3: Attempt airdrop if wallet is empty (localnet only)
                if (coreWallet.CurrentSolBalance == 0)
                {
                    _logger.LogInformation("💸 Core wallet is empty, attempting airdrop of 1 SOL...");
                    
                    try
                    {
                        var airdropSignature = await _solanaClient.RequestAirdropAsync(
                            coreWallet.PublicKey, 
                            SolanaConfiguration.SOL_AIRDROP_AMOUNT);
                        
                        if (!string.IsNullOrEmpty(airdropSignature))
                        {
                            _logger.LogInformation("✅ Airdrop successful! Signature: {Signature}", airdropSignature);
                            
                            // Wait a moment for the airdrop to be processed
                            await Task.Delay(2000, cancellationToken);
                            
                            // Check updated balance
                            var updatedBalance = await _solanaClient.GetSolBalanceAsync(coreWallet.PublicKey);
                            _logger.LogInformation("💰 Updated balance: {Balance} SOL ({Lamports} lamports)", 
                                updatedBalance / 1_000_000_000.0, updatedBalance);
                        }
                        else
                        {
                            _logger.LogWarning("⚠️ Airdrop request returned empty signature - may have failed");
                        }
                    }
                    catch (Exception airdropEx)
                    {
                        _logger.LogWarning(airdropEx, "⚠️ Airdrop failed, but continuing startup (localnet testing)");
                        _logger.LogWarning("This is not a critical error - the application will continue with empty wallet");
                    }
                }
                else
                {
                    _logger.LogInformation("💰 Core wallet has sufficient balance, skipping airdrop");
                }
            }
            else
            {
                _logger.LogInformation("🌐 Non-localnet environment detected, skipping airdrop attempt");
            }

            _logger.LogInformation("✅ Core wallet initialization completed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "💥 Core wallet initialization failed");
            _logger.LogCritical("🛑 Cannot continue without core wallet - TERMINATING APPLICATION");
            
            // Force immediate application termination
            Environment.Exit(1);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Core wallet startup service stopping.");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Determines if this is localnet testing based on the program ID
    /// </summary>
    private bool IsLocalnetTesting()
    {
        try
        {
            var config = _configuration.GetSection("SolanaConfiguration").Get<SolanaConfig>();
            var programId = config?.ProgramId ?? SolanaConfiguration.LocalnetProgramId;
            
            var isLocalnet = programId == SolanaConfiguration.LocalnetProgramId;
            
            _logger.LogDebug("Program ID detection: {ProgramId} -> Localnet: {IsLocalnet}", programId, isLocalnet);
            
            return isLocalnet;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to detect program ID from configuration, assuming localnet");
            return true; // Default to localnet for safety
        }
    }
}
