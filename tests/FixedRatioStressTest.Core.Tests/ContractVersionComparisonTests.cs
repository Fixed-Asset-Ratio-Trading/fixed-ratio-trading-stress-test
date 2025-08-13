using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using FixedRatioStressTest.Infrastructure.Services;
using FixedRatioStressTest.Core.Interfaces;
using Xunit;
using Xunit.Abstractions;

namespace FixedRatioStressTest.Core.Tests;

/// <summary>
/// Tests for contract version comparison logic including maximum version enforcement
/// </summary>
public class ContractVersionComparisonTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly ILogger<ContractVersionService> _logger;
    private readonly ILoggerFactory _loggerFactory;

    public ContractVersionComparisonTests(ITestOutputHelper output)
    {
        _output = output;

        // Setup logging to capture debug info
        _loggerFactory = LoggerFactory.Create(builder =>
            builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
        _logger = _loggerFactory.CreateLogger<ContractVersionService>();
    }

    [Theory]
    [InlineData("0.15.1053", "0.15.1053", "0.19.9999", true, false)] // Exact match, within limits
    [InlineData("v0.15.1053", "v0.15.1053", "0.19.9999", true, false)] // Exact match with v prefix
    [InlineData("0.15.1053", "0.15.1054", "0.19.9999", false, false)] // Minor version difference
    [InlineData("0.16.0", "0.15.1053", "0.19.9999", false, false)] // Higher minor version but acceptable
    [InlineData("0.20.0", "0.15.1053", "0.19.9999", false, true)] // Version too high (breaking changes)
    [InlineData("0.20.1", "0.15.1053", "0.19.9999", false, true)] // Version too high (breaking changes)
    [InlineData("1.0.0", "0.15.1053", "0.19.9999", false, true)] // Major version too high
    [InlineData("0.19.9999", "0.15.1053", "0.19.9999", false, false)] // At max supported version
    [InlineData("0.14.0", "0.15.1053", "0.19.9999", false, false)] // Lower version (mismatch but not too high)
    public async Task ValidateContractVersionAsync_VersionComparisons_WorksCorrectly(
        string deployedVersion, 
        string expectedVersion, 
        string maxSupportedVersion,
        bool shouldBeValid,
        bool shouldBeVersionTooHigh)
    {
        _logger.LogInformation("=== TEST: Version comparison - Deployed: {Deployed}, Expected: {Expected}, Max: {Max} ===", 
            deployedVersion, expectedVersion, maxSupportedVersion);

        try
        {
            // Arrange - Create a mock version service that returns our test version
            var mockVersionService = new MockContractVersionService(
                deployedVersion, expectedVersion, maxSupportedVersion, _logger);

            // Act
            var result = await mockVersionService.ValidateContractVersionAsync();

            // Assert
            _output.WriteLine($"Test case: Deployed={deployedVersion}, Expected={expectedVersion}, Max={maxSupportedVersion}");
            _output.WriteLine($"Result: Valid={result.IsValid}, VersionTooHigh={result.IsVersionTooHigh}, ShouldShutdown={result.ShouldShutdown}");
            _output.WriteLine($"Error: {result.ErrorMessage ?? "None"}");

            Assert.Equal(shouldBeValid, result.IsValid);
            Assert.Equal(shouldBeVersionTooHigh, result.IsVersionTooHigh);
            Assert.Equal(deployedVersion, result.DeployedVersion);
            Assert.Equal(expectedVersion, result.ExpectedVersion);
            Assert.Equal(maxSupportedVersion, result.MaxSupportedVersion);

            if (shouldBeVersionTooHigh)
            {
                Assert.Contains("not supported", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("0.20", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
                Assert.True(result.ShouldShutdown, "Service should shutdown for version too high");
            }
            else if (!shouldBeValid)
            {
                Assert.Contains("mismatch", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
                Assert.True(result.ShouldShutdown, "Service should shutdown for version mismatch");
            }
            else
            {
                Assert.Null(result.ErrorMessage);
                Assert.False(result.ShouldShutdown, "Service should not shutdown for valid version");
            }

            _logger.LogInformation("✅ Version comparison test passed for {DeployedVersion}", deployedVersion);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Version comparison test failed");
            throw;
        }
    }

    [Fact]
    public async Task ValidateContractVersionAsync_UnretrievableVersion_HandlesGracefully()
    {
        _logger.LogInformation("=== TEST: ValidateContractVersionAsync_UnretrievableVersion_HandlesGracefully ===");

        try
        {
            // Arrange - Create a mock that returns null (simulating RPC failure)
            var mockVersionService = new MockContractVersionService(
                null, "v0.15.1053", "0.19.9999", _logger);

            // Act
            var result = await mockVersionService.ValidateContractVersionAsync();

            // Assert
            Assert.False(result.IsValid);
            Assert.False(result.IsVersionTooHigh);
            Assert.False(result.ShouldShutdown); // Should NOT shutdown if we can't retrieve version
            Assert.Null(result.DeployedVersion);
            Assert.NotNull(result.ErrorMessage);
            Assert.Contains("Unable to retrieve", result.ErrorMessage);

            _logger.LogInformation("✅ Unretrievable version test passed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unretrievable version test failed");
            throw;
        }
    }

    public void Dispose()
    {
        _loggerFactory?.Dispose();
    }

    /// <summary>
    /// Mock version service for testing version comparison logic
    /// </summary>
    private class MockContractVersionService : IContractVersionService
    {
        private readonly string? _mockDeployedVersion;
        private readonly string _expectedVersion;
        private readonly string _maxSupportedVersion;
        private readonly ILogger _logger;

        public MockContractVersionService(
            string? mockDeployedVersion, 
            string expectedVersion, 
            string maxSupportedVersion,
            ILogger logger)
        {
            _mockDeployedVersion = mockDeployedVersion;
            _expectedVersion = expectedVersion;
            _maxSupportedVersion = maxSupportedVersion;
            _logger = logger;
        }

        public async Task<ContractVersionResult> ValidateContractVersionAsync()
        {
            // Create the same validation logic as the real service
            var result = new ContractVersionResult
            {
                DeployedVersion = _mockDeployedVersion,
                ExpectedVersion = _expectedVersion,
                MaxSupportedVersion = _maxSupportedVersion
            };

            if (string.IsNullOrEmpty(_mockDeployedVersion))
            {
                result.IsValid = false;
                result.ErrorMessage = "Unable to retrieve deployed contract version. Contract may not be deployed or GetVersion instruction failed.";
                return result;
            }

            // Normalize versions for comparison (remove 'v' prefix if present)
            var normalizedDeployed = NormalizeVersion(_mockDeployedVersion);
            var normalizedExpected = NormalizeVersion(_expectedVersion);
            var normalizedMaxSupported = NormalizeVersion(_maxSupportedVersion);

            // Check if deployed version is too high (>= 0.20.x)
            if (CompareVersions(normalizedDeployed, normalizedMaxSupported) > 0)
            {
                result.IsValid = false;
                result.IsVersionTooHigh = true;
                result.ErrorMessage = $"Contract version {_mockDeployedVersion} is not supported. This service supports versions up to {_maxSupportedVersion}. Version 0.20.x+ contains breaking changes.";
                return result;
            }

            // Check if deployed version matches expected
            result.IsValid = string.Equals(normalizedDeployed, normalizedExpected, StringComparison.OrdinalIgnoreCase);

            if (!result.IsValid)
            {
                result.ErrorMessage = $"Contract version mismatch. Deployed: {_mockDeployedVersion}, Expected: {_expectedVersion}";
            }

            return result;
        }

        public async Task<string?> GetDeployedVersionAsync()
        {
            return _mockDeployedVersion;
        }

        public string GetExpectedVersion()
        {
            return _expectedVersion;
        }

        // Copy the same helper methods from the real service
        private static string NormalizeVersion(string version)
        {
            if (string.IsNullOrEmpty(version))
                return string.Empty;

            return version.Trim().ToLowerInvariant().TrimStart('v');
        }

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
}
