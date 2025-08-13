using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Solnet.Rpc;
using Solnet.Rpc.Builders;
using Solnet.Rpc.Models;
using Solnet.Rpc.Types;
using Solnet.Wallet;
using Solnet.Programs;
using Solnet.Programs.Utilities;
using FixedRatioStressTest.Core.Interfaces;
using FixedRatioStressTest.Core.Services;
using FixedRatioStressTest.Common.Models;

namespace FixedRatioStressTest.Infrastructure.Services
{
    /// <summary>
    /// Enhanced transaction builder with full account structures per API documentation
    /// </summary>
public class TransactionBuilderService : ITransactionBuilderService
{
        private readonly IComputeUnitManager _computeUnitManager;
        private readonly ILogger<TransactionBuilderService> _logger;
        private readonly SolanaConfig _config;
    private readonly IRpcClient _rpcClient;
        
        public TransactionBuilderService(
            IConfiguration configuration,
            IComputeUnitManager computeUnitManager,
            ILogger<TransactionBuilderService> logger)
        {
            _computeUnitManager = computeUnitManager;
            _logger = logger;
            _config = configuration.GetSection("SolanaConfiguration").Get<SolanaConfig>() ?? new SolanaConfig();
            
            var rpcUrl = _config.GetActiveRpcUrl();
            _rpcClient = ClientFactory.GetClient(rpcUrl);
            
            // Remove circular dependency - we'll pass required data as parameters instead
        }
        
        public async Task<byte[]> BuildCreatePoolTransactionAsync(
            Wallet payer,
            PoolConfig poolConfig)
        {
            try
            {
                _logger.LogInformation("Building pool creation transaction");
                
                var programId = new PublicKey(_config.ProgramId);
                var systemStatePda = DeriveSystemStatePda();
                var poolStatePda = DerivePoolStatePda(poolConfig.TokenAMint, poolConfig.TokenBMint);
                var mainTreasuryPda = DeriveMainTreasuryPda();
                
                // Derive vault and LP mint PDAs
                var tokenAVaultPda = DeriveTokenVaultPda(poolStatePda, poolConfig.TokenAMint);
                var tokenBVaultPda = DeriveTokenVaultPda(poolStatePda, poolConfig.TokenBMint);
                var lpTokenAMintPda = DeriveLpMintPda(poolStatePda, poolConfig.TokenAMint);
                var lpTokenBMintPda = DeriveLpMintPda(poolStatePda, poolConfig.TokenBMint);
                
                // Build account structure per API documentation
                var accounts = new List<AccountMeta>
                {
                    // [0] User (pays fees & rent)
                    AccountMeta.Writable(payer.Account.PublicKey, true),
                    // [1] System Program
                    AccountMeta.ReadOnly(SystemProgram.ProgramIdKey, false),
                    // [2] System State PDA
                    AccountMeta.ReadOnly(systemStatePda, false),
                    // [3] Pool State PDA (to create)
                    AccountMeta.Writable(poolStatePda, false),
                    // [4] SPL Token Program
                    AccountMeta.ReadOnly(TokenProgram.ProgramIdKey, false),
                    // [5] Main Treasury PDA
                    AccountMeta.Writable(mainTreasuryPda, false),
                    // [6] Rent Sysvar
                    AccountMeta.ReadOnly(new PublicKey("SysvarRent111111111111111111111111111111111"), false),
                    // [7] Token A Mint
                    AccountMeta.ReadOnly(new PublicKey(poolConfig.TokenAMint), false),
                    // [8] Token B Mint
                    AccountMeta.ReadOnly(new PublicKey(poolConfig.TokenBMint), false),
                    // [9] Token A Vault PDA (to create)
                    AccountMeta.Writable(tokenAVaultPda, false),
                    // [10] Token B Vault PDA (to create)
                    AccountMeta.Writable(tokenBVaultPda, false),
                    // [11] LP Token A Mint PDA (to create)
                    AccountMeta.Writable(lpTokenAMintPda, false),
                    // [12] LP Token B Mint PDA (to create)
                    AccountMeta.Writable(lpTokenBMintPda, false)
                };
                
                // Create instruction data
                var data = new PoolInitializeInstructionData
                {
                    Discriminator = 4, // process_pool_initialize
                    RatioANumerator = poolConfig.RatioANumerator,
                    RatioBDenominator = poolConfig.RatioBDenominator
                };
                
                var instruction = new TransactionInstruction
                {
                    ProgramId = programId,
                    Keys = accounts,
                    Data = SerializeInstructionData(data)
                };
                
                // Get compute units
                var computeUnits = _computeUnitManager.GetComputeUnits("process_pool_initialize");
                
                // Build transaction
                var blockHash = await GetRecentBlockHashAsync();
                var builder = new TransactionBuilder()
                    .SetFeePayer(payer.Account.PublicKey)
                    .SetRecentBlockHash(blockHash)
                    .AddInstruction(ComputeBudgetProgram.SetComputeUnitLimit(computeUnits))
                    .AddInstruction(instruction);
                
                return builder.Build(payer.Account);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to build pool creation transaction");
                throw;
            }
        }
        
