using FixedRatioStressTest.Abstractions;
using FixedRatioStressTest.Core.Interfaces;

namespace FixedRatioStressTest.Core.Services;

/// <summary>
/// Implementation of system state service that maintains its own state
/// to avoid circular dependencies with ServiceLifecycleManager
/// </summary>
public class SystemStateService : ISystemStateService, ISystemStateUpdater
{
    private ServiceState _systemState = ServiceState.Stopped;
    private bool _isSystemPaused = false;
    private readonly object _stateLock = new object();

    /// <inheritdoc />
    public bool IsSystemPaused 
    { 
        get 
        { 
            lock (_stateLock) 
            { 
                return _isSystemPaused; 
            } 
        } 
    }

    /// <inheritdoc />
    public bool IsSystemStarted 
    { 
        get 
        { 
            lock (_stateLock) 
            { 
                return _systemState == ServiceState.Started; 
            } 
        } 
    }

    /// <summary>
    /// Updates the system state (called by ServiceLifecycleManager)
    /// </summary>
    public void UpdateSystemState(ServiceState state, bool isPaused)
    {
        lock (_stateLock)
        {
            _systemState = state;
            _isSystemPaused = isPaused;
        }
    }

    /// <inheritdoc />
    public void ValidateSystemState()
    {
        lock (_stateLock)
        {
            if (_systemState != ServiceState.Started)
            {
                throw new InvalidOperationException($"System is not started. Current state: {_systemState}");
            }

            if (_isSystemPaused)
            {
                throw new InvalidOperationException("System is paused");
            }
        }
    }
}
