# Fixed Ratio Trading Stress Test - Unit Testing Plan

**Document Version:** 1.0  
**Date:** January 2025  
**Purpose:** Comprehensive unit testing strategy for Fixed Ratio Trading contract stress testing  
**Target:** Isolated, self-contained tests that can run independently on remote servers

---

## 🎯 Testing Philosophy

### Core Principles
- **Isolation**: Each test is completely independent and self-contained
- **Self-Healing**: Tests automatically recreate pools if they don't exist
- **Configurable**: All test parameters are constants at the top of each test file
- **Reusable**: Common testing utilities are shared across all tests
- **Documented**: Every test and constant is thoroughly documented

### Remote Server Considerations
- Tests run on remote 32-core Threadripper server
- Pools may be deleted between test runs
- Tests must handle missing pools gracefully
- No dependency on previous test state
- All tests must be idempotent

---

## 📌 Test Suite Overview - What Will Be Tested

### Total Test Count: 25 Tests (Core Coverage)

#### 1. Core Application Tests (7 tests)
- **ThreadManager_CreateThread_Success**: Validates thread creation with proper configuration
- **ThreadManager_StartThread_Success**: Ensures threads start correctly
- **ThreadManager_StopThread_Success**: Validates clean thread shutdown
- **StorageService_SaveLoadConfig_Success**: Tests JSON persistence for thread configs
- **StorageService_SaveLoadStatistics_Success**: Tests statistics persistence
- **PerformanceMonitor_CollectMetrics_Success**: Validates performance monitoring
- **HealthCheck_AllSystemsOperational_Success**: Tests health check endpoints

#### 2. Pool Creation Tests (3 tests)
- **PoolCreation_StandardTokens_Success**: Create pool with standard token decimals (9/6)
- **PoolCreation_ExtremeRatios_Success**: Create pool with extreme exchange ratios
- **PoolCreation_RecreateIfMissing_Success**: Validate pool recreation if deleted

#### 3. Thread Creation & Lifecycle Tests (9 tests)

##### Deposit Thread Tests (3 tests)
- **DepositThread_Create_Success**: Create deposit thread with initial configuration
- **DepositThread_ExecuteTargetDeposits_Success**: Run thread until `TARGET_OPERATIONS_PER_TEST` successful deposits complete
- **DepositThread_StopAfterTargetOperations_Success**: Validate clean stop after `TARGET_OPERATIONS_PER_TEST` operations

##### Withdrawal Thread Tests (3 tests)
- **WithdrawalThread_Create_Success**: Create withdrawal thread (LP tokens from deposits)
- **WithdrawalThread_ExecuteTargetWithdrawals_Success**: Run thread until `TARGET_OPERATIONS_PER_TEST` successful withdrawals
- **WithdrawalThread_StopAfterTargetOperations_Success**: Validate clean stop after `TARGET_OPERATIONS_PER_TEST` operations

##### Swap Thread Tests (3 tests)
- **SwapThread_CreateAtoB_Success**: Create A→B swap thread
- **SwapThread_ExecuteTargetSwaps_Success**: Run thread until `TARGET_OPERATIONS_PER_TEST` successful swaps complete
- **SwapThread_StopAfterTargetOperations_Success**: Validate clean stop after `TARGET_OPERATIONS_PER_TEST` operations

#### 4. Integration Tests (6 tests)
- **Integration_DepositWithdrawalFlow_Success**: Deposit → Share LP → Withdrawal flow
- **Integration_SwapBothDirections_Success**: A→B and B→A swap coordination
- **Integration_TokenSharing_Success**: Validate token sharing between threads
- **Integration_ConcurrentOperations_Success**: Run 3 threads simultaneously (1 of each type)
- **Integration_PoolRecovery_Success**: Recreate pool and resume operations
- **Integration_ErrorRecovery_Success**: Validate recovery from transient errors

### Test Execution Pattern
Each thread lifecycle test follows this pattern:
1. **Create** thread with test configuration
2. **Start** thread and monitor operations
3. **Wait** for exactly `TARGET_OPERATIONS_PER_TEST` primary transactions to complete
4. **Stop** thread gracefully
5. **Validate** statistics show exactly `TARGET_OPERATIONS_PER_TEST` successful operations
6. **Cleanup** resources