        public async Task<byte[]> BuildDepositTransactionAsync(
        Wallet wallet, 
        PoolState poolState, 
        TokenType tokenType, 
            ulong amountInBasisPoints)
    {
        try
        {
                _logger.LogInformation("Building deposit transaction for {Amount} basis points", amountInBasisPoints);
                
                // Pool state will be passed as parameter to avoid circular dependency
                var programId = new PublicKey(_config.ProgramId);
                
                // Determine token mint based on type
                var tokenMint = tokenType == TokenType.A ? poolState.TokenAMint : poolState.TokenBMint;
                var vault = tokenType == TokenType.A ? poolState.VaultA : poolState.VaultB;
                var lpMint = tokenType == TokenType.A ? poolState.LpMintA : poolState.LpMintB;
                
                // Get or create associated token accounts
                var userTokenAccount = await GetOrCreateAssociatedTokenAccountAsync(wallet, tokenMint);
                var userLpTokenAccount = await GetOrCreateAssociatedTokenAccountAsync(wallet, lpMint);
                
                // Build account structure per API documentation
                var accounts = new List<AccountMeta>
                {
                    // [0] User wallet (signer, writable) - pays fees & provides tokens
                    AccountMeta.Writable(wallet.Account.PublicKey, true),
                    // [1] System Program
                    AccountMeta.ReadOnly(SystemProgram.ProgramIdKey, false),
                    // [2] SPL Token Program
                    AccountMeta.ReadOnly(TokenProgram.ProgramIdKey, false),
                    // [3] System State PDA
                    AccountMeta.ReadOnly(DeriveSystemStatePda(), false),
                    // [4] Pool State PDA
                    AccountMeta.ReadOnly(new PublicKey(poolState.PoolId), false),
                    // [5] Deposit token mint (A or B)
                    AccountMeta.ReadOnly(new PublicKey(tokenMint), false),
                    // [6] Appropriate vault PDA (writable)
                    AccountMeta.Writable(new PublicKey(vault), false),
                    // [7] User's token account
                    AccountMeta.Writable(new PublicKey(userTokenAccount), false),
                    // [8] LP mint PDA (writable)
                    AccountMeta.Writable(new PublicKey(lpMint), false),
                    // [9] User's LP token account (writable)
                    AccountMeta.Writable(new PublicKey(userLpTokenAccount), false),
                    // [10] Main Treasury PDA (writable)
                    AccountMeta.Writable(new PublicKey(poolState.MainTreasury), false),
                    // [11] Pool Treasury PDA (writable)
                    AccountMeta.Writable(new PublicKey(poolState.PoolTreasury), false)
                };
                
                // Create instruction data
                var data = new DepositInstructionData
                {
                    Discriminator = 6, // process_liquidity_deposit
                    Amount = amountInBasisPoints
                };
                
                var instruction = new TransactionInstruction
                {
                    ProgramId = programId,
                    Keys = accounts,
                    Data = SerializeInstructionData(data)
                };
                
                // Get compute units
                var computeUnits = _computeUnitManager.GetComputeUnits("process_liquidity_deposit");
                
                // Build transaction
                var blockHash = await GetRecentBlockHashAsync();
                var builder = new TransactionBuilder()
                    .SetFeePayer(wallet.Account.PublicKey)
                    .SetRecentBlockHash(blockHash)
                    .AddInstruction(ComputeBudgetProgram.SetComputeUnitLimit(computeUnits))
                    .AddInstruction(instruction);
                
                return builder.Build(wallet.Account);
        }
        catch (Exception ex)
        {
                _logger.LogError(ex, "Failed to build deposit transaction");
            throw;
        }
    }

