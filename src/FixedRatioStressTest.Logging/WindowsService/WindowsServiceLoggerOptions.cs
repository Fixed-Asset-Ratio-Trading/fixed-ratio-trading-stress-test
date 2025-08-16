using Microsoft.Extensions.Logging;

namespace FixedRatioStressTest.Logging.WindowsService;

/// <summary>
/// Configuration options for the Windows Service logger provider.
/// </summary>
public class WindowsServiceLoggerOptions
{
    /// <summary>
    /// Gets or sets the Windows Event Log source name.
    /// </summary>
    public string EventLogSource { get; set; } = "FixedRatioStressTest";

    /// <summary>
    /// Gets or sets whether file logging is enabled.
    /// </summary>
    public bool EnableFileLogging { get; set; } = true;

    /// <summary>
    /// Gets or sets the file logging options.
    /// </summary>
    public FileLoggingOptions FileLogging { get; set; } = new();

    /// <summary>
    /// Gets or sets whether UDP transport is enabled.
    /// </summary>
    public bool EnableUdpTransport { get; set; } = false;

    /// <summary>
    /// Gets or sets the UDP transport options.
    /// </summary>
    public UdpTransportOptions UdpTransport { get; set; } = new();

    /// <summary>
    /// Gets or sets the minimum log level.
    /// </summary>
    public LogLevel MinimumLevel { get; set; } = LogLevel.Information;

    /// <summary>
    /// Gets or sets whether to suppress debug messages from being logged.
    /// When true, debug messages will be filtered out even if MinimumLevel is set to Debug.
    /// </summary>
    public bool SuppressDebugMessages { get; set; } = false;
}

/// <summary>
/// File logging configuration options.
/// </summary>
public class FileLoggingOptions
{
    /// <summary>
    /// Gets or sets the log file path.
    /// </summary>
    public string Path { get; set; } = "logs/service.log";

    /// <summary>
    /// Gets or sets the maximum file size in KB before rotation.
    /// </summary>
    public int MaxSizeKB { get; set; } = 10240;

    /// <summary>
    /// Gets or sets the number of backup files to keep.
    /// </summary>
    public int MaxBackupFiles { get; set; } = 5;
}

/// <summary>
/// UDP transport configuration options.
/// </summary>
public class UdpTransportOptions
{
    /// <summary>
    /// Gets or sets the UDP endpoint address.
    /// </summary>
    public string Endpoint { get; set; } = "127.0.0.1:12345";

    /// <summary>
    /// Gets or sets the source identifier for UDP messages.
    /// </summary>
    public string Source { get; set; } = "WindowsService";
}
