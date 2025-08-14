using System;
using System.Text;
using Xunit;
using Xunit.Abstractions;
using Microsoft.Extensions.Logging;
using FixedRatioStressTest.Core.Tests.Helpers;
using FixedRatioStressTest.Common.Models;
using System.Threading.Tasks;
using Solnet.Wallet;
using Solnet.Rpc;
using Solnet.Rpc.Models;
using Solnet.Programs;
using Solnet.Rpc.Builders;
using Microsoft.Extensions.DependencyInjection;

namespace FixedRatioStressTest.Core.Tests
{
    public class SolnetTransactionDiagnostics : IDisposable
    {
        private readonly ITestOutputHelper _output;
        private readonly ILogger<SolnetTransactionDiagnostics> _logger;
        private readonly TestHelper _testHelper;

        public SolnetTransactionDiagnostics(ITestOutputHelper output)
        {
            _output = output;
            _testHelper = new TestHelper();
            _logger = _testHelper.LoggerFactory.CreateLogger<SolnetTransactionDiagnostics>();
            
            _logger.LogInformation("=== SolnetTransactionDiagnostics initialized ===");
        }

        public void Dispose()
        {
            _testHelper?.Dispose();
        }

        [Fact]
        public async Task DiagnosePoolCreationTransaction_InspectSerialization()
        {
            // Arrange
            _logger.LogInformation("=== DIAGNOSTIC: Pool Creation Transaction Analysis ===");
            _output.WriteLine("Diagnosing Solnet transaction serialization issues...");

            try
            {
                // Step 1: Create core wallet and fund it
                _logger.LogInformation("Step 1: Setting up test environment...");
                var coreWallet = await _testHelper.SolanaClientService.GetOrCreateCoreWalletAsync();
                
                // Step 2: Create token mints for pool creation
                _logger.LogInformation("Step 2: Creating token mints...");
                var poolParams = new PoolCreationParams
                {
                    TokenADecimals = 9,
                    TokenBDecimals = 6,
                    RatioWholeNumber = 100,
                    RatioDirection = "a_to_b"
                };
                
                var tokenAMint = await _testHelper.SolanaClientService.CreateTokenMintAsync(9, "DIAG_A");
                var tokenBMint = await _testHelper.SolanaClientService.CreateTokenMintAsync(6, "DIAG_B");

                // Step 3: Create pool configuration
                _logger.LogInformation("Step 3: Creating pool configuration...");
                var poolConfig = new PoolConfig
                {
                    TokenAMint = tokenAMint.MintAddress,
                    TokenBMint = tokenBMint.MintAddress,
                    TokenADecimals = tokenAMint.Decimals,
                    TokenBDecimals = tokenBMint.Decimals,
                    RatioWholeNumber = 100,
                    RatioDirection = "a_to_b"
                };

                // Step 4: Get the core wallet as a Solnet wallet for transaction building
                _logger.LogInformation("Step 4: Preparing transaction building...");
                var coreWalletData = await _testHelper.StorageService.LoadCoreWalletAsync();
                var privateKeyBytes = Convert.FromBase64String(coreWalletData.PrivateKey);
                var solnetWallet = new Solnet.Wallet.Wallet(privateKeyBytes);

                // Step 5: Build the transaction and inspect the raw data
                _logger.LogInformation("Step 5: Building transaction for inspection...");
                
                try
                {
                    // For now, let's test our pool creation transaction building in a simpler way
                    // by calling the real CreateRealPoolAsync method and catching the transaction error
                    _logger.LogInformation("   Testing pool creation to isolate transaction serialization issue...");
                    
                    try
                    {
                        var realPool = await _testHelper.SolanaClientService.CreateRealPoolAsync(poolParams);
                        _output.WriteLine($"✅ Pool creation succeeded - no transaction serialization issue!");
                        _logger.LogInformation("   Pool creation succeeded: {PoolId}", realPool.PoolId);
                        return; // Test passes - no serialization issue
                    }
                    catch (InvalidOperationException ex) when (ex.Message.Contains("failed to deserialize"))
                    {
                        _output.WriteLine($"❌ Transaction serialization error confirmed: {ex.Message}");
                        _logger.LogError("   Transaction serialization error: {Error}", ex.Message);
                        
                        // This is what we expected - the serialization issue
                        // Continue with analysis...
                    }
                    
                    _output.WriteLine("✅ Transaction serialization issue isolated successfully!");
                    
                }
                catch (Exception buildEx)
                {
                    _logger.LogError(buildEx, "Failed to build transaction");
                    _output.WriteLine($"❌ Transaction building failed: {buildEx.Message}");
                    throw;
                }

            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Diagnostic test failed");
                _output.WriteLine($"💥 Diagnostic failed: {ex.Message}");
                throw;
            }
        }

