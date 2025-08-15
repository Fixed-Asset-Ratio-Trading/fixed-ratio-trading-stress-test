# Service Abstraction Layer Design Document
## Fixed Ratio Trading Stress Test Application

### Version: 1.0
### Date: December 2024

---

## 1. Executive Summary

This document outlines the architectural design for abstracting the Fixed Ratio Trading Stress Test application from its current tightly-coupled Windows Service implementation into a flexible, host-agnostic core engine with pluggable service hosts and logging interfaces.

### 1.1 Goals
- **Service Abstraction**: Create a clear separation between the core business logic and the hosting environment (Windows Service vs GUI Application)
- **Logging Abstraction**: Enable the core engine to log to different outputs (Windows Event Viewer vs GUI ListView) without knowledge of the destination
- **Lifecycle Management**: Provide consistent Start/Stop/Pause operations across different hosting models
- **Maintainability**: Reduce code duplication and improve testability
- **Future Extensibility**: Allow for additional hosting models (console app, Docker container, etc.)

### 1.2 Current State Analysis
The application currently has:
- **ASP.NET Core Web API** with embedded Windows Service support
- **Core Business Logic** tightly integrated with the web host
- **Direct Windows Service Registration** in `Program.cs`
- **Built-in Background Services** for monitoring and management
- **Thread Manager** that handles the core stress testing functionality

---

## 2. Architecture Overview

### 2.1 High-Level Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                    HOST LAYER                                   │
├─────────────────────────┬───────────────────────────────────────┤
│   Windows Service Host │           GUI Service Host            │
│   ┌─────────────────┐   │   ┌─────────────────────────────────┐ │
│   │ Service Control │   │   │ WinForms/WPF Application       │ │
│   │ Manager         │   │   │ ┌─────────────────────────────┐ │ │
│   │ Integration     │   │   │ │ Start/Stop/Pause Buttons   │ │ │
│   └─────────────────┘   │   │ └─────────────────────────────┘ │ │
│                         │   │ ┌─────────────────────────────┐ │ │
│                         │   │ │ Event Messages ListView    │ │ │
│                         │   │ └─────────────────────────────┘ │ │
│                         │   └─────────────────────────────────┘ │
└─────────────────────────┴───────────────────────────────────────┘
                          │
          ┌───────────────┴───────────────┐
          │     SERVICE ABSTRACTION       │
          │                               │
          │  ┌─────────────────────────┐  │
          │  │ IServiceLifecycle       │  │
          │  │ - Start()               │  │
          │  │ - Stop()                │  │
          │  │ - Pause()               │  │
          │  │ - Resume()              │  │
          │  └─────────────────────────┘  │
          │                               │
          │  ┌─────────────────────────┐  │
          │  │ IEventLogger            │  │
          │  │ - LogInformation()      │  │
          │  │ - LogWarning()          │  │
          │  │ - LogError()            │  │
          │  │ - LogCritical()         │  │
          │  └─────────────────────────┘  │
          └───────────────┬───────────────┘
                          │
          ┌───────────────┴───────────────┐
          │        CORE ENGINE            │
          │                               │
          │  ┌─────────────────────────┐  │
          │  │ StressTestEngine        │  │
          │  │                         │  │
          │  │ - ThreadManager         │  │
          │  │ - SolanaClientService   │  │
          │  │ - TransactionBuilder    │  │
          │  │ - PerformanceMonitor    │  │
          │  │ - StorageService        │  │
          │  └─────────────────────────┘  │
          └───────────────────────────────┘
```

### 2.2 Key Components

#### 2.2.1 Core Engine (`StressTestEngine`)
- **Single Responsibility**: Contains all business logic for stress testing
- **Host Agnostic**: No knowledge of Windows Services, GUI, or logging destinations
- **Dependency Injection**: Receives all dependencies through constructor injection
- **Lifecycle Aware**: Implements proper startup, shutdown, and pause semantics

#### 2.2.2 Service Abstraction Interfaces
- **`IServiceLifecycle`**: Defines Start/Stop/Pause/Resume operations
- **`IEventLogger`**: Abstracts logging operations from destination
- **`IServiceHost`**: Represents the hosting environment

#### 2.2.3 Service Hosts
- **`WindowsServiceHost`**: Integrates with Windows Service Control Manager
- **`GuiServiceHost`**: Provides WinForms/WPF interface for service control

#### 2.2.4 Logging Implementations
- **`WindowsEventLogger`**: Writes to Windows Event Viewer
- **`GuiEventLogger`**: Updates GUI ListView control

---

## 3. Detailed Interface Design

### 3.1 Core Interfaces

```csharp
namespace FixedRatioStressTest.Abstractions;

