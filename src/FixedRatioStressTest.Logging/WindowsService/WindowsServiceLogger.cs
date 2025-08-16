using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using FixedRatioStressTest.Logging.Transport;

namespace FixedRatioStressTest.Logging.WindowsService;

/// <summary>
/// Logger implementation for Windows Service that writes to Event Log, file, and optionally UDP.
/// </summary>
public class WindowsServiceLogger : ILogger
{
    private readonly string _categoryName;
    private readonly WindowsServiceLoggerOptions _options;
    private readonly EventLog? _eventLog;
    private readonly object _fileLock = new();
    private readonly UdpClient? _udpClient;
    private readonly IPEndPoint? _udpEndPoint;

    /// <summary>
    /// Initializes a new instance of the <see cref="WindowsServiceLogger"/> class.
    /// </summary>
    /// <param name="categoryName">The logger category name.</param>
    /// <param name="options">The logger options.</param>
    public WindowsServiceLogger(string categoryName, WindowsServiceLoggerOptions options)
    {
        _categoryName = categoryName;
        _options = options;

        // Initialize Event Log (Windows only)
        if (OperatingSystem.IsWindows())
        {
            try
            {
                if (!EventLog.SourceExists(_options.EventLogSource))
                {
                    EventLog.CreateEventSource(_options.EventLogSource, "Application");
                }
                _eventLog = new EventLog("Application") { Source = _options.EventLogSource };
            }
            catch (Exception ex)
            {
                // Fail gracefully if we can't access Event Log
                Console.WriteLine($"Failed to initialize Event Log: {ex.Message}");
            }
        }

        // Initialize UDP client if enabled
        if (_options.EnableUdpTransport)
        {
            try
            {
                var parts = _options.UdpTransport.Endpoint.Split(':');
                if (parts.Length == 2 && int.TryParse(parts[1], out var port))
                {
                    _udpClient = new UdpClient();
                    _udpEndPoint = new IPEndPoint(IPAddress.Parse(parts[0]), port);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to initialize UDP transport: {ex.Message}");
            }
        }

        // Ensure log directory exists for file logging
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
    public bool IsEnabled(LogLevel logLevel) => logLevel >= _options.MinimumLevel;

    /// <inheritdoc />
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
            return;

        var message = formatter(state, exception);
        if (string.IsNullOrEmpty(message) && exception == null)
            return;

        var timestamp = DateTime.Now;
        var formattedMessage = FormatMessage(timestamp, logLevel, _categoryName, eventId, message, exception);

        // Write to Event Log
        if (_eventLog != null && OperatingSystem.IsWindows())
        {
            try
            {
                var eventLogType = logLevel switch
                {
                    LogLevel.Critical or LogLevel.Error => EventLogEntryType.Error,
                    LogLevel.Warning => EventLogEntryType.Warning,
                    _ => EventLogEntryType.Information
                };
                _eventLog.WriteEntry(formattedMessage, eventLogType, eventId.Id);
            }
            catch
            {
                // Ignore Event Log write failures
            }
        }

        // Write to file
        if (_options.EnableFileLogging)
        {
            WriteToFile(formattedMessage);
        }

        // Send via UDP
        if (_udpClient != null && _udpEndPoint != null)
        {
            SendViaUdp(timestamp, logLevel, _categoryName, eventId, message, exception);
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

    private void SendViaUdp(DateTime timestamp, LogLevel logLevel, string category, EventId eventId, string message, Exception? exception)
    {
        try
        {
            var udpMessage = new UdpLogMessage
            {
                Timestamp = timestamp,
                Level = logLevel,
                Category = category,
                EventId = eventId.Id,
                Message = message,
                Exception = exception?.ToString(),
                Source = _options.UdpTransport.Source
            };

            var json = JsonSerializer.Serialize(udpMessage);
            var bytes = Encoding.UTF8.GetBytes(json);
            
            _udpClient?.Send(bytes, bytes.Length, _udpEndPoint);
        }
        catch
        {
            // Ignore UDP send failures
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