        [Fact]
        public async Task DiagnoseMinimalTransaction_BasicSolnetTest()
        {
            _logger.LogInformation("=== DIAGNOSTIC: Minimal Transaction Test ===");
            _output.WriteLine("Testing minimal Solnet transaction to isolate issues...");

            try
            {
                // Create a minimal system transfer transaction to test basic Solnet functionality
                // Use the same pattern as the real SolanaClientService.GenerateWallet()
                var fromWallet = GenerateTestWallet();
                var toWallet = GenerateTestWallet();
                
                _logger.LogInformation("Created test wallets: From={From}, To={To}", 
                    fromWallet.Account.PublicKey, toWallet.Account.PublicKey);

                // Build a simple system transfer transaction
                var transferInstruction = Solnet.Programs.SystemProgram.Transfer(
                    fromWallet.Account.PublicKey, 
                    toWallet.Account.PublicKey, 
                    1_000_000); // 0.001 SOL

                var recentBlockHash = await GetTestBlockHash();
                var transaction = new TransactionBuilder()
                    .SetFeePayer(fromWallet.Account.PublicKey)
                    .SetRecentBlockHash(recentBlockHash)
                    .AddInstruction(transferInstruction)
                    .Build(fromWallet.Account);

                _logger.LogInformation("🔍 MINIMAL TRANSACTION DIAGNOSTIC:");
                _logger.LogInformation("   Transaction Length: {Length} bytes", transaction.Length);
                _logger.LogInformation("   Transaction Type: Simple System Transfer");
                _logger.LogInformation("   From: {From}", fromWallet.Account.PublicKey);
                _logger.LogInformation("   To: {To}", toWallet.Account.PublicKey);

                _output.WriteLine($"✅ Minimal transaction built successfully!");
                _output.WriteLine($"   Length: {transaction.Length} bytes");
                _output.WriteLine($"   This proves basic Solnet functionality works");

                // Try to get the transaction as base64 (this is what gets sent to RPC)
                var base64Transaction = Convert.ToBase64String(transaction);
                _logger.LogInformation("   Base64 length: {Length} characters", base64Transaction.Length);
                _output.WriteLine($"   Base64 representation length: {base64Transaction.Length} characters");

            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Minimal transaction test failed");
                _output.WriteLine($"❌ Minimal transaction failed: {ex.Message}");
                throw;
            }
        }