/// <summary>
/// Defines service lifecycle operations
/// </summary>
public interface IServiceLifecycle
{
    /// <summary>
    /// Current state of the service
    /// </summary>
    ServiceState State { get; }
    
    /// <summary>
    /// Raised when service state changes
    /// </summary>
    event EventHandler<ServiceStateChangedEventArgs> StateChanged;
    
    /// <summary>
    /// Start the service and all background operations
    /// </summary>
    Task StartAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Stop the service and cleanup resources
    /// </summary>
    Task StopAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Pause service operations (threads stop but connections maintained)
    /// </summary>
    Task PauseAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Resume service operations from paused state
    /// </summary>
    Task ResumeAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get current service health status
    /// </summary>
    Task<ServiceHealthStatus> GetHealthAsync();
}

/// <summary>
/// Service state enumeration
/// </summary>
public enum ServiceState
{
    Stopped,
    Starting,
    Started,
    Pausing,
    Paused,
    Resuming,
    Stopping,
    Error
}

/// <summary>
/// Abstracts event logging from destination
/// </summary>
public interface IEventLogger
{
    /// <summary>
    /// Log informational message
    /// </summary>
    void LogInformation(string message, params object[] args);
    
    /// <summary>
    /// Log warning message
    /// </summary>
    void LogWarning(string message, params object[] args);
    
    /// <summary>
    /// Log error message
    /// </summary>
    void LogError(string message, Exception? exception = null, params object[] args);
    
    /// <summary>
    /// Log critical error message
    /// </summary>
    void LogCritical(string message, Exception? exception = null, params object[] args);
    
    /// <summary>
    /// Log debug message (only in debug builds)
    /// </summary>
    void LogDebug(string message, params object[] args);
    
    /// <summary>
    /// Raised when a new log entry is created
    /// </summary>
    event EventHandler<LogEventArgs> LogEntryCreated;
}

/// <summary>
/// Represents a service host environment
/// </summary>
public interface IServiceHost
{
    /// <summary>
    /// Name of the host type
    /// </summary>
    string HostType { get; }
    
    /// <summary>
    /// Initialize the host environment
    /// </summary>
    Task InitializeAsync();
    
    /// <summary>
    /// Run the host until cancellation
    /// </summary>
    Task RunAsync(CancellationToken cancellationToken);
    
    /// <summary>
    /// Cleanup host resources
    /// </summary>
    Task ShutdownAsync();
}
```

### 3.2 Event Models

```csharp
/// <summary>
/// Service state change event arguments
/// </summary>
public class ServiceStateChangedEventArgs : EventArgs
{
    public ServiceState PreviousState { get; init; }
    public ServiceState NewState { get; init; }
    public DateTime Timestamp { get; init; }
    public string? Reason { get; init; }
}

/// <summary>
/// Log event arguments
/// </summary>
public class LogEventArgs : EventArgs
{
    public LogLevel Level { get; init; }
    public string Message { get; init; } = string.Empty;
    public Exception? Exception { get; init; }
    public DateTime Timestamp { get; init; }
    public string Category { get; init; } = string.Empty;
}

/// <summary>
/// Service health status
/// </summary>
public class ServiceHealthStatus
{
    public bool IsHealthy { get; init; }
    public string Status { get; init; } = string.Empty;
    public Dictionary<string, object> Metrics { get; init; } = new();
    public DateTime Timestamp { get; init; }
}
```

---

## 4. Core Engine Design

### 4.1 StressTestEngine Class

```csharp
namespace FixedRatioStressTest.Core;

/// <summary>
/// Main engine that contains all stress testing business logic
/// </summary>
public class StressTestEngine : IServiceLifecycle, IDisposable
{
    private readonly IThreadManager _threadManager;
    private readonly ISolanaClientService _solanaClient;
    private readonly IStorageService _storageService;
    private readonly IEventLogger _eventLogger;
    private readonly IConfiguration _configuration;
    private readonly List<IBackgroundService> _backgroundServices;
    
    private ServiceState _state = ServiceState.Stopped;
    private readonly SemaphoreSlim _stateLock = new(1, 1);
    
    public ServiceState State => _state;
    public event EventHandler<ServiceStateChangedEventArgs>? StateChanged;
    
    public StressTestEngine(
        IThreadManager threadManager,
        ISolanaClientService solanaClient,
        IStorageService storageService,
        IEventLogger eventLogger,
        IConfiguration configuration)
    {
        _threadManager = threadManager;
        _solanaClient = solanaClient;
        _storageService = storageService;
        _eventLogger = eventLogger;
        _configuration = configuration;
        
        // Initialize background services
        _backgroundServices = new List<IBackgroundService>
        {
            new ContractVersionStartupService(/* dependencies */),
            new PoolManagementStartupService(/* dependencies */),
            new PerformanceMonitorService(_eventLogger, _threadManager)
        };
    }
    
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await using var _ = await _stateLock.WaitAsync(cancellationToken);
        
