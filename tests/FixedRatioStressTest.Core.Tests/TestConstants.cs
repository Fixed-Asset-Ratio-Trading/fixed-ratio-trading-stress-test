using FixedRatioStressTest.Common.Models;

namespace FixedRatioStressTest.Core.Tests;

/// <summary>
/// Test constants for unit tests - all configurable for different test scenarios
/// These constants allow developers to modify test behavior without changing test logic
/// </summary>
public static class TestConstants
{
    // Network Configuration
    /// <summary>RPC endpoint for localnet testing</summary>
    public const string RPC_URL = "http://192.168.2.88:8899";
    
    /// <summary>Program ID for the fixed ratio contract on localnet</summary>
    public const string PROGRAM_ID = "4aeVqtWhrUh6wpX8acNj2hpWXKEQwxjA3PYb2sHhNyCn";
    
    /// <summary>Transaction confirmation level</summary>
    public const string COMMITMENT_LEVEL = "confirmed";
    
    // Threading Configuration
    /// <summary>Maximum concurrent threads for stress testing</summary>
    public const int CONCURRENT_THREADS = 4;
    
    /// <summary>Duration in seconds for each thread lifecycle test</summary>
    public const int TEST_DURATION_SECONDS = 300; // 5 minutes
    
    /// <summary>Maximum time to wait for thread operations in milliseconds</summary>
    public const int OPERATION_TIMEOUT_MS = 30000; // 30 seconds
    
    /// <summary>Time to wait between operation checks in milliseconds</summary>
    public const int OPERATION_CHECK_INTERVAL_MS = 1000; // 1 second
    
    // Pool Configuration
    /// <summary>Default Token A decimals for test pools</summary>
    public const int TOKEN_A_DECIMALS = 9;
    
    /// <summary>Default Token B decimals for test pools</summary>
    public const int TOKEN_B_DECIMALS = 6;
    
    /// <summary>Default exchange ratio numerator (Token A)</summary>
    public const ulong EXCHANGE_RATIO_NUMERATOR = 1;
    
    /// <summary>Default exchange ratio denominator (Token B)</summary>
    public const ulong EXCHANGE_RATIO_DENOMINATOR = 1000;
    
    /// <summary>Pool creation timeout in milliseconds</summary>
    public const int POOL_CREATION_TIMEOUT_MS = 60000; // 1 minute
    
    // Transaction Configuration
    /// <summary>Initial SOL amount for test wallets (in lamports)</summary>
    public const ulong INITIAL_SOL_AMOUNT = 2_000_000_000; // 2 SOL
    
    /// <summary>Minimum SOL balance to maintain for fees (in lamports)</summary>
    public const ulong MIN_SOL_BALANCE = 100_000_000; // 0.1 SOL
    
    /// <summary>Default deposit amount in basis points (0.01% = 1 basis point)</summary>
    public const ulong DEFAULT_DEPOSIT_AMOUNT = 1000; // 10 basis points = 0.1%
    
    /// <summary>Default swap amount in basis points</summary>
    public const ulong DEFAULT_SWAP_AMOUNT = 500; // 5 basis points = 0.05%
    
    /// <summary>Maximum slippage tolerance for swaps (5%)</summary>
    public const double MAX_SLIPPAGE_PERCENT = 5.0;
    
    /// <summary>Transaction confirmation timeout in milliseconds</summary>
    public const int TRANSACTION_TIMEOUT_MS = 30000; // 30 seconds
    
    // Performance Configuration
    /// <summary>Target operations per second for stress tests</summary>
    public const int TARGET_OPERATIONS_PER_SECOND = 5;
    
    /// <summary>Maximum acceptable error rate percentage</summary>
    public const double MAX_ERROR_RATE_PERCENT = 5.0;
    
    /// <summary>Memory usage limit in MB</summary>
    public const int MEMORY_USAGE_LIMIT_MB = 1024;
    
    /// <summary>CPU usage limit percentage</summary>
    public const double CPU_USAGE_LIMIT_PERCENT = 80.0;
    