### Success Criteria
- ✅ All 25 tests pass consistently
- ✅ Each thread type completes exactly `TARGET_OPERATIONS_PER_TEST` operations before stopping (default: 3)
- ✅ Thread stop commands execute within 2 seconds
- ✅ No resource leaks after test completion
- ✅ Pool recreation works when pools are missing

---

## 🖥️ Local VM Test Environment (This Machine)

- **OS**: Microsoft Windows 11 Pro
- **Logical processors**: 12
- **CPU**: Apple silicon (reported by virtualization layer)
- **Memory**: ~32 GB RAM

### Test Tuning Recommendations (Local VM)
- **Concurrency**: Target 4–8 concurrent test threads for integration/end-to-end tests to avoid contention on 12 logical processors.
- **Throughput goals**: Aim for 2–4 ops/sec aggregate in medium scenarios on this VM; raise on the 32-core host.
- **Rate limits**: Use modest backoff (500–1000 ms) for retries to reduce RPC burst load.
- **Batching**: Prefer smaller batch sizes for swap/deposit/withdraw tests to keep latency predictable.
- **CPU/memory guards**: Fail fast if sustained CPU > 85% or RAM free < 2 GB during performance runs.

### Scenario Preset Guidance (Local VM)
- Small: unchanged
- Medium (local-tuned): 6 concurrent threads, deposit ~0.005 SOL, duration 3–5 minutes
- Large (local-tuned): 10–16 concurrent threads max; prefer shorter runs (10–15 minutes)
- Extreme: Do not run on this VM; reserve for the 32-core host

### Optional Local Overrides Snippet
```csharp
public static class LocalVmOverrides
{
    // Use these to override defaults when running on this VM
    public const int CONCURRENT_THREADS = 6;                  // medium-scale on 12 logical CPUs
    public const ulong DEPOSIT_AMOUNT = 5_000_000;            // 0.005 SOL
    public const int TEST_DURATION_S = 300;                   // 5 minutes
    public const int TARGET_OPERATIONS_PER_SECOND = 3;        // aggregate target

    // Guardrails for this VM
    public const double CPU_USAGE_LIMIT_PERCENT = 85.0;       // tighter than default
    public const int MEMORY_USAGE_LIMIT_MB = 28_000;          // ~28 GB ceiling to leave headroom

    // Retry pacing for local RPC
    public const int RETRY_DELAY_MS = 800;                    // modest backoff to reduce spikes
}
```

> Note: Unit tests and light integration tests are executed on this VM. Full stress, extreme, and long-running performance benchmarks are reserved for the 32‑core Windows deployment machine.

---

## 📋 Testing Architecture

### 1. Test Library Structure
```
tests/
├── FixedRatioStressTest.Core.Tests/
│   ├── TestConstants.cs              # Global test constants
│   ├── TestUtilities/
│   │   ├── PoolTestHelper.cs         # Pool creation and management
│   │   ├── LiquidityTestHelper.cs    # Liquidity setup utilities
│   │   ├── WalletTestHelper.cs       # Wallet management utilities
│   │   └── TransactionTestHelper.cs  # Transaction utilities
│   ├── Unit/
│   │   ├── ThreadManagerTests.cs
│   │   ├── StorageServiceTests.cs
│   │   └── PerformanceTests.cs
│   └── Integration/
│       ├── PoolCreationTests.cs
│       ├── DepositTests.cs
│       ├── WithdrawalTests.cs
│       ├── SwapTests.cs
│       └── StressTests.cs
├── FixedRatioStressTest.Api.Tests/
│   ├── Controllers/
│   │   ├── ThreadControllerTests.cs
│   │   └── HealthControllerTests.cs
│   └── Services/
│       └── ThreadHealthCheckTests.cs
└── FixedRatioStressTest.Integration.Tests/
    ├── EndToEndTests.cs
    ├── ConcurrentOperationTests.cs
    └── PerformanceBenchmarkTests.cs
```

### 2. Test Constants Structure
Every test file must have a constants section at the top:

```csharp
/// <summary>
/// Test Configuration Constants
/// 
/// POOL_CONFIGURATION:
/// - TokenA_Decimals: Number of decimal places for Token A (e.g., 9 for SOL)
/// - TokenB_Decimals: Number of decimal places for Token B (e.g., 6 for USDT)
/// - Ratio_WholeNumber: Exchange rate as whole number (e.g., 160 means 1 Token A = 160 Token B)
/// - Ratio_Direction: "a_to_b" or "b_to_a" - determines which token is more valuable
/// 
/// LIQUIDITY_CONFIGURATION:
/// - Initial_Liquidity_A: Starting amount of Token A in pool (in basis points)
/// - Initial_Liquidity_B: Starting amount of Token B in pool (in basis points)
/// - Deposit_Amount: Amount to deposit in each test (in basis points)
/// - Withdrawal_Amount: Amount to withdraw in each test (in basis points)
/// - Swap_Amount: Amount to swap in each test (in basis points)
/// 
/// TIMING_CONFIGURATION:
/// - Operation_Delay_Ms: Delay between operations (750-2000ms for realistic testing)
/// - Confirmation_Timeout_S: Maximum time to wait for transaction confirmation
/// - Test_Duration_S: How long to run the test
/// 
/// THREADING_CONFIGURATION:
/// - Concurrent_Threads: Number of threads to run simultaneously
/// - Thread_Types: Array of thread types to test (deposit, withdrawal, swap)
/// - Auto_Refill_Enabled: Whether to enable automatic token refilling
/// - LP_Token_Sharing: Whether to enable LP token sharing between threads
/// 
/// NETWORK_CONFIGURATION:
/// - Rpc_Url: Solana RPC endpoint (localnet: http://192.168.2.88:8899)
/// - Program_Id: Fixed Ratio Trading program ID
/// - Commitment_Level: Transaction commitment level (confirmed, finalized)
/// 
/// PERFORMANCE_CONFIGURATION:
/// - Target_Operations_Per_Second: Expected operation throughput
/// - Max_Error_Rate_Percent: Maximum acceptable error rate
/// - Memory_Usage_Limit_Mb: Maximum memory usage during test
/// - Cpu_Usage_Limit_Percent: Maximum CPU usage during test
/// </summary>
public static class TestConstants
{
    // Pool Configuration
    public const int TOKEN_A_DECIMALS = 9;
    public const int TOKEN_B_DECIMALS = 6;
    public const int RATIO_WHOLE_NUMBER = 160;
    public const string RATIO_DIRECTION = "a_to_b";
    
    // Liquidity Configuration
    public const ulong INITIAL_LIQUIDITY_A = 1_000_000_000_000; // 1000 SOL in basis points
    public const ulong INITIAL_LIQUIDITY_B = 160_000_000_000;   // 160,000 USDT in basis points
    public const ulong DEPOSIT_AMOUNT = 10_000_000;             // 0.01 SOL in basis points
    public const ulong WITHDRAWAL_AMOUNT = 5_000_000;           // 0.005 SOL in basis points
    public const ulong SWAP_AMOUNT = 1_000_000;                 // 0.001 SOL in basis points
    
    // Timing Configuration
    public const int OPERATION_DELAY_MS = 1500;
    public const int CONFIRMATION_TIMEOUT_S = 30;
    public const int TEST_DURATION_S = 300; // 5 minutes
    
    // Threading Configuration
    public const int CONCURRENT_THREADS = 10;
    public static readonly string[] THREAD_TYPES = { "deposit", "withdrawal", "swap" };
    public const bool AUTO_REFILL_ENABLED = true;
    public const bool LP_TOKEN_SHARING = true;
    
    // Network Configuration
    public const string RPC_URL = "http://192.168.2.88:8899";
    public const string PROGRAM_ID = "4aeVqtWhrUh6wpX8acNj2hpWXKEQwxjA3PYb2sHhNyCn"; // Localnet
    public const string COMMITMENT_LEVEL = "confirmed";
    
         // Performance Configuration
     public const int TARGET_OPERATIONS_PER_SECOND = 5;
     public const double MAX_ERROR_RATE_PERCENT = 5.0;
     public const int MEMORY_USAGE_LIMIT_MB = 1024;
     public const double CPU_USAGE_LIMIT_PERCENT = 80.0;
     
     // Test Execution Configuration
     public const int TARGET_OPERATIONS_PER_TEST = 3; // Number of operations to complete before stopping thread
}
```

---

## 🧪 Test Library Components

