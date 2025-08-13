using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Solnet.Rpc;
using Solnet.Wallet;
using Solnet.Rpc.Builders;
using Solnet.Rpc.Models;
using Xunit;
using Xunit.Abstractions;
using System.Text;

namespace FixedRatioStressTest.Core.Tests;

/// <summary>
/// Diagnostic tests to identify why GetVersion instruction is failing
/// </summary>
public class GetVersionDiagnosticTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly ILogger<GetVersionDiagnosticTests> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IRpcClient _rpcClient;

    private const string RPC_URL = "http://192.168.2.88:8899";
    private const string PROGRAM_ID = "4aeVqtWhrUh6wpX8acNj2hpWXKEQwxjA3PYb2sHhNyCn";
    private const byte GET_VERSION_DISCRIMINATOR = 14;

    public GetVersionDiagnosticTests(ITestOutputHelper output)
    {
        _output = output;
        
        _loggerFactory = LoggerFactory.Create(builder =>
            builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
        _logger = _loggerFactory.CreateLogger<GetVersionDiagnosticTests>();
        
        _rpcClient = ClientFactory.GetClient(RPC_URL);
    }

    [Fact]
    public async Task DiagnoseGetVersionTransaction_AnalyzeFailure()
    {
        _logger.LogInformation("=== DIAGNOSTIC: Analyzing GetVersion Transaction Failure ===");

        try
        {
            // Step 1: Verify RPC connection
            var healthResult = await _rpcClient.GetHealthAsync();
            _output.WriteLine($"RPC Health: {healthResult.WasRequestSuccessfullyHandled}");
            Assert.True(healthResult.WasRequestSuccessfullyHandled, "RPC should be healthy");

            // Step 2: Verify program exists
            var programAccount = await _rpcClient.GetAccountInfoAsync(PROGRAM_ID);
            _output.WriteLine($"Program exists: {programAccount.WasRequestSuccessfullyHandled}");
            if (programAccount.WasRequestSuccessfullyHandled && programAccount.Result?.Value != null)
            {
                _output.WriteLine($"Program owner: {programAccount.Result.Value.Owner}");
                _output.WriteLine($"Program executable: {programAccount.Result.Value.Executable}");
            }

            // Step 3: Create ephemeral wallet
            var ephemeralWallet = GenerateEphemeralWallet();
            _output.WriteLine($"Ephemeral wallet: {ephemeralWallet.Account.PublicKey}");

            // Step 4: Get recent blockhash
            var blockHashResponse = await _rpcClient.GetLatestBlockHashAsync();
            Assert.True(blockHashResponse.WasRequestSuccessfullyHandled, "Should get blockhash");
            var recentBlockHash = blockHashResponse.Result.Value.Blockhash;
            _output.WriteLine($"Recent blockhash: {recentBlockHash}");

            // Step 5: Build minimal GetVersion transaction
            var instructionData = new byte[] { GET_VERSION_DISCRIMINATOR };
            _output.WriteLine($"Instruction data: [{string.Join(", ", instructionData)}] (length: {instructionData.Length})");

            var instruction = new TransactionInstruction
            {
                ProgramId = new PublicKey(PROGRAM_ID),
                Keys = new List<AccountMeta>(), // GetVersion requires NO accounts
                Data = instructionData
            };

            _output.WriteLine($"Instruction program ID: {instruction.ProgramId}");
            _output.WriteLine($"Instruction accounts: {instruction.Keys.Count}");

            // Step 6: Build transaction using Solnet
            var transaction = new TransactionBuilder()
                .SetFeePayer(ephemeralWallet.Account.PublicKey)
                .SetRecentBlockHash(recentBlockHash)
                .AddInstruction(instruction)
                .Build(ephemeralWallet.Account);

            _output.WriteLine($"Transaction size: {transaction.Length} bytes");
            _output.WriteLine($"Transaction hex: {Convert.ToHexString(transaction)}");

            // Step 7: Analyze transaction structure
            AnalyzeTransactionBytes(transaction);

            // Step 8: Simulate with different options
            await TestSimulationOptions(transaction);

        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Diagnostic test failed");
            throw;
        }
    }

    [Fact]
    public async Task TestAlternativeTransactionFormats()
    {
        _logger.LogInformation("=== DIAGNOSTIC: Testing Alternative Transaction Formats ===");

        try
        {
            var ephemeralWallet = GenerateEphemeralWallet();
            var blockHashResponse = await _rpcClient.GetLatestBlockHashAsync();
            var recentBlockHash = blockHashResponse.Result.Value.Blockhash;

            // Test 1: Minimal transaction (just discriminator)
            await TestTransactionFormat("Minimal", new byte[] { GET_VERSION_DISCRIMINATOR }, ephemeralWallet, recentBlockHash);

            // Test 2: With explicit zero-length data
            await TestTransactionFormat("Zero-length data", new byte[0], ephemeralWallet, recentBlockHash);

            // Test 3: Different discriminator values to test if it's a discriminator issue
            await TestTransactionFormat("Wrong discriminator", new byte[] { 0 }, ephemeralWallet, recentBlockHash);

        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Alternative format test failed");
            throw;
        }
    }

    private async Task TestTransactionFormat(string testName, byte[] instructionData, Wallet wallet, string blockhash)
    {
        _output.WriteLine($"\n--- Testing {testName} ---");
        _output.WriteLine($"Instruction data: [{string.Join(", ", instructionData)}]");

        try
        {
            var instruction = new TransactionInstruction
            {
                ProgramId = new PublicKey(PROGRAM_ID),
                Keys = new List<AccountMeta>(),
                Data = instructionData
            };

            var transaction = new TransactionBuilder()
                .SetFeePayer(wallet.Account.PublicKey)
                .SetRecentBlockHash(blockhash)
                .AddInstruction(instruction)
                .Build(wallet.Account);

            var simulationResult = await _rpcClient.SimulateTransactionAsync(
                transaction, 
                commitment: Solnet.Rpc.Types.Commitment.Processed,
                sigVerify: false,
                replaceRecentBlockhash: true);

            if (simulationResult.WasRequestSuccessfullyHandled)
            {
                var result = simulationResult.Result?.Value;
                if (result?.Error != null)
                {
                    _output.WriteLine($"Simulation error: {result.Error}");
                }
                else
                {
                    _output.WriteLine("✅ Simulation SUCCESS!");
                    if (result?.Logs != null)
                    {
                        foreach (var log in result.Logs)
                        {
                            _output.WriteLine($"Log: {log}");
                        }
                    }
                }
            }
            else
            {
                _output.WriteLine($"RPC call failed: {simulationResult.Reason}");
            }
        }
        catch (Exception ex)
        {
            _output.WriteLine($"Exception: {ex.Message}");
        }
    }

    private void AnalyzeTransactionBytes(byte[] transaction)
    {
        _output.WriteLine("\n--- Transaction Byte Analysis ---");
        
        try
        {
            if (transaction.Length < 10)
            {
                _output.WriteLine("❌ Transaction too short");
                return;
            }

            // Basic transaction structure analysis
            _output.WriteLine($"First 10 bytes: {Convert.ToHexString(transaction.Take(10).ToArray())}");
            _output.WriteLine($"Last 10 bytes: {Convert.ToHexString(transaction.TakeLast(10).ToArray())}");

            // Try to identify transaction components
            var index = 0;
            if (index < transaction.Length)
            {
                var numSignatures = transaction[index];
                _output.WriteLine($"Number of signatures: {numSignatures}");
                index++;
            }

            // Skip signature validation for now - focus on instruction data
            _output.WriteLine("Transaction appears to have basic structure");
        }
        catch (Exception ex)
        {
            _output.WriteLine($"Analysis failed: {ex.Message}");
        }
    }

    private async Task TestSimulationOptions(byte[] transaction)
    {
        _output.WriteLine("\n--- Testing Different Simulation Options ---");

        var testCases = new[]
        {
            ("Default", Solnet.Rpc.Types.Commitment.Processed, false, false),
            ("No SigVerify", Solnet.Rpc.Types.Commitment.Processed, false, true),
            ("Replace Blockhash", Solnet.Rpc.Types.Commitment.Processed, true, false),
            ("Both Options", Solnet.Rpc.Types.Commitment.Processed, true, true),
            ("Confirmed", Solnet.Rpc.Types.Commitment.Confirmed, false, true),
        };

        foreach (var (name, commitment, replaceBlockhash, noSigVerify) in testCases)
        {
            try
            {
                _output.WriteLine($"\nTesting {name}:");
                
                var result = await _rpcClient.SimulateTransactionAsync(
                    transaction, 
                    commitment: commitment,
                    sigVerify: !noSigVerify,
                    replaceRecentBlockhash: replaceBlockhash);

                if (result.WasRequestSuccessfullyHandled)
                {
                    var simResult = result.Result?.Value;
                    if (simResult?.Error != null)
                    {
                        _output.WriteLine($"  Error: {simResult.Error}");
                    }
                    else
                    {
                        _output.WriteLine($"  ✅ SUCCESS!");
                        if (simResult?.Logs?.Length > 0)
                        {
                            _output.WriteLine($"  Logs: {simResult.Logs.Length} entries");
                        }
                    }
                }
                else
                {
                    _output.WriteLine($"  RPC failed: {result.Reason}");
                }
            }
            catch (Exception ex)
            {
                _output.WriteLine($"  Exception: {ex.Message}");
            }
        }
    }

    private Wallet GenerateEphemeralWallet()
    {
        var privateKey = new byte[64];
        System.Security.Cryptography.RandomNumberGenerator.Fill(privateKey);
        return new Wallet(privateKey, "", SeedMode.Bip39);
    }

    public void Dispose()
    {
        _loggerFactory?.Dispose();
    }
}
