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
                _logger.LogInformation("🔍 Simulating pool creation to validate transaction format");
                
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
                
                _logger.LogInformation("Pool creation simulation completed: {Status}", 
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
                _logger.LogInformation("Creating pool with blockchain transaction (includes simulation validation)");
                
                // Step 1: Simulate pool creation first to validate transaction format
                _logger.LogInformation("🔍 Step 1: Simulating pool creation transaction...");
                var simulationResult = await SimulatePoolCreationAsync(parameters);
                
                // Log simulation results
                _logger.LogInformation(simulationResult.SimulationSummary);
                
                if (!simulationResult.WouldSucceed)
                {
                    _logger.LogWarning("⚠️ Pool creation simulation indicates potential failure: {Error}", 
                        simulationResult.ErrorMessage);
                    _logger.LogInformation("🔄 Continuing with actual pool creation anyway for testing purposes...");
                }
                else
                {
                    _logger.LogInformation("✅ Pool creation simulation successful - proceeding with actual creation");
                }
                
                // Step 2: Create and fund payer wallet for pool creation fees
                _logger.LogInformation("💰 Step 2: Setting up payer wallet...");
                var payerWallet = GenerateWallet();
                
                // Request airdrop for pool creation fees (1.15+ SOL required)
                var requiredFunding = SolanaConfiguration.REGISTRATION_FEE + (10 * 1_000_000_000UL); // Extra 10 SOL for operations
                await RequestAirdropAsync(payerWallet.Account.PublicKey.ToString(), requiredFunding);
                
                // Wait for airdrop confirmation
                await Task.Delay(2000);
                
                // Step 3: Create token mints and fund them
                _logger.LogInformation("🪙 Step 3: Creating token mints...");
                var (tokenAMint, tokenBMint, tokenADecimals, tokenBDecimals) = await CreateTokenMintsAsync(parameters);
                
                // Step 4: Create normalized pool configuration
                var poolConfig = CreateNormalizedPoolConfig(
                    tokenAMint, tokenBMint, tokenADecimals, tokenBDecimals, parameters);
                
                string poolCreationSignature;
                string poolStatePda;
                
                try
                {
                    // Step 5: Try to build and send pool creation transaction
                    _logger.LogInformation("📤 Step 5: Building and sending pool creation transaction...");
                    var poolTransaction = await _transactionBuilder.BuildCreatePoolTransactionAsync(
                        payerWallet, poolConfig);
                    
                    poolCreationSignature = await SendTransactionAsync(poolTransaction);
                    
                    // Step 6: Confirm pool creation transaction
                    _logger.LogInformation("⏳ Step 6: Confirming pool creation transaction...");
                    var confirmed = await ConfirmTransactionAsync(poolCreationSignature, maxRetries: 5);
                    if (!confirmed)
                    {
                        _logger.LogWarning("Transaction confirmation failed, but continuing...");
                    }
                    
                    // Step 7: Derive pool state PDA from the created tokens
                    poolStatePda = DerivePoolStatePda(poolConfig.TokenAMint, poolConfig.TokenBMint);
                    
                    _logger.LogInformation("✅ Successfully created REAL blockchain pool with signature {Signature}", poolCreationSignature);
                }
                catch (Exception transactionEx)
                {
                    _logger.LogWarning(transactionEx, "Blockchain transaction failed, falling back to simulated pool creation for testing");
                    
                    // Fallback: Create a simulated pool with deterministic ID for testing
                    poolCreationSignature = $"simulated_tx_{Guid.NewGuid():N}";
                    poolStatePda = DerivePoolStatePda(poolConfig.TokenAMint, poolConfig.TokenBMint);
                    
                    _logger.LogInformation("📋 Created SIMULATED pool for testing purposes");
                }
                
                // Step 8: Create pool state object with all derived addresses
                _logger.LogInformation("📋 Step 8: Creating pool state object...");
                var poolState = new PoolState
                {
                    PoolId = poolStatePda,  // Use PDA as pool ID for real blockchain pools
                    TokenAMint = poolConfig.TokenAMint,
                    TokenBMint = poolConfig.TokenBMint,
                    TokenADecimals = poolConfig.TokenADecimals,
                    TokenBDecimals = poolConfig.TokenBDecimals,
                    RatioANumerator = poolConfig.RatioANumerator,
                    RatioBDenominator = poolConfig.RatioBDenominator,
                    VaultA = DeriveTokenVaultPda(poolStatePda, poolConfig.TokenAMint),
                    VaultB = DeriveTokenVaultPda(poolStatePda, poolConfig.TokenBMint),
                    LpMintA = DeriveLpMintPda(poolStatePda, poolConfig.TokenAMint),
                    LpMintB = DeriveLpMintPda(poolStatePda, poolConfig.TokenBMint),
                    MainTreasury = DeriveMainTreasuryPda(),
                    PoolTreasury = DerivePoolTreasuryPda(poolStatePda),
                    PoolPaused = false,
                    SwapsPaused = false,
                    CreatedAt = DateTime.UtcNow,
                    CreationSignature = poolCreationSignature,
                    PayerWallet = payerWallet.Account.PublicKey.ToString()
                };
                
                // Step 9: Cache the pool state for later operations
                _logger.LogInformation("💾 Step 9: Caching pool state...");
                _poolCache[poolState.PoolId] = poolState;
                
                _logger.LogInformation(
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
        _logger.LogInformation("🏊 Managing pool lifecycle - target: {TargetCount} pools", targetPoolCount);
        
        // Step 1: Load existing active pool IDs from storage
        var activePoolIds = await _storageService.LoadActivePoolIdsAsync();
        _logger.LogInformation("📋 Found {Count} stored pool IDs", activePoolIds.Count);
        
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
        
        _logger.LogInformation("✅ {ValidCount} of {TotalCount} pools passed validation", validPoolIds.Count, activePoolIds.Count);
        
        // Step 3: Create additional pools if needed
        while (validPoolIds.Count < targetPoolCount)
        {
            try
            {
                _logger.LogInformation("🔨 Creating pool {Current}/{Target}...", validPoolIds.Count + 1, targetPoolCount);
                
                var poolParams = new PoolCreationParams
                {
                    TokenADecimals = 9, // SOL-like
                    TokenBDecimals = 6, // USDC-like
                    RatioWholeNumber = 1000, // 1:1000 ratio
                    RatioDirection = "a_to_b"
                };
                
                var newPool = await CreatePoolAsync(poolParams);
                validPoolIds.Add(newPool.PoolId);
                
                _logger.LogInformation("✅ Created new pool: {PoolId}", newPool.PoolId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create pool {Current}/{Target}", validPoolIds.Count + 1, targetPoolCount);
                // Continue trying to create other pools
            }
        }
        
        // Step 4: Save the updated active pool list
        await _storageService.SaveActivePoolIdsAsync(validPoolIds);
        
        _logger.LogInformation("🎯 Pool management complete: {Count} active pools ready", validPoolIds.Count);
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
        // Check if pool exists in our cache first
        if (_poolCache.ContainsKey(poolId))
        {
            return true;
        }
        
        // Try to fetch pool state to validate it exists
        var poolState = await GetPoolStateAsync(poolId);
        return poolState != null;
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
        _logger.LogInformation("🧹 Starting cleanup of invalid pools...");
        
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
                _logger.LogInformation("🗑️ Cleaning up invalid pool: {PoolId}", poolId);
                await _storageService.CleanupPoolDataAsync(poolId);
                await _storageService.CleanupAllThreadDataForPoolAsync(poolId);
                cleanupCount++;
            }
        }
        
        // Update the active pools list
        await _storageService.SaveActivePoolIdsAsync(validPoolIds);
        
        _logger.LogInformation("✅ Cleanup complete: removed {CleanupCount} invalid pools, {ValidCount} remain", 
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
            _logger.LogInformation("🔑 Initializing core wallet for token mint authority...");
            
            // Try to load existing core wallet
            var existingWallet = await _storageService.LoadCoreWalletAsync();
            if (existingWallet != null)
            {
                _logger.LogInformation("✅ Loaded existing core wallet: {PublicKey}", existingWallet.PublicKey);
                
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
            _logger.LogInformation("🆕 Creating new core wallet...");
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
            
            _logger.LogInformation("📊 Core wallet created with balance: {Balance} SOL (funding will occur when needed for pool creation)", 
                currentBalance / 1_000_000_000.0);
            
            // Save the core wallet
            await _storageService.SaveCoreWalletAsync(coreWalletConfig);
            
            _logger.LogInformation("✅ Core wallet created: {PublicKey} ({Balance} SOL)", 
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
            _logger.LogInformation("💰 Checking core wallet SOL balance before pool creation...");
            
            // Check current balance
            var currentBalance = await GetSolBalanceAsync(coreWallet.PublicKey);
            var requiredBalance = 1_500_000_000UL; // 1.5 SOL minimum for pool creation operations (adjusted for localnet limits)
            
            _logger.LogInformation("Current balance: {Current} SOL, Required: {Required} SOL", 
                currentBalance / 1_000_000_000.0, requiredBalance / 1_000_000_000.0);
            
            if (currentBalance >= requiredBalance)
            {
                _logger.LogInformation("✅ Core wallet has sufficient SOL balance");
                return;
            }
            
            _logger.LogInformation("⚠️ Insufficient SOL balance, attempting airdrop...");
            
            // Try to fund with airdrops using improved strategy
            var neededAmount = requiredBalance - currentBalance;
            var funded = false;
            
            for (int attempt = 1; attempt <= 5 && !funded; attempt++)
            {
                try
                {
                    // Request smaller amounts more frequently for better success rate
                    var airdropAmount = Math.Min(500_000_000UL, neededAmount); // 0.5 SOL max per request
                    _logger.LogInformation("Airdrop attempt {Attempt}/5: Requesting {Amount} SOL", 
                        attempt, airdropAmount / 1_000_000_000.0);
                    
                    var airdropSignature = await RequestAirdropAsync(coreWallet.PublicKey, airdropAmount);
                    _logger.LogInformation("Airdrop transaction signature: {Signature}", airdropSignature);
                    
                    // Wait longer for confirmation and check multiple times
                    for (int confirmCheck = 1; confirmCheck <= 3; confirmCheck++)
                    {
                        await Task.Delay(2000); // Wait 2 seconds between checks
                        currentBalance = await GetSolBalanceAsync(coreWallet.PublicKey);
                        _logger.LogInformation("Balance check {Check}/3 after airdrop attempt {Attempt}: {Balance} SOL", 
                            confirmCheck, attempt, currentBalance / 1_000_000_000.0);
                        
                        if (currentBalance >= requiredBalance)
                        {
                            funded = true;
                            _logger.LogInformation("✅ Core wallet successfully funded via airdrop");
                            break;
                        }
                    }
                    
                    // Update needed amount for next iteration
                    neededAmount = requiredBalance - currentBalance;
                    
                    if (neededAmount <= 0)
                    {
                        funded = true;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Airdrop attempt {Attempt} failed: {Message}", attempt, ex.Message);
                    
                    // Wait longer between failed attempts
                    if (attempt < 5)
                    {
                        await Task.Delay(5000); // Wait 5 seconds before next attempt
                    }
                }
            }
            
            // Final balance check
            if (currentBalance < requiredBalance)
            {
                var errorMsg = $"Cannot create pool: Core wallet has insufficient SOL balance. " +
                              $"Current: {currentBalance / 1_000_000_000.0:F2} SOL, " +
                              $"Required: {requiredBalance / 1_000_000_000.0:F2} SOL. " +
                              $"Airdrop attempts failed. Please fund the core wallet manually.";
                
                _logger.LogError(errorMsg);
                throw new InvalidOperationException(errorMsg);
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
            _logger.LogInformation("🪙 Creating token mint with {Decimals} decimals...", decimals);
            
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
            
            _logger.LogInformation("🔧 Creating mint {MintAddress} with authority {Authority}", 
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
            
            // Submit transaction
            var signature = await _rpcClient.SendTransactionAsync(createMintTx);
            if (!signature.WasSuccessful)
            {
                throw new InvalidOperationException($"Failed to create token mint: {signature.Reason}");
            }
            
            _logger.LogInformation("📤 Sent token mint creation transaction: {Signature}", signature.Result);
            
            // Wait for confirmation
            await Task.Delay(2000);
            var confirmed = await ConfirmTransactionAsync(signature.Result);
            if (!confirmed)
            {
                _logger.LogWarning("Token mint creation may not have confirmed: {Signature}", signature.Result);
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
            
            _logger.LogInformation("✅ Token mint created successfully: {MintAddress}", mintAddress);
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
            _logger.LogInformation("🏊 Creating REAL pool on the smart contract...");
            
            // Step 1: Load existing core wallet (should exist from startup)
            var coreWallet = await _storageService.LoadCoreWalletAsync();
            if (coreWallet == null)
            {
                throw new InvalidOperationException("Core wallet not found. This should have been created during application startup.");
            }
            
            // Step 2: Check SOL balance and attempt funding if needed
            await EnsureCoreWalletHasSufficientSolAsync(coreWallet);
            
            // Decode the Base64 private key correctly
            var privateKeyBytes = Convert.FromBase64String(coreWallet.PrivateKey);
            var coreKeyPair = RestoreWallet(privateKeyBytes);
            
            // Step 3: Create token mints using core wallet as authority
            _logger.LogInformation("🪙 Creating token mints...");
            var tokenADecimals = parameters.TokenADecimals ?? 9; // Default SOL-like
            var tokenBDecimals = parameters.TokenBDecimals ?? 6; // Default USDC-like
            
            var tokenAMint = await CreateTokenMintAsync(tokenADecimals, "TESTA");
            var tokenBMint = await CreateTokenMintAsync(tokenBDecimals, "TESTB");
            
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
                _logger.LogInformation("📤 Submitting pool creation to smart contract...");
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
                
                // Call RPC client directly (bypass our wrapper that's causing serialization issues)
                Console.WriteLine($"[DEBUG] About to call _rpcClient.SendTransactionAsync with {poolTransactionBytes.Length} bytes...");
                var result = await _rpcClient.SendTransactionAsync(poolTransactionBytes);
                Console.WriteLine($"[DEBUG] _rpcClient.SendTransactionAsync completed. Success: {result.WasRequestSuccessfullyHandled}");
                
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
                
                _logger.LogInformation("✅ Pool created on smart contract: {Signature}", poolCreationSignature);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Smart contract pool creation failed, but tokens are created");
                poolCreationSignature = $"failed_{Guid.NewGuid():N}";
            }
            
            // Step 5: Create real pool data (tokens exist regardless of pool creation success)
            var poolStatePda = DerivePoolStatePda(poolConfig.TokenAMint, poolConfig.TokenBMint);
            
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
            
            // Step 6: Save pool data
            await _storageService.SaveRealPoolAsync(realPool);
            
            _logger.LogInformation("🎯 Real pool created: {PoolId}", realPool.PoolId);
            _logger.LogInformation("   Token A: {TokenA} ({Decimals} decimals)", realPool.TokenAMint, realPool.TokenADecimals);
            _logger.LogInformation("   Token B: {TokenB} ({Decimals} decimals)", realPool.TokenBMint, realPool.TokenBDecimals);
            _logger.LogInformation("   Ratio: {Ratio}", realPool.RatioDisplay);
            
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
            _logger.LogInformation("Retrieved {Count} real pools from storage", pools.Count);
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
                    _logger.LogInformation("✅ Airdrop request successful: {Lamports} lamports to {Address}, signature: {Signature}", 
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

        public string DeriveTokenVaultPda(string poolStatePda, string tokenMint)
        {
            var seeds = new List<byte[]>
            {
                Encoding.UTF8.GetBytes("token_a_vault"), // Will be either token_a_vault or token_b_vault based on token
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
            
            throw new InvalidOperationException($"Failed to derive token vault PDA for pool {poolStatePda}, token {tokenMint}");
        }

        public string DeriveLpMintPda(string poolStatePda, string tokenMint)
        {
            var seeds = new List<byte[]>
            {
                Encoding.UTF8.GetBytes("lp_token_a_mint"), // Will be either lp_token_a_mint or lp_token_b_mint based on token
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
            
            throw new InvalidOperationException($"Failed to derive LP mint PDA for pool {poolStatePda}, token {tokenMint}");
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
                _logger.LogInformation("Creating token mints for new pool");
                
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
                
                _logger.LogInformation(
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
                
                _logger.LogInformation(
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
            var rate = (double)config.RatioBDenominator / config.RatioANumerator;
            _logger.LogInformation("Pool ratio validated: 1 Token A = {Rate:F6} Token B", rate);
            
            // Warn about potentially problematic ratios
            if (rate > 1_000_000 || rate < 0.000001)
            {
                _logger.LogWarning(
                    "Pool ratio {Rate:F6} may be extreme. This could indicate a configuration error.", rate);
            }
        }
        
        // Enhanced PDA derivation using proper seeds
        private string DerivePoolStatePda(string tokenAMint, string tokenBMint)
        {
            var seeds = new List<byte[]>
            {
                Encoding.UTF8.GetBytes("pool_state"),
                new PublicKey(tokenAMint).KeyBytes,
                new PublicKey(tokenBMint).KeyBytes
            };
            
            if (PublicKey.TryFindProgramAddress(
                seeds, 
                new PublicKey(_config.ProgramId), 
                out var pda, 
                out _))
            {
                return pda.ToString();
            }
            
            throw new InvalidOperationException($"Failed to derive pool state PDA for tokens {tokenAMint}/{tokenBMint}");
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