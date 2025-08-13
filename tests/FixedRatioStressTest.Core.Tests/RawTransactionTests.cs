using Microsoft.Extensions.Logging;
using Solnet.Rpc;
using Solnet.Wallet;
using Solnet.Rpc.Models;
using Xunit;
using Xunit.Abstractions;

namespace FixedRatioStressTest.Core.Tests;

/// <summary>
/// Tests using raw transaction building instead of TransactionBuilder
/// </summary>
public class RawTransactionTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly ILogger<RawTransactionTests> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IRpcClient _rpcClient;

    private const string RPC_URL = "http://192.168.2.88:8899";
    private const string PROGRAM_ID = "4aeVqtWhrUh6wpX8acNj2hpWXKEQwxjA3PYb2sHhNyCn";

    public RawTransactionTests(ITestOutputHelper output)
    {
        _output = output;
        
        _loggerFactory = LoggerFactory.Create(builder =>
            builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
        _logger = _loggerFactory.CreateLogger<RawTransactionTests>();
        
        _rpcClient = ClientFactory.GetClient(RPC_URL);
    }

    [Fact]
    public async Task TestGetVersionWithLegacyTransaction()
    {
        _logger.LogInformation("=== Testing GetVersion with Legacy Transaction Format ===");

        try
        {
            // Create ephemeral wallet
            var ephemeralWallet = GenerateEphemeralWallet();
            _output.WriteLine($"Ephemeral wallet: {ephemeralWallet.Account.PublicKey}");

            // Get recent blockhash
            var blockHashResponse = await _rpcClient.GetLatestBlockHashAsync();
            var recentBlockHash = blockHashResponse.Result.Value.Blockhash;
            _output.WriteLine($"Recent blockhash: {recentBlockHash}");

            // Create legacy transaction using Solnet's Transaction class directly
            var transaction = new Transaction
            {
                RecentBlockHash = recentBlockHash,
                FeePayer = ephemeralWallet.Account.PublicKey,
                Instructions = new List<TransactionInstruction>
                {
                    new TransactionInstruction
                    {
                        ProgramId = new PublicKey(PROGRAM_ID),
                        Keys = new List<AccountMeta>(), // No accounts for GetVersion
                        Data = new byte[] { 14 } // GetVersion discriminator
                    }
                }
            };

            // Sign the transaction
            transaction.Sign(ephemeralWallet.Account);

            _output.WriteLine($"Legacy transaction size: {transaction.Serialize().Length} bytes");
            _output.WriteLine($"Legacy transaction hex: {Convert.ToHexString(transaction.Serialize())}");

            // Simulate the transaction
            var simulationResult = await _rpcClient.SimulateTransactionAsync(
                transaction.Serialize(),
                commitment: Solnet.Rpc.Types.Commitment.Processed,
                sigVerify: false,
                replaceRecentBlockhash: true);

            if (simulationResult.WasRequestSuccessfullyHandled)
            {
                var result = simulationResult.Result?.Value;
                if (result?.Error != null)
                {
                    _output.WriteLine($"Legacy simulation error: {result.Error}");
                }
                else
                {
                    _output.WriteLine("✅ Legacy simulation SUCCESS!");
                    if (result?.Logs != null)
                    {
                        foreach (var log in result.Logs)
                        {
                            _output.WriteLine($"Log: {log}");
                            if (log.Contains("Contract Version:", StringComparison.OrdinalIgnoreCase))
                            {
                                _output.WriteLine($"🎯 FOUND VERSION: {log}");
                            }
                        }
                    }
                }
            }
            else
            {
                _output.WriteLine($"Legacy RPC call failed: {simulationResult.Reason}");
            }

        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Legacy transaction test failed");
            throw;
        }
    }

    [Fact]
    public async Task TestMinimalSystemProgramCall()
    {
        _logger.LogInformation("=== Testing Minimal System Program Call (Control Test) ===");

        try
        {
            // Test with a known working instruction - system program transfer
            var ephemeralWallet = GenerateEphemeralWallet();
            var blockHashResponse = await _rpcClient.GetLatestBlockHashAsync();
            var recentBlockHash = blockHashResponse.Result.Value.Blockhash;

            // Create minimal system transfer (to self, 0 lamports)
            var transferInstruction = new TransactionInstruction
            {
                ProgramId = new PublicKey("11111111111111111111111111111111"), // System Program
                Keys = new List<AccountMeta>
                {
                    AccountMeta.Writable(ephemeralWallet.Account.PublicKey, true), // From
                    AccountMeta.Writable(ephemeralWallet.Account.PublicKey, false), // To
                },
                Data = new byte[] { 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 } // Transfer 0 lamports
            };

            var transaction = new Transaction
            {
                RecentBlockHash = recentBlockHash,
                FeePayer = ephemeralWallet.Account.PublicKey,
                Instructions = new List<TransactionInstruction> { transferInstruction }
            };

            transaction.Sign(ephemeralWallet.Account);

            var simulationResult = await _rpcClient.SimulateTransactionAsync(
                transaction.Serialize(),
                commitment: Solnet.Rpc.Types.Commitment.Processed,
                sigVerify: false,
                replaceRecentBlockhash: true);

            if (simulationResult.WasRequestSuccessfullyHandled)
            {
                var result = simulationResult.Result?.Value;
                if (result?.Error != null)
                {
                    _output.WriteLine($"System program error: {result.Error}");
                }
                else
                {
                    _output.WriteLine("✅ System program transaction works - Solnet serialization is OK");
                }
            }
            else
            {
                _output.WriteLine($"System program RPC failed: {simulationResult.Reason}");
            }

        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "System program test failed");
            throw;
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