        if (_state != ServiceState.Stopped)
            throw new InvalidOperationException($"Cannot start service in state {_state}");
            
        await ChangeStateAsync(ServiceState.Starting, "Service startup initiated");
        
        try
        {
            // Initialize Windows optimizations
            if (OperatingSystem.IsWindows())
            {
                WindowsPerformanceOptimizer.OptimizeForThreadripper();
            }
            
            // Start background services in sequence
            foreach (var service in _backgroundServices)
            {
                await service.StartAsync(cancellationToken);
            }
            
            _eventLogger.LogInformation("Stress Test Engine started successfully");
            await ChangeStateAsync(ServiceState.Started, "Service startup completed");
        }
        catch (Exception ex)
        {
            _eventLogger.LogError("Failed to start Stress Test Engine", ex);
            await ChangeStateAsync(ServiceState.Error, $"Startup failed: {ex.Message}");
            throw;
        }
    }
    
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await using var _ = await _stateLock.WaitAsync(cancellationToken);
        
        if (_state == ServiceState.Stopped)
            return;
            
        await ChangeStateAsync(ServiceState.Stopping, "Service shutdown initiated");
        
        try
        {
            // Stop all running threads
            var allThreads = await _threadManager.GetAllThreadsAsync();
            var runningThreads = allThreads.Where(t => t.Status == ThreadStatus.Running);
            
            foreach (var thread in runningThreads)
            {
                await _threadManager.StopThreadAsync(thread.ThreadId);
            }
            
            // Stop background services in reverse order
            foreach (var service in _backgroundServices.AsEnumerable().Reverse())
            {
                await service.StopAsync(cancellationToken);
            }
            
            _eventLogger.LogInformation("Stress Test Engine stopped successfully");
            await ChangeStateAsync(ServiceState.Stopped, "Service shutdown completed");
        }
        catch (Exception ex)
        {
            _eventLogger.LogError("Error during service shutdown", ex);
            await ChangeStateAsync(ServiceState.Error, $"Shutdown error: {ex.Message}");
            throw;
        }
    }
    
    public async Task PauseAsync(CancellationToken cancellationToken = default)
    {
        await using var _ = await _stateLock.WaitAsync(cancellationToken);
        
        if (_state != ServiceState.Started)
            throw new InvalidOperationException($"Cannot pause service in state {_state}");
            
        await ChangeStateAsync(ServiceState.Pausing, "Service pause initiated");
        
        try
        {
            // Pause all running threads (without stopping them)
            var allThreads = await _threadManager.GetAllThreadsAsync();
            var runningThreads = allThreads.Where(t => t.Status == ThreadStatus.Running);
            
            foreach (var thread in runningThreads)
            {
                await _threadManager.StopThreadAsync(thread.ThreadId); // Will implement pause later
            }
            
            _eventLogger.LogInformation("Stress Test Engine paused");
            await ChangeStateAsync(ServiceState.Paused, "Service paused");
        }
        catch (Exception ex)
        {
            _eventLogger.LogError("Error during service pause", ex);
            throw;
        }
    }
    
    public async Task ResumeAsync(CancellationToken cancellationToken = default)
    {
        await using var _ = await _stateLock.WaitAsync(cancellationToken);
        
        if (_state != ServiceState.Paused)
            throw new InvalidOperationException($"Cannot resume service in state {_state}");
            
        await ChangeStateAsync(ServiceState.Resuming, "Service resume initiated");
        
        try
        {
            // Resume paused threads
            var allThreads = await _threadManager.GetAllThreadsAsync();
            var pausedThreads = allThreads.Where(t => t.Status == ThreadStatus.Paused);
            
            foreach (var thread in pausedThreads)
            {
                await _threadManager.StartThreadAsync(thread.ThreadId);
            }
            
            _eventLogger.LogInformation("Stress Test Engine resumed");
            await ChangeStateAsync(ServiceState.Started, "Service resumed");
        }
        catch (Exception ex)
        {
            _eventLogger.LogError("Error during service resume", ex);
            throw;
        }
    }
    
    public async Task<ServiceHealthStatus> GetHealthAsync()
    {
        var allThreads = await _threadManager.GetAllThreadsAsync();
        var runningThreads = allThreads.Count(t => t.Status == ThreadStatus.Running);
        var failedThreads = allThreads.Count(t => t.Status == ThreadStatus.Failed);
        
        var metrics = new Dictionary<string, object>
        {
            ["State"] = _state.ToString(),
            ["TotalThreads"] = allThreads.Count,
            ["RunningThreads"] = runningThreads,
            ["FailedThreads"] = failedThreads,
            ["ProcessId"] = Environment.ProcessId,
            ["MemoryUsageMB"] = GC.GetTotalMemory(false) / 1024 / 1024
        };
        
        var isHealthy = _state == ServiceState.Started && failedThreads == 0;
        
        return new ServiceHealthStatus
        {
            IsHealthy = isHealthy,
            Status = isHealthy ? "Healthy" : "Degraded",
            Metrics = metrics,
            Timestamp = DateTime.UtcNow
        };
    }
    
    private async Task ChangeStateAsync(ServiceState newState, string reason)
    {
        var previousState = _state;
        _state = newState;
        
        var args = new ServiceStateChangedEventArgs
        {
            PreviousState = previousState,
            NewState = newState,
            Timestamp = DateTime.UtcNow,
            Reason = reason
        };
        
        StateChanged?.Invoke(this, args);
    }
    
    public void Dispose()
    {
        _stateLock?.Dispose();
        foreach (var service in _backgroundServices.OfType<IDisposable>())
        {
            service.Dispose();
        }
    }
}
```

---

## 5. Service Host Implementations

### 5.1 Windows Service Host

```csharp
namespace FixedRatioStressTest.Hosting.WindowsService;

