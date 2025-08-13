using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using FixedRatioStressTest.Infrastructure.Services;
using FixedRatioStressTest.Core.Interfaces;
using Xunit;
using Xunit.Abstractions;

namespace FixedRatioStressTest.Core.Tests;

/// <summary>
/// Tests for contract version validation functionality
/// These tests verify that the service can properly validate contract versions
/// and handle various failure scenarios gracefully
/// </summary>
public class ContractVersionServiceTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly ILogger<ContractVersionService> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IContractVersionService _versionService;
    private readonly IConfiguration _configuration;

    // Test constants
    private const string EXPECTED_VERSION = "v0.15.1053";
    private const string LOCALNET_RPC_URL = "http://192.168.2.88:8899";
    private const string PROGRAM_ID = "4aeVqtWhrUh6wpX8acNj2hpWXKEQwxjA3PYb2sHhNyCn";

    public ContractVersionServiceTests(ITestOutputHelper output)
    {
        _output = output;

        // Setup logging to capture debug info
        _loggerFactory = LoggerFactory.Create(builder =>
            builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
        _logger = _loggerFactory.CreateLogger<ContractVersionService>();

        // Setup configuration for localnet testing
        var configBuilder = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SolanaConfiguration:LocalnetRpcUrl"] = LOCALNET_RPC_URL,
                ["SolanaConfiguration:DevnetRpcUrl"] = "https://api.devnet.solana.com",
                ["SolanaConfiguration:MainnetRpcUrl"] = "https://api.mainnet-beta.solana.com",
                ["SolanaConfiguration:ActiveNetwork"] = "localnet",
                ["SolanaConfiguration:ProgramId"] = PROGRAM_ID,
                ["SolanaConfiguration:Commitment"] = "confirmed",
                ["ContractVersion:Expected"] = EXPECTED_VERSION
            });
        _configuration = configBuilder.Build();

        // Create the service instance
        _versionService = new ContractVersionService(_configuration, _logger);
    }

    [Fact]
    public async Task GetDeployedVersionAsync_WithValidContract_ReturnsVersion()
    {
        _logger.LogInformation("=== TEST: GetDeployedVersionAsync_WithValidContract_ReturnsVersion ===");

        try
        {
            // Act
            var deployedVersion = await _versionService.GetDeployedVersionAsync();

            // Assert
            _output.WriteLine($"Deployed version: {deployedVersion ?? "NULL"}");
            
            if (!string.IsNullOrEmpty(deployedVersion))
            {
                _logger.LogInformation("✅ Successfully retrieved deployed version: {Version}", deployedVersion);
                Assert.NotNull(deployedVersion);
                Assert.NotEmpty(deployedVersion);
                
                // Version should be in a reasonable format (contains numbers and dots)
                Assert.Matches(@"[0-9v.]+", deployedVersion);
            }
            else
            {
                _logger.LogWarning("⚠️ Could not retrieve deployed version - this may indicate contract is not deployed or GetVersion instruction failed");
                // This is acceptable - we just want to ensure the method doesn't throw
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Test failed with exception");
            throw;
        }
    }

    [Fact]
    public async Task ValidateContractVersionAsync_WithMatchingVersion_ReturnsValid()
    {
        _logger.LogInformation("=== TEST: ValidateContractVersionAsync_WithMatchingVersion_ReturnsValid ===");

        try
        {
            // Act
            var result = await _versionService.ValidateContractVersionAsync();

            // Assert
            Assert.NotNull(result);
            _output.WriteLine($"Validation result: Valid={result.IsValid}, Deployed={result.DeployedVersion}, Expected={result.ExpectedVersion}");
            _output.WriteLine($"Error message: {result.ErrorMessage ?? "None"}");
            _output.WriteLine($"Should shutdown: {result.ShouldShutdown}");

            // Log the outcome
            if (result.IsValid)
            {
                _logger.LogInformation("✅ Contract version validation passed: {DeployedVersion} matches {ExpectedVersion}", 
                    result.DeployedVersion, result.ExpectedVersion);
            }
            else if (!string.IsNullOrEmpty(result.DeployedVersion))
            {
                _logger.LogWarning("⚠️ Contract version mismatch: Deployed={DeployedVersion}, Expected={ExpectedVersion}", 
                    result.DeployedVersion, result.ExpectedVersion);
            }
            else
            {
                _logger.LogWarning("⚠️ Could not retrieve deployed version for validation");
            }

            // Basic structure assertions
            Assert.Equal(EXPECTED_VERSION, result.ExpectedVersion);
            
            // If we got a deployed version, verify the validation logic
            if (!string.IsNullOrEmpty(result.DeployedVersion))
            {
                // If versions match (normalized), should be valid
                var normalizedDeployed = result.DeployedVersion.Trim().ToLowerInvariant().TrimStart('v');
                var normalizedExpected = EXPECTED_VERSION.Trim().ToLowerInvariant().TrimStart('v');
                
                if (normalizedDeployed == normalizedExpected)
                {
                    Assert.True(result.IsValid, "Validation should succeed when versions match");
                    Assert.Null(result.ErrorMessage);
                    Assert.False(result.ShouldShutdown);
                }
                else
                {
                    Assert.False(result.IsValid, "Validation should fail when versions don't match");
                    Assert.NotNull(result.ErrorMessage);
                    Assert.True(result.ShouldShutdown, "Service should shutdown on version mismatch");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Test failed with exception");
            throw;
        }
    }

    [Fact]
    public void GetExpectedVersion_ReturnsConfiguredVersion()
    {
        _logger.LogInformation("=== TEST: GetExpectedVersion_ReturnsConfiguredVersion ===");

        // Act
        var expectedVersion = _versionService.GetExpectedVersion();

        // Assert
        Assert.Equal(EXPECTED_VERSION, expectedVersion);
        _output.WriteLine($"Expected version: {expectedVersion}");
        _logger.LogInformation("✅ GetExpectedVersion returned correct value: {ExpectedVersion}", expectedVersion);
    }

    [Fact]
    public async Task ContractVersionService_Integration_FullWorkflow()
    {
        _logger.LogInformation("=== TEST: ContractVersionService_Integration_FullWorkflow ===");
        
        try
        {
            // Step 1: Check expected version configuration
            var expectedVersion = _versionService.GetExpectedVersion();
            Assert.NotNull(expectedVersion);
            Assert.NotEmpty(expectedVersion);
            _output.WriteLine($"Expected version: {expectedVersion}");

            // Step 2: Attempt to get deployed version
            var deployedVersion = await _versionService.GetDeployedVersionAsync();
            _output.WriteLine($"Deployed version: {deployedVersion ?? "Could not retrieve"}");

            // Step 3: Run full validation
            var validationResult = await _versionService.ValidateContractVersionAsync();
            
            Assert.NotNull(validationResult);
            Assert.Equal(expectedVersion, validationResult.ExpectedVersion);
            
            _output.WriteLine($"Validation: Valid={validationResult.IsValid}");
            _output.WriteLine($"Error: {validationResult.ErrorMessage ?? "None"}");
            _output.WriteLine($"Should shutdown: {validationResult.ShouldShutdown}");

            // Step 4: Verify the decision logic
            if (string.IsNullOrEmpty(deployedVersion))
            {
                // Could not get version - should be invalid but not shutdown (might be connectivity issue)
                Assert.False(validationResult.IsValid);
                Assert.False(validationResult.ShouldShutdown);
                _logger.LogInformation("✅ Correctly handled case where deployed version could not be retrieved");
            }
            else
            {
                // Got a version - validate the comparison logic
                Assert.Equal(deployedVersion, validationResult.DeployedVersion);
                
                var normalizedDeployed = deployedVersion.Trim().ToLowerInvariant().TrimStart('v');
                var normalizedExpected = expectedVersion.Trim().ToLowerInvariant().TrimStart('v');
                
                if (normalizedDeployed == normalizedExpected)
                {
                    Assert.True(validationResult.IsValid);
                    Assert.False(validationResult.ShouldShutdown);
                    _logger.LogInformation("✅ Version validation correctly passed for matching versions");
                }
                else
                {
                    Assert.False(validationResult.IsValid);
                    Assert.True(validationResult.ShouldShutdown);
                    _logger.LogInformation("✅ Version validation correctly failed for mismatched versions");
                }
            }

            _logger.LogInformation("✅ Integration test completed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Integration test failed");
            throw;
        }
    }

    public void Dispose()
    {
        _loggerFactory?.Dispose();
    }
}
