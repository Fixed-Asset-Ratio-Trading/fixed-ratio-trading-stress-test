# Unified Logging Design

## Overview

This document outlines the migration from the dual-logger system (`ILogger<T>` and `IEventLogger`) to a unified logging approach using only `ILogger<T>` from Microsoft.Extensions.Logging, with custom `ILoggerProvider` implementations for different hosting scenarios.

## Current State

The codebase currently uses two logging interfaces:
1. **`ILogger<T>`** - Standard Microsoft.Extensions.Logging interface used by most services
2. **`IEventLogger`** - Custom abstraction for host-agnostic logging with event support

This dual approach creates confusion and redundancy.

## Proposed Architecture

### Core Principle

Use `ILogger<T>` exclusively throughout the application, with different `ILoggerProvider` implementations based on the hosting environment:

```
Application Code → ILogger<T> → ILoggerProvider → Destination
                                        ↓
                               ┌────────┴────────┐
                               │                 │
                         Windows Service    GUI App    Console App
                               │                 │           │
                         Event Log + File   ListView + File  Console + File
```

### Logger Provider Implementations

#### 1. Windows Service Logger Provider

```csharp
namespace FixedRatioStressTest.Logging.WindowsService;

public class WindowsServiceLoggerProvider : ILoggerProvider
{
    private readonly WindowsServiceLoggerOptions _options;
    private readonly ConcurrentDictionary<string, WindowsServiceLogger> _loggers;
    
    public ILogger CreateLogger(string categoryName)
    {
        return _loggers.GetOrAdd(categoryName, 
            name => new WindowsServiceLogger(name, _options));
    }
}

public class WindowsServiceLogger : ILogger
{
    // Logs to:
    // - Windows Event Log (for production monitoring)
    // - File (for detailed debugging)
    // - UDP endpoint (if configured for remote monitoring)
}
```

#### 2. GUI Logger Provider

```csharp
namespace FixedRatioStressTest.Logging.Gui;

public class GuiLoggerProvider : ILoggerProvider
{
    public event EventHandler<LogMessageEventArgs>? LogMessageReceived;
    
    private readonly GuiLoggerOptions _options;
    
    public ILogger CreateLogger(string categoryName)
    {
        return new GuiLogger(categoryName, _options, OnLogMessageReceived);
    }
    
    private void OnLogMessageReceived(LogMessageEventArgs e)
    {
        LogMessageReceived?.Invoke(this, e);
    }
}

public class GuiLogger : ILogger
{
    // Logs to:
    // - In-memory buffer with events for ListView updates
    // - File (with rotation)
    // - Filters by log level as configured
}

public class LogMessageEventArgs : EventArgs
{
    public DateTime Timestamp { get; init; }
    public LogLevel Level { get; init; }
    public string Category { get; init; }
    public string Message { get; init; }
    public Exception? Exception { get; init; }
}
```

#### 3. Console Logger Provider

```csharp
namespace FixedRatioStressTest.Logging.Console;

public class EnhancedConsoleLoggerProvider : ILoggerProvider
{
    // Extends the built-in console logger with:
    // - File logging
    // - Structured output format
    // - Color coding
}
```

### UDP Log Transport

For real-time log streaming between processes (e.g., API → GUI):

```csharp
namespace FixedRatioStressTest.Logging.Transport;

public class UdpLoggerProvider : ILoggerProvider
{
    private readonly UdpClient _udpClient;
    private readonly IPEndPoint _endpoint;
    
    // Sends log messages as JSON over UDP
}

public class UdpLogListenerService : BackgroundService
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly UdpClient _udpListener;
    
    // Receives UDP log messages and injects them into local ILogger
}
```

## Migration Plan

### Phase 1: Create New Logger Providers

1. **Create base project**: `FixedRatioStressTest.Logging`
   - Define common logging models and interfaces
   - Create abstract base classes for logger providers

2. **Implement Windows Service Logger Provider**
   - Event Log writer
   - File writer with rotation
   - Optional UDP transport

3. **Implement GUI Logger Provider**
   - Event-based message delivery for UI updates
   - File writer
   - Log level filtering

4. **Implement UDP Transport**
   - UDP sender provider
   - UDP listener service

### Phase 2: Update Host Applications

