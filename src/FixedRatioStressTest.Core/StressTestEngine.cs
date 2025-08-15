using System.Collections.Concurrent;
using FixedRatioStressTest.Abstractions;
using FixedRatioStressTest.Common.Models;
using FixedRatioStressTest.Core.Interfaces;
using FixedRatioStressTest.Core.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace FixedRatioStressTest.Core;

/// <summary>
/// Host-agnostic core engine which coordinates background services and thread operations.
/// This class implements <see cref="IServiceLifecycle"/> so it can be controlled uniformly by
/// different hosts (Windows Service, GUI, etc.).
/// </summary>
public sealed class StressTestEngine : IServiceLifecycle, IDisposable
{
    // External dependencies required by the engine.
    private readonly IThreadManager _threadManager;
    private readonly ISolanaClientService _solanaClientService;
    private readonly IContractVersionService _contractVersionService;
    private readonly IStorageService _storageService;
    private readonly IEventLogger _eventLogger;
    private readonly IConfiguration _configuration;
    private readonly IComputeUnitManager _computeUnitManager;

    // Hosted services which the engine starts and stops in a defined order.
    private readonly List<IHostedService> _startupServices;

    // Lifecycle state tracking with a lightweight async lock for safety.
    private readonly SemaphoreSlim _stateLock = new(1, 1);
    private ServiceState _state = ServiceState.Stopped;

    /// <inheritdoc />
    public ServiceState State => _state;

    /// <inheritdoc />
    public event EventHandler<ServiceStateChangedEventArgs>? StateChanged;