### 1. PoolTestHelper.cs
```csharp
/// <summary>
/// Utility class for creating and managing test pools
/// 
/// This helper provides methods to:
/// - Create pools with specific token configurations
/// - Check if pools exist and recreate them if needed
/// - Validate pool state and ratios
/// - Clean up pools after tests
/// 
/// Usage:
/// var poolHelper = new PoolTestHelper(rpcUrl, programId);
/// var poolId = await poolHelper.CreateOrGetPoolAsync(tokenA, tokenB, ratio);
/// </summary>
public class PoolTestHelper
{
    /// <summary>
    /// Creates a pool if it doesn't exist, or returns existing pool ID
    /// 
    /// Parameters:
    /// - tokenAMint: Public key of Token A mint
    /// - tokenBMint: Public key of Token B mint  
    /// - ratioNumerator: Exchange rate numerator
    /// - ratioDenominator: Exchange rate denominator
    /// - poolName: Optional name for the pool (for identification)
    /// 
    /// Returns: Pool ID that can be used for operations
    /// </summary>
    public async Task<string> CreateOrGetPoolAsync(
        string tokenAMint, 
        string tokenBMint, 
        ulong ratioNumerator, 
        ulong ratioDenominator, 
        string? poolName = null);
    
    /// <summary>
    /// Validates that a pool exists and has correct configuration
    /// 
    /// Parameters:
    /// - poolId: The pool ID to validate
    /// - expectedTokenA: Expected Token A mint
    /// - expectedTokenB: Expected Token B mint
    /// - expectedRatio: Expected exchange ratio
    /// 
    /// Returns: True if pool is valid, false otherwise
    /// </summary>
    public async Task<bool> ValidatePoolAsync(
        string poolId, 
        string expectedTokenA, 
        string expectedTokenB, 
        decimal expectedRatio);
    
    /// <summary>
    /// Adds initial liquidity to a pool for testing
    /// 
    /// Parameters:
    /// - poolId: Pool to add liquidity to
    /// - amountA: Amount of Token A to add (in basis points)
    /// - amountB: Amount of Token B to add (in basis points)
    /// - wallet: Wallet to use for the operation
    /// 
    /// Returns: Transaction signature
    /// </summary>
    public async Task<string> AddInitialLiquidityAsync(
        string poolId, 
        ulong amountA, 
        ulong amountB, 
        Wallet wallet);
}
```

### 2. LiquidityTestHelper.cs
```csharp
/// <summary>
/// Utility class for managing liquidity in test pools
/// 
/// This helper provides methods to:
/// - Add liquidity to pools for testing
/// - Remove liquidity from pools
/// - Check liquidity balances
/// - Create liquidity scenarios for different test types
/// 
/// Usage:
/// var liquidityHelper = new LiquidityTestHelper(rpcUrl, programId);
/// await liquidityHelper.SetupTestLiquidityAsync(poolId, wallet, amountA, amountB);
/// </summary>
public class LiquidityTestHelper
{
    /// <summary>
    /// Sets up initial liquidity for a test scenario
    /// 
    /// Parameters:
    /// - poolId: Pool to add liquidity to
    /// - wallet: Wallet to use for the operation
    /// - amountA: Amount of Token A (in basis points)
    /// - amountB: Amount of Token B (in basis points)
    /// - scenario: Test scenario type (small, medium, large, extreme)
    /// 
    /// Returns: Transaction signature
    /// </summary>
    public async Task<string> SetupTestLiquidityAsync(
        string poolId, 
        Wallet wallet, 
        ulong amountA, 
        ulong amountB, 
        LiquidityScenario scenario);
    
    /// <summary>
    /// Creates a specific liquidity scenario for testing
    /// 
    /// Scenarios:
    /// - Small: Minimal liquidity for edge case testing
    /// - Medium: Normal liquidity for standard testing
    /// - Large: High liquidity for performance testing
    /// - Extreme: Maximum liquidity for stress testing
    /// 
    /// Parameters:
    /// - poolId: Pool to create scenario in
    /// - wallet: Wallet to use
    /// - scenario: Type of scenario to create
    /// 
    /// Returns: Transaction signature
    /// </summary>
    public async Task<string> CreateLiquidityScenarioAsync(
        string poolId, 
        Wallet wallet, 
        LiquidityScenario scenario);
}
```

