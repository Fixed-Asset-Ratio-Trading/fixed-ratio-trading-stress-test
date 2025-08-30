using FixedRatioStressTest.Abstractions;

namespace FixedRatioStressTest.Core.Interfaces;

/// <summary>
/// Service to check system-wide state for RPC operations
/// </summary>
public interface ISystemStateService
{
    /// <summary>
    /// Gets whether the system is currently paused
    /// </summary>
    bool IsSystemPaused { get; }
    
    /// <summary>
    /// Gets whether the system is currently started and operational
    /// </summary>
    bool IsSystemStarted { get; }
    
    /// <summary>
    /// Throws an appropriate exception if the system cannot handle RPC requests
    /// </summary>
    void ValidateSystemState();
}

/// <summary>
/// Internal interface for updating system state (used by ServiceLifecycleManager)
/// </summary>
internal interface ISystemStateUpdater
{
    /// <summary>
    /// Updates the system state
    /// </summary>
    void UpdateSystemState(ServiceState state, bool isPaused);
}
