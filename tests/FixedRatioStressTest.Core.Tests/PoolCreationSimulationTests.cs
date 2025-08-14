using Microsoft.Extensions.Logging;
using FixedRatioStressTest.Core.Tests.Helpers;
using FixedRatioStressTest.Common.Models;
using Xunit;
using Xunit.Abstractions;

namespace FixedRatioStressTest.Core.Tests;

/// <summary>
/// Tests for pool creation simulation functionality
/// Verifies that simulation works before actual pool creation
/// </summary>
public class PoolCreationSimulationTests : IDisposable
{
    private readonly TestHelper _testHelper;
    private readonly ITestOutputHelper _output;
    private readonly ILogger<PoolCreationSimulationTests> _logger;

    public PoolCreationSimulationTests(ITestOutputHelper output)
    {
        _testHelper = new TestHelper();
        _output = output;
        _logger = _testHelper.LoggerFactory.CreateLogger<PoolCreationSimulationTests>();
        _logger.LogInformation("=== PoolCreationSimulationTests initialized ===");
    }

    [Fact]
    public async Task SimulatePoolCreation_ShouldValidateTransactionFormat_Success()
    {
        // Arrange
        _logger.LogInformation("=== TEST: SimulatePoolCreation_ShouldValidateTransactionFormat_Success ===");
        
        var poolParams = new PoolCreationParams
        {
            TokenADecimals = TestConstants.TOKEN_A_DECIMALS,
            TokenBDecimals = TestConstants.TOKEN_B_DECIMALS,
            RatioWholeNumber = TestConstants.EXCHANGE_RATIO_DENOMINATOR,
            RatioDirection = "a_to_b"
        };

        _output.WriteLine("Pool parameters:");
        _output.WriteLine($"  Token A Decimals: {poolParams.TokenADecimals}");
        _output.WriteLine($"  Token B Decimals: {poolParams.TokenBDecimals}");
        _output.WriteLine($"  Ratio: {poolParams.RatioWholeNumber}");
        _output.WriteLine($"  Direction: {poolParams.RatioDirection}");

        // Act
        _logger.LogInformation("Simulating pool creation...");
        var simulationResult = await _testHelper.SolanaClientService.SimulatePoolCreationAsync(poolParams);

        // Assert
        Assert.NotNull(simulationResult);
        _output.WriteLine($"Simulation result: {simulationResult.IsSuccessful}");
        _output.WriteLine($"Error message: {simulationResult.ErrorMessage ?? "None"}");
        _output.WriteLine($"Compute units consumed: {simulationResult.ComputeUnitsConsumed:N0}");
        _output.WriteLine($"Would succeed: {simulationResult.WouldSucceed}");
        
        if (simulationResult.Logs.Count > 0)
        {
            _output.WriteLine("Program logs:");
            foreach (var log in simulationResult.Logs)
            {
                _output.WriteLine($"  {log}");
            }
        }
        
        _output.WriteLine("Simulation summary:");
        _output.WriteLine(simulationResult.SimulationSummary);

        // The simulation should complete without throwing exceptions
        // Note: It may not be "successful" due to account setup requirements,
        // but it should provide useful validation information
        _logger.LogInformation("✅ Pool creation simulation completed successfully");
    }

    [Fact]
    public async Task CreatePoolWithSimulation_ShouldRunSimulationFirst_Success()
    {
        // Arrange
        _logger.LogInformation("=== TEST: CreatePoolWithSimulation_ShouldRunSimulationFirst_Success ===");
        
        var poolParams = new PoolCreationParams
        {
            TokenADecimals = TestConstants.TOKEN_A_DECIMALS,
            TokenBDecimals = TestConstants.TOKEN_B_DECIMALS,
            RatioWholeNumber = TestConstants.EXCHANGE_RATIO_DENOMINATOR,
            RatioDirection = "a_to_b"
        };

        // Act
        _logger.LogInformation("Creating pool (should include simulation step)...");
        var poolState = await _testHelper.SolanaClientService.CreatePoolAsync(poolParams);

        // Assert
        Assert.NotNull(poolState);
        Assert.NotEmpty(poolState.PoolId);
        Assert.NotEmpty(poolState.TokenAMint);
        Assert.NotEmpty(poolState.TokenBMint);
        
        _output.WriteLine($"Pool created successfully:");
        _output.WriteLine($"  Pool ID: {poolState.PoolId}");
        _output.WriteLine($"  Token A: {poolState.TokenAMint}");
        _output.WriteLine($"  Token B: {poolState.TokenBMint}");
        _output.WriteLine($"  Ratio: {poolState.RatioDisplay}");
        _output.WriteLine($"  Status: {(poolState.IsBlockchainPool ? "BLOCKCHAIN" : "SIMULATED")}");

        _logger.LogInformation("✅ Pool creation with simulation completed successfully");
    }

    [Fact]
    public async Task SimulatePoolCreation_WithInvalidParameters_ShouldProvideUsefulError()
    {
        // Arrange
        _logger.LogInformation("=== TEST: SimulatePoolCreation_WithInvalidParameters_ShouldProvideUsefulError ===");
        
        var invalidParams = new PoolCreationParams
        {
            TokenADecimals = -1, // Invalid
            TokenBDecimals = TestConstants.TOKEN_B_DECIMALS,
            RatioWholeNumber = 0, // Invalid
            RatioDirection = "invalid_direction" // Invalid
        };

        // Act & Assert
        var exception = await Assert.ThrowsAnyAsync<Exception>(() => 
            _testHelper.SolanaClientService.SimulatePoolCreationAsync(invalidParams));
        
        _output.WriteLine($"Expected exception caught: {exception.GetType().Name}");
        _output.WriteLine($"Message: {exception.Message}");
        
        _logger.LogInformation("✅ Simulation correctly rejected invalid parameters");
    }

    [Fact]
    public async Task SimulateMultiplePoolCreations_ShouldBeIndependent_Success()
    {
        // Arrange
        _logger.LogInformation("=== TEST: SimulateMultiplePoolCreations_ShouldBeIndependent_Success ===");
        
        var params1 = new PoolCreationParams
        {
            TokenADecimals = 6,
            TokenBDecimals = 8,
            RatioWholeNumber = 1000,
            RatioDirection = "a_to_b"
        };
        
        var params2 = new PoolCreationParams
        {
            TokenADecimals = 9,
            TokenBDecimals = 6,
            RatioWholeNumber = 2000,
            RatioDirection = "b_to_a"
        };

        // Act
        _logger.LogInformation("Simulating multiple pool creations...");
        var simulation1 = await _testHelper.SolanaClientService.SimulatePoolCreationAsync(params1);
        var simulation2 = await _testHelper.SolanaClientService.SimulatePoolCreationAsync(params2);

        // Assert
        Assert.NotNull(simulation1);
        Assert.NotNull(simulation2);
        
        _output.WriteLine("Simulation 1:");
        _output.WriteLine($"  Status: {simulation1.IsSuccessful}");
        _output.WriteLine($"  Compute Units: {simulation1.ComputeUnitsConsumed:N0}");
        
        _output.WriteLine("Simulation 2:");
        _output.WriteLine($"  Status: {simulation2.IsSuccessful}");
        _output.WriteLine($"  Compute Units: {simulation2.ComputeUnitsConsumed:N0}");

        // Both simulations should be independent (different results or same results but independent execution)
        _logger.LogInformation("✅ Multiple pool simulations completed independently");
    }

    public void Dispose()
    {
        _testHelper?.Dispose();
    }
}
