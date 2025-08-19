using System.Text;
using System.Text.Json;
using System.IO;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Solnet.Rpc;
using Solnet.Rpc.Builders;
using Solnet.Rpc.Core.Http;
using Solnet.Rpc.Messages;
using Solnet.Rpc.Models;
using Solnet.Rpc.Types;
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
    private readonly IStorageService _storageService;
    private readonly ILogger<SolanaClientService> _logger;
    private readonly SolanaConfig _config;
        private readonly Dictionary<string, PoolState> _poolCache = new();
        private readonly Dictionary<string, Wallet> _mintAuthorities = new();

        public SolanaClientService(
            IConfiguration configuration,
            ITransactionBuilderService transactionBuilder,
            IStorageService storageService,
            ILogger<SolanaClientService> logger)
        {
            _config = configuration.GetSection("SolanaConfiguration").Get<SolanaConfig>() ?? new SolanaConfig();
            _transactionBuilder = transactionBuilder;
            _storageService = storageService;
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
        public async Task<TransactionSimulationResult> SimulatePoolCreationAsync(PoolCreationParams parameters)
        {
            try
            {
                _logger.LogDebug("🔍 Simulating pool creation to validate transaction format");
                
                // Step 1: Create temporary payer wallet for simulation
                var payerWallet = GenerateWallet();
                
                // Step 2: Create token mints for simulation
                var (tokenAMint, tokenBMint, tokenADecimals, tokenBDecimals) = await CreateTokenMintsAsync(parameters);
                
                // Step 3: Create normalized pool configuration
                var poolConfig = CreateNormalizedPoolConfig(
                    tokenAMint, tokenBMint, tokenADecimals, tokenBDecimals, parameters);
                
                // Step 4: Simulate the pool creation transaction
                var simulationResult = await _transactionBuilder.SimulateCreatePoolTransactionAsync(
                    payerWallet, poolConfig);
                
                _logger.LogDebug("Pool creation simulation completed: {Status}", 
                    simulationResult.IsSuccessful ? "SUCCESS" : "FAILED");
                
                if (!simulationResult.IsSuccessful)
                {
                    _logger.LogWarning("Pool creation simulation failed: {Error}", simulationResult.ErrorMessage);
                }
                
                return simulationResult;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to simulate pool creation");
                
                return new TransactionSimulationResult
                {
                    IsSuccessful = false,
                    ErrorMessage = $"Simulation failed: {ex.Message}",
                    SimulationSummary = $"❌ Pool creation simulation failed with exception: {ex.Message}"
                };
            }
        }
        
        public async Task<PoolState> CreatePoolAsync(PoolCreationParams parameters)
        {
            try
            {
                _logger.LogDebug("Creating pool with blockchain transaction (includes simulation validation)");
                
                // Step 1: Simulate pool creation first to validate transaction format
                _logger.LogDebug("🔍 Step 1: Simulating pool creation transaction...");
                var simulationResult = await SimulatePoolCreationAsync(parameters);
                
                // Log simulation results
                _logger.LogDebug(simulationResult.SimulationSummary);
                
                if (!simulationResult.WouldSucceed)
                {
                    _logger.LogWarning("⚠️ Pool creation simulation indicates potential failure: {Error}", 
                        simulationResult.ErrorMessage);
                    _logger.LogDebug("🔄 Continuing with actual pool creation anyway for testing purposes...");
                }
                else
                {
                    _logger.LogDebug("✅ Pool creation simulation successful - proceeding with actual creation");
                }
                
                // Step 2: Create and fund payer wallet for pool creation fees
                _logger.LogDebug("💰 Step 2: Setting up payer wallet...");
                var payerWallet = GenerateWallet();
                
                // Request airdrop for pool creation fees (1.15+ SOL required)
                var requiredFunding = SolanaConfiguration.REGISTRATION_FEE + (10 * 1_000_000_000UL); // Extra 10 SOL for operations
                await RequestAirdropAsync(payerWallet.Account.PublicKey.ToString(), requiredFunding);
                
                // Wait for airdrop confirmation
                await Task.Delay(2000);
                
                // Step 3: Create token mints and fund them
                _logger.LogDebug("🪙 Step 3: Creating token mints...");
                var (tokenAMint, tokenBMint, tokenADecimals, tokenBDecimals) = await CreateTokenMintsAsync(parameters);
                
                // Step 4: Create normalized pool configuration
                var poolConfig = CreateNormalizedPoolConfig(
                    tokenAMint, tokenBMint, tokenADecimals, tokenBDecimals, parameters);
                
                string poolCreationSignature;
                string poolStatePda;
                
                try
                {
                    // Step 5: Try to build and send pool creation transaction
                    _logger.LogDebug("📤 Step 5: Building and sending pool creation transaction...");
                    var poolTransaction = await _transactionBuilder.BuildCreatePoolTransactionAsync(
                        payerWallet, poolConfig);
                    
                    poolCreationSignature = await SendTransactionAsync(poolTransaction);
                    
                    // Step 6: Confirm pool creation transaction
                    _logger.LogDebug("⏳ Step 6: Confirming pool creation transaction...");
                    var confirmed = await ConfirmTransactionAsync(poolCreationSignature, maxRetries: 5);
                    if (!confirmed)
                    {
                        _logger.LogWarning("Transaction confirmation failed, but continuing...");
                    }
                    
                    // Step 7: Derive pool state PDA from the created tokens using unified seeds
                    var (ordA1, ordB1) = GetOrderedTokens(poolConfig.TokenAMint, poolConfig.TokenBMint);
                    var (bpA1, bpB1) = CalculateBasisPoints(
                        poolConfig.TokenAMint,
                        poolConfig.TokenBMint,
                        poolConfig.TokenADecimals,
                        poolConfig.TokenBDecimals,
                        poolConfig.RatioWholeNumber,
                        poolConfig.RatioDirection);
                    poolStatePda = DerivePoolStatePda(ordA1, ordB1, bpA1, bpB1);
                    
                    _logger.LogDebug("✅ Successfully created REAL blockchain pool with signature {Signature}", poolCreationSignature);
                }
                catch (Exception transactionEx)
                {
                    _logger.LogWarning(transactionEx, "Blockchain transaction failed, falling back to simulated pool creation for testing");
                    
                    // Fallback: Create a simulated pool with deterministic ID for testing
                    poolCreationSignature = $"simulated_tx_{Guid.NewGuid():N}";
                    var (ordA2, ordB2) = GetOrderedTokens(poolConfig.TokenAMint, poolConfig.TokenBMint);
                    var (bpA2, bpB2) = CalculateBasisPoints(
                        poolConfig.TokenAMint,
                        poolConfig.TokenBMint,
                        poolConfig.TokenADecimals,
                        poolConfig.TokenBDecimals,
                        poolConfig.RatioWholeNumber,
                        poolConfig.RatioDirection);
                    poolStatePda = DerivePoolStatePda(ordA2, ordB2, bpA2, bpB2);
                    
                    _logger.LogDebug("📋 Created SIMULATED pool for testing purposes");
                }
                
                // Step 8: Create pool state object with all derived addresses
                _logger.LogDebug("📋 Step 8: Creating pool state object...");
                var poolState = new PoolState
                {
                    PoolId = poolStatePda,  // Use PDA as pool ID for real blockchain pools
                    TokenAMint = poolConfig.TokenAMint,
                    TokenBMint = poolConfig.TokenBMint,
                    TokenADecimals = poolConfig.TokenADecimals,
                    TokenBDecimals = poolConfig.TokenBDecimals,
                    RatioANumerator = poolConfig.RatioANumerator,
                    RatioBDenominator = poolConfig.RatioBDenominator,
                    VaultA = DeriveTokenAVaultPda(poolStatePda),
                    VaultB = DeriveTokenBVaultPda(poolStatePda),
                    LpMintA = DeriveLpTokenAMintPda(poolStatePda),
                    LpMintB = DeriveLpTokenBMintPda(poolStatePda),
                    MainTreasury = DeriveMainTreasuryPda(),
                    PoolTreasury = DerivePoolTreasuryPda(poolStatePda),
                    PoolPaused = false,
                    SwapsPaused = false,
                    CreatedAt = DateTime.UtcNow,
                    CreationSignature = poolCreationSignature,
                    PayerWallet = payerWallet.Account.PublicKey.ToString()
                };
                
                // Step 9: Cache the pool state for later operations
                _logger.LogDebug("💾 Step 9: Caching pool state...");
                _poolCache[poolState.PoolId] = poolState;
                
                _logger.LogDebug(
                    "Pool created: {PoolId} ({Status}). " +
                    "Ratio: {RatioDisplay}, TokenA: {TokenA} ({TokenADecimals}), TokenB: {TokenB} ({TokenBDecimals})",
                    poolState.PoolId, poolState.IsBlockchainPool ? "BLOCKCHAIN" : "SIMULATED", poolState.RatioDisplay,
                    poolState.TokenAMint, poolState.TokenADecimals,
                    poolState.TokenBMint, poolState.TokenBDecimals);
                
                        return poolState;
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Failed to create pool");
        throw;
    }
}

public async Task<List<string>> GetOrCreateManagedPoolsAsync(int targetPoolCount = 3)
{
    try
    {
        _logger.LogDebug("🏊 Managing pool lifecycle - target: {TargetCount} pools", targetPoolCount);
        
        // Snapshot all on-chain program accounts for diagnostics
        await SaveAllBlockchainPoolsSnapshotAsync();
        
        // Step 1: Load existing active pool IDs from storage
        var activePoolIds = await _storageService.LoadActivePoolIdsAsync();
        _logger.LogDebug("📋 Found {Count} stored pool IDs", activePoolIds.Count);

        // Step 1.1: Attempt to auto-import previously saved real pools (created by this app) into active list
        // This ensures pools created in prior runs are reused if they still exist on-chain
        var savedRealPools = await _storageService.LoadRealPoolsAsync();
        foreach (var rp in savedRealPools)
        {
            if (!activePoolIds.Contains(rp.PoolId))
            {
                var exists = await ValidatePoolExistsOnBlockchainAsync(rp.PoolId);
                if (exists)
                {
                    _logger.LogDebug("♻️ Auto-importing saved pool into active set: {PoolId}", rp.PoolId);
                    activePoolIds.Add(rp.PoolId);
                }
                else
                {
                    _logger.LogWarning("🗑️ Removing saved pool not found on-chain: {PoolId}", rp.PoolId);
                    await _storageService.DeleteRealPoolAsync(rp.PoolId);
                }
            }
        }
        
        // Step 2: Validate each stored pool still exists and works
        var validPoolIds = new List<string>();
        foreach (var poolId in activePoolIds)
        {
            if (await ValidatePoolExistsAsync(poolId))
            {
                validPoolIds.Add(poolId);
                _logger.LogDebug("✅ Pool {PoolId} validated successfully", poolId);
            }
            else
            {
                _logger.LogWarning("❌ Pool {PoolId} failed validation - will be cleaned up", poolId);
                await _storageService.CleanupPoolDataAsync(poolId);
                await _storageService.CleanupAllThreadDataForPoolAsync(poolId);
            }
        }
        
        _logger.LogDebug("✅ {ValidCount} of {TotalCount} pools passed validation", validPoolIds.Count, activePoolIds.Count);
        
        // Step 3: Create additional pools if needed
        while (validPoolIds.Count < targetPoolCount)
        {
            try
            {
                _logger.LogDebug("🔨 Creating pool {Current}/{Target}...", validPoolIds.Count + 1, targetPoolCount);
                
                var poolParams = new PoolCreationParams
                {
                    TokenADecimals = 9, // SOL-like
                    TokenBDecimals = 6, // USDC-like
                    RatioWholeNumber = 1000, // 1:1000 ratio
                    RatioDirection = "a_to_b"
                };
                
                var newPool = await CreatePoolAsync(poolParams);
                validPoolIds.Add(newPool.PoolId);
                
                _logger.LogDebug("✅ Created new pool: {PoolId}", newPool.PoolId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create pool {Current}/{Target}", validPoolIds.Count + 1, targetPoolCount);
                // Continue trying to create other pools
            }
        }
        
        // Step 4: Save the updated active pool list
        await _storageService.SaveActivePoolIdsAsync(validPoolIds);
        
        _logger.LogDebug("🎯 Pool management complete: {Count} active pools ready", validPoolIds.Count);
        return validPoolIds;
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Failed to manage pool lifecycle");
        throw;
    }
}

public async Task<bool> ValidatePoolExistsAsync(string poolId)
{
    try
    {
        // 1) Fast path: known pool in memory
        if (_poolCache.ContainsKey(poolId))
        {
            return true;
        }

        // 2) On-chain validation for externally created pools
        var existsOnChain = await ValidatePoolExistsOnBlockchainAsync(poolId);
        if (existsOnChain)
        {
            _logger.LogDebug("Pool {PoolId} validated via on-chain lookup (not yet cached)", poolId);
            return true;
        }

        // 3) Legacy/local fetch as a last resort (may throw if unsupported)
        try
        {
            var poolState = await GetPoolStateAsync(poolId);
            return poolState != null;
        }
        catch (KeyNotFoundException)
        {
            // Expected if the pool was not created by this service instance
        }

        return false;
    }
    catch (Exception ex)
    {
        _logger.LogDebug("Pool {PoolId} validation failed: {Error}", poolId, ex.Message);
        return false;
    }
}

public async Task CleanupInvalidPoolsAsync()
{
    try
    {
        _logger.LogDebug("🧹 Starting cleanup of invalid pools...");
        
        // Snapshot all on-chain program accounts for diagnostics
        await SaveAllBlockchainPoolsSnapshotAsync();
        
        var activePoolIds = await _storageService.LoadActivePoolIdsAsync();
        var validPoolIds = new List<string>();
        var cleanupCount = 0;
        
        foreach (var poolId in activePoolIds)
        {
            if (await ValidatePoolExistsAsync(poolId))
            {
                validPoolIds.Add(poolId);
            }
            else
            {
                _logger.LogDebug("🗑️ Cleaning up invalid pool: {PoolId}", poolId);
                await _storageService.CleanupPoolDataAsync(poolId);
                await _storageService.CleanupAllThreadDataForPoolAsync(poolId);
                cleanupCount++;
            }
        }
        
        // Update the active pools list
        await _storageService.SaveActivePoolIdsAsync(validPoolIds);
        
        _logger.LogDebug("✅ Cleanup complete: removed {CleanupCount} invalid pools, {ValidCount} remain", 
            cleanupCount, validPoolIds.Count);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Failed to cleanup invalid pools");
        throw;
            }
    }
    
    public async Task<CoreWalletConfig> GetOrCreateCoreWalletAsync()
    {
        try
        {
            _logger.LogDebug("🔑 Initializing core wallet for token mint authority...");
            
            // Try to load existing core wallet
            var existingWallet = await _storageService.LoadCoreWalletAsync();
            if (existingWallet != null)
            {
                _logger.LogDebug("✅ Loaded existing core wallet: {PublicKey}", existingWallet.PublicKey);
                
                // Check balance and update
                var walletBalance = await GetSolBalanceAsync(existingWallet.PublicKey);
                existingWallet.CurrentSolBalance = walletBalance;
                existingWallet.LastBalanceCheck = DateTime.UtcNow;
                
                if (walletBalance < existingWallet.MinimumSolBalance)
                {
                    _logger.LogWarning("⚠️ Core wallet balance is low: {Balance} SOL (minimum: {MinBalance} SOL)", 
                        walletBalance / 1_000_000_000.0, existingWallet.MinimumSolBalance / 1_000_000_000.0);
                }
                
                await _storageService.SaveCoreWalletAsync(existingWallet);
                return existingWallet;
            }
            
            // Create new core wallet
            _logger.LogDebug("🆕 Creating new core wallet...");
            var newWallet = GenerateWallet();
            
            var coreWalletConfig = new CoreWalletConfig
            {
                PrivateKey = Convert.ToBase64String(newWallet.Account.PrivateKey.KeyBytes),
                PublicKey = newWallet.Account.PublicKey.ToString(),
                CreatedAt = DateTime.UtcNow,
                LastBalanceCheck = DateTime.UtcNow
            };
            
            // Check initial balance but don't fund automatically
            var currentBalance = await GetSolBalanceAsync(coreWalletConfig.PublicKey);
            coreWalletConfig.CurrentSolBalance = currentBalance;
            
            _logger.LogDebug("📊 Core wallet created with balance: {Balance} SOL (funding will occur when needed for pool creation)", 
                currentBalance / 1_000_000_000.0);
            
            // Save the core wallet
            await _storageService.SaveCoreWalletAsync(coreWalletConfig);
            
            _logger.LogDebug("✅ Core wallet created: {PublicKey} ({Balance} SOL)", 
                coreWalletConfig.PublicKey, currentBalance / 1_000_000_000.0);
            
            return coreWalletConfig;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get or create core wallet");
            throw;
        }
    }
    
    private async Task EnsureCoreWalletHasSufficientSolAsync(CoreWalletConfig coreWallet)
    {
        try
        {
            _logger.LogDebug("💰 Checking core wallet SOL balance before pool creation...");
            
            // Check current balance
            var currentBalance = await GetSolBalanceAsync(coreWallet.PublicKey);
            var requiredBalance = 10_000_000_000UL; // 10 SOL minimum (ensures sufficient balance for registration fees and operations)
            
            _logger.LogDebug("Current balance: {Current} SOL, Required: {Required} SOL", 
                currentBalance / 1_000_000_000.0, requiredBalance / 1_000_000_000.0);
            
            if (currentBalance >= requiredBalance)
            {
                _logger.LogDebug("✅ Core wallet has sufficient SOL balance");
                return;
            }
            
            _logger.LogDebug("⚠️ Insufficient SOL balance, attempting airdrop...");
            
            // Request multiple airdrops to reach 100 SOL target (optimal for localnet)
            var targetAmount = 100_000_000_000UL; // 100 SOL target
            var maxAirdropPerRequest = 10_000_000_000UL; // 10 SOL per request (optimal for localnet)
            var funded = false;
            
            _logger.LogDebug("💰 Attempting to fund wallet with {Target} SOL using multiple airdrops", targetAmount / 1_000_000_000.0);
            
            for (int attempt = 1; attempt <= 15 && !funded; attempt++) // Up to 15 attempts (15 x 10 = 150 SOL max)
            {
                try
                {
                    var neededAmount = Math.Min(maxAirdropPerRequest, targetAmount);
                    
                    _logger.LogDebug("Airdrop attempt {Attempt}/15: Requesting {Amount} SOL", 
                        attempt, neededAmount / 1_000_000_000.0);
                    
                    var airdropSignature = await RequestAirdropAsync(coreWallet.PublicKey, neededAmount);
                    _logger.LogDebug("✅ Airdrop request successful: signature {Signature}", airdropSignature);
                    
                    // Wait for confirmation
                    await Task.Delay(2000); // Wait 2 seconds between requests
                    
                    // Check balance
                    currentBalance = await GetSolBalanceAsync(coreWallet.PublicKey);
                    _logger.LogDebug("Balance after attempt {Attempt}: {Balance} SOL", 
                        attempt, currentBalance / 1_000_000_000.0);
                    
                    // Check if we have enough (target reached)
                    if (currentBalance >= targetAmount || currentBalance >= requiredBalance)
                    {
                        funded = true;
                        _logger.LogDebug("🎉 Core wallet successfully funded! Current: {Balance} SOL", 
                            currentBalance / 1_000_000_000.0);
                        break;
                    }
                    
                    // Reduce airdrop size if localnet has strict limits
                    if (attempt >= 3 && currentBalance == 0)
                    {
                        _logger.LogWarning("⚠️ No balance increase after {Attempts} attempts. Reducing request size.", attempt);
                        maxAirdropPerRequest = 1_000_000_000UL; // Try 1 SOL per request
                        _logger.LogDebug("Reducing airdrop size to {Amount} SOL per request", maxAirdropPerRequest / 1_000_000_000.0);
                    }
                    
                    if (attempt >= 8 && currentBalance == 0)
                    {
                        _logger.LogWarning("⚠️ Still no balance after {Attempts} attempts. Trying very small amounts.", attempt);
                        maxAirdropPerRequest = 500_000_000UL; // Try 0.5 SOL per request
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Airdrop attempt {Attempt} failed: {Message}", attempt, ex.Message);
                    
                    // Wait longer between failed attempts
                    await Task.Delay(3000);
                }
            }
            
            if (!funded && currentBalance > 0)
            {
                _logger.LogDebug("💰 Partial funding achieved: {Balance} SOL", currentBalance / 1_000_000_000.0);
            }
            
            // Final balance check - allow proceeding with lower balance for testing
            if (currentBalance < 1_200_000_000UL) // Minimum 1.2 SOL for registration fee
            {
                var errorMsg = $"Cannot create pool: Core wallet has insufficient SOL balance. " +
                              $"Current: {currentBalance / 1_000_000_000.0:F2} SOL, " +
                              $"Required: {1_200_000_000UL / 1_000_000_000.0:F2} SOL minimum. " +
                              $"Airdrop attempts failed. Please fund the core wallet manually.";
                
                _logger.LogError(errorMsg);
                throw new InvalidOperationException(errorMsg);
            }
            else if (currentBalance < requiredBalance)
            {
                _logger.LogWarning("⚠️ Wallet balance ({Current} SOL) is below preferred amount ({Required} SOL) but sufficient for testing",
                    currentBalance / 1_000_000_000.0, requiredBalance / 1_000_000_000.0);
            }
            
            // Update wallet config with new balance
            coreWallet.CurrentSolBalance = currentBalance;
            coreWallet.LastBalanceCheck = DateTime.UtcNow;
            await _storageService.SaveCoreWalletAsync(coreWallet);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to ensure core wallet has sufficient SOL");
            throw;
        }
    }
    
    public async Task<StressTestTokenMint> CreateTokenMintAsync(int decimals, string? symbol = null)
    {
        try
        {
            _logger.LogDebug("🪙 Creating token mint with {Decimals} decimals...", decimals);
            
            // Load existing core wallet as mint authority
            var coreWallet = await _storageService.LoadCoreWalletAsync();
            if (coreWallet == null)
            {
                throw new InvalidOperationException("Core wallet not found. This should have been created during application startup.");
            }
            
            // Decode the Base64 private key correctly
            var privateKeyBytes = Convert.FromBase64String(coreWallet.PrivateKey);
            var coreKeyPair = RestoreWallet(privateKeyBytes);
            
            // Generate new mint address
            var mintKeypair = GenerateWallet();
            var mintAddress = mintKeypair.Account.PublicKey.ToString();
            
            _logger.LogDebug("🔧 Creating mint {MintAddress} with authority {Authority}", 
                mintAddress, coreWallet.PublicKey);
            
            // Build create mint transaction
            var rentExemptBalance = await _rpcClient.GetMinimumBalanceForRentExemptionAsync(TokenProgram.MintAccountDataSize);
            var createMintIx = SystemProgram.CreateAccount(
                coreKeyPair.Account.PublicKey,
                mintKeypair.Account.PublicKey,
                rentExemptBalance.Result,
                TokenProgram.MintAccountDataSize,
                TokenProgram.ProgramIdKey);
            
            var initializeMintIx = TokenProgram.InitializeMint(
                mintKeypair.Account.PublicKey,
                (byte)decimals,
                coreKeyPair.Account.PublicKey, // mint authority
                coreKeyPair.Account.PublicKey); // freeze authority
            
            var blockHash = await _rpcClient.GetLatestBlockHashAsync();
            var createMintTx = new TransactionBuilder()
                .SetFeePayer(coreKeyPair.Account.PublicKey)
                .SetRecentBlockHash(blockHash.Result.Value.Blockhash)
                .AddInstruction(createMintIx)
                .AddInstruction(initializeMintIx)
                .Build(new[] { coreKeyPair.Account, mintKeypair.Account });
            
            // Submit transaction with preflight ON, lower commitment to reduce flakiness
            var signature = await _rpcClient.SendTransactionAsync(createMintTx, skipPreflight: false, commitment: Commitment.Processed);
            if (!signature.WasSuccessful)
            {
                throw new InvalidOperationException($"Failed to create token mint: {signature.Reason}");
            }
            
            _logger.LogDebug("📤 Sent token mint creation transaction: {Signature}", signature.Result);
            
            // Wait for confirmation with retry logic
            _logger.LogDebug("⏳ Waiting for token mint transaction confirmation...");
            var confirmed = false;
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                await Task.Delay(3000); // Longer wait for localnet
                confirmed = await ConfirmTransactionAsync(signature.Result);
                if (confirmed)
                {
                    _logger.LogDebug("✅ Token mint transaction confirmed on attempt {Attempt}", attempt);
                    break;
                }
                _logger.LogWarning("⏳ Token mint confirmation attempt {Attempt}/3 failed", attempt);
            }
            
            if (!confirmed)
            {
                _logger.LogError("❌ Token mint creation failed to confirm after 3 attempts: {Signature}", signature.Result);
                throw new InvalidOperationException($"Token mint transaction failed to confirm: {signature.Result}");
            }
            
            // Verify the token mint account is now accessible
            _logger.LogDebug("🔍 Verifying token mint account is accessible...");
            
            // Try with different commitment levels if first attempt fails
            var mintAccountInfo = await _rpcClient.GetAccountInfoAsync(mintAddress, Solnet.Rpc.Types.Commitment.Confirmed);
            if (mintAccountInfo.Result?.Value == null)
            {
                _logger.LogWarning("⚠️ Token mint not found with Confirmed commitment, trying Finalized...");
                await Task.Delay(2000); // Additional wait
                mintAccountInfo = await _rpcClient.GetAccountInfoAsync(mintAddress, Solnet.Rpc.Types.Commitment.Finalized);
            }
            
            if (mintAccountInfo.Result?.Value == null)
            {
                _logger.LogWarning("⚠️ Token mint not found with Finalized commitment, trying Processed...");
                await Task.Delay(1000);
                mintAccountInfo = await _rpcClient.GetAccountInfoAsync(mintAddress, Solnet.Rpc.Types.Commitment.Processed);
            }
            
            if (mintAccountInfo.Result?.Value == null)
            {
                _logger.LogError("❌ Token mint account not accessible with any commitment level: {MintAddress}", mintAddress);
                _logger.LogDebug("🔍 Transaction was confirmed but account is not accessible. This might be a localnet timing issue.");
                _logger.LogDebug("🔍 Transaction signature: {Signature}", signature.Result);
                _logger.LogDebug("🔍 Mint address: {MintAddress}", mintAddress);
                // For now, continue anyway since the transaction was confirmed
                _logger.LogWarning("⚠️ Continuing despite account accessibility issue...");
            }
            else
            {
                _logger.LogDebug("✅ Token mint account verified: {MintAddress}", mintAddress);
            }
            
            // Create token mint info
            var tokenMint = new StressTestTokenMint
            {
                MintAddress = mintAddress,
                Decimals = decimals,
                MintAuthority = coreWallet.PublicKey,
                CreationSignature = signature.Result,
                CreatedAt = DateTime.UtcNow,
                TotalMinted = 0
            };
            
            // Save to storage
            await _storageService.SaveTokenMintAsync(tokenMint);
            
            _logger.LogDebug("✅ Token mint created successfully: {MintAddress}", mintAddress);
            return tokenMint;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create token mint");
            throw;
        }
    }

    public async Task<RealPoolData> CreateRealPoolAsync(PoolCreationParams parameters)
    {
        try
        {
            _logger.LogDebug("🏊 Creating REAL pool on the smart contract...");
            
            // Step 0: Check if a pool already exists for these parameters and reuse if valid
            var existingPools = await _storageService.LoadRealPoolsAsync();
            var existingPool = existingPools.FirstOrDefault(p => 
                p.TokenADecimals == parameters.TokenADecimals &&
                p.TokenBDecimals == parameters.TokenBDecimals);
                
            if (existingPool != null)
            {
                _logger.LogDebug("♻️ Found existing pool: {PoolId}", existingPool.PoolId);
                
                // Validate the existing pool still exists on blockchain
                try
                {
                    var poolExists = await ValidatePoolExistsOnBlockchainAsync(existingPool.PoolId);
                    if (poolExists)
                    {
                        _logger.LogDebug("✅ Existing pool validated on blockchain - reusing pool: {PoolId}", existingPool.PoolId);
                        return new RealPoolData
                        {
                            PoolId = existingPool.PoolId,
                            TokenAMint = existingPool.TokenAMint,
                            TokenBMint = existingPool.TokenBMint,
                            TokenADecimals = existingPool.TokenADecimals,
                            TokenBDecimals = existingPool.TokenBDecimals,
                            RatioANumerator = existingPool.RatioANumerator,
                            RatioBDenominator = existingPool.RatioBDenominator
                        };
                    }
                    else
                    {
                        _logger.LogWarning("❌ Existing pool not found on blockchain - will create new pool");
                        // Remove invalid pool from storage
                        await _storageService.DeleteRealPoolAsync(existingPool.PoolId);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to validate existing pool {PoolId} - will create new pool", existingPool.PoolId);
                    await _storageService.DeleteRealPoolAsync(existingPool.PoolId);
                }
            }
            
            // Step 1: Ensure treasury system is initialized before pool creation
            await InitializeTreasurySystemAsync();
            
            // Step 2: Load existing core wallet (should exist from startup)
            var coreWallet = await _storageService.LoadCoreWalletAsync();
            if (coreWallet == null)
            {
                throw new InvalidOperationException("Core wallet not found. This should have been created during application startup.");
            }
            
            // Step 3: Check SOL balance and attempt funding if needed
            await CheckCoreWalletBalanceAsync(coreWallet);
            // Extra: wait until balance is visible at Finalized commitment (stabilizes preflight)
            try
            {
                for (int i = 0; i < 5; i++)
                {
                    var bal = await _rpcClient.GetBalanceAsync(coreWallet.PublicKey, Commitment.Finalized);
                    if ((bal.Result?.Value ?? 0) >= 1_200_000_000UL) break;
                    await Task.Delay(1000);
                }
            }
            catch { /* best-effort */ }
            
            // Decode the Base64 private key correctly
            var privateKeyBytes = Convert.FromBase64String(coreWallet.PrivateKey);
            var coreKeyPair = RestoreWallet(privateKeyBytes);
            
            // Step 3: Create token mints using core wallet as authority
            _logger.LogDebug("🪙 Creating token mints...");
            var tokenADecimals = parameters.TokenADecimals ?? 9; // Default SOL-like
            var tokenBDecimals = parameters.TokenBDecimals ?? 6; // Default USDC-like
            
            var tokenAMint = await CreateTokenMintAsync(tokenADecimals, "TESTA");
            var tokenBMint = await CreateTokenMintAsync(tokenBDecimals, "TESTB");

            // Ensure proper token ordering (smaller pubkey is Token A), including decimals
            if (string.Compare(tokenAMint.MintAddress, tokenBMint.MintAddress, StringComparison.Ordinal) > 0)
            {
                (tokenAMint, tokenBMint) = (tokenBMint, tokenAMint);
                (tokenADecimals, tokenBDecimals) = (tokenBDecimals, tokenADecimals);
            }

            // Step 3: Create normalized pool configuration
            var ratioWholeNumber = parameters.RatioWholeNumber ?? 1000;
            var ratioDirection = parameters.RatioDirection ?? "a_to_b";

            var (ratioANumerator, ratioBDenominator) = ratioDirection == "a_to_b"
                ? ((ulong)Math.Pow(10, tokenADecimals), ratioWholeNumber * (ulong)Math.Pow(10, tokenBDecimals))
                : (ratioWholeNumber * (ulong)Math.Pow(10, tokenADecimals), (ulong)Math.Pow(10, tokenBDecimals));
            
            var poolConfig = CreateNormalizedPoolConfig(
                tokenAMint.MintAddress, tokenBMint.MintAddress, 
                tokenADecimals, tokenBDecimals, 
                new PoolCreationParams 
                { 
                    TokenADecimals = tokenADecimals,
                    TokenBDecimals = tokenBDecimals,
                    RatioWholeNumber = ratioWholeNumber,
                    RatioDirection = ratioDirection
                });
            
            // Step 4: Try to create the pool on the smart contract
            string poolCreationSignature;
            try
            {
                _logger.LogDebug("🔍 Validating token mints before pool creation...");
                // Verify token mints exist and are valid (like JavaScript does)
                // Try multiple commitment levels for localnet compatibility
                var tokenAInfo = await _rpcClient.GetAccountInfoAsync(tokenAMint.MintAddress, Solnet.Rpc.Types.Commitment.Processed);
                var tokenBInfo = await _rpcClient.GetAccountInfoAsync(tokenBMint.MintAddress, Solnet.Rpc.Types.Commitment.Processed);
                
                if (tokenAInfo.Result?.Value == null)
                {
                    _logger.LogWarning("⚠️ Token A mint not found with Processed commitment, trying Confirmed...");
                    await Task.Delay(1000);
                    tokenAInfo = await _rpcClient.GetAccountInfoAsync(tokenAMint.MintAddress, Solnet.Rpc.Types.Commitment.Confirmed);
                }
                if (tokenBInfo.Result?.Value == null)
                {
                    _logger.LogWarning("⚠️ Token B mint not found with Processed commitment, trying Confirmed...");
                    await Task.Delay(1000);
                    tokenBInfo = await _rpcClient.GetAccountInfoAsync(tokenBMint.MintAddress, Solnet.Rpc.Types.Commitment.Confirmed);
                }
                
                if (tokenAInfo.Result?.Value == null)
                {
                    _logger.LogWarning("⚠️ Token A mint still not accessible, but transaction was confirmed. Continuing...");
                }
                if (tokenBInfo.Result?.Value == null)
                {
                    _logger.LogWarning("⚠️ Token B mint still not accessible, but transaction was confirmed. Continuing...");
                }
                
                _logger.LogDebug("✅ Token mint validation completed (may have timing issues on localnet)");
                
                _logger.LogDebug("🔍 Validating system state is initialized...");
                // Check if system state PDA exists (like JavaScript validates pause state)
                var systemStatePda = _transactionBuilder.DeriveSystemStatePda();
                var systemStateInfo = await _rpcClient.GetAccountInfoAsync(systemStatePda.ToString());
                
                if (systemStateInfo.Result?.Value == null)
                {
                    _logger.LogWarning("⚠️ System state PDA not found - the contract may not be initialized");
                    _logger.LogDebug($"   System State PDA: {systemStatePda}");
                    _logger.LogDebug("   This might be the reason for 'Program failed to complete'");
                    // Continue anyway to see what other error we get
                }
                else
                {
                    _logger.LogDebug("✅ System state validation passed");
                }
                
                _logger.LogDebug("📤 Submitting pool creation to smart contract...");
                Console.WriteLine("[DEBUG] About to call BuildCreatePoolTransactionAsync...");
                // CRITICAL FIX: Build transaction using existing method but bypass our wrapper
                // The issue is with our SendTransactionAsync wrapper, not the transaction building
                byte[] poolTransactionBytes;
                try
                {
                    poolTransactionBytes = await _transactionBuilder.BuildCreatePoolTransactionAsync(coreKeyPair, poolConfig);
                    Console.WriteLine($"[DEBUG] BuildCreatePoolTransactionAsync returned {poolTransactionBytes.Length} bytes");
                    if (poolTransactionBytes.Length < 100) // Transaction should be much larger
                    {
                        Console.WriteLine($"[DEBUG] WARNING: Transaction bytes too small! First 20 bytes: {Convert.ToHexString(poolTransactionBytes)}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[DEBUG] Exception in BuildCreatePoolTransactionAsync: {ex.Message}");
                    Console.WriteLine($"[DEBUG] Exception type: {ex.GetType().Name}");
                    Console.WriteLine($"[DEBUG] Stack trace: {ex.StackTrace}");
                    throw;
                }
                
                // Preflight simulation to capture on-chain logs
                try
                {
                    _logger.LogDebug("🔎 Simulating pool creation transaction (sigVerify=false, replaceRecentBlockhash=true)...");
                    var sim = await _rpcClient.SimulateTransactionAsync(
                        poolTransactionBytes,
                        sigVerify: false,
                        commitment: Commitment.Processed,
                        replaceRecentBlockhash: true,
                        accountsToReturn: null);

                    if (sim.WasRequestSuccessfullyHandled && sim.Result?.Value?.Logs != null)
                    {
                        _logger.LogDebug("📝 Program logs (simulate):");
                        foreach (var log in sim.Result.Value.Logs)
                        {
                            _logger.LogDebug(log);
                        }
                    }
                    else
                    {
                        _logger.LogWarning("⚠️ Simulation returned no logs. Reason: {Reason}", sim.Reason);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "simulateTransaction failed (continuing with send)");
                }

                // Attempt preflight send first (production path)
                Console.WriteLine($"[DEBUG] About to call _rpcClient.SendTransactionAsync (preflight {(!_config.SkipPreflight).ToString().ToLower()}) with {poolTransactionBytes.Length} bytes...");
                var result = await _rpcClient.SendTransactionAsync(poolTransactionBytes, skipPreflight: _config.SkipPreflight, commitment: Commitment.Processed);
                Console.WriteLine($"[DEBUG] preflight send completed. Success: {result.WasRequestSuccessfullyHandled}");
                if (!result.WasRequestSuccessfullyHandled && !_config.SkipPreflight)
                {
                    _logger.LogWarning("Preflight send failed. Reason: {Reason}", result.Reason);

                    // Additional diagnostic simulation mimicking preflight (sigVerify=true, replaceRecentBlockhash=false)
                    try
                    {
                        _logger.LogDebug("🔎 Preflight-mimic simulate (sigVerify=true, replaceRecentBlockhash=false)...");
                        var preflightSim = await _rpcClient.SimulateTransactionAsync(
                            poolTransactionBytes,
                            sigVerify: true,
                            commitment: Commitment.Processed,
                            replaceRecentBlockhash: false,
                            accountsToReturn: null);
                        if (preflightSim.WasRequestSuccessfullyHandled && preflightSim.Result?.Value?.Logs != null)
                        {
                            _logger.LogDebug("📝 Program logs (preflight-mimic simulate):");
                            foreach (var log in preflightSim.Result.Value.Logs)
                            {
                                _logger.LogDebug(log);
                            }
                        }
                        else
                        {
                            _logger.LogWarning("⚠️ Preflight-mimic simulation returned no logs. Reason: {Reason}", preflightSim.Reason);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Preflight-mimic simulateTransaction failed");
                    }

                    // Fallback: skip preflight for localnet robustness
                    _logger.LogWarning("⚠️ Falling back to skipPreflight=true (localnet fast path)");
                    result = await _rpcClient.SendTransactionAsync(poolTransactionBytes, skipPreflight: true, commitment: Commitment.Processed);
                    Console.WriteLine($"[DEBUG] fallback send completed. Success: {result.WasRequestSuccessfullyHandled}");
                }
                
                if (!result.WasRequestSuccessfullyHandled)
                {
                    throw new InvalidOperationException($"Pool creation transaction failed: {result.Reason}");
                }
                
                poolCreationSignature = result.Result;
                
                var confirmed = await ConfirmTransactionAsync(poolCreationSignature, maxRetries: 5);
                if (!confirmed)
                {
                    _logger.LogWarning("Pool creation transaction may not have confirmed: {Signature}", poolCreationSignature);
                }
                
                _logger.LogDebug("✅ Pool created on smart contract: {Signature}", poolCreationSignature);

                // Fetch and print post-send program logs
                try
                {
                    await Task.Delay(1000);
                    var tx = await _rpcClient.GetTransactionAsync(poolCreationSignature, Commitment.Confirmed);
                    var logs = tx.Result?.Meta?.LogMessages;
                    if (logs != null)
                    {
                        _logger.LogDebug("📝 Program logs (confirmed tx):");
                        foreach (var log in logs)
                        {
                            _logger.LogDebug(log);
                        }
                    }
                    else
                    {
                        _logger.LogWarning("⚠️ No log messages found on confirmed transaction {Signature}", poolCreationSignature);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to fetch confirmed transaction logs for {Signature}", poolCreationSignature);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Smart contract pool creation failed, but tokens are created");
                poolCreationSignature = $"failed_{Guid.NewGuid():N}";
            }
            
            // Step 5: Create real pool data (tokens exist regardless of pool creation success)
            // IMPORTANT: Derive pool PDA using the SAME seeds as the transaction builder
            // - ordered token mints (lexicographic by raw bytes)
            // - basis points derived from ratio and decimals
            var (normalizedTokenA, normalizedTokenB) = GetOrderedTokens(
                poolConfig.TokenAMint,
                poolConfig.TokenBMint);

            var (bpRatioA, bpRatioB) = CalculateBasisPoints(
                poolConfig.TokenAMint,
                poolConfig.TokenBMint,
                poolConfig.TokenADecimals,
                poolConfig.TokenBDecimals,
                poolConfig.RatioWholeNumber,
                poolConfig.RatioDirection);

            var poolStatePda = DerivePoolStatePda(
                normalizedTokenA,
                normalizedTokenB,
                bpRatioA,
                bpRatioB);
            
            var realPool = new RealPoolData
            {
                PoolId = poolStatePda,
                TokenAMint = tokenAMint.MintAddress,
                TokenBMint = tokenBMint.MintAddress,
                TokenADecimals = tokenADecimals,
                TokenBDecimals = tokenBDecimals,
                RatioANumerator = ratioANumerator,
                RatioBDenominator = ratioBDenominator,
                CreationSignature = poolCreationSignature,
                CreatedAt = DateTime.UtcNow,
                LastValidated = DateTime.UtcNow,
                IsValid = true
            };
            
            // Safety check: verify the derived pool PDA exists on-chain before saving
            try
            {
                var existsOnChain = await ValidatePoolExistsOnBlockchainAsync(realPool.PoolId);
                realPool.IsValid = existsOnChain;
                if (!existsOnChain)
                {
                    _logger.LogWarning("Pool PDA {PoolId} not found on-chain at save time; data will be saved but marked invalid.", realPool.PoolId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to validate pool {PoolId} on-chain during save", realPool.PoolId);
            }

            // Step 6: Save pool data
            await _storageService.SaveRealPoolAsync(realPool);

            // Step 7: Populate in-memory cache so JSON-RPC get_pool works for real pools
            try
            {
                var cachedPoolState = new PoolState
                {
                    PoolId = realPool.PoolId,
                    TokenAMint = realPool.TokenAMint,
                    TokenBMint = realPool.TokenBMint,
                    TokenADecimals = realPool.TokenADecimals,
                    TokenBDecimals = realPool.TokenBDecimals,
                    RatioANumerator = realPool.RatioANumerator,
                    RatioBDenominator = realPool.RatioBDenominator,
                    VaultA = DeriveTokenAVaultPda(realPool.PoolId),
                    VaultB = DeriveTokenBVaultPda(realPool.PoolId),
                    LpMintA = DeriveLpTokenAMintPda(realPool.PoolId),
                    LpMintB = DeriveLpTokenBMintPda(realPool.PoolId),
                    MainTreasury = DeriveMainTreasuryPda(),
                    PoolTreasury = DerivePoolTreasuryPda(realPool.PoolId),
                    PoolPaused = false,
                    SwapsPaused = false,
                    CreatedAt = realPool.CreatedAt,
                    CreationSignature = realPool.CreationSignature
                };

                _poolCache[cachedPoolState.PoolId] = cachedPoolState;
                _logger.LogDebug("💾 Cached real pool in memory for fast access: {PoolId}", cachedPoolState.PoolId);
            }
            catch (Exception cacheEx)
            {
                _logger.LogWarning(cacheEx, "Failed to cache real pool state in memory (continuing)");
            }

            _logger.LogDebug("🎯 Real pool created: {PoolId}", realPool.PoolId);
            _logger.LogDebug("   Token A: {TokenA} ({Decimals} decimals)", realPool.TokenAMint, realPool.TokenADecimals);
            _logger.LogDebug("   Token B: {TokenB} ({Decimals} decimals)", realPool.TokenBMint, realPool.TokenBDecimals);
            _logger.LogDebug("   Ratio: {Ratio}", realPool.RatioDisplay);
            
            return realPool;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create real pool");
            throw;
        }
    }
    
    public async Task<bool> ValidateRealPoolAsync(RealPoolData pool)
    {
        try
        {
            // Check if tokens still exist and core wallet is still mint authority
            var coreWallet = await GetOrCreateCoreWalletAsync();
            
            // Validate Token A mint
            var tokenAInfo = await _rpcClient.GetAccountInfoAsync(pool.TokenAMint);
            if (!tokenAInfo.WasSuccessful || tokenAInfo.Result?.Value == null)
            {
                _logger.LogWarning("Token A mint not found: {TokenA}", pool.TokenAMint);
                return false;
            }
            
            // Validate Token B mint
            var tokenBInfo = await _rpcClient.GetAccountInfoAsync(pool.TokenBMint);
            if (!tokenBInfo.WasSuccessful || tokenBInfo.Result?.Value == null)
            {
                _logger.LogWarning("Token B mint not found: {TokenB}", pool.TokenBMint);
                return false;
            }
            
            _logger.LogDebug("Real pool validation passed: {PoolId}", pool.PoolId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to validate real pool {PoolId}", pool.PoolId);
            return false;
        }
    }
    
    public async Task<List<RealPoolData>> GetRealPoolsAsync()
    {
        try
        {
            var pools = await _storageService.LoadRealPoolsAsync();
            _logger.LogDebug("Retrieved {Count} real pools from storage", pools.Count);
            return pools;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get real pools");
            return new List<RealPoolData>();
        }
    }

        public async Task<PoolState> GetPoolStateAsync(string poolId)
        {
            // First check local cache
            if (_poolCache.TryGetValue(poolId, out var poolState))
            {
                _logger.LogDebug("Retrieved pool {PoolId} from cache", poolId);
                return poolState;
            }
            
            // TODO: In future phases, query blockchain for pool state
            // For now, only support cached pools (created by this service instance)
            throw new KeyNotFoundException($"Pool {poolId} not found in cache. Only pools created by this service instance are supported.");
        }

        public async Task<List<PoolState>> GetAllPoolsAsync()
        {
            // Start with any pools already cached
            var poolIdToState = _poolCache.Values.ToDictionary(p => p.PoolId, p => p);

            try
            {
                // Load active pool IDs and real pool metadata
                var activePoolIds = await _storageService.LoadActivePoolIdsAsync();
                var realPools = await _storageService.LoadRealPoolsAsync();

                foreach (var poolId in activePoolIds)
                {
                    if (poolIdToState.ContainsKey(poolId)) continue;

                    var rp = realPools.FirstOrDefault(p => p.PoolId == poolId);
                    if (rp != null)
                    {
                        // Hydrate a minimal PoolState from stored real pool data
                        var hydrated = new PoolState
                        {
                            PoolId = rp.PoolId,
                            TokenAMint = rp.TokenAMint,
                            TokenBMint = rp.TokenBMint,
                            TokenADecimals = rp.TokenADecimals,
                            TokenBDecimals = rp.TokenBDecimals,
                            RatioANumerator = rp.RatioANumerator,
                            RatioBDenominator = rp.RatioBDenominator,
                            VaultA = DeriveTokenAVaultPda(rp.PoolId),
                            VaultB = DeriveTokenBVaultPda(rp.PoolId),
                            LpMintA = DeriveLpTokenAMintPda(rp.PoolId),
                            LpMintB = DeriveLpTokenBMintPda(rp.PoolId),
                            MainTreasury = DeriveMainTreasuryPda(),
                            PoolTreasury = DerivePoolTreasuryPda(rp.PoolId),
                            PoolPaused = false,
                            SwapsPaused = false,
                            CreatedAt = rp.CreatedAt,
                            CreationSignature = rp.CreationSignature
                        };

                        poolIdToState[hydrated.PoolId] = hydrated;

                        // Also cache for future fast access
                        _poolCache[hydrated.PoolId] = hydrated;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to hydrate all pools from storage; returning cached pools only");
            }

            return poolIdToState.Values.ToList();
        }

        public async Task<List<string>> GetActivePoolsAsync()
        {
            return await _storageService.LoadActivePoolIdsAsync();
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
                    wallet, pool, tokenType, amountInBasisPoints);
                
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
                    wallet, pool, tokenType, lpTokenAmountToBurn);
                
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
                    wallet, pool, direction, inputAmountBasisPoints, minimumOutputBasisPoints);
                
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
                _logger.LogDebug("Requesting airdrop of {Lamports} lamports ({SOL} SOL) to {Address}", 
                    lamports, lamports / 1_000_000_000.0, walletAddress);
                
                var result = await _rpcClient.RequestAirdropAsync(walletAddress, lamports);
                if (result.WasRequestSuccessfullyHandled)
                {
                    _logger.LogDebug("✅ Airdrop request successful: {Lamports} lamports to {Address}, signature: {Signature}", 
                        lamports, walletAddress, result.Result);
                    return result.Result;
                }
                
                var errorMsg = $"Airdrop request failed: {result.Reason} (HTTP {result.HttpStatusCode})";
                _logger.LogWarning("❌ {ErrorMsg}", errorMsg);
                throw new InvalidOperationException(errorMsg);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "💥 Failed to request airdrop of {Lamports} lamports to {Address}: {Message}", 
                    lamports, walletAddress, ex.Message);
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
                
                _logger.LogDebug("Transferred {Amount} tokens to {Address}", amount, toWalletAddress);
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
                
                _logger.LogDebug("Minted {Amount} tokens to {Address}", amount, recipientAddress);
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
            // For backward compatibility with existing tests, support both formats
            if (poolId.Length == 44) // Base58 address length - assume it's already a PDA
            {
                return poolId;
            }
            
            // Legacy GUID-based poolId - derive from poolId string
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
            
            throw new InvalidOperationException($"Failed to derive pool state PDA for poolId {poolId}");
        }

        public string DeriveTokenAVaultPda(string poolStatePda)
        {
            var seeds = new List<byte[]>
            {
                Encoding.UTF8.GetBytes("token_a_vault"),
                new PublicKey(poolStatePda).KeyBytes
            };
            if (PublicKey.TryFindProgramAddress(
                seeds,
                new PublicKey(_config.ProgramId),
                out var pda,
                out _))
            {
                return pda.ToString();
            }
            throw new InvalidOperationException($"Failed to derive token A vault PDA for pool {poolStatePda}");
        }

        public string DeriveTokenBVaultPda(string poolStatePda)
        {
            var seeds = new List<byte[]>
            {
                Encoding.UTF8.GetBytes("token_b_vault"),
                new PublicKey(poolStatePda).KeyBytes
            };
            if (PublicKey.TryFindProgramAddress(
                seeds,
                new PublicKey(_config.ProgramId),
                out var pda,
                out _))
            {
                return pda.ToString();
            }
            throw new InvalidOperationException($"Failed to derive token B vault PDA for pool {poolStatePda}");
        }

        public string DeriveLpTokenAMintPda(string poolStatePda)
        {
            var seeds = new List<byte[]>
            {
                Encoding.UTF8.GetBytes("lp_token_a_mint"),
                new PublicKey(poolStatePda).KeyBytes
            };
            if (PublicKey.TryFindProgramAddress(
                seeds,
                new PublicKey(_config.ProgramId),
                out var pda,
                out _))
            {
                return pda.ToString();
            }
            throw new InvalidOperationException($"Failed to derive LP token A mint PDA for pool {poolStatePda}");
        }

        public string DeriveLpTokenBMintPda(string poolStatePda)
        {
            var seeds = new List<byte[]>
            {
                Encoding.UTF8.GetBytes("lp_token_b_mint"),
                new PublicKey(poolStatePda).KeyBytes
            };
            if (PublicKey.TryFindProgramAddress(
                seeds,
                new PublicKey(_config.ProgramId),
                out var pda,
                out _))
            {
                return pda.ToString();
            }
            throw new InvalidOperationException($"Failed to derive LP token B mint PDA for pool {poolStatePda}");
        }

        public string DerivePoolTreasuryPda(string poolStatePda)
        {
            var seeds = new List<byte[]>
            {
                Encoding.UTF8.GetBytes("pool_treasury"),
                new PublicKey(poolStatePda).KeyBytes
            };
            
            if (PublicKey.TryFindProgramAddress(
                seeds, 
                new PublicKey(_config.ProgramId), 
                out var pda, 
                out _))
            {
                return pda.ToString();
            }
            
            throw new InvalidOperationException($"Failed to derive pool treasury PDA for pool {poolStatePda}");
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
        
        // Pool creation helper methods
        private async Task<(string tokenAMint, string tokenBMint, int tokenADecimals, int tokenBDecimals)> CreateTokenMintsAsync(
            PoolCreationParams parameters)
        {
            try
            {
                _logger.LogDebug("Creating token mints for new pool");
                
                // Generate decimals if not specified
                var tokenADecimals = parameters.TokenADecimals ?? Random.Shared.Next(0, 10);
                var tokenBDecimals = parameters.TokenBDecimals ?? Random.Shared.Next(0, 10);
                
                // Create token mints using new wallets as mint authorities
                var tokenAWallet = GenerateWallet();
                var tokenBWallet = GenerateWallet();
                
                // Fund mint authorities
                await RequestAirdropAsync(tokenAWallet.Account.PublicKey.ToString(), 2 * 1_000_000_000UL); // 2 SOL
                await RequestAirdropAsync(tokenBWallet.Account.PublicKey.ToString(), 2 * 1_000_000_000UL); // 2 SOL
                
                // Wait for funding
                await Task.Delay(1000);
                
                // Create token mints (this would normally involve creating mint accounts on blockchain)
                // For now, we'll use the wallet public keys as mint addresses
                var tokenAMint = tokenAWallet.Account.PublicKey.ToString();
                var tokenBMint = tokenBWallet.Account.PublicKey.ToString();
                
                // Ensure proper token ordering (smaller pubkey is Token A)
                if (string.Compare(tokenAMint, tokenBMint) > 0)
                {
                    (tokenAMint, tokenBMint) = (tokenBMint, tokenAMint);
                    (tokenADecimals, tokenBDecimals) = (tokenBDecimals, tokenADecimals);
                    (tokenAWallet, tokenBWallet) = (tokenBWallet, tokenAWallet);
                }
                
                // Store mint authorities for later token minting
                _mintAuthorities[tokenAMint] = tokenAWallet;
                _mintAuthorities[tokenBMint] = tokenBWallet;
                
                _logger.LogDebug(
                    "Created token mints: A={TokenA} ({TokenADecimals} decimals), B={TokenB} ({TokenBDecimals} decimals)",
                    tokenAMint, tokenADecimals, tokenBMint, tokenBDecimals);
                
                return (tokenAMint, tokenBMint, tokenADecimals, tokenBDecimals);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create token mints");
                throw;
            }
        }
        
        private PoolConfig CreateNormalizedPoolConfig(
            string tokenAMint, 
            string tokenBMint, 
            int tokenADecimals, 
            int tokenBDecimals, 
            PoolCreationParams parameters)
        {
            try
            {
                // Generate ratio if not specified
                var ratioWholeNumber = parameters.RatioWholeNumber ?? (ulong)Random.Shared.Next(1, 1000);
                var ratioDirection = parameters.RatioDirection ?? "a_to_b";
                
                // Create normalized pool configuration following design documents
                var poolConfig = new PoolConfig
                {
                    TokenAMint = tokenAMint,
                    TokenBMint = tokenBMint,
                    TokenADecimals = tokenADecimals,
                    TokenBDecimals = tokenBDecimals,
                    RatioWholeNumber = ratioWholeNumber,
                    RatioDirection = ratioDirection
                };
                
                // Apply "anchored to 1" rule from design documents
                if (ratioDirection == "a_to_b")
                {
                    // Token A is anchored to 1 (10^decimals)
                    poolConfig.RatioANumerator = (ulong)Math.Pow(10, tokenADecimals);
                    poolConfig.RatioBDenominator = ratioWholeNumber * (ulong)Math.Pow(10, tokenBDecimals);
                }
                else // b_to_a
                {
                    // Token B is anchored to 1 (10^decimals)
                    poolConfig.RatioBDenominator = (ulong)Math.Pow(10, tokenBDecimals);
                    poolConfig.RatioANumerator = ratioWholeNumber * (ulong)Math.Pow(10, tokenADecimals);
                }
                
                // Validate the pool ratio (safety mechanism from design docs)
                ValidatePoolRatio(poolConfig, tokenADecimals, tokenBDecimals);
                
                _logger.LogDebug(
                    "Normalized pool config: A={TokenA}, B={TokenB}, Ratio={RatioA}:{RatioB} ({Direction})",
                    tokenAMint, tokenBMint, poolConfig.RatioANumerator, poolConfig.RatioBDenominator, ratioDirection);
                
                return poolConfig;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create normalized pool config");
                throw;
            }
        }
        
        private void ValidatePoolRatio(PoolConfig config, int tokenADecimals, int tokenBDecimals)
        {
            // Verify one side equals exactly 10^decimals (anchored to 1 rule)
            var expectedA = (ulong)Math.Pow(10, tokenADecimals);
            var expectedB = (ulong)Math.Pow(10, tokenBDecimals);
            
            if (config.RatioANumerator != expectedA && config.RatioBDenominator != expectedB)
            {
                throw new InvalidOperationException(
                    $"Invalid pool ratio: neither side is anchored to 1. " +
                    $"Expected A={expectedA} or B={expectedB}, " +
                    $"got A={config.RatioANumerator}, B={config.RatioBDenominator}");
            }
            
            // Calculate and log the exchange rate for verification 
            // Convert basis points back to display units for verification
            var tokenADisplayAmount = config.RatioANumerator / Math.Pow(10, tokenADecimals);
            var tokenBDisplayAmount = config.RatioBDenominator / Math.Pow(10, tokenBDecimals);
            var rate = tokenBDisplayAmount / tokenADisplayAmount;
            _logger.LogDebug("Pool ratio validated: 1 Token A = {Rate:F6} Token B", rate);
            _logger.LogDebug("   Basis points: {RatioA} : {RatioB}", config.RatioANumerator, config.RatioBDenominator);
            _logger.LogDebug("   Display units: {DisplayA} : {DisplayB}", tokenADisplayAmount, tokenBDisplayAmount);
            
            // Warn about potentially problematic ratios
            if (rate > 1_000_000 || rate < 0.000001)
            {
                _logger.LogWarning(
                    "Pool ratio {Rate:F6} may be extreme. This could indicate a configuration error.", rate);
            }
        }
        
        // PDA derivation using the SAME seeds as TransactionBuilderService
        private string DerivePoolStatePda(string orderedTokenAMint, string orderedTokenBMint, ulong ratioANumerator, ulong ratioBDenominator)
        {
            var seeds = new List<byte[]>
            {
                Encoding.UTF8.GetBytes("pool_state"),
                new PublicKey(orderedTokenAMint).KeyBytes,
                new PublicKey(orderedTokenBMint).KeyBytes,
                BitConverter.GetBytes(ratioANumerator),
                BitConverter.GetBytes(ratioBDenominator)
            };
            if (PublicKey.TryFindProgramAddress(
                seeds,
                new PublicKey(_config.ProgramId),
                out var pda,
                out _))
            {
                return pda.ToString();
            }
            throw new InvalidOperationException($"Failed to derive pool state PDA for tokens {orderedTokenAMint}/{orderedTokenBMint}");
        }

        // Helper: normalize token order like the dashboard / TransactionBuilderService
        private (string tokenA, string tokenB) GetOrderedTokens(string tokenAMint, string tokenBMint)
        {
            var mintA = new PublicKey(tokenAMint);
            var mintB = new PublicKey(tokenBMint);
            var bytesA = mintA.KeyBytes;
            var bytesB = mintB.KeyBytes;
            bool aLessThanB = false;
            for (int i = 0; i < 32; i++)
            {
                if (bytesA[i] < bytesB[i]) { aLessThanB = true; break; }
                if (bytesA[i] > bytesB[i]) { aLessThanB = false; break; }
            }
            return aLessThanB ? (tokenAMint, tokenBMint) : (tokenBMint, tokenAMint);
        }

        // Helper: replicate basis-points calculation to match TransactionBuilderService
        private (ulong ratioANumerator, ulong ratioBDenominator) CalculateBasisPoints(
            string tokenAMint, string tokenBMint,
            int tokenADecimals, int tokenBDecimals,
            ulong ratioWholeNumber, string ratioDirection)
        {
            var (orderedTokenA, _) = GetOrderedTokens(tokenAMint, tokenBMint);
            bool needsInversion = (orderedTokenA != tokenAMint);
            if (ratioDirection == "b_to_a")
            {
                needsInversion = !needsInversion;
            }
            var orderedDecimalsA = needsInversion ? tokenBDecimals : tokenADecimals;
            var orderedDecimalsB = needsInversion ? tokenADecimals : tokenBDecimals;
            ulong ratioANumerator = (ulong)(1 * Math.Pow(10, orderedDecimalsA));
            ulong ratioBDenominator = (ulong)(ratioWholeNumber * Math.Pow(10, orderedDecimalsB));
            return (ratioANumerator, ratioBDenominator);
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
        
        /// <summary>
        /// Validates that a pool exists on the blockchain by checking its PDA account
        /// </summary>
        public async Task<bool> ValidatePoolExistsOnBlockchainAsync(string poolId)
        {
            try
            {
                var poolPda = new PublicKey(poolId);
                var accountInfo = await _rpcClient.GetAccountInfoAsync(poolPda);
                
                if (accountInfo.Result?.Value == null)
                {
                    _logger.LogDebug("Pool {PoolId} not found via direct account lookup, enumerating program accounts...", poolId);
                    var allPools = await FetchAllBlockchainPoolIdsAsync();
                    var existsInList = allPools.Contains(poolId);
                    if (existsInList)
                    {
                        _logger.LogDebug("Pool {PoolId} found in program-owned accounts list", poolId);
                        return true;
                    }
                    _logger.LogDebug("Pool {PoolId} not present in program-owned accounts list", poolId);
                    return false;
                }
                
                // Check if account has data (pool state should be initialized)
                if (accountInfo.Result.Value.Data == null || accountInfo.Result.Value.Data.Count == 0)
                {
                    _logger.LogDebug("Pool {PoolId} exists but has no data; cross-checking program-owned accounts list", poolId);
                    var allPools = await FetchAllBlockchainPoolIdsAsync();
                    var existsInList = allPools.Contains(poolId);
                    if (existsInList)
                    {
                        _logger.LogDebug("Pool {PoolId} confirmed via program-owned accounts list", poolId);
                        return true;
                    }
                    return false;
                }
                
                _logger.LogDebug("Pool {PoolId} validated on blockchain", poolId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to validate pool {PoolId} on blockchain", poolId);
                return false;
            }
        }

        // Enumerate all accounts owned by the configured program and return their public keys
        private async Task<List<string>> FetchAllBlockchainPoolIdsAsync()
        {
            try
            {
                var programId = _config.ProgramId;
                if (string.IsNullOrWhiteSpace(programId))
                {
                    _logger.LogWarning("ProgramId is not configured; cannot enumerate program accounts");
                    return new List<string>();
                }

                var result = await _rpcClient.GetProgramAccountsAsync(programId, Commitment.Processed, null, null);
                if (!result.WasRequestSuccessfullyHandled || result.Result == null)
                {
                    _logger.LogWarning("GetProgramAccountsAsync failed or returned null. Reason: {Reason}", result.Reason);
                    return new List<string>();
                }

                var ids = result.Result
                    .Select(kv => kv.PublicKey)
                    .Where(pk => !string.IsNullOrWhiteSpace(pk))
                    .Distinct()
                    .ToList();

                _logger.LogDebug("Enumerated {Count} program-owned accounts for ProgramId {ProgramId}", ids.Count, programId);
                return ids;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch program-owned accounts");
                return new List<string>();
            }
        }

        // Save a snapshot of all program-owned accounts to data/allblockchainpools.json for diagnostics
        private async Task SaveAllBlockchainPoolsSnapshotAsync()
        {
            try
            {
                var ids = await FetchAllBlockchainPoolIdsAsync();
                var snapshot = new
                {
                    fetchedAt = DateTime.UtcNow,
                    programId = _config.ProgramId,
                    totalCount = ids.Count,
                    pools = ids
                };

                var dataDir = Path.Combine(Environment.CurrentDirectory, "data");
                Directory.CreateDirectory(dataDir);
                var filePath = Path.Combine(dataDir, "allblockchainpools.json");
                var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(filePath, json);
                _logger.LogDebug("Saved blockchain pools snapshot to {Path}", filePath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to save blockchain pools snapshot");
            }
        }
        
        /// <summary>
        /// Initializes the treasury system (required before any pool operations)
        /// </summary>
        public async Task InitializeTreasurySystemAsync()
        {
            try
            {
                _logger.LogDebug("🏦 Initializing treasury system...");
                
                // Check if treasury system is already initialized
                var systemStatePda = _transactionBuilder.DeriveSystemStatePda();
                var systemStateAccount = await _rpcClient.GetAccountInfoAsync(systemStatePda);
                
                if (systemStateAccount.Result?.Value != null && systemStateAccount.Result.Value.Data?.Count > 0)
                {
                    _logger.LogDebug("✅ Treasury system already initialized");
                    return;
                }
                
                _logger.LogDebug("🔧 Treasury system not found - initializing...");
                
                // Load core wallet to use as system authority
                var coreWallet = await _storageService.LoadCoreWalletAsync();
                if (coreWallet == null)
                {
                    throw new InvalidOperationException("Core wallet not found. Cannot initialize treasury system.");
                }
                
                // Ensure core wallet has enough SOL for initialization
                await EnsureCoreWalletHasSufficientSolAsync(coreWallet);
                
                // Decode the private key to create Account
                var privateKeyBytes = Convert.FromBase64String(coreWallet.PrivateKey);
                var coreKeyPair = RestoreWallet(privateKeyBytes);
                
                // Build InitializeProgram transaction
                var initTransaction = await _transactionBuilder.BuildInitializeProgramTransactionAsync(coreKeyPair.Account);
                
                // Send transaction
                var response = await _rpcClient.SendTransactionAsync(initTransaction);
                if (!response.WasSuccessful)
                {
                    throw new InvalidOperationException($"Treasury initialization failed: {response.Reason}");
                }
                
                _logger.LogDebug("✅ Treasury system initialized successfully: {Signature}", response.Result);
                
                // Wait for confirmation
                await Task.Delay(3000);
                
                // Verify initialization
                var verifyAccount = await _rpcClient.GetAccountInfoAsync(systemStatePda);
                if (verifyAccount.Result?.Value != null && verifyAccount.Result.Value.Data?.Count > 0)
                {
                    _logger.LogDebug("🎉 Treasury system initialization verified");
                }
                else
                {
                    _logger.LogWarning("⚠️ Treasury system initialization could not be verified");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize treasury system");
                throw;
            }
        }
        
        /// <summary>
        /// Validates all saved pools on startup and removes invalid ones
        /// </summary>
        public async Task ValidateAndCleanupSavedPoolsAsync()
        {
            try
            {
                _logger.LogDebug("🔍 Validating saved pools on startup...");
                
                var savedPools = await _storageService.LoadRealPoolsAsync();
                if (!savedPools.Any())
                {
                    _logger.LogDebug("No saved pools found to validate");
                    return;
                }
                
                var validPools = new List<RealPoolData>();
                var invalidPoolIds = new List<string>();
                
                foreach (var pool in savedPools)
                {
                    var exists = await ValidatePoolExistsOnBlockchainAsync(pool.PoolId);
                    if (exists)
                    {
                        validPools.Add(pool);
                        _logger.LogDebug("✅ Pool {PoolId} validated", pool.PoolId);
                    }
                    else
                    {
                        invalidPoolIds.Add(pool.PoolId);
                        _logger.LogWarning("❌ Pool {PoolId} not found on blockchain - will be removed", pool.PoolId);
                    }
                }
                
                // Remove invalid pools
                foreach (var invalidPoolId in invalidPoolIds)
                {
                    await _storageService.DeleteRealPoolAsync(invalidPoolId);
                }
                
                _logger.LogDebug("🧹 Pool validation completed: {Valid} valid, {Invalid} removed", 
                    validPools.Count, invalidPoolIds.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to validate saved pools");
            }
        }

        public async Task CheckCoreWalletBalanceAsync(CoreWalletConfig coreWallet)
        {
            try
            {
                _logger.LogDebug("💰 Checking core wallet SOL balance for pool creation...");
                var currentBalance = await GetSolBalanceAsync(coreWallet.PublicKey);
                _logger.LogDebug("Current core wallet balance: {Balance} SOL", currentBalance / 1_000_000_000.0);

                if (currentBalance < 1_200_000_000UL) // Minimum 1.2 SOL for registration fee
                {
                    var errorMsg = $"Core wallet balance is insufficient for pool creation. " +
                                  $"Current: {currentBalance / 1_000_000_000.0:F2} SOL, " +
                                  $"Required: {1_200_000_000UL / 1_000_000_000.0:F2} SOL minimum.";
                    _logger.LogError(errorMsg);
                    throw new InvalidOperationException(errorMsg);
                }
                _logger.LogDebug("✅ Core wallet has sufficient SOL balance for pool creation.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to check core wallet balance");
                throw;
            }
        }
    }
}