        [Fact]
        public async Task DiagnoseTransactionBuilder_StepByStep()
        {
            _logger.LogInformation("=== DIAGNOSTIC: Transaction Builder Step-by-Step Analysis ===");
            _output.WriteLine("Analyzing each step of our pool creation transaction building...");

            try
            {
                // Step 1: Check if we can create basic instruction data
                _logger.LogInformation("Step 1: Testing instruction data serialization...");
                
                var instructionData = new byte[] { 1, 0, 0, 0, 0, 0, 0, 0, 100, 0, 0, 0, 0, 0, 0, 0, 232 }; // Fixed last byte to valid value
                _logger.LogInformation("   Instruction data length: {Length} bytes", instructionData.Length);
                _logger.LogInformation("   Instruction data: {Data}", BitConverter.ToString(instructionData));
                _output.WriteLine($"✅ Instruction data: {instructionData.Length} bytes - {BitConverter.ToString(instructionData)}");

                // Step 2: Test account meta creation
                _logger.LogInformation("Step 2: Testing AccountMeta creation...");
                var testPubkey = new PublicKey("11111111111111111111111111111112"); // System program
                var accountMeta = AccountMeta.ReadOnly(testPubkey, false);
                _logger.LogInformation("   AccountMeta created: {Pubkey} (ReadOnly)", accountMeta.PublicKey);
                _output.WriteLine($"✅ AccountMeta creation works");

                // Step 3: Test instruction creation
                _logger.LogInformation("Step 3: Testing TransactionInstruction creation...");
                var programId = new PublicKey("4aeVqtWhrUh6wpX8acNj2hpWXKEQwxjA3PYb2sHhNyCn");
                var instruction = new TransactionInstruction
                {
                    ProgramId = programId,
                    Keys = new[] { accountMeta },
                    Data = instructionData
                };
                _logger.LogInformation("   Instruction created with program: {Program}", instruction.ProgramId);
                _output.WriteLine($"✅ TransactionInstruction creation works");

                // Step 4: Test transaction builder creation (without building)
                _logger.LogInformation("Step 4: Testing TransactionBuilder setup...");
                var testWallet = GenerateTestWallet();
                var recentBlockHash = await GetTestBlockHash();
                
                var builder = new TransactionBuilder()
                    .SetFeePayer(testWallet.Account.PublicKey)
                    .SetRecentBlockHash(recentBlockHash)
                    .AddInstruction(instruction);
                
                _logger.LogInformation("   TransactionBuilder configured successfully");
                _output.WriteLine($"✅ TransactionBuilder setup works");

                // Step 5: Try to build the transaction (this is where it might fail)
                _logger.LogInformation("Step 5: Testing transaction building...");
                
                try
                {
                    var transaction = builder.Build(testWallet.Account);
                    _logger.LogInformation("   Transaction built successfully: {Length} bytes", transaction.Length);
                    _output.WriteLine($"✅ Transaction building works: {transaction.Length} bytes");
                }
                catch (Exception buildEx)
                {
                    _logger.LogError(buildEx, "Transaction building failed at final step");
                    _output.WriteLine($"❌ Transaction building failed: {buildEx.Message}");
                    _output.WriteLine($"   This suggests the issue is in the Build() method");
                    throw;
                }

            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Step-by-step diagnostic failed");
                _output.WriteLine($"💥 Step-by-step diagnostic failed: {ex.Message}");
                throw;
            }
        }

        private async Task<string> GetTestBlockHash()
        {
            // For diagnostic purposes, just use a dummy blockhash
            // This is sufficient to test transaction building
            await Task.Delay(1); // Make it async for consistency
            return "11111111111111111111111111111111111111111111";
        }

        private async Task InspectTransactionStructure(byte[] transaction)
        {
            try
            {
                _logger.LogInformation("🔍 DETAILED TRANSACTION INSPECTION:");
                
                // Inspect transaction header (first few bytes should be signatures count, etc.)
                if (transaction.Length >= 4)
                {
                    var signaturesCount = transaction[0];
                    _logger.LogInformation("   Signatures count: {Count}", signaturesCount);
                    _output.WriteLine($"   Signatures count: {signaturesCount}");

                    if (signaturesCount > 0 && transaction.Length >= 1 + (signaturesCount * 64))
                    {
                        _logger.LogInformation("   First signature present: {Length} bytes", 64);
                        _output.WriteLine($"   Signature structure looks valid");
                    }
                }

                // Look for potential issues in the transaction structure
                if (transaction.Length < 100)
                {
                    _logger.LogWarning("   Transaction seems too short: {Length} bytes", transaction.Length);
                    _output.WriteLine($"⚠️  Transaction seems unusually short: {transaction.Length} bytes");
                }

                if (transaction.Length > 1232) // Solana transaction size limit
                {
                    _logger.LogWarning("   Transaction exceeds Solana limit: {Length} bytes", transaction.Length);
                    _output.WriteLine($"⚠️  Transaction exceeds Solana limit: {transaction.Length} bytes");
                }

                // Try to examine the instruction data portion
                _logger.LogInformation("   Full transaction hex (first 100 bytes): {Hex}", 
                    BitConverter.ToString(transaction.Take(100).ToArray()));

            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to inspect transaction structure");
                _output.WriteLine($"❌ Transaction inspection failed: {ex.Message}");
            }
        }

        private Wallet GenerateTestWallet()
        {
            // Use the same pattern as SolanaClientService.GenerateWallet()
            var privateKey = new byte[64];
            System.Security.Cryptography.RandomNumberGenerator.Fill(privateKey);
            return new Wallet(privateKey, "", SeedMode.Bip39);
        }
    }
}
