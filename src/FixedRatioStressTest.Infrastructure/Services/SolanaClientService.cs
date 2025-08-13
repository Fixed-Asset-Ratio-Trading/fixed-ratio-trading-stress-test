using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Solnet.Rpc;
using Solnet.Rpc.Builders;
using Solnet.Rpc.Core.Http;
using Solnet.Rpc.Messages;
using Solnet.Rpc.Models;
using Solnet.Wallet;
using Solnet.Programs;
using Solnet.Programs.Utilities;
using FixedRatioStressTest.Core.Interfaces;
using FixedRatioStressTest.Common.Models;

namespace FixedRatioStressTest.Infrastructure.Services
{
    public class SolanaClientService : ISolanaClientService
    {
        private readonly IRpcClient _rpcClient;
        private readonly ITransactionBuilderService _transactionBuilder;
        private readonly ILogger<SolanaClientService> _logger;
        private readonly SolanaConfig _config;
        private readonly Dictionary<string, PoolState> _poolCache = new();
        private readonly Dictionary<string, Wallet> _mintAuthorities = new();

        public SolanaClientService(
            IConfiguration configuration,
            ITransactionBuilderService transactionBuilder,
            ILogger<SolanaClientService> logger)
        {
            _config = configuration.GetSection("SolanaConfiguration").Get<SolanaConfig>() ?? new SolanaConfig();
            _transactionBuilder = transactionBuilder;
            _logger = logger;
            
            var rpcUrl = _config.GetActiveRpcUrl();
            _rpcClient = ClientFactory.GetClient(rpcUrl);
            _logger.LogInformation("Initialized Solana client with RPC URL: {RpcUrl}", rpcUrl);
        }

        public Wallet GenerateWallet()
        {
            // Generate a new wallet with a random private key
            var privateKey = new byte[64];
            System.Security.Cryptography.RandomNumberGenerator.Fill(privateKey);
            var wallet = new Wallet(privateKey, "", SeedMode.Bip39);
            _logger.LogDebug("Generated new wallet: {Address}", wallet.Account.PublicKey);
            return wallet;
        }

        public Wallet RestoreWallet(byte[] privateKey)
        {
            var wallet = new Wallet(privateKey, "", SeedMode.Bip39);
            return wallet;
        }

        public async Task<ulong> GetSolBalanceAsync(string publicKey)
        {
            try
            {
                var balance = await _rpcClient.GetBalanceAsync(publicKey);
                return balance.Result?.Value ?? 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get SOL balance for {PublicKey}", publicKey);
                throw;
            }
        }

        public async Task<ulong> GetTokenBalanceAsync(string publicKey, string mintAddress)
        {
            try
            {
                var tokenAccounts = await _rpcClient.GetTokenAccountsByOwnerAsync(
                    publicKey, 
                    mintAddress, 
                    TokenProgram.ProgramIdKey.ToString());
                
                if (tokenAccounts.Result?.Value?.Count > 0)
                {
                    var account = tokenAccounts.Result.Value[0];
                    return account.Account.Data.Parsed.Info.TokenAmount.AmountUlong;
                }
                
                return 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get token balance for {PublicKey} mint {MintAddress}", publicKey, mintAddress);
                throw;
            }
        }

        public async Task<bool> IsHealthyAsync()
        {
            try
            {
                var health = await _rpcClient.GetHealthAsync();
                return health.Result == "ok";
            }
            catch
            {
                return false;
            }
        }

        public async Task<ulong> GetSlotAsync()
        {
            var slot = await _rpcClient.GetSlotAsync();
            return slot.Result;
        }

