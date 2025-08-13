using FixedRatioStressTest.Common.Models;

namespace FixedRatioStressTest.Core.Interfaces;

/// <summary>
/// Service for validating the deployed contract version matches expected version
/// This ensures the stress test service is compatible with the deployed contract
/// </summary>
public interface IContractVersionService
{
    /// <summary>
    /// Validates that the deployed contract version matches the expected version
    /// Uses GetVersion instruction (discriminator 14) via simulation for zero-cost verification
    /// </summary>
    /// <returns>ValidationResult with success status and version information</returns>
    Task<ContractVersionResult> ValidateContractVersionAsync();
    
    /// <summary>
    /// Gets the deployed contract version without validation
    /// </summary>
    /// <returns>The version string from the deployed contract, or null if unable to retrieve</returns>
    Task<string?> GetDeployedVersionAsync();
    
    /// <summary>
    /// Gets the expected contract version from configuration
    /// </summary>
    string GetExpectedVersion();
}

/// <summary>
/// Result of contract version validation
/// </summary>
public class ContractVersionResult
{
    public bool IsValid { get; set; }
    public string? DeployedVersion { get; set; }
    public string ExpectedVersion { get; set; } = string.Empty;
    public string MaxSupportedVersion { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
    public List<string> ProgramLogs { get; set; } = new();
    public bool IsVersionTooHigh { get; set; }
    
    /// <summary>
    /// True if the service should shut down due to version mismatch or unsupported version
    /// </summary>
    public bool ShouldShutdown => !IsValid && !string.IsNullOrEmpty(DeployedVersion);
}
