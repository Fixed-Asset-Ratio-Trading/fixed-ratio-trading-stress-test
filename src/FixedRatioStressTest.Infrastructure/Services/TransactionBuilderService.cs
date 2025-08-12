using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Solnet.Rpc;
using Solnet.Rpc.Builders;
using Solnet.Rpc.Models;
using Solnet.Wallet;
using Solnet.Programs;
using Solnet.Programs.TokenProgram;
using Solnet.Programs.AssociatedTokenAccountProgram;
using Solnet.Programs.SystemProgram;
using FixedRatioStressTest.Core.Interfaces;
using FixedRatioStressTest.Common.Models;

namespace FixedRatioStressTest.Infrastructure.Services
{
    public class TransactionBuilderService : ITransactionBuilderService
    {
        private readonly ISolanaClientService _solanaClient;
        private readonly ILogger<TransactionBuilderService> _logger;
        private readonly SolanaConfig _config;
        private readonly IRpcClient _rpcClient;
        
        public TransactionBuilderService(
            IConfiguration configuration,
            ISolanaClientService solanaClient,
            ILogger<TransactionBuilderService> logger)
        {
            _solanaClient = solanaClient;
            _logger = logger;
            _config = configuration.GetSection("SolanaConfiguration").Get<SolanaConfig>() ?? new SolanaConfig();
            
            var rpcUrl = _config.GetActiveRpcUrl();
            _rpcClient = ClientFactory.GetClient(rpcUrl);
        }
        
        public async Task<byte[]> BuildCreatePoolTransactionAsync(
            Wallet payer,
            PoolConfig poolConfig)
        {
            try
            {
                var programId = new PublicKey(_config.ProgramId);
                var transaction = new TransactionBuilder()
                    .SetFeePayer(payer.PublicKey)
                    .SetRecentBlockHash(await GetRecentBlockHashAsync());
                
                // For Phase 3, we simulate pool creation
                // In real implementation, this would include:
                // 1. Create token mints
                // 2. Create pool state account
                // 3. Create vault accounts
                // 4. Create LP mint accounts
                // 5. Initialize pool with ratios
                
                // Add a simple transfer as placeholder
                transaction.AddInstruction(SystemProgram.Transfer(
                    payer.PublicKey,
                    payer.PublicKey,
                    SolanaConfiguration.REGISTRATION_FEE));
                
                var tx = transaction.Build(payer.Account);
                return tx.Serialize();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to build create pool transaction");
                throw;
            }
        }
        
        public async Task<byte[]> BuildDepositTransactionAsync(
            Wallet wallet,
            string poolId,
            TokenType tokenType,
            ulong amountInBasisPoints)
        {
            try
            {
                var programId = new PublicKey(_config.ProgramId);
                var pool = await _solanaClient.GetPoolStateAsync(poolId);
                
                // Get token mint based on token type
                var tokenMint = tokenType == TokenType.A ? 
                    new PublicKey(pool.TokenAMint) : 
                    new PublicKey(pool.TokenBMint);
                
                // Get or create associated token accounts
                var userTokenAccount = await GetOrCreateAssociatedTokenAccountAsync(wallet, tokenMint.ToString());
                var userLpTokenAccount = await GetOrCreateAssociatedTokenAccountAsync(
                    wallet, 
                    tokenType == TokenType.A ? pool.LpMintA : pool.LpMintB);
                
                var transaction = new TransactionBuilder()
                    .SetFeePayer(wallet.PublicKey)
                    .SetRecentBlockHash(await GetRecentBlockHashAsync());
                
                // Add compute budget instruction
                transaction.AddInstruction(ComputeBudgetProgram.SetComputeUnitLimit(
                    SolanaConfiguration.DEPOSIT_COMPUTE_UNITS));
                
                // For Phase 3, simulate deposit with a transfer
                // In real implementation, this would call process_liquidity_deposit
                transaction.AddInstruction(SystemProgram.Transfer(
                    wallet.PublicKey,
                    wallet.PublicKey,
                    SolanaConfiguration.DEPOSIT_WITHDRAWAL_FEE));
                
                var tx = transaction.Build(wallet.Account);
                return tx.Serialize();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to build deposit transaction");
                throw;
            }
        }
        
