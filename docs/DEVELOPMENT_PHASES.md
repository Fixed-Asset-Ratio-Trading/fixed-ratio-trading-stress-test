# Fixed Ratio Trading Stress Test Service - Development Phases

**Document Version:** 1.0  
**Date:** January 2025  
**Purpose:** Phased development plan for building the .NET stress test service  
**Target:** Local development and testing with remote deployment

---

## 🎯 Development Overview

This document outlines the phased approach to building the Fixed Ratio Trading Stress Test Service. We'll develop locally using the latest .NET version, create a basic working application, and incrementally add features through testable phases.

### Development Principles
- **Local Development:** All development and testing done locally
- **Phased Approach:** One testable feature at a time
- **Incremental Testing:** Each phase is fully testable before proceeding
- **Remote Deployment:** Final deployment will be on the 32-core Threadripper system

---

## 📋 Phase 0: Environment Setup

### 0.1 Development Environment Requirements

#### **Required Software**
- **.NET 8.0 SDK** (latest version)
- **Visual Studio 2022** or **VS Code** with C# extensions
- **Git** for version control
- **PowerShell 7+** for Windows scripting
- **Windows 10/11** or **Windows Server 2019+**

#### **Optional Tools**
- **Postman** or **Insomnia** for API testing
- **Docker Desktop** for containerized testing
- **SQL Server Express** for local database testing

### 0.2 Project Structure Setup

```
FixedRatioStressTest/
├── src/
│   ├── FixedRatioStressTest.Api/          # ASP.NET Core Web API
│   ├── FixedRatioStressTest.Core/         # Business logic and services
│   ├── FixedRatioStressTest.Infrastructure/ # Data access and external services
│   └── FixedRatioStressTest.Common/       # Shared models and utilities
├── tests/
│   ├── FixedRatioStressTest.Api.Tests/    # API integration tests
│   ├── FixedRatioStressTest.Core.Tests/   # Unit tests
│   └── FixedRatioStressTest.Integration.Tests/ # End-to-end tests
├── scripts/
│   ├── setup-dev-environment.ps1          # Development environment setup
│   ├── run-tests.ps1                      # Test execution script
│   └── deploy-local.ps1                   # Local deployment script
├── docs/
│   ├── STRESS_TEST_SERVICE_DESIGN.md      # Service design document
│   ├── DEVELOPMENT_PHASES.md              # This document
│   └── API_DOCUMENTATION.md               # API documentation
├── config/
│   ├── appsettings.Development.json       # Development configuration
│   ├── appsettings.Testing.json           # Testing configuration
│   └── appsettings.Production.json        # Production configuration
└── README.md
```

### 0.3 Initial Project Creation

```powershell
# Create solution and project structure
dotnet new sln -n FixedRatioStressTest
dotnet new webapi -n FixedRatioStressTest.Api -o src/FixedRatioStressTest.Api
dotnet new classlib -n FixedRatioStressTest.Core -o src/FixedRatioStressTest.Core
dotnet new classlib -n FixedRatioStressTest.Infrastructure -o src/FixedRatioStressTest.Infrastructure
dotnet new classlib -n FixedRatioStressTest.Common -o src/FixedRatioStressTest.Common

# Add test projects
dotnet new xunit -n FixedRatioStressTest.Api.Tests -o tests/FixedRatioStressTest.Api.Tests
dotnet new xunit -n FixedRatioStressTest.Core.Tests -o tests/FixedRatioStressTest.Core.Tests
dotnet new xunit -n FixedRatioStressTest.Integration.Tests -o tests/FixedRatioStressTest.Integration.Tests

# Add projects to solution
dotnet sln add src/FixedRatioStressTest.Api/FixedRatioStressTest.Api.csproj
dotnet sln add src/FixedRatioStressTest.Core/FixedRatioStressTest.Core.csproj
dotnet sln add src/FixedRatioStressTest.Infrastructure/FixedRatioStressTest.Infrastructure.csproj
dotnet sln add src/FixedRatioStressTest.Common/FixedRatioStressTest.Common.csproj
dotnet sln add tests/FixedRatioStressTest.Api.Tests/FixedRatioStressTest.Api.Tests.csproj
dotnet sln add tests/FixedRatioStressTest.Core.Tests/FixedRatioStressTest.Core.Tests.csproj
dotnet sln add tests/FixedRatioStressTest.Integration.Tests/FixedRatioStressTest.Integration.Tests.csproj
```

---

## 🚀 Phase 1: Basic Application Structure

### 1.1 Core Models and Interfaces

#### **Thread Types and Configuration**
```csharp
// FixedRatioStressTest.Common/Models/ThreadTypes.cs
public enum ThreadType
{
    Deposit,
    Withdrawal,
    Swap
}

public enum TokenType
{
    A,
    B
}

public enum SwapDirection
{
    AToB,
    BToA
}

public enum ThreadStatus
{
    Created,
    Running,
    Stopped,
    Error
}

// FixedRatioStressTest.Common/Models/ThreadConfig.cs
public class ThreadConfig
{
    public string ThreadId { get; set; } = string.Empty;
    public ThreadType ThreadType { get; set; }
    public string PoolId { get; set; } = string.Empty;
    public TokenType TokenType { get; set; }
    public SwapDirection? SwapDirection { get; set; }
    public ulong InitialAmount { get; set; }
    public bool AutoRefill { get; set; }
    public bool ShareTokens { get; set; }
    public ThreadStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastOperationAt { get; set; }
}

// FixedRatioStressTest.Common/Models/ThreadStatistics.cs
public class ThreadStatistics
{
    public int SuccessfulOperations { get; set; }
    public int FailedOperations { get; set; }
    public ulong TotalVolumeProcessed { get; set; }
    public ulong TotalFeesPaid { get; set; }
    public DateTime LastOperationAt { get; set; }
    public List<ThreadError> RecentErrors { get; set; } = new();
}

public class ThreadError
{
    public DateTime Timestamp { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
    public string OperationType { get; set; } = string.Empty;
}
```

