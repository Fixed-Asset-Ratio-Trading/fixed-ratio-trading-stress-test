using System.Diagnostics;
using FixedRatioStressTest.Abstractions;
using Microsoft.Extensions.Logging;

namespace FixedRatioStressTest.Hosting.WindowsService;

/// <summary>
/// Event logger that writes to Windows Event Viewer and re-emits entries via <see cref="LogEntryCreated"/>.
/// </summary>
public sealed class WindowsEventLogger : IEventLogger, IDisposable
{
    private readonly EventLog _eventLog;
    private readonly ILogger<WindowsEventLogger> _msLogger;

    /// <inheritdoc />
    public event EventHandler<LogEventArgs>? LogEntryCreated;

    public WindowsEventLogger(ILogger<WindowsEventLogger> msLogger, string? sourceName = null)
    {
        _msLogger = msLogger;

        // Ensure the event source exists for Application log.
        sourceName ??= "Fixed Ratio Stress Test";
        try
        {
            if (!EventLog.SourceExists(sourceName))
            {
                EventLog.CreateEventSource(sourceName, "Application");
            }
        }
        catch
        {
            // Creating sources requires admin; if it fails we still log via ILogger and raise events.
        }

        _eventLog = new EventLog("Application") { Source = sourceName };
    }

    public void LogInformation(string message, params object[] args)
    {
        var formatted = SafeFormat(message, args);
        TryWrite(EventLogEntryType.Information, formatted);
        _msLogger.LogInformation(formatted);
        Raise(LogLevel.Information, formatted);
    }

    public void LogWarning(string message, params object[] args)
    {
        var formatted = SafeFormat(message, args);
        TryWrite(EventLogEntryType.Warning, formatted);
        _msLogger.LogWarning(formatted);
        Raise(LogLevel.Warning, formatted);
    }

    public void LogError(string message, Exception? exception = null, params object[] args)
    {
        var formatted = WithException(SafeFormat(message, args), exception);
        TryWrite(EventLogEntryType.Error, formatted);
        _msLogger.LogError(exception, formatted);
        Raise(LogLevel.Error, formatted, exception);
    }

    public void LogCritical(string message, Exception? exception = null, params object[] args)
    {
        var formatted = WithException(SafeFormat(message, args), exception);
        TryWrite(EventLogEntryType.Error, formatted);
        _msLogger.LogCritical(exception, formatted);
        Raise(LogLevel.Critical, formatted, exception);
    }

    public void LogDebug(string message, params object[] args)
    {
        var formatted = SafeFormat(message, args);
        // Event Viewer typically does not store Debug; keep it in ILogger and raise event.
        _msLogger.LogDebug(formatted);
        Raise(LogLevel.Debug, formatted);
    }

    private static string SafeFormat(string message, params object[] args)
        => args is { Length: > 0 } ? string.Format(message, args) : message;

    private static string WithException(string message, Exception? ex)
        => ex == null ? message : message + "\n" + ex;

    private void TryWrite(EventLogEntryType type, string message)
    {
        try
        {
            _eventLog.WriteEntry(message, type);
        }
        catch
        {
            // Swallow errors writing to EventLog to avoid crashing logging path.
        }
    }

    private void Raise(LogLevel level, string message, Exception? exception = null)
    {
        LogEntryCreated?.Invoke(this, new LogEventArgs
        {
            Level = level,
            Message = message,
            Exception = exception,
            Timestamp = DateTime.UtcNow,
            Category = "WindowsService"
        });
    }

    public void Dispose()
    {
        _eventLog?.Dispose();
    }
}