        public async Task<byte[]> BuildWithdrawalTransactionAsync(
            Wallet wallet,
            string poolId,
            TokenType tokenType,
            ulong lpTokenAmountToBurn)
        {
            try
            {
                var programId = new PublicKey(_config.ProgramId);
                var pool = await _solanaClient.GetPoolStateAsync(poolId);
                
                // Get token mint based on token type
                var tokenMint = tokenType == TokenType.A ? 
                    new PublicKey(pool.TokenAMint) : 
                    new PublicKey(pool.TokenBMint);
                
                // Get LP mint based on token type
                var lpMint = tokenType == TokenType.A ?
                    new PublicKey(pool.LpMintA) :
                    new PublicKey(pool.LpMintB);
                
                var transaction = new TransactionBuilder()
                    .SetFeePayer(wallet.PublicKey)
                    .SetRecentBlockHash(await GetRecentBlockHashAsync());
                
                // Add compute budget instruction
                transaction.AddInstruction(ComputeBudgetProgram.SetComputeUnitLimit(
                    SolanaConfiguration.WITHDRAWAL_COMPUTE_UNITS));
                
                // For Phase 3, simulate withdrawal with a transfer
                // In real implementation, this would call process_liquidity_withdraw
                transaction.AddInstruction(SystemProgram.Transfer(
                    wallet.PublicKey,
                    wallet.PublicKey,
                    SolanaConfiguration.DEPOSIT_WITHDRAWAL_FEE));
                
                var tx = transaction.Build(wallet.Account);
                return tx.Serialize();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to build withdrawal transaction");
                throw;
            }
        }
        
        public async Task<byte[]> BuildSwapTransactionAsync(
            Wallet wallet,
            string poolId,
            SwapDirection direction,
            ulong inputAmountBasisPoints,
            ulong minimumOutputBasisPoints)
        {
            try
            {
                var programId = new PublicKey(_config.ProgramId);
                var pool = await _solanaClient.GetPoolStateAsync(poolId);
                
                // Determine input and output mints based on direction
                var (inputMint, outputMint) = direction == SwapDirection.AToB ?
                    (new PublicKey(pool.TokenAMint), new PublicKey(pool.TokenBMint)) :
                    (new PublicKey(pool.TokenBMint), new PublicKey(pool.TokenAMint));
                
                var transaction = new TransactionBuilder()
                    .SetFeePayer(wallet.PublicKey)
                    .SetRecentBlockHash(await GetRecentBlockHashAsync());
                
                // Add compute budget instruction
                transaction.AddInstruction(ComputeBudgetProgram.SetComputeUnitLimit(
                    SolanaConfiguration.SWAP_COMPUTE_UNITS));
                
                // For Phase 3, simulate swap with a transfer
                // In real implementation, this would call process_swap_execute
                transaction.AddInstruction(SystemProgram.Transfer(
                    wallet.PublicKey,
                    wallet.PublicKey,
                    SolanaConfiguration.SWAP_CONTRACT_FEE));
                
                var tx = transaction.Build(wallet.Account);
                return tx.Serialize();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to build swap transaction");
                throw;
            }
        }
        