#### **JSON-RPC Models**
```csharp
// FixedRatioStressTest.Common/Models/JsonRpcModels.cs
public class JsonRpcRequest
{
    [JsonPropertyName("jsonrpc")]
    public string JsonRpc { get; set; } = "2.0";
    
    [JsonPropertyName("method")]
    public string Method { get; set; } = string.Empty;
    
    [JsonPropertyName("params")]
    public object? Params { get; set; }
    
    [JsonPropertyName("id")]
    public object Id { get; set; } = string.Empty;
}

public class JsonRpcResponse<T>
{
    [JsonPropertyName("jsonrpc")]
    public string JsonRpc { get; set; } = "2.0";
    
    [JsonPropertyName("result")]
    public T? Result { get; set; }
    
    [JsonPropertyName("error")]
    public JsonRpcError? Error { get; set; }
    
    [JsonPropertyName("id")]
    public object Id { get; set; } = string.Empty;
}

public class JsonRpcError
{
    [JsonPropertyName("code")]
    public int Code { get; set; }
    
    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;
}
```

### 1.2 Basic Service Interfaces

```csharp
// FixedRatioStressTest.Core/Interfaces/IThreadManager.cs
public interface IThreadManager
{
    Task<string> CreateThreadAsync(ThreadConfig config);
    Task StartThreadAsync(string threadId);
    Task StopThreadAsync(string threadId);
    Task DeleteThreadAsync(string threadId);
    Task<ThreadConfig> GetThreadConfigAsync(string threadId);
    Task<List<ThreadConfig>> GetAllThreadsAsync();
    Task<ThreadStatistics> GetThreadStatisticsAsync(string threadId);
}

// FixedRatioStressTest.Core/Interfaces/IStorageService.cs
public interface IStorageService
{
    Task SaveThreadConfigAsync(string threadId, ThreadConfig config);
    Task<ThreadConfig> LoadThreadConfigAsync(string threadId);
    Task<List<ThreadConfig>> LoadAllThreadsAsync();
    Task SaveThreadStatisticsAsync(string threadId, ThreadStatistics statistics);
    Task<ThreadStatistics> LoadThreadStatisticsAsync(string threadId);
    Task AddThreadErrorAsync(string threadId, ThreadError error);
}
```

### 1.3 Basic Implementation (Phase 1 - No Blockchain)

```csharp
// FixedRatioStressTest.Core/Services/ThreadManager.cs
public class ThreadManager : IThreadManager
{
    private readonly IStorageService _storageService;
    private readonly ILogger<ThreadManager> _logger;
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _runningThreads;

    public ThreadManager(IStorageService storageService, ILogger<ThreadManager> logger)
    {
        _storageService = storageService;
        _logger = logger;
        _runningThreads = new ConcurrentDictionary<string, CancellationTokenSource>();
    }

    public async Task<string> CreateThreadAsync(ThreadConfig config)
    {
        // TODO: Phase 2 - Add Solana keypair generation
        config.ThreadId = $"{config.ThreadType.ToString().ToLower()}_{Guid.NewGuid():N}";
        config.CreatedAt = DateTime.UtcNow;
        config.Status = ThreadStatus.Created;

        await _storageService.SaveThreadConfigAsync(config.ThreadId, config);
        
        // Initialize empty statistics
        var statistics = new ThreadStatistics();
        await _storageService.SaveThreadStatisticsAsync(config.ThreadId, statistics);

        _logger.LogInformation("Created thread {ThreadId} of type {ThreadType}", 
            config.ThreadId, config.ThreadType);

        return config.ThreadId;
    }

    public async Task StartThreadAsync(string threadId)
    {
        var config = await _storageService.LoadThreadConfigAsync(threadId);
        
        if (config.Status == ThreadStatus.Running)
        {
            throw new InvalidOperationException($"Thread {threadId} is already running");
        }

        // TODO: Phase 2 - Add Solana wallet restoration
        // TODO: Phase 3 - Add actual blockchain operations

        var cancellationToken = new CancellationTokenSource();
        _runningThreads[threadId] = cancellationToken;

        // Start mock worker thread
        _ = Task.Run(async () => await RunMockWorkerThread(config, cancellationToken.Token));

        config.Status = ThreadStatus.Running;
        await _storageService.SaveThreadConfigAsync(threadId, config);

        _logger.LogInformation("Started thread {ThreadId}", threadId);
    }

    public async Task StopThreadAsync(string threadId)
    {
        if (_runningThreads.TryRemove(threadId, out var cancellationToken))
        {
            cancellationToken.Cancel();
        }

        var config = await _storageService.LoadThreadConfigAsync(threadId);
        config.Status = ThreadStatus.Stopped;
        await _storageService.SaveThreadConfigAsync(threadId, config);

        _logger.LogInformation("Stopped thread {ThreadId}", threadId);
    }

    public async Task DeleteThreadAsync(string threadId)
    {
        await StopThreadAsync(threadId);
        // TODO: Phase 2 - Add thread cleanup logic
        _logger.LogInformation("Deleted thread {ThreadId}", threadId);
    }

    public async Task<ThreadConfig> GetThreadConfigAsync(string threadId)
    {
        return await _storageService.LoadThreadConfigAsync(threadId);
    }

    public async Task<List<ThreadConfig>> GetAllThreadsAsync()
    {
        return await _storageService.LoadAllThreadsAsync();
    }

    public async Task<ThreadStatistics> GetThreadStatisticsAsync(string threadId)
    {
        return await _storageService.LoadThreadStatisticsAsync(threadId);
    }

    private async Task RunMockWorkerThread(ThreadConfig config, CancellationToken cancellationToken)
    {
        var random = new Random();
        
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                // TODO: Phase 2 - Add actual blockchain operations
                // TODO: Phase 3 - Add token balance checking
                // TODO: Phase 4 - Add deposit/withdrawal/swap logic

                // Mock operation - just log and update statistics
                var statistics = await _storageService.LoadThreadStatisticsAsync(config.ThreadId);
                statistics.SuccessfulOperations++;
                statistics.TotalVolumeProcessed += (ulong)random.Next(1000, 10000);
                statistics.LastOperationAt = DateTime.UtcNow;
                
                await _storageService.SaveThreadStatisticsAsync(config.ThreadId, statistics);

                // Random delay between operations
                await Task.Delay(random.Next(750, 2000), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in mock worker thread {ThreadId}", config.ThreadId);
                
                var error = new ThreadError
                {
                    Timestamp = DateTime.UtcNow,
                    ErrorMessage = ex.Message,
                    OperationType = "mock_operation"
                };
                
                await _storageService.AddThreadErrorAsync(config.ThreadId, error);
                
                // Wait before retrying
                await Task.Delay(5000, cancellationToken);
            }
        }
    }
}
```

