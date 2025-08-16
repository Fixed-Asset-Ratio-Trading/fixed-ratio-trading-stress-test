using Solnet.Wallet;
using Solnet.Rpc.Models;
using FixedRatioStressTest.Common.Models;

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
    
    // Pool operations
    Task<PoolState> CreatePoolAsync(PoolCreationParams parameters);
    
    /// <summary>
    /// Simulates pool creation to validate the transaction before execution
    /// </summary>
    Task<TransactionSimulationResult> SimulatePoolCreationAsync(PoolCreationParams parameters);
    Task<PoolState> GetPoolStateAsync(string poolId);
    Task<List<PoolState>> GetAllPoolsAsync();
    
    // Pool lifecycle management  
    Task<List<string>> GetActivePoolsAsync();
    Task<List<string>> GetOrCreateManagedPoolsAsync(int targetPoolCount = 3);
    Task<bool> ValidatePoolExistsAsync(string poolId);
    Task CleanupInvalidPoolsAsync();
    Task ValidateAndCleanupSavedPoolsAsync();
    
    // Core wallet and real blockchain pool management
    Task<CoreWalletConfig> GetOrCreateCoreWalletAsync();
    Task CheckCoreWalletBalanceAsync(CoreWalletConfig coreWallet);
    Task<RealPoolData> CreateRealPoolAsync(PoolCreationParams parameters);
    Task<bool> ValidateRealPoolAsync(RealPoolData pool);
    Task<List<RealPoolData>> GetRealPoolsAsync();
    Task<StressTestTokenMint> CreateTokenMintAsync(int decimals, string? symbol = null);
    
    // Deposit operations
    Task<DepositResult> ExecuteDepositAsync(
        Wallet wallet, 
        string poolId, 
        TokenType tokenType, 
        ulong amountInBasisPoints);
    
    // Withdrawal operations
    Task<WithdrawalResult> ExecuteWithdrawalAsync(
        Wallet wallet, 
        string poolId, 
        TokenType tokenType, 
        ulong lpTokenAmountToBurn);
    
    // Swap operations
    Task<SwapResult> ExecuteSwapAsync(
        Wallet wallet, 
        string poolId, 
        SwapDirection direction, 
        ulong inputAmountBasisPoints, 
        ulong minimumOutputBasisPoints);
    
    // Airdrop and transfers
    Task<string> RequestAirdropAsync(string walletAddress, ulong lamports);
    Task<string> TransferTokensAsync(
        Wallet fromWallet, 
        string toWalletAddress, 
        string tokenMint, 
        ulong amount);
    
    // Token minting (for testing)
    Task<string> MintTokensAsync(string tokenMint, string recipientAddress, ulong amount);
    
    // PDA derivation
    string DerivePoolStatePda(string poolId);
    string DeriveTokenVaultPda(string poolId, string tokenMint);
    string DeriveLpMintPda(string poolId, string tokenMint);
    string DerivePoolTreasuryPda(string poolId);
    
    // System state
    Task<bool> IsSystemPausedAsync();
    Task<bool> IsPoolPausedAsync(string poolId);
    Task<bool> ArePoolSwapsPausedAsync(string poolId);
    
    // Transaction utilities
    Task<string> SendTransactionAsync(byte[] transaction);
    Task<bool> ConfirmTransactionAsync(string signature, int maxRetries = 3);
}
