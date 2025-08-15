using FixedRatioStressTest.Abstractions;
using Microsoft.Extensions.Logging;

namespace FixedRatioStressTest.Hosting.WindowsService;

public sealed class CompositeEventLogger : IEventLogger
{
    private readonly IReadOnlyList<IEventLogger> _loggers;

    public event EventHandler<LogEventArgs>? LogEntryCreated;

    public CompositeEventLogger(IEnumerable<IEventLogger> loggers)
    {
        _loggers = loggers.ToList();
        foreach (var logger in _loggers)
        {
            logger.LogEntryCreated += (_, e) => LogEntryCreated?.Invoke(this, e);
        }
    }

    public void LogInformation(string message, params object[] args)
        => Forward(l => l.LogInformation(message, args));

    public void LogWarning(string message, params object[] args)
        => Forward(l => l.LogWarning(message, args));

    public void LogError(string message, Exception? exception = null, params object[] args)
        => Forward(l => l.LogError(message, exception, args));

    public void LogCritical(string message, Exception? exception = null, params object[] args)
        => Forward(l => l.LogCritical(message, exception, args));

    public void LogDebug(string message, params object[] args)
        => Forward(l => l.LogDebug(message, args));

    private void Forward(Action<IEventLogger> action)
    {
        foreach (var logger in _loggers)
        {
            try { action(logger); } catch { /* ignore */ }
        }
    }
}