        public async Task<byte[]> BuildWithdrawalTransactionAsync(
        Wallet wallet, 
        PoolState poolState, 
        TokenType tokenType, 
            ulong lpTokenAmountToBurn)
    {
        try
        {
                _logger.LogInformation("Building withdrawal transaction for {Amount} LP tokens", lpTokenAmountToBurn);
                
                // Pool state will be passed as parameter to avoid circular dependency
                var programId = new PublicKey(_config.ProgramId);
                
                // Determine token mint based on type
                var tokenMint = tokenType == TokenType.A ? poolState.TokenAMint : poolState.TokenBMint;
                var vault = tokenType == TokenType.A ? poolState.VaultA : poolState.VaultB;
                var lpMint = tokenType == TokenType.A ? poolState.LpMintA : poolState.LpMintB;
                
                // Get associated token accounts
                var userTokenAccount = await GetOrCreateAssociatedTokenAccountAsync(wallet, tokenMint);
                var userLpTokenAccount = await GetOrCreateAssociatedTokenAccountAsync(wallet, lpMint);
                
                // Build account structure per API documentation
                var accounts = new List<AccountMeta>
                {
                    // [0] User wallet (signer, writable) - pays fees & burns LP tokens
                    AccountMeta.Writable(wallet.Account.PublicKey, true),
                    // [1] System Program
                    AccountMeta.ReadOnly(SystemProgram.ProgramIdKey, false),
                    // [2] SPL Token Program
                    AccountMeta.ReadOnly(TokenProgram.ProgramIdKey, false),
                    // [3] System State PDA
                    AccountMeta.ReadOnly(DeriveSystemStatePda(), false),
                    // [4] Pool State PDA (writable)
                    AccountMeta.Writable(new PublicKey(poolState.PoolId), false),
                    // [5] Withdrawal token mint (A or B)
                    AccountMeta.ReadOnly(new PublicKey(tokenMint), false),
                    // [6] Appropriate vault PDA (writable)
                    AccountMeta.Writable(new PublicKey(vault), false),
                    // [7] User's token account (writable)
                    AccountMeta.Writable(new PublicKey(userTokenAccount), false),
                    // [8] LP mint PDA (writable)
                    AccountMeta.Writable(new PublicKey(lpMint), false),
                    // [9] User's LP token account (writable)
                    AccountMeta.Writable(new PublicKey(userLpTokenAccount), false),
                    // [10] Main Treasury PDA (writable)
                    AccountMeta.Writable(new PublicKey(poolState.MainTreasury), false),
                    // [11] Pool Treasury PDA (writable)
                    AccountMeta.Writable(new PublicKey(poolState.PoolTreasury), false)
                };
                
                // Create instruction data
                var data = new WithdrawalInstructionData
                {
                    Discriminator = 7, // process_liquidity_withdraw
                    Amount = lpTokenAmountToBurn
                };
                
                var instruction = new TransactionInstruction
                {
                    ProgramId = programId,
                    Keys = accounts,
                    Data = SerializeInstructionData(data)
                };
                
                // Get compute units
                var computeUnits = _computeUnitManager.GetComputeUnits("process_liquidity_withdraw");
                
                // Build transaction
                var blockHash = await GetRecentBlockHashAsync();
                var builder = new TransactionBuilder()
                    .SetFeePayer(wallet.Account.PublicKey)
                    .SetRecentBlockHash(blockHash)
                    .AddInstruction(ComputeBudgetProgram.SetComputeUnitLimit(computeUnits))
                    .AddInstruction(instruction);
                
                return builder.Build(wallet.Account);
        }
        catch (Exception ex)
        {
                _logger.LogError(ex, "Failed to build withdrawal transaction");
            throw;
        }
    }