### 3. WalletTestHelper.cs
```csharp
/// <summary>
/// Utility class for managing test wallets
/// 
/// This helper provides methods to:
/// - Create test wallets with specific balances
/// - Fund wallets with SOL and tokens
/// - Check wallet balances
/// - Create multiple wallets for concurrent testing
/// 
/// Usage:
/// var walletHelper = new WalletTestHelper(rpcUrl);
/// var wallet = await walletHelper.CreateFundedWalletAsync(solAmount, tokenAmounts);
/// </summary>
public class WalletTestHelper
{
    /// <summary>
    /// Creates a new wallet and funds it with specified amounts
    /// 
    /// Parameters:
    /// - solAmount: Amount of SOL to fund (in lamports)
    /// - tokenAmounts: Dictionary of token mint to amount (in basis points)
    /// - autoAirdrop: Whether to automatically airdrop SOL if needed
    /// 
    /// Returns: Funded wallet ready for testing
    /// </summary>
    public async Task<Wallet> CreateFundedWalletAsync(
        ulong solAmount, 
        Dictionary<string, ulong>? tokenAmounts = null, 
        bool autoAirdrop = true);
    
    /// <summary>
    /// Creates multiple wallets for concurrent testing
    /// 
    /// Parameters:
    /// - count: Number of wallets to create
    /// - solAmountPerWallet: SOL amount per wallet (in lamports)
    /// - tokenAmountsPerWallet: Token amounts per wallet
    /// 
    /// Returns: Array of funded wallets
    /// </summary>
    public async Task<Wallet[]> CreateMultipleWalletsAsync(
        int count, 
        ulong solAmountPerWallet, 
        Dictionary<string, ulong>? tokenAmountsPerWallet = null);
    
    /// <summary>
    /// Checks if a wallet has sufficient balance for operations
    /// 
    /// Parameters:
    /// - wallet: Wallet to check
    /// - requiredSol: Required SOL balance (in lamports)
    /// - requiredTokens: Required token balances (mint to amount)
    /// 
    /// Returns: True if wallet has sufficient balance
    /// </summary>
    public async Task<bool> HasSufficientBalanceAsync(
        Wallet wallet, 
        ulong requiredSol, 
        Dictionary<string, ulong>? requiredTokens = null);
}
```

### 4. TransactionTestHelper.cs
```csharp
/// <summary>
/// Utility class for transaction testing and validation
/// 
/// This helper provides methods to:
/// - Submit transactions and wait for confirmation
/// - Validate transaction results
/// - Handle transaction errors and retries
/// - Measure transaction performance
/// 
/// Usage:
/// var txHelper = new TransactionTestHelper(rpcUrl);
/// var result = await txHelper.SubmitAndConfirmAsync(transaction, wallet);
/// </summary>
public class TransactionTestHelper
{
    /// <summary>
    /// Submits a transaction and waits for confirmation
    /// 
    /// Parameters:
    /// - transaction: Transaction to submit
    /// - wallet: Wallet to sign with
    /// - maxRetries: Maximum number of retry attempts
    /// - retryDelayMs: Delay between retries
    /// 
    /// Returns: Transaction result with signature and status
    /// </summary>
    public async Task<TransactionResult> SubmitAndConfirmAsync(
        Transaction transaction, 
        Wallet wallet, 
        int maxRetries = 3, 
        int retryDelayMs = 1000);
    
    /// <summary>
    /// Validates transaction result and logs details
    /// 
    /// Parameters:
    /// - result: Transaction result to validate
    /// - expectedSuccess: Whether transaction should succeed
    /// - expectedErrorCode: Expected error code if transaction should fail
    /// 
    /// Returns: True if validation passes
    /// </summary>
    public bool ValidateTransactionResult(
        TransactionResult result, 
        bool expectedSuccess, 
        int? expectedErrorCode = null);
    
    /// <summary>
    /// Measures transaction performance metrics
    /// 
    /// Parameters:
    /// - transaction: Transaction to measure
    /// - wallet: Wallet to use
    /// - iterations: Number of times to run the transaction
    /// 
    /// Returns: Performance metrics (latency, throughput, error rate)
    /// </summary>
    public async Task<PerformanceMetrics> MeasureTransactionPerformanceAsync(
        Transaction transaction, 
        Wallet wallet, 
        int iterations = 10);
}
```

