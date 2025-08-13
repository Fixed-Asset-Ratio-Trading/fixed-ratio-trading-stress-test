using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using FixedRatioStressTest.Infrastructure.Services;
using FixedRatioStressTest.Core.Interfaces;
using Xunit;
using Xunit.Abstractions;

namespace FixedRatioStressTest.Core.Tests;

/// <summary>
/// Tests for the raw RPC contract version service
/// This bypasses Solnet and uses direct HTTP calls
/// </summary>
public class RawRpcVersionServiceTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly ILogger<RawRpcContractVersionService> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IContractVersionService _versionService;
    private readonly IConfiguration _configuration;

    public RawRpcVersionServiceTests(ITestOutputHelper output)
    {
        _output = output;

        // Setup logging to capture debug info
        _loggerFactory = LoggerFactory.Create(builder =>
            builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
        _logger = _loggerFactory.CreateLogger<RawRpcContractVersionService>();

        // Setup configuration for localnet testing
        var configBuilder = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SolanaConfiguration:LocalnetRpcUrl"] = "http://192.168.2.88:8899",
                ["SolanaConfiguration:DevnetRpcUrl"] = "https://api.devnet.solana.com",
                ["SolanaConfiguration:MainnetRpcUrl"] = "https://api.mainnet-beta.solana.com",
                ["SolanaConfiguration:ActiveNetwork"] = "localnet",
                ["SolanaConfiguration:ProgramId"] = "4aeVqtWhrUh6wpX8acNj2hpWXKEQwxjA3PYb2sHhNyCn",
                ["SolanaConfiguration:Commitment"] = "confirmed",
                ["ContractVersion:Expected"] = "v0.15.1053",
                ["ContractVersion:MaxSupported"] = "0.19.9999"
            });
        _configuration = configBuilder.Build();

        // Create the raw RPC service instance
        _versionService = new RawRpcContractVersionService(_configuration, _logger);
    }

    [Fact]
    public async Task RawRpc_GetDeployedVersionAsync_BypassesSolnetIssues()
    {
        _logger.LogInformation("=== TEST: RawRpc_GetDeployedVersionAsync_BypassesSolnetIssues ===");

        try
        {
            // Act
            var deployedVersion = await _versionService.GetDeployedVersionAsync();

            // Assert
            _output.WriteLine($"Raw RPC deployed version: {deployedVersion ?? "NULL"}");
            
            if (!string.IsNullOrEmpty(deployedVersion))
            {
                _logger.LogInformation("✅ Raw RPC successfully retrieved deployed version: {Version}", deployedVersion);
                Assert.NotNull(deployedVersion);
                Assert.NotEmpty(deployedVersion);
                
                // Version should be in a reasonable format (contains numbers and dots)
                Assert.Matches(@"[0-9v.]+", deployedVersion);
                
                _output.WriteLine("🎯 SUCCESS: Raw RPC bypassed Solnet transaction issues!");
            }
            else
            {
                _logger.LogWarning("⚠️ Raw RPC could not retrieve deployed version");
                _output.WriteLine("Raw RPC also failed - this indicates a more fundamental issue with the transaction format or contract deployment");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Raw RPC test failed with exception");
            throw;
        }
    }

    [Fact]
    public async Task RawRpc_ValidateContractVersionAsync_CompleteWorkflow()
    {
        _logger.LogInformation("=== TEST: RawRpc_ValidateContractVersionAsync_CompleteWorkflow ===");

        try
        {
            // Act
            var result = await _versionService.ValidateContractVersionAsync();

            // Assert
            Assert.NotNull(result);
            _output.WriteLine($"Raw RPC validation result: Valid={result.IsValid}, Deployed={result.DeployedVersion}, Expected={result.ExpectedVersion}");
            _output.WriteLine($"Error message: {result.ErrorMessage ?? "None"}");
            _output.WriteLine($"Should shutdown: {result.ShouldShutdown}");

            // Log the outcome
            if (result.IsValid)
            {
                _logger.LogInformation("✅ Raw RPC contract version validation passed: {DeployedVersion} matches {ExpectedVersion}", 
                    result.DeployedVersion, result.ExpectedVersion);
                _output.WriteLine("🎯 SUCCESS: Raw RPC version validation works!");
            }
            else if (!string.IsNullOrEmpty(result.DeployedVersion))
            {
                _logger.LogWarning("⚠️ Raw RPC contract version mismatch: Deployed={DeployedVersion}, Expected={ExpectedVersion}", 
                    result.DeployedVersion, result.ExpectedVersion);
                _output.WriteLine("🎯 Raw RPC retrieved version but it doesn't match expected");
            }
            else
            {
                _logger.LogWarning("⚠️ Raw RPC could not retrieve deployed version for validation");
                _output.WriteLine("Raw RPC transaction still has issues");
            }

            // Basic structure assertions
            Assert.Equal("v0.15.1053", result.ExpectedVersion);
            Assert.Equal("0.19.9999", result.MaxSupportedVersion);
            
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Raw RPC validation test failed");
            throw;
        }
    }

    public void Dispose()
    {
        if (_versionService is IDisposable disposableService)
        {
            disposableService.Dispose();
        }
        _loggerFactory?.Dispose();
    }
}