1. **Update API Program.cs**
   ```csharp
   builder.Logging.ClearProviders();
   builder.Logging.AddProvider(new WindowsServiceLoggerProvider(options));
   builder.Logging.AddProvider(new UdpLoggerProvider(udpOptions));
   ```

2. **Update GUI Program.cs**
   ```csharp
   services.AddLogging(builder =>
   {
       builder.ClearProviders();
       var guiProvider = new GuiLoggerProvider(guiOptions);
       builder.AddProvider(guiProvider);
       services.AddSingleton(guiProvider); // For UI event subscription
   });
   
   services.AddHostedService<UdpLogListenerService>();
   ```

3. **Update GUI Form**
   ```csharp
   public GuiServiceHost(IServiceLifecycle engine, GuiLoggerProvider loggerProvider)
   {
       _engine = engine;
       loggerProvider.LogMessageReceived += OnLogMessageReceived;
   }
   
   private void OnLogMessageReceived(object? sender, LogMessageEventArgs e)
   {
       if (IsHandleCreated)
           BeginInvoke(() => AddLogToListView(e));
   }
   ```

### Phase 3: Remove IEventLogger

1. **Update StressTestEngine**
   - Replace `IEventLogger` with `ILogger<StressTestEngine>`
   - Update all logging calls

2. **Update other components**
   - `PoolController`: Remove `IEventLogger`, use only `ILogger<PoolController>`
   - `RequestLoggingMiddleware`: Remove `IEventLogger`, use only `ILogger<RequestLoggingMiddleware>`
   - Remove all `IEventLogger` implementations

3. **Delete obsolete code**
   - Remove `IEventLogger` interface
   - Remove `WindowsEventLogger`, `GuiEventLogger`, `CompositeEventLogger`, etc.

## Configuration

### Windows Service appsettings.json
```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft": "Warning",
      "FixedRatioStressTest": "Debug"
    },
    "WindowsService": {
      "EventLogSource": "FixedRatioStressTest",
      "FileLogging": {
        "Enabled": true,
        "Path": "logs/service.log",
        "MaxSizeKB": 10240
      },
      "UdpTransport": {
        "Enabled": true,
        "Endpoint": "127.0.0.1:12345"
      }
    }
  }
}
```

### GUI appsettings.json
```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "FixedRatioStressTest": "Debug"
    },
    "Gui": {
      "FileLogging": {
        "Enabled": true,
        "Path": "logs/gui.log",
        "MaxSizeKB": 10240
      },
      "Display": {
        "MaxEntries": 5000,
        "AutoScroll": true
      }
    },
    "UdpListener": {
      "Port": 12345,
      "Enabled": true
    }
  }
}
```

## Benefits

1. **Standardization**: Uses industry-standard logging abstractions
2. **Flexibility**: Easy to add new logging destinations
3. **Performance**: Leverages optimized Microsoft.Extensions.Logging pipeline
4. **Testability**: Can mock `ILogger<T>` in unit tests
5. **Ecosystem**: Compatible with popular logging frameworks (Serilog, NLog)
6. **Configuration**: Unified configuration through appsettings.json

## Considerations

### Thread Safety
All logger implementations must be thread-safe as they will be called from multiple threads concurrently.

### Performance
- Use `LoggerMessage.Define` for high-frequency log messages
- Implement buffering for file and network writes
- Consider using `Channel<T>` for async log processing

### GUI Updates
- Always marshal log updates to UI thread using `BeginInvoke`
- Implement throttling to prevent UI flooding
- Maintain circular buffer to limit memory usage

### Backwards Compatibility
During migration, both logging systems will coexist temporarily. Ensure no logs are lost during the transition.

## Testing Strategy

1. **Unit Tests**
   - Test each logger provider in isolation
   - Verify thread safety
   - Test configuration binding

2. **Integration Tests**
   - Test UDP transport between processes
   - Verify GUI updates from background threads
   - Test file rotation logic

3. **End-to-End Tests**
   - Run all three hosting modes
   - Verify logs appear in correct destinations
   - Test filtering and configuration changes

## Success Criteria

1. All components use only `ILogger<T>`
2. No references to `IEventLogger` remain
3. GUI continues to show real-time logs
4. Windows Service logs to Event Viewer
5. All existing functionality preserved
6. Performance is equal or better than current implementation