### 1.4 JSON File Storage Implementation

```csharp
// FixedRatioStressTest.Infrastructure/Services/JsonFileStorageService.cs
public class JsonFileStorageService : IStorageService
{
    private readonly string _dataDirectory;
    private readonly ILogger<JsonFileStorageService> _logger;
    private readonly SemaphoreSlim _fileLock;
    private readonly JsonSerializerOptions _jsonOptions;

    public JsonFileStorageService(IConfiguration configuration, ILogger<JsonFileStorageService> logger)
    {
        _dataDirectory = configuration.GetValue<string>("Storage:DataDirectory") 
                        ?? Path.Combine(Environment.CurrentDirectory, "data");
        _logger = logger;
        _fileLock = new SemaphoreSlim(1, 1);
        
        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        
        EnsureDirectoriesExist();
    }

    public async Task SaveThreadConfigAsync(string threadId, ThreadConfig config)
    {
        await _fileLock.WaitAsync();
        try
        {
            var threadsFile = Path.Combine(_dataDirectory, "threads.json");
            var tempFile = $"{threadsFile}.tmp";
            
            var threads = await LoadAllThreadsAsync() ?? new List<ThreadConfig>();
            var existingIndex = threads.FindIndex(t => t.ThreadId == threadId);
            
            if (existingIndex >= 0)
                threads[existingIndex] = config;
            else
                threads.Add(config);
            
            var json = JsonSerializer.Serialize(new { threads }, _jsonOptions);
            await File.WriteAllTextAsync(tempFile, json);
            
            if (File.Exists(threadsFile))
                File.Replace(tempFile, threadsFile, $"{threadsFile}.backup");
            else
                File.Move(tempFile, threadsFile);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task<ThreadConfig> LoadThreadConfigAsync(string threadId)
    {
        var threads = await LoadAllThreadsAsync();
        return threads?.FirstOrDefault(t => t.ThreadId == threadId) 
               ?? throw new KeyNotFoundException($"Thread {threadId} not found");
    }

    public async Task<List<ThreadConfig>> LoadAllThreadsAsync()
    {
        var threadsFile = Path.Combine(_dataDirectory, "threads.json");
        if (!File.Exists(threadsFile))
            return new List<ThreadConfig>();
            
        var json = await File.ReadAllTextAsync(threadsFile);
        var data = JsonSerializer.Deserialize<JsonElement>(json, _jsonOptions);
        
        if (data.TryGetProperty("threads", out var threadsElement))
        {
            return threadsElement.Deserialize<List<ThreadConfig>>(_jsonOptions) ?? new List<ThreadConfig>();
        }
        
        return new List<ThreadConfig>();
    }

    public async Task SaveThreadStatisticsAsync(string threadId, ThreadStatistics statistics)
    {
        var statsFile = Path.Combine(_dataDirectory, "statistics.json");
        var tempFile = $"{statsFile}.tmp";
        
        var allStats = await LoadAllStatisticsAsync();
        allStats[threadId] = statistics;
        
        var json = JsonSerializer.Serialize(new { statistics = allStats }, _jsonOptions);
        await File.WriteAllTextAsync(tempFile, json);
        
        if (File.Exists(statsFile))
            File.Replace(tempFile, statsFile, $"{statsFile}.backup");
        else
            File.Move(tempFile, statsFile);
    }

    public async Task<ThreadStatistics> LoadThreadStatisticsAsync(string threadId)
    {
        var allStats = await LoadAllStatisticsAsync();
        return allStats.GetValueOrDefault(threadId, new ThreadStatistics());
    }

    public async Task AddThreadErrorAsync(string threadId, ThreadError error)
    {
        var statistics = await LoadThreadStatisticsAsync(threadId);
        statistics.RecentErrors.Add(error);
        
        // Keep only last 10 errors
        if (statistics.RecentErrors.Count > 10)
        {
            statistics.RecentErrors = statistics.RecentErrors
                .OrderByDescending(e => e.Timestamp)
                .Take(10)
                .ToList();
        }
        
        await SaveThreadStatisticsAsync(threadId, statistics);
    }

    private async Task<Dictionary<string, ThreadStatistics>> LoadAllStatisticsAsync()
    {
        var statsFile = Path.Combine(_dataDirectory, "statistics.json");
        if (!File.Exists(statsFile))
            return new Dictionary<string, ThreadStatistics>();
            
        var json = await File.ReadAllTextAsync(statsFile);
        var data = JsonSerializer.Deserialize<JsonElement>(json, _jsonOptions);
        
        if (data.TryGetProperty("statistics", out var statsElement))
        {
            return statsElement.Deserialize<Dictionary<string, ThreadStatistics>>(_jsonOptions) 
                   ?? new Dictionary<string, ThreadStatistics>();
        }
        
        return new Dictionary<string, ThreadStatistics>();
    }

    private void EnsureDirectoriesExist()
    {
        if (!Directory.Exists(_dataDirectory))
        {
            Directory.CreateDirectory(_dataDirectory);
        }
    }
}
```

### 1.5 Basic API Controller