        // Pool operations
        public async Task<PoolState> CreatePoolAsync(PoolCreationParams parameters)
        {
            try
            {
                // Generate pool ID
                var poolId = Guid.NewGuid().ToString("N");
                
                // Create token mints using new wallets
                var tokenAWallet = GenerateWallet();
                var tokenBWallet = GenerateWallet();
                
                // Ensure proper token ordering (smaller pubkey is Token A)
                if (string.Compare(tokenAWallet.Account.PublicKey.ToString(), tokenBWallet.Account.PublicKey.ToString()) > 0)
                {
                    (tokenAWallet, tokenBWallet) = (tokenBWallet, tokenAWallet);
                }
                
                // Generate decimals if not specified
                var tokenADecimals = parameters.TokenADecimals ?? Random.Shared.Next(0, 10);
                var tokenBDecimals = parameters.TokenBDecimals ?? Random.Shared.Next(0, 10);
                
                // Generate ratio if not specified
                var ratioWholeNumber = parameters.RatioWholeNumber ?? (ulong)Random.Shared.Next(1, 1000);
                var ratioDirection = parameters.RatioDirection ?? "a_to_b";
                
                // Create pool config
                var poolConfig = new PoolConfig
                {
                    TokenAMint = tokenAWallet.Account.PublicKey.ToString(),
                    TokenBMint = tokenBWallet.Account.PublicKey.ToString(),
                    TokenADecimals = tokenADecimals,
                    TokenBDecimals = tokenBDecimals,
                    RatioDirection = ratioDirection
                };
                
                // Normalize the pool configuration
                if (ratioDirection == "a_to_b")
                {
                    poolConfig.RatioANumerator = (ulong)Math.Pow(10, tokenADecimals);
                    poolConfig.RatioBDenominator = ratioWholeNumber * (ulong)Math.Pow(10, tokenBDecimals);
                }
                else
                {
                    poolConfig.RatioBDenominator = (ulong)Math.Pow(10, tokenBDecimals);
                    poolConfig.RatioANumerator = ratioWholeNumber * (ulong)Math.Pow(10, tokenADecimals);
                }
                
                // Store mint authorities for later minting
                _mintAuthorities[tokenAWallet.Account.PublicKey.ToString()] = tokenAWallet;
                _mintAuthorities[tokenBWallet.Account.PublicKey.ToString()] = tokenBWallet;
                
                // Create pool state
                var poolState = new PoolState
                {
                    PoolId = poolId,
                    TokenAMint = poolConfig.TokenAMint,
                    TokenBMint = poolConfig.TokenBMint,
                    TokenADecimals = poolConfig.TokenADecimals,
                    TokenBDecimals = poolConfig.TokenBDecimals,
                    RatioANumerator = poolConfig.RatioANumerator,
                    RatioBDenominator = poolConfig.RatioBDenominator,
                    VaultA = DeriveTokenVaultPda(poolId, poolConfig.TokenAMint),
                    VaultB = DeriveTokenVaultPda(poolId, poolConfig.TokenBMint),
                    LpMintA = DeriveLpMintPda(poolId, poolConfig.TokenAMint),
                    LpMintB = DeriveLpMintPda(poolId, poolConfig.TokenBMint),
                    MainTreasury = DeriveMainTreasuryPda(),
                    PoolTreasury = DerivePoolTreasuryPda(poolId),
                    PoolPaused = false,
                    SwapsPaused = false,
                    CreatedAt = DateTime.UtcNow
                };
                
                // Cache the pool state
                _poolCache[poolId] = poolState;
                
                _logger.LogInformation("Created pool {PoolId} with ratio {Ratio}", poolId, poolState.RatioDisplay);
                return poolState;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create pool");
                throw;
            }
        }

        public async Task<PoolState> GetPoolStateAsync(string poolId)
        {
            // For Phase 3, return cached pool state
            if (_poolCache.TryGetValue(poolId, out var poolState))
            {
                return poolState;
            }
            
            throw new KeyNotFoundException($"Pool {poolId} not found");
        }

        public async Task<List<PoolState>> GetAllPoolsAsync()
        {
            return _poolCache.Values.ToList();
        }