/// <summary>
/// Windows Service implementation that hosts the core engine
/// </summary>
public class WindowsServiceHost : BackgroundService, IServiceHost
{
    private readonly StressTestEngine _engine;
    private readonly IEventLogger _eventLogger;
    private readonly ILogger<WindowsServiceHost> _msLogger;
    
    public string HostType => "WindowsService";
    
    public WindowsServiceHost(
        StressTestEngine engine,
        IEventLogger eventLogger,
        ILogger<WindowsServiceHost> msLogger)
    {
        _engine = engine;
        _eventLogger = eventLogger;
        _msLogger = msLogger;
    }
    
    public async Task InitializeAsync()
    {
        _eventLogger.LogInformation("Windows Service Host initializing");
        
        // Register for Windows Service events
        _engine.StateChanged += OnEngineStateChanged;
    }
    
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await ExecuteAsync(cancellationToken);
    }
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await _engine.StartAsync(stoppingToken);
            
            // Keep service running until cancellation
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(1000, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown
        }
        catch (Exception ex)
        {
            _eventLogger.LogCritical("Windows Service Host crashed", ex);
            throw;
        }
        finally
        {
            await _engine.StopAsync();
        }
    }
    
    public async Task ShutdownAsync()
    {
        _eventLogger.LogInformation("Windows Service Host shutting down");
        await _engine.StopAsync();
    }
    
    private void OnEngineStateChanged(object? sender, ServiceStateChangedEventArgs e)
    {
        _eventLogger.LogInformation($"Service state changed: {e.PreviousState} -> {e.NewState}. Reason: {e.Reason}");
    }
}

/// <summary>
/// Windows Event Logger implementation
/// </summary>
public class WindowsEventLogger : IEventLogger
{
    private readonly EventLog _eventLog;
    private readonly ILogger<WindowsEventLogger> _msLogger;
    
    public event EventHandler<LogEventArgs>? LogEntryCreated;
    
    public WindowsEventLogger(ILogger<WindowsEventLogger> msLogger)
    {
        _msLogger = msLogger;
        
        // Create Windows Event Log source
        var sourceName = "Fixed Ratio Stress Test";
        if (!EventLog.SourceExists(sourceName))
        {
            EventLog.CreateEventSource(sourceName, "Application");
        }
        
        _eventLog = new EventLog("Application") { Source = sourceName };
    }
    
    public void LogInformation(string message, params object[] args)
    {
        var formattedMessage = string.Format(message, args);
        _eventLog.WriteEntry(formattedMessage, EventLogEntryType.Information);
        _msLogger.LogInformation(formattedMessage);
        
        OnLogEntryCreated(LogLevel.Information, formattedMessage);
    }
    
    public void LogWarning(string message, params object[] args)
    {
        var formattedMessage = string.Format(message, args);
        _eventLog.WriteEntry(formattedMessage, EventLogEntryType.Warning);
        _msLogger.LogWarning(formattedMessage);
        
        OnLogEntryCreated(LogLevel.Warning, formattedMessage);
    }
    
    public void LogError(string message, Exception? exception = null, params object[] args)
    {
        var formattedMessage = string.Format(message, args);
        if (exception != null)
        {
            formattedMessage += $"\nException: {exception}";
        }
        
        _eventLog.WriteEntry(formattedMessage, EventLogEntryType.Error);
        _msLogger.LogError(exception, formattedMessage);
        
        OnLogEntryCreated(LogLevel.Error, formattedMessage, exception);
    }
    