```csharp
// FixedRatioStressTest.Api/Controllers/ThreadController.cs
[ApiController]
[Route("api/[controller]")]
public class ThreadController : ControllerBase
{
    private readonly IThreadManager _threadManager;
    private readonly ILogger<ThreadController> _logger;

    public ThreadController(IThreadManager threadManager, ILogger<ThreadController> logger)
    {
        _threadManager = threadManager;
        _logger = logger;
    }

    [HttpPost("create")]
    public async Task<ActionResult<JsonRpcResponse<CreateThreadResult>>> CreateThread(
        [FromBody] JsonRpcRequest request)
    {
        try
        {
            // TODO: Phase 2 - Add proper JSON-RPC parameter validation
            var threadId = await _threadManager.CreateThreadAsync(new ThreadConfig
            {
                ThreadType = ThreadType.Deposit,
                PoolId = "mock_pool_1",
                TokenType = TokenType.A,
                InitialAmount = 1000000
            });

            var result = new CreateThreadResult
            {
                ThreadId = threadId,
                Status = "created"
            };

            return Ok(new JsonRpcResponse<CreateThreadResult>
            {
                Result = result,
                Id = request.Id
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating thread");
            return Ok(new JsonRpcResponse<CreateThreadResult>
            {
                Error = new JsonRpcError
                {
                    Code = -1,
                    Message = ex.Message
                },
                Id = request.Id
            });
        }
    }

    [HttpPost("start")]
    public async Task<ActionResult<JsonRpcResponse<object>>> StartThread(
        [FromBody] JsonRpcRequest request)
    {
        try
        {
            // TODO: Phase 2 - Add proper parameter extraction
            var threadId = "deposit_123"; // Mock for now
            
            await _threadManager.StartThreadAsync(threadId);

            return Ok(new JsonRpcResponse<object>
            {
                Result = new { success = true },
                Id = request.Id
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting thread");
            return Ok(new JsonRpcResponse<object>
            {
                Error = new JsonRpcError
                {
                    Code = -1,
                    Message = ex.Message
                },
                Id = request.Id
            });
        }
    }

    [HttpGet("status/{threadId}")]
    public async Task<ActionResult<ThreadConfig>> GetThreadStatus(string threadId)
    {
        try
        {
            var config = await _threadManager.GetThreadConfigAsync(threadId);
            var statistics = await _threadManager.GetThreadStatisticsAsync(threadId);
            
            return Ok(new
            {
                config,
                statistics
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting thread status");
            return NotFound();
        }
    }

    [HttpGet("all")]
    public async Task<ActionResult<List<ThreadConfig>>> GetAllThreads()
    {
        try
        {
            var threads = await _threadManager.GetAllThreadsAsync();
            return Ok(threads);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting all threads");
            return StatusCode(500, "Internal server error");
        }
    }
}

public class CreateThreadResult
{
    public string ThreadId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}
```

---

## 🔧 Phase 2: Solana Integration Foundation

### 2.1 Solana Client Setup
- Add Solnet library dependencies
- Create Solana client service
- Implement basic wallet management
- Add configuration for local Solana testnet

### 2.2 Thread Enhancement
- Add actual Solana keypair generation
- Implement wallet state restoration
- Add basic balance checking
- Create mock pool creation

---

## 🎯 Phase 3: Core Operations Implementation

**📚 API Reference:** See `docs/API.md` for the complete Fixed Ratio Trading Contract API documentation with detailed function signatures, account structures, and implementation examples.

### 3.1 Contract Integration Foundation

#### 3.1.1 Enhanced Configuration
- **Program ID Configuration**: Add network-specific program IDs from API docs
- **Contract Settings**: Configure fees, compute units, and operational parameters
- **Network Selection**: Support localnet (`4aeVqtWhrUh6wpX8acNj2hpWXKEQwxjA3PYb2sHhNyCn`), devnet (`9iqh69RqeG3RRrFBNZVoE77TMRvYboFUtC2sykaFVzB7`), and mainnet (`quXSYkeZ8ByTCtYY1J1uxQmE36UZ3LmNGgE3CYMFixD`)
- **Localnet RPC Configuration**: Use `http://192.168.2.88:8899` for local development (per shared-config.json)
- **Public Access via ngrok**: Use `https://fixed.ngrok.app` for external testing (including Backpack wallet)
- **Basis Point Conversion**: All amounts must be converted to basis points (smallest token units)
- **Test Wallet**: Use provided localnet wallet `5GGZiMwU56rYL1L52q7Jz7ELkSN4iYyQqdv418hxPh6t` (LOCALNET ONLY!)

#### 3.1.2 Account Management System
- **PDA Derivation**: Implement proper Program Derived Address calculation for pool states, vaults, and LP mints
- **Token Account Handling**: Create and manage Associated Token Accounts for users and LP tokens
- **System State Integration**: Connect to contract's system state for pause validation
- **Treasury Integration**: Route fees to contract's main treasury

#### 3.1.3 Pool Normalization
- **Token Ordering**: Implement lexicographic token ordering (smaller pubkey = Token A)
- **Ratio Normalization**: Ensure one side equals exactly `10^decimals` (anchored to 1 rule)
- **Configuration Validation**: Use `normalize_pool_config()` pattern to prevent costly mistakes
- **Pool Creation Safety**: Validate ratios before spending 1.15 SOL pool creation fee (REGISTRATION_FEE constant)

### 3.2 Real Transaction Implementation

#### 3.2.1 Deposit Operations (`process_liquidity_deposit`)
- **Single Token Deposits**: Implement either Token A OR Token B deposits (not both)
- **1:1 LP Minting**: Receive LP tokens in exact 1:1 ratio with deposited amount  
- **Account Structure**: 12 accounts including user wallet, pool state, vaults, LP mints
- **Fee Handling**: Pay 0.0013 SOL fee per deposit operation (DEPOSIT_WITHDRAWAL_FEE constant)
- **Compute Units**: Allocate 310,000 CUs for reliable execution (Dashboard tested: min observed 249K; set 310K for safety margin)
- **Token Validation**: Ensure deposit token is one of pool's supported tokens

```csharp
// Example deposit operation structure
public async Task<string> SubmitDepositTransactionAsync(
    Wallet wallet, 
    string poolId, 
    TokenType tokenType, 
    ulong amountInBasisPoints)
{
    // 1. Derive pool state PDA
    // 2. Get appropriate token vault (A or B)
    // 3. Get LP token mint (A or B based on deposit)
    // 4. Create/get user's LP token account
    // 5. Build 12-account instruction
    // 6. Submit with 310k CU limit and 0.0013 SOL fee
}
```

