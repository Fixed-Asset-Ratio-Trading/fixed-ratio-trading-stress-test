using System;
using Microsoft.Extensions.Logging;

namespace FixedRatioStressTest.Abstractions;

/// <summary>
/// Abstracts event logging away from the destination so the core engine remains host-agnostic.
/// Implementations might target Windows Event Viewer, a GUI ListView, files, or telemetry backends.
/// </summary>
public interface IEventLogger
{
    /// <summary>
    /// Raised whenever a new log entry is created. Allows observers (e.g., GUI) to render live logs.
    /// </summary>
    event EventHandler<LogEventArgs> LogEntryCreated;

    /// <summary>
    /// Logs an informational message.
    /// </summary>
    void LogInformation(string message, params object[] args);

    /// <summary>
    /// Logs a warning message.
    /// </summary>
    void LogWarning(string message, params object[] args);

    /// <summary>
    /// Logs an error message. Exception is optional and, if provided, should be included in the entry.
    /// </summary>
    void LogError(string message, Exception? exception = null, params object[] args);

    /// <summary>
    /// Logs a critical error message. Exception is optional and, if provided, should be included in the entry.
    /// </summary>
    void LogCritical(string message, Exception? exception = null, params object[] args);

    /// <summary>
    /// Logs a debug-level message. Implementations may no-op in Release builds.
    /// </summary>
    void LogDebug(string message, params object[] args);
}

/// <summary>
/// Strongly-typed log event payload for UI observers.
/// </summary>
public sealed class LogEventArgs : EventArgs
{
    /// <summary>
    /// Severity level of the log entry.
    /// </summary>
    public LogLevel Level { get; init; }

    /// <summary>
    /// Rendered log message (after format string expansion).
    /// </summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>
    /// Optional exception attached to the log entry, if any.
    /// </summary>
    public Exception? Exception { get; init; }

    /// <summary>
    /// UTC timestamp for when the entry was created.
    /// </summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Optional logical category (e.g., "Engine", "Thread", "Network").
    /// </summary>
    public string Category { get; init; } = string.Empty;
}


