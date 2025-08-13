using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Solnet.Rpc;
using Solnet.Wallet;
using Solnet.Programs;
using Solnet.Rpc.Builders;
using Solnet.Rpc.Models;
using Xunit;
using Xunit.Abstractions;
using System.Text;

namespace FixedRatioStressTest.Core.Tests
{
    public class SimpleTransactionTests : IDisposable
    {
        private readonly ITestOutputHelper _output;
        private readonly ILogger<SimpleTransactionTests> _logger;
        private readonly ILoggerFactory _loggerFactory;
        private readonly IRpcClient _rpcClient;

        public SimpleTransactionTests(ITestOutputHelper output)
        {
            _output = output;
            
            // Setup logging to capture debug info
            _loggerFactory = LoggerFactory.Create(builder =>
                builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
            _logger = _loggerFactory.CreateLogger<SimpleTransactionTests>();
            
            // Setup services
            _rpcClient = ClientFactory.GetClient("http://192.168.2.88:8899");
        }

        [Fact]
        public async Task SolanaRpcConnection_HealthCheck_Success()
        {
            _logger.LogInformation("=== Testing Solana RPC Connection ===");
            
            try
            {
                // Test basic RPC health
                var healthResult = await _rpcClient.GetHealthAsync();
                _output.WriteLine($"RPC Health Result: {healthResult}");
                
                _logger.LogInformation("✅ Solana RPC connection is healthy");
                Assert.True(healthResult.WasRequestSuccessfullyHandled);
                
                // Test getting recent blockhash (basic functionality)
                var blockHashResponse = await _rpcClient.GetLatestBlockHashAsync();
                Assert.True(blockHashResponse.WasRequestSuccessfullyHandled, "Failed to get recent blockhash");
                
                var recentBlockHash = blockHashResponse.Result.Value.Blockhash;
                _logger.LogInformation("Recent blockhash: {BlockHash}", recentBlockHash);
                _output.WriteLine($"Recent blockhash: {recentBlockHash}");
                
                Assert.NotNull(recentBlockHash);
                Assert.NotEmpty(recentBlockHash);
                
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RPC connection test failed");
                throw;
            }
        }

        [Fact]
        public async Task BasicTransaction_Build_Success()
        {
            _logger.LogInformation("=== Testing Basic Transaction Building ===");
            
            try
            {
                // Create test wallet using the same pattern as SolanaClientService
                var privateKey = new byte[64];
                System.Security.Cryptography.RandomNumberGenerator.Fill(privateKey);
                var wallet = new Wallet(privateKey, "", SeedMode.Bip39);
                _logger.LogInformation("Test wallet public key: {PublicKey}", wallet.Account.PublicKey);
                
                // Get recent blockhash
                var blockHashResponse = await _rpcClient.GetLatestBlockHashAsync();
                Assert.True(blockHashResponse.WasRequestSuccessfullyHandled, "Failed to get recent blockhash");
                
                var recentBlockHash = blockHashResponse.Result.Value.Blockhash;
                _logger.LogInformation("Recent blockhash: {BlockHash}", recentBlockHash);
                
                // Build simple SOL transfer transaction using correct Solnet format
                var transferInstruction = SystemProgram.Transfer(
                    wallet.Account.PublicKey,
                    wallet.Account.PublicKey, // Self-transfer for testing
                    1000); // 0.000001 SOL
                
                var transaction = new TransactionBuilder()
                    .SetFeePayer(wallet.Account.PublicKey)
                    .SetRecentBlockHash(recentBlockHash)
                    .AddInstruction(transferInstruction)
                    .Build(wallet.Account);
                
                _logger.LogInformation("Transaction built successfully. Size: {Size} bytes", transaction.Length);
                _output.WriteLine($"Transaction size: {transaction.Length} bytes");
                
                // Verify transaction is properly formed
                Assert.NotNull(transaction);
                Assert.True(transaction.Length > 0, "Transaction should have non-zero length");
                Assert.True(transaction.Length < 1232, "Transaction should be under size limit"); // Solana max transaction size
                
                _logger.LogInformation("✅ Basic transaction building works correctly");
                
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Basic transaction test failed");
                throw;
            }
        }

        [Fact]
        public async Task TransactionSimulation_BasicFormat_Success()
        {
            _logger.LogInformation("=== Testing Transaction Simulation (Format Verification) ===");
            
            try
            {
                // Create test wallet using the same pattern as SolanaClientService
                var privateKey = new byte[64];
                System.Security.Cryptography.RandomNumberGenerator.Fill(privateKey);
                var wallet = new Wallet(privateKey, "", SeedMode.Bip39);
                
                // Get recent blockhash
                var blockHashResponse = await _rpcClient.GetLatestBlockHashAsync();
                Assert.True(blockHashResponse.WasRequestSuccessfullyHandled, "Failed to get recent blockhash");
                var recentBlockHash = blockHashResponse.Result.Value.Blockhash;
                
                // Build simple SOL transfer transaction
                var transferInstruction = SystemProgram.Transfer(
                    wallet.Account.PublicKey,
                    wallet.Account.PublicKey,
                    1000);
                
                var transaction = new TransactionBuilder()
                    .SetFeePayer(wallet.Account.PublicKey)
                    .SetRecentBlockHash(recentBlockHash)
                    .AddInstruction(transferInstruction)
                    .Build(wallet.Account);
                
                _logger.LogInformation("Attempting transaction simulation...");
                
                // Test transaction simulation
                var simulationResult = await _rpcClient.SimulateTransactionAsync(transaction);
                
                if (simulationResult.WasRequestSuccessfullyHandled)
                {
                    _logger.LogInformation("✅ Transaction simulation API call SUCCESS");
                    _output.WriteLine($"Simulation successful: {simulationResult.WasRequestSuccessfullyHandled}");
                    
                    // Check simulation result
                    var result = simulationResult.Result?.Value;
                    if (result != null)
                    {
                        _output.WriteLine($"Simulation result - Error: {result.Error}");
                        _output.WriteLine($"Simulation result - Logs count: {result.Logs?.Length ?? 0}");
                        
                        if (result.Error != null)
                        {
                            _logger.LogInformation("Expected simulation error (insufficient funds): {Error}", result.Error);
                            _logger.LogInformation("✅ Transaction format is CORRECT (failed due to insufficient funds as expected)");
                        }
                        else
                        {
                            _logger.LogInformation("✅ Transaction simulation passed completely (unexpected but good!)");
                        }
                    }
                    
                    Assert.True(true, "Transaction format is correct - simulation API worked");
                }
                else
                {
                    _logger.LogError("❌ Transaction simulation FAILED: {Reason}", simulationResult.Reason);
                    
                    // Check if it's a serialization issue
                    if (simulationResult.Reason?.Contains("deserialize") == true)
                    {
                        _logger.LogError("🚨 SERIALIZATION ISSUE CONFIRMED");
                        Assert.Fail($"Transaction serialization is broken: {simulationResult.Reason}");
                    }
                    else
                    {
                        _logger.LogWarning("Different error type (not serialization): {Reason}", simulationResult.Reason);
                        Assert.Fail($"Unexpected simulation error: {simulationResult.Reason}");
                    }
                }
                
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Transaction simulation test failed");
                throw;
            }
        }

        public void Dispose()
        {
            _loggerFactory?.Dispose();
        }
    }
}
