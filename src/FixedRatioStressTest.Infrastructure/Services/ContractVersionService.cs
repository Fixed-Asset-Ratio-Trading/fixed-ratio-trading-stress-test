using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Solnet.Rpc;
using Solnet.Wallet;
using Solnet.Rpc.Builders;
using Solnet.Rpc.Models;
using FixedRatioStressTest.Core.Interfaces;
using FixedRatioStressTest.Common.Models;

namespace FixedRatioStressTest.Infrastructure.Services;

/// <summary>
/// Service for validating deployed contract version using GetVersion instruction
/// Uses simulation to avoid transaction fees and ensure zero-cost validation
/// </summary>
public class ContractVersionService : IContractVersionService
{
    private readonly IRpcClient _rpcClient;
    private readonly ILogger<ContractVersionService> _logger;
    private readonly SolanaConfig _config;
    private readonly string _expectedVersion;
    private readonly string _maxSupportedVersion;

    // GetVersion instruction discriminator as per API documentation
    private const byte GET_VERSION_DISCRIMINATOR = 14;
    
    // Version 0.20.x marks major breaking changes - not supported
    private const string DEFAULT_MAX_SUPPORTED_VERSION = "0.19.9999";

    public ContractVersionService(
        IConfiguration configuration,
        ILogger<ContractVersionService> logger)
    {
        _config = configuration.GetSection("SolanaConfiguration").Get<SolanaConfig>() ?? new SolanaConfig();
        _logger = logger;
        
        var rpcUrl = _config.GetActiveRpcUrl();
        _rpcClient = ClientFactory.GetClient(rpcUrl);
        
        // Get expected and max supported versions from configuration
        _expectedVersion = configuration["ContractVersion:Expected"] ?? "v0.15.1054";
        _maxSupportedVersion = configuration["ContractVersion:MaxSupported"] ?? DEFAULT_MAX_SUPPORTED_VERSION;
        
        _logger.LogInformation("ContractVersionService initialized. RPC: {RpcUrl}, Expected: {ExpectedVersion}, Max Supported: {MaxSupportedVersion}", 
            rpcUrl, _expectedVersion, _maxSupportedVersion);
    }

    public async Task<ContractVersionResult> ValidateContractVersionAsync()
    {
        _logger.LogInformation("🔍 Validating deployed contract version...");
        
        try
        {
            var deployedVersion = await GetDeployedVersionAsync();
            
            var result = new ContractVersionResult
            {
                DeployedVersion = deployedVersion,
                ExpectedVersion = _expectedVersion,
                MaxSupportedVersion = _maxSupportedVersion
            };

            if (string.IsNullOrEmpty(deployedVersion))
            {
                result.IsValid = false;
                result.ErrorMessage = "Unable to retrieve deployed contract version. Contract may not be deployed or GetVersion instruction failed.";
                _logger.LogError("❌ Contract version validation FAILED: {ErrorMessage}", result.ErrorMessage);
                return result;
            }

            // Normalize versions for comparison (remove 'v' prefix if present)
            var normalizedDeployed = NormalizeVersion(deployedVersion);
            var normalizedExpected = NormalizeVersion(_expectedVersion);
            var normalizedMaxSupported = NormalizeVersion(_maxSupportedVersion);

            // Check if deployed version is too high (>= 0.20.x)
            if (CompareVersions(normalizedDeployed, normalizedMaxSupported) > 0)
            {
                result.IsValid = false;
                result.IsVersionTooHigh = true;
                result.ShouldShutdown = true;
                result.ErrorMessage = $"Contract version {deployedVersion} is not supported. This service supports versions up to {_maxSupportedVersion}. Version 0.20.x+ contains breaking changes.";
                _logger.LogError("❌ Contract version validation FAILED - VERSION TOO HIGH: {ErrorMessage}", result.ErrorMessage);
                return result;
            }

            // Check if deployed version matches expected
            result.IsValid = string.Equals(normalizedDeployed, normalizedExpected, StringComparison.OrdinalIgnoreCase);

            if (result.IsValid)
            {
                _logger.LogInformation("✅ Contract version validation SUCCESS: Deployed={DeployedVersion}, Expected={ExpectedVersion}", 
                    deployedVersion, _expectedVersion);
            }
            else
            {
                result.ShouldShutdown = true;
                result.ErrorMessage = $"Contract version mismatch. Deployed: {deployedVersion}, Expected: {_expectedVersion}";
                _logger.LogError("❌ Contract version validation FAILED: {ErrorMessage}", result.ErrorMessage);
            }

            return result;
        }
        catch (Exception ex)
        {
            var result = new ContractVersionResult
            {
                DeployedVersion = null,
                ExpectedVersion = _expectedVersion,
                IsValid = false,
                ShouldShutdown = true,
                ErrorMessage = $"Contract version validation failed with exception: {ex.Message}"
            };

            _logger.LogError(ex, "❌ Contract version validation encountered an error");
            return result;
        }
    }