        public async Task<byte[]> BuildTransferTransactionAsync(
            Wallet fromWallet,
            string toWalletAddress,
            string tokenMint,
            ulong amount)
        {
            try
            {
                var mint = new PublicKey(tokenMint);
                var toPubkey = new PublicKey(toWalletAddress);
                
                // Get or create associated token accounts
                var fromTokenAccount = await GetOrCreateAssociatedTokenAccountAsync(fromWallet, tokenMint);
                var toTokenAccount = AssociatedTokenAccountProgram.DeriveAssociatedTokenAccount(
                    toPubkey, mint);
                
                var transaction = new TransactionBuilder()
                    .SetFeePayer(fromWallet.PublicKey)
                    .SetRecentBlockHash(await GetRecentBlockHashAsync());
                
                // Add token transfer instruction
                transaction.AddInstruction(TokenProgram.Transfer(
                    new PublicKey(fromTokenAccount),
                    toTokenAccount,
                    amount,
                    fromWallet.PublicKey));
                
                var tx = transaction.Build(fromWallet.Account);
                return tx.Serialize();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to build transfer transaction");
                throw;
            }
        }
        
        public async Task<byte[]> BuildMintTransactionAsync(
            Wallet mintAuthority,
            string tokenMint,
            string recipientAddress,
            ulong amount)
        {
            try
            {
                var mint = new PublicKey(tokenMint);
                var recipient = new PublicKey(recipientAddress);
                
                // Get or create associated token account for recipient
                var recipientTokenAccount = AssociatedTokenAccountProgram.DeriveAssociatedTokenAccount(
                    recipient, mint);
                
                var transaction = new TransactionBuilder()
                    .SetFeePayer(mintAuthority.PublicKey)
                    .SetRecentBlockHash(await GetRecentBlockHashAsync());
                
                // Create associated token account if needed
                transaction.AddInstruction(AssociatedTokenAccountProgram.CreateAssociatedTokenAccount(
                    mintAuthority.PublicKey,
                    recipient,
                    mint));
                
                // Add mint to instruction
                transaction.AddInstruction(TokenProgram.MintTo(
                    mint,
                    recipientTokenAccount,
                    amount,
                    mintAuthority.PublicKey));
                
                var tx = transaction.Build(mintAuthority.Account);
                return tx.Serialize();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to build mint transaction");
                throw;
            }
        }
        
        public async Task<string> GetOrCreateAssociatedTokenAccountAsync(
            Wallet wallet, 
            string mintAddress)
        {
            try
            {
                var mint = new PublicKey(mintAddress);
                var owner = wallet.PublicKey;
                
                // Derive associated token account address
                var associatedTokenAccount = AssociatedTokenAccountProgram.DeriveAssociatedTokenAccount(
                    owner, mint);
                
                // For Phase 3, return the derived address
                // In real implementation, we would check if it exists and create if needed
                return associatedTokenAccount.ToString();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get/create associated token account");
                throw;
            }
        }
        
        private async Task<string> GetRecentBlockHashAsync()
        {
            try
            {
                var blockHash = await _rpcClient.GetRecentBlockHashAsync();
                if (blockHash.WasRequestSuccessfullyHandled && blockHash.Result != null)
                {
                    return blockHash.Result.Value.Blockhash;
                }
                
                // Fallback to placeholder
                return "11111111111111111111111111111111";
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get recent blockhash, using placeholder");
                return "11111111111111111111111111111111";
            }
        }
    }
    
    // Placeholder for ComputeBudgetProgram
    public static class ComputeBudgetProgram
    {
        public static TransactionInstruction SetComputeUnitLimit(uint units)
        {
            // Create compute budget instruction
            // Instruction format: [0x02, units_low, units_middle, units_high, units_highest]
            var data = new byte[5];
            data[0] = 0x02; // SetComputeUnitLimit instruction
            data[1] = (byte)(units & 0xFF);
            data[2] = (byte)((units >> 8) & 0xFF);
            data[3] = (byte)((units >> 16) & 0xFF);
            data[4] = (byte)((units >> 24) & 0xFF);
            
            return new TransactionInstruction
            {
                ProgramId = new PublicKey("ComputeBudget111111111111111111111111111111"),
                Keys = new List<AccountMeta>(),
                Data = data
            };
        }
    }
}
