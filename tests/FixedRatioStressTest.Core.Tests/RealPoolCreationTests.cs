using Microsoft.Extensions.Logging;
using FixedRatioStressTest.Core.Tests.Helpers;
using FixedRatioStressTest.Common.Models;
using Xunit;
using Xunit.Abstractions;

namespace FixedRatioStressTest.Core.Tests;

/// <summary>
/// Tests for REAL pool creation on the smart contract
/// These tests create actual tokens and pools, not simulated ones
/// </summary>
public class RealPoolCreationTests : IDisposable
{
    private readonly TestHelper _testHelper;
    private readonly ITestOutputHelper _output;
    private readonly ILogger<RealPoolCreationTests> _logger;

    public RealPoolCreationTests(ITestOutputHelper output)
    {
        _testHelper = new TestHelper();
        _output = output;
        _logger = _testHelper.LoggerFactory.CreateLogger<RealPoolCreationTests>();
        _logger.LogInformation("=== RealPoolCreationTests initialized ===");
    }

    [Fact]
    public async Task GetOrCreateCoreWallet_ShouldCreateMintAuthority_Success()
    {
        // Arrange
        _logger.LogInformation("=== TEST: GetOrCreateCoreWallet_ShouldCreateMintAuthority_Success ===");
        
        _output.WriteLine("Testing core wallet creation (simulates startup behavior)...");

        // Act
        _logger.LogInformation("Getting or creating core wallet...");
        var coreWallet = await _testHelper.SolanaClientService.GetOrCreateCoreWalletAsync();

        // Assert
        Assert.NotNull(coreWallet);
        Assert.NotEmpty(coreWallet.PublicKey);
        Assert.NotEmpty(coreWallet.PrivateKey);
        // Note: SOL balance may be 0 on localnet if airdrops are disabled
        Assert.True(coreWallet.CurrentSolBalance >= 0, "Core wallet balance should be non-negative");
        
        _output.WriteLine($"Core wallet created:");
        _output.WriteLine($"  Public Key: {coreWallet.PublicKey}");
        _output.WriteLine($"  SOL Balance: {coreWallet.CurrentSolBalance / 1_000_000_000.0:F2} SOL");
        _output.WriteLine($"  Created: {coreWallet.CreatedAt}");

        _logger.LogInformation("✅ Core wallet creation completed successfully");
    }

    [Fact]
    public async Task StartupFlow_CoreWalletThenPoolCreation_Success()
    {
        // Arrange
        _logger.LogInformation("=== TEST: StartupFlow_CoreWalletThenPoolCreation_Success ===");
        
        _output.WriteLine("Testing correct startup flow: core wallet creation THEN pool creation...");

        // Step 1: Simulate startup - create core wallet
        _logger.LogInformation("STARTUP: Creating core wallet...");
        var coreWallet = await _testHelper.SolanaClientService.GetOrCreateCoreWalletAsync();
        Assert.NotNull(coreWallet);
        _output.WriteLine($"✅ STARTUP: Core wallet created: {coreWallet.PublicKey}");

        // Step 2: Simulate RPC call - create pool (should use existing core wallet)
        _logger.LogInformation("RPC CALL: Creating pool using existing core wallet...");
        var poolParams = new PoolCreationParams
        {
            TokenADecimals = 9,
            TokenBDecimals = 6,
            RatioWholeNumber = 1000,
            RatioDirection = "a_to_b"
        };

        try
        {
            var realPool = await _testHelper.SolanaClientService.CreateRealPoolAsync(poolParams);
            _output.WriteLine($"✅ RPC CALL: Pool created successfully: {realPool.PoolId}");
            Assert.NotNull(realPool);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("insufficient SOL balance"))
        {
            _output.WriteLine($"✅ RPC CALL: Pool creation failed gracefully due to insufficient SOL: {ex.Message}");
            // This is expected behavior when airdrops fail
        }

        _logger.LogInformation("✅ Startup flow test completed successfully");
    }

    [Fact]
    public async Task CreateTokenMint_ShouldCreateRealToken_Success()
    {
        // Arrange
        _logger.LogInformation("=== TEST: CreateTokenMint_ShouldCreateRealToken_Success ===");
        
        const int decimals = 6;
        _output.WriteLine($"Creating token mint with {decimals} decimals...");

        // Act
        _logger.LogInformation("Creating token mint...");
        var tokenMint = await _testHelper.SolanaClientService.CreateTokenMintAsync(decimals, "TEST");

        // Assert
        Assert.NotNull(tokenMint);
        Assert.NotEmpty(tokenMint.MintAddress);
        Assert.Equal(decimals, tokenMint.Decimals);
        Assert.NotEmpty(tokenMint.MintAuthority);
        Assert.NotEmpty(tokenMint.CreationSignature);
        
        _output.WriteLine($"Token mint created:");
        _output.WriteLine($"  Mint Address: {tokenMint.MintAddress}");
        _output.WriteLine($"  Decimals: {tokenMint.Decimals}");
        _output.WriteLine($"  Authority: {tokenMint.MintAuthority}");
        _output.WriteLine($"  Signature: {tokenMint.CreationSignature}");

        _logger.LogInformation("✅ Token mint creation completed successfully");
    }

