using Microsoft.Extensions.Logging;

namespace FixedRatioStressTest.Logging.Models;

/// <summary>
/// Event arguments for log messages that need to be forwarded to UI or other consumers.
/// </summary>
public class LogMessageEventArgs : EventArgs
{
    /// <summary>
    /// Gets the timestamp when the log message was created.
    /// </summary>
    public DateTime Timestamp { get; init; }

    /// <summary>
    /// Gets the log level of the message.
    /// </summary>
    public LogLevel Level { get; init; }

    /// <summary>
    /// Gets the category name (usually the fully qualified class name).
    /// </summary>
    public string Category { get; init; } = string.Empty;

    /// <summary>
    /// Gets the formatted log message.
    /// </summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>
    /// Gets the exception associated with the log entry, if any.
    /// </summary>
    public Exception? Exception { get; init; }

    /// <summary>
    /// Gets the event ID associated with the log entry.
    /// </summary>
    public EventId EventId { get; init; }

    /// <summary>
    /// Gets the source application or process that generated the log.
    /// </summary>
    public string? Source { get; init; }
}
