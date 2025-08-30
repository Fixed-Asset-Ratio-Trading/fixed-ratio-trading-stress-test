using System;
using System.Threading;
using System.Threading.Tasks;
using FixedRatioStressTest.Abstractions;
using FixedRatioStressTest.Core.Interfaces;
using FixedRatioStressTest.Core.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FixedRatioStressTest.Core;

/// <summary>
/// Manages the lifecycle of the StressTestEngine Core object.
/// Ensures the Core object does not exist before Start and is completely removed after Stop.
/// Handles pause/resume with system-wide state management.
/// </summary>
public sealed class ServiceLifecycleManager : IServiceLifecycle, IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ISystemStateService _systemStateService;
    private readonly ILogger<ServiceLifecycleManager> _logger;
    private readonly SemaphoreSlim _stateLock = new(1, 1);
    
    private StressTestEngine? _coreEngine;
    private ServiceState _state = ServiceState.Stopped;

    /// <inheritdoc />
    public ServiceState State => _state;

    /// <inheritdoc />
    public event EventHandler<ServiceStateChangedEventArgs>? StateChanged;

    public ServiceLifecycleManager(IServiceProvider serviceProvider, ISystemStateService systemStateService, ILogger<ServiceLifecycleManager> logger)
    {
        _serviceProvider = serviceProvider;
        _systemStateService = systemStateService;
        _logger = logger;
    }



    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await _stateLock.WaitAsync(cancellationToken);
        try
        {
            if (_state != ServiceState.Stopped)
            {
                _logger.LogWarning("StartAsync ignored because state is {State}", _state);
                return;
            }

            _logger.LogInformation("[ServiceLifecycleManager] Starting system - creating Core object");
            await ChangeStateAsync(ServiceState.Starting, "Creating and initializing Core object");

            // Create the Core object (StressTestEngine) fresh
            _coreEngine = new StressTestEngine(
                _serviceProvider.GetRequiredService<IThreadManager>(),
                _serviceProvider.GetRequiredService<ISolanaClientService>(),
                _serviceProvider.GetRequiredService<IContractVersionService>(),
                _serviceProvider.GetRequiredService<IStorageService>(),
                _serviceProvider.GetRequiredService<IComputeUnitManager>(),
                _serviceProvider.GetRequiredService<ILogger<StressTestEngine>>(),
                _serviceProvider.GetRequiredService<IConfiguration>());

            // Subscribe to core engine state changes
            _coreEngine.StateChanged += OnCoreEngineStateChanged;

            // Start the core engine
            await _coreEngine.StartAsync(cancellationToken);

            await ChangeStateAsync(ServiceState.Started, "Core object created and system started");
            ((ISystemStateUpdater)_systemStateService).UpdateSystemState(ServiceState.Started, false);
            _logger.LogInformation("[ServiceLifecycleManager] System started successfully");
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "[ServiceLifecycleManager] Failed to start system");
            await ChangeStateAsync(ServiceState.Error, ex.Message);
            
            // Clean up if creation failed
            if (_coreEngine != null)
            {
                _coreEngine.StateChanged -= OnCoreEngineStateChanged;
                _coreEngine.Dispose();
                _coreEngine = null;
            }
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

            _logger.LogWarning("[ServiceLifecycleManager] STOPPING SYSTEM - DESTROYING CORE OBJECT AND ALL OPERATIONS");
            await ChangeStateAsync(ServiceState.Stopping, "Stopping and destroying Core object");

            // CRITICAL: Force stop the ThreadManager FIRST to prevent any new operations
            var threadManager = _serviceProvider.GetService<IThreadManager>();
            if (threadManager != null)
            {
                _logger.LogWarning("[ServiceLifecycleManager] FORCE STOPPING ThreadManager - All threads will be terminated immediately");
                await threadManager.ForceStopAllThreadsAsync();
            }

            if (_coreEngine != null)
            {
                try
                {
                    // Stop the core engine - threads should already be stopped
                    _logger.LogWarning("[ServiceLifecycleManager] Stopping Core Engine - Should be clean now");
                    await _coreEngine.StopAsync(cancellationToken);
                    
                    // Unsubscribe from events
                    _coreEngine.StateChanged -= OnCoreEngineStateChanged;
                    
                    // Dispose and remove the core engine completely
                    _coreEngine.Dispose();
                    
                    _logger.LogWarning("[ServiceLifecycleManager] Core Engine DESTROYED - All operations terminated");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[ServiceLifecycleManager] Error stopping core engine");
                }
                finally
                {
                    _coreEngine = null;
                }
            }

            await ChangeStateAsync(ServiceState.Stopped, "Core object destroyed and system stopped");
            ((ISystemStateUpdater)_systemStateService).UpdateSystemState(ServiceState.Stopped, false);
            _logger.LogInformation("[ServiceLifecycleManager] System stopped - Core object no longer exists");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ServiceLifecycleManager] Error during system shutdown");
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
                _logger.LogWarning("PauseAsync ignored because state is {State}", _state);
                return;
            }

            _logger.LogInformation("[ServiceLifecycleManager] Pausing system - threads will be paused, RPC will return 'system paused' errors");
            await ChangeStateAsync(ServiceState.Pausing, "Pausing system operations");

            if (_coreEngine != null)
            {
                await _coreEngine.PauseAsync(cancellationToken);
            }

            await ChangeStateAsync(ServiceState.Paused, "System paused - RPC calls will return 'system paused' errors");
            ((ISystemStateUpdater)_systemStateService).UpdateSystemState(ServiceState.Paused, true);
            _logger.LogInformation("[ServiceLifecycleManager] System paused");
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
                _logger.LogWarning("ResumeAsync ignored because state is {State}", _state);
                return;
            }

            _logger.LogInformation("[ServiceLifecycleManager] Resuming system - re-enabling threads and RPC calls");
            await ChangeStateAsync(ServiceState.Resuming, "Resuming system operations");

            if (_coreEngine != null)
            {
                await _coreEngine.ResumeAsync(cancellationToken);
            }

            await ChangeStateAsync(ServiceState.Started, "System resumed - all operations enabled");
            ((ISystemStateUpdater)_systemStateService).UpdateSystemState(ServiceState.Started, false);
            _logger.LogInformation("[ServiceLifecycleManager] System resumed");
        }
        finally
        {
            _stateLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<ServiceHealthStatus> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        if (_coreEngine != null)
        {
            var coreHealth = await _coreEngine.GetHealthAsync(cancellationToken);
            
            // Add system pause status to health metrics
            var metrics = new Dictionary<string, object>(coreHealth.Metrics)
            {
                ["IsSystemPaused"] = _systemStateService.IsSystemPaused,
                ["CoreEngineExists"] = true
            };

            return new ServiceHealthStatus
            {
                IsHealthy = coreHealth.IsHealthy && !_systemStateService.IsSystemPaused,
                Status = _systemStateService.IsSystemPaused ? "Paused" : coreHealth.Status,
                Metrics = metrics,
                Timestamp = DateTime.UtcNow
            };
        }

        // No core engine exists
        return new ServiceHealthStatus
        {
            IsHealthy = _state == ServiceState.Stopped,
            Status = _state == ServiceState.Stopped ? "Stopped" : "Error",
            Metrics = new Dictionary<string, object>
            {
                ["State"] = _state.ToString(),
                ["IsSystemPaused"] = _systemStateService.IsSystemPaused,
                ["CoreEngineExists"] = false,
                ["ProcessId"] = Environment.ProcessId,
                ["MemoryUsageMB"] = GC.GetTotalMemory(false) / 1024 / 1024
            },
            Timestamp = DateTime.UtcNow
        };
    }

    private void OnCoreEngineStateChanged(object? sender, ServiceStateChangedEventArgs e)
    {
        // Forward core engine state changes, but don't change our own state
        // Our state represents the lifecycle management, not the core engine state
        _logger.LogDebug("[ServiceLifecycleManager] Core engine state changed: {PreviousState} -> {NewState}", 
            e.PreviousState, e.NewState);
    }

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
        if (_coreEngine != null)
        {
            _coreEngine.StateChanged -= OnCoreEngineStateChanged;
            _coreEngine.Dispose();
            _coreEngine = null;
        }
        _stateLock.Dispose();
    }
}