    // Test Execution Configuration
    /// <summary>Number of operations to complete before stopping thread (configurable per test)</summary>
    public const int TARGET_OPERATIONS_PER_TEST = 3;
    
    /// <summary>Maximum time to wait for target operations to complete</summary>
    public const int TARGET_OPERATIONS_TIMEOUT_MS = 180000; // 3 minutes
    
    /// <summary>Delay between operations in milliseconds</summary>
    public const int OPERATION_DELAY_MS = 1000; // 1 second
    
    /// <summary>Maximum retries for failed operations</summary>
    public const int MAX_OPERATION_RETRIES = 3;
    
    /// <summary>Backoff delay for retries in milliseconds</summary>
    public const int RETRY_BACKOFF_MS = 2000; // 2 seconds
    
    // Storage Configuration
    /// <summary>Test data directory for thread configs and statistics</summary>
    public const string TEST_DATA_DIRECTORY = "test_data";
    
    /// <summary>Cleanup test data after tests complete</summary>
    public const bool CLEANUP_TEST_DATA = true;
    
    /// <summary>Preserve test data for debugging (overrides cleanup)</summary>
    public const bool PRESERVE_TEST_DATA_FOR_DEBUG = false;
    
    // Logging Configuration
    /// <summary>Enable verbose logging for troubleshooting</summary>
    public const bool ENABLE_VERBOSE_LOGGING = true;
    
    /// <summary>Log operation details</summary>
    public const bool LOG_OPERATION_DETAILS = true;
    
    /// <summary>Log performance metrics</summary>
    public const bool LOG_PERFORMANCE_METRICS = true;
}

/// <summary>
/// Local VM overrides for running tests on 12-core development machine
/// Use these constants when running tests locally for optimal performance
/// </summary>
public static class LocalVmOverrides
{
    /// <summary>Adjusted concurrent threads for 12-core VM</summary>
    public const int CONCURRENT_THREADS_LOCAL = 8;
    
    /// <summary>Reduced test duration for faster local testing</summary>
    public const int TEST_DURATION_SECONDS_LOCAL = 120; // 2 minutes
    
    /// <summary>Lower target operations per second for VM</summary>
    public const int TARGET_OPERATIONS_PER_SECOND_LOCAL = 2;
    
    /// <summary>Reduced memory limit for VM</summary>
    public const int MEMORY_USAGE_LIMIT_MB_LOCAL = 8192; // 8 GB
    
    /// <summary>Higher CPU limit for dedicated testing</summary>
    public const double CPU_USAGE_LIMIT_PERCENT_LOCAL = 90.0;
    
    /// <summary>Longer timeout for VM network latency</summary>
    public const int OPERATION_TIMEOUT_MS_LOCAL = 45000; // 45 seconds
}

/// <summary>
/// Test scenario presets for different testing approaches
/// </summary>
public static class TestScenarios
{
    /// <summary>Quick smoke test configuration</summary>
    public static class QuickTest
    {
        public const int TARGET_OPERATIONS = 1;
        public const int TIMEOUT_MS = 30000;
        public const int CONCURRENT_THREADS = 1;
    }
    
    /// <summary>Standard test configuration</summary>
    public static class StandardTest
    {
        public const int TARGET_OPERATIONS = 3;
        public const int TIMEOUT_MS = 180000;
        public const int CONCURRENT_THREADS = 2;
    }
    
    /// <summary>Extended test configuration</summary>
    public static class ExtendedTest
    {
        public const int TARGET_OPERATIONS = 10;
        public const int TIMEOUT_MS = 600000; // 10 minutes
        public const int CONCURRENT_THREADS = 4;
    }
    
    /// <summary>Large scale test (for 32-core remote server)</summary>
    public static class LargeScaleTest
    {
        public const int TARGET_OPERATIONS = 50;
        public const int TIMEOUT_MS = 1800000; // 30 minutes
        public const int CONCURRENT_THREADS = 16;
    }
}
