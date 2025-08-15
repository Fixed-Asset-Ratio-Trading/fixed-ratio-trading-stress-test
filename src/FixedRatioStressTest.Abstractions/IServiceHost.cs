using System.Threading;
using System.Threading.Tasks;

namespace FixedRatioStressTest.Abstractions;

/// <summary>
/// Represents a hosting environment (Windows Service, GUI app, etc.) for the core engine.
/// This interface is intentionally minimal and host-focused; the engine lifecycle is handled via <see cref="IServiceLifecycle"/>.
/// </summary>
public interface IServiceHost
{
    /// <summary>
    /// Human-readable host type identifier (e.g., "WindowsService", "GUI").
    /// </summary>
    string HostType { get; }

    /// <summary>
    /// Initializes host-specific facilities (UI wiring, SCM registration, etc.).
    /// Should be idempotent.
    /// </summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs the host message loop or long-running process until cancellation is requested.
    /// </summary>
    Task RunAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Performs graceful host shutdown and cleanup.
    /// </summary>
    Task ShutdownAsync(CancellationToken cancellationToken = default);
}