        public async Task<byte[]> BuildSwapTransactionAsync(
        Wallet wallet, 
        PoolState poolState, 
        SwapDirection direction, 
            ulong inputAmountBasisPoints,
            ulong minimumOutputBasisPoints)
    {
        try
        {
                _logger.LogInformation("Building swap transaction: {Direction} {Amount} basis points", 
                    direction, inputAmountBasisPoints);
                
                // Pool state will be passed as parameter to avoid circular dependency
                var programId = new PublicKey(_config.ProgramId);
                
                // Determine input and output mints based on direction
                var (inputMint, outputMint, inputVault, outputVault) = direction == SwapDirection.AToB ?
                    (poolState.TokenAMint, poolState.TokenBMint, poolState.VaultA, poolState.VaultB) :
                    (poolState.TokenBMint, poolState.TokenAMint, poolState.VaultB, poolState.VaultA);
                
                // Get associated token accounts
                var userInputAccount = await GetOrCreateAssociatedTokenAccountAsync(wallet, inputMint);
                var userOutputAccount = await GetOrCreateAssociatedTokenAccountAsync(wallet, outputMint);
                
                // Build account structure per API documentation
                var accounts = new List<AccountMeta>
                {
                    // [0] User wallet (signer, writable) - pays fees
                    AccountMeta.Writable(wallet.Account.PublicKey, true),
                    // [1] System Program
                    AccountMeta.ReadOnly(SystemProgram.ProgramIdKey, false),
                    // [2] SPL Token Program
                    AccountMeta.ReadOnly(TokenProgram.ProgramIdKey, false),
                    // [3] System State PDA
                    AccountMeta.ReadOnly(DeriveSystemStatePda(), false),
                    // [4] Pool State PDA
                    AccountMeta.ReadOnly(new PublicKey(poolState.PoolId), false),
                    // [5] User's input token account (writable)
                    AccountMeta.Writable(new PublicKey(userInputAccount), false),
                    // [6] User's output token account (writable)
                    AccountMeta.Writable(new PublicKey(userOutputAccount), false),
                    // [7] Input vault PDA (writable)
                    AccountMeta.Writable(new PublicKey(inputVault), false),
                    // [8] Output vault PDA (writable)
                    AccountMeta.Writable(new PublicKey(outputVault), false),
                    // [9] Main Treasury PDA (writable)
                    AccountMeta.Writable(new PublicKey(poolState.MainTreasury), false),
                    // [10] Pool Treasury PDA (writable)
                    AccountMeta.Writable(new PublicKey(poolState.PoolTreasury), false)
                };
                
                // Create instruction data
                var data = new SwapInstructionData
                {
                    Discriminator = 8, // process_swap_execute
                    InputAmount = inputAmountBasisPoints,
                    MinimumOutputAmount = minimumOutputBasisPoints
                };
                
                var instruction = new TransactionInstruction
                {
                    ProgramId = programId,
                    Keys = accounts,
                    Data = SerializeInstructionData(data)
                };
                
                // Get compute units
                var computeUnits = _computeUnitManager.GetComputeUnits("process_swap_execute");
                
                // Build transaction
                var blockHash = await GetRecentBlockHashAsync();
                var builder = new TransactionBuilder()
                    .SetFeePayer(wallet.Account.PublicKey)
                    .SetRecentBlockHash(blockHash)
                    .AddInstruction(ComputeBudgetProgram.SetComputeUnitLimit(computeUnits))
                    .AddInstruction(instruction);
                
                return builder.Build(wallet.Account);
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
                _logger.LogInformation("Building transfer transaction for {Amount} tokens to {To}", 
                    amount, toWalletAddress);
                
                var mint = new PublicKey(tokenMint);
                var toPubkey = new PublicKey(toWalletAddress);
                
                // Get or create associated token accounts
                var fromTokenAccount = await GetOrCreateAssociatedTokenAccountAsync(fromWallet, tokenMint);
                var toTokenAccount = AssociatedTokenAccountProgram.DeriveAssociatedTokenAccount(
                    toPubkey, mint);
                
                var blockHash = await GetRecentBlockHashAsync();
                var transactionBuilder = new TransactionBuilder()
                    .SetFeePayer(fromWallet.Account.PublicKey)
                    .SetRecentBlockHash(blockHash);
                
                // Check if recipient token account exists
                var accountInfo = await _rpcClient.GetAccountInfoAsync(toTokenAccount.ToString());
                if (accountInfo.Result?.Value == null)
                {
                    // Create associated token account for recipient
                    transactionBuilder.AddInstruction(AssociatedTokenAccountProgram.CreateAssociatedTokenAccount(
                        fromWallet.Account.PublicKey,
                        toPubkey,
                        mint));
                }
                
                // Add token transfer instruction
                transactionBuilder.AddInstruction(TokenProgram.Transfer(
                    new PublicKey(fromTokenAccount),
                    toTokenAccount,
                    amount,
                    fromWallet.Account.PublicKey));
                
                return transactionBuilder.Build(fromWallet.Account);
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
                _logger.LogInformation("Building mint transaction for {Amount} tokens to {Recipient}", 
                    amount, recipientAddress);
                
                var mint = new PublicKey(tokenMint);
                var recipient = new PublicKey(recipientAddress);
                
                // Get or create associated token account for recipient
                var recipientTokenAccount = AssociatedTokenAccountProgram.DeriveAssociatedTokenAccount(
                    recipient, mint);
                
                var blockHash = await GetRecentBlockHashAsync();
                var transactionBuilder = new TransactionBuilder()
                    .SetFeePayer(mintAuthority.Account.PublicKey)
                    .SetRecentBlockHash(blockHash);
                
                // Check if recipient token account exists
                var accountInfo = await _rpcClient.GetAccountInfoAsync(recipientTokenAccount.ToString());
                if (accountInfo.Result?.Value == null)
                {
                    // Create associated token account for recipient
                    transactionBuilder.AddInstruction(AssociatedTokenAccountProgram.CreateAssociatedTokenAccount(
                        mintAuthority.Account.PublicKey,
                        recipient,
                        mint));
                }
                
                // Add mint to instruction
                transactionBuilder.AddInstruction(TokenProgram.MintTo(
                    mint,
                    recipientTokenAccount,
                    amount,
                    mintAuthority.Account.PublicKey));
                
                return transactionBuilder.Build(mintAuthority.Account);
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
                var owner = wallet.Account.PublicKey;
                
                // Derive associated token account address
                var associatedTokenAccount = AssociatedTokenAccountProgram.DeriveAssociatedTokenAccount(
                    owner, mint);
                
                _logger.LogDebug("Derived ATA {ATA} for wallet {Wallet} and mint {Mint}", 
                    associatedTokenAccount, owner, mint);
                
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
                var blockHash = await _rpcClient.GetLatestBlockHashAsync();
                if (blockHash.WasRequestSuccessfullyHandled && blockHash.Result != null)
                {
                    return blockHash.Result.Value.Blockhash;
                }
                
                // Fallback to placeholder
                _logger.LogWarning("Failed to get recent blockhash, using placeholder");
                return "11111111111111111111111111111111";
        }
        catch (Exception ex)
        {
                _logger.LogWarning(ex, "Failed to get recent blockhash, using placeholder");
                return "11111111111111111111111111111111";
            }
        }
        