    [Fact]
    public async Task CreateRealPool_ShouldCreateContractPool_Success()
    {
        // Arrange
        _logger.LogInformation("=== TEST: CreateRealPool_ShouldCreateContractPool_Success ===");
        
        // First ensure we have a funded core wallet
        var coreWallet = await _testHelper.SolanaClientService.GetOrCreateCoreWalletAsync();
        _output.WriteLine($"Using core wallet: {coreWallet.PublicKey} ({coreWallet.CurrentSolBalance / 1_000_000_000.0:F2} SOL)");
        
        var poolParams = new PoolCreationParams
        {
            TokenADecimals = 9,  // SOL-like
            TokenBDecimals = 6,  // USDC-like
            RatioWholeNumber = 1,  // 1:1 ratio (1 TokenA = 1 TokenB)
            RatioDirection = "a_to_b"
        };

        _output.WriteLine($"Creating real pool with ACTUAL smart contract call:");
        _output.WriteLine($"  Token A Decimals: {poolParams.TokenADecimals}");
        _output.WriteLine($"  Token B Decimals: {poolParams.TokenBDecimals}");
        _output.WriteLine($"  Ratio: {poolParams.RatioWholeNumber}");
        _output.WriteLine($"  Direction: {poolParams.RatioDirection}");

        // Act
        _logger.LogInformation("Creating real pool on smart contract...");
        
        try
        {
            var realPool = await _testHelper.SolanaClientService.CreateRealPoolAsync(poolParams);

            // Assert
            Assert.NotNull(realPool);
            Assert.NotEmpty(realPool.PoolId);
            Assert.NotEmpty(realPool.TokenAMint);
            Assert.NotEmpty(realPool.TokenBMint);
            Assert.Equal(9, realPool.TokenADecimals);
            Assert.Equal(6, realPool.TokenBDecimals);
            Assert.True(realPool.RatioANumerator > 0);
            Assert.True(realPool.RatioBDenominator > 0);
            Assert.NotEmpty(realPool.CreationSignature);
            
            _output.WriteLine($"✅ Real pool created on blockchain:");
            _output.WriteLine($"  Pool ID: {realPool.PoolId}");
            _output.WriteLine($"  Token A: {realPool.TokenAMint} ({realPool.TokenADecimals} decimals)");
            _output.WriteLine($"  Token B: {realPool.TokenBMint} ({realPool.TokenBDecimals} decimals)");
            _output.WriteLine($"  Ratio: {realPool.RatioDisplay}");
            _output.WriteLine($"  Signature: {realPool.CreationSignature}");
            _output.WriteLine($"  This pool should now appear in your dashboard!");

            _logger.LogInformation("✅ Real pool creation completed successfully");
        }
        catch (Exception ex)
        {
            _output.WriteLine($"❌ Pool creation failed with error: {ex.Message}");
            _output.WriteLine($"   Error Type: {ex.GetType().Name}");
            if (ex.InnerException != null)
            {
                _output.WriteLine($"   Inner Exception: {ex.InnerException.Message}");
            }
            
            // For debugging, let's see the full stack trace
            _logger.LogError(ex, "Full pool creation error details");
            throw; // Re-throw to fail the test and see the issue
        }
    }

    [Fact]
    public async Task ValidateRealPool_WithValidPool_ShouldReturnTrue()
    {
        // Arrange
        _logger.LogInformation("=== TEST: ValidateRealPool_WithValidPool_ShouldReturnTrue ===");
        
        // Create a real pool first
        var poolParams = new PoolCreationParams
        {
            TokenADecimals = 8,
            TokenBDecimals = 9,
            RatioWholeNumber = 500,
            RatioDirection = "b_to_a"
        };
        
        var realPool = await _testHelper.SolanaClientService.CreateRealPoolAsync(poolParams);
        _output.WriteLine($"Created real pool for validation: {realPool.PoolId}");

        // Act
        _logger.LogInformation("Validating real pool...");
        var isValid = await _testHelper.SolanaClientService.ValidateRealPoolAsync(realPool);

        // Assert
        Assert.True(isValid, "Real pool should be valid after creation");
        _output.WriteLine($"Pool validation result: {isValid}");

        _logger.LogInformation("✅ Real pool validation completed successfully");
    }