        // Deposit operations
        public async Task<DepositResult> ExecuteDepositAsync(
            Wallet wallet, 
            string poolId, 
            TokenType tokenType, 
            ulong amountInBasisPoints)
        {
            try
            {
                var pool = await GetPoolStateAsync(poolId);
                
                // Build deposit transaction
                var transaction = await _transactionBuilder.BuildDepositTransactionAsync(
                    wallet, poolId, tokenType, amountInBasisPoints);
                
                // Send transaction
                var signature = await SendTransactionAsync(transaction);
                
                // Confirm transaction
                await ConfirmTransactionAsync(signature);
                
                // Calculate LP tokens received (1:1 ratio)
                var lpTokensReceived = amountInBasisPoints;
                
                return new DepositResult
                {
                    TransactionSignature = signature,
                    TokensDeposited = amountInBasisPoints,
                    LpTokensReceived = lpTokensReceived,
                    PoolFeePaid = SolanaConfiguration.DEPOSIT_WITHDRAWAL_FEE,
                    NetworkFeePaid = 5000 // Base transaction fee
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to execute deposit");
                throw;
            }
        }

        // Withdrawal operations
        public async Task<WithdrawalResult> ExecuteWithdrawalAsync(
            Wallet wallet, 
            string poolId, 
            TokenType tokenType, 
            ulong lpTokenAmountToBurn)
        {
            try
            {
                var pool = await GetPoolStateAsync(poolId);
                
                // Build withdrawal transaction
                var transaction = await _transactionBuilder.BuildWithdrawalTransactionAsync(
                    wallet, poolId, tokenType, lpTokenAmountToBurn);
                
                // Send transaction
                var signature = await SendTransactionAsync(transaction);
                
                // Confirm transaction
                await ConfirmTransactionAsync(signature);
                
                // Calculate tokens withdrawn (1:1 ratio)
                var tokensWithdrawn = lpTokenAmountToBurn;
                
                return new WithdrawalResult
                {
                    TransactionSignature = signature,
                    LpTokensBurned = lpTokenAmountToBurn,
                    TokensWithdrawn = tokensWithdrawn,
                    PoolFeePaid = SolanaConfiguration.DEPOSIT_WITHDRAWAL_FEE,
                    NetworkFeePaid = 5000 // Base transaction fee
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to execute withdrawal");
                throw;
            }
        }

        // Swap operations
        public async Task<SwapResult> ExecuteSwapAsync(
            Wallet wallet, 
            string poolId, 
            SwapDirection direction, 
            ulong inputAmountBasisPoints, 
            ulong minimumOutputBasisPoints)
        {
            try
            {
                var pool = await GetPoolStateAsync(poolId);
                
                // Calculate swap output
                var swapCalc = SwapCalculation.Calculate(pool, direction, inputAmountBasisPoints);
                
                // Verify minimum output
                if (swapCalc.OutputAmount < minimumOutputBasisPoints)
                {
                    throw new InvalidOperationException($"Output amount {swapCalc.OutputAmount} is less than minimum {minimumOutputBasisPoints}");
                }
                
                // Build swap transaction
                var transaction = await _transactionBuilder.BuildSwapTransactionAsync(
                    wallet, poolId, direction, inputAmountBasisPoints, minimumOutputBasisPoints);
                
                // Send transaction
                var signature = await SendTransactionAsync(transaction);
                
                // Confirm transaction
                await ConfirmTransactionAsync(signature);
                
                return new SwapResult
                {
                    TransactionSignature = signature,
                    InputTokens = inputAmountBasisPoints,
                    OutputTokens = swapCalc.OutputAmount,
                    PoolFeePaid = SolanaConfiguration.SWAP_CONTRACT_FEE,
                    NetworkFeePaid = 5000, // Base transaction fee
                    SwapDirection = direction.ToString()
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to execute swap");
                throw;
            }
        }

        // Airdrop and transfers
        public async Task<string> RequestAirdropAsync(string walletAddress, ulong lamports)
        {
            try
            {
                var result = await _rpcClient.RequestAirdropAsync(walletAddress, lamports);
                if (result.WasRequestSuccessfullyHandled)
                {
                    _logger.LogInformation("Requested airdrop of {Lamports} to {Address}", lamports, walletAddress);
                    return result.Result;
                }
                
                throw new InvalidOperationException($"Airdrop request failed: {result.Reason}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to request airdrop");
                throw;
            }
        }

        public async Task<string> TransferTokensAsync(
            Wallet fromWallet, 
            string toWalletAddress, 
            string tokenMint, 
            ulong amount)
        {
            try
            {
                var transaction = await _transactionBuilder.BuildTransferTransactionAsync(
                    fromWallet, toWalletAddress, tokenMint, amount);
                
                var signature = await SendTransactionAsync(transaction);
                await ConfirmTransactionAsync(signature);
                
                _logger.LogInformation("Transferred {Amount} tokens to {Address}", amount, toWalletAddress);
                return signature;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to transfer tokens");
                throw;
            }
        }

        // Token minting (for testing)
        public async Task<string> MintTokensAsync(string tokenMint, string recipientAddress, ulong amount)
        {
            try
            {
                if (!_mintAuthorities.TryGetValue(tokenMint, out var mintAuthority))
                {
                    throw new InvalidOperationException($"No mint authority for token {tokenMint}");
                }
                
                var transaction = await _transactionBuilder.BuildMintTransactionAsync(
                    mintAuthority, tokenMint, recipientAddress, amount);
                
                var signature = await SendTransactionAsync(transaction);
                await ConfirmTransactionAsync(signature);
                
                _logger.LogInformation("Minted {Amount} tokens to {Address}", amount, recipientAddress);
                return signature;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to mint tokens");
                throw;
            }
        }

        // PDA derivation
        public string DerivePoolStatePda(string poolId)
        {
            var seeds = new List<byte[]>
            {
                Encoding.UTF8.GetBytes("pool_state"),
                Encoding.UTF8.GetBytes(poolId)
            };
            
            if (PublicKey.TryFindProgramAddress(
                seeds, 
                new PublicKey(_config.ProgramId), 
                out var pda, 
                out _))
            {
                return pda.ToString();
            }
            
            throw new InvalidOperationException("Failed to derive pool state PDA");
        }

        public string DeriveTokenVaultPda(string poolId, string tokenMint)
        {
            var seeds = new List<byte[]>
            {
                Encoding.UTF8.GetBytes("vault"),
                Encoding.UTF8.GetBytes(poolId),
                new PublicKey(tokenMint).KeyBytes
            };
            
            if (PublicKey.TryFindProgramAddress(
                seeds, 
                new PublicKey(_config.ProgramId), 
                out var pda, 
                out _))
            {
                return pda.ToString();
            }
            
            throw new InvalidOperationException("Failed to derive token vault PDA");
        }

        public string DeriveLpMintPda(string poolId, string tokenMint)
        {
            var seeds = new List<byte[]>
            {
                Encoding.UTF8.GetBytes("lp_mint"),
                Encoding.UTF8.GetBytes(poolId),
                new PublicKey(tokenMint).KeyBytes
            };
            
            if (PublicKey.TryFindProgramAddress(
                seeds, 
                new PublicKey(_config.ProgramId), 
                out var pda, 
                out _))
            {
                return pda.ToString();
            }
            
            throw new InvalidOperationException("Failed to derive LP mint PDA");
        }

        public string DerivePoolTreasuryPda(string poolId)
        {
            var seeds = new List<byte[]>
            {
                Encoding.UTF8.GetBytes("pool_treasury"),
                Encoding.UTF8.GetBytes(poolId)
            };
            
            if (PublicKey.TryFindProgramAddress(
                seeds, 
                new PublicKey(_config.ProgramId), 
                out var pda, 
                out _))
            {
                return pda.ToString();
            }
            
            throw new InvalidOperationException("Failed to derive pool treasury PDA");
        }

        private string DeriveMainTreasuryPda()
        {
            var seeds = new List<byte[]>
            {
                Encoding.UTF8.GetBytes("main_treasury")
            };
            
            if (PublicKey.TryFindProgramAddress(
                seeds, 
                new PublicKey(_config.ProgramId), 
                out var pda, 
                out _))
            {
                return pda.ToString();
            }
            
            throw new InvalidOperationException("Failed to derive main treasury PDA");
        }

        // System state
        public async Task<bool> IsSystemPausedAsync()
        {
            // For Phase 3, return false (not paused)
            return false;
        }

        public async Task<bool> IsPoolPausedAsync(string poolId)
        {
            if (_poolCache.TryGetValue(poolId, out var pool))
            {
                return pool.PoolPaused;
            }
            return false;
        }

        public async Task<bool> ArePoolSwapsPausedAsync(string poolId)
        {
            if (_poolCache.TryGetValue(poolId, out var pool))
            {
                return pool.SwapsPaused;
            }
            return false;
        }

        // Transaction utilities
        public async Task<string> SendTransactionAsync(byte[] transaction)
        {
            try
            {
                var result = await _rpcClient.SendTransactionAsync(transaction);
                if (result.WasRequestSuccessfullyHandled)
                {
                    return result.Result;
                }
                
                throw new InvalidOperationException($"Transaction failed: {result.Reason}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send transaction");
                throw;
            }
        }

        public async Task<bool> ConfirmTransactionAsync(string signature, int maxRetries = 3)
        {
            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    var signatures = new List<string> { signature };
                    var result = await _rpcClient.GetSignatureStatusesAsync(signatures);
                    if (result.WasRequestSuccessfullyHandled && result.Result?.Value?.Count > 0)
                    {
                        var status = result.Result.Value[0];
                        if (status?.ConfirmationStatus == "confirmed" || status?.ConfirmationStatus == "finalized")
                        {
                            return true;
                        }
                    }
                    
                    await Task.Delay(1000); // Wait 1 second before retry
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error confirming transaction, retry {Retry}/{MaxRetries}", i + 1, maxRetries);
                }
            }
            
            return false;
        }
    }
}