#### 3.2.2 Withdrawal Operations (`process_liquidity_withdraw`)
- **LP Token Burning**: Burn Token A LP or Token B LP tokens (not both)
- **Token Recovery**: Receive underlying tokens matching LP token type
- **Account Validation**: Ensure user has sufficient LP tokens
- **Fee Payment**: Pay 0.0013 SOL withdrawal fee (DEPOSIT_WITHDRAWAL_FEE constant)
- **Compute Units**: Allocate 290,000 CUs for execution (Dashboard tested: min observed 227K; set 290K for safety margin)
- **Balance Updates**: Update pool liquidity tracking

```csharp
// Example withdrawal operation structure  
public async Task<string> SubmitWithdrawalTransactionAsync(
    Wallet wallet,
    string poolId, 
    TokenType tokenType,
    ulong lpTokenAmountToBurn)
{
    // 1. Validate LP token ownership
    // 2. Derive pool and vault PDAs
    // 3. Build withdrawal instruction
    // 4. Submit with 290k CU limit
}
```

#### 3.2.3 Swap Operations (`process_swap_execute`)
- **Fixed Ratio Swaps**: Use predetermined exchange rates (zero slippage)
- **Exact Input Model**: User specifies input, receives calculated output
- **Mathematical Formula**: `output = (input × output_ratio) ÷ input_ratio`
- **Slippage Protection**: Validate expected minimum output
- **Fee Structure**: Pay 0.00002715 SOL per swap (SWAP_CONTRACT_FEE constant)
- **Compute Units**: Allocate 250,000 CUs for execution (Dashboard tested: 202K works; set to 250K for headroom)

```csharp
// Example swap calculation and execution
public async Task<string> SubmitSwapTransactionAsync(
    Wallet wallet,
    string poolId,
    SwapDirection direction,
    ulong inputAmountBasisPoints,
    ulong minimumOutputBasisPoints)
{
    // 1. Load pool configuration and ratios
    // 2. Calculate exact output: (input × output_ratio) ÷ input_ratio  
    // 3. Validate against minimum expected
    // 4. Build 11-account swap instruction
    // 5. Submit with 250k CU limit
}
```

### 3.3 Enhanced Thread Operations

#### 3.3.1 Deposit Thread Enhancement
- **Balance Monitoring**: Check token balances before operations
- **Amount Calculation**: Random amounts from 1bp to 5% of balance (per design spec)
- **Airdrop Management**: Request SOL airdrops when balance < 0.1 SOL
- **LP Token Sharing**: Distribute earned LP tokens to withdrawal threads (if enabled)
- **Error Handling**: Handle contract-specific errors (1001-1042 error codes)

#### 3.3.2 Withdrawal Thread Enhancement  
- **LP Token Validation**: Verify LP token availability and type compatibility
- **Active Waiting**: Wait for LP tokens from deposit threads
- **Pool Verification**: Ensure LP tokens belong to correct pool
- **Token Distribution**: Share withdrawn tokens with deposit threads
- **Patience Logic**: Continue waiting without consuming resources

#### 3.3.3 Swap Thread Enhancement
- **Direction Management**: Handle A→B and B→A swap directions
- **Cross-Thread Exchange**: Transfer received tokens to opposite-direction threads
- **Pool Ratio Awareness**: Use contract's fixed ratios for calculations
- **Liquidity Checks**: Handle "no liquidity" scenarios gracefully
- **Volume Limits**: Respect 2% of balance limit per design

### 3.4 Contract Error Handling

#### 3.4.1 Error Code Integration
- **Standard Errors**: Handle ProgramError::Custom(code) format
- **Contract-Specific Codes**: 
  - 1001: InvalidTokenPair
  - 1002: InvalidRatio  
  - 1003: InsufficientFunds
  - 1004: InvalidTokenAccount
  - 1005: InvalidSwapAmount
  - 1006: RentExemptError
  - 1007: PoolPaused
  - 1012: Unauthorized
  - 1019: ArithmeticOverflow
  - 1023: SystemPaused
  - 1024: SystemAlreadyPaused
  - 1025: SystemNotPaused
  - 1026: UnauthorizedAccess
  - 1027: PoolSwapsPaused
  - 1029: PoolSwapsAlreadyPaused
  - 1030: PoolSwapsNotPaused
  - And others per API documentation

#### 3.4.2 Recovery Strategies
- **Pause Handling**: Detect and wait for system/pool unpause
- **Insufficient Funds**: Request airdrops or wait for token sharing
- **Rate Limiting**: Respect contract's operational timing constraints
- **Transaction Retries**: Implement exponential backoff for network issues

### 3.5 Integration Testing & Validation

#### 3.5.1 Contract Connectivity
- **Health Checks**: Verify connection to Fixed Ratio Trading program
- **System State**: Monitor contract's system pause status
- **Pool Discovery**: Enumerate available pools for testing
- **Account Validation**: Verify all derived accounts match contract expectations

#### 3.5.2 Operation Validation
- **Transaction Confirmation**: Wait for and validate transaction finality
- **Balance Verification**: Confirm expected balance changes after operations
- **LP Token Tracking**: Monitor LP token minting/burning
- **Fee Deduction**: Verify proper fee payments to treasury

**📖 Implementation Notes:**
- All amount calculations must use basis points (multiply by 10^decimals)
- Pool ratios must be anchored to 1 (one side = 10^decimals exactly)
- Use `normalize_pool_config()` pattern to prevent 1.15 SOL mistakes
- Refer to `C:\Users\Davinci\code\fixed-ratio-trading\docs\api\FIXED_RATIO_TRADING_API.md` for complete account structures and validation requirements
- Test on localnet first with program ID `4aeVqtWhrUh6wpX8acNj2hpWXKEQwxjA3PYb2sHhNyCn`
- For development outside LAN, use public ngrok endpoint: `https://fixed.ngrok.app`
- For devnet testing, use program ID `9iqh69RqeG3RRrFBNZVoE77TMRvYboFUtC2sykaFVzB7`