---

## 📝 Test Documentation Standards

### 1. Test File Header Template
```csharp
/// <summary>
/// [Test Name] - Unit/Integration Test
/// 
/// PURPOSE:
/// This test validates [specific functionality] by [testing approach].
/// It ensures that [expected behavior] occurs when [test conditions].
/// 
/// TEST SCENARIO:
/// 1. [Step 1 description]
/// 2. [Step 2 description]
/// 3. [Step 3 description]
/// ...
/// 
/// EXPECTED RESULTS:
/// - [Expected outcome 1]
/// - [Expected outcome 2]
/// - [Expected outcome 3]
/// 
/// CONSTANT CONFIGURATION:
/// - [Constant Name]: [Description of what this controls and how to modify it]
/// - [Constant Name]: [Description of what this controls and how to modify it]
/// - [Constant Name]: [Description of what this controls and how to modify it]
/// 
/// MODIFICATION GUIDE:
/// To test different scenarios, modify these constants:
/// - Increase [Constant] to test [scenario]
/// - Decrease [Constant] to test [scenario]
/// - Change [Constant] to [value] to test [scenario]
/// 
/// DEPENDENCIES:
/// - Requires [dependency 1]
/// - Requires [dependency 2]
/// - Uses [helper class] for [purpose]
/// 
/// CLEANUP:
/// - [What gets cleaned up after the test]
/// - [How cleanup is performed]
/// - [What state is preserved]
/// </summary>
```

### 2. Test Method Documentation Template
```csharp
/// <summary>
/// [Test Method Name]
/// 
/// WHAT IT TESTS:
/// This test method specifically validates [specific aspect] by [testing approach].
/// 
/// TEST FLOW:
/// 1. [Setup step] - [Purpose]
/// 2. [Action step] - [Purpose]
/// 3. [Validation step] - [Purpose]
/// 4. [Cleanup step] - [Purpose]
/// 
/// PARAMETERS:
/// - [Parameter Name]: [Description and valid values]
/// - [Parameter Name]: [Description and valid values]
/// 
/// ASSERTIONS:
/// - [Assertion 1]: [What it validates]
/// - [Assertion 2]: [What it validates]
/// - [Assertion 3]: [What it validates]
/// 
/// ERROR SCENARIOS:
/// - [Error condition 1]: [Expected behavior]
/// - [Error condition 2]: [Expected behavior]
/// 
/// PERFORMANCE EXPECTATIONS:
/// - Expected duration: [time]
/// - Expected memory usage: [amount]
/// - Expected CPU usage: [percentage]
/// </summary>
```

---

## 🧪 Test Categories

### 1. Unit Tests
- **ThreadManagerTests.cs**: Tests thread creation, management, and lifecycle
- **StorageServiceTests.cs**: Tests JSON file storage and state persistence
- **PerformanceTests.cs**: Tests 32-core optimization and memory management

### 2. Integration Tests
- **PoolCreationTests.cs**: Tests pool creation with various configurations
- **DepositTests.cs**: Tests liquidity deposit operations
- **WithdrawalTests.cs**: Tests liquidity withdrawal operations
- **SwapTests.cs**: Tests token swap operations
- **StressTests.cs**: Tests concurrent operations and load handling

### 3. API Tests
- **ThreadControllerTests.cs**: Tests REST API endpoints
- **HealthControllerTests.cs**: Tests health check endpoints
- **ThreadHealthCheckTests.cs**: Tests health monitoring service

### 4. End-to-End Tests
- **EndToEndTests.cs**: Complete workflow testing
- **ConcurrentOperationTests.cs**: Multi-thread concurrent testing
- **PerformanceBenchmarkTests.cs**: Performance benchmarking

---

## 🔧 Test Configuration Management

