using Solnet.Wallet;
using Solnet.Rpc.Models;

namespace FixedRatioStressTest.Core.Interfaces;

public interface ISolanaClientService
{
    /// <summary>
    /// Generates a new Solana keypair for a thread
    /// </summary>
    Wallet GenerateWallet();
    
    /// <summary>
    /// Restores a wallet from a private key byte array
    /// </summary>
    Wallet RestoreWallet(byte[] privateKey);
    
    /// <summary>
    /// Gets the SOL balance for a public key
    /// </summary>
    Task<ulong> GetSolBalanceAsync(string publicKey);
    
    /// <summary>
    /// Gets the SPL token balance for a public key and mint
    /// </summary>
    Task<ulong> GetTokenBalanceAsync(string publicKey, string mintAddress);
    
    /// <summary>
    /// Checks if the Solana connection is healthy
    /// </summary>
    Task<bool> IsHealthyAsync();
    
    /// <summary>
    /// Gets the current slot
    /// </summary>
    Task<ulong> GetSlotAsync();
}