**⚠️ Important Function Name Corrections:**
- Fee consolidation function is named `process_consolidate_pool_fees` (not `process_treasury_consolidate_fees`)
- Use exact fee constants: `REGISTRATION_FEE`, `DEPOSIT_WITHDRAWAL_FEE`, `SWAP_CONTRACT_FEE`, `MIN_DONATION_AMOUNT`
- Pool creation requires proper `normalize_pool_config()` implementation to prevent permanent 1.15+ SOL losses

---

## 📊 Phase 4: Advanced Features

### 4.1 Performance Optimization
- Implement 32-core thread pool optimization
- Add NUMA-aware memory allocation
- Implement object pooling

### 4.2 Monitoring and Metrics
- Add comprehensive logging
- Implement performance counters
- Add health monitoring endpoints

### 4.3 Network Accessibility
- Configure remote access
- Add Windows Firewall rules
- Implement security features

---

## 🚀 Phase 5: Production Deployment

### 5.1 Windows Service
- Create Windows Service wrapper
- Add service installation scripts
- Configure auto-start and recovery

### 5.2 Remote Deployment
- Prepare deployment package
- Create remote installation scripts
- Configure production settings

---

## 📋 Development Workflow

### Daily Development Process
1. **Start with Phase 1:** Build basic structure
2. **Test each feature:** Ensure everything works before moving to next phase
3. **Commit frequently:** Use proper git commit standards
4. **Document changes:** Update this document as phases complete

### Testing Strategy
- **Unit Tests:** Each service and component
- **Integration Tests:** API endpoints and workflows
- **End-to-End Tests:** Complete thread lifecycle
- **Performance Tests:** Load testing with multiple threads

### Success Criteria for Each Phase
- ✅ All tests passing
- ✅ Basic functionality working
- ✅ No critical errors
- ✅ Ready for next phase

---

## 🎯 Next Steps

1. **Set up development environment** (Phase 0)
2. **Create basic project structure** (Phase 1)
3. **Implement mock thread system** (Phase 1)
4. **Test basic API functionality** (Phase 1)
5. **Begin Solana integration** (Phase 2)

This phased approach ensures we build a solid foundation and can test each component thoroughly before adding complexity.

---

## 🎯 Phase 3.5: Enhanced Contract Integration with Production-Ready Features

### Overview
Based on the enhanced API documentation with production-tested values and detailed implementation requirements, this phase upgrades the Phase 3 implementation to match production standards.

### 3.5.1 Compute Unit Management Enhancement

#### **Dynamic CU Allocation System**
```csharp
public class ComputeUnitManager
{
    // Production-tested CU requirements from dashboard
    private readonly Dictionary<string, uint> _computeUnits = new()
    {
        ["process_liquidity_deposit"] = 310_000,  // Min observed: 249K
        ["process_liquidity_withdraw"] = 290_000, // Min observed: 227K
        ["process_swap_execute"] = 250_000,       // Min observed: 202K
        ["process_pool_initialize"] = 150_000,    // Min observed: 91K
        ["process_consolidate_pool_fees"] = 150_000,
        ["process_treasury_donate_sol"] = 150_000,
        ["process_system_pause"] = 150_000,
        ["process_system_unpause"] = 150_000
    };
    
    public uint GetComputeUnits(string operation, TransactionContext context)
    {
        // Dynamic calculation for consolidation
        if (operation == "process_consolidate_pool_fees")
        {
            return CalculateConsolidationCU(context.PoolCount);
        }
        
        // Dynamic calculation for donations
        if (operation == "process_treasury_donate_sol")
        {
            return CalculateDonationCU(context.DonationAmount);
        }
        
        return _computeUnits.GetValueOrDefault(operation, 150_000);
    }
    
    private uint CalculateConsolidationCU(int poolCount)
    {
        // Formula: Base_CUs = 4,000 + (pool_count × 5,000)
        const uint BASE_CU = 4_000;
        const uint PER_POOL_CU = 5_000;
        return Math.Min(BASE_CU + (uint)(poolCount * PER_POOL_CU), 150_000);
    }
    
    private uint CalculateDonationCU(ulong donationLamports)
    {
        const ulong SMALL_DONATION_THRESHOLD = 1000L * 1_000_000_000L; // 1,000 SOL
        return donationLamports <= SMALL_DONATION_THRESHOLD ? 25_000u : 120_000u;
    }
}
```

### 3.5.2 Enhanced Pool Creation with Safety Mechanisms

#### **Pool Configuration Normalization**
```csharp
public class PoolNormalizer
{
    public NormalizedPoolConfig NormalizePoolConfig(
        PublicKey multipleMint,    // Abundant token (e.g., USDT)
        PublicKey baseMint,        // Valuable token (e.g., SOL)
        ulong originalRatioA,
        ulong originalRatioB)
    {
        // Token normalization (smaller pubkey = Token A)
        var shouldSwap = string.Compare(multipleMint.ToString(), baseMint.ToString()) > 0;
        
        if (shouldSwap)
        {
            // Swap tokens AND ratios to maintain correct exchange rate
            return new NormalizedPoolConfig
            {
                TokenAMint = baseMint,
                TokenBMint = multipleMint,
                RatioANumerator = originalRatioB,    // Swapped!
                RatioBDenominator = originalRatioA,  // Swapped!
                PoolStatePda = DerivePoolStatePda(baseMint, multipleMint)
            };
        }
        
        return new NormalizedPoolConfig
        {
            TokenAMint = multipleMint,
            TokenBMint = baseMint,
            RatioANumerator = originalRatioA,
            RatioBDenominator = originalRatioB,
            PoolStatePda = DerivePoolStatePda(multipleMint, baseMint)
        };
    }
    
    public void ValidatePoolRatio(NormalizedPoolConfig config, int tokenADecimals, int tokenBDecimals)
    {
        // Verify one side equals exactly 10^decimals (anchored to 1)
        var expectedA = (ulong)Math.Pow(10, tokenADecimals);
        var expectedB = (ulong)Math.Pow(10, tokenBDecimals);
        
        if (config.RatioANumerator != expectedA && config.RatioBDenominator != expectedB)
        {
            throw new InvalidOperationException(
                $"Invalid pool ratio: neither side is anchored to 1. " +
                $"Expected A={expectedA} or B={expectedB}, " +
                $"got A={config.RatioANumerator}, B={config.RatioBDenominator}");
        }
        
        // Log the exchange rate for verification
        var rate = (double)config.RatioBDenominator / config.RatioANumerator;
        _logger.LogInformation("Pool ratio validated: 1 Token A = {Rate} Token B", rate);
    }
}
```

