using FixedRatioStressTest.Abstractions;
using Microsoft.Extensions.Logging;

namespace FixedRatioStressTest.Hosting.Gui;

/// <summary>
/// GUI-side logger that raises log events for a ListView without any platform logging side-effects.
/// </summary>
public sealed class GuiEventLogger : IEventLogger
{
    /// <inheritdoc />
    public event EventHandler<LogEventArgs>? LogEntryCreated;

    public void LogInformation(string message, params object[] args)
        => Raise(LogLevel.Information, message, args);

    public void LogWarning(string message, params object[] args)
        => Raise(LogLevel.Warning, message, args);

    public void LogError(string message, Exception? exception = null, params object[] args)
        => Raise(LogLevel.Error, WithException(message, exception), args, exception);

    public void LogCritical(string message, Exception? exception = null, params object[] args)
        => Raise(LogLevel.Critical, WithException(message, exception), args, exception);

    public void LogDebug(string message, params object[] args)
        => Raise(LogLevel.Debug, message, args);

    private static string Format(string message, object[] args)
        => args is { Length: > 0 } ? string.Format(message, args) : message;

    private static string WithException(string message, Exception? ex)
        => ex == null ? message : message + " - " + ex.Message;

    private void Raise(LogLevel level, string message, object[] args, Exception? exception = null)
    {
        var rendered = Format(message, args);
        LogEntryCreated?.Invoke(this, new LogEventArgs
        {
            Level = level,
            Message = rendered,
            Exception = exception,
            Timestamp = DateTime.UtcNow,
            Category = "GUI"
        });
    }
}