### 1. Environment-Specific Constants
```csharp
/// <summary>
/// Environment-specific test configuration
/// 
/// These constants change based on the testing environment:
/// - LOCALNET: http://192.168.2.88:8899 (for local development)
/// - DEVNET: https://api.devnet.solana.com (for devnet testing)
/// - MAINNET: https://api.mainnet-beta.solana.com (for production testing)
/// 
/// To switch environments, modify these constants:
/// - RPC_URL: Change to target environment
/// - PROGRAM_ID: Use appropriate program ID for environment
/// - COMMITMENT_LEVEL: Adjust based on environment requirements
/// </summary>
public static class EnvironmentConstants
{
    // Localnet Configuration
    public const string LOCALNET_RPC_URL = "http://192.168.2.88:8899";
    public const string LOCALNET_PROGRAM_ID = "4aeVqtWhrUh6wpX8acNj2hpWXKEQwxjA3PYb2sHhNyCn";
    
    // Devnet Configuration
    public const string DEVNET_RPC_URL = "https://api.devnet.solana.com";
    public const string DEVNET_PROGRAM_ID = "9iqh69RqeG3RRrFBNZVoE77TMRvYboFUtC2sykaFVzB7";
    
    // Mainnet Configuration
    public const string MAINNET_RPC_URL = "https://api.mainnet-beta.solana.com";
    public const string MAINNET_PROGRAM_ID = "quXSYkeZ8ByTCtYY1J1uxQmE36UZ3LmNGgE3CYMFixD";
}
```

### 2. Test Scenario Presets
```csharp
/// <summary>
/// Predefined test scenarios for different testing purposes
/// 
/// These presets provide common configurations for different test types:
/// - SMALL_SCALE: Minimal resources for quick testing
/// - MEDIUM_SCALE: Balanced resources for standard testing
/// - LARGE_SCALE: High resources for performance testing
/// - EXTREME_SCALE: Maximum resources for stress testing
/// 
/// To use a preset, copy the constants to your test file and modify as needed.
/// </summary>
public static class TestScenarioPresets
{
    public static class SmallScale
    {
        public const int CONCURRENT_THREADS = 2;
        public const ulong DEPOSIT_AMOUNT = 1_000_000; // 0.001 SOL
        public const int TEST_DURATION_S = 60; // 1 minute
        public const int TARGET_OPERATIONS_PER_SECOND = 1;
    }
    
    public static class MediumScale
    {
        public const int CONCURRENT_THREADS = 10;
        public const ulong DEPOSIT_AMOUNT = 10_000_000; // 0.01 SOL
        public const int TEST_DURATION_S = 300; // 5 minutes
        public const int TARGET_OPERATIONS_PER_SECOND = 5;
    }
    
    public static class LargeScale
    {
        public const int CONCURRENT_THREADS = 50;
        public const ulong DEPOSIT_AMOUNT = 100_000_000; // 0.1 SOL
        public const int TEST_DURATION_S = 1800; // 30 minutes
        public const int TARGET_OPERATIONS_PER_SECOND = 20;
    }
    
    public static class ExtremeScale
    {
        public const int CONCURRENT_THREADS = 100;
        public const ulong DEPOSIT_AMOUNT = 1_000_000_000; // 1 SOL
        public const int TEST_DURATION_S = 3600; // 1 hour
        public const int TARGET_OPERATIONS_PER_SECOND = 50;
    }
}
```

---

## 📊 Test Results and Reporting

### 1. Test Result Structure
```csharp
/// <summary>
/// Comprehensive test result structure
/// 
/// This structure captures all relevant information about a test run:
/// - Performance metrics (latency, throughput, resource usage)
/// - Error information (counts, types, details)
/// - Transaction details (signatures, confirmations, fees)
/// - System metrics (CPU, memory, network usage)
/// 
/// All test methods should return this structure for consistent reporting.
/// </summary>
public class TestResult
{
    public string TestName { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public TimeSpan Duration => EndTime - StartTime;
    
    // Performance Metrics
    public int TotalOperations { get; set; }
    public int SuccessfulOperations { get; set; }
    public int FailedOperations { get; set; }
    public double SuccessRate => TotalOperations > 0 ? (double)SuccessfulOperations / TotalOperations * 100 : 0;
    public double OperationsPerSecond { get; set; }
    public double AverageLatencyMs { get; set; }
    public double MaxLatencyMs { get; set; }
    public double MinLatencyMs { get; set; }
    
    // Resource Usage
    public double AverageCpuUsagePercent { get; set; }
    public double MaxCpuUsagePercent { get; set; }
    public double AverageMemoryUsageMb { get; set; }
    public double MaxMemoryUsageMb { get; set; }
    
    // Transaction Details
    public List<string> TransactionSignatures { get; set; } = new();
    public ulong TotalFeesPaid { get; set; }
    public double AverageFeePerTransaction { get; set; }
    
    // Error Information
    public List<TestError> Errors { get; set; } = new();
    public Dictionary<string, int> ErrorCounts { get; set; } = new();
    
    // Test Configuration
    public Dictionary<string, object> TestConfiguration { get; set; } = new();
    
    public bool IsSuccessful => SuccessRate >= 95.0 && FailedOperations <= 5;
}
```