### 3.5.3 Enhanced Transaction Builder with Account Validation

#### **Account Structure Validation**
```csharp
public class EnhancedTransactionBuilder : ITransactionBuilderService
{
    // Account structure validators for each operation
    private readonly Dictionary<string, AccountStructureValidator> _validators = new();
    
    public async Task<byte[]> BuildDepositTransactionAsync(
        Wallet wallet,
        string poolId,
        TokenType tokenType,
        ulong amountInBasisPoints)
    {
        var pool = await _solanaClient.GetPoolStateAsync(poolId);
        
        // Build account structure per API documentation
        var accounts = new List<AccountMeta>
        {
            // [0] User wallet (signer, writable) - pays fees & provides tokens
            AccountMeta.Writable(wallet.PublicKey, true),
            // [1] System Program
            AccountMeta.ReadOnly(SystemProgram.ProgramIdKey, false),
            // [2] SPL Token Program
            AccountMeta.ReadOnly(TokenProgram.ProgramIdKey, false),
            // [3] System State PDA
            AccountMeta.ReadOnly(DeriveSystemStatePda(), false),
            // [4] Pool State PDA
            AccountMeta.ReadOnly(new PublicKey(pool.PoolId), false),
            // [5] Deposit token mint (A or B)
            AccountMeta.ReadOnly(new PublicKey(tokenType == TokenType.A ? pool.TokenAMint : pool.TokenBMint), false),
            // [6] Appropriate vault PDA (writable)
            AccountMeta.Writable(new PublicKey(tokenType == TokenType.A ? pool.VaultA : pool.VaultB), false),
            // [7] User's token account
            AccountMeta.Writable(await GetAssociatedTokenAccount(wallet, tokenType == TokenType.A ? pool.TokenAMint : pool.TokenBMint), false),
            // [8] LP mint PDA (writable)
            AccountMeta.Writable(new PublicKey(tokenType == TokenType.A ? pool.LpMintA : pool.LpMintB), false),
            // [9] User's LP token account (writable)
            AccountMeta.Writable(await GetAssociatedTokenAccount(wallet, tokenType == TokenType.A ? pool.LpMintA : pool.LpMintB), false),
            // [10] Main Treasury PDA (writable)
            AccountMeta.Writable(new PublicKey(pool.MainTreasury), false),
            // [11] Pool Treasury PDA (writable)
            AccountMeta.Writable(new PublicKey(pool.PoolTreasury), false)
        };
        
        // Create instruction with proper discriminator and data
        var data = new DepositInstructionData
        {
            Discriminator = 6, // process_liquidity_deposit
            Amount = amountInBasisPoints
        };
        
        var instruction = new TransactionInstruction
        {
            ProgramId = new PublicKey(_config.ProgramId),
            Keys = accounts,
            Data = data.Serialize()
        };
        
        // Build transaction with compute budget
        var transaction = new TransactionBuilder()
            .SetFeePayer(wallet.PublicKey)
            .SetRecentBlockHash(await GetRecentBlockHashAsync())
            .AddInstruction(ComputeBudgetProgram.SetComputeUnitLimit(310_000))
            .AddInstruction(instruction)
            .Build(wallet.Account);
        
        return transaction.Serialize();
    }
}
```

### 3.5.4 Contract Error Handling with Recovery Strategies

#### **Comprehensive Error Handler**
```csharp
public class ContractErrorHandler
{
    public async Task<bool> HandleContractError(Exception ex, ThreadContext context)
    {
        if (ex is RpcException rpcEx && TryParseContractError(rpcEx, out var errorCode))
        {
            return errorCode switch
            {
                ContractErrorCodes.InsufficientFunds => await HandleInsufficientFunds(context),
                ContractErrorCodes.PoolPaused => await HandlePoolPaused(context),
                ContractErrorCodes.SystemPaused => await HandleSystemPaused(context),
                ContractErrorCodes.InsufficientLiquidity => await HandleInsufficientLiquidity(context),
                ContractErrorCodes.SlippageExceeded => await HandleSlippageExceeded(context),
                ContractErrorCodes.InvalidTokenAccount => await HandleInvalidTokenAccount(context),
                _ => await HandleUnknownError(errorCode, context)
            };
        }
        
        return false;
    }
    
    private async Task<bool> HandleInsufficientFunds(ThreadContext context)
    {
        _logger.LogWarning("Insufficient funds for {ThreadId}, requesting funding", context.ThreadId);
        
        // Check SOL balance
        var solBalance = await _solanaClient.GetSolBalanceAsync(context.WalletAddress);
        if (solBalance < SolanaConfiguration.MIN_SOL_BALANCE)
        {
            await _solanaClient.RequestAirdropAsync(context.WalletAddress, SolanaConfiguration.SOL_AIRDROP_AMOUNT);
        }
        
        // For deposit threads, check token balance and request minting if needed
        if (context.ThreadType == ThreadType.Deposit && context.AutoRefill)
        {
            await RequestTokenRefill(context);
        }
        
        // Wait before retrying
        await Task.Delay(5000);
        return true; // Retry operation
    }
    
    private async Task<bool> HandlePoolPaused(ThreadContext context)
    {
        _logger.LogInformation("Pool {PoolId} is paused, waiting for unpause", context.PoolId);
        
        // Check pool pause status periodically
        while (await _solanaClient.IsPoolPausedAsync(context.PoolId))
        {
            await Task.Delay(30000); // Check every 30 seconds
        }
        
        return true; // Retry operation
    }
}
```

### 3.5.5 Enhanced Thread Workers with Real Operations

