using Xunit;
using FixedRatioStressTest.Common.Models;
using Microsoft.Extensions.Logging;

namespace FixedRatioStressTest.Core.Tests;

public class PoolReuseTests : IDisposable
{
    private readonly Helpers.TestHelper _testHelper;
    private readonly ILogger<PoolReuseTests> _logger;
    
    public PoolReuseTests()
    {
        _testHelper = new Helpers.TestHelper();
        _logger = _testHelper.LoggerFactory.CreateLogger<PoolReuseTests>();
    }
    
    public void Dispose() => _testHelper.Dispose();
    
    [Fact]
    public async Task ImportExistingPools_ShouldReuseInsteadOfCreatingNew()
    {
        // Arrange - Import existing pools from dashboard
        var existingPools = new List<RealPoolData>
        {
            new RealPoolData
            {
                PoolId = "5aSkBPrzXcQi5pABsNxwuQVtreMCNU9iDYHeHhCawixm",
                TokenAMint = "3R5dixSUaAARbs45Cuq3Mxt7SLpztZi4ZtqFqdtqo7DN",
                TokenBMint = "BVyjcnkZ9fnUYS8XpisN2P7AGjmQjNubPjkRBAwGY2M6",
                TokenADecimals = 9,
                TokenBDecimals = 6,
                RatioANumerator = 1000000000,
                RatioBDenominator = 2000000,
                CreationSignature = "dashboard_import",
                CreatedAt = DateTime.UtcNow
            }
        };
        
        // Save the imported pools
        foreach (var importedPool in existingPools)
        {
            await _testHelper.StorageService.SaveRealPoolAsync(importedPool);
            _logger.LogInformation("Imported pool: {PoolId}", importedPool.PoolId);
        }
        
        // Act - Request to create a pool with same parameters
        var poolParams = new PoolCreationParams
        {
            TokenADecimals = 9,
            TokenBDecimals = 6,
            RatioDirection = PoolCreationParams.RatioDirectionType.A_TO_B,
            RatioWholeNumber = 2
        };
        
        var pool = await _testHelper.SolanaClientService.CreateRealPoolAsync(poolParams);
        
        // Assert - Should reuse existing pool
        Assert.Equal("5aSkBPrzXcQi5pABsNxwuQVtreMCNU9iDYHeHhCawixm", pool.PoolId);
        Assert.Equal("3R5dixSUaAARbs45Cuq3Mxt7SLpztZi4ZtqFqdtqo7DN", pool.TokenAMint);
        Assert.Equal("BVyjcnkZ9fnUYS8XpisN2P7AGjmQjNubPjkRBAwGY2M6", pool.TokenBMint);
        
        _logger.LogInformation("✅ Successfully reused existing pool instead of creating new one");
    }
}
