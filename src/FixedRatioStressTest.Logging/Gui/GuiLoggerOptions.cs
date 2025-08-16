using Microsoft.Extensions.Logging;

namespace FixedRatioStressTest.Logging.Gui;

/// <summary>
/// Configuration options for the GUI logger provider.
/// </summary>
public class GuiLoggerOptions
{
    /// <summary>
    /// Gets or sets whether file logging is enabled.
    /// </summary>
    public bool EnableFileLogging { get; set; } = true;

    /// <summary>
    /// Gets or sets the file logging options.
    /// </summary>
    public FileLoggingOptions FileLogging { get; set; } = new();

    /// <summary>
    /// Gets or sets the display options for the GUI.
    /// </summary>
    public DisplayOptions Display { get; set; } = new();

    /// <summary>
    /// Gets or sets the minimum log level.
    /// </summary>
    public LogLevel MinimumLevel { get; set; } = LogLevel.Debug;

    /// <summary>
    /// Gets or sets whether to suppress debug messages from being logged.
    /// When true, debug messages will be filtered out even if MinimumLevel is set to Debug.
    /// </summary>
    public bool SuppressDebugMessages { get; set; } = false;
}

/// <summary>
/// File logging configuration for GUI.
/// </summary>
public class FileLoggingOptions
{
    /// <summary>
    /// Gets or sets the log file path.
    /// </summary>
    public string Path { get; set; } = "logs/gui.log";

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
/// Display configuration for GUI ListView.
/// </summary>
public class DisplayOptions
{
    /// <summary>
    /// Gets or sets the maximum number of log entries to keep in memory.
    /// </summary>
    public int MaxEntries { get; set; } = 5000;

    /// <summary>
    /// Gets or sets whether auto-scroll is enabled by default.
    /// </summary>
    public bool AutoScroll { get; set; } = true;

    /// <summary>
    /// Gets or sets the refresh interval in milliseconds for batching updates.
    /// </summary>
    public int RefreshInterval { get; set; } = 100;
}
