using FixedRatioStressTest.Common.Models;
using FixedRatioStressTest.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace FixedRatioStressTest.Core.Tests.Helpers;

/// <summary>
/// Helper class for pool-related testing operations
/// Creates and manages test pools for realistic testing scenarios
/// </summary>
public class PoolTestHelper
{
    private readonly ISolanaClientService _solanaClient;
    private readonly ILogger<PoolTestHelper> _logger;
    private readonly List<string> _createdPoolIds;

    public PoolTestHelper(ISolanaClientService solanaClient, ILogger<PoolTestHelper> logger)
    {
        _solanaClient = solanaClient;
        _logger = logger;
        _createdPoolIds = new List<string>();
    }

    /// <summary>
    /// Creates a test pool with standard configuration
    /// Records pool ID for cleanup
    /// </summary>
    public async Task<PoolState> CreateTestPoolAsync(
        int tokenADecimals = TestConstants.TOKEN_A_DECIMALS,
        int tokenBDecimals = TestConstants.TOKEN_B_DECIMALS,
        ulong ratioNumerator = TestConstants.EXCHANGE_RATIO_NUMERATOR,
        ulong ratioDenominator = TestConstants.EXCHANGE_RATIO_DENOMINATOR)
    {
        try
        {
            _logger.LogInformation("Creating test pool with Token A decimals: {ADecimals}, Token B decimals: {BDecimals}, Ratio: {Num}:{Den}",
                tokenADecimals, tokenBDecimals, ratioNumerator, ratioDenominator);

            var poolParams = new PoolCreationParams
            {
                TokenADecimals = tokenADecimals,
                TokenBDecimals = tokenBDecimals,
                RatioWholeNumber = ratioDenominator / ratioNumerator, // Convert ratio to whole number
                RatioDirection = "a_to_b"
            };

            var poolState = await _solanaClient.CreatePoolAsync(poolParams);
            _createdPoolIds.Add(poolState.PoolId);

            _logger.LogInformation("Created test pool {PoolId} with ratio {Ratio}",
                poolState.PoolId, poolState.RatioDisplay);

            return poolState;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create test pool");
            throw;
        }
    }

    /// <summary>
    /// Creates a pool for extreme ratio testing (large differences in token values)
    /// </summary>
    public async Task<PoolState> CreateExtremeRatioPoolAsync()
    {
        // Create pool with extreme ratio: 1 Token A = 1,000,000 Token B
        return await CreateTestPoolAsync(
            tokenADecimals: 9,      // High precision token
            tokenBDecimals: 2,      // Low precision token (like cents)
            ratioNumerator: 1,
            ratioDenominator: 1_000_000);
    }

    /// <summary>
    /// Attempts to find an existing pool or creates a new one if not found
    /// This simulates the self-healing behavior described in requirements
    /// </summary>
    public async Task<PoolState> GetOrCreatePoolAsync(string? preferredPoolId = null)
    {
        try
        {
            // First, try to get existing pool if specified
            if (!string.IsNullOrEmpty(preferredPoolId))
            {
                try
                {
                    var existingPool = await _solanaClient.GetPoolStateAsync(preferredPoolId);
                    _logger.LogInformation("Found existing pool {PoolId}", preferredPoolId);
                    return existingPool;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Pool {PoolId} not found, will create new pool", preferredPoolId);
                }
            }

            // Check if any pools exist on the system
            var allPools = await _solanaClient.GetAllPoolsAsync();
            if (allPools.Count > 0)
            {
                var existingPool = allPools.First();
                _logger.LogInformation("Using existing pool {PoolId} from system", existingPool.PoolId);
                return existingPool;
            }

            // No pools found, create a new one
            _logger.LogInformation("No pools found, creating new test pool");
            return await CreateTestPoolAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get or create pool");
            throw;
        }
    }

