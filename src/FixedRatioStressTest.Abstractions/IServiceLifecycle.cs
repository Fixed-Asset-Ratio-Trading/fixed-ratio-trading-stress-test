using System;
using System.Threading;
using System.Threading.Tasks;

namespace FixedRatioStressTest.Abstractions;

/// <summary>
/// Defines a uniform lifecycle control surface for hosting the core engine.
/// Implementations should be thread-safe and idempotent where applicable.
/// </summary>
public interface IServiceLifecycle
{
    /// <summary>
    /// Gets the current state of the service lifecycle.
    /// Consumers should treat this as a snapshot; subscribe to <see cref="StateChanged"/> for updates.
    /// </summary>
    ServiceState State { get; }

    /// <summary>
    /// Raised whenever the service changes state (e.g., Starting → Started).
    /// Hosts (Windows Service, GUI) can use this to update UI or system status.
    /// </summary>
    event EventHandler<ServiceStateChangedEventArgs> StateChanged;

    /// <summary>
    /// Starts the service and any background operations.
    /// Should be safe to call only from Stopped state; implementations may throw if called from other states.
    /// </summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops the service and gracefully disposes managed resources.
    /// Should be safe to call from any state; redundant calls should be no-ops.
    /// </summary>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Pauses ongoing work while keeping necessary resources initialized (e.g., connections).
    /// </summary>
    Task PauseAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Resumes work after a pause.
    /// </summary>
    Task ResumeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a summarized health snapshot suitable for UI display or health endpoints.
    /// This should be light-weight and non-blocking.
    /// </summary>
    Task<ServiceHealthStatus> GetHealthAsync(CancellationToken cancellationToken = default);
}


