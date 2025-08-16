using System.Collections.Concurrent;
using System.Text;
using Microsoft.Extensions.Logging;
using FixedRatioStressTest.Logging.Models;

namespace FixedRatioStressTest.Logging.Gui;

/// <summary>
/// Logger implementation for GUI that raises events for UI updates and optionally writes to file.
/// </summary>
public class GuiLogger : ILogger
{
    private readonly string _categoryName;
    private readonly GuiLoggerOptions _options;
    private readonly GuiLoggerProvider _provider;
    private readonly object _fileLock = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="GuiLogger"/> class.
    /// </summary>
    /// <param name="categoryName">The logger category name.</param>
    /// <param name="options">The logger options.</param>
    /// <param name="provider">The parent provider for event notifications.</param>
    public GuiLogger(string categoryName, GuiLoggerOptions options, GuiLoggerProvider provider)
    {
        _categoryName = categoryName;
        _options = options;
        _provider = provider;

        // Ensure log directory exists
        if (_options.EnableFileLogging)
        {
            var directory = Path.GetDirectoryName(_options.FileLogging.Path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }
    }

    /// <inheritdoc />
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    /// <inheritdoc />
    public bool IsEnabled(LogLevel logLevel)
    {
        // Check minimum level first
        if (logLevel < _options.MinimumLevel)
            return false;

        // If debug messages are suppressed, filter them out
        if (_options.SuppressDebugMessages && logLevel == LogLevel.Debug)
            return false;

        return true;
    }

    /// <inheritdoc />
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
            return;

        var message = formatter(state, exception);
        if (string.IsNullOrEmpty(message) && exception == null)
            return;

        var timestamp = DateTime.Now;

        // Create log event args
        var logEventArgs = new LogMessageEventArgs
        {
            Timestamp = timestamp,
            Level = logLevel,
            Category = _categoryName,
            Message = message,
            Exception = exception,
            EventId = eventId,
            Source = "GUI"
        };

        // Notify the provider to raise the event
        _provider.RaiseLogMessage(logEventArgs);

        // Write to file if enabled
        if (_options.EnableFileLogging)
        {
            var formattedMessage = FormatMessage(timestamp, logLevel, _categoryName, eventId, message, exception);
            WriteToFile(formattedMessage);
        }
    }

    private string FormatMessage(DateTime timestamp, LogLevel logLevel, string category, EventId eventId, string message, Exception? exception)
    {
        var sb = new StringBuilder();
        sb.Append($"[{timestamp:yyyy-MM-dd HH:mm:ss.fff}] ");
        sb.Append($"[{GetShortLogLevel(logLevel)}] ");
        sb.Append($"[{GetShortCategory(category)}] ");
        
        if (eventId.Id != 0)
        {
            sb.Append($"[{eventId.Id}] ");
        }
        
        sb.Append(message);
        
        if (exception != null)
        {
            sb.AppendLine();
            sb.Append(exception.ToString());
        }

        return sb.ToString();
    }

    private void WriteToFile(string message)
    {
        try
        {
            lock (_fileLock)
            {
                var filePath = _options.FileLogging.Path;
                
                // Check if rotation is needed
                if (File.Exists(filePath))
                {
                    var fileInfo = new FileInfo(filePath);
                    if (fileInfo.Length > _options.FileLogging.MaxSizeKB * 1024)
                    {
                        RotateLogFile(filePath);
                    }
                }

                File.AppendAllText(filePath, message + Environment.NewLine);
            }
        }
        catch
        {
            // Ignore file write failures
        }
    }

    private void RotateLogFile(string filePath)
    {
        try
        {
            // Delete oldest backup if we're at max
            var maxBackup = $"{filePath}.{_options.FileLogging.MaxBackupFiles}";
            if (File.Exists(maxBackup))
            {
                File.Delete(maxBackup);
            }

            // Shift existing backups
            for (int i = _options.FileLogging.MaxBackupFiles - 1; i > 0; i--)
            {
                var source = $"{filePath}.{i}";
                var dest = $"{filePath}.{i + 1}";
                if (File.Exists(source))
                {
                    File.Move(source, dest);
                }
            }

            // Move current file to .1
            File.Move(filePath, $"{filePath}.1");
        }
        catch
        {
            // If rotation fails, we'll just overwrite the current file
        }
    }

    private static string GetShortLogLevel(LogLevel logLevel) => logLevel switch
    {
        LogLevel.Trace => "TRCE",
        LogLevel.Debug => "DBUG",
        LogLevel.Information => "INFO",
        LogLevel.Warning => "WARN",
        LogLevel.Error => "FAIL",
        LogLevel.Critical => "CRIT",
        _ => "NONE"
    };

    private static string GetShortCategory(string category)
    {
        var lastDot = category.LastIndexOf('.');
        return lastDot >= 0 ? category[(lastDot + 1)..] : category;
    }
}