    /// <summary>
    /// Validates that a pool is in the correct state for testing
    /// </summary>
    public async Task<bool> ValidatePoolStateAsync(string poolId)
    {
        try
        {
            var poolState = await _solanaClient.GetPoolStateAsync(poolId);
            
            // Check if pool is paused
            if (poolState.PoolPaused)
            {
                _logger.LogWarning("Pool {PoolId} is paused", poolId);
                return false;
            }

            // Check if swaps are paused
            if (poolState.SwapsPaused)
            {
                _logger.LogWarning("Pool {PoolId} has swaps paused", poolId);
                return false;
            }

            // Validate token addresses exist
            if (string.IsNullOrEmpty(poolState.TokenAMint) || string.IsNullOrEmpty(poolState.TokenBMint))
            {
                _logger.LogError("Pool {PoolId} has invalid token mint addresses", poolId);
                return false;
            }

            // Validate vault addresses exist
            if (string.IsNullOrEmpty(poolState.VaultA) || string.IsNullOrEmpty(poolState.VaultB))
            {
                _logger.LogError("Pool {PoolId} has invalid vault addresses", poolId);
                return false;
            }

            _logger.LogInformation("Pool {PoolId} validation passed", poolId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to validate pool {PoolId}", poolId);
            return false;
        }
    }

    /// <summary>
    /// Waits for pool creation to complete and be confirmed on chain
    /// </summary>
    public async Task<bool> WaitForPoolConfirmationAsync(
        string poolId, 
        int timeoutMs = TestConstants.POOL_CREATION_TIMEOUT_MS)
    {
        var startTime = DateTime.UtcNow;
        var timeout = TimeSpan.FromMilliseconds(timeoutMs);

        while (DateTime.UtcNow - startTime < timeout)
        {
            try
            {
                var poolState = await _solanaClient.GetPoolStateAsync(poolId);
                if (poolState != null && !string.IsNullOrEmpty(poolState.PoolId))
                {
                    _logger.LogInformation("Pool {PoolId} confirmed on chain", poolId);
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Pool {PoolId} not yet confirmed, continuing to wait", poolId);
            }

            await Task.Delay(2000); // Check every 2 seconds
        }

        _logger.LogWarning("Timeout waiting for pool {PoolId} confirmation", poolId);
        return false;
    }

    /// <summary>
    /// Gets pool liquidity information for testing deposit/withdrawal scenarios
    /// </summary>
    public async Task<(ulong tokenALiquidity, ulong tokenBLiquidity)> GetPoolLiquidityAsync(string poolId)
    {
        try
        {
            var poolState = await _solanaClient.GetPoolStateAsync(poolId);
            
            // Get actual token balances in vaults
            var tokenABalance = await _solanaClient.GetTokenBalanceAsync(poolState.VaultA, poolState.TokenAMint);
            var tokenBBalance = await _solanaClient.GetTokenBalanceAsync(poolState.VaultB, poolState.TokenBMint);

            _logger.LogDebug("Pool {PoolId} liquidity: Token A: {TokenA}, Token B: {TokenB}",
                poolId, tokenABalance, tokenBBalance);

            return (tokenABalance, tokenBBalance);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get pool liquidity for {PoolId}", poolId);
            throw;
        }
    }

    /// <summary>
    /// Creates initial liquidity in a pool for testing (if needed)
    /// This helps ensure there's enough liquidity for swap and withdrawal tests
    /// </summary>
    public async Task<bool> EnsurePoolLiquidityAsync(
        string poolId, 
        ulong minTokenAAmount = 1_000_000_000, // 1 token A (9 decimals)
        ulong minTokenBAmount = 1_000_000)     // 1 token B (6 decimals)
    {
        try
        {
            var (tokenALiquidity, tokenBLiquidity) = await GetPoolLiquidityAsync(poolId);

            if (tokenALiquidity < minTokenAAmount || tokenBLiquidity < minTokenBAmount)
            {
                _logger.LogInformation(
                    "Pool {PoolId} needs liquidity. Current: A={TokenA}, B={TokenB}. Required: A>={MinA}, B>={MinB}",
                    poolId, tokenALiquidity, tokenBLiquidity, minTokenAAmount, minTokenBAmount);

                // In a real scenario, we would add liquidity here
                // For now, we'll return false to indicate low liquidity
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to ensure pool liquidity for {PoolId}", poolId);
            return false;
        }
    }

    /// <summary>
    /// Returns all pool IDs created during testing for cleanup or reference
    /// </summary>
    public List<string> GetCreatedPoolIds() => new List<string>(_createdPoolIds);

    /// <summary>
    /// Logs pool information for debugging
    /// </summary>
    public async Task LogPoolInfoAsync(string poolId)
    {
        try
        {
            var poolState = await _solanaClient.GetPoolStateAsync(poolId);
            var (tokenALiquidity, tokenBLiquidity) = await GetPoolLiquidityAsync(poolId);

            _logger.LogInformation(
                "Pool {PoolId} Info: Ratio={Ratio}, Paused={Paused}, SwapsPaused={SwapsPaused}, " +
                "TokenA={TokenAMint}({TokenALiquidity}), TokenB={TokenBMint}({TokenBLiquidity})",
                poolId, poolState.RatioDisplay, poolState.PoolPaused, poolState.SwapsPaused,
                poolState.TokenAMint, tokenALiquidity, poolState.TokenBMint, tokenBLiquidity);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to log pool info for {PoolId}", poolId);
        }
    }
}