    public void LogCritical(string message, Exception? exception = null, params object[] args)
    {
        var formattedMessage = string.Format(message, args);
        if (exception != null)
        {
            formattedMessage += $"\nException: {exception}";
        }
        
        _eventLog.WriteEntry(formattedMessage, EventLogEntryType.Error);
        _msLogger.LogCritical(exception, formattedMessage);
        
        OnLogEntryCreated(LogLevel.Critical, formattedMessage, exception);
    }
    
    public void LogDebug(string message, params object[] args)
    {
        var formattedMessage = string.Format(message, args);
        _msLogger.LogDebug(formattedMessage);
        
        OnLogEntryCreated(LogLevel.Debug, formattedMessage);
    }
    
    private void OnLogEntryCreated(LogLevel level, string message, Exception? exception = null)
    {
        LogEntryCreated?.Invoke(this, new LogEventArgs
        {
            Level = level,
            Message = message,
            Exception = exception,
            Timestamp = DateTime.UtcNow,
            Category = "StressTest"
        });
    }
}
```

### 5.2 GUI Service Host

```csharp
namespace FixedRatioStressTest.Hosting.Gui;

/// <summary>
/// GUI Service Host for WinForms application
/// </summary>
public partial class GuiServiceHost : Form, IServiceHost
{
    private readonly StressTestEngine _engine;
    private readonly GuiEventLogger _eventLogger;
    
    public string HostType => "GUI";
    
    // UI Controls
    private Button _startButton = null!;
    private Button _stopButton = null!;
    private Button _pauseButton = null!;
    private ListView _logListView = null!;
    private Label _statusLabel = null!;
    private ProgressBar _healthProgressBar = null!;
    
    public GuiServiceHost(StressTestEngine engine, GuiEventLogger eventLogger)
    {
        _engine = engine;
        _eventLogger = eventLogger;
        
        InitializeComponent();
        InitializeEventHandlers();
    }
    
