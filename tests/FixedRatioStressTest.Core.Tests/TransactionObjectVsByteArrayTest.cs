using System;
using Xunit;
using Xunit.Abstractions;
using Microsoft.Extensions.Logging;
using FixedRatioStressTest.Core.Tests.Helpers;
using FixedRatioStressTest.Common.Models;
using System.Threading.Tasks;

namespace FixedRatioStressTest.Core.Tests
{
    public class TransactionObjectVsByteArrayTest : IDisposable
    {
        private readonly ITestOutputHelper _output;
        private readonly ILogger<TransactionObjectVsByteArrayTest> _logger;
        private readonly TestHelper _testHelper;

        public TransactionObjectVsByteArrayTest(ITestOutputHelper output)
        {
            _output = output;
            _testHelper = new TestHelper();
            _logger = _testHelper.LoggerFactory.CreateLogger<TransactionObjectVsByteArrayTest>();
            
            _logger.LogInformation("=== TransactionObjectVsByteArrayTest initialized ===");
        }

        public void Dispose()
        {
            _testHelper?.Dispose();
        }

        [Fact]
        public async Task TestTransactionSubmissionMethods_IdentifyDifference()
        {
            _logger.LogInformation("=== TEST: Comparing Transaction Object vs Byte Array submission ===");
            _output.WriteLine("Testing different transaction submission methods to identify the issue...");

            try
            {
                // Set up core wallet and fund it first
                _logger.LogInformation("Setting up funded core wallet...");
                var coreWallet = await _testHelper.SolanaClientService.GetOrCreateCoreWalletAsync();
                _output.WriteLine($"Core wallet: {coreWallet.PublicKey} ({coreWallet.CurrentSolBalance / 1_000_000_000.0:F3} SOL)");

                // Test token creation (we know this works) vs pool creation (fails)
                _logger.LogInformation("Testing token creation (uses Transaction object)...");
                
                try
                {
                    var tokenMint = await _testHelper.SolanaClientService.CreateTokenMintAsync(9, "TEST");
                    _output.WriteLine($"✅ Token creation succeeded: {tokenMint.MintAddress}");
                    _output.WriteLine("   Uses: TransactionBuilder.Build() -> Transaction object -> _rpcClient.SendTransactionAsync(Transaction)");
                }
                catch (Exception ex)
                {
                    _output.WriteLine($"❌ Token creation failed: {ex.Message}");
                    if (ex.Message.Contains("insufficient SOL"))
                    {
                        _output.WriteLine("   This is expected - need to fund wallet first");
                        _output.WriteLine("   But the transaction format itself is valid");
                    }
                }

                // Now test pool creation (this should fail with deserialization error)
                _logger.LogInformation("Testing pool creation (uses byte array)...");
                var poolParams = new PoolCreationParams
                {
                    TokenADecimals = 9,
                    TokenBDecimals = 6,
                    RatioWholeNumber = 100,
                    RatioDirection = "a_to_b"
                };
                
                try
                {
                    var realPool = await _testHelper.SolanaClientService.CreateRealPoolAsync(poolParams);
                    _output.WriteLine($"✅ Pool creation succeeded: {realPool.PoolId}");
                    _output.WriteLine("   This means the issue has been resolved!");
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("failed to deserialize"))
                {
                    _output.WriteLine($"❌ Pool creation failed with serialization error: {ex.Message}");
                    _output.WriteLine("   Uses: TransactionBuilder.Build() -> byte[] -> SendTransactionAsync(byte[])");
                    _output.WriteLine("🔍 This confirms the issue is in the byte[] transaction submission path");
                    
                    _output.WriteLine("\n💡 SOLUTION IDENTIFIED:");
                    _output.WriteLine("   Change pool creation to use Transaction object like token creation");
                    _output.WriteLine("   Instead of: BuildCreatePoolTransactionAsync() -> byte[]");
                    _output.WriteLine("   Use: Build transaction -> Transaction object -> _rpcClient.SendTransactionAsync(Transaction)");
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("insufficient SOL"))
                {
                    _output.WriteLine($"✅ Pool creation has correct transaction format!");
                    _output.WriteLine("   Failed at SOL balance check, not serialization");
                    _output.WriteLine("   This means both methods work correctly when properly funded");
                }

            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Transaction comparison test failed");
                _output.WriteLine($"💥 Test failed: {ex.Message}");
                throw;
            }
        }
    }
}

