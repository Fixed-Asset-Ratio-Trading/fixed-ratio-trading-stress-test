using FixedRatioStressTest.Core.Interfaces;
using FixedRatioStressTest.Common.Models;
using Solnet.Wallet;

namespace FixedRatioStressTest.Core.Tests.Helpers;

/// <summary>
/// Stub implementation of ITransactionBuilderService for testing
/// This is NOT a mock - it's a minimal real implementation that allows testing
/// of ThreadManager functionality without the full blockchain transaction complexity
/// </summary>
public class StubTransactionBuilderService : ITransactionBuilderService
{
    private static readonly byte[] StubTransaction = new byte[] 
    { 
        0x01, 0x00, 0x01, 0x03, // Version, num required signatures, num readonly signed accounts
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, // Mock transaction data
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00
    };

    public Task<byte[]> BuildCreatePoolTransactionAsync(Wallet payer, PoolConfig poolConfig)
    {
        // Return a mock transaction that represents pool creation
        return Task.FromResult(StubTransaction);
    }

    public Task<TransactionSimulationResult> SimulateCreatePoolTransactionAsync(Wallet payer, PoolConfig poolConfig)
    {
        // Return a mock simulation result that indicates success
        return Task.FromResult(new TransactionSimulationResult 
        { 
            IsSuccessful = true, 
            ComputeUnitsConsumed = 15000,
            SimulationSummary = "✅ Stub simulation - would succeed"
        });
    }

    public Task<byte[]> BuildDepositTransactionAsync(
        Wallet wallet, 
        PoolState poolState, 
        TokenType tokenType, 
        ulong amountInBasisPoints)
    {
        // Return a mock transaction that represents a deposit
        return Task.FromResult(StubTransaction);
    }

    public Task<byte[]> BuildWithdrawalTransactionAsync(
        Wallet wallet, 
        PoolState poolState, 
        TokenType tokenType, 
        ulong lpTokenAmountToBurn)
    {
        // Return a mock transaction that represents a withdrawal
        return Task.FromResult(StubTransaction);
    }

    public Task<byte[]> BuildSwapTransactionAsync(
        Wallet wallet, 
        PoolState poolState, 
        SwapDirection direction, 
        ulong inputAmountBasisPoints, 
        ulong minimumOutputBasisPoints)
    {
        // Return a mock transaction that represents a swap
        return Task.FromResult(StubTransaction);
    }

    public Task<byte[]> BuildTransferTransactionAsync(
        Wallet fromWallet, 
        string toWalletAddress, 
        string tokenMint, 
        ulong amount)
    {
        // Return a mock transaction that represents a transfer
        return Task.FromResult(StubTransaction);
    }

    public Task<byte[]> BuildMintTransactionAsync(
        Wallet mintAuthority, 
        string tokenMint, 
        string recipientAddress, 
        ulong amount)
    {
        // Return a mock transaction that represents token minting
        return Task.FromResult(StubTransaction);
    }

    public Task<string> GetOrCreateAssociatedTokenAccountAsync(Wallet wallet, string mintAddress)
    {
        // Return a mock associated token account address
        return Task.FromResult($"stub_ata_{Guid.NewGuid():N}");
    }

    public PublicKey DeriveSystemStatePda()
    {
        // Return a stub system state PDA
        return new PublicKey("11111111111111111111111111111111");
    }
}