        // PDA derivation helpers
        private PublicKey DeriveSystemStatePda()
        {
            var seeds = new List<byte[]> { Encoding.UTF8.GetBytes("system_state") };
            if (PublicKey.TryFindProgramAddress(
                seeds, 
                new PublicKey(_config.ProgramId), 
                out var pda, 
                out _))
            {
                return pda;
            }
            throw new InvalidOperationException("Failed to derive system state PDA");
        }
        
        private PublicKey DerivePoolStatePda(string tokenA, string tokenB)
        {
            var seeds = new List<byte[]>
            {
                Encoding.UTF8.GetBytes("pool_state"),
                new PublicKey(tokenA).KeyBytes,
                new PublicKey(tokenB).KeyBytes
            };
            
            if (PublicKey.TryFindProgramAddress(
                seeds, 
                new PublicKey(_config.ProgramId), 
                out var pda, 
                out _))
            {
                return pda;
            }
            throw new InvalidOperationException("Failed to derive pool state PDA");
        }
        
        private PublicKey DeriveTokenVaultPda(PublicKey poolStatePda, string tokenMint)
        {
            var seeds = new List<byte[]>
            {
                Encoding.UTF8.GetBytes("token_a_vault"), // or "token_b_vault"
                poolStatePda.KeyBytes
            };
            
            if (PublicKey.TryFindProgramAddress(
                seeds, 
                new PublicKey(_config.ProgramId), 
                out var pda, 
                out _))
            {
                return pda;
            }
            throw new InvalidOperationException("Failed to derive token vault PDA");
        }
        