    /// <summary>
    /// Creates a new engine instance with all required dependencies.
    /// </summary>
    public StressTestEngine(
        IThreadManager threadManager,
        ISolanaClientService solanaClientService,
        IContractVersionService contractVersionService,
        IStorageService storageService,
        IComputeUnitManager computeUnitManager,
        IEventLogger eventLogger,
        IConfiguration configuration)
    {
        _threadManager = threadManager;
        _solanaClientService = solanaClientService;
        _contractVersionService = contractVersionService;
        _storageService = storageService;
        _computeUnitManager = computeUnitManager;
        _eventLogger = eventLogger;
        _configuration = configuration;

        // Construct the startup pipeline.
        // NOTE: We do not inject IHostApplicationLifetime here since this engine is host-agnostic.
        //       Startup services which require app lifetime control should be adapted if needed or
        //       replaced by engine-native checks.
        _startupServices = new List<IHostedService>
        {
            // Contract version check (must run first). We provide a shim for lifetime via engine state changes.
            new ContractVersionStartupService(
                versionService: _contractVersionService,
                appLifetime: new NoopHostApplicationLifetime(),
                logger: new Microsoft.Extensions.Logging.Abstractions.NullLogger<ContractVersionStartupService>()),

            // Pool management second; depends on successful version validation.
            new PoolManagementStartupService(
                solanaClient: _solanaClientService,
                logger: new Microsoft.Extensions.Logging.Abstractions.NullLogger<PoolManagementStartupService>())
        };
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await _stateLock.WaitAsync(cancellationToken);
        try
        {
            if (_state != ServiceState.Stopped)
            {
                _eventLogger.LogWarning("StartAsync ignored because state is {State}", _state);
                return;
            }

            _eventLogger.LogDebug("[Engine] StartAsync invoked");
            await ChangeStateAsync(ServiceState.Starting, "Engine startup initiated");

            // Apply Windows optimizations if running on Windows.
            if (OperatingSystem.IsWindows())
            {
                _eventLogger.LogDebug("[Engine] Applying Windows performance optimizations");
                WindowsPerformanceOptimizer.OptimizeForThreadripper();
            }

            // Start startup services in order.
            _eventLogger.LogDebug("[Engine] Starting {0} startup services", _startupServices.Count);
            foreach (var svc in _startupServices)
            {
                _eventLogger.LogDebug("[Engine] Starting service {0}", svc.GetType().Name);
                await svc.StartAsync(cancellationToken);
            }

            _eventLogger.LogInformation("Stress Test Engine started successfully");
            await ChangeStateAsync(ServiceState.Started, "Engine startup complete");
        }
        catch (Exception ex)
        {
            _eventLogger.LogCritical("Engine failed to start", ex);
            await ChangeStateAsync(ServiceState.Error, ex.Message);
            throw;
        }
        finally
        {
            _stateLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _stateLock.WaitAsync(cancellationToken);
        try
        {
            if (_state == ServiceState.Stopped)
            {
                return;
            }

            _eventLogger.LogDebug("[Engine] StopAsync invoked");
            await ChangeStateAsync(ServiceState.Stopping, "Engine shutdown initiated");

            // Stop all running stress threads gracefully.
            var allThreads = await _threadManager.GetAllThreadsAsync();
            _eventLogger.LogDebug("[Engine] Stopping {0} running threads", allThreads.Count(t => t.Status == ThreadStatus.Running));
            foreach (var t in allThreads.Where(t => t.Status == ThreadStatus.Running))
            {
                await _threadManager.StopThreadAsync(t.ThreadId);
            }

            // Stop services in reverse order.
            foreach (var svc in Enumerable.Reverse(_startupServices))
            {
                _eventLogger.LogDebug("[Engine] Stopping service {0}", svc.GetType().Name);
                await svc.StopAsync(cancellationToken);
            }

            await ChangeStateAsync(ServiceState.Stopped, "Engine shutdown complete");
            _eventLogger.LogInformation("Stress Test Engine stopped");
        }
        catch (Exception ex)
        {
            _eventLogger.LogError("Error during engine shutdown", ex);
            await ChangeStateAsync(ServiceState.Error, ex.Message);
            throw;
        }
        finally
        {
            _stateLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task PauseAsync(CancellationToken cancellationToken = default)
    {
        await _stateLock.WaitAsync(cancellationToken);
        try
        {
            if (_state != ServiceState.Started)
            {
                _eventLogger.LogWarning("PauseAsync ignored because state is {State}", _state);
                return;
            }

            _eventLogger.LogDebug("[Engine] PauseAsync invoked");
            await ChangeStateAsync(ServiceState.Pausing, "Pausing engine");

            // For now, we stop threads to simulate pause. A future enhancement can add native pause.
            var allThreads = await _threadManager.GetAllThreadsAsync();
            foreach (var t in allThreads.Where(t => t.Status == ThreadStatus.Running))
            {
                await _threadManager.StopThreadAsync(t.ThreadId);
            }

            await ChangeStateAsync(ServiceState.Paused, "Engine paused");
            _eventLogger.LogInformation("Engine paused");
        }
        finally
        {
            _stateLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task ResumeAsync(CancellationToken cancellationToken = default)
    {
        await _stateLock.WaitAsync(cancellationToken);
        try
        {
            if (_state != ServiceState.Paused)
            {
                _eventLogger.LogWarning("ResumeAsync ignored because state is {State}", _state);
                return;
            }

            await ChangeStateAsync(ServiceState.Resuming, "Resuming engine");

            // Resume threads that were previously paused (we treat Paused as Stopped threads in current model).
            var allThreads = await _threadManager.GetAllThreadsAsync();
            foreach (var t in allThreads.Where(t => t.Status == ThreadStatus.Paused))
            {
                await _threadManager.StartThreadAsync(t.ThreadId);
            }

            _eventLogger.LogDebug("[Engine] ResumeAsync invoked");
            await ChangeStateAsync(ServiceState.Started, "Engine resumed");
            _eventLogger.LogInformation("Engine resumed");
        }
        finally
        {
            _stateLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<ServiceHealthStatus> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        // Gather a minimal set of metrics without blocking long-running operations.
        var threads = await _threadManager.GetAllThreadsAsync();
        var running = threads.Count(t => t.Status == ThreadStatus.Running);
        var failed = threads.Count(t => t.Status == ThreadStatus.Failed);

        var metrics = new Dictionary<string, object>
        {
            ["State"] = _state.ToString(),
            ["TotalThreads"] = threads.Count,
            ["RunningThreads"] = running,
            ["FailedThreads"] = failed,
            ["ProcessId"] = Environment.ProcessId,
            ["MemoryUsageMB"] = GC.GetTotalMemory(false) / 1024 / 1024
        };

        var isHealthy = _state == ServiceState.Started && failed == 0;

        return new ServiceHealthStatus
        {
            IsHealthy = isHealthy,
            Status = isHealthy ? "Healthy" : "Degraded",
            Metrics = metrics,
            Timestamp = DateTime.UtcNow
        };
    }

    // Emits a typed state change event to observers (hosts, UI, etc.).
    private Task ChangeStateAsync(ServiceState newState, string reason)
    {
        var previous = _state;
        _state = newState;
        StateChanged?.Invoke(this, new ServiceStateChangedEventArgs
        {
            PreviousState = previous,
            NewState = newState,
            Timestamp = DateTime.UtcNow,
            Reason = reason
        });
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _stateLock.Dispose();
        foreach (var svc in _startupServices.OfType<IDisposable>())
        {
            svc.Dispose();
        }
    }

    /// <summary>
    /// Minimal IHostApplicationLifetime stub for startup services that expect a lifetime.
    /// In engine context we avoid process-level lifetime control.
    /// </summary>
    private sealed class NoopHostApplicationLifetime : IHostApplicationLifetime
    {
        public CancellationToken ApplicationStarted => CancellationToken.None;
        public CancellationToken ApplicationStopping => CancellationToken.None;
        public CancellationToken ApplicationStopped => CancellationToken.None;
        public void StopApplication() { /* intentionally no-op in engine context */ }
    }
}


