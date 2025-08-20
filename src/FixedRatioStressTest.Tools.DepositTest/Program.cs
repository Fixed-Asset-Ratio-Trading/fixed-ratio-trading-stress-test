using FixedRatioStressTest.Common.Models;
using FixedRatioStressTest.Core.Interfaces;
using FixedRatioStressTest.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using FixedRatioStressTest.Core.Services;

// Usage:
// dotnet run --project src/FixedRatioStressTest.Tools.DepositTest -- <pool_id> <thread_id> <token_type:A|B> <amount>

if (args.Length < 4)
{
    Console.Error.WriteLine("Usage: DepositTest <pool_id> <thread_id> <A|B> <amount_basis_points>");
    return 1;
}

string poolId = args[0];
string threadId = args[1];
string tokenTypeStr = args[2];
if (!ulong.TryParse(args[3], out var amount))
{
    Console.Error.WriteLine("amount_basis_points must be an unsigned integer");
    return 1;
}

var tokenType = string.Equals(tokenTypeStr, "B", StringComparison.OrdinalIgnoreCase) ? TokenType.B : TokenType.A;

// Simple DI setup
var services = new ServiceCollection();

// Minimal logging
services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Debug));

// Configuration (optional appsettings.json + environment variables)
var configuration = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json", optional: true)
    .AddEnvironmentVariables()
    .Build();
services.AddSingleton<IConfiguration>(configuration);

// Core services
services.AddSingleton<IComputeUnitManager, ComputeUnitManager>();
services.AddSingleton<IStorageService, JsonFileStorageService>();
services.AddSingleton<ITransactionBuilderService, TransactionBuilderService>();
services.AddSingleton<ISolanaClientService, SolanaClientService>();

var provider = services.BuildServiceProvider();
var solana = provider.GetRequiredService<ISolanaClientService>();
var storage = provider.GetRequiredService<IStorageService>();
var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("DepositTest");

try
{
    // Ensure treasury/system initialized and pool exists
    await solana.InitializeTreasurySystemAsync();

    // Load thread config to get its wallet
    var config = await storage.LoadThreadConfigAsync(threadId);
    if (config.PrivateKey is null)
    {
        Console.Error.WriteLine("Thread has no private key saved. Cannot restore wallet.");
        return 1;
    }

    var wallet = solana.RestoreWallet(config.PrivateKey);
    Console.WriteLine($"Thread wallet: {wallet.Account.PublicKey}");

    // Ensure ATAs exist per API requirement
    // Validate pool exists on-chain (per new doc guidance)
    var exists = await solana.ValidatePoolExistsOnBlockchainAsync(poolId);
    if (!exists)
    {
        Console.Error.WriteLine($"Pool {poolId} not found on-chain. Aborting.");
        return 1;
    }
    var pool = await solana.GetPoolStateAsync(poolId);
    var depositMint = tokenType == TokenType.A ? pool.TokenAMint : pool.TokenBMint;
    var lpMint = tokenType == TokenType.A ? pool.LpMintA : pool.LpMintB;
    await solana.EnsureAtaExistsAsync(wallet, depositMint);
    await solana.EnsureAtaExistsAsync(wallet, lpMint);
    
    // Mint some test tokens to the wallet for deposit testing
    try
    {
        Console.WriteLine($"Attempting to mint 1000 test tokens of {depositMint} to wallet...");
        await solana.MintTokensAsync(depositMint, wallet.Account.PublicKey.Key, 1000);
        Console.WriteLine("✅ Test tokens minted successfully");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"⚠️ Could not mint test tokens: {ex.Message}");
        Console.WriteLine("Proceeding with deposit test anyway (may fail due to insufficient balance)");
    }

    // Ensure pool is resolvable
    Console.WriteLine($"Pool resolved: {pool.PoolId}");

    // Execute deposit
    Console.WriteLine($"Submitting deposit: {amount} basis points, token {tokenType}...");
    var result = await solana.ExecuteDepositAsync(wallet, poolId, tokenType, amount);
    Console.WriteLine($"OK. Signature: {result.TransactionSignature}");
    Console.WriteLine($"LP received: {result.LpTokensReceived}");
    return 0;
}
catch (Exception ex)
{
    logger.LogError(ex, "Deposit test failed");
    Console.Error.WriteLine($"ERROR: {ex.Message}");
    return 1;
}