#### **Production-Ready Deposit Worker**
```csharp
public class EnhancedDepositWorker : IThreadWorker
{
    private readonly ITransactionBuilderService _transactionBuilder;
    private readonly ISolanaClientService _solanaClient;
    private readonly IContractErrorHandler _errorHandler;
    private readonly ILogger<EnhancedDepositWorker> _logger;
    
    public async Task RunAsync(ThreadContext context, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                // Check balances
                var tokenBalance = await GetTokenBalance(context);
                
                // Check for auto-refill threshold
                if (context.AutoRefill && context.InitialAmount > 0)
                {
                    var threshold = (ulong)(context.InitialAmount * SolanaConfiguration.AUTO_REFILL_THRESHOLD);
                    if (tokenBalance < threshold)
                    {
                        await RequestTokenRefill(context);
                        tokenBalance = await GetTokenBalance(context);
                    }
                }
                
                // Calculate deposit amount (1bp to 5% of balance)
                var maxAmount = (ulong)(tokenBalance * SolanaConfiguration.MAX_DEPOSIT_PERCENTAGE);
                var depositAmount = (ulong)Random.Shared.NextInt64(1, (long)maxAmount + 1);
                
                // Execute deposit
                var result = await _solanaClient.ExecuteDepositAsync(
                    context.Wallet,
                    context.PoolId,
                    context.TokenType,
                    depositAmount);
                
                // Update statistics
                context.Statistics.SuccessfulDeposits++;
                context.Statistics.TotalTokensDeposited += result.TokensDeposited;
                context.Statistics.TotalLpTokensReceived += result.LpTokensReceived;
                context.Statistics.TotalPoolFeesPaid += result.PoolFeePaid;
                context.Statistics.TotalNetworkFeesPaid += result.NetworkFeePaid;
                
                // Share LP tokens if enabled
                if (context.ShareLpTokens)
                {
                    await ShareLpTokensWithWithdrawalThreads(context, result.LpTokensReceived);
                }
                
                // Random delay
                var delay = Random.Shared.Next(
                    SolanaConfiguration.MIN_OPERATION_DELAY_MS,
                    SolanaConfiguration.MAX_OPERATION_DELAY_MS);
                await Task.Delay(delay, cancellationToken);
            }
            catch (Exception ex)
            {
                var shouldRetry = await _errorHandler.HandleContractError(ex, context);
                if (!shouldRetry)
                {
                    _logger.LogError(ex, "Unrecoverable error in deposit thread {ThreadId}", context.ThreadId);
                    context.Status = ThreadStatus.Error;
                    break;
                }
            }
        }
    }
}
```

### 3.5.6 Empty Command Implementation

#### **Safe Empty Operations**
```csharp
public class EmptyCommandHandler
{
    public async Task<EmptyResult> ExecuteEmptyAsync(ThreadContext context)
    {
        var result = new EmptyResult
        {
            ThreadId = context.ThreadId,
            ThreadType = context.ThreadType,
            OperationType = $"{context.ThreadType.ToString().ToLower()}_empty"
        };
        
        try
        {
            switch (context.ThreadType)
            {
                case ThreadType.Deposit:
                    return await ExecuteDepositEmpty(context, result);
                case ThreadType.Withdrawal:
                    return await ExecuteWithdrawalEmpty(context, result);
                case ThreadType.Swap:
                    return await ExecuteSwapEmpty(context, result);
                default:
                    throw new InvalidOperationException($"Unknown thread type: {context.ThreadType}");
            }
        }
        catch (Exception ex)
        {
            result.OperationSuccessful = false;
            result.ErrorMessage = ex.Message;
            _logger.LogError(ex, "Empty command failed for thread {ThreadId}", context.ThreadId);
            return result;
        }
    }
    
    private async Task<EmptyResult> ExecuteDepositEmpty(ThreadContext context, EmptyResult result)
    {
        // Get current token balance
        var tokenBalance = await GetTokenBalance(context);
        result.TokensUsed = tokenBalance;
        
        if (tokenBalance == 0)
        {
            result.ErrorMessage = "No tokens available";
            return result;
        }
        
        // Burn tokens first (guaranteed removal)
        await BurnTokens(context, tokenBalance);
        result.TokensBurned = tokenBalance;
        
        try
        {
            // Attempt deposit operation
            var depositResult = await _solanaClient.ExecuteDepositAsync(
                context.Wallet,
                context.PoolId,
                context.TokenType,
                tokenBalance);
            
            result.LpTokensReceived = depositResult.LpTokensReceived;
            result.OperationSuccessful = true;
            
            // Burn received LP tokens
            await BurnLpTokens(context, depositResult.LpTokensReceived);
            result.LpTokensBurned = depositResult.LpTokensReceived;
        }
        catch (Exception ex)
        {
            // Operation failed but tokens already burned
            result.ErrorMessage = $"Deposit failed: {ex.Message}";
        }
        
        return result;
    }
}
```

### 3.5.7 Implementation Tasks

- [ ] Implement ComputeUnitManager with dynamic CU calculation
- [ ] Create PoolNormalizer with safety validation
- [ ] Enhance TransactionBuilder with full account structures
- [ ] Implement ContractErrorHandler with recovery strategies
- [ ] Update all thread workers with real blockchain operations
- [ ] Add EmptyCommandHandler for resource cleanup
- [ ] Create comprehensive test suite for each component
- [ ] Add performance monitoring and metrics collection
- [ ] Implement transaction confirmation with retry logic
- [ ] Add detailed logging for debugging and analysis

### 3.5.8 Testing Requirements

#### **Unit Tests**
- Pool normalization edge cases
- CU calculation formulas
- Error code parsing and handling
- Account structure validation

#### **Integration Tests**
- Full deposit/withdrawal/swap flows
- Error recovery scenarios
- Multi-thread coordination
- Empty command execution

#### **Stress Tests**
- High-frequency operations
- Concurrent thread execution
- Network failure simulation
- Pool pause/unpause handling

### 3.5.9 Success Criteria

- ✅ All operations use production-tested CU values
- ✅ Pool creation includes safety mechanisms
- ✅ Error handling matches contract error codes
- ✅ Thread workers perform real blockchain operations
- ✅ Empty commands properly clean up resources
- ✅ Statistics accurately track all operations
- ✅ Recovery strategies handle common failures
- ✅ Performance meets production requirements