### 2. Test Reporting
```csharp
/// <summary>
/// Test reporting utilities
/// 
/// These utilities provide methods to:
/// - Generate detailed test reports
/// - Export results to various formats
/// - Compare test results across runs
/// - Create performance trend analysis
/// 
/// Usage:
/// var reporter = new TestReporter();
/// await reporter.GenerateReportAsync(testResults, "test-report.html");
/// </summary>
public class TestReporter
{
    /// <summary>
    /// Generates a comprehensive HTML test report
    /// 
    /// Parameters:
    /// - results: Array of test results to include
    /// - outputPath: Path to save the HTML report
    /// - includeCharts: Whether to include performance charts
    /// 
    /// Returns: Path to generated report
    /// </summary>
    public async Task<string> GenerateHtmlReportAsync(
        TestResult[] results, 
        string outputPath, 
        bool includeCharts = true);
    
    /// <summary>
    /// Exports test results to JSON format
    /// 
    /// Parameters:
    /// - results: Test results to export
    /// - outputPath: Path to save JSON file
    /// 
    /// Returns: Path to generated JSON file
    /// </summary>
    public async Task<string> ExportToJsonAsync(
        TestResult[] results, 
        string outputPath);
    
    /// <summary>
    /// Compares test results and identifies performance regressions
    /// 
    /// Parameters:
    /// - baselineResults: Previous test results as baseline
    /// - currentResults: Current test results to compare
    /// - thresholdPercent: Performance degradation threshold
    /// 
    /// Returns: Comparison report with regressions identified
    /// </summary>
    public TestComparisonResult CompareResultsAsync(
        TestResult[] baselineResults, 
        TestResult[] currentResults, 
        double thresholdPercent = 10.0);
}
```

---

## 🚀 Implementation Roadmap

### Phase 1: Foundation (Week 1)
- [ ] Create TestConstants.cs with comprehensive documentation
- [ ] Implement PoolTestHelper.cs with pool creation and validation
- [ ] Implement WalletTestHelper.cs with wallet management
- [ ] Create basic test structure and templates

### Phase 2: Core Testing (Week 2)
- [ ] Implement ThreadManagerTests.cs
- [ ] Implement StorageServiceTests.cs
- [ ] Create basic integration tests for pool operations
- [ ] Add test result reporting structure

### Phase 3: Advanced Testing (Week 3)
- [ ] Implement comprehensive stress tests
- [ ] Add performance benchmarking tests
- [ ] Create concurrent operation tests
- [ ] Implement test result comparison and analysis

### Phase 4: Documentation and Validation (Week 4)
- [ ] Complete test documentation
- [ ] Create test execution scripts
- [ ] Validate all tests work on remote server
- [ ] Create test maintenance guide

---

## 📋 Test Execution Guidelines

### 1. Test Execution Order
1. **Unit Tests**: Run first to validate individual components
2. **Integration Tests**: Run after unit tests pass
3. **API Tests**: Run after integration tests pass
4. **End-to-End Tests**: Run last to validate complete workflows

### 2. Test Isolation Requirements
- Each test must create its own pools if needed
- Tests must not depend on previous test state
- All tests must clean up after themselves
- Tests must handle missing resources gracefully

### 3. Performance Testing Guidelines
- Run performance tests during off-peak hours
- Monitor system resources during test execution
- Collect detailed metrics for analysis
- Compare results against established baselines

### 4. Error Handling Requirements
- All tests must handle network errors gracefully
- Tests must retry failed operations with exponential backoff
- Tests must log detailed error information
- Tests must continue execution even if some operations fail

---

This testing plan provides a comprehensive framework for creating robust, maintainable, and well-documented tests that can run independently on remote servers while providing detailed feedback and performance metrics.
