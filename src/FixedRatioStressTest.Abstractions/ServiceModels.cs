using System;
using System.Collections.Generic;

namespace FixedRatioStressTest.Abstractions;

/// <summary>
/// Canonical lifecycle states for the hosted engine.
/// </summary>
public enum ServiceState
{
    /// <summary>
    /// Engine is fully stopped and not performing any work.
    /// </summary>
    Stopped,

    /// <summary>
    /// Engine is in the process of starting up.
    /// </summary>
    Starting,

    /// <summary>
    /// Engine is fully started and actively running.
    /// </summary>
    Started,

    /// <summary>
    /// Engine is in the process of entering a paused state.
    /// </summary>
    Pausing,

    /// <summary>
    /// Engine is paused; background work is suspended but resources may remain initialized.
    /// </summary>
    Paused,

    /// <summary>
    /// Engine is resuming from a paused state.
    /// </summary>
    Resuming,

    /// <summary>
    /// Engine is in the process of shutting down.
    /// </summary>
    Stopping,

    /// <summary>
    /// Engine has encountered an unrecoverable error state.
    /// </summary>
    Error
}

/// <summary>
/// Event payload describing a state transition within the engine lifecycle.
/// </summary>
public sealed class ServiceStateChangedEventArgs : EventArgs
{
    /// <summary>
    /// The previous lifecycle state.
    /// </summary>
    public ServiceState PreviousState { get; init; }

    /// <summary>
    /// The new lifecycle state.
    /// </summary>
    public ServiceState NewState { get; init; }

    /// <summary>
    /// UTC timestamp of when the transition occurred.
    /// </summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Optional human-readable reason for the transition.
    /// </summary>
    public string? Reason { get; init; }
}

/// <summary>
/// Lightweight service health snapshot suitable for UIs and health endpoints.
/// </summary>
public sealed class ServiceHealthStatus
{
    /// <summary>
    /// True when the engine considers itself fully healthy.
    /// </summary>
    public bool IsHealthy { get; init; }

    /// <summary>
    /// Human-readable summary (e.g., "Healthy", "Degraded").
    /// </summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>
    /// Arbitrary metrics key/value pairs (e.g., counts, memory usage).
    /// </summary>
    public Dictionary<string, object> Metrics { get; init; } = new();

    /// <summary>
    /// UTC timestamp of when the snapshot was created.
    /// </summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}


