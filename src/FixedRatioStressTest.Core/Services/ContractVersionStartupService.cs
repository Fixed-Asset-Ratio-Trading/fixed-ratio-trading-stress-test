using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using FixedRatioStressTest.Core.Interfaces;

namespace FixedRatioStressTest.Core.Services;

/// <summary>
/// Hosted service that validates contract version on startup
/// If version validation fails, the service will shut down gracefully
/// </summary>
public class ContractVersionStartupService : IHostedService
{
    private readonly IContractVersionService _versionService;
    private readonly IHostApplicationLifetime _appLifetime;
    private readonly ILogger<ContractVersionStartupService> _logger;

    public ContractVersionStartupService(
        IContractVersionService versionService,
        IHostApplicationLifetime appLifetime,
        ILogger<ContractVersionStartupService> logger)
    {
        _versionService = versionService;
        _appLifetime = appLifetime;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("🔍 Starting contract version validation...");

        try
        {
            var validationResult = await _versionService.ValidateContractVersionAsync();

            if (validationResult.IsValid)
            {
                _logger.LogInformation("✅ Contract version validation PASSED. Service starting normally.");
                _logger.LogInformation("Deployed Version: {DeployedVersion}, Expected: {ExpectedVersion}", 
                    validationResult.DeployedVersion, validationResult.ExpectedVersion);
                return; // Continue with normal startup
            }

            // Version validation failed
            _logger.LogError("❌ Contract version validation FAILED!");
            _logger.LogError("Deployed Version: {DeployedVersion}", validationResult.DeployedVersion ?? "Unable to retrieve");
            _logger.LogError("Expected Version: {ExpectedVersion}", validationResult.ExpectedVersion);
            _logger.LogError("Max Supported Version: {MaxSupportedVersion}", validationResult.MaxSupportedVersion);
            _logger.LogError("Error: {ErrorMessage}", validationResult.ErrorMessage);

            if (validationResult.ShouldShutdown)
            {
                if (validationResult.IsVersionTooHigh)
                {
                    _logger.LogCritical("🚨 CONTRACT VERSION TOO HIGH - SHUTTING DOWN SERVICE FOR SAFETY");
                    _logger.LogCritical("Deployed contract version {DeployedVersion} contains breaking changes not supported by this service.", validationResult.DeployedVersion);
                    _logger.LogCritical("This service supports versions up to {MaxSupportedVersion}. Version 0.20.x+ is not compatible.", validationResult.MaxSupportedVersion);
                    _logger.LogCritical("Please deploy a supported contract version or update the service to a newer version.");
                }
                else
                {
                    _logger.LogCritical("🚨 CONTRACT VERSION MISMATCH - SHUTTING DOWN SERVICE FOR SAFETY");
                    _logger.LogCritical("The stress test service is not compatible with the deployed contract version.");
                    _logger.LogCritical("Please update the service or deploy the correct contract version.");
                }
                
                // Graceful shutdown
                _appLifetime.StopApplication();
                return;
            }

            // Could not retrieve version but contract might not be deployed yet
            _logger.LogWarning("⚠️ Could not retrieve contract version. This may be acceptable if:");
            _logger.LogWarning("  - Contract is not yet deployed");
            _logger.LogWarning("  - RPC connection issues (temporary)");
            _logger.LogWarning("  - GetVersion instruction format issues");
            _logger.LogWarning("Service will continue to start but may not function correctly.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "💥 Contract version validation failed with unexpected error");
            _logger.LogWarning("Service will continue to start but version compatibility is unknown.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Contract version startup service stopping.");
        return Task.CompletedTask;
    }
}