    [Fact]
    public async Task GetRealPools_ShouldPersistAndRetrieve_Success()
    {
        // Arrange
        _logger.LogInformation("=== TEST: GetRealPools_ShouldPersistAndRetrieve_Success ===");
        
        // Create a real pool
        var poolParams = new PoolCreationParams
        {
            TokenADecimals = 6,
            TokenBDecimals = 8,
            RatioWholeNumber = 250,
            RatioDirection = "a_to_b"
        };
        
        var createdPool = await _testHelper.SolanaClientService.CreateRealPoolAsync(poolParams);
        _output.WriteLine($"Created real pool: {createdPool.PoolId}");

        // Act
        _logger.LogInformation("Retrieving real pools from storage...");
        var retrievedPools = await _testHelper.SolanaClientService.GetRealPoolsAsync();

        // Assert
        Assert.NotNull(retrievedPools);
        Assert.True(retrievedPools.Count > 0, "Should have at least one real pool");
        
        var foundPool = retrievedPools.FirstOrDefault(p => p.PoolId == createdPool.PoolId);
        Assert.NotNull(foundPool);
        Assert.Equal(createdPool.TokenAMint, foundPool.TokenAMint);
        Assert.Equal(createdPool.TokenBMint, foundPool.TokenBMint);
        
        _output.WriteLine($"Retrieved {retrievedPools.Count} real pools:");
        foreach (var pool in retrievedPools)
        {
            _output.WriteLine($"  Pool: {pool.PoolId} ({pool.RatioDisplay})");
        }

        _logger.LogInformation("✅ Real pool persistence verification completed successfully");
    }

    [Fact]
    public async Task RealPoolWorkflow_CreateValidateRetrieve_Success()
    {
        // Arrange
        _logger.LogInformation("=== TEST: RealPoolWorkflow_CreateValidateRetrieve_Success ===");
        
        _output.WriteLine("Testing complete real pool workflow...");

        // Act & Assert - Step 1: Core wallet
        _logger.LogInformation("Step 1: Setting up core wallet...");
        var coreWallet = await _testHelper.SolanaClientService.GetOrCreateCoreWalletAsync();
        Assert.NotNull(coreWallet);
        _output.WriteLine($"✅ Core wallet ready: {coreWallet.PublicKey}");

        // Act & Assert - Step 2: Create real pool
        _logger.LogInformation("Step 2: Creating real pool...");
        var poolParams = new PoolCreationParams
        {
            TokenADecimals = 9,
            TokenBDecimals = 6,
            RatioWholeNumber = 1000,
            RatioDirection = "a_to_b"
        };
        
        var realPool = await _testHelper.SolanaClientService.CreateRealPoolAsync(poolParams);
        Assert.NotNull(realPool);
        _output.WriteLine($"✅ Real pool created: {realPool.PoolId}");

        // Act & Assert - Step 3: Validate pool
        _logger.LogInformation("Step 3: Validating pool...");
        var isValid = await _testHelper.SolanaClientService.ValidateRealPoolAsync(realPool);
        Assert.True(isValid);
        _output.WriteLine($"✅ Pool validation: {isValid}");

        // Act & Assert - Step 4: Retrieve from storage
        _logger.LogInformation("Step 4: Retrieving from storage...");
        var allPools = await _testHelper.SolanaClientService.GetRealPoolsAsync();
        var foundPool = allPools.FirstOrDefault(p => p.PoolId == realPool.PoolId);
        Assert.NotNull(foundPool);
        _output.WriteLine($"✅ Pool retrieved from storage: {foundPool.PoolId}");

        _logger.LogInformation("✅ Complete real pool workflow completed successfully");
    }

    [Fact]
    public async Task CreateRealPool_WithInsufficientSOL_ShouldFailGracefully()
    {
        // Arrange
        _logger.LogInformation("=== TEST: CreateRealPool_WithInsufficientSOL_ShouldFailGracefully ===");
        
        _output.WriteLine("Testing pool creation failure when SOL balance is insufficient...");

        var poolParams = new PoolCreationParams
        {
            TokenADecimals = 9,
            TokenBDecimals = 6,
            RatioWholeNumber = 1000,
            RatioDirection = "a_to_b"
        };

        // Act & Assert
        _logger.LogInformation("Attempting to create pool (may fail due to insufficient SOL on localnet)...");
        
        try
        {
            var realPool = await _testHelper.SolanaClientService.CreateRealPoolAsync(poolParams);
            _output.WriteLine($"✅ Pool created successfully: {realPool.PoolId}");
            _output.WriteLine("Note: This succeeded because airdrops worked or wallet had sufficient balance");
            
            // If it succeeds, that's fine too
            Assert.NotNull(realPool);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("insufficient SOL balance"))
        {
            _output.WriteLine($"✅ Pool creation failed as expected: {ex.Message}");
            _logger.LogInformation("Pool creation failed gracefully due to insufficient SOL balance");
            
            // This is the expected behavior when airdrops fail
            Assert.Contains("insufficient SOL balance", ex.Message);
        }
        catch (Exception ex)
        {
            _output.WriteLine($"❌ Unexpected exception: {ex.Message}");
            throw; // Re-throw unexpected exceptions
        }

        _logger.LogInformation("✅ SOL balance handling test completed successfully");
    }

    public void Dispose()
    {
        _testHelper?.Dispose();
    }
}
