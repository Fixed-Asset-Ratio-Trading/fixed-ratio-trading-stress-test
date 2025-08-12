using Solnet.Wallet;
using Solnet.Rpc.Models;
using FixedRatioStressTest.Common.Models;
using SolanaTokenAccount = Solnet.Rpc.Models.TokenAccount;
using AppTokenAccount = FixedRatioStressTest.Common.Models.TokenAccount;

namespace FixedRatioStressTest.Core.Interfaces;

public interface ITransactionBuilderService
{
    /// <summary>
    /// Creates and submits a deposit transaction to add liquidity to a pool
    /// </summary>
    Task<string> SubmitDepositTransactionAsync(
        Wallet wallet, 
        string poolId, 
        TokenType tokenType, 
        ulong amount);

    /// <summary>
    /// Creates and submits a withdrawal transaction to remove liquidity from a pool
    /// </summary>
    Task<string> SubmitWithdrawalTransactionAsync(
        Wallet wallet, 
        string poolId, 
        TokenType tokenType, 
        ulong lpTokenAmount);

    /// <summary>
    /// Creates and submits a swap transaction
    /// </summary>
    Task<string> SubmitSwapTransactionAsync(
        Wallet wallet, 
        string poolId, 
        SwapDirection direction, 
        ulong inputAmount, 
        ulong minimumOutputAmount);

    /// <summary>
    /// Gets or creates token accounts for a wallet
    /// </summary>
    Task<AppTokenAccount> GetOrCreateTokenAccountAsync(Wallet wallet, string mintAddress);

    /// <summary>
    /// Gets pool information
    /// </summary>
    Task<PoolInfo?> GetPoolInfoAsync(string poolId);

    /// <summary>
    /// Transfers tokens between wallets (for token sharing)
    /// </summary>
    Task<string> TransferTokensAsync(
        Wallet fromWallet, 
        string toPublicKey, 
        string mintAddress, 
        ulong amount);

    /// <summary>
    /// Requests SOL airdrop for testing
    /// </summary>
    Task<bool> RequestAirdropAsync(string publicKey, ulong lamports);
}