    public async Task<string?> GetDeployedVersionAsync()
    {
        try
        {
            _logger.LogDebug("Executing GetVersion instruction via simulation...");

            // Create ephemeral wallet for simulation (as per API documentation)
            var ephemeralWallet = GenerateEphemeralWallet();
            
            // Create GetVersion instruction with discriminator 14 and no accounts
            var instructionData = new byte[] { GET_VERSION_DISCRIMINATOR };
            
            var instruction = new TransactionInstruction
            {
                ProgramId = new PublicKey(_config.ProgramId),
                Keys = new List<AccountMeta>(), // GetVersion requires no accounts
                Data = instructionData
            };

            // Get recent blockhash
            var blockHashResponse = await _rpcClient.GetLatestBlockHashAsync();
            if (!blockHashResponse.WasRequestSuccessfullyHandled)
            {
                _logger.LogError("Failed to get recent blockhash for version check");
                return null;
            }

            // Build transaction
            var transaction = new TransactionBuilder()
                .SetFeePayer(ephemeralWallet.Account.PublicKey)
                .SetRecentBlockHash(blockHashResponse.Result.Value.Blockhash)
                .AddInstruction(instruction)
                .Build(ephemeralWallet.Account);

            _logger.LogDebug("Simulating GetVersion transaction...");

            // Simulate transaction with RPC options to avoid payer existence checks
            var simulationResult = await _rpcClient.SimulateTransactionAsync(
                transaction, 
                commitment: Solnet.Rpc.Types.Commitment.Processed,
                sigVerify: false,
                replaceRecentBlockhash: true);

            if (!simulationResult.WasRequestSuccessfullyHandled)
            {
                _logger.LogError("GetVersion simulation failed: {Reason}", simulationResult.Reason);
                return null;
            }

            var result = simulationResult.Result?.Value;
            if (result?.Error != null)
            {
                _logger.LogError("GetVersion simulation returned error: {Error}", result.Error);
                return null;
            }

            // Parse version from program logs
            var logs = result?.Logs ?? new string[0];
            _logger.LogDebug("GetVersion simulation successful. Logs count: {LogCount}", logs.Length);

            foreach (var log in logs)
            {
                _logger.LogDebug("Program log: {Log}", log);
            }

            // Look for version line in logs: "Contract Version: x.x.x"
            var versionLog = logs.FirstOrDefault(log => log.Contains("Contract Version:", StringComparison.OrdinalIgnoreCase));
            
            if (string.IsNullOrEmpty(versionLog))
            {
                _logger.LogWarning("GetVersion succeeded but no 'Contract Version:' found in logs");
                return null;
            }

            // Extract version using regex
            var versionMatch = Regex.Match(versionLog, @"Contract Version:\s*([0-9v.]+)", RegexOptions.IgnoreCase);
            if (versionMatch.Success)
            {
                var version = versionMatch.Groups[1].Value;
                _logger.LogInformation("✅ Successfully retrieved deployed contract version: {Version}", version);
                return version;
            }

            _logger.LogWarning("Found version log but failed to parse version: {VersionLog}", versionLog);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get deployed contract version");
            return null;
        }
    }

    public string GetExpectedVersion()
    {
        return _expectedVersion;
    }

    /// <summary>
    /// Generate ephemeral wallet for simulation (doesn't need to exist on-chain)
    /// </summary>
    private Wallet GenerateEphemeralWallet()
    {
        var privateKey = new byte[64];
        System.Security.Cryptography.RandomNumberGenerator.Fill(privateKey);
        return new Wallet(privateKey, "", SeedMode.Bip39);
    }

    /// <summary>
    /// Normalize version string for comparison (remove 'v' prefix, trim whitespace)
    /// </summary>
    private static string NormalizeVersion(string version)
    {
        if (string.IsNullOrEmpty(version))
            return string.Empty;

        return version.Trim().ToLowerInvariant().TrimStart('v');
    }

    /// <summary>
    /// Compare two version strings (semantic versioning)
    /// Returns: -1 if version1 < version2, 0 if equal, 1 if version1 > version2
    /// </summary>
    private static int CompareVersions(string version1, string version2)
    {
        if (string.IsNullOrEmpty(version1) && string.IsNullOrEmpty(version2))
            return 0;
        if (string.IsNullOrEmpty(version1))
            return -1;
        if (string.IsNullOrEmpty(version2))
            return 1;

        try
        {
            // Parse version components (major.minor.patch)
            var v1Parts = version1.Split('.').Select(p => int.TryParse(p, out var num) ? num : 0).ToArray();
            var v2Parts = version2.Split('.').Select(p => int.TryParse(p, out var num) ? num : 0).ToArray();

            // Ensure we have at least 3 parts (major.minor.patch)
            var v1 = new int[3];
            var v2 = new int[3];
            
            for (int i = 0; i < 3; i++)
            {
                v1[i] = i < v1Parts.Length ? v1Parts[i] : 0;
                v2[i] = i < v2Parts.Length ? v2Parts[i] : 0;
            }

            // Compare major.minor.patch
            for (int i = 0; i < 3; i++)
            {
                if (v1[i] < v2[i]) return -1;
                if (v1[i] > v2[i]) return 1;
            }

            return 0; // Versions are equal
        }
        catch
        {
            // Fallback to string comparison if parsing fails
            return string.Compare(version1, version2, StringComparison.OrdinalIgnoreCase);
        }
    }
}