    public async Task InitializeAsync()
    {
        // Subscribe to engine events
        _engine.StateChanged += OnEngineStateChanged;
        _eventLogger.LogEntryCreated += OnLogEntryCreated;
        
        // Initialize UI state
        UpdateUIState();
        
        // Start health monitoring timer
        var healthTimer = new System.Windows.Forms.Timer
        {
            Interval = 5000 // 5 seconds
        };
        healthTimer.Tick += async (s, e) => await UpdateHealthStatus();
        healthTimer.Start();
    }
    
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        // Run the WinForms message loop
        Application.Run(this);
    }
    
    public async Task ShutdownAsync()
    {
        if (_engine.State != ServiceState.Stopped)
        {
            await _engine.StopAsync();
        }
        
        Invoke(() => Close());
    }
    
    private void InitializeComponent()
    {
        SuspendLayout();
        
        // Form properties
        Text = "Fixed Ratio Stress Test Service Manager";
        Size = new Size(800, 600);
        StartPosition = FormStartPosition.CenterScreen;
        
        // Start Button
        _startButton = new Button
        {
            Text = "Start Service",
            Location = new Point(20, 20),
            Size = new Size(100, 30)
        };
        _startButton.Click += async (s, e) => await StartService();
        
        // Stop Button
        _stopButton = new Button
        {
            Text = "Stop Service",
            Location = new Point(130, 20),
            Size = new Size(100, 30)
        };
        _stopButton.Click += async (s, e) => await StopService();
        
        // Pause Button
        _pauseButton = new Button
        {
            Text = "Pause Service",
            Location = new Point(240, 20),
            Size = new Size(100, 30)
        };
        _pauseButton.Click += async (s, e) => await PauseResumeService();
        
        // Status Label
        _statusLabel = new Label
        {
            Text = "Status: Stopped",
            Location = new Point(20, 60),
            Size = new Size(200, 20),
            Font = new Font("Arial", 9, FontStyle.Bold)
        };
        
        // Health Progress Bar
        _healthProgressBar = new ProgressBar
        {
            Location = new Point(20, 90),
            Size = new Size(300, 20),
            Style = ProgressBarStyle.Continuous
        };
        
        // Log ListView
        _logListView = new ListView
        {
            Location = new Point(20, 120),
            Size = new Size(740, 420),
            View = View.Details,
            FullRowSelect = true,
            GridLines = true
        };
        
        _logListView.Columns.Add("Time", 120);
        _logListView.Columns.Add("Level", 80);
        _logListView.Columns.Add("Message", 520);
        
        // Add controls to form
        Controls.AddRange(new Control[] {
            _startButton, _stopButton, _pauseButton,
            _statusLabel, _healthProgressBar, _logListView
        });
        
        ResumeLayout();
    }
    
    private void InitializeEventHandlers()
    {
        FormClosing += async (s, e) =>
        {
            if (_engine.State != ServiceState.Stopped)
            {
                e.Cancel = true;
                await StopService();
                Close();
            }
        };
    }
    
    private async Task StartService()
    {
        try
        {
            await _engine.StartAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to start service: {ex.Message}", "Error", 
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
    
    private async Task StopService()
    {
        try
        {
            await _engine.StopAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to stop service: {ex.Message}", "Error", 
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
    
    private async Task PauseResumeService()
    {
        try
        {
            if (_engine.State == ServiceState.Started)
            {
                await _engine.PauseAsync();
            }
            else if (_engine.State == ServiceState.Paused)
            {
                await _engine.ResumeAsync();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to pause/resume service: {ex.Message}", "Error", 
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
    
    private void OnEngineStateChanged(object? sender, ServiceStateChangedEventArgs e)
    {
        if (InvokeRequired)
        {
            Invoke(() => OnEngineStateChanged(sender, e));
            return;
        }
        
        UpdateUIState();
    }
    
    private void OnLogEntryCreated(object? sender, LogEventArgs e)
    {
        if (InvokeRequired)
        {
            Invoke(() => OnLogEntryCreated(sender, e));
            return;
        }
        
        var item = new ListViewItem(e.Timestamp.ToString("HH:mm:ss"));
        item.SubItems.Add(e.Level.ToString());
        item.SubItems.Add(e.Message);
        
        // Color code by log level
        switch (e.Level)
        {
            case LogLevel.Error:
            case LogLevel.Critical:
                item.BackColor = Color.LightPink;
                break;
            case LogLevel.Warning:
                item.BackColor = Color.LightYellow;
                break;
        }
        
        _logListView.Items.Insert(0, item);
        
        // Keep only last 1000 entries
        while (_logListView.Items.Count > 1000)
        {
            _logListView.Items.RemoveAt(_logListView.Items.Count - 1);
        }
    }
    
    private void UpdateUIState()
    {
        var state = _engine.State;
        
        _startButton.Enabled = state == ServiceState.Stopped;
        _stopButton.Enabled = state == ServiceState.Started || state == ServiceState.Paused;
        _pauseButton.Enabled = state == ServiceState.Started || state == ServiceState.Paused;
        _pauseButton.Text = state == ServiceState.Paused ? "Resume Service" : "Pause Service";
        
        _statusLabel.Text = $"Status: {state}";
        _statusLabel.ForeColor = state switch
        {
            ServiceState.Started => Color.Green,
            ServiceState.Error => Color.Red,
            ServiceState.Paused => Color.Orange,
            _ => Color.Black
        };
    }
    
    private async Task UpdateHealthStatus()
    {
        try
        {
            var health = await _engine.GetHealthAsync();
            
            if (InvokeRequired)
            {
                Invoke(async () => await UpdateHealthStatus());
                return;
            }
            
            _healthProgressBar.Value = health.IsHealthy ? 100 : 50;
            _healthProgressBar.ForeColor = health.IsHealthy ? Color.Green : Color.Red;
        }
        catch
        {
            // Ignore health check errors
        }
    }
}

/// <summary>
/// GUI Event Logger that updates ListView
/// </summary>
public class GuiEventLogger : IEventLogger
{
    public event EventHandler<LogEventArgs>? LogEntryCreated;
    
    public void LogInformation(string message, params object[] args)
    {
        OnLogEntryCreated(LogLevel.Information, string.Format(message, args));
    }
    
    public void LogWarning(string message, params object[] args)
    {
        OnLogEntryCreated(LogLevel.Warning, string.Format(message, args));
    }
    
    public void LogError(string message, Exception? exception = null, params object[] args)
    {
        var formattedMessage = string.Format(message, args);
        if (exception != null)
        {
            formattedMessage += $" - {exception.Message}";
        }
        OnLogEntryCreated(LogLevel.Error, formattedMessage, exception);
    }
    
    public void LogCritical(string message, Exception? exception = null, params object[] args)
    {
        var formattedMessage = string.Format(message, args);
        if (exception != null)
        {
            formattedMessage += $" - {exception.Message}";
        }
        OnLogEntryCreated(LogLevel.Critical, formattedMessage, exception);
    }
    
    public void LogDebug(string message, params object[] args)
    {
        OnLogEntryCreated(LogLevel.Debug, string.Format(message, args));
    }
    
    private void OnLogEntryCreated(LogLevel level, string message, Exception? exception = null)
    {
        LogEntryCreated?.Invoke(this, new LogEventArgs
        {
            Level = level,
            Message = message,
            Exception = exception,
            Timestamp = DateTime.UtcNow,
            Category = "StressTest"
        });
    }
}
```

---

## 6. Project Structure and File Organization

### 6.1 New Project Structure

```
src/
├── FixedRatioStressTest.Abstractions/           # New: Core interfaces
│   ├── IServiceLifecycle.cs
│   ├── IEventLogger.cs
│   ├── IServiceHost.cs
│   ├── ServiceModels.cs
│   └── FixedRatioStressTest.Abstractions.csproj
│
├── FixedRatioStressTest.Core/                   # Modified: Business logic only
│   ├── StressTestEngine.cs                     # New: Main engine class
│   ├── Services/                                # Existing services
│   ├── Threading/                               # Existing threading
│   ├── Interfaces/                              # Existing interfaces
│   └── FixedRatioStressTest.Core.csproj        # Modified dependencies
│
├── FixedRatioStressTest.Hosting.WindowsService/ # New: Windows Service host
│   ├── WindowsServiceHost.cs
│   ├── WindowsEventLogger.cs
│   ├── Program.cs                               # Service entry point
│   └── FixedRatioStressTest.Hosting.WindowsService.csproj
│
├── FixedRatioStressTest.Hosting.Gui/           # New: GUI host
│   ├── GuiServiceHost.cs
│   ├── GuiServiceHost.Designer.cs
│   ├── GuiEventLogger.cs
│   ├── Program.cs                               # GUI entry point
│   └── FixedRatioStressTest.Hosting.Gui.csproj
│
├── FixedRatioStressTest.Api/                   # Modified: API only
│   ├── Controllers/                             # Existing controllers
│   ├── Program.cs                               # Modified: Use StressTestEngine
│   └── FixedRatioStressTest.Api.csproj        # Modified dependencies
│
├── FixedRatioStressTest.Common/                # Existing: Models
├── FixedRatioStressTest.Infrastructure/        # Existing: Data access
└── SharedBuildProperties.props                 # Existing: Build props
```

### 6.2 Dependency Graph

```
Abstractions (interfaces only)
    ↑
Core (business logic + StressTestEngine)
    ↑
├── Hosting.WindowsService
├── Hosting.Gui
└── Api (modified to use StressTestEngine)
```

---

## 7. Implementation Plan

### 7.1 Phase 1: Create Abstraction Layer
1. **Create `FixedRatioStressTest.Abstractions` project**
   - Define `IServiceLifecycle`, `IEventLogger`, `IServiceHost` interfaces
   - Create supporting models and enums
   
2. **Create `StressTestEngine` class**
   - Move business logic from `Program.cs` to engine
   - Implement `IServiceLifecycle` interface
   - Add dependency injection for `IEventLogger`

### 7.2 Phase 2: Create Windows Service Host
1. **Create `FixedRatioStressTest.Hosting.WindowsService` project**
   - Implement `WindowsServiceHost` class
   - Implement `WindowsEventLogger` class
   - Create new `Program.cs` for Windows Service

2. **Test Windows Service functionality**
   - Ensure Start/Stop/Pause operations work
   - Verify Event Viewer logging works

### 7.3 Phase 3: Create GUI Service Host
1. **Create `FixedRatioStressTest.Hosting.Gui` project**
   - Implement `GuiServiceHost` WinForms application
   - Implement `GuiEventLogger` class
   - Create service control UI with buttons and ListView

2. **Test GUI functionality**
   - Ensure buttons control service lifecycle
   - Verify ListView shows log messages in real-time

### 7.4 Phase 4: Modify Existing API
1. **Update `FixedRatioStressTest.Api` project**
   - Modify `Program.cs` to use `StressTestEngine`
   - Remove direct Windows Service registration
   - Keep API functionality intact

### 7.5 Phase 5: Testing and Documentation
1. **Integration testing**
   - Test all three hosting models
   - Verify shared core functionality
   
2. **Update documentation**
   - Installation guides for each host type
   - Configuration documentation

---

## 8. Benefits of This Design

### 8.1 Separation of Concerns
- **Core business logic** is isolated from hosting concerns
- **Logging destinations** are abstracted from log generation
- **Service lifecycle** is standardized across host types

### 8.2 Testability
- **Core engine** can be unit tested without hosting dependencies
- **Mock implementations** of interfaces enable isolated testing
- **Integration tests** can be written for each host type

### 8.3 Maintainability
- **Single source of truth** for business logic
- **Reduced code duplication** across different hosting models
- **Clear interfaces** make future modifications easier

### 8.4 Extensibility
- **New host types** can be added easily (console, Docker, etc.)
- **New logging destinations** can be implemented
- **Additional service features** can be added to the engine

---

## 9. Design Decisions and Requirements

Based on clarification, the following decisions have been finalized:

### 9.1 Requirements Summary
- **GUI Framework**: **WinForms** - Simple, reliable, and perfect for service management UI
- **Log Filtering**: Include dropdown/checkbox filtering for different log levels (Debug, Info, Warning, Error, Critical)
- **Configuration**: GUI application will have its own `appsettings.json` separate from Windows Service
- **Security**: No authentication required - GUI runs in test mode only
- **Scope**: GUI is service-agnostic - it should not know or care about stress testing specifics
- **Real-time Updates**: Show only log messages, no business-specific statistics or metrics
- **Installation**: No installer - both applications will be standalone executables

### 9.2 Enhanced GUI Features

The GUI will include advanced logging capabilities:

#### Log Filtering Options
- **All Logs**: Show all log levels (Trace, Debug, Info, Warning, Error, Critical)
- **Debug+**: Show Debug and above (excludes Trace)
- **Info+**: Show Information and above (excludes Trace, Debug) 
- **Warning+**: Show Warning and above (production-level logs)
- **Error+**: Show only Error and Critical logs
- **Critical Only**: Show only Critical errors

#### Visual Enhancements
- **Color Coding**: Different colors for each log level for quick identification
- **Auto-scroll**: Optional automatic scrolling to newest entries
- **Entry Limits**: Display last 1000 entries, store 5000 in memory
- **Clear Function**: Button to clear all displayed logs
- **Entry Counter**: Shows filtered/total entry counts

### 9.3 Configuration Files

#### GUI Application Configuration (`appsettings.json`)
```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "FixedRatioStressTest": "Debug"
    }
  },
  "GuiSettings": {
    "WindowTitle": "Service Manager - Test Mode",
    "DefaultLogLevel": "Information",
    "MaxLogEntries": 5000,
    "MaxDisplayEntries": 1000,
    "AutoScroll": true,
    "RefreshInterval": 100
  },
  "ServiceSettings": {
    "StartupMode": "Manual",
    "EnableDebugLogging": true,
    "LogToFile": false
  }
}
```

#### Windows Service Configuration (separate `appsettings.json`)
```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    },
    "EventLog": {
      "LogLevel": {
        "Default": "Information"
      }
    }
  },
  "NetworkConfiguration": {
    "BindAddress": "0.0.0.0",
    "HttpPort": 8080,
    "HttpsPort": 8443,
    "EnableHttps": false
  },
  "SolanaConfiguration": {
    "RpcUrl": "http://localhost:8899",
    "Commitment": "confirmed"
  },
  "ThreadingConfiguration": {
    "ReservedCores": 4,
    "WorkerCores": 28,
    "MaxThreadsPerCore": 4
  }
}
```

### 9.4 Final Project Structure

```
src/
├── FixedRatioStressTest.Abstractions/
│   ├── IServiceLifecycle.cs
│   ├── IEventLogger.cs
│   ├── IServiceHost.cs
│   ├── ServiceModels.cs
│   └── FixedRatioStressTest.Abstractions.csproj
│
├── FixedRatioStressTest.Core/
│   ├── StressTestEngine.cs                     # Host-agnostic business logic
│   ├── Services/                               # Existing services
│   ├── Threading/                              # Existing threading
│   ├── Interfaces/                             # Existing interfaces
│   └── FixedRatioStressTest.Core.csproj
│
├── FixedRatioStressTest.Hosting.WindowsService/
│   ├── WindowsServiceHost.cs
│   ├── WindowsEventLogger.cs
│   ├── Program.cs
│   ├── appsettings.json                        # Service-specific config
│   └── FixedRatioStressTest.Hosting.WindowsService.csproj
│
├── FixedRatioStressTest.Hosting.Gui/
│   ├── GuiServiceHost.cs                       # WinForms with log filtering
│   ├── GuiServiceHost.Designer.cs
│   ├── GuiEventLogger.cs
│   ├── Program.cs
│   ├── appsettings.json                        # GUI-specific config
│   └── FixedRatioStressTest.Hosting.Gui.csproj
│
├── FixedRatioStressTest.Api/                   # Optional: Keep existing API
│   ├── Controllers/
│   ├── Program.cs                              # Modified to use StressTestEngine
│   └── FixedRatioStressTest.Api.csproj
│
├── FixedRatioStressTest.Common/                # Existing
├── FixedRatioStressTest.Infrastructure/        # Existing
└── SharedBuildProperties.props
```

### 9.5 Key Design Principles

1. **Service Agnostic**: GUI doesn't know it's managing a stress testing service - it's a generic service manager
2. **Clean Separation**: Core business logic completely isolated from hosting concerns  
3. **Rich Logging**: Advanced filtering, color coding, and log management in GUI
4. **No Installation**: Both applications are portable executables
5. **Test Mode Focus**: GUI is designed for development/testing scenarios, not production

This design provides a robust, maintainable solution that meets all your requirements while keeping the GUI generic and reusable for other services in the future.
