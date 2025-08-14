using Solnet.Wallet;
using Solnet.Rpc.Models;
using FixedRatioStressTest.Common.Models;

namespace FixedRatioStressTest.Core.Interfaces;

public interface ITransactionBuilderService
{
    /// <summary>
    /// Builds a transaction for creating a new pool (returns byte array - legacy method)
    /// </summary>
    Task<byte[]> BuildCreatePoolTransactionAsync(
        Wallet payer,
        PoolConfig poolConfig);
        

    
    /// <summary>
    /// Simulates a pool creation transaction without executing it
    /// Returns simulation result with logs and error information
    /// </summary>
    Task<TransactionSimulationResult> SimulateCreatePoolTransactionAsync(
        Wallet payer,
        PoolConfig poolConfig);

            /// <summary>
        /// Derive the system state PDA for validation
        /// </summary>
        PublicKey DeriveSystemStatePda();
        
        /// <summary>
        /// Build InitializeProgram transaction to initialize treasury system
        /// </summary>
        Task<byte[]> BuildInitializeProgramTransactionAsync(Account systemAuthority);
    
    /// <summary>
    /// Builds a transaction for depositing tokens into a pool
    /// </summary>
    Task<byte[]> BuildDepositTransactionAsync(
        Wallet wallet,
        PoolState poolState,
        TokenType tokenType,
        ulong amountInBasisPoints);
    
    /// <summary>
    /// Builds a transaction for withdrawing tokens from a pool
    /// </summary>
    Task<byte[]> BuildWithdrawalTransactionAsync(
        Wallet wallet,
        PoolState poolState,
        TokenType tokenType,
        ulong lpTokenAmountToBurn);
    
    /// <summary>
    /// Builds a transaction for swapping tokens
    /// </summary>
    Task<byte[]> BuildSwapTransactionAsync(
        Wallet wallet,
        PoolState poolState,
        SwapDirection direction,
        ulong inputAmountBasisPoints,
        ulong minimumOutputBasisPoints);
    
    /// <summary>
    /// Builds a transaction for transferring tokens
    /// </summary>
    Task<byte[]> BuildTransferTransactionAsync(
        Wallet fromWallet,
        string toWalletAddress,
        string tokenMint,
        ulong amount);
    
    /// <summary>
    /// Builds a transaction for minting tokens (test only)
    /// </summary>
    Task<byte[]> BuildMintTransactionAsync(
        Wallet mintAuthority,
        string tokenMint,
        string recipientAddress,
        ulong amount);
    
    /// <summary>
    /// Gets or creates associated token account for a wallet
    /// </summary>
    Task<string> GetOrCreateAssociatedTokenAccountAsync(
        Wallet wallet, 
        string mintAddress);
}
