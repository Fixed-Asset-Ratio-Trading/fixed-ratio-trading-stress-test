using Solnet.Wallet;
using FixedRatioStressTest.Common.Models;

namespace FixedRatioStressTest.Core.Interfaces;

public interface ITransactionBuilderService
{
    /// <summary>
    /// Builds a transaction for creating a new pool
    /// </summary>
    Task<byte[]> BuildCreatePoolTransactionAsync(
        Wallet payer,
        PoolConfig poolConfig);
    
    /// <summary>
    /// Builds a transaction for depositing tokens into a pool
    /// </summary>
    Task<byte[]> BuildDepositTransactionAsync(
        Wallet wallet,
        string poolId,
        TokenType tokenType,
        ulong amountInBasisPoints);
    
    /// <summary>
    /// Builds a transaction for withdrawing tokens from a pool
    /// </summary>
    Task<byte[]> BuildWithdrawalTransactionAsync(
        Wallet wallet,
        string poolId,
        TokenType tokenType,
        ulong lpTokenAmountToBurn);
    
    /// <summary>
    /// Builds a transaction for swapping tokens
    /// </summary>
    Task<byte[]> BuildSwapTransactionAsync(
        Wallet wallet,
        string poolId,
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
