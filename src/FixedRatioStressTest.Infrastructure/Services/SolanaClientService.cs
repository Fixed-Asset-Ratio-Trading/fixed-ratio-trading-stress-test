using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Solnet.Rpc;
using Solnet.Rpc.Types;
using Solnet.Wallet;
using Solnet.Wallet.Bip39;
using FixedRatioStressTest.Common.Models;
using FixedRatioStressTest.Core.Interfaces;

namespace FixedRatioStressTest.Infrastructure.Services;

public class SolanaClientService : ISolanaClientService
{
    private readonly IRpcClient _rpcClient;
    private readonly ILogger<SolanaClientService> _logger;
    private readonly SolanaConfiguration _config;

    public SolanaClientService(IConfiguration configuration, ILogger<SolanaClientService> logger)
    {
        _logger = logger;
        
        // Get Solana configuration from appsettings
        _config = new SolanaConfiguration();
        configuration.GetSection("Solana").Bind(_config);
        
        // Parse commitment level
        var commitment = _config.Commitment.ToLowerInvariant() switch
        {
            "finalized" => Commitment.Finalized,
            "confirmed" => Commitment.Confirmed,
            "processed" => Commitment.Processed,
            _ => Commitment.Confirmed
        };

        // Create RPC client with proper configuration
        _rpcClient = ClientFactory.GetClient(Cluster.DevNet, logger);
        
        _logger.LogInformation("Solana client initialized for {Network} at {RpcUrl} with {Commitment} commitment", 
            _config.Network, _config.RpcUrl, _config.Commitment);
    }

    public Wallet GenerateWallet()
    {
        var mnemonic = new Mnemonic(WordList.English, WordCount.TwentyFour);
        var wallet = new Wallet(mnemonic);
        
        _logger.LogDebug("Generated new wallet with public key: {PublicKey}", 
            wallet.Account.PublicKey.Key);
        
        return wallet;
    }

    public Wallet RestoreWallet(byte[] privateKey)
    {
        var wallet = new Wallet(privateKey, string.Empty);
        
        _logger.LogDebug("Restored wallet with public key: {PublicKey}", 
            wallet.Account.PublicKey.Key);
        
        return wallet;
    }

    public async Task<ulong> GetSolBalanceAsync(string publicKey)
    {
        try
        {
            var response = await _rpcClient.GetBalanceAsync(publicKey);
            
            if (response.WasSuccessful && response.Result != null)
            {
                var balance = response.Result.Value;
                _logger.LogDebug("SOL balance for {PublicKey}: {Balance} lamports", publicKey, balance);
                return balance;
            }
            
            _logger.LogWarning("Failed to get SOL balance for {PublicKey}: {Error}", 
                publicKey, response.Reason);
            return 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception getting SOL balance for {PublicKey}", publicKey);
            return 0;
        }
    }

    public Task<ulong> GetTokenBalanceAsync(string publicKey, string mintAddress)
    {
        try
        {
            // TODO: Phase 3 - Implement SPL token balance checking
            // This requires finding token accounts by owner and mint
            _logger.LogDebug("Token balance check not yet implemented for {PublicKey} and mint {MintAddress}", 
                publicKey, mintAddress);
            return Task.FromResult(0ul);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception getting token balance for {PublicKey} and mint {MintAddress}", 
                publicKey, mintAddress);
            return Task.FromResult(0ul);
        }
    }

    public async Task<bool> IsHealthyAsync()
    {
        try
        {
            var response = await _rpcClient.GetHealthAsync();
            var isHealthy = response.WasSuccessful && response.Result == "ok";
            
            _logger.LogDebug("Solana health check: {IsHealthy}", isHealthy);
            return isHealthy;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception during Solana health check");
            return false;
        }
    }

    public async Task<ulong> GetSlotAsync()
    {
        try
        {
            var response = await _rpcClient.GetSlotAsync();
            
            if (response.WasSuccessful)
            {
                var slot = response.Result;
                _logger.LogDebug("Current slot: {Slot}", slot);
                return slot;
            }
            
            _logger.LogWarning("Failed to get current slot: {Error}", response.Reason);
            return 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception getting current slot");
            return 0;
        }
    }
}
