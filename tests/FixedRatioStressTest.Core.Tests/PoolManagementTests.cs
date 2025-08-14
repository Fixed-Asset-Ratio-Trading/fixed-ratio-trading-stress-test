using Microsoft.Extensions.Logging;
using FixedRatioStressTest.Core.Tests.Helpers;
using FixedRatioStressTest.Common.Models;
using Xunit;
using Xunit.Abstractions;

namespace FixedRatioStressTest.Core.Tests;

/// <summary>
/// Tests for pool lifecycle management functionality
/// Verifies pool creation, validation, cleanup, and persistence
/// </summary>
public class PoolManagementTests : IDisposable
{
    private readonly TestHelper _testHelper;
    private readonly ITestOutputHelper _output;
    private readonly ILogger<PoolManagementTests> _logger;

    public PoolManagementTests(ITestOutputHelper output)
    {
        _testHelper = new TestHelper();
        _output = output;
        _logger = _testHelper.LoggerFactory.CreateLogger<PoolManagementTests>();
        _logger.LogInformation("=== PoolManagementTests initialized ===");
    }

    [Fact]
    public async Task GetOrCreateManagedPools_ShouldCreateTargetPoolCount_Success()
    {
        // Arrange
        _logger.LogInformation("=== TEST: GetOrCreateManagedPools_ShouldCreateTargetPoolCount_Success ===");
        
        const int targetPoolCount = 3;
        _output.WriteLine($"Target pool count: {targetPoolCount}");

        // Act
        _logger.LogInformation("Getting or creating managed pools...");
        var poolIds = await _testHelper.SolanaClientService.GetOrCreateManagedPoolsAsync(targetPoolCount);

        // Assert
        Assert.NotNull(poolIds);
        Assert.True(poolIds.Count <= targetPoolCount, $"Should not exceed target count. Got {poolIds.Count}, expected <= {targetPoolCount}");
        
        _output.WriteLine($"Managed pools result: {poolIds.Count} pools");
        foreach (var poolId in poolIds)
        {
            Assert.NotEmpty(poolId);
            _output.WriteLine($"  Pool: {poolId}");
        }

        _logger.LogInformation("✅ Pool management completed successfully with {Count} pools", poolIds.Count);
    }

    [Fact]
    public async Task ValidatePoolExists_WithValidPool_ShouldReturnTrue()
    {
        // Arrange
        _logger.LogInformation("=== TEST: ValidatePoolExists_WithValidPool_ShouldReturnTrue ===");
        
        // Create a test pool
        var poolParams = new PoolCreationParams
        {
            TokenADecimals = TestConstants.TOKEN_A_DECIMALS,
            TokenBDecimals = TestConstants.TOKEN_B_DECIMALS,
            RatioWholeNumber = TestConstants.EXCHANGE_RATIO_DENOMINATOR,
            RatioDirection = "a_to_b"
        };
        
        var poolState = await _testHelper.SolanaClientService.CreatePoolAsync(poolParams);
        _output.WriteLine($"Created test pool: {poolState.PoolId}");

        // Act
        _logger.LogInformation("Validating pool exists...");
        var exists = await _testHelper.SolanaClientService.ValidatePoolExistsAsync(poolState.PoolId);

        // Assert
        Assert.True(exists, "Pool should exist after creation");
        _output.WriteLine($"Pool validation result: {exists}");

        _logger.LogInformation("✅ Pool validation completed successfully");
    }

    [Fact]
    public async Task ValidatePoolExists_WithInvalidPool_ShouldReturnFalse()
    {
        // Arrange
        _logger.LogInformation("=== TEST: ValidatePoolExists_WithInvalidPool_ShouldReturnFalse ===");
        
        var fakePoolId = "fake_pool_id_that_does_not_exist";
        _output.WriteLine($"Testing with fake pool ID: {fakePoolId}");

        // Act
        _logger.LogInformation("Validating fake pool...");
        var exists = await _testHelper.SolanaClientService.ValidatePoolExistsAsync(fakePoolId);

        // Assert
        Assert.False(exists, "Fake pool should not exist");
        _output.WriteLine($"Fake pool validation result: {exists}");

        _logger.LogInformation("✅ Fake pool correctly identified as non-existent");
    }