        private PublicKey DeriveLpMintPda(PublicKey poolStatePda, string tokenMint)
        {
            var seeds = new List<byte[]>
            {
                Encoding.UTF8.GetBytes("lp_token_a_mint"), // or "lp_token_b_mint"
                poolStatePda.KeyBytes
            };
            
            if (PublicKey.TryFindProgramAddress(
                seeds, 
                new PublicKey(_config.ProgramId), 
                out var pda, 
                out _))
            {
                return pda;
            }
            throw new InvalidOperationException("Failed to derive LP mint PDA");
        }
        
        private PublicKey DeriveMainTreasuryPda()
        {
            var seeds = new List<byte[]> { Encoding.UTF8.GetBytes("main_treasury") };
            if (PublicKey.TryFindProgramAddress(
                seeds, 
                new PublicKey(_config.ProgramId), 
                out var pda, 
                out _))
            {
                return pda;
            }
            throw new InvalidOperationException("Failed to derive main treasury PDA");
        }
        
        // Helper method to serialize instruction data
        private byte[] SerializeInstructionData<T>(T data) where T : class
        {
            if (data is PoolInitializeInstructionData poolInit)
            {
                var buffer = new List<byte> { poolInit.Discriminator };
                buffer.AddRange(BitConverter.GetBytes(poolInit.RatioANumerator));
                buffer.AddRange(BitConverter.GetBytes(poolInit.RatioBDenominator));
                return buffer.ToArray();
            }
            else if (data is DepositInstructionData deposit)
            {
                var buffer = new List<byte> { deposit.Discriminator };
                buffer.AddRange(BitConverter.GetBytes(deposit.Amount));
                return buffer.ToArray();
            }
            else if (data is WithdrawalInstructionData withdrawal)
            {
                var buffer = new List<byte> { withdrawal.Discriminator };
                buffer.AddRange(BitConverter.GetBytes(withdrawal.Amount));
                return buffer.ToArray();
            }
            else if (data is SwapInstructionData swap)
            {
                var buffer = new List<byte> { swap.Discriminator };
                buffer.AddRange(BitConverter.GetBytes(swap.InputAmount));
                buffer.AddRange(BitConverter.GetBytes(swap.MinimumOutputAmount));
                return buffer.ToArray();
            }
            
            throw new NotSupportedException($"Unknown instruction data type: {typeof(T).Name}");
        }
    }
    
    // Instruction data structures
    public class PoolInitializeInstructionData
    {
        public byte Discriminator { get; set; }
        public ulong RatioANumerator { get; set; }
        public ulong RatioBDenominator { get; set; }
    }
    
    public class DepositInstructionData
    {
        public byte Discriminator { get; set; }
        public ulong Amount { get; set; }
    }
    
    public class WithdrawalInstructionData
    {
        public byte Discriminator { get; set; }
        public ulong Amount { get; set; }
    }
    
    public class SwapInstructionData
    {
        public byte Discriminator { get; set; }
        public ulong InputAmount { get; set; }
        public ulong MinimumOutputAmount { get; set; }
    }
    
    // Enhanced ComputeBudgetProgram
    public static class ComputeBudgetProgram
    {
        private static readonly PublicKey ProgramId = new("ComputeBudget111111111111111111111111111111");
        
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
                ProgramId = ProgramId,
                Keys = new List<AccountMeta>(),
                Data = data
            };
        }
        
        public static TransactionInstruction SetComputeUnitPrice(ulong microLamports)
        {
            // Create compute unit price instruction
            // Instruction format: [0x03, price_low...price_high (8 bytes)]
            var data = new byte[9];
            data[0] = 0x03; // SetComputeUnitPrice instruction
            var priceBytes = BitConverter.GetBytes(microLamports);
            Array.Copy(priceBytes, 0, data, 1, 8);
            
            return new TransactionInstruction
            {
                ProgramId = ProgramId,
                Keys = new List<AccountMeta>(),
                Data = data
            };
        }
    }
}