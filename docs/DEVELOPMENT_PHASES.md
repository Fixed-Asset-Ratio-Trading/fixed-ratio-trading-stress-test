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
- **Remote Deployment:** Final deployment will be on the 128-core Threadripper system

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

### 3.1 Deposit Operations
- Implement actual deposit transactions
- Add LP token calculation
- Implement token sharing between threads

### 3.2 Withdrawal Operations
- Implement withdrawal transactions
- Add LP token validation
- Implement token distribution

### 3.3 Swap Operations
- Implement swap transactions
- Add price impact calculation
- Implement cross-thread token exchange

---

## 📊 Phase 4: Advanced Features

### 4.1 Performance Optimization
- Implement 128-core thread pool optimization
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