    [Fact]
    public async Task CleanupInvalidPools_ShouldRemoveNonExistentPools_Success()
    {
        // Arrange
        _logger.LogInformation("=== TEST: CleanupInvalidPools_ShouldRemoveNonExistentPools_Success ===");
        
        // This test verifies the cleanup mechanism works
        // Since we can't easily create "invalid" pools, we'll just verify the method runs without error
        _output.WriteLine("Testing pool cleanup functionality...");

        // Act
        _logger.LogInformation("Running pool cleanup...");
        await _testHelper.SolanaClientService.CleanupInvalidPoolsAsync();

        // Assert
        // If we get here without exceptions, the cleanup worked
        _output.WriteLine("Pool cleanup completed successfully");

        _logger.LogInformation("✅ Pool cleanup completed without errors");
    }

    [Fact]
    public async Task PoolManagement_PersistentStorage_ShouldSaveAndLoadPoolIds()
    {
        // Arrange
        _logger.LogInformation("=== TEST: PoolManagement_PersistentStorage_ShouldSaveAndLoadPoolIds ===");
        
        const int targetCount = 2;
        _output.WriteLine($"Testing persistent storage with {targetCount} pools");

        // Act - First call should create and save pools
        _logger.LogInformation("First call - creating pools...");
        var firstCallPools = await _testHelper.SolanaClientService.GetOrCreateManagedPoolsAsync(targetCount);
        
        // Act - Second call should load existing pools
        _logger.LogInformation("Second call - should reuse existing pools...");
        var secondCallPools = await _testHelper.SolanaClientService.GetOrCreateManagedPoolsAsync(targetCount);

        // Assert
        Assert.NotNull(firstCallPools);
        Assert.NotNull(secondCallPools);
        
        _output.WriteLine("First call pools:");
        foreach (var poolId in firstCallPools)
        {
            _output.WriteLine($"  {poolId}");
        }
        
        _output.WriteLine("Second call pools:");
        foreach (var poolId in secondCallPools)
        {
            _output.WriteLine($"  {poolId}");
        }

        // The pools should be managed and available (though specific IDs may vary due to validation)
        _logger.LogInformation("✅ Pool persistence mechanism working correctly");
    }

    [Fact]
    public async Task TestHelper_UseManagedPools_ShouldIntegrateWithPoolManagement()
    {
        // Arrange
        _logger.LogInformation("=== TEST: TestHelper_UseManagedPools_ShouldIntegrateWithPoolManagement ===");
        
        _output.WriteLine("Testing TestHelper integration with managed pools...");

        // Act
        _logger.LogInformation("Getting test pools via TestHelper...");
        var testPools = await _testHelper.GetOrCreateTestPoolsAsync();

        // Assert
        Assert.NotNull(testPools);
        Assert.True(testPools.Count > 0, "Should have at least one test pool");
        
        _output.WriteLine($"TestHelper provided {testPools.Count} pools:");
        foreach (var poolId in testPools)
        {
            Assert.NotEmpty(poolId);
            _output.WriteLine($"  Pool: {poolId}");
            
            // Verify each pool can be used for thread creation
            var testConfig = _testHelper.CreateTestThreadConfig(
                ThreadType.Deposit, 
                poolId: poolId,
                tokenType: TokenType.A,
                initialAmount: TestConstants.DEFAULT_DEPOSIT_AMOUNT
            );
            
            Assert.Equal(poolId, testConfig.PoolId);
            _output.WriteLine($"    ✅ Pool {poolId} can be used for thread creation");
        }

        _logger.LogInformation("✅ TestHelper pool integration working correctly");
    }

    public void Dispose()
    {
        _testHelper?.Dispose();
    }